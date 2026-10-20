#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

    // ⚠ RESOLVED DEFENSIVELY, AND NOT IN A STATIC INITIALISER THAT CAN THROW.
    //   FindSystemTimeZoneById raises TimeZoneNotFoundException when the id is
    //   unknown to the host. In a `static readonly` field that becomes a
    //   TypeInitializationException on first touch, which kills the whole scraper
    //   — not just the closing date — on a machine whose ICU data differs from the
    //   dev box. The IANA id works on .NET 8 with ICU; the Windows id is the
    //   fallback; null means "no deadline captured", which is already a handled,
    //   diagnosed outcome rather than a dead source.
    private static readonly TimeZoneInfo? AlbertaTimeZone = ResolveAlbertaTimeZone();

    private static TimeZoneInfo? ResolveAlbertaTimeZone()
    {
        foreach (var id in new[] { "America/Edmonton", "Mountain Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return null;
    }

    private readonly ILogger<AlbertaPurchasingScraper> _logger;

    public AlbertaPurchasingScraper(
        PlaywrightBrowserPool pool,
        ILogger<AlbertaPurchasingScraper> logger)
        : base(pool, logger)
    {
        _logger = logger;
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
        var truncated = false;
        for (var pageNum = 1; pageNum <= maxPages; pageNum++)
        {
            ct.ThrowIfCancellationRequested();

            var pageCandidates = await ExtractPageAsync(page, baseUri, buyer, ct).ConfigureAwait(false);
            candidates.AddRange(pageCandidates);

            if (!await TryAdvanceToNextPageAsync(page, ct).ConfigureAwait(false))
            {
                break;
            }

            // Advancing on the final allowed page means the portal had more.
            if (pageNum == maxPages)
            {
                truncated = true;
            }
        }

        if (truncated)
        {
            _logger.LogWarning(
                "APC pagination TRUNCATED at {MaxPages} page(s) for {Source} — the portal offered another page; raise playwright.maxPages in the source config.",
                maxPages, source.Name);
            IngestionRunDiagnostics.AddWarning(
                $"pagination truncated at {maxPages} page(s) — portal offered more; raise playwright.maxPages");
        }

        if (candidates.Count == 0)
        {
            await TryWriteNoRowsDiagnosticAsync(page, ct).ConfigureAwait(false);
        }

        var missingDeadlines = candidates.FindAll(c => c.SubmissionDeadlineUtc is null);
        if (missingDeadlines.Count > 0)
        {
            foreach (var candidate in missingDeadlines)
            {
                _logger.LogWarning("APC closing date missing for {Reference}: {Url}",
                    candidate.ExternalReference, candidate.Url);
            }

            // One bounded warning survives ErrorSummary's 2,000-character limit;
            // the log above identifies every affected posting without dropping rows.
            IngestionRunDiagnostics.AddWarning(
                $"APC closing date missing for {missingDeadlines.Count}/{candidates.Count} posting(s); " +
                $"sample references: {string.Join(", ", missingDeadlines.Take(5).Select(c => c.ExternalReference))}");
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
            // Each card names its contracting organisation and carries the posting's summary; the source-level buyer
            // ("Government of Alberta (APC)") is only the fallback when a card does not.
            var card = await link.QuerySelectorAsync("xpath=ancestor::apc-opportunity-search-result[1]").ConfigureAwait(false);
            // The organisation is the element whose class is exactly "fg-text"; the posting-type header above it also
            // carries fg-text among its classes, so ".fg-text" would read "Open & Competitive Opportunity".
            var cardBuyer = await CardTextAsync(card, "[class='fg-text']").ConfigureAwait(false);
            result.Add(new OpportunityCandidate
            {
                Title = linkText,
                Buyer = cardBuyer ?? buyer,
                Url = url,
                Description = await CardTextAsync(card, ".search-result__description").ConfigureAwait(false),
                PostedDateUtc = null,
                SubmissionDeadlineUtc = await FindDeadlineAsync(link).ConfigureAwait(false),
                ExternalReference = externalReference,
                RawJson = $"{linkText}|{href}",
                ProjectProvince = "AB",
            });
        }

        return result;
    }

    private static async Task<string?> CardTextAsync(IElementHandle? card, string selector)
    {
        if (card is null)
        {
            return null;
        }

        var element = await card.QuerySelectorAsync(selector).ConfigureAwait(false);
        if (element is null)
        {
            return null;
        }

        var text = NormalizeText(await element.InnerTextAsync().ConfigureAwait(false));
        return string.IsNullOrWhiteSpace(text) ? null : text;
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
        // Live APC markup (2026-10-08): cards are DIVs inside this component,
        // not tr/li/role=row. Scope to one posting: an outer search-result DIV
        // also wraps the entire list. Posting Date precedes Closing Date.
        var card = await link.QuerySelectorAsync("xpath=ancestor::apc-opportunity-search-result[1]")
            .ConfigureAwait(false);
        if (card is not null)
        {
            var closingValue = await card.QuerySelectorAsync(
                ".search-result__detail:has(.search-result__detail-header:has-text('Closing Date')) .search-result__detail-value")
                .ConfigureAwait(false);
            if (closingValue is null)
            {
                return null;
            }

            // Observed value: Oct 23, 2026 • 4:00:00 pm (MT).
            // MT follows Alberta's DST rules; do not treat the wall clock as UTC
            // or discard the time, and never fall back to the card's posted date.
            var closingText = NormalizeText(await closingValue.InnerTextAsync().ConfigureAwait(false));
            if (AlbertaTimeZone is not null
                && DateTime.TryParseExact(closingText, "MMM d, yyyy '•' h:mm:ss tt '(MT)'",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
                && !AlbertaTimeZone.IsInvalidTime(local)
                && !AlbertaTimeZone.IsAmbiguousTime(local))
            {
                return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, AlbertaTimeZone));
            }

            return null;
        }

        // Preserve the existing parser for legacy table/list markup.
        var container = await link
            .QuerySelectorAsync("xpath=ancestor::*[self::tr or self::li or @role='row'][1]")
            .ConfigureAwait(false);
        if (container is null)
        {
            return null;
        }

        // Legacy table/list markup. DateLikeRegex matches a date with no time, and
        // APC is an Alberta portal, so a bare date means END of that day in
        // Alberta -- it is a CLOSING date. Parsed with AssumeUniversal it became
        // midnight UTC, i.e. 18:00 the previous day locally, expiring the posting
        // a day before it closed.
        var text = NormalizeText(await container.InnerTextAsync().ConfigureAwait(false));
        foreach (Match match in DateLikeRegex.Matches(text))
        {
            var parsed = PublishedClock.ParseDeadline(match.Value, "America/Edmonton");
            if (parsed is not null)
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
