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
/// Anonymous Playwright scraper for bidsandtenders.ca tenant opportunity lists.
/// Each source points at one tenant and supplies that tenant's buyer name via
/// the bidsandtenders.buyer mapping.
/// </summary>
/// <remarks>
/// The Bids&amp;Tenders platform renders its list as a Fuel UX "repeater" table.
/// Each opportunity is TWO sibling &lt;tr&gt; rows under &lt;tbody&gt;:
///   1. Data row — first &lt;td&gt; wraps "REF - TITLE" in &lt;strong&gt;, then
///      Status / Closing Date / Days Left cells.
///   2. Action row — single &lt;td colspan="4"&gt; containing
///      &lt;a href="/Module/Tenders/en/Tender/Detail/&lt;GUID&gt;"&gt;Bid Details&lt;/a&gt; plus
///      Register / Submit a Question / Download Documents / Plan Takers links.
/// We walk &lt;tbody&gt; rows in document order, pair data rows with the next
/// sibling, and emit one OpportunityCandidate per pair.
/// </remarks>
public sealed class BidsAndTendersScraper : PlaywrightScraperBase<OpportunityCandidate>, IOpportunityProvider
{
    private const int DefaultMaxPages = 5;
    private const int PageWaitTimeoutMs = 30_000;
    private const int RowWaitTimeoutMs = 15_000;

    // Wait for any <tbody> row to render — that covers both populated tenants
    // (rows with bid <strong> cells) and tenants with currently zero opportunities
    // (single <tr class="empty"> placeholder). We disambiguate after the wait
    // so empty portals don't trigger noisy "no-rows" diagnostics.
    private const string AnyBodyRowSelector = "tbody tr";

    private static readonly Regex TimezoneSuffixRegex =
        new(@"\s*\([A-Z]{2,5}\)\s*$", RegexOptions.Compiled);

    private readonly ILogger<BidsAndTendersScraper> _logger;

    public BidsAndTendersScraper(
        PlaywrightBrowserPool pool,
        ILogger<BidsAndTendersScraper> logger)
        : base(pool, logger)
    {
        _logger = logger;
    }

    public override OpportunitySourceType SourceType => OpportunitySourceType.BidsAndTenders;

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

        if (!await WaitForRowsAsync(page).ConfigureAwait(false))
        {
            await TryWriteNoRowsDiagnosticAsync(page, ct).ConfigureAwait(false);
            return Array.Empty<OpportunityCandidate>();
        }

        // Tenants with zero open opportunities render a single
        // <tr class="empty"><td colspan="4">There are currently no open bid opportunities available.</td></tr>
        // placeholder. Treat that as a legitimate empty result, NOT a scraper failure.
        if (await IsEmptyPlaceholderAsync(page).ConfigureAwait(false))
        {
            return Array.Empty<OpportunityCandidate>();
        }

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

    private static async Task<bool> IsEmptyPlaceholderAsync(IPage page)
    {
        var placeholder = await page.QuerySelectorAsync("tbody tr.empty").ConfigureAwait(false);
        return placeholder is not null;
    }

    private string ResolveBuyer(OpportunitySource source, IReadOnlyDictionary<string, string> sourceConfig)
    {
        if (sourceConfig.TryGetValue("bidsandtenders.buyer", out var configuredBuyer)
            && !string.IsNullOrWhiteSpace(configuredBuyer))
        {
            return configuredBuyer.Trim();
        }

        _logger.LogWarning(
            "BidsAndTenders source {SourceName} has no bidsandtenders.buyer mapping; using source name as buyer.",
            source.Name);
        return source.Name;
    }

