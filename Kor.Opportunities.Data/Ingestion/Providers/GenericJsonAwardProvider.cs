#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Ingestion.Providers;

/// <summary>
/// Mapping-driven HTTP JSON award provider for CKAN-style offset/limit APIs
/// and similar paginated REST endpoints.
/// </summary>
public sealed class GenericJsonAwardProvider : IAwardProvider
{
    private const int DefaultPageSize = 100;
    private const int DefaultMaxRowsPerRun = 100_000;
    private const string DefaultUserAgent = "Kor.Opportunities.Worker/1.0 (+ilalonde@korstructural.com)";
    private const string DefaultAccept = "application/json";

    private readonly HttpClient _httpClient;
    private readonly ILogger<GenericJsonAwardProvider> _logger;
    private readonly int _maxBytesPerResponse;
    private readonly int _defaultMaxRowsPerRun;

    public GenericJsonAwardProvider(
        HttpClient httpClient,
        ILogger<GenericJsonAwardProvider> logger,
        int maxBytesPerResponse = 50 * 1024 * 1024,
        int defaultMaxRowsPerRun = DefaultMaxRowsPerRun)
    {
        _httpClient = httpClient;
        _logger = logger;
        _maxBytesPerResponse = maxBytesPerResponse > 0 ? maxBytesPerResponse : int.MaxValue;
        _defaultMaxRowsPerRun = defaultMaxRowsPerRun > 0 ? defaultMaxRowsPerRun : DefaultMaxRowsPerRun;
    }

    public OpportunitySourceType SourceType => OpportunitySourceType.GenericJsonAward;

    public async Task<IReadOnlyList<AwardCandidate>> FetchAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        var mapping = GenericJsonAwardMapping.Build(sourceConfig, _defaultMaxRowsPerRun);
        if (mapping.SqlQuery is not null && ContainsSqlPagingKeyword(mapping.SqlQuery))
        {
            _logger.LogWarning(
                "GenericJsonAward provider {SourceName}: json.sqlQuery must not contain LIMIT or OFFSET; paging is appended by the provider.",
                source.Name);
            throw new InvalidOperationException("GenericJsonAward json.sqlQuery must not contain LIMIT or OFFSET.");
        }

        if (mapping.SqlQuery is not null
            && (!string.IsNullOrWhiteSpace(mapping.FilterParam) || !string.IsNullOrWhiteSpace(mapping.FilterValue)))
        {
            _logger.LogDebug(
                "GenericJsonAward provider {SourceName}: ignoring json.filterParam/json.filterValue because json.sqlQuery is configured.",
                source.Name);
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(10, source.RequestTimeoutSeconds));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        var candidates = new List<AwardCandidate>();
        var dropped = 0;
        var processed = 0;
        var offset = 0;
        int? total = null;
        var trimmed = false;

        while (!ct.IsCancellationRequested && processed < mapping.MaxRowsPerRun)
        {
            var pageUrl = mapping.SqlQuery is not null
                ? BuildSqlPageUrl(source.BaseUrl, mapping.SqlQuery, offset, mapping.PageSize)
                : BuildPageUrl(source.BaseUrl, offset, mapping.PageSize, mapping.FilterParam, mapping.FilterValue);
            using var request = new HttpRequestMessage(HttpMethod.Get, pageUrl);
            ApplyHeaders(request, sourceConfig);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var body = await HttpReadHelpers.ReadStringWithCapAsync(
                response.Content,
                _maxBytesPerResponse,
                $"GenericJsonAward provider {source.Name}",
                timeoutCts.Token).ConfigureAwait(false);

            var root = JsonNode.Parse(body)
                ?? throw new InvalidOperationException($"GenericJsonAward provider {source.Name}: response body was not valid JSON.");

            if (mapping.SqlQuery is null && mapping.TotalPath is not null && total is null)
            {
                total = ReadInt(root, mapping.TotalPath);
            }

            if (!TryResolvePath(root, mapping.ItemsPath, out var itemsNode) || itemsNode is not JsonArray items)
            {
                throw new InvalidOperationException(
                    $"GenericJsonAward provider {source.Name}: json.itemsPath '{mapping.ItemsPath}' did not resolve to an array.");
            }

            if (items.Count == 0)
            {
                break;
            }

            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                if (processed >= mapping.MaxRowsPerRun)
                {
                    trimmed = true;
                    break;
                }

                processed++;
                if (item is null)
                {
                    dropped++;
                    continue;
                }

                var candidate = MapItem(item, mapping);
                if (candidate is null)
                {
                    dropped++;
                    continue;
                }

                candidates.Add(candidate);
            }

