#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Ingestion.Providers;

public sealed class RssOpportunityProvider : IOpportunityProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RssOpportunityProvider> _logger;

    public RssOpportunityProvider(HttpClient httpClient, ILogger<RssOpportunityProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public OpportunitySourceType SourceType => OpportunitySourceType.Rss;

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

            // User-Agent uses a typed setter; everything else goes through TryAddWithoutValidation
            // because some headers (e.g. Authorization with custom schemes) reject strict validation.
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
                    "RSS provider {SourceName}: failed to apply custom header {HeaderName}.",
                    source.Name, headerName);
            }
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
        var document = XDocument.Load(stream, LoadOptions.None);

        var candidates = ParseRssItems(document, source.BaseUrl, sourceConfig);
        var total = document.Descendants("item").Count();
        if (candidates.Count == 0)
        {
            candidates = ParseAtomEntries(document, source.BaseUrl, sourceConfig);
            XNamespace atom = "http://www.w3.org/2005/Atom";
            total = document.Descendants(atom + "entry").Count();
        }

        _logger.LogInformation(
            "RSS provider {SourceName}: {Kept} candidate(s) parsed from {Total} item(s).",
            source.Name,
            candidates.Count,
            total);

        return candidates;
    }

    internal static List<OpportunityCandidate> ParseRssItems(
        XDocument doc,
        string fallbackBaseUrl,
        IReadOnlyDictionary<string, string>? sourceConfig = null)
    {
        sourceConfig ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var titleElement = GetConfig(sourceConfig, "rss.titleElement") ?? "title";
        var linkElement = GetConfig(sourceConfig, "rss.linkElement") ?? "link";
        var descriptionElement = GetConfig(sourceConfig, "rss.descriptionElement") ?? "description";
        var dateElement = GetConfig(sourceConfig, "rss.dateElement") ?? "pubDate";
        var externalReferenceElement = GetConfig(sourceConfig, "rss.externalReferenceElement") ?? "guid";
        var buyerFromTitle = GetBoolConfig(sourceConfig, "rss.buyerFromTitle", defaultValue: true);

        var items = doc.Descendants("item");
        var candidates = new List<OpportunityCandidate>();

        foreach (var item in items)
        {
            var title = ReadElementValue(item, titleElement);
            var link = ReadElementValue(item, linkElement);
            var description = ReadElementValue(item, descriptionElement);
            var pubDate = ReadElementValue(item, dateElement);
            var externalReference = ReadElementValue(item, externalReferenceElement);

            if (string.IsNullOrWhiteSpace(title) || !TryNormalizeUrl(link, fallbackBaseUrl, out var url))
            {
                continue;
            }

            candidates.Add(new OpportunityCandidate
            {
                Title = title.Trim(),
                Buyer = buyerFromTitle ? ExtractBuyerFromTitle(title) : "Unknown",
                Url = url,
                Description = TrimTo(description, 1200),
                PostedDateUtc = ParseDate(pubDate),
                SubmissionDeadlineUtc = null,
                ExternalReference = externalReference,
                RawJson = item.ToString(SaveOptions.DisableFormatting),
            });
        }

        return candidates;
    }

    internal static List<OpportunityCandidate> ParseAtomEntries(
        XDocument doc,
        string fallbackBaseUrl,
        IReadOnlyDictionary<string, string>? sourceConfig = null)
    {
        sourceConfig ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var titleElement = GetConfig(sourceConfig, "rss.titleElement") ?? "title";
        var linkElement = GetConfig(sourceConfig, "rss.linkElement") ?? "link[href]";
        var descriptionElement = GetConfig(sourceConfig, "rss.descriptionElement") ?? "summary";
        var dateElement = GetConfig(sourceConfig, "rss.dateElement") ?? "published";
        var externalReferenceElement = GetConfig(sourceConfig, "rss.externalReferenceElement") ?? "id";
        var buyerFromTitle = GetBoolConfig(sourceConfig, "rss.buyerFromTitle", defaultValue: true);

        var entries = doc.Descendants(atom + "entry");
        var candidates = new List<OpportunityCandidate>();

        foreach (var entry in entries)
        {
            var title = ReadElementValue(entry, titleElement);
            var link = ReadElementValue(entry, linkElement);
            var summary = ReadElementValue(entry, descriptionElement) ?? ReadElementValue(entry, "content");
            var published = ReadElementValue(entry, dateElement) ?? ReadElementValue(entry, "updated");
            var externalReference = ReadElementValue(entry, externalReferenceElement);

            if (string.IsNullOrWhiteSpace(title) || !TryNormalizeUrl(link, fallbackBaseUrl, out var url))
            {
                continue;
            }

            candidates.Add(new OpportunityCandidate
            {
                Title = title.Trim(),
                Buyer = buyerFromTitle ? ExtractBuyerFromTitle(title) : "Unknown",
                Url = url,
                Description = TrimTo(summary, 1200),
                PostedDateUtc = ParseDate(published),
                SubmissionDeadlineUtc = null,
                ExternalReference = externalReference,
                RawJson = entry.ToString(SaveOptions.DisableFormatting),
            });
        }

        return candidates;
    }

    internal static bool TryNormalizeUrl(string? candidate, string fallbackBaseUrl, out string url)
    {
        url = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (Uri.TryCreate(candidate.Trim(), UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            url = absolute.AbsoluteUri;
            return true;
        }

        if (!Uri.TryCreate(fallbackBaseUrl, UriKind.Absolute, out var baseUri))
        {
            return false;
        }

        if (!Uri.TryCreate(baseUri, candidate.Trim(), out var combined) ||
            (combined.Scheme != Uri.UriSchemeHttp && combined.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        url = combined.AbsoluteUri;
        return true;
    }

    internal static string ExtractBuyerFromTitle(string title)
    {
        var separators = new[] { " at ", " @ ", " - ", " | " };
        foreach (var separator in separators)
        {
            var index = title.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
            if (index <= 0)
            {
                continue;
            }

            var right = title[(index + separator.Length)..].Trim();
            if (!string.IsNullOrWhiteSpace(right) && right.Length <= 120)
            {
                return right;
            }
        }

        return "Unknown";
    }

    private static string? ValueOrNull(XElement? value)
    {
        var text = value?.Value?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
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

    private static DateTimeOffset? ParseDate(string? value)
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

    private static string? ReadElementValue(XElement parent, string selector)
    {
        var trimmed = selector.Trim();
        var bracket = trimmed.IndexOf('[');
        if (bracket > 0 && trimmed.EndsWith("]", StringComparison.Ordinal))
        {
            var elementName = trimmed[..bracket];
            var attributeName = trimmed[(bracket + 1)..^1];
            return parent.Elements()
                .Where(e => string.Equals(e.Name.LocalName, elementName, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Attribute(attributeName)?.Value?.Trim())
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }

        return ValueOrNull(parent.Elements().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, trimmed, StringComparison.OrdinalIgnoreCase)));
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

    private static bool GetBoolConfig(IReadOnlyDictionary<string, string> sourceConfig, string key, bool defaultValue)
    {
        return sourceConfig.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;
    }
}
