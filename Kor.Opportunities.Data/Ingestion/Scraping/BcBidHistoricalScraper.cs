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
/// Scrapes BC Bid's historical Opportunities view for legacy records exposed
/// by the Historical Records filter on the authenticated opportunities page.
/// </summary>
public sealed class BcBidHistoricalScraper
    : PlaywrightScraperBase<OpportunityCandidate>, IOpportunityProvider
{
    private const int DefaultMaxPages = 50;
    private const int PageWaitTimeoutMs = 30_000;
    private const string LoginEntryUrl = "https://bcbid.gov.bc.ca/page.aspx/en/buy/homepage";

    private static readonly Regex TimezoneSuffixRegex = new(
        @"\s*\((?:PDT|PST)\)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly BcBidCredentials _credentials;

    public BcBidHistoricalScraper(
        PlaywrightBrowserPool pool,
        ILogger<BcBidHistoricalScraper> logger,
        BcBidCredentials credentials)
        : base(pool, logger)
    {
        _credentials = credentials;
    }

    public override OpportunitySourceType SourceType => OpportunitySourceType.BcBidHistorical;

    protected override async Task<IReadOnlyList<OpportunityCandidate>> ScrapeAsync(
        IPage page,
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        if (!_credentials.IsConfigured)
        {
            return Array.Empty<OpportunityCandidate>();
        }

        await LoginAsync(page, ct).ConfigureAwait(false);

        await page.GotoAsync(source.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = PageWaitTimeoutMs,
        }).ConfigureAwait(false);

        if (!await TrySelectHistoricalFilterAsync(page).ConfigureAwait(false))
        {
            await TryWriteDiagnosticAsync(page, "BcBidHistorical-nofilter", ct).ConfigureAwait(false);
            return Array.Empty<OpportunityCandidate>();
        }

        await TryFillIssueDateRangeAsync(page, sourceConfig).ConfigureAwait(false);
        await TryClickSearchAsync(page).ConfigureAwait(false);

        try
        {
            await page.WaitForSelectorAsync("tr[id*='_grd_tr_']",
                new PageWaitForSelectorOptions
                {
                    Timeout = PageWaitTimeoutMs,
                    State = WaitForSelectorState.Attached,
                }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await TryWriteDiagnosticAsync(page, "BcBidHistorical-norows", ct).ConfigureAwait(false);
            return Array.Empty<OpportunityCandidate>();
        }

        var buyer = ResolveBuyer(sourceConfig);
        var baseUri = new Uri(source.BaseUrl);
        var candidates = new List<OpportunityCandidate>();
        var maxPages = ResolveInt(sourceConfig, "playwright.maxPages", DefaultMaxPages);

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
            await TryWriteDiagnosticAsync(page, "BcBidHistorical-nocandidates", ct).ConfigureAwait(false);
        }

        return candidates;
    }

    private static string ResolveBuyer(IReadOnlyDictionary<string, string> sourceConfig)
    {
        return sourceConfig.TryGetValue("bcbid.buyer", out var buyer)
            && !string.IsNullOrWhiteSpace(buyer)
            ? buyer.Trim()
            : "Province of British Columbia (BC Bid Historical)";
    }

    private static async Task<bool> TrySelectHistoricalFilterAsync(IPage page)
    {
        // BC Bid runs on Ivalua/Semantic-UI: the "Opportunity Type on Historical
        // Records" filter is a CUSTOM <div class="ui dropdown selection ...">,
        // NOT a native <select>. The original GetByLabel / select[...] / combobox
        // locators all miss it (diagnostic 2026-05-21 confirmed). Actual markup:
        //   <label><span class="label-field">Opportunity Type on Historical Records<br>(Apr 1, 2015 - Dec 15, 2022)</span></label>
        //   <div data-iv-role="controlWrapper" class="control-wrapper">
        //     <div class="ui dropdown selection ...">  <-- click this to open
        //       <div class="menu">
        //         <div class="item" data-value="..."> option text </div>
        // Two-step interaction: click the wrapper, then click an item.
        try
        {
            var dropdownCandidates = new[]
            {
                page.Locator("label:has(span:has-text('Historical Records')) + div.control-wrapper div.ui.dropdown.selection").First,
                page.Locator("span:has-text('Historical Records')").Locator("xpath=ancestor::label/following-sibling::div//div[contains(@class,'ui') and contains(@class,'dropdown')]").First,
                page.Locator(":text('Historical Records')").Locator("xpath=ancestor::div[contains(@class,'field') or contains(@class,'wrapper')][1]//div[contains(@class,'dropdown')]").First,
            };

            ILocator? dropdown = null;
            foreach (var c in dropdownCandidates)
            {
                if (await c.CountAsync().ConfigureAwait(false) > 0) { dropdown = c; break; }
            }
            if (dropdown is null) return false;

            await dropdown.ScrollIntoViewIfNeededAsync().ConfigureAwait(false);
            await dropdown.ClickAsync(new LocatorClickOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);

            // Menu becomes visible after click. Items are <li>, NOT <div> (Ivalua/
            // Semantic-UI markup pattern verified 2026-05-21):
            //   <li data-iv-role="item" class="item" id="body_x_selNtypeCode_RFP"
            //       data-value="RFP" aria-selected="false" role="option">
            //     <span class="text">Invitation Tender (RFP)</span>
            //   </li>
            // Use [role='option'] as the most stable cross-platform selector.
            var menuItems = dropdown.Locator("[role='option']");
            try
            {
                await menuItems.First.WaitForAsync(new LocatorWaitForOptions
                {
                    Timeout = 5_000,
                    State = WaitForSelectorState.Visible,
                }).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return false;
            }

            // Pick the first non-empty option — we just need ANY filter applied
            // so the historical grid populates. Specific tender-type scoping is
            // a future config knob (bcbid.historicalType).
            var itemCount = await menuItems.CountAsync().ConfigureAwait(false);
            for (var i = 0; i < itemCount; i++)
            {
                var item = menuItems.Nth(i);
                var dataValue = (await item.GetAttributeAsync("data-value").ConfigureAwait(false))?.Trim();
                var text = (await item.InnerTextAsync().ConfigureAwait(false))?.Trim();
                if (string.IsNullOrWhiteSpace(dataValue) || string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                try
                {
                    await item.ClickAsync(new LocatorClickOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);
                    return true;
                }
                catch (TimeoutException) { continue; }
                catch (PlaywrightException) { continue; }
            }

            return false;
        }
        catch (TimeoutException) { return false; }
        catch (PlaywrightException) { return false; }
    }

    private static async Task TryFillIssueDateRangeAsync(
        IPage page,
        IReadOnlyDictionary<string, string> sourceConfig)
    {
        var min = sourceConfig.TryGetValue("playwright.issueDateMinFrom", out var configuredMin)
            && !string.IsNullOrWhiteSpace(configuredMin)
            ? configuredMin
            : "2015-04-01";
        try
        {
            await page.Locator("input[placeholder='Min value']").First
                .FillAsync(min, new LocatorFillOptions { Timeout = PageWaitTimeoutMs })
                .ConfigureAwait(false);
        }
        catch (TimeoutException) { }
        catch (PlaywrightException) { }

        var max = sourceConfig.TryGetValue("playwright.issueDateMaxTo", out var configuredMax)
            && !string.IsNullOrWhiteSpace(configuredMax)
            ? configuredMax
            : "2022-12-15";
        try
        {
            await page.Locator("input[placeholder='Max value']").First
                .FillAsync(max, new LocatorFillOptions { Timeout = PageWaitTimeoutMs })
                .ConfigureAwait(false);
        }
        catch (TimeoutException) { }
        catch (PlaywrightException) { }
    }

    private static async Task TryClickSearchAsync(IPage page)
    {
        try
        {
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions
            {
                Name = "Search",
                Exact = true,
            }).First.ClickAsync(new LocatorClickOptions { Timeout = PageWaitTimeoutMs })
                .ConfigureAwait(false);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                new PageWaitForLoadStateOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);
        }
        catch (TimeoutException) { }
    }

    private static async Task<List<OpportunityCandidate>> ExtractPageAsync(
        IPage page,
        Uri baseUri,
        string buyer,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var rows = await page.QuerySelectorAllAsync("tr[id*='_grd_tr_']").ConfigureAwait(false);
        var result = new List<OpportunityCandidate>(rows.Count);
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            var candidate = await TryMapRowAsync(row, baseUri, buyer).ConfigureAwait(false);
            if (candidate is not null)
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    private static async Task<OpportunityCandidate?> TryMapRowAsync(
        IElementHandle row,
        Uri baseUri,
        string buyer)
    {
        var cells = await row.QuerySelectorAllAsync(":scope > td").ConfigureAwait(false);
        if (cells.Count < 2)
        {
            return null;
        }

        var cellTexts = new List<string>(cells.Count);
        foreach (var cell in cells)
        {
            cellTexts.Add((await cell.InnerTextAsync().ConfigureAwait(false)).Trim());
        }

        var externalReference = cellTexts[0];
        var title = cellTexts[1];
        if (string.IsNullOrWhiteSpace(externalReference) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return new OpportunityCandidate
        {
            ExternalReference = externalReference,
            Title = title,
            Buyer = buyer,
            Url = await ResolveRowUrlAsync(row, baseUri).ConfigureAwait(false),
            Description = cellTexts.Count > 2 && !string.IsNullOrWhiteSpace(cellTexts[2])
                ? cellTexts[2]
                : null,
            PostedDateUtc = cellTexts.Count > 4 ? ParseDate(cellTexts[4]) : null,
            SubmissionDeadlineUtc = cellTexts.Count > 5 ? ParseDate(cellTexts[5]) : null,
            ProjectProvince = "BC",
            Location = "BC",
            RawJson = string.Join("|", cellTexts),
        };
    }

    private static async Task<string> ResolveRowUrlAsync(IElementHandle row, Uri baseUri)
    {
        var anchors = await row.QuerySelectorAllAsync("a[href]").ConfigureAwait(false);
        foreach (var anchor in anchors)
        {
            var href = (await anchor.GetAttributeAsync("href").ConfigureAwait(false))?.Trim();
            if (string.IsNullOrWhiteSpace(href)
                || (!href.Contains("process_open", StringComparison.OrdinalIgnoreCase)
                    && !href.Contains("request_browse", StringComparison.OrdinalIgnoreCase)
                    && !href.Contains("Tender/Detail", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            return new Uri(baseUri, href).AbsoluteUri;
        }

        return baseUri.AbsoluteUri;
    }

    private static DateTimeOffset? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var stripped = TimezoneSuffixRegex.Replace(raw.Trim(), string.Empty);
        return DateTimeOffset.TryParse(
            stripped,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static async Task<bool> TryAdvanceToNextPageAsync(IPage page, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

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
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                    new PageWaitForLoadStateOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);
                return true;
            }
            catch { }
        }

        return false;
    }

    private static int ResolveInt(IReadOnlyDictionary<string, string> config, string key, int defaultValue)
        => config.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : defaultValue;

    private static async Task TryWriteDiagnosticAsync(IPage page, string stem, CancellationToken ct)
    {
        try
        {
            var diagDir = Path.Combine(
                Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
                "KorOperations", "Opportunities", "diagnostics");
            Directory.CreateDirectory(diagDir);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(diagDir, $"{stem}-{stamp}.png"),
                FullPage = true,
            }).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(diagDir, $"{stem}-{stamp}.url.txt"),
                page.Url,
                ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(diagDir, $"{stem}-{stamp}.html"),
                await page.ContentAsync().ConfigureAwait(false),
                ct).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort diagnostic only.
        }
    }

    private async Task<bool> LoginAsync(IPage page, CancellationToken ct)
    {
        if (!_credentials.IsConfigured)
        {
            return false;
        }

        try
        {
            await page.GotoAsync(LoginEntryUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = PageWaitTimeoutMs,
            }).ConfigureAwait(false);

            // Click the "Login" link in the top nav. BCeID redirect follows.
            await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions
            {
                Name = "Login",
                Exact = true,
            }).First.ClickAsync(new LocatorClickOptions
            {
                Timeout = PageWaitTimeoutMs,
            }).ConfigureAwait(false);

            // Step 2: BC Bid shows an intermediate "Login to BC Bid" page with
            // two options - "Login with a Business or Basic BCeID" (for
            // suppliers, our path) and "Login with IDIR" (for ministry users).
            // Click the BCeID one. Regex match on "Business...BCeID" is robust
            // against minor copy changes.
            await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions
            {
                NameRegex = new Regex(
                    @"Business\s+or\s+Basic\s+BCeID",
                    RegexOptions.IgnoreCase),
            }).First.ClickAsync(new LocatorClickOptions
            {
                Timeout = PageWaitTimeoutMs,
            }).ConfigureAwait(false);

            // Step 3: Now wait for BCeID logon gateway redirect.
            await page.WaitForURLAsync(
                url => url.Contains("logon.gov.bc.ca", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("bceid", StringComparison.OrdinalIgnoreCase),
                new PageWaitForURLOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);

            // BCeID's logon page does not use <label for> fields. Type-based
            // selectors are unambiguous on this form.
            await page.Locator("input[type='text']:visible")
                .First.FillAsync(_credentials.Username, new LocatorFillOptions { Timeout = PageWaitTimeoutMs })
                .ConfigureAwait(false);
            await page.Locator("input[type='password']:visible")
                .First.FillAsync(_credentials.Password, new LocatorFillOptions { Timeout = PageWaitTimeoutMs })
                .ConfigureAwait(false);

            // Submit.
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions
            {
                Name = "Continue",
            }).First.ClickAsync(new LocatorClickOptions
            {
                Timeout = PageWaitTimeoutMs,
            }).ConfigureAwait(false);

            // Wait for redirect back to bcbid.gov.bc.ca (authenticated landing).
            await page.WaitForURLAsync(
                url => url.Contains("bcbid.gov.bc.ca", StringComparison.OrdinalIgnoreCase)
                    && !url.Contains("logon", StringComparison.OrdinalIgnoreCase),
                new PageWaitForURLOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);

            // Brief network-idle wait so any post-login redirects or cookie sets complete.
            try
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                    new PageWaitForLoadStateOptions { Timeout = 10_000 }).ConfigureAwait(false);
            }
            catch { }

            // Diagnostic: dump where we landed and a screenshot so the session
            // path can be checked against BC Bid login changes.
            try
            {
                var diagDir = Path.Combine(
                    Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
                    "KorOperations", "Opportunities", "diagnostics");
                Directory.CreateDirectory(diagDir);
                var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = Path.Combine(diagDir, $"BcBid-postlogin-{stamp}.png"),
                    FullPage = true,
                }).ConfigureAwait(false);
                await File.WriteAllTextAsync(
                    Path.Combine(diagDir, $"BcBid-postlogin-{stamp}.url.txt"),
                    page.Url, ct).ConfigureAwait(false);
            }
            catch { }

            return true;
        }
        catch (Exception ex)
        {
            // Diagnostic screenshot - same dir as the no-rows diag.
            try
            {
                var diagDir = Path.Combine(
                    Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
                    "KorOperations", "Opportunities", "diagnostics");
                Directory.CreateDirectory(diagDir);
                var path = Path.Combine(diagDir, $"BcBid-login-fail-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true }).ConfigureAwait(false);
            }
            catch { }

            // Base FetchAsync captures a diagnostic screenshot and logs this failure.
            throw new InvalidOperationException("BC Bid login failed: " + ex.Message, ex);
        }
    }
}
