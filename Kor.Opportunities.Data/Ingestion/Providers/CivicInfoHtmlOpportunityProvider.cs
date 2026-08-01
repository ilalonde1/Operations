#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Ingestion.Providers;

public sealed class CivicInfoHtmlOpportunityProvider : IOpportunityProvider
{
    private static readonly string[] DefaultCardSelectors =
    [
        "article",
        ".job",
        ".job-card",
        ".job-listing",
        ".listing-item",
        "li"
    ];

    private static readonly string[] DefaultTitleSelectors =
    [
        "h1 a",
        "h2 a",
        "h3 a",
        "a.job-title",
        ".job-title a",
        "a"
    ];

    private static readonly string[] DefaultBuyerSelectors =
    [
        ".organization",
        ".org",
        ".company",
        ".employer",
        "[data-company]"
    ];

    private static readonly string[] DefaultLocationSelectors =
    [
        ".location",
        ".job-location",
        "[data-location]"
    ];

    private static readonly string[] DefaultDescriptionSelectors =
    [
        ".description",
        ".summary",
        ".snippet",
        "p"
    ];

    private static readonly string[] DefaultDateSelectors =
    [
        "time",
        ".posted-date",
        ".date"
    ];

    private static readonly Regex RelativeAgoRegex = new(
        @"(?<num>\d+|a|an)\s+(?<unit>second|minute|hour|day|week|month|year)s?\s+ago",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DeadlinePrefixRegex = new(
        @"^\s*(Expires|Closes|Closing|Deadline|Due)\s*:?\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly HtmlParser _htmlParser;
    private readonly ILogger<CivicInfoHtmlOpportunityProvider> _logger;

    public CivicInfoHtmlOpportunityProvider(
        HttpClient httpClient,
        ILogger<CivicInfoHtmlOpportunityProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _htmlParser = new HtmlParser();
    }

    public OpportunitySourceType SourceType => OpportunitySourceType.CivicInfoHtml;

    public async Task<IReadOnlyList<OpportunityCandidate>> FetchAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        var listingPath = GetConfig(sourceConfig, "html.listingPath");
        if (string.IsNullOrWhiteSpace(listingPath))
        {
            _logger.LogWarning("CivicInfoHtml provider {SourceName}: html.listingPath is required.", source.Name);
            return Array.Empty<OpportunityCandidate>();
        }

        var baseUri = BuildBaseUri(source.BaseUrl);
        var listingRootUri = BuildListingRootUri(baseUri, listingPath);
        var maxPages = Math.Clamp(GetIntConfig(sourceConfig, "html.maxPages", 5), 1, 20);
        var crawlDelaySeconds = Math.Clamp(GetIntConfig(sourceConfig, "html.crawlDelaySeconds", 5), 1, 60);
        var timeout = TimeSpan.FromSeconds(Math.Max(10, source.RequestTimeoutSeconds));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var token = timeoutCts.Token;

        var cardSelectors = GetSelectors(sourceConfig, "html.cardSelector", DefaultCardSelectors);
        var titleSelectors = GetSelectors(sourceConfig, "html.titleSelector", DefaultTitleSelectors);
        var buyerSelectors = GetSelectors(sourceConfig, "html.buyerSelector", DefaultBuyerSelectors);
        var locationSelectors = GetSelectors(sourceConfig, "html.locationSelector", DefaultLocationSelectors);
        var descriptionSelectors = GetSelectors(sourceConfig, "html.descriptionSelector", DefaultDescriptionSelectors);
        var dateSelectors = GetSelectors(sourceConfig, "html.dateSelector", DefaultDateSelectors);
        var deadlineSelectors = GetSelectors(sourceConfig, "html.deadlineSelector", Array.Empty<string>());

        var currentPageUri = listingRootUri;
        var lastRequestUtc = DateTime.MinValue;
        var pagesFetched = 0;
        var candidates = new List<OpportunityCandidate>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var page = 1; page <= maxPages && currentPageUri is not null; page++)
        {
            if (!IsAllowedRequest(baseUri, listingRootUri, currentPageUri))
            {
                _logger.LogWarning(
                    "CivicInfoHtml provider {SourceName}: skipping disallowed page URL {Url}.",
                    source.Name,
                    currentPageUri);
                break;
            }

            await EnforceDelayAsync(lastRequestUtc, crawlDelaySeconds, token).ConfigureAwait(false);
            lastRequestUtc = DateTime.UtcNow;

            IDocument document;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, currentPageUri);
                request.Headers.UserAgent.ParseAdd("Kor.Opportunities.Worker/1.0 (+ilalonde@korstructural.com)");
                ApplyCustomHeaders(request, source, sourceConfig);

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var html = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                document = await _htmlParser.ParseDocumentAsync(html, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "CivicInfoHtml provider {SourceName}: failed to fetch or parse page {Page}: {Url}.",
                    source.Name,
                    page,
                    currentPageUri);

                currentPageUri = AdvanceToNextPageUrl(listingRootUri, page, maxPages);
                continue;
            }

