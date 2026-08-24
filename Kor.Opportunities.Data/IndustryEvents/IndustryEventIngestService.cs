#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Ingestion;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.IndustryEvents;

/// <summary>
/// Polls every active row in <c>opportunities.IndustryEventSource</c> and
/// upserts what it finds into <c>opportunities.IndustryEvents</c>.
///
/// Before this existed the events table had a reaper (DataRetirementJob) and no
/// feeder: 83 rows from two manual loads in May/June 2026 and nothing since,
/// which is how ICBA — an organiser named in eight IntelAction recommendations
/// as the place to meet KOR's targets — stayed absent from the "comprehensive"
/// event list entirely.
/// </summary>
public sealed class IndustryEventIngestService
{
    private readonly HttpClient _http;
    private readonly IIndustryEventSourceStore _sourceStore;
    private readonly IIndustryEventStore _eventStore;
    private readonly IEventMarketRegionStore _marketStore;
    private readonly IReadOnlyDictionary<string, IIndustryEventCalendarParser> _parsers;
    private readonly ILogger<IndustryEventIngestService> _logger;
    private readonly int _maxBytesPerResponse;

    public IndustryEventIngestService(
        HttpClient http,
        IIndustryEventSourceStore sourceStore,
        IIndustryEventStore eventStore,
        IEventMarketRegionStore marketStore,
        IEnumerable<IIndustryEventCalendarParser> parsers,
        ILogger<IndustryEventIngestService> logger,
        int maxBytesPerResponse = 8 * 1024 * 1024)
    {
        _http = http;
        _sourceStore = sourceStore;
        _eventStore = eventStore;
        _marketStore = marketStore;
        _logger = logger;
        _maxBytesPerResponse = maxBytesPerResponse > 0 ? maxBytesPerResponse : int.MaxValue;
        _parsers = parsers.ToDictionary(p => p.ParserKey, StringComparer.OrdinalIgnoreCase);
    }

    public sealed record IngestResult(int SourcesPolled, int SourcesSkipped, int EventsParsed, int Upserted, int Failed);

    public async Task<IngestResult> IngestAllAsync(CancellationToken ct)
        => await IngestAllAsync(DateTimeOffset.UtcNow, forceAll: false, ct).ConfigureAwait(false);

    /// <param name="forceAll">
    /// Ignore each source's CrawlDelaySeconds. Used by an operator-triggered run.
    /// </param>
    public async Task<IngestResult> IngestAllAsync(DateTimeOffset now, bool forceAll, CancellationToken ct)
    {
        var sources = await _sourceStore.ListActiveAsync(ct).ConfigureAwait(false);
        var marketsByCity = await _marketStore.LoadAsync(ct).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var polled = 0;
        var skipped = 0;
        var parsedTotal = 0;
        var upserted = 0;
        var failed = 0;

        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();

            if (!forceAll && IsWithinCrawlDelay(source, now))
            {
                skipped++;
                continue;
            }

            if (!_parsers.TryGetValue(source.ParserKey, out var parser))
            {
                // An active source with no parser is a configuration error the
                // operator needs to see, not a silent no-op.
                _logger.LogWarning(
                    "IndustryEventIngest: source {Source} has no parser for ParserKey '{ParserKey}'.",
                    source.Name,
                    source.ParserKey);
                await _sourceStore.UpdateHeartbeatAsync(
                    source.Id,
                    null,
                    $"No parser registered for ParserKey '{source.ParserKey}'.",
                    ct).ConfigureAwait(false);
                failed++;
                continue;
            }

            polled++;

            try
            {
                using var response = await _http.GetAsync(source.CalendarUrl, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    await _sourceStore.UpdateHeartbeatAsync(
                        source.Id,
                        null,
                        $"HTTP {(int)response.StatusCode}",
                        ct).ConfigureAwait(false);
                    failed++;
                    continue;
                }

                var content = await HttpReadHelpers.ReadStringWithCapAsync(
                    response.Content,
                    _maxBytesPerResponse,
                    $"Industry event calendar {source.Name}",
                    ct).ConfigureAwait(false);

                var parsed = parser.Parse(content, source, today);
                parsedTotal += parsed.Count;

                if (parsed.Count == 0)
                {
                    // The page fetched but yielded nothing — almost always a
                    // markup change at the source. Surface it instead of
                    // reporting a clean run over zero events.
                    _logger.LogWarning(
                        "IndustryEventIngest: {Source} returned {Bytes} bytes but parser '{ParserKey}' found no events.",
                        source.Name,
                        content.Length,
                        source.ParserKey);
                    await _sourceStore.UpdateHeartbeatAsync(
                        source.Id,
                        0,
                        $"Fetched {content.Length} bytes but parser '{source.ParserKey}' matched no events — check for a layout change.",
                        ct).ConfigureAwait(false);
                    failed++;
                    continue;
                }

                foreach (var item in parsed)
                {
                    ct.ThrowIfCancellationRequested();
                    await _eventStore.UpsertAsync(
                        ToRecord(item, source, now, marketsByCity),
                        ct).ConfigureAwait(false);
                    upserted++;
                }

                await _sourceStore.UpdateHeartbeatAsync(source.Id, parsed.Count, null, ct)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "IndustryEventIngest: {Source} -> {Count} events upserted.",
                    source.Name,
                    parsed.Count);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "IndustryEventIngest: source {Source} ({Url}) failed.",
                    source.Name,
                    source.CalendarUrl);
                try
                {
                    await _sourceStore.UpdateHeartbeatAsync(source.Id, null, ex.Message, ct)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Heartbeat is best-effort; the exception above is the signal.
                }

