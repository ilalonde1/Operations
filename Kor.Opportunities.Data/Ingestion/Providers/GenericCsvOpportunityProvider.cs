#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Ingestion.Providers;

/// <summary>
/// HTTP CSV provider. Fetches the configured URL, parses the body, applies
/// CanadaBuys-aware column-name fallbacks, and applies the source-side filter
/// (procurement category + region of delivery) before yielding candidates.
///
/// In v1 the filter logic is hardcoded for the CanadaBuys dataset because that
/// is the only configured CSV source. When we add a second one in Phase 7 we'll
/// move the filter into <c>OpportunitySourceMappings</c> JSON so each source
/// brings its own keep/drop rules.
/// </summary>
public sealed class GenericCsvOpportunityProvider : IOpportunityProvider
{
    /// <summary>Source.Name we treat as canonical CanadaBuys. Used to gate the
    /// CanadaBuys-specific filter logic.</summary>
    public const string CanadaBuysSourceName = "CanadaBuys";

    private static readonly string[] CanadaBuysKeptCategories = { "CNST", "SRVTGD" };
    private static readonly string[] CanadaBuysKeptRegionTokens = { "BC", "BRITISH COLUMBIA", "AB", "ALBERTA" };

    private readonly HttpClient _httpClient;
    private readonly ILogger<GenericCsvOpportunityProvider> _logger;