    private static async Task<bool> WaitForRowsAsync(IPage page)
    {
        try
        {
            await page.WaitForSelectorAsync(AnyBodyRowSelector, new PageWaitForSelectorOptions
            {
                Timeout = RowWaitTimeoutMs,
                State = WaitForSelectorState.Attached,
            }).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static async Task<List<OpportunityCandidate>> ExtractPageAsync(
        IPage page,
        Uri baseUri,
        string buyer,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Walk every <tbody><tr> in document order. Data rows have a <strong>
        // descendant; the very next <tr> is its action row.
        var rows = await page.QuerySelectorAllAsync("tbody > tr").ConfigureAwait(false);
        var result = new List<OpportunityCandidate>();

        for (var i = 0; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var strong = await rows[i].QuerySelectorAsync("td strong").ConfigureAwait(false);
            if (strong is null)
            {
                continue;
            }

            var bidName = NormalizeText(await strong.InnerTextAsync().ConfigureAwait(false));
            if (string.IsNullOrWhiteSpace(bidName))
            {
                continue;
            }

            // Status and closing-date live in the 2nd and 3rd <td> of the data row.
            var cells = await rows[i].QuerySelectorAllAsync(":scope > td").ConfigureAwait(false);
            string? statusText = null;
            string? closingText = null;
            if (cells.Count > 1) statusText = NormalizeText(await cells[1].InnerTextAsync().ConfigureAwait(false));
            if (cells.Count > 2) closingText = NormalizeText(await cells[2].InnerTextAsync().ConfigureAwait(false));

            // Action row is the next sibling <tr>. Pull the Detail URL from it.
            string? url = null;
            if (i + 1 < rows.Count)
            {
                var detailLink = await rows[i + 1].QuerySelectorAsync("a[href*='/Tender/Detail/']").ConfigureAwait(false);
                if (detailLink is not null)
                {
                    var href = (await detailLink.GetAttributeAsync("href").ConfigureAwait(false))?.Trim();
                    if (!string.IsNullOrWhiteSpace(href) && Uri.TryCreate(baseUri, href, out var absolute))
                    {
                        // Strip URL fragments — variants append /#Document, /#PlanTaker.
                        url = new UriBuilder(absolute) { Fragment = string.Empty }.Uri.AbsoluteUri;
                    }
                }
                i++; // consume action row
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var (extRef, title) = ParseBidName(bidName);

            result.Add(new OpportunityCandidate
            {
                Title = title,
                Buyer = buyer,
                Url = url,
                Description = null,
                PostedDateUtc = null,
                SubmissionDeadlineUtc = ParseClosingDate(closingText),
                ExternalReference = extRef,
                RawJson = string.Join("|", bidName, statusText ?? string.Empty, closingText ?? string.Empty),
            });
        }

        return result;
    }

    /// <summary>
    /// Splits "REF - Title" on the first " - " separator. Bid-name examples from
    /// production tenants:
    ///   "105-04-26 - Supply, Laundering and Management of Coveralls..."
    ///   "NOI#118-05-26 - Simeio IDaaS Technical Support Renewal"
    ///   "RFP #146-09-25 - Synthetic Turf BLSCW Field #3 Design"
    /// If no separator is present, the whole string is returned as the title.
    /// </summary>
    private static (string? ExternalReference, string Title) ParseBidName(string bidName)
    {
        var sepIdx = bidName.IndexOf(" - ", StringComparison.Ordinal);
        if (sepIdx > 0 && sepIdx + 3 < bidName.Length)
        {
            var refText = bidName[..sepIdx].Trim();
            var titleText = bidName[(sepIdx + 3)..].Trim();
            return (string.IsNullOrWhiteSpace(refText) ? null : refText, titleText);
        }

        return (null, bidName);
    }

    private static DateTimeOffset? ParseClosingDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Examples: "Thu May 21, 2026 3:00 PM (PDT)" or "Fri May 22, 2026 3:00 PM (PST)"
        // DateTimeOffset.Parse handles the named TZ suffix poorly on some hosts —
        // strip it and assume universal/local conversion downstream.
        var clean = TimezoneSuffixRegex.Replace(text, string.Empty).Trim();
        return DateTimeOffset.TryParse(
            clean,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static async Task<bool> TryAdvanceToNextPageAsync(IPage page, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var nextLocators = new[]
        {
            page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Next", Exact = true }),
            page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Next", Exact = true }),
            page.Locator("a.next-page, button.next, li.next a"),
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

                await next.ClickAsync(new LocatorClickOptions { Timeout = RowWaitTimeoutMs }).ConfigureAwait(false);
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
                Path = Path.Combine(diagnosticsDir, $"BidsAndTenders-norows-{stamp}.png"),
                FullPage = true,
            }).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(diagnosticsDir, $"BidsAndTenders-norows-{stamp}.url.txt"),
                page.Url,
                ct).ConfigureAwait(false);
            var html = await page.ContentAsync().ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(diagnosticsDir, $"BidsAndTenders-norows-{stamp}.html"),
                html,
                ct).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort diagnostic only.
        }
    }
}
