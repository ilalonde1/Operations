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
/// column-name fallbacks, and applies any source-side filter configured in
/// <c>OpportunitySourceMappings</c> before yielding candidates.
/// </summary>
public sealed class GenericCsvOpportunityProvider : IOpportunityProvider
{
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

            if (!PassesConfiguredFilter(row, headers, sourceConfig))
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

    private static bool PassesConfiguredFilter(
        IReadOnlyList<string> row,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> sourceConfig)
    {
        if (!PassesSubstringFilter(
            row,
            headers,
            sourceConfig,
            "csv.filter.categoryColumns",
            "csv.filter.categoryKeepValues"))
        {
            return false;
        }

        if (!PassesWordBoundaryFilter(
            row,
            headers,
            sourceConfig,
            "csv.filter.regionColumns",
            "csv.filter.regionKeepTokens"))
        {
            return false;
        }

        return true;
    }

    private static bool PassesSubstringFilter(
        IReadOnlyList<string> row,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> sourceConfig,
        string columnsKey,
        string keepValuesKey)
    {
        if (!sourceConfig.TryGetValue(columnsKey, out var columnsRaw))
        {
            return true;
        }

        var columns = ParseMappingCsv(columnsRaw);
        if (columns.Count == 0)
        {
            return true;
        }

        var value = CsvParser.FirstValue(row, headers, columns.ToArray());
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        IReadOnlyList<string> keepValues = sourceConfig.TryGetValue(keepValuesKey, out var keepValuesRaw)
            ? ParseMappingCsv(keepValuesRaw)
            : Array.Empty<string>();

        return keepValues.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool PassesWordBoundaryFilter(
        IReadOnlyList<string> row,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> sourceConfig,
        string columnsKey,
        string keepTokensKey)
    {
        if (!sourceConfig.TryGetValue(columnsKey, out var columnsRaw))
        {
            return true;
        }

        var columns = ParseMappingCsv(columnsRaw);
        if (columns.Count == 0)
        {
            return true;
        }

        var value = CsvParser.FirstValue(row, headers, columns.ToArray());
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var upperValue = value.ToUpperInvariant();
        IReadOnlyList<string> keepTokens = sourceConfig.TryGetValue(keepTokensKey, out var keepTokensRaw)
            ? ParseMappingCsv(keepTokensRaw)
            : Array.Empty<string>();

        return keepTokens.Any(token => HasIsolatedToken(upperValue, token.ToUpperInvariant()));
    }

    private static IReadOnlyList<string> ParseMappingCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        var rows = CsvParser.Parse(value);
        if (rows.Count == 0)
        {
            return Array.Empty<string>();
        }

        return rows[0]
            .Select(v => v.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToArray();
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
