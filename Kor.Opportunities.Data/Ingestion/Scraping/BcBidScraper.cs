#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Ingestion.Scraping;

/// <summary>
/// Scrapes the BC Bid public Opportunities tab via Playwright. Page is
/// Ivalua-platform with Microsoft AJAX postbacks - HTTP scraping doesn't
/// work; we need a real browser.
///
/// Flow:
///   1. Navigate to source.BaseUrl
///   2. Wait for tr[data-object-type='rfp'] rows to render
///   3. Extract rows on page 1
///   4. Click pagination "next" until disabled or maxPages reached
///   5. Return aggregated candidates
/// </summary>
public sealed class BcBidScraper : PlaywrightScraperBase
{
    private const int DefaultMaxPages = 10;       // 15 rows/page  10 = 150 max
    private const int PageWaitTimeoutMs = 30_000;

    public BcBidScraper(PlaywrightBrowserPool pool, ILogger<BcBidScraper> logger)
        : base(pool, logger)
    {
    }

    public override OpportunitySourceType SourceType => OpportunitySourceType.BcBid;

    protected override async Task<IReadOnlyList<OpportunityCandidate>> ScrapeAsync(
        IPage page,
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        var candidates = new List<OpportunityCandidate>();
        var maxPages = ResolveInt(sourceConfig, "playwright.maxPages", DefaultMaxPages);

        await page.GotoAsync(source.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = PageWaitTimeoutMs,
        }).ConfigureAwait(false);

