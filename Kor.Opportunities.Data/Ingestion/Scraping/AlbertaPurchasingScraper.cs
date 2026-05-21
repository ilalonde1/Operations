#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Ingestion.Scraping;

/// <summary>
/// Anonymous Playwright scraper for Alberta Purchasing Connection opportunity
/// links. APC renders postings in a JavaScript SPA; list links carry the stable
/// AB posting reference used downstream for idempotent ingestion.
/// </summary>
public sealed class AlbertaPurchasingScraper : PlaywrightScraperBase<OpportunityCandidate>, IOpportunityProvider
{
    private const int DefaultMaxPages = 5;
    private const int PageWaitTimeoutMs = 30_000;
    private const int PostingWaitTimeoutMs = 15_000;

    private static readonly Regex PostingReferenceRegex = new(
        @"/posting/(AB-\d{4}-\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DateLikeRegex = new(
        @"\b(?:\d{4}-\d{1,2}-\d{1,2}|[A-Z][a-z]{2,8}\s+\d{1,2},?\s+\d{4}|\d{1,2}/\d{1,2}/\d{4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public AlbertaPurchasingScraper(
        PlaywrightBrowserPool pool,
        ILogger<AlbertaPurchasingScraper> logger)
        : base(pool, logger)
    {
    }

    public override OpportunitySourceType SourceType => OpportunitySourceType.AlbertaPurchasingConnection;

    protected override async Task<IReadOnlyList<OpportunityCandidate>> ScrapeAsync(
        IPage page,
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        var buyer = ResolveBuyer(source, sourceConfig);
        var candidates = new List<OpportunityCandidate>();
        var maxPages = ResolveInt(sourceConfig, "playwright.maxPages", DefaultMaxPages);

        await page.GotoAsync(source.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = PageWaitTimeoutMs,
        }).ConfigureAwait(false);

        if (!await WaitForPostingsAsync(page).ConfigureAwait(false))
        {
            await TryWriteNoRowsDiagnosticAsync(page, ct).ConfigureAwait(false);
            return Array.Empty<OpportunityCandidate>();
        }

        // APC paginates by 10 per page by default with internal AJAX state
        // (not URL-driven). Bump the page-size <select> to its max so we get
        // the full set in a single render. The dropdown lives at
        // <select class="page-size-options"> and offers values 5/10/25/50/100.
        // Validated 2026-05-21: full Alberta open set was 100 postings, all
        // ingested in one scrape (was 10/run before the page-size bump).
        await TrySelectMaxPageSizeAsync(page).ConfigureAwait(false);

        var baseUri = new Uri(source.BaseUrl);
        for (var pageNum = 1; pageNum <= maxPages; pageNum++)
        {
            ct.ThrowIfCancellationRequested();

            var pageCandidates = await ExtractPageAsync(page, baseUri, buyer, ct).ConfigureAwait(false);
            candidates.AddRange(pageCandidates);

            if (!await TryAdvanceToNextPageAsync(page, ct).ConfigureAwait(false))
            {
                break;
            }
        }

        if (candidates.Count == 0)
        {
            await TryWriteNoRowsDiagnosticAsync(page, ct).ConfigureAwait(false);
        }