            if (trimmed)
            {
                break;
            }

            if (items.Count < mapping.PageSize)
            {
                break;
            }

            offset += mapping.PageSize;
            if (mapping.SqlQuery is null && total.HasValue && offset >= total.Value)
            {
                break;
            }
        }

        if (trimmed || processed >= mapping.MaxRowsPerRun)
        {
            _logger.LogWarning(
                "GenericJsonAward provider {SourceName}: row cap {MaxRows} reached; remaining rows were skipped.",
                source.Name,
                mapping.MaxRowsPerRun);
        }

        _logger.LogInformation(
            "GenericJsonAward provider {SourceName}: {Kept} award candidate(s) parsed from {Processed} item(s) ({Dropped} dropped).",
            source.Name,
            candidates.Count,
            processed,
            dropped);

        return candidates;
    }

    private static AwardCandidate? MapItem(JsonNode item, GenericJsonAwardMapping mapping)
    {
        var externalRef = ReadString(item, mapping.ExternalRefPath);
        var title = ReadString(item, mapping.TitlePath);
        var awardedTo = ReadString(item, mapping.AwardedToPath);
        var awardingOrg = mapping.AwardingOrgOverride ?? ReadString(item, mapping.AwardingOrgPath);

        if (string.IsNullOrWhiteSpace(externalRef)
            || string.IsNullOrWhiteSpace(title)
            || string.IsNullOrWhiteSpace(awardedTo)
            || string.IsNullOrWhiteSpace(awardingOrg))
        {
            return null;
        }

        var currency = mapping.ContractCurrencyOverride ?? ReadString(item, mapping.ContractCurrencyPath) ?? "CAD";
        var sourceUrl = mapping.SourceUrlOverride ?? ReadString(item, mapping.SourceUrlPath) ?? "";

        return new AwardCandidate
        {
            ExternalReference = externalRef.Trim(),
            Title = title.Trim(),
            SolicitationType = TrimOrNull(ReadString(item, mapping.SolicitationTypePath)),
            AwardingOrganization = awardingOrg.Trim(),
            AwardedToOrganization = awardedTo.Trim(),
            ContractValue = ParseDecimal(ReadString(item, mapping.ContractValuePath)),
            ContractCurrency = TrimOrNull(currency)?.ToUpperInvariant() ?? "CAD",
            AwardedAtUtc = ParseDateTimeOffset(ReadString(item, mapping.ContractDatePath)),
            ContractNumber = TrimOrNull(ReadString(item, mapping.ContractNumberPath)),
            SourceUrl = sourceUrl.Trim(),
            RawJson = item.ToJsonString(),
        };
    }

    private static void ApplyHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string> sourceConfig)
    {
        var userAgent = Get(sourceConfig, "header.userAgent") ?? DefaultUserAgent;
        var accept = Get(sourceConfig, "header.accept") ?? DefaultAccept;

        request.Headers.UserAgent.Clear();
        if (!request.Headers.UserAgent.TryParseAdd(userAgent))
        {
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        }

        request.Headers.Accept.Clear();
        if (!request.Headers.Accept.TryParseAdd(accept))
        {
            request.Headers.TryAddWithoutValidation("Accept", accept);
        }

        const string HeaderPrefix = "header.";
        foreach (var kv in sourceConfig)
        {
            if (!kv.Key.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var headerName = kv.Key[HeaderPrefix.Length..].Trim();
            if (headerName.Equals("userAgent", StringComparison.OrdinalIgnoreCase)
                || headerName.Equals("accept", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(headerName)
                || string.IsNullOrWhiteSpace(kv.Value))
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(headerName, kv.Value.Trim());
        }
    }

    private static string BuildPageUrl(
        string baseUrl,
        int offset,
        int limit,
        string? filterParam,
        string? filterValue)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var url = $"{baseUrl}{separator}offset={offset.ToString(CultureInfo.InvariantCulture)}&limit={limit.ToString(CultureInfo.InvariantCulture)}";

        if (!string.IsNullOrWhiteSpace(filterParam) && !string.IsNullOrWhiteSpace(filterValue))
        {
            url += $"&{Uri.EscapeDataString(filterParam)}={Uri.EscapeDataString(filterValue)}";
        }

        return url;
    }

    private static string BuildSqlPageUrl(string baseUrl, string sqlQuery, int offset, int limit)
    {
        var actionPrefix = ResolveActionPrefix(baseUrl);
        var pagedSql = FormattableString.Invariant($"{sqlQuery} LIMIT {limit} OFFSET {offset}");
        return $"{actionPrefix}datastore_search_sql?sql={Uri.EscapeDataString(pagedSql)}";
    }

    private static string ResolveActionPrefix(string baseUrl)
    {
        const string Marker = "/api/3/action/";
        var markerIndex = baseUrl.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            throw new InvalidOperationException(
                "GenericJsonAward json.sqlQuery requires BaseUrl to contain '/api/3/action/'.");
        }

        return baseUrl[..(markerIndex + Marker.Length)];
    }

    private static bool ContainsSqlPagingKeyword(string sqlQuery) =>
        Regex.IsMatch(sqlQuery, @"\b(LIMIT|OFFSET)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string? ReadString(JsonNode root, string? path)
    {
        if (!TryResolvePath(root, path, out var value) || value is null)
        {
            return null;
        }

        return ReadStringValue(value);
    }

    private static string? ReadStringValue(JsonNode value)
    {
        if (value is JsonValue scalar)
        {
            return scalar.ToString();
        }

        if (value is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is null)
                {
                    continue;
                }

                var nested = ReadStringValue(item);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }

            return null;
        }

        if (value is JsonObject obj)
        {
            foreach (var preferred in new[] { "value", "url", "uri", "href" })
            {
                if (TryGetPropertyCaseInsensitive(obj, preferred, out var preferredNode)
                    && preferredNode is not null)
                {
                    var preferredValue = ReadStringValue(preferredNode);
                    if (!string.IsNullOrWhiteSpace(preferredValue))
                    {
                        return preferredValue;
                    }
                }
            }

            foreach (var property in obj)
            {
                if (property.Value is null)
                {
                    continue;
                }

                var nested = ReadStringValue(property.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static bool TryResolvePath(JsonNode root, string? path, out JsonNode? value)
    {
        value = root;
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var segments = path.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            if (value is null || !TryApplySegment(value, segment, out value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryApplySegment(JsonNode current, string segment, out JsonNode? next)
    {
        next = current;
        var cursor = segment.Trim();
        var bracket = cursor.IndexOf('[');
        var propertyName = bracket >= 0 ? cursor[..bracket] : cursor;

        if (!string.IsNullOrWhiteSpace(propertyName))
        {
            if (next is not JsonObject obj || !TryGetPropertyCaseInsensitive(obj, propertyName, out next))
            {
                return false;
            }
        }

        var offset = bracket >= 0 ? bracket : cursor.Length;
        while (offset < cursor.Length)
        {
            if (cursor[offset] != '[')
            {
                return false;
            }

            var close = cursor.IndexOf(']', offset + 1);
            if (close <= offset + 1)
            {
                return false;
            }

            var rawIndex = cursor[(offset + 1)..close];
            if (!int.TryParse(rawIndex, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                || index < 0
                || next is not JsonArray arr
                || index >= arr.Count)
            {
                return false;
            }

            next = arr[index];
            offset = close + 1;
        }

        return next is not null;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonObject obj, string propertyName, out JsonNode? value)
    {
        if (obj.TryGetPropertyValue(propertyName, out value))
        {
            return true;
        }

        foreach (var property in obj)
        {
            if (property.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = null;
        return false;
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

    private static int? ReadInt(JsonNode root, string? path)
    {
        var value = ReadString(root, path);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string? TrimOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? Get(IReadOnlyDictionary<string, string> sourceConfig, string key)
    {
        return sourceConfig.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private sealed record GenericJsonAwardMapping(
        string ItemsPath,
        string? TotalPath,
        int PageSize,
        int MaxRowsPerRun,
        string? FilterParam,
        string? FilterValue,
        string? SqlQuery,
        string ExternalRefPath,
        string TitlePath,
        string? AwardingOrgPath,
        string? AwardingOrgOverride,
        string AwardedToPath,
        string ContractValuePath,
        string ContractDatePath,
        string? ContractCurrencyPath,
        string? ContractCurrencyOverride,
        string? SourceUrlPath,
        string? SourceUrlOverride,
        string? ContractNumberPath,
        string? SolicitationTypePath)
    {
        public static GenericJsonAwardMapping Build(
            IReadOnlyDictionary<string, string> sourceConfig,
            int defaultMaxRowsPerRun)
        {
            var awardingOrgPath = Get(sourceConfig, "json.awardingOrgPath");
            var awardingOrgOverride = Get(sourceConfig, "json.awardingOrgOverride");
            var currencyPath = Get(sourceConfig, "json.contractCurrencyPath");
            var currencyOverride = Get(sourceConfig, "json.contractCurrencyOverride");
            var sourceUrlPath = Get(sourceConfig, "json.sourceUrlPath");
            var sourceUrlOverride = Get(sourceConfig, "json.sourceUrlOverride");

            if (awardingOrgPath is null && awardingOrgOverride is null)
            {
                throw new InvalidOperationException("GenericJsonAward requires json.awardingOrgPath or json.awardingOrgOverride.");
            }

            if (currencyPath is null && currencyOverride is null)
            {
                throw new InvalidOperationException("GenericJsonAward requires json.contractCurrencyPath or json.contractCurrencyOverride.");
            }

            if (sourceUrlPath is null && sourceUrlOverride is null)
            {
                throw new InvalidOperationException("GenericJsonAward requires json.sourceUrlPath or json.sourceUrlOverride.");
            }

            return new GenericJsonAwardMapping(
                Required(sourceConfig, "json.itemsPath"),
                Get(sourceConfig, "json.totalPath"),
                ReadPositiveInt(sourceConfig, "json.pageSize", DefaultPageSize),
                ReadPositiveInt(sourceConfig, "json.maxRowsPerRun", defaultMaxRowsPerRun),
                Get(sourceConfig, "json.filterParam"),
                Get(sourceConfig, "json.filterValue"),
                Get(sourceConfig, "json.sqlQuery"),
                Required(sourceConfig, "json.externalRefPath"),
                Required(sourceConfig, "json.titlePath"),
                awardingOrgPath,
                awardingOrgOverride,
                Required(sourceConfig, "json.awardedToPath"),
                Required(sourceConfig, "json.contractValuePath"),
                Required(sourceConfig, "json.contractDatePath"),
                currencyPath,
                currencyOverride,
                sourceUrlPath,
                sourceUrlOverride,
                Get(sourceConfig, "json.contractNumberPath"),
                Get(sourceConfig, "json.solicitationTypePath"));
        }

        private static string Required(IReadOnlyDictionary<string, string> sourceConfig, string key)
        {
            return Get(sourceConfig, key)
                ?? throw new InvalidOperationException($"GenericJsonAward requires mapping '{key}'.");
        }

        private static int ReadPositiveInt(IReadOnlyDictionary<string, string> sourceConfig, string key, int fallback)
        {
            var value = Get(sourceConfig, key);
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
                ? parsed
                : fallback;
        }
    }
}