                failed++;
            }
        }

        return new IngestResult(polled, skipped, parsedTotal, upserted, failed);
    }

    private static bool IsWithinCrawlDelay(IndustryEventSourceRow source, DateTimeOffset now)
    {
        if (source.LastPolledAtUtc is not { } last)
        {
            return false;
        }

        return now < last.AddSeconds(source.CrawlDelaySeconds);
    }

    internal static IndustryEventRecord ToRecord(
        ParsedIndustryEvent item,
        IndustryEventSourceRow source,
        DateTimeOffset now,
        IReadOnlyDictionary<string, string> marketsByCity)
    {
        var sourceNote = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"Ingested from {source.Name} calendar ({source.CalendarUrl})")
            .Append(CultureInfo.InvariantCulture, $" on {now:yyyy-MM-dd}.")
            .ToString();

        if (item.YearInferred)
        {
            sourceNote +=
                " Calendar lists month/day only; year inferred as the next occurrence — verify before booking travel.";
        }

        return new IndustryEventRecord(
            SourceKey: BuildSourceKey(item.Name, item.StartDate),
            Name: item.Name,
            Organizer: source.Organizer,
            EventType: source.DefaultEventType,
            StartDate: item.StartDate,
            EndDate: item.EndDate,
            Recurrence: null,
            City: item.City,
            Market: ResolveMarket(item.City, source, marketsByCity),
            Format: item.Venue,
            SectorsThemes: null,
            Audience: item.Blurb,
            TargetsPresent: null,
            RegistrationUrl: string.IsNullOrWhiteSpace(item.RegistrationUrl)
                ? source.CalendarUrl
                : item.RegistrationUrl,
            CostNote: null,
            KorRelevance: source.KorRelevance,
            SourceNote: sourceNote,
            IndustryEventSourceId: source.Id);
    }

    /// <summary>
    /// A source's DefaultMarket is a blanket ("British Columbia / Alberta" for
    /// ICBA, which runs events in both). Prefer the market the event's own city
    /// maps to, so a Victoria event is not filed under Alberta.
    /// </summary>
    internal static string? ResolveMarket(
        string? city,
        IndustryEventSourceRow source,
        IReadOnlyDictionary<string, string> marketsByCity)
    {
        if (!string.IsNullOrWhiteSpace(city) &&
            marketsByCity.TryGetValue(city.Trim(), out var market))
        {
            return market;
        }

        return source.DefaultMarket;
    }

    /// <summary>
    /// SHA-1 of <c>name|yyyy-MM-dd</c> — the same key the 2026-05-28 and
    /// 2026-06-21 manual loads used, so an ingested row merges onto its
    /// hand-curated twin instead of duplicating it.
    /// </summary>
    internal static string BuildSourceKey(string name, DateOnly startDate)
    {
        var input = $"{name}|{startDate:yyyy-MM-dd}";
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
