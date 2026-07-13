#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// Source-agnostic live-opportunity detail enricher. For each registered
/// <see cref="ILiveOppDetailExtractor"/> (BC Bid, Bids&amp;Tenders, …) it walks live
/// opps whose observation URL that extractor handles and that haven't been read
/// yet (DetailEnrichedAtUtc IS NULL), opens the detail page, and FILL-ONLY
/// persists the recovered Discipline (from commodity codes or the scraped
/// description), buyer contact, and documents. Every attempted opp is stamped
/// DetailEnrichedAtUtc so it is processed exactly once — never re-queued.
///
/// The detail URL comes from opportunities.OpportunityObservations.Url (populated
/// for every source), so adding a portal is one new extractor + a DI registration
/// — this job never changes.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class LiveOppDetailEnrichmentJob : IJob
{
    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<LiveOppDetailEnrichmentJob> _logger;
    private readonly IEnumerable<ILiveOppDetailExtractor> _extractors;
    private readonly PlaywrightBrowserPool _browserPool;
    private readonly Kor.Opportunities.Data.Awards.IOpportunityInterestedFirmStore _interestStore;

    public LiveOppDetailEnrichmentJob(
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<LiveOppDetailEnrichmentJob> logger,
        IEnumerable<ILiveOppDetailExtractor> extractors,
        PlaywrightBrowserPool browserPool,
        Kor.Opportunities.Data.Awards.IOpportunityInterestedFirmStore interestStore)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _extractors = extractors ?? throw new ArgumentNullException(nameof(extractors));
        _browserPool = browserPool ?? throw new ArgumentNullException(nameof(browserPool));
        _interestStore = interestStore ?? throw new ArgumentNullException(nameof(interestStore));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.LiveOppDetailEnrichmentEnabled)
        {
            _logger.LogInformation("{Job}: disabled by configuration", nameof(LiveOppDetailEnrichmentJob));
            return;
        }

        var ct = context.CancellationToken;
        var batch = Math.Max(1, opt.LiveOppDetailEnrichmentBatchSize);
        var db = opt.OpportunitiesDb!;

        int totalProcessed = 0, totalDiscipline = 0, totalContact = 0, totalDocs = 0, totalFirms = 0, totalFail = 0;

        foreach (var extractor in _extractors)
        {
            ct.ThrowIfCancellationRequested();
            if (!extractor.IsAvailable)
            {
                _logger.LogInformation("{Job}: extractor {Src} unavailable; skipping", nameof(LiveOppDetailEnrichmentJob), extractor.Name);
                continue;
            }

            var targets = await LoadTargetsAsync(db, extractor.UrlHostLike, batch, ct).ConfigureAwait(false);
            if (targets.Count == 0) continue;

            _logger.LogInformation("{Job}: {Src} — {Count} opps", nameof(LiveOppDetailEnrichmentJob), extractor.Name, targets.Count);

            await using var ctxRef = await _browserPool.AcquireContextAsync(ct).ConfigureAwait(false);
            var page = await ctxRef.NewPageAsync().ConfigureAwait(false);
            if (extractor.RequiresLogin)
            {
                await extractor.LoginAsync(page, ct).ConfigureAwait(false);
            }

            foreach (var t in targets)
            {
                ct.ThrowIfCancellationRequested();
                totalProcessed++;
                LiveDetailResult? result = null;
                try
                {
                    result = await extractor.ExtractAsync(page, t.Url, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    totalFail++;
                    _logger.LogWarning(ex, "{Key}: {Src} detail extract failed; marking attempted", t.OpportunityKey, extractor.Name);
                }

                try
                {
                    var (d, c, docs) = await PersistAsync(db, t, result, extractor.Name, ct).ConfigureAwait(false);
                    totalDiscipline += d; totalContact += c; totalDocs += docs;

                    // Plan-holder / document-request firms (MERX DCC) go through
                    // the interested-firm store — idempotent upsert, same rail
                    // as APC interest and BcBid plan-takers.
                    if (result?.InterestedFirms is { Count: > 0 } firms)
                    {
                        foreach (var firm in firms)
                        {
                            await _interestStore.UpsertAsync(
                                t.Id, firm, resolvedCanonicalOrgId: null, resolvedKind: null,
                                sourcePortal: extractor.Name, sourcePostingUrl: t.Url,
                                expressedAtUtc: null, notes: "document-request list",
                                rawJson: null, ct).ConfigureAwait(false);
                            totalFirms++;
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    totalFail++;
                    _logger.LogWarning(ex, "{Key}: {Src} detail persist failed", t.OpportunityKey, extractor.Name);
                }
            }
        }

        var summary = $"processed={totalProcessed}; disciplineSet={totalDiscipline}; contactSet={totalContact}; docs={totalDocs}; interestedFirms={totalFirms}; failures={totalFail}";
        _logger.LogInformation("{Job}: {Summary}", nameof(LiveOppDetailEnrichmentJob), summary);
        context.Result = summary;
    }

    private static async Task<IReadOnlyList<Target>> LoadTargetsAsync(string db, string hostLike, int batch, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@batch) o.Id, o.OpportunityKey, o.Name, obs.Url
FROM opportunities.Opportunities o
CROSS APPLY (
    SELECT TOP 1 x.Url
    FROM opportunities.OpportunityObservations x
    WHERE x.OpportunityId = o.Id AND x.Url IS NOT NULL
    ORDER BY x.IsActive DESC, x.IngestedAtUtc DESC
) obs
WHERE o.Status IN (0,1)
  AND o.DetailEnrichedAtUtc IS NULL
  AND obs.Url LIKE @host
ORDER BY o.SubmissionDeadlineUtc ASC;";   // soonest-closing first

        await using var con = new SqlConnection(db);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@batch", SqlDbType.Int).Value = batch;
        cmd.Parameters.Add("@host", SqlDbType.NVarChar, 200).Value = hostLike;
        var list = new List<Target>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new Target(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        }
        return list;
    }

    /// <summary>Fill-only persist + idempotent docs + always-mark-attempted.</summary>
    private static async Task<(int discipline, int contact, int docs)> PersistAsync(
        string db, Target t, LiveDetailResult? result, string sourcePortal, CancellationToken ct)
    {
        int discSet = 0, contactSet = 0, docsWritten = 0;
        await using var con = new SqlConnection(db);
        await con.OpenAsync(ct).ConfigureAwait(false);

        if (result is not null)
        {
            // Discipline from structured codes (BC Bid) or the scraped description (B&T).
            var discipline = DisciplineClassifier.Classify(result.CommodityCodes, t.Name, result.Description);
            if (discipline != OpportunityDiscipline.Unknown)
            {
                discSet += await ExecAsync(con,
                    "UPDATE opportunities.Opportunities SET Discipline=@v WHERE Id=@id AND Discipline=0",
                    ("@v", SqlDbType.Int, (int)discipline), ("@id", SqlDbType.BigInt, t.Id));
            }
            if (!string.IsNullOrWhiteSpace(result.ContactEmail))
            {
                contactSet += await ExecAsync(con,
                    "UPDATE opportunities.Opportunities SET BuyerContactEmail=@v WHERE Id=@id AND BuyerContactEmail IS NULL",
                    ("@v", SqlDbType.NVarChar, Trunc(result.ContactEmail, 255)), ("@id", SqlDbType.BigInt, t.Id));
            }
            if (!string.IsNullOrWhiteSpace(result.ContactName))
            {
                await ExecAsync(con,
                    "UPDATE opportunities.Opportunities SET BuyerContactName=@v WHERE Id=@id AND BuyerContactName IS NULL",
                    ("@v", SqlDbType.NVarChar, Trunc(result.ContactName, 120)), ("@id", SqlDbType.BigInt, t.Id));
            }
            if (!string.IsNullOrWhiteSpace(result.ContactPhone))
            {
                await ExecAsync(con,
                    "UPDATE opportunities.Opportunities SET BuyerContactPhone=@v WHERE Id=@id AND BuyerContactPhone IS NULL",
                    ("@v", SqlDbType.NVarChar, Trunc(result.ContactPhone, 40)), ("@id", SqlDbType.BigInt, t.Id));
            }
            foreach (var doc in result.Documents)
            {
                if (string.IsNullOrWhiteSpace(doc.Url)) continue;
                docsWritten += await ExecAsync(con, @"
INSERT INTO opportunities.OpportunityDocuments (OpportunityId, DocumentName, DocumentUrl, SourcePortal)
SELECT @id, @name, @url, @portal
WHERE NOT EXISTS (SELECT 1 FROM opportunities.OpportunityDocuments
                  WHERE OpportunityId=@id AND DocumentUrl=@url)",
                    ("@id", SqlDbType.BigInt, t.Id),
                    ("@name", SqlDbType.NVarChar, Trunc(doc.Name, 400)),
                    ("@url", SqlDbType.NVarChar, Trunc(doc.Url, 1000)),
                    ("@portal", SqlDbType.NVarChar, sourcePortal));
            }
        }

        // Always mark attempted (success, no-data, or failure) — no opp re-queues.
        await ExecAsync(con,
            "UPDATE opportunities.Opportunities SET DetailEnrichedAtUtc=sysdatetimeoffset() WHERE Id=@id AND DetailEnrichedAtUtc IS NULL",
            ("@id", SqlDbType.BigInt, t.Id));

        return (discSet, contactSet, docsWritten);
    }

    private static async Task<int> ExecAsync(SqlConnection con, string sql, params (string, SqlDbType, object)[] ps)
    {
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        foreach (var (n, ty, v) in ps) cmd.Parameters.Add(n, ty).Value = v;
        return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s.Substring(0, n);

    private sealed record Target(long Id, string OpportunityKey, string Name, string Url);
}
