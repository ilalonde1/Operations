#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Ingestion.Providers;

/// <summary>
/// BC Bid public Opportunities tab scraper. Page is Ivalua/.aspx with
/// server-side rendered table rows tagged data-object-type="rfp".
///
/// v1: GET + parse the listing only. If POST/ViewState handling is needed
/// for filtered views, add in v2 after observing actual behaviour.
/// </summary>
public sealed class BcBidOpportunityProvider : IOpportunityProvider
{
    private readonly HttpClient _httpClient;
    private readonly HtmlParser _parser;
    private readonly ILogger<BcBidOpportunityProvider> _logger;

    public BcBidOpportunityProvider(HttpClient httpClient, ILogger<BcBidOpportunityProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _parser = new HtmlParser();
    }

    public OpportunitySourceType SourceType => OpportunitySourceType.BcBid;

    public async Task<IReadOnlyList<OpportunityCandidate>> FetchAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(20, source.RequestTimeoutSeconds));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var token = timeoutCts.Token;

        using var request = new HttpRequestMessage(HttpMethod.Get, source.BaseUrl);
        request.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        request.Headers.TryAddWithoutValidation("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-CA,en;q=0.9");

        // Apply any operator-configured http.header.* overrides on top of these defaults.
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

            request.Headers.Remove(headerName);
            request.Headers.TryAddWithoutValidation(headerName, headerValue);
        }

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "BC Bid {SourceName}: GET {Url} returned {StatusCode}.",
            source.Name, source.BaseUrl, (int)response.StatusCode);

        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        var document = await _parser.ParseDocumentAsync(html, token).ConfigureAwait(false);

        var rows = document.QuerySelectorAll("tr[data-object-type='rfp']");
        if (rows.Length == 0)
        {
            _logger.LogWarning(
                "BC Bid {SourceName}: 0 rows found in response (length={Length} bytes). " +
                "Likely a POST/ViewState handshake is required; v2 will add that.",
                source.Name, html.Length);
            return Array.Empty<OpportunityCandidate>();
        }

        var baseUri = new Uri(source.BaseUrl);
        var candidates = new List<OpportunityCandidate>(rows.Length);
        var dropped = 0;
        foreach (var row in rows)
        {
            var candidate = MapRow(row, baseUri);
            if (candidate is null)
            {
                dropped++;
                continue;
            }

            candidates.Add(candidate);
        }

        _logger.LogInformation(
            "BC Bid {SourceName}: parsed {Count} candidate(s) from {Total} row(s); {Dropped} dropped.",
            source.Name, candidates.Count, rows.Length, dropped);

        return candidates;
    }

    private static OpportunityCandidate? MapRow(IElement row, Uri baseUri)
    {
        var cells = row.QuerySelectorAll(":scope > td");
        if (cells.Length < 7)
        {
            return null;
        }

        var status = cells[0].TextContent.Trim();
        if (!string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase))
        {
            // Defensive: listing page is already filtered Status=Open, but we
            // skip anything else so closed/awarded don't pollute the grid.
            return null;
        }

        // External reference = the row's data-id attribute (the BC Bid Opportunity ID).
        var externalRef = row.GetAttribute("data-id")?.Trim();
        if (string.IsNullOrWhiteSpace(externalRef))
        {
            // Fall back to the anchor text in the ID cell.
            externalRef = cells[1].QuerySelector("a")?.TextContent?.Trim();
        }

        // Title = Description column (cells[2]).
        var title = cells[2].TextContent.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        // URL = first anchor in the row (which is the ID-cell link),
        // resolved against the base.
        var anchor = row.QuerySelector("a[href]") as IHtmlAnchorElement;
        var href = anchor?.GetAttribute("href")?.Trim();
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var url = new Uri(baseUri, href).AbsoluteUri;

        // Buyer = Organization (Issued by): cells[10] in the current layout
        // (1-indexed col 11). If column count is shorter, accept null buyer.
        var buyer = cells.Length > 10 ? cells[10].TextContent.Trim() : null;
        if (string.IsNullOrWhiteSpace(buyer))
        {
            buyer = "Unknown";
        }

        // Description = commodities list joined with " / " for downstream scoring.
        var commoditiesNode = cells[3];
        var commodities = string.Join(" / ", EnumerateLi(commoditiesNode));
        var solicitationType = cells.Length > 4 ? cells[4].TextContent.Trim() : null;
        var description = string.IsNullOrWhiteSpace(commodities)
            ? solicitationType
            : (string.IsNullOrWhiteSpace(solicitationType) ? commodities : $"{solicitationType}: {commodities}");

        // Issue Date (cells[5]) and Closing Date (cells[6]).
        var posted = ParseBcBidDate(cells[5].TextContent.Trim());
        var deadline = ParseBcBidDate(cells[6].TextContent.Trim());

        return new OpportunityCandidate
        {
            Title = title,
            Buyer = buyer,
            Url = url,
            Description = description,
            PostedDateUtc = posted,
            SubmissionDeadlineUtc = deadline,
            ExternalReference = externalRef,
            // BC Bid is BC-only; the scoring engine's region weights handle the rest.
            ProjectProvince = "BC",
            ProjectCity = null,
            Location = "BC",
            RawJson = row.OuterHtml,
        };
    }

    private static IEnumerable<string> EnumerateLi(IElement parent)
    {
        foreach (var li in parent.QuerySelectorAll("li"))
        {
            var text = li.TextContent.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return text;
            }
        }
    }

    /// <summary>BC Bid renders dates as "2026-05-20 3:17:39 PM" Pacific Time.</summary>
    private static DateTimeOffset? ParseBcBidDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTime.TryParseExact(
                raw,
                "yyyy-MM-dd h:mm:ss tt",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var localPacific))
        {
            var pacific = GetPacificTimeZone();
            return new DateTimeOffset(
                DateTime.SpecifyKind(localPacific, DateTimeKind.Unspecified),
                pacific.GetUtcOffset(localPacific)).ToUniversalTime();
        }

        if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static TimeZoneInfo GetPacificTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Vancouver");
        }
    }
}
