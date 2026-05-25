#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Ingestion.Providers;

/// <summary>
/// Mapping-driven HTTP CSV award provider. Mirrors GenericCsvOpportunityProvider
/// but emits awarded-contract candidates for AwardIngestionService.
/// </summary>
public sealed class GenericCsvAwardProvider : IAwardProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GenericCsvAwardProvider> _logger;
    private readonly int _maxBytesPerResponse;
    private readonly int _maxRowsPerRun;

    public GenericCsvAwardProvider(
        HttpClient httpClient,
        ILogger<GenericCsvAwardProvider> logger,
        int maxBytesPerResponse = 50 * 1024 * 1024,
        int maxRowsPerRun = 5000)
    {
        _httpClient = httpClient;
        _logger = logger;
        _maxBytesPerResponse = maxBytesPerResponse > 0 ? maxBytesPerResponse : int.MaxValue;
        _maxRowsPerRun = maxRowsPerRun > 0 ? maxRowsPerRun : int.MaxValue;
    }

    public OpportunitySourceType SourceType => OpportunitySourceType.GenericCsvAward;

    public async Task<IReadOnlyList<AwardCandidate>> FetchAsync(
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

        var csv = await HttpReadHelpers.ReadStringWithCapAsync(
            response.Content,
            _maxBytesPerResponse,
            $"GenericCsvAward provider {source.Name}",
            timeoutCts.Token).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(csv))
        {
            _logger.LogWarning("GenericCsvAward provider {SourceName} returned an empty body.", source.Name);
            return Array.Empty<AwardCandidate>();
        }

        var rows = CsvParser.Parse(csv);
        if (rows.Count < 2)
        {
            _logger.LogWarning("GenericCsvAward provider {SourceName}: only {Rows} row(s) parsed; nothing to ingest.", source.Name, rows.Count);
            return Array.Empty<AwardCandidate>();
        }

        var headers = CsvParser.NormalizeHeaderRow(rows[0]);
        var candidates = new List<AwardCandidate>();
        var dropped = 0;
        var processedRows = 0;
        var trimmed = false;

        for (var i = 1; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (processedRows >= _maxRowsPerRun)
            {
                trimmed = true;
                break;
            }

            processedRows++;
            var row = rows[i];

            if (!PassesConfiguredFilter(row, headers, sourceConfig))
            {
                dropped++;
                continue;
            }

            var candidate = MapRow(row, headers, sourceConfig);
            if (candidate is null)
            {
                dropped++;
                continue;
            }

            candidates.Add(candidate);
        }

        if (trimmed)
        {
            _logger.LogWarning(
                "GenericCsvAward provider {SourceName}: row cap {MaxRows} reached; remaining rows were skipped.",
                source.Name,
                _maxRowsPerRun);
        }

        _logger.LogInformation(
            "GenericCsvAward provider {SourceName}: {Kept} award candidate(s) parsed from {Total} data row(s) ({Dropped} dropped).",
            source.Name,
            candidates.Count,
            processedRows,
            dropped);

        return candidates;
    }

    private static AwardCandidate? MapRow(
        IReadOnlyList<string> row,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> sourceConfig)
    {
        var externalRef = ReadMappedValue(row, headers, sourceConfig, "csv.externalRefPath");
        var title = ReadMappedValue(row, headers, sourceConfig, "csv.titlePath");
        var awardedTo = ReadMappedValue(row, headers, sourceConfig, "csv.awardedToPath");

        if (string.IsNullOrWhiteSpace(externalRef)
            || string.IsNullOrWhiteSpace(title)
            || string.IsNullOrWhiteSpace(awardedTo))
        {
            return null;
        }

        var awardingOrg = ReadOverrideOrMapped(row, headers, sourceConfig, "csv.awardingOrgOverride", "csv.awardingOrgPath")
            ?? "Unknown";
        var currency = ReadMappedOrLiteral(row, headers, sourceConfig, "csv.contractCurrencyPath")
            ?? "CAD";

        return new AwardCandidate
        {
            ExternalReference = externalRef.Trim(),
            Title = title.Trim(),
            SolicitationType = TrimOrNull(ReadMappedValue(row, headers, sourceConfig, "csv.solicitationTypePath")),
            AwardingOrganization = awardingOrg.Trim(),
            AwardedToOrganization = awardedTo.Trim(),
            ContractValue = ParseDecimal(ReadMappedValue(row, headers, sourceConfig, "csv.contractValuePath")),
            ContractCurrency = TrimOrNull(currency)?.ToUpperInvariant() ?? "CAD",
            AwardedAtUtc = ParseDateTimeOffset(ReadMappedValue(row, headers, sourceConfig, "csv.awardedAtPath")),
            IssuingLocation = TrimOrNull(ReadMappedValue(row, headers, sourceConfig, "csv.issuingLocationPath")),
            SupplierAddress = TrimOrNull(ReadMappedValue(row, headers, sourceConfig, "csv.supplierAddressPath")),
            ContactEmail = TrimOrNull(ReadMappedValue(row, headers, sourceConfig, "csv.contactEmailPath")),
            ContractNumber = TrimOrNull(ReadMappedValue(row, headers, sourceConfig, "csv.contractNumberPath")),
            SourceUrl = TrimOrNull(ReadMappedOrLiteral(row, headers, sourceConfig, "csv.sourceUrlPath")) ?? "",
            RawJson = BuildRowJson(row, headers),
        };
    }

    private static bool PassesConfiguredFilter(
        IReadOnlyList<string> row,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> sourceConfig)
    {
        if (!sourceConfig.TryGetValue("csv.filterColumn", out var column)
            || string.IsNullOrWhiteSpace(column)
            || !sourceConfig.TryGetValue("csv.filterRegex", out var pattern)
            || string.IsNullOrWhiteSpace(pattern))
        {
            return true;
        }

        var value = CsvParser.FirstValue(row, headers, column);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Regex.IsMatch(
            value.Trim(),
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    private static string? ReadOverrideOrMapped(
        IReadOnlyList<string> row,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> sourceConfig,
        string overrideKey,
        string pathKey)
    {
        if (sourceConfig.TryGetValue(overrideKey, out var overrideValue)
            && !string.IsNullOrWhiteSpace(overrideValue))
        {
            return overrideValue.Trim();
        }

        return ReadMappedValue(row, headers, sourceConfig, pathKey);
    }

    private static string? ReadMappedValue(
        IReadOnlyList<string> row,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> sourceConfig,
        string key)
    {
        if (!sourceConfig.TryGetValue(key, out var mapping) || string.IsNullOrWhiteSpace(mapping))
        {
            return null;
        }

        return CsvParser.FirstValue(row, headers, mapping);
    }

    private static string? ReadMappedOrLiteral(
        IReadOnlyList<string> row,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> sourceConfig,
        string key)
    {
        if (!sourceConfig.TryGetValue(key, out var mapping) || string.IsNullOrWhiteSpace(mapping))
        {
            return null;
        }

        var value = CsvParser.FirstValue(row, headers, mapping);
        return !string.IsNullOrWhiteSpace(value) ? value.Trim() : mapping.Trim();
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(
            value.Trim(),
            NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string? TrimOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string BuildRowJson(IReadOnlyList<string> row, IReadOnlyList<string> headers)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count && i < row.Count; i++)
        {
            var key = headers[i];
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            dict[key] = row[i] ?? string.Empty;
        }

        return JsonSerializer.Serialize(dict);
    }
}
