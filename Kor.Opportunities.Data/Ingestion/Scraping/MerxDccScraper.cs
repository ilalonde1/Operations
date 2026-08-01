#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Ingestion.Scraping;

/// <summary>
/// Scrapes MERX's public Defence Construction Canada solicitations listing
/// (https://www.merx.com/dcc). DCC posts ALL its tenders on MERX; only the
/// trade-agreement subset mirrors to CanadaBuys, and defence construction
/// routinely invokes national-security exceptions — so without this source
/// the platform is structurally blind to DCC (the $184-232M CFHA RFP of
/// June 2026 never entered the system; probe + build 2026-07-03).
///
/// Plain HTTP fetches get 403 from MERX's WAF; a pooled Playwright browser
/// context passes (probe-verified 200). Listing metadata needs no login.
///
/// Selector map (probe-verified against saved DOM, 2026-07-03):
///   rows        tr.mets-table-row
///   number      .sol-num
///   title+url   .sol-title a.solicitation-link  (relative href)
///   regions     .sol-region .sol-region-item    (multiple)
///   published   .sol-publication-date .date-value   (yyyy/MM/dd)
///   closing     .sol-closing-date .date-value       (yyyy/MM/dd)
///   next page   .mets-page-navigation-next
/// </summary>
public sealed class MerxDccScraper : PlaywrightScraperBase<OpportunityCandidate>, IOpportunityProvider
{
    private const int DefaultMaxPages = 5;        // ~25 rows/page; DCC's open list is small
    private const int PageWaitTimeoutMs = 60_000;
    private const string RowSelector = "tr.mets-table-row";

    private readonly ILogger<MerxDccScraper> _logger;

    public MerxDccScraper(PlaywrightBrowserPool pool, ILogger<MerxDccScraper> logger)
        : base(pool, logger)
    {
        _logger = logger;
    }

    public override OpportunitySourceType SourceType => OpportunitySourceType.MerxDcc;

    protected override async Task<IReadOnlyList<OpportunityCandidate>> ScrapeAsync(
        IPage page,
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        var maxPages = sourceConfig.TryGetValue("merx.maxPages", out var mp)
            && int.TryParse(mp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? Math.Clamp(parsed, 1, 25)
                : DefaultMaxPages;

        await page.GotoAsync(source.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = PageWaitTimeoutMs,
        }).ConfigureAwait(false);

        await page.Locator(RowSelector).First.WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = PageWaitTimeoutMs,
        }).ConfigureAwait(false);

        var results = new List<OpportunityCandidate>();
        var baseUri = new Uri(source.BaseUrl);

        for (var pageNo = 1; pageNo <= maxPages; pageNo++)
        {
            ct.ThrowIfCancellationRequested();

            var rows = page.Locator(RowSelector);
            var rowCount = await rows.CountAsync().ConfigureAwait(false);
            _logger.LogInformation("MERX-DCC page {Page}: {Rows} row(s).", pageNo, rowCount);

            for (var i = 0; i < rowCount; i++)
            {
                var row = rows.Nth(i);
                var candidate = await ExtractRowAsync(row, baseUri, ct).ConfigureAwait(false);
                if (candidate is not null)
                {
                    results.Add(candidate);
                }
            }

            // Advance. The control exists but is disabled (or absent) on the last page.
            var next = page.Locator(".mets-page-navigation-next a, a.mets-page-navigation-next");
            if (await next.CountAsync().ConfigureAwait(false) == 0)
            {
                break;
            }
            var disabled = await next.First.GetAttributeAsync("class").ConfigureAwait(false);
            if (disabled?.Contains("disabled", StringComparison.OrdinalIgnoreCase) == true)
            {
                break;
            }
            if (pageNo == maxPages)
            {
                break;
            }

            await next.First.ClickAsync().ConfigureAwait(false);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
            {
                Timeout = PageWaitTimeoutMs,
            }).ConfigureAwait(false);
        }

        return results;
    }

    private async Task<OpportunityCandidate?> ExtractRowAsync(ILocator row, Uri baseUri, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var titleLink = row.Locator(".sol-title a.solicitation-link");
            if (await titleLink.CountAsync().ConfigureAwait(false) == 0)
            {
                return null; // header/spacer rows
            }

            var title = (await titleLink.First.InnerTextAsync().ConfigureAwait(false)).Trim();
            var href = await titleLink.First.GetAttributeAsync("href").ConfigureAwait(false) ?? "";
            var url = string.IsNullOrWhiteSpace(href) ? baseUri.ToString() : new Uri(baseUri, href).ToString();

            var number = await InnerTextOrNullAsync(row.Locator(".sol-num")).ConfigureAwait(false);
            var regions = new List<string>();
            var regionItems = row.Locator(".sol-region .sol-region-item");
            var regionCount = await regionItems.CountAsync().ConfigureAwait(false);
            for (var r = 0; r < regionCount; r++)
            {
                var region = (await regionItems.Nth(r).InnerTextAsync().ConfigureAwait(false)).Trim();
                if (region.Length > 0) regions.Add(region);
            }

            var published = ParseMerxDate(
                await InnerTextOrNullAsync(row.Locator(".sol-publication-date .date-value")).ConfigureAwait(false));
            var closing = ParseMerxDate(
                await InnerTextOrNullAsync(row.Locator(".sol-closing-date .date-value")).ConfigureAwait(false));

            var raw = JsonSerializer.Serialize(new
            {
                source = "merx-dcc",
                number,
                title,
                url,
                regions,
                published = published?.ToString("O"),
                closing = closing?.ToString("O"),
            });

            return new OpportunityCandidate
            {
                Title = title,
                Buyer = "Defence Construction Canada",
                Location = regions.Count > 0 ? string.Join("; ", regions) : null,
                Url = url,
                PostedDateUtc = published,
                SubmissionDeadlineUtc = closing,
                ExternalReference = string.IsNullOrWhiteSpace(number) ? null : number,
                RawJson = raw,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MERX-DCC row extraction failed; skipping row.");
            return null;
        }
    }

    private static async Task<string?> InnerTextOrNullAsync(ILocator locator)
    {
        if (await locator.CountAsync().ConfigureAwait(false) == 0) return null;
        var text = (await locator.First.InnerTextAsync().ConfigureAwait(false)).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>MERX renders dates as yyyy/MM/dd in Pacific-agnostic local form;
    /// treat as date-only UTC (closing hour lives on the detail page).</summary>
    private static DateTimeOffset? ParseMerxDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return DateTime.TryParseExact(text.Trim(), "yyyy/MM/dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? new DateTimeOffset(d, TimeSpan.Zero)
            : null;
    }
}
