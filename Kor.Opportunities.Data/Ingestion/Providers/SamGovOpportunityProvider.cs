#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Ingestion.Providers;

public sealed class SamGovOpportunityProvider : IOpportunityProvider
{
    private const int MaxPages = 5;
    private const int PageLimit = 1000;
    private const int DefaultPostedDaysLookback = 7;
    private const string EngineeringNaics = "541330";

    // Client-side state filter. SAM.gov v2 API does NOT support multi-value
    // state via repeated query params (verified 2026-05-18: state=CA&state=NY
    // returns 0 records). Pull broadly and filter on the response.
    private static readonly HashSet<string> PacificRimStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "CA", "OR", "WA", "AK", "HI"
    };

    // Client-side notice-type filter. Matches response field `baseType`
    // (full names, not letter codes). "Solicitation" letter code 'o' was
    // retired (API returned "Valid values are p,k,r,g,s,f,i,u,a" 2026-05-18).
    private static readonly HashSet<string> ActiveBidBaseTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Combined Synopsis/Solicitation",
        "Presolicitation",
        "Sources Sought",
        "Solicitation", // kept for forward-compat if it reappears as a baseType value
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<SamGovOpportunityProvider> _logger;
    private readonly string _apiKey;
    private readonly int _postedDaysLookback;

    public SamGovOpportunityProvider(
        HttpClient httpClient,
        ILogger<SamGovOpportunityProvider> logger,
        string apiKey,
        int postedDaysLookback)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = apiKey;
        if (postedDaysLookback <= 0)
        {
            _logger.LogWarning(
                "SAM.gov posted-days lookback {Lookback} is invalid; using {DefaultLookback}.",
                postedDaysLookback,
                DefaultPostedDaysLookback);
            _postedDaysLookback = DefaultPostedDaysLookback;
        }
        else
        {
            _postedDaysLookback = postedDaysLookback;
        }
    }

    public OpportunitySourceType SourceType => OpportunitySourceType.SamGov;

    public async Task<IReadOnlyList<OpportunityCandidate>> FetchAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("SAM.gov API key not configured; skipping pull");
            return Array.Empty<OpportunityCandidate>();
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(10, source.RequestTimeoutSeconds));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        var postedTo = DateTime.UtcNow;
        var postedFrom = postedTo.AddDays(-_postedDaysLookback);

        var candidates = new List<OpportunityCandidate>();
        var totalRecords = 0;
        var processed = 0;
        var dropped = 0;
        var pages = 0;
        var offset = 0;

        while (pages < MaxPages)
        {
            ct.ThrowIfCancellationRequested();

            var uri = BuildUri(source.BaseUrl, postedFrom, postedTo, offset);
            _logger.LogInformation(
                "SAM.gov request URL (api_key redacted): {Url}",
                uri.Replace(_apiKey, "REDACTED", StringComparison.Ordinal));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Kor.Opportunities.Worker/1.0 (+ilalonde@korstructural.com)");

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
            var page = await JsonSerializer.DeserializeAsync<SamGovSearchResponse>(stream, JsonOptions, timeoutCts.Token)
                .ConfigureAwait(false);

            pages++;
            totalRecords = page?.TotalRecords ?? 0;
            var data = page?.OpportunitiesData ?? Array.Empty<SamGovOpportunity>();
            if (data.Count == 0)
            {
                break;
            }

            foreach (var opportunity in data)
            {
                processed++;

                if (HasForeignPlaceOfPerformance(opportunity))
                {
                    dropped++;
                    continue;
                }

                var candidate = MapRow(opportunity);
                if (candidate is null)
                {
                    dropped++;
                    continue;
                }

                candidates.Add(candidate);
            }

            offset += data.Count;
            if (offset >= totalRecords)
            {
                break;
            }
        }

        if (pages >= MaxPages && offset < totalRecords)
        {
            _logger.LogWarning(
                "SAM.gov page safety cap hit after {Pages} pages ({Fetched}/{Total} records fetched).",
                pages,
                offset,
                totalRecords);
        }

        _logger.LogInformation(
            "SAM.gov: {Kept} candidate(s) parsed from {Total} record(s) ({Dropped} dropped, {Pages} pages).",
            candidates.Count,
            processed,
            dropped,
            pages);

        return candidates;
    }

    private string BuildUri(string baseUrl, DateTime postedFrom, DateTime postedTo, int offset)
    {
        // state and ptype filters are applied client-side in MapRow; see the
        // PacificRimStates / ActiveBidBaseTypes HashSet comments for the why.
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("api_key", _apiKey),
            new("postedFrom", postedFrom.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)),
            new("postedTo", postedTo.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)),
            new("ncode", EngineeringNaics),
            new("limit", PageLimit.ToString(CultureInfo.InvariantCulture)),
            new("offset", offset.ToString(CultureInfo.InvariantCulture)),
        };

        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return baseUrl + separator + BuildQueryString(pairs);
    }

    private static string BuildQueryString(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        var sb = new StringBuilder();
        foreach (var kvp in pairs)
        {
            if (sb.Length > 0)
            {
                sb.Append('&');
            }

            sb.Append(Uri.EscapeDataString(kvp.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(kvp.Value));
        }

        return sb.ToString();
    }

    private static OpportunityCandidate? MapRow(SamGovOpportunity opportunity)
    {
        if (string.IsNullOrWhiteSpace(opportunity.NoticeId)
            || string.IsNullOrWhiteSpace(opportunity.Title)
            || string.IsNullOrWhiteSpace(opportunity.UiLink))
        {
            return null;
        }

        // Client-side state filter. Drop notices outside KOR's Pacific Rim footprint.
        // Empty state usually means non-US or HQ-only; drop too.
        var stateCode = opportunity.PlaceOfPerformance?.State?.Code?.Trim();
        if (string.IsNullOrEmpty(stateCode) || !PacificRimStates.Contains(stateCode))
        {
            return null;
        }

        // Client-side notice-type filter. Match against the response's baseType
        // (full name string), not the request-side ptype letter codes.
        var bt = opportunity.BaseType?.Trim();
        if (string.IsNullOrEmpty(bt) || !ActiveBidBaseTypes.Contains(bt))
        {
            return null;
        }

        var state = FirstNonBlank(opportunity.PlaceOfPerformance?.State?.Code, opportunity.OfficeAddress?.State);
        var description = string.IsNullOrWhiteSpace(opportunity.Description)
            || opportunity.Description.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? null
                : opportunity.Description.Trim();
        var postedDate = ParsePostedDate(opportunity.PostedDate);
        var deadline = ParseDeadline(opportunity.ResponseDeadLine);

        var contact = opportunity.PointOfContact?.Find(p =>
            !string.IsNullOrWhiteSpace(p.Email)
            || !string.IsNullOrWhiteSpace(p.FullName)
            || !string.IsNullOrWhiteSpace(p.Phone));
        var commodityCodes = string.IsNullOrWhiteSpace(opportunity.NaicsCode)
            ? null
            : new List<string> { opportunity.NaicsCode.Trim() };

        return new OpportunityCandidate
        {
            Title = opportunity.Title.Trim(),
            Buyer = FirstNonBlank(opportunity.SubTier, opportunity.Department, opportunity.Office) ?? "U.S. Federal",
            Location = string.IsNullOrWhiteSpace(state) ? null : state.Trim().ToUpperInvariant(),
            Url = opportunity.UiLink.Trim(),
            Description = description,
            PostedDateUtc = postedDate,
            SubmissionDeadlineUtc = deadline,
            ProjectCity = string.IsNullOrWhiteSpace(opportunity.PlaceOfPerformance?.City?.Name)
                ? null
                : opportunity.PlaceOfPerformance.City.Name.Trim(),
            ProjectProvince = TrimUpper(opportunity.PlaceOfPerformance?.State?.Code, 20),
            EstimatedValueCad = null,
            BuyerContactName = string.IsNullOrWhiteSpace(contact?.FullName) ? null : contact.FullName.Trim(),
            BuyerContactEmail = string.IsNullOrWhiteSpace(contact?.Email) ? null : contact.Email.Trim(),
            BuyerContactPhone = string.IsNullOrWhiteSpace(contact?.Phone) ? null : contact.Phone.Trim(),
            CommodityCodes = commodityCodes,
            ExternalReference = opportunity.NoticeId.Trim(),
            RawJson = JsonSerializer.Serialize(opportunity, JsonOptions),
        };
    }

    private static bool HasForeignPlaceOfPerformance(SamGovOpportunity opportunity)
    {
        var country = opportunity.PlaceOfPerformance?.Country?.Code;
        return !string.IsNullOrWhiteSpace(country)
            && !string.Equals(country.Trim(), "USA", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? ParsePostedDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParseExact(
            value.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? new DateTimeOffset(DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc))
            : null;
    }

    private static DateTimeOffset? ParseDeadline(string? value)
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

    private static string? FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? TrimUpper(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().ToUpperInvariant();
        return trimmed.Length <= max ? trimmed : trimmed.Substring(0, max);
    }

    private sealed class SamGovSearchResponse
    {
        [JsonPropertyName("totalRecords")]
        public int TotalRecords { get; init; }

        [JsonPropertyName("opportunitiesData")]
        public IReadOnlyList<SamGovOpportunity>? OpportunitiesData { get; init; }
    }

    private sealed class SamGovOpportunity
    {
        [JsonPropertyName("noticeId")]
        public string? NoticeId { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("department")]
        public string? Department { get; init; }

        [JsonPropertyName("subTier")]
        public string? SubTier { get; init; }

        [JsonPropertyName("office")]
        public string? Office { get; init; }

        [JsonPropertyName("postedDate")]
        public string? PostedDate { get; init; }

        [JsonPropertyName("responseDeadLine")]
        public string? ResponseDeadLine { get; init; }

        [JsonPropertyName("naicsCode")]
        public string? NaicsCode { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("baseType")]
        public string? BaseType { get; init; }

        [JsonPropertyName("uiLink")]
        public string? UiLink { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("placeOfPerformance")]
        public SamGovPlaceOfPerformance? PlaceOfPerformance { get; init; }

        [JsonPropertyName("officeAddress")]
        public SamGovOfficeAddress? OfficeAddress { get; init; }

        [JsonPropertyName("pointOfContact")]
        public List<SamGovPointOfContact>? PointOfContact { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; init; }
    }

    private sealed class SamGovPointOfContact
    {
        [JsonPropertyName("fullName")]
        public string? FullName { get; init; }

        [JsonPropertyName("email")]
        public string? Email { get; init; }

        [JsonPropertyName("phone")]
        public string? Phone { get; init; }
    }

    private sealed class SamGovPlaceOfPerformance
    {
        [JsonPropertyName("city")]
        public SamGovNamedValue? City { get; init; }

        [JsonPropertyName("state")]
        public SamGovCodedValue? State { get; init; }

        [JsonPropertyName("country")]
        public SamGovCodedValue? Country { get; init; }
    }

    private sealed class SamGovNamedValue
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }

    private sealed class SamGovCodedValue
    {
        [JsonPropertyName("code")]
        public string? Code { get; init; }
    }

    private sealed class SamGovOfficeAddress
    {
        [JsonPropertyName("city")]
        public string? City { get; init; }

        [JsonPropertyName("state")]
        public string? State { get; init; }

        [JsonPropertyName("zipcode")]
        public string? Zipcode { get; init; }
    }
}
