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
public sealed class BcBidScraper : PlaywrightScraperBase<OpportunityCandidate>, IOpportunityProvider
{
    private const int DefaultMaxPages = 10;       // 15 rows/page  10 = 150 max
    private const int PageWaitTimeoutMs = 30_000;
    private const string LoginEntryUrl = "https://bcbid.gov.bc.ca/page.aspx/en/buy/homepage";

    private readonly BcBidCredentials _credentials;

    public BcBidScraper(
        PlaywrightBrowserPool pool,
        ILogger<BcBidScraper> logger,
        BcBidCredentials credentials)
        : base(pool, logger)
    {
        _credentials = credentials;
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

        // Authenticate if credentials are configured; otherwise fall back to
        // anonymous (which BC Bid captcha-gates to zero results, captured in
        // the existing diagnostic on TimeoutException).
        var loggedIn = await LoginAsync(page, ct).ConfigureAwait(false);

        if (loggedIn)
        {
            // Click the supplier-dashboard "Opportunities" side-nav link.
            // Direct GET on source.BaseUrl post-login hits BC Bid's captcha
            // (BC Bid still treats that URL as anonymous traffic regardless
            // of session). The side-nav link routes through the supplier
            // session and lands captcha-free.
            await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions
            {
                Name = "Opportunities",
                Exact = true,
            }).First.ClickAsync(new LocatorClickOptions
            {
                Timeout = PageWaitTimeoutMs,
            }).ConfigureAwait(false);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                new PageWaitForLoadStateOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);
        }
        else
        {
            await page.GotoAsync(source.BaseUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = PageWaitTimeoutMs,
            }).ConfigureAwait(false);
        }

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

        // One-time diagnostic: dump the rendered results-grid HTML so the live
        // Ivalua pagination DOM can be inspected to harden TryAdvanceToNextPage.
        // Gated on bcbid.dumpGrid=true; remove the mapping once captured.
        await MaybeDumpGridAsync(page, sourceConfig, ct).ConfigureAwait(false);

        var baseUri = new Uri(source.BaseUrl);

        // BC Bid (Ivalua) exposes total/current page via hidden fields; loop
        // deterministically to the last page. The old aria-label/anchor selectors
        // didn't match Ivalua's id-based <button> pager, so pagination quit early
        // at a random page (yields bounced 30/75/90/150 run-to-run).
        var maxIndex = await ReadIntFieldAsync(page, "#maxpageindexbody_x_grid_grd", 0).ConfigureAwait(false);
        var lastPage = Math.Min(maxIndex, maxPages - 1);
        for (var idx = 0; idx <= lastPage; idx++)
        {
            candidates.AddRange(await ExtractPageAsync(page, baseUri, ct).ConfigureAwait(false));
            if (idx >= lastPage)
            {
                break;
            }

            if (!await AdvanceToNextPageAsync(page, idx, ct).ConfigureAwait(false))
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

    // Clicks Ivalua's id-based Next button and waits until the current-page
    // hidden field advances past fromIndex — confirms the AJAX postback actually
    // repainted the grid before we extract (the source of the old flakiness).
    private static async Task<bool> AdvanceToNextPageAsync(IPage page, int fromIndex, CancellationToken ct)
    {
        // Ivalua's AJAX postback intermittently re-renders the pager mid-flight, so
        // a single click can miss. Retry the click+wait a couple times before giving
        // up; each attempt waits for the page-index hidden field to actually advance.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var next = page.Locator("#body_x_grid_gridPagerBtnNextPage");
                await next.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Attached,
                    Timeout = 10_000,
                }).ConfigureAwait(false);
                await next.ClickAsync(new LocatorClickOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);

                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (DateTime.UtcNow < deadline)
                {
                    ct.ThrowIfCancellationRequested();
                    var current = await ReadIntFieldAsync(page, "#hdnCurrentPageIndexbody_x_grid_grd", -1).ConfigureAwait(false);
                    if (current > fromIndex)
                    {
                        return true;
                    }

                    await page.WaitForTimeoutAsync(250).ConfigureAwait(false);
                }
            }
            catch
            {
                // fall through to retry
            }

            await page.WaitForTimeoutAsync(1_500).ConfigureAwait(false);
        }

        return false;
    }

    private static async Task<int> ReadIntFieldAsync(IPage page, string selector, int defaultValue)
    {
        try
        {
            var value = await page.Locator(selector)
                .InputValueAsync(new LocatorInputValueOptions { Timeout = 5_000 }).ConfigureAwait(false);
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : defaultValue;
        }
        catch
        {
            return defaultValue;
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
            // two options — "Login with a Business or Basic BCeID" (for
            // suppliers, our path) and "Login with IDIR" (for ministry users).
            // Click the BCeID one. Regex match on "Business...BCeID" is robust
            // against minor copy changes.
            await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions
            {
                NameRegex = new System.Text.RegularExpressions.Regex(
                    @"Business\s+or\s+Basic\s+BCeID",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            }).First.ClickAsync(new LocatorClickOptions
            {
                Timeout = PageWaitTimeoutMs,
            }).ConfigureAwait(false);

            // Step 3: Now wait for BCeID logon gateway redirect.
            await page.WaitForURLAsync(
                url => url.Contains("logon.gov.bc.ca", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("bceid", StringComparison.OrdinalIgnoreCase),
                new PageWaitForURLOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);

            // BCeID's logon page (sfs7.gov.bc.ca) doesn't use <label for> — labels
            // are bare text divs. Type-based selectors are unambiguous on this
            // form (one visible text input + one password input). The first
            // text input is auto-focused on page load.
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

            // Brief network-idle wait so any post-login redirects / cookie sets complete.
            try
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                    new PageWaitForLoadStateOptions { Timeout = 10_000 }).ConfigureAwait(false);
            }
            catch { }

            // Diagnostic: dump where we landed + a screenshot so we can verify
            // session vs. error page. Cheap, single fire per scrape.
            try
            {
                var diagDir = System.IO.Path.Combine(
                    Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
                    "KorOperations", "Opportunities", "diagnostics");
                System.IO.Directory.CreateDirectory(diagDir);
                var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = System.IO.Path.Combine(diagDir, $"BcBid-postlogin-{stamp}.png"),
                    FullPage = true,
                }).ConfigureAwait(false);
                var url = page.Url;
                await System.IO.File.WriteAllTextAsync(
                    System.IO.Path.Combine(diagDir, $"BcBid-postlogin-{stamp}.url.txt"),
                    url, ct).ConfigureAwait(false);
            }
            catch { }

            return true;
        }
        catch (Exception ex)
        {
            // Diagnostic screenshot - same dir as the no-rows diag.
            try
            {
                var diagDir = System.IO.Path.Combine(
                    Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
                    "KorOperations", "Opportunities", "diagnostics");
                System.IO.Directory.CreateDirectory(diagDir);
                var path = System.IO.Path.Combine(diagDir, $"BcBid-login-fail-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true }).ConfigureAwait(false);
            }
            catch { }

            // Base FetchAsync captures a diagnostic screenshot and logs this failure.
            throw new InvalidOperationException("BC Bid login failed: " + ex.Message, ex);
        }
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

    private async Task MaybeDumpGridAsync(IPage page, IReadOnlyDictionary<string, string> config, CancellationToken ct)
    {
        if (!(config.TryGetValue("bcbid.dumpGrid", out var flag)
              && string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        try
        {
            var diagDir = System.IO.Path.Combine(
                Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
                "KorOperations", "Opportunities", "diagnostics");
            System.IO.Directory.CreateDirectory(diagDir);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var path = System.IO.Path.Combine(diagDir, $"BcBid-grid-{stamp}.html");
            await System.IO.File.WriteAllTextAsync(path, await page.ContentAsync().ConfigureAwait(false), ct).ConfigureAwait(false);
        }
        catch { }
    }
}
