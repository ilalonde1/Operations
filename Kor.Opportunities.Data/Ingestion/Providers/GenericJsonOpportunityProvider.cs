#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Ingestion.Providers;

/// <summary>
/// Mapping-driven HTTP JSON provider. Source-specific field paths live in
/// <c>OpportunitySourceMappings</c> so new JSON sources can be added by config.
/// </summary>
public sealed class GenericJsonOpportunityProvider : IOpportunityProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GenericJsonOpportunityProvider> _logger;
    private readonly int _maxBytesPerResponse;
    private readonly int _maxItemsPerRun;

    public GenericJsonOpportunityProvider(
        HttpClient httpClient,
        ILogger<GenericJsonOpportunityProvider> logger,
        int maxBytesPerResponse = 50 * 1024 * 1024,
        int maxItemsPerRun = 5000)
    {
        _httpClient = httpClient;
        _logger = logger;
        _maxBytesPerResponse = maxBytesPerResponse > 0 ? maxBytesPerResponse : int.MaxValue;
        _maxItemsPerRun = maxItemsPerRun > 0 ? maxItemsPerRun : int.MaxValue;
    }

    public OpportunitySourceType SourceType => OpportunitySourceType.GenericJson;

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

        const string HeaderMappingPrefix = "http.header.";
        foreach (var kv in sourceConfig)
        {
            if (kv.Key is null || !kv.Key.StartsWith(HeaderMappingPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var headerName = kv.Key.Substring(HeaderMappingPrefix.Length).Trim();
            var headerValue = kv.Value?.Trim();
            if (string.IsNullOrWhiteSpace(headerName) || string.IsNullOrWhiteSpace(headerValue))
            {
                continue;
            }

            // User-Agent uses a typed setter; everything else goes through
            // TryAddWithoutValidation because some headers (e.g. Authorization
            // with custom schemes) reject strict validation.
            if (headerName.Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.UserAgent.Clear();
                try
                {
                    request.Headers.UserAgent.ParseAdd(headerValue);
                }
                catch (FormatException)
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", headerValue);
                }

                continue;
            }

            if (!request.Headers.TryAddWithoutValidation(headerName, headerValue))
            {
                _logger.LogWarning(
                    "GenericJson provider {SourceName}: failed to apply custom header {HeaderName}.",
                    source.Name, headerName);
            }
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await HttpReadHelpers.ReadStringWithCapAsync(
            response.Content,
            _maxBytesPerResponse,
            $"GenericJson provider {source.Name}",
            timeoutCts.Token).ConfigureAwait(false);

        using var document = JsonDocument.Parse(body);

        var mapping = GenericJsonMapping.Build(sourceConfig);
        var candidates = new List<OpportunityCandidate>();
        var dropped = 0;
        var total = 0;
        var trimmed = false;

        foreach (var item in ResolveItems(document.RootElement, mapping))
        {
            ct.ThrowIfCancellationRequested();
            if (total >= _maxItemsPerRun)
            {
                trimmed = true;
                break;
            }

            total++;

            var title = ReadString(item, mapping.TitlePath);
            var url = ReadString(item, mapping.UrlPath);
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
            {
                dropped++;
                continue;
            }

            var buyer = ReadString(item, mapping.BuyerPath);
            var estimatedValueRaw = ReadString(item, mapping.EstimatedValuePath);
            decimal? estimatedValue = null;
            if (!string.IsNullOrWhiteSpace(estimatedValueRaw)
                && decimal.TryParse(
                    estimatedValueRaw,
                    NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
                    CultureInfo.InvariantCulture,
                    out var parsedEstimatedValue))
            {
                estimatedValue = parsedEstimatedValue;
            }

            candidates.Add(new OpportunityCandidate
            {
                Title = title.Trim(),
                Buyer = string.IsNullOrWhiteSpace(buyer) ? "Unknown" : buyer.Trim(),
                Location = TrimOrNull(ReadString(item, mapping.LocationPath)),
                Url = url.Trim(),
                Description = TrimOrNull(ReadString(item, mapping.DescriptionPath)),
                PostedDateUtc = ParseDateTimeOffset(ReadString(item, mapping.PostedDatePath)),
                SubmissionDeadlineUtc = ParseDateTimeOffset(ReadString(item, mapping.DeadlinePath)),
                ProjectCity = mapping.CityOverride ?? TrimOrNull(ReadString(item, mapping.CityPath)),
                ProjectProvince = mapping.ProvinceOverride ?? TrimOrNull(ReadString(item, mapping.ProvincePath)),
                EstimatedValueCad = estimatedValue,
                ExternalReference = TrimOrNull(ReadString(item, mapping.ExternalReferencePath)),
                RawJson = item.GetRawText(),
            });
        }

        if (trimmed)
        {
            _logger.LogWarning(
                "GenericJson provider {SourceName}: item cap {MaxItems} reached; remaining items were skipped.",
                source.Name,
                _maxItemsPerRun);
        }

        _logger.LogInformation(
            "GenericJson provider {SourceName}: {Kept} candidate(s) parsed from {Total} item(s) ({Dropped} dropped).",
            source.Name,
            candidates.Count,
            total,
            dropped);

        return candidates;
    }

    private static IEnumerable<JsonElement> ResolveItems(JsonElement root, GenericJsonMapping mapping)
    {
        if (TryResolvePath(root, mapping.ItemsPath, out var atPath))
        {
            if (atPath.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in atPath.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        yield return item;
                    }
                }

                yield break;
            }

            if (atPath.ValueKind == JsonValueKind.Object)
            {
                yield return atPath;
                yield break;
            }
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    yield return item;
                }
            }

            yield break;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            yield return root;
        }
    }

    private static string? ReadString(JsonElement root, string? path)
    {
        // A null/empty field path means "not configured" -> no value. Without
        // this guard TryResolvePath returns the whole item object and
        // ReadStringValue extracts a garbage string (e.g. bid_number leaking
        // into ProjectCity/ProjectProvince on sources with no city/province map).
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!TryResolvePath(root, path, out var value))
        {
            return null;
        }

        return ReadStringValue(value);
    }

    private static string? ReadStringValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array => ReadFirstStringFromArray(value),
            JsonValueKind.Object => ReadStringFromObject(value),
            _ => null
        };
    }

    private static string? ReadFirstStringFromArray(JsonElement array)
    {
        foreach (var item in array.EnumerateArray())
        {
            var scalar = ReadStringValue(item);
            if (!string.IsNullOrWhiteSpace(scalar))
            {
                return scalar;
            }
        }

        return null;
    }

    private static string? ReadStringFromObject(JsonElement obj)
    {
        if (TryGetPreferredObjectString(obj, "value", out var value))
        {
            return value;
        }

        if (TryGetPreferredObjectString(obj, "url", out var url))
        {
            return url;
        }

        if (TryGetPreferredObjectString(obj, "uri", out var uri))
        {
            return uri;
        }

        if (TryGetPreferredObjectString(obj, "href", out var href))
        {
            return href;
        }

        foreach (var property in obj.EnumerateObject())
        {
            var nested = ReadStringValue(property.Value);
            if (!string.IsNullOrWhiteSpace(nested))
            {
                return nested;
            }
        }

        return null;
    }

    private static bool TryGetPreferredObjectString(JsonElement obj, string propertyName, out string? value)
    {
        value = null;
        if (!TryGetPropertyCaseInsensitive(obj, propertyName, out var property))
        {
            return false;
        }

        value = ReadStringValue(property);
        return !string.IsNullOrWhiteSpace(value);
    }

    internal static bool TryResolvePath(JsonElement root, string? path, out JsonElement value)
    {
        value = root;
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var remaining = path.Trim();
        var segments = remaining.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            if (!TryApplySegment(value, segment, out value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryApplySegment(JsonElement current, string segment, out JsonElement next)
    {
        next = current;
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        var cursor = segment;
        var bracket = cursor.IndexOf('[');
        var propertyName = bracket >= 0 ? cursor[..bracket] : cursor;
        if (!string.IsNullOrWhiteSpace(propertyName))
        {
            if (current.ValueKind != JsonValueKind.Object || !TryGetPropertyCaseInsensitive(current, propertyName, out next))
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
            if (!int.TryParse(rawIndex, out var index) || index < 0 || next.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var found = false;
            var i = 0;
            foreach (var item in next.EnumerateArray())
            {
                if (i == index)
                {
                    next = item;
                    found = true;
                    break;
                }

                i++;
            }

            if (!found)
            {
                return false;
            }

            offset = close + 1;
        }

        return true;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
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

    private sealed record GenericJsonMapping(
        string? ItemsPath,
        string TitlePath,
        string BuyerPath,
        string LocationPath,
        string UrlPath,
        string DescriptionPath,
        string PostedDatePath,
        string DeadlinePath,
        string? CityPath,
        string? ProvincePath,
        string? EstimatedValuePath,
        string? ExternalReferencePath,
        string? CityOverride,
        string? ProvinceOverride)
    {
        public static GenericJsonMapping Build(IReadOnlyDictionary<string, string> sourceConfig)
        {
            return new GenericJsonMapping(
                Get(sourceConfig, "json.itemsPath"),
                Get(sourceConfig, "json.titlePath") ?? "title",
                Get(sourceConfig, "json.buyerPath") ?? "buyer",
                Get(sourceConfig, "json.locationPath") ?? "location",
                Get(sourceConfig, "json.urlPath") ?? "url",
                Get(sourceConfig, "json.descriptionPath") ?? "description",
                Get(sourceConfig, "json.postedDatePath") ?? "postedDate",
                Get(sourceConfig, "json.deadlinePath") ?? "deadline",
                Get(sourceConfig, "json.cityPath"),
                Get(sourceConfig, "json.provincePath"),
                Get(sourceConfig, "json.estimatedValuePath"),
                Get(sourceConfig, "json.externalReferencePath"),
                Get(sourceConfig, "json.cityOverride"),
                Get(sourceConfig, "json.provinceOverride"));
        }

        private static string? Get(IReadOnlyDictionary<string, string> sourceConfig, string key)
        {
            if (!sourceConfig.TryGetValue(key, out var value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }
    }
}