    public GenericCsvOpportunityProvider(HttpClient httpClient, ILogger<GenericCsvOpportunityProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public OpportunitySourceType SourceType => OpportunitySourceType.GenericCsv;

    public async Task<IReadOnlyList<OpportunityCandidate>> FetchAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(10, source.RequestTimeoutSeconds));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, source.BaseUrl);
        request.Headers.UserAgent.ParseAdd("Kor.Opportunities.Worker/1.0 (+ilalonde@korstructural.com)");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var csv = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(csv))
        {
            _logger.LogWarning("CSV provider {SourceName} returned an empty body.", source.Name);
            return Array.Empty<OpportunityCandidate>();
        }

        var rows = CsvParser.Parse(csv);
        if (rows.Count < 2)
        {
            _logger.LogWarning("CSV provider {SourceName}: only {Rows} row(s) parsed; nothing to ingest.", source.Name, rows.Count);
            return Array.Empty<OpportunityCandidate>();
        }

        var headers = CsvParser.NormalizeHeaderRow(rows[0]);
        var candidates = new List<OpportunityCandidate>();
        var dropped = 0;

        for (var i = 1; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var row = rows[i];

            // CanadaBuys filter: keep CNST / SRVTGD categories AND BC / AB regions.
            if (string.Equals(source.Name, CanadaBuysSourceName, StringComparison.OrdinalIgnoreCase)
                && !PassesCanadaBuysFilter(row, headers))
            {
                dropped++;
                continue;
            }

            var candidate = MapRow(row, headers);
            if (candidate is null)
            {
                dropped++;
                continue;
            }

            candidates.Add(candidate);
        }

        _logger.LogInformation(
            "CSV provider {SourceName}: {Kept} candidate(s) parsed from {Total} data row(s) ({Dropped} dropped).",
            source.Name,
            candidates.Count,
            rows.Count - 1,
            dropped);

        return candidates;
    }

    private static OpportunityCandidate? MapRow(IReadOnlyList<string> row, IReadOnlyList<string> headers)
    {
        // Title + URL are required - everything else is best-effort.
        var title = CsvParser.FirstValue(row, headers,
            "title",
            "noticeTitle",
            "noticeTitle-titreAvis",
            "tenderTitle");
        var url = CsvParser.FirstValue(row, headers,
            "url",
            "noticeUrl",
            "tenderUrl",
            "noticeUrl-urlAvis",
            "tenderUrl-urlAppelOffres");

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var buyer = CsvParser.FirstValue(row, headers,
            "buyer",
            "buyerName",
            "departmentName",
            "departmentName-nomMinistere",
            "endUser") ?? "Unknown";

        var description = CsvParser.FirstValue(row, headers,
            "description",
            "noticeDescription",
            "noticeDescription-descriptionAvis",
            "tenderDescription");

        var location = CsvParser.FirstValue(row, headers,
            "location",
            "regionsOfDelivery",
            "regionsOfDelivery-regionsLivraison",
            "deliveryRegion",
            "region");

        var postedRaw = CsvParser.FirstValue(row, headers,
            "postedDate",
            "publicationDate",
            "publicationDate-datePublication");
        DateTimeOffset? postedDate = null;
        if (!string.IsNullOrWhiteSpace(postedRaw)
            && DateTimeOffset.TryParse(postedRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            postedDate = parsed;
        }

        var deadlineRaw = CsvParser.FirstValue(row, headers,
            "deadline",
            "submissionDeadline",
            "tenderClosingDate",
            "tenderClosingDate-dateFermetureSoumissions",
            "closingDate");
        DateTimeOffset? deadline = null;
        if (!string.IsNullOrWhiteSpace(deadlineRaw)
            && DateTimeOffset.TryParse(deadlineRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDeadline))
        {
            deadline = parsedDeadline;
        }

        var external = CsvParser.FirstValue(row, headers,
            "referenceNumber",
            "referenceNumber-numeroReference",
            "noticeId",
            "tenderId",
            "solicitationNumber");

        // Re-build a JSON of the row in (header -> value) form so the observation
        // captures everything for future AI features without hard-coding columns.
        var rawJson = BuildRowJson(row, headers);

        return new OpportunityCandidate
        {
            Title = title.Trim(),
            Buyer = buyer.Trim(),
            Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
            Url = url.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            PostedDateUtc = postedDate,
            SubmissionDeadlineUtc = deadline,
            ProjectCity = null,    // CanadaBuys CSV doesn't expose city directly
            ProjectProvince = ExtractProvince(location),
            EstimatedValueCad = null, // CanadaBuys doesn't include estimated value
            ExternalReference = external,
            RawJson = rawJson,
        };
    }

    private static bool PassesCanadaBuysFilter(IReadOnlyList<string> row, IReadOnlyList<string> headers)
    {
        var category = CsvParser.FirstValue(row, headers,
            "procurementCategory",
            "procurementCategory-categorieApprovisionnement");
        if (!string.IsNullOrWhiteSpace(category))
        {
            var match = CanadaBuysKeptCategories.Any(c => category.Contains(c, StringComparison.OrdinalIgnoreCase));
            if (!match)
            {
                return false;
            }
        }

        var regions = CsvParser.FirstValue(row, headers,
            "regionsOfDelivery",
            "regionsOfDelivery-regionsLivraison");
        if (!string.IsNullOrWhiteSpace(regions))
        {
            return CanadaBuysKeptRegionTokens.Any(t => regions.Contains(t, StringComparison.OrdinalIgnoreCase));
        }

        // No region info: keep, let the scorer sort it out.
        return true;
    }

    private static string? ExtractProvince(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        // CanadaBuys regions appear in many shapes: "British Columbia (BC)",
        // "Alberta (AB)", "ON, BC, AB", bare "BC". Match a word-boundary 2-letter
        // code OR the full province name. Word boundary prevents false positives
        // like ABBOTSFORD or BCS.
        var upper = location.ToUpperInvariant();
        if (upper.Contains("BRITISH COLUMBIA") || HasIsolatedToken(upper, "BC"))
        {
            return "BC";
        }

        if (upper.Contains("ALBERTA") || HasIsolatedToken(upper, "AB"))
        {
            return "AB";
        }

        return null;
    }

    private static bool HasIsolatedToken(string upperHaystack, string token)
    {
        var idx = 0;
        while ((idx = upperHaystack.IndexOf(token, idx, StringComparison.Ordinal)) >= 0)
        {
            var leftOk  = idx == 0 || !char.IsLetterOrDigit(upperHaystack[idx - 1]);
            var rightOk = idx + token.Length == upperHaystack.Length
                       || !char.IsLetterOrDigit(upperHaystack[idx + token.Length]);
            if (leftOk && rightOk)
            {
                return true;
            }
            idx += token.Length;
        }
        return false;
    }

    private static string BuildRowJson(IReadOnlyList<string> row, IReadOnlyList<string> headers)
    {
        var dict = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count && i < row.Count; i++)
        {
            var key = headers[i];
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            dict[key] = row[i] ?? string.Empty;
        }

        return System.Text.Json.JsonSerializer.Serialize(dict);
    }
}