        return candidates;
    }

    private static string ResolveBuyer(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig)
    {
        return sourceConfig.TryGetValue("apc.buyer", out var configuredBuyer)
            && !string.IsNullOrWhiteSpace(configuredBuyer)
            ? configuredBuyer.Trim()
            : source.Name;
    }

    private static async Task TrySelectMaxPageSizeAsync(IPage page)
    {
        try
        {
            var select = page.Locator("select.page-size-options").First;
            if (await select.CountAsync().ConfigureAwait(false) == 0)
            {
                return;
            }

            // Available values per current markup: 5 / 10 / 25 / 50 / 100.
            // Pick the largest available so the full result set lands in one render.
            await select.SelectOptionAsync(new[] { "100" }, new LocatorSelectOptionOptions
            {
                Timeout = PostingWaitTimeoutMs,
            }).ConfigureAwait(false);

            // Angular re-renders the list after the change. Wait briefly for
            // the AJAX to settle.
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
            {
                Timeout = PostingWaitTimeoutMs,
            }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Best-effort — falls back to the default page size if anything fails.
        }
        catch (PlaywrightException)
        {
            // Selector or option not present in some tenant variants. Same fallback.
        }
    }

    private static async Task<bool> WaitForPostingsAsync(IPage page)
    {
        var selectors = new[]
        {
            "a[href^='/posting/AB-']",
            "a[href*='/posting/AB-']",
            "table tbody tr",
        };

        foreach (var selector in selectors)
        {
            try
            {
                await page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions
                {
                    Timeout = PostingWaitTimeoutMs,
                    State = WaitForSelectorState.Attached,
                }).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException)
            {
                // APC markup can vary between link cards and table rows.
            }
        }

        return false;
    }

    private static async Task<List<OpportunityCandidate>> ExtractPageAsync(
        IPage page,
        Uri baseUri,
        string buyer,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var links = await page.QuerySelectorAllAsync("a[href*='/posting/AB-']").ConfigureAwait(false);
        var seenReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<OpportunityCandidate>(links.Count);

        foreach (var link in links)
        {
            ct.ThrowIfCancellationRequested();

            var href = (await link.GetAttributeAsync("href").ConfigureAwait(false))?.Trim();
            var externalReference = ParseExternalReference(href);
            var url = TryResolveUrl(baseUri, href);
            if (string.IsNullOrWhiteSpace(externalReference)
                || string.IsNullOrWhiteSpace(url)
                || !seenReferences.Add(externalReference))
            {
                continue;
            }

            var linkText = NormalizeText(await link.InnerTextAsync().ConfigureAwait(false));
            result.Add(new OpportunityCandidate
            {
                Title = linkText,
                Buyer = buyer,
                Url = url,
                Description = null,
                PostedDateUtc = null,
                SubmissionDeadlineUtc = await FindDeadlineAsync(link).ConfigureAwait(false),
                ExternalReference = externalReference,
                RawJson = $"{linkText}|{href}",
            });
        }

        return result;
    }

    private static string? ParseExternalReference(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var match = PostingReferenceRegex.Match(href);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? TryResolveUrl(Uri baseUri, string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        return Uri.TryCreate(baseUri, href, out var uri) ? uri.AbsoluteUri : null;
    }

    private static async Task<DateTimeOffset?> FindDeadlineAsync(IElementHandle link)
    {
        var container = await link
            .QuerySelectorAsync("xpath=ancestor::*[self::tr or self::li or @role='row'][1]")
            .ConfigureAwait(false);
        if (container is null)
        {
            return null;
        }

        var text = NormalizeText(await container.InnerTextAsync().ConfigureAwait(false));
        foreach (Match match in DateLikeRegex.Matches(text))
        {
            if (DateTimeOffset.TryParse(
                match.Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static async Task<bool> TryAdvanceToNextPageAsync(IPage page, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var nextLocators = new[]
        {
            page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Next", Exact = true }),
            page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Next", Exact = true }),
            page.Locator("a.next-page, button.next, li.next a, [aria-label*='Next' i]"),
        };

        foreach (var locator in nextLocators)
        {
            try
            {
                if (await locator.CountAsync().ConfigureAwait(false) == 0)
                {
                    continue;
                }

                var next = locator.First;
                if (await IsDisabledAsync(next).ConfigureAwait(false))
                {
                    continue;
                }

                await next.ClickAsync(new LocatorClickOptions
                {
                    Timeout = PostingWaitTimeoutMs,
                }).ConfigureAwait(false);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
                {
                    Timeout = PageWaitTimeoutMs,
                }).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException)
            {
                continue;
            }
        }

        return false;
    }

    private static async Task<bool> IsDisabledAsync(ILocator locator)
    {
        if (await locator.IsDisabledAsync().ConfigureAwait(false))
        {
            return true;
        }

        var ariaDisabled = await locator.GetAttributeAsync("aria-disabled").ConfigureAwait(false);
        if (string.Equals(ariaDisabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var className = await locator.GetAttributeAsync("class").ConfigureAwait(false);
        return className?.Contains("disabled", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string NormalizeText(string text) =>
        Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();

    private static int ResolveInt(IReadOnlyDictionary<string, string> config, string key, int defaultValue)
        => config.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;

    private static async Task TryWriteNoRowsDiagnosticAsync(IPage page, CancellationToken ct)
    {
        try
        {
            var diagnosticsDir = Path.Combine(
                Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
                "KorOperations", "Opportunities", "diagnostics");
            Directory.CreateDirectory(diagnosticsDir);

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(diagnosticsDir, $"APC-norows-{stamp}.png"),
                FullPage = true,
            }).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(diagnosticsDir, $"APC-norows-{stamp}.url.txt"),
                page.Url,
                ct).ConfigureAwait(false);
            var html = await page.ContentAsync().ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(diagnosticsDir, $"APC-norows-{stamp}.html"),
                html,
                ct).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort diagnostic only.
        }
    }
}