            pagesFetched++;
            var pageCandidates = ParseCards(
                document,
                baseUri,
                cardSelectors,
                titleSelectors,
                buyerSelectors,
                locationSelectors,
                descriptionSelectors,
                dateSelectors,
                deadlineSelectors,
                seenUrls);

            candidates.AddRange(pageCandidates);

            _logger.LogInformation(
                "CivicInfoHtml provider {SourceName}: page {Page} produced {Count} candidate(s).",
                source.Name,
                page,
                pageCandidates.Count);

            currentPageUri = GetNextPageUri(document, baseUri, listingRootUri, page, maxPages)
                ?? AdvanceToNextPageUrl(listingRootUri, page, maxPages);
        }

        _logger.LogInformation(
            "CivicInfoHtml provider {SourceName}: {Total} candidate(s) parsed across {Pages} page(s).",
            source.Name,
            candidates.Count,
            pagesFetched);

        return candidates;
    }

    private List<OpportunityCandidate> ParseCards(
        IDocument document,
        Uri baseUri,
        IReadOnlyList<string> cardSelectors,
        IReadOnlyList<string> titleSelectors,
        IReadOnlyList<string> buyerSelectors,
        IReadOnlyList<string> locationSelectors,
        IReadOnlyList<string> descriptionSelectors,
        IReadOnlyList<string> dateSelectors,
        IReadOnlyList<string> deadlineSelectors,
        HashSet<string> seenUrls)
    {
        var candidates = new List<OpportunityCandidate>();

        foreach (var selector in cardSelectors)
        {
            foreach (var card in document.QuerySelectorAll(selector))
            {
                var title = SelectText(card, titleSelectors);
                var urlRaw = SelectLink(card, titleSelectors) ?? SelectLink(card, ["a"]);

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(urlRaw))
                {
                    continue;
                }

                if (!TryNormalizeUrl(baseUri, urlRaw, out var normalizedUrl))
                {
                    continue;
                }

                if (!seenUrls.Add(normalizedUrl))
                {
                    continue;
                }

                candidates.Add(new OpportunityCandidate
                {
                    Title = title,
                    Buyer = SelectText(card, buyerSelectors) ?? "Unknown",
                    Location = SelectText(card, locationSelectors),
                    Url = normalizedUrl,
                    Description = TrimTo(SelectText(card, descriptionSelectors), 1200),
                    PostedDateUtc = ParsePostedDate(card, dateSelectors),
                    SubmissionDeadlineUtc = ParseDeadline(card, deadlineSelectors),
                    RawJson = TrimTo(card.OuterHtml.Trim(), 4000),
                    ExternalReference = null,
                });
            }

            if (candidates.Count > 0)
            {
                break;
            }
        }

        return candidates;
    }

    internal static string? SelectText(IElement root, IEnumerable<string> selectors)
    {
        foreach (var raw in selectors)
        {
            var (selector, attr) = ParseSelectorSpec(raw);
            var node = root.QuerySelector(selector);
            if (node is null)
            {
                continue;
            }

            string? text;
            if (attr is not null)
            {
                text = node.GetAttribute(attr)?.Trim();
            }
            else
            {
                text = node.TextContent?.Trim();
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static (string Selector, string? Attribute) ParseSelectorSpec(string raw)
    {
        var pipeIdx = raw.LastIndexOf('|');
        if (pipeIdx < 0 || pipeIdx == raw.Length - 1)
        {
            return (raw.Trim(), null);
        }

        var selector = raw[..pipeIdx].Trim();
        var attr = raw[(pipeIdx + 1)..].Trim();
        return (selector, string.IsNullOrEmpty(attr) ? null : attr);
    }

    internal static string? SelectLink(IElement root, IEnumerable<string> selectors)
    {
        foreach (var selector in selectors)
        {
            var node = root.QuerySelector(selector);
            if (node is null)
            {
                continue;
            }

            var href = node.GetAttribute("href")?.Trim();
            if (!string.IsNullOrWhiteSpace(href))
            {
                return href;
            }

            if (node is IHtmlAnchorElement anchor)
            {
                href = anchor.Href?.Trim();
                if (!string.IsNullOrWhiteSpace(href))
                {
                    return href;
                }
            }
        }

        return null;
    }

    private static DateTimeOffset? ParsePostedDate(IElement card, IEnumerable<string> dateSelectors)
    {
        foreach (var raw in dateSelectors)
        {
            var (selector, attr) = ParseSelectorSpec(raw);
            var node = card.QuerySelector(selector);
            if (node is null)
            {
                continue;
            }

            var dateValue = attr is not null
                ? node.GetAttribute(attr)
                : (node.GetAttribute("datetime") ?? node.TextContent);
            if (string.IsNullOrWhiteSpace(dateValue))
            {
                continue;
            }

            var trimmed = dateValue.Trim();

            if (DateTimeOffset.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
            {
                return parsed;
            }

            var relative = ParseRelativeAgo(trimmed, DateTimeOffset.UtcNow);
            if (relative is not null)
            {
                return relative;
            }
        }

        return null;
    }

    internal static DateTimeOffset? ParseRelativeAgo(string text, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var m = RelativeAgoRegex.Match(text);
        if (!m.Success)
        {
            return null;
        }

        var numStr = m.Groups["num"].Value;
        int num;
        if (string.Equals(numStr, "a", StringComparison.OrdinalIgnoreCase)
            || string.Equals(numStr, "an", StringComparison.OrdinalIgnoreCase))
        {
            num = 1;
        }
        else if (!int.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out num))
        {
            return null;
        }

        var unit = m.Groups["unit"].Value.ToLowerInvariant();
        return unit switch
        {
            "second" => now.AddSeconds(-num),
            "minute" => now.AddMinutes(-num),
            "hour" => now.AddHours(-num),
            "day" => now.AddDays(-num),
            "week" => now.AddDays(-7 * num),
            "month" => now.AddMonths(-num),
            "year" => now.AddYears(-num),
            _ => null,
        };
    }

    private static DateTimeOffset? ParseDeadline(IElement card, IEnumerable<string> deadlineSelectors)
    {
        foreach (var raw in deadlineSelectors)
        {
            var (selector, attr) = ParseSelectorSpec(raw);
            var node = card.QuerySelector(selector);
            if (node is null)
            {
                continue;
            }

            var value = attr is not null
                ? node.GetAttribute(attr)
                : (node.GetAttribute("datetime") ?? node.TextContent);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var stripped = DeadlinePrefixRegex.Replace(value.Trim(), string.Empty).Trim();
            if (stripped.Length == 0)
            {
                continue;
            }

            if (DateTimeOffset.TryParse(
                stripped,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    internal static Uri? GetNextPageUri(
        IDocument document,
        Uri baseUri,
        Uri listingRootUri,
        int page,
        int maxPages)
    {
        if (page >= maxPages)
        {
            return null;
        }

        var nextLink = document.QuerySelector("a[rel='next']")?.GetAttribute("href")
            ?? document.QuerySelector(".next a")?.GetAttribute("href")
            ?? document.QuerySelector("a.next")?.GetAttribute("href");

        if (string.IsNullOrWhiteSpace(nextLink) ||
            !TryNormalizeUrl(baseUri, nextLink, out var normalized))
        {
            return null;
        }

        var nextUri = new Uri(normalized);
        return IsAllowedRequest(baseUri, listingRootUri, nextUri) ? nextUri : null;
    }

    internal static Uri? AdvanceToNextPageUrl(Uri listingRootUri, int page, int maxPages)
    {
        if (page >= maxPages)
        {
            return null;
        }

        var nextPage = page + 1;
        var separator = listingRootUri.Query.Length == 0 ? "?" : "&";
        var next = $"{listingRootUri}{separator}page={nextPage}";

        return Uri.TryCreate(next, UriKind.Absolute, out var parsed) ? parsed : null;
    }

    internal static Uri BuildBaseUri(string rawBaseUrl)
    {
        if (!Uri.TryCreate(rawBaseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("CivicInfoHtml source BaseUrl must be a valid absolute URL.");
        }

        return uri;
    }

    internal static Uri BuildListingRootUri(Uri baseUri, string listingPath)
    {
        if (string.IsNullOrWhiteSpace(listingPath))
        {
            throw new InvalidOperationException("html.listingPath is required.");
        }

        return Uri.TryCreate(baseUri, listingPath, out var uri)
            ? uri
            : throw new InvalidOperationException("Unable to create listing URL from BaseUrl and html.listingPath.");
    }

    internal static bool TryNormalizeUrl(Uri baseUri, string candidate, out string absolute)
    {
        absolute = string.Empty;

        if (!Uri.TryCreate(baseUri, candidate, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        absolute = uri.AbsoluteUri;
        return true;
    }

    internal static bool IsAllowedRequest(Uri baseUri, Uri listingRootUri, Uri candidate)
    {
        if (!string.Equals(baseUri.Host, candidate.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (candidate.AbsolutePath.Contains("/api/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var allowedPath = listingRootUri.AbsolutePath.TrimEnd('/');
        if (string.IsNullOrEmpty(allowedPath))
        {
            allowedPath = "/";
        }

        return string.Equals(candidate.AbsolutePath, allowedPath, StringComparison.OrdinalIgnoreCase)
            || candidate.AbsolutePath.StartsWith($"{allowedPath}/", StringComparison.OrdinalIgnoreCase);
    }

    internal static async Task EnforceDelayAsync(DateTime lastRequestUtc, int crawlDelaySeconds, CancellationToken ct)
    {
        if (lastRequestUtc == DateTime.MinValue)
        {
            return;
        }

        var elapsed = DateTime.UtcNow - lastRequestUtc;
        var required = TimeSpan.FromSeconds(crawlDelaySeconds);
        if (elapsed >= required)
        {
            return;
        }

        await Task.Delay(required - elapsed, ct).ConfigureAwait(false);
    }

    private void ApplyCustomHeaders(
        HttpRequestMessage request,
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig)
    {
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
                    "CivicInfoHtml provider {SourceName}: failed to apply custom header {HeaderName}.",
                    source.Name, headerName);
            }
        }
    }

    private static IReadOnlyList<string> GetSelectors(
        IReadOnlyDictionary<string, string> sourceConfig,
        string key,
        IReadOnlyList<string> defaults)
    {
        if (!sourceConfig.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return defaults;
        }

        var selectors = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        return selectors.Length == 0 ? defaults : selectors;
    }

    private static string? GetConfig(IReadOnlyDictionary<string, string> sourceConfig, string key)
    {
        if (!sourceConfig.TryGetValue(key, out var value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static int GetIntConfig(IReadOnlyDictionary<string, string> sourceConfig, string key, int defaultValue)
    {
        return sourceConfig.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static string? TrimTo(string? value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLen ? trimmed : trimmed[..maxLen];
    }
}