        // BC Bid loads with the Status filter defaulted to "Open" but the
        // results grid stays empty until the user clicks Search. Click it
        // programmatically; matched by visible label "Search" inside the
        // main filter form (excludes any global header search).
        try
        {
            var searchButton = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions
            {
                Name = "Search",
                Exact = true,
            });
            await searchButton.First.ClickAsync(new LocatorClickOptions
            {
                Timeout = PageWaitTimeoutMs,
            }).ConfigureAwait(false);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
            {
                Timeout = PageWaitTimeoutMs,
            }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Search button not found or click timed out — fall through to
            // the row-wait below; some loads might pre-populate.
        }

        // Wait for the row container to render (Ivalua sometimes streams content
        // after the initial document is ready).
        try
        {
            await page.WaitForSelectorAsync("tr[data-object-type='rfp']", new PageWaitForSelectorOptions
            {
                Timeout = PageWaitTimeoutMs,
                State = WaitForSelectorState.Attached,
            }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // No rows ever appeared. Take a diagnostic screenshot + dump
            // the URL so we can see what the browser actually rendered.
            try
            {
                var diagDir = System.IO.Path.Combine(
                    Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
                    "KorOperations", "Opportunities", "diagnostics");
                System.IO.Directory.CreateDirectory(diagDir);
                var screenshotPath = System.IO.Path.Combine(diagDir, $"BcBid-norows-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true }).ConfigureAwait(false);
                var htmlPath = System.IO.Path.Combine(diagDir, $"BcBid-norows-{DateTime.UtcNow:yyyyMMdd-HHmmss}.html");
                await System.IO.File.WriteAllTextAsync(htmlPath, await page.ContentAsync().ConfigureAwait(false), ct).ConfigureAwait(false);
            }
            catch { }
            return Array.Empty<OpportunityCandidate>();
        }

        var baseUri = new Uri(source.BaseUrl);
        for (var pageNum = 1; pageNum <= maxPages; pageNum++)
        {
            var pageCandidates = await ExtractPageAsync(page, baseUri, ct).ConfigureAwait(false);
            candidates.AddRange(pageCandidates);

            if (!await TryAdvanceToNextPageAsync(page, ct).ConfigureAwait(false))
            {
                break;
            }
        }

        return candidates;
    }

    private static async Task<List<OpportunityCandidate>> ExtractPageAsync(
        IPage page, Uri baseUri, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Pull all RFP rows; for each, read the cell texts in DOM order.
        var rows = await page.QuerySelectorAllAsync("tr[data-object-type='rfp']").ConfigureAwait(false);
        var result = new List<OpportunityCandidate>(rows.Count);

        foreach (var row in rows)
        {
            var candidate = await TryMapRowAsync(row, baseUri).ConfigureAwait(false);
            if (candidate is not null) result.Add(candidate);
        }

        return result;
    }

    private static async Task<OpportunityCandidate?> TryMapRowAsync(IElementHandle row, Uri baseUri)
    {
        var cells = await row.QuerySelectorAllAsync(":scope > td").ConfigureAwait(false);
        if (cells.Count < 12) return null;

        var status = (await cells[0].InnerTextAsync().ConfigureAwait(false)).Trim();
        if (!string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var externalRef = (await row.GetAttributeAsync("data-id").ConfigureAwait(false))?.Trim();
        if (string.IsNullOrWhiteSpace(externalRef))
        {
            var idAnchor = await cells[1].QuerySelectorAsync("a").ConfigureAwait(false);
            if (idAnchor is not null)
            {
                externalRef = (await idAnchor.InnerTextAsync().ConfigureAwait(false))?.Trim();
            }
        }

        var title = (await cells[2].InnerTextAsync().ConfigureAwait(false)).Trim();
        if (string.IsNullOrWhiteSpace(title)) return null;

        var anchor = await row.QuerySelectorAsync("a[href]").ConfigureAwait(false);
        var href = anchor is null ? null : (await anchor.GetAttributeAsync("href").ConfigureAwait(false))?.Trim();
        if (string.IsNullOrWhiteSpace(href)) return null;
        var url = new Uri(baseUri, href).AbsoluteUri;

        var commoditiesText = (await cells[3].InnerTextAsync().ConfigureAwait(false))
            .Replace("\r", " ").Replace("\n", " / ").Trim();
        var solicitationType = (await cells[4].InnerTextAsync().ConfigureAwait(false)).Trim();

        var description = string.IsNullOrWhiteSpace(commoditiesText)
            ? solicitationType
            : (string.IsNullOrWhiteSpace(solicitationType)
                ? commoditiesText
                : $"{solicitationType}: {commoditiesText}");

        var posted = ParseBcBidDate((await cells[5].InnerTextAsync().ConfigureAwait(false)).Trim());
        var deadline = ParseBcBidDate((await cells[6].InnerTextAsync().ConfigureAwait(false)).Trim());

        var buyer = (await cells[10].InnerTextAsync().ConfigureAwait(false)).Trim();
        if (string.IsNullOrWhiteSpace(buyer)) buyer = "Unknown";

        return new OpportunityCandidate
        {
            Title = title,
            Buyer = buyer,
            Url = url,
            Description = description,
            PostedDateUtc = posted,
            SubmissionDeadlineUtc = deadline,
            ExternalReference = externalRef,
            ProjectProvince = "BC",
            Location = "BC",
            RawJson = null,
        };
    }

    private static async Task<bool> TryAdvanceToNextPageAsync(IPage page, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // BC Bid's Ivalua grid uses an `a` button labeled "Next page" or
        // similar; selector falls back to common patterns. If none match,
        // assume single page.
        var nextSelectors = new[]
        {
            "a[aria-label='Next page']:not([aria-disabled='true'])",
            "button[aria-label='Next page']:not([disabled])",
            "a.iv-pagination-next:not(.disabled)",
            "li.iv-pagination-next:not(.disabled) > a",
        };

        foreach (var selector in nextSelectors)
        {
            var handle = await page.QuerySelectorAsync(selector).ConfigureAwait(false);
            if (handle is null) continue;

            try
            {
                await handle.ClickAsync().ConfigureAwait(false);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
                {
                    Timeout = PageWaitTimeoutMs,
                }).ConfigureAwait(false);
                return true;
            }
            catch
            {
                continue;
            }
        }

        return false;
    }

    private static DateTimeOffset? ParseBcBidDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTimeOffset.TryParse(
            raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed) ? parsed : null;
    }

    private static int ResolveInt(IReadOnlyDictionary<string, string> config, string key, int defaultValue)
        => config.TryGetValue(key, out var s) && int.TryParse(s, out var v) ? v : defaultValue;
}
