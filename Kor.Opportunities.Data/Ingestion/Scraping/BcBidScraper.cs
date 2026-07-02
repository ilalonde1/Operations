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
    private const int PageWaitTimeoutMs = 60_000;
    private const int LoginMaxAttempts = 3;
    private const string LoginEntryUrl = "https://bcbid.gov.bc.ca/page.aspx/en/buy/homepage";
    internal const string KeywordConfigKey = "bcbid.keyword";
    internal const string KeywordInputSelector = "input[aria-label='Keyword(s)']";
    private static readonly string[] KeywordInputSelectors =
    [
        KeywordInputSelector,
        "input[id*='keyword' i]",
        "input[name*='keyword' i]",
        "input[placeholder*='Keyword' i]",
    ];

    private readonly BcBidCredentials _credentials;
    private readonly ILogger<BcBidScraper> _logger;

    public BcBidScraper(
        PlaywrightBrowserPool pool,
        ILogger<BcBidScraper> logger,
        BcBidCredentials credentials)
        : base(pool, logger)
    {
        _credentials = credentials;
        _logger = logger;
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

        await TryApplyKeywordFilterAsync(page, sourceConfig).ConfigureAwait(false);

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

        // BD-Audit-2026-06-09 M19: map grid columns by header text, resolved
        // once per scrape — a silent Ivalua column insertion must not feed the
        // wrong cell (e.g. garbage BuyerName) into the canonical resolver.
        // Throws (aborting the run loudly) when expected headers are missing;
        // falls back to the verified ordinal layout only when no header row
        // can be located at all.
        var columnMap = await ResolveColumnMapAsync(page).ConfigureAwait(false);

        var baseUri = new Uri(source.BaseUrl);

        // BC Bid (Ivalua) exposes total/current page via hidden fields; loop
        // deterministically to the last page. The old aria-label/anchor selectors
        // didn't match Ivalua's id-based <button> pager, so pagination quit early
        // at a random page (yields bounced 30/75/90/150 run-to-run).
        var maxIndex = await ReadIntFieldAsync(page, "#maxpageindexbody_x_grid_grd", 0).ConfigureAwait(false);
        var lastPage = Math.Min(maxIndex, maxPages - 1);
        for (var idx = 0; idx <= lastPage; idx++)
        {
            candidates.AddRange(await ExtractPageAsync(page, baseUri, columnMap, ct).ConfigureAwait(false));
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

    private async Task TryApplyKeywordFilterAsync(
        IPage page,
        IReadOnlyDictionary<string, string> sourceConfig)
    {
        var keyword = ResolveKeywordFilter(sourceConfig);
        if (keyword is null)
        {
            return;
        }

        foreach (var selector in KeywordInputSelectors)
        {
            try
            {
                var keywordInput = page.Locator(selector);
                if (await keywordInput.CountAsync().ConfigureAwait(false) == 0)
                {
                    continue;
                }

                await keywordInput.First.FillAsync(keyword, new LocatorFillOptions
                {
                    Timeout = PageWaitTimeoutMs,
                }).ConfigureAwait(false);

                _logger.LogInformation("BC Bid scrape filtered by keyword: {Keyword}", keyword);
                return;
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(
                    ex,
                    "BC Bid keyword filter '{Keyword}' selector timed out ({Selector}).",
                    keyword,
                    selector);
            }
            catch (PlaywrightException ex)
            {
                _logger.LogWarning(
                    ex,
                    "BC Bid keyword filter '{Keyword}' selector failed ({Selector}).",
                    keyword,
                    selector);
            }
        }

        _logger.LogWarning(
            "BC Bid keyword filter '{Keyword}' skipped: keyword input selector not found ({Selector}).",
            keyword,
            string.Join(", ", KeywordInputSelectors));

        // Diagnostic: the portal changed its search-box markup (2026-06/07 —
        // none of the selectors above match anymore). Log an inventory of the
        // text inputs actually on the page so the selector list can be refreshed
        // from a production run without hand-driving the authenticated portal.
        try
        {
            var inventory = await page.EvaluateAsync<string>(@"() =>
                Array.from(document.querySelectorAll('input, textarea'))
                    .filter(el => el.type !== 'hidden' && el.type !== 'checkbox' && el.type !== 'radio' && el.type !== 'submit' && el.type !== 'button')
                    .slice(0, 40)
                    .map(el => `type=${el.type||''} id=${el.id||''} name=${el.name||''} placeholder=${el.getAttribute('placeholder')||''} aria-label=${el.getAttribute('aria-label')||''} class=${(el.className||'').toString().slice(0,80)}`)
                    .join(' || ')").ConfigureAwait(false);
            _logger.LogWarning("BC Bid keyword-box diagnostic — visible text inputs on the page: {Inputs}", inventory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BC Bid keyword-box diagnostic failed.");
        }
    }

    internal static string? ResolveKeywordFilter(IReadOnlyDictionary<string, string> sourceConfig)
        => sourceConfig.TryGetValue(KeywordConfigKey, out var keyword)
           && !string.IsNullOrWhiteSpace(keyword)
            ? keyword.Trim()
            : null;

    /// <summary>
    /// Resolved cell indexes for the Ivalua opportunities grid. Built from the
    /// header row once per scrape; <see cref="OrdinalFallback"/> is the layout
    /// verified against production 2026-05-21 (see BcBidHistoricalScraper's
    /// column-order comment) and is used only when no header row exists.
    /// </summary>
    private sealed record BcBidColumnMap(
        int Status,
        int ExternalRef,
        int Title,
        int Commodities,
        int SolicitationType,
        int IssueDate,
        int ClosingDate,
        int Buyer)
    {
        public static readonly BcBidColumnMap OrdinalFallback = new(0, 1, 2, 3, 4, 5, 6, 10);

        public int MinimumCellCount =>
            1 + Math.Max(Status, Math.Max(ExternalRef, Math.Max(Title, Math.Max(Commodities,
                Math.Max(SolicitationType, Math.Max(IssueDate, Math.Max(ClosingDate, Buyer)))))));
    }

    /// <summary>
    /// Reads the header row of the table hosting the rfp rows and maps each
    /// expected column by header text (case/whitespace tolerant). Missing
    /// expected headers fall back to the verified ordinal layout with a
    /// warning; abort only when the grid has no usable rows at all.
    /// </summary>
    private async Task<BcBidColumnMap> ResolveColumnMapAsync(IPage page)
    {
        var firstRow = await page.QuerySelectorAsync("tr[data-object-type='rfp']").ConfigureAwait(false);
        if (firstRow is null)
        {
            // Caller already waited for rows; defensive only.
            throw new InvalidOperationException("BC Bid results-grid header mapping failed: no usable rfp rows found.");
        }

        var headerCells = await firstRow.QuerySelectorAllAsync("xpath=ancestor::table[1]/thead//th").ConfigureAwait(false);
        if (headerCells.Count == 0)
        {
            headerCells = await firstRow.QuerySelectorAllAsync("xpath=ancestor::table[1]//tr[th][1]/th").ConfigureAwait(false);
        }

        if (headerCells.Count == 0)
        {
            if (!await HasUsableRowsAsync(page, BcBidColumnMap.OrdinalFallback.MinimumCellCount).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "BC Bid results-grid header mapping failed: no usable rows found for the verified ordinal layout.");
            }

            _logger.LogWarning(
                "BC Bid column mapping: no header row located in the results grid; " +
                "falling back to the verified ordinal layout (status=0, id=1, title=2, commodities=3, type=4, issued=5, closing=6, buyer=10).");
            return BcBidColumnMap.OrdinalFallback;
        }

        var headers = new List<string>(headerCells.Count);
        foreach (var cell in headerCells)
        {
            headers.Add(NormalizeHeaderText(await cell.InnerTextAsync().ConfigureAwait(false)));
        }

        int FindExact(string name)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                if (string.Equals(headers[i], name, StringComparison.Ordinal)) return i;
            }

            return -1;
        }

        int FindContains(string fragment)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                if (headers[i].Contains(fragment, StringComparison.Ordinal)) return i;
            }

            return -1;
        }

        int FindStartsWith(string prefix)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                if (headers[i].StartsWith(prefix, StringComparison.Ordinal)) return i;
            }

            return -1;
        }

        // Header labels per the production-verified Ivalua grid (the
        // historical scraper documents the same column set). Buyer is the
        // "Organization (Issued by)" column — matched on the "issued by"
        // fragment so it can't be confused with "Organization (Issued to)".
        var status = FindExact("status");
        var externalRef = FindExact("opportunity id");
        var title = FindExact("opportunity description");
        var commodities = FindExact("commodities");
        var solicitationType = FindExact("type");
        // 2026-07-01 portal change: the date columns are now rendered as
        // "issue date and time (pacific time)" / "closing date and time
        // (pacific time)". Exact-match first (old layout), then prefix-match —
        // StartsWith, not Contains, so "last updated"/"ends in" can't collide.
        var issueDate = FindExact("issue date");
        if (issueDate < 0) issueDate = FindStartsWith("issue date");
        var closingDate = FindExact("closing date");
        if (closingDate < 0) closingDate = FindStartsWith("closing date");
        var buyer = FindContains("issued by");

        var missing = new List<string>();
        if (status < 0) missing.Add("Status");
        if (externalRef < 0) missing.Add("Opportunity ID");
        if (title < 0) missing.Add("Opportunity Description");
        if (commodities < 0) missing.Add("Commodities");
        if (solicitationType < 0) missing.Add("Type");
        if (issueDate < 0) missing.Add("Issue Date");
        if (closingDate < 0) missing.Add("Closing Date");
        if (buyer < 0) missing.Add("Organization (Issued by)");

        if (missing.Count > 0)
        {
            _logger.LogWarning(
                "BC Bid results-grid header mapping missing expected column(s) [{Missing}] among rendered headers [{Headers}]; falling back to the verified ordinal layout for missing columns.",
                string.Join(", ", missing),
                string.Join(" | ", headers));
        }

        status = status < 0 ? BcBidColumnMap.OrdinalFallback.Status : status;
        externalRef = externalRef < 0 ? BcBidColumnMap.OrdinalFallback.ExternalRef : externalRef;
        title = title < 0 ? BcBidColumnMap.OrdinalFallback.Title : title;
        commodities = commodities < 0 ? BcBidColumnMap.OrdinalFallback.Commodities : commodities;
        solicitationType = solicitationType < 0 ? BcBidColumnMap.OrdinalFallback.SolicitationType : solicitationType;
        issueDate = issueDate < 0 ? BcBidColumnMap.OrdinalFallback.IssueDate : issueDate;
        closingDate = closingDate < 0 ? BcBidColumnMap.OrdinalFallback.ClosingDate : closingDate;
        buyer = buyer < 0 ? BcBidColumnMap.OrdinalFallback.Buyer : buyer;

        var columnMap = new BcBidColumnMap(status, externalRef, title, commodities, solicitationType, issueDate, closingDate, buyer);
        if (!await HasUsableRowsAsync(page, columnMap.MinimumCellCount).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "BC Bid results-grid header mapping failed: no usable rows found for the resolved column layout.");
        }

        _logger.LogInformation(
            "BC Bid column mapping resolved from headers: status={Status}, id={Id}, title={Title}, commodities={Commodities}, type={Type}, issued={Issued}, closing={Closing}, buyer={Buyer}.",
            status, externalRef, title, commodities, solicitationType, issueDate, closingDate, buyer);
        return columnMap;
    }

    private static async Task<bool> HasUsableRowsAsync(IPage page, int minimumCellCount)
    {
        var rows = await page.QuerySelectorAllAsync("tr[data-object-type='rfp']").ConfigureAwait(false);
        foreach (var row in rows)
        {
            var cells = await row.QuerySelectorAllAsync(":scope > td").ConfigureAwait(false);
            if (cells.Count >= minimumCellCount)
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeHeaderText(string text)
        => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private static async Task<List<OpportunityCandidate>> ExtractPageAsync(
        IPage page, Uri baseUri, BcBidColumnMap columnMap, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Pull all RFP rows; for each, read the cell texts by mapped index.
        var rows = await page.QuerySelectorAllAsync("tr[data-object-type='rfp']").ConfigureAwait(false);
        var result = new List<OpportunityCandidate>(rows.Count);

        foreach (var row in rows)
        {
            var candidate = await TryMapRowAsync(row, baseUri, columnMap).ConfigureAwait(false);
            if (candidate is not null) result.Add(candidate);
        }

        return result;
    }

    private static async Task<OpportunityCandidate?> TryMapRowAsync(IElementHandle row, Uri baseUri, BcBidColumnMap map)
    {
        var cells = await row.QuerySelectorAllAsync(":scope > td").ConfigureAwait(false);
        if (cells.Count < map.MinimumCellCount) return null;

        var status = (await cells[map.Status].InnerTextAsync().ConfigureAwait(false)).Trim();
        if (!string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var externalRef = (await row.GetAttributeAsync("data-id").ConfigureAwait(false))?.Trim();
        if (string.IsNullOrWhiteSpace(externalRef))
        {
            var idAnchor = await cells[map.ExternalRef].QuerySelectorAsync("a").ConfigureAwait(false);
            if (idAnchor is not null)
            {
                externalRef = (await idAnchor.InnerTextAsync().ConfigureAwait(false))?.Trim();
            }
        }

        var title = (await cells[map.Title].InnerTextAsync().ConfigureAwait(false)).Trim();
        if (string.IsNullOrWhiteSpace(title)) return null;

        var anchor = await row.QuerySelectorAsync("a[href]").ConfigureAwait(false);
        var href = anchor is null ? null : (await anchor.GetAttributeAsync("href").ConfigureAwait(false))?.Trim();
        if (string.IsNullOrWhiteSpace(href)) return null;
        var url = new Uri(baseUri, href).AbsoluteUri;

        var commoditiesText = (await cells[map.Commodities].InnerTextAsync().ConfigureAwait(false))
            .Replace("\r", " ").Replace("\n", " / ").Trim();
        var solicitationType = (await cells[map.SolicitationType].InnerTextAsync().ConfigureAwait(false)).Trim();

        var description = string.IsNullOrWhiteSpace(commoditiesText)
            ? solicitationType
            : (string.IsNullOrWhiteSpace(solicitationType)
                ? commoditiesText
                : $"{solicitationType}: {commoditiesText}");

        var posted = ParseBcBidDate((await cells[map.IssueDate].InnerTextAsync().ConfigureAwait(false)).Trim());
        var deadline = ParseBcBidDate((await cells[map.ClosingDate].InnerTextAsync().ConfigureAwait(false)).Trim());

        var buyer = (await cells[map.Buyer].InnerTextAsync().ConfigureAwait(false)).Trim();
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
            for (var attempt = 1; attempt <= LoginMaxAttempts; attempt++)
            {
                try
                {
                    await RunLoginAttemptAsync(page).ConfigureAwait(false);
                    break;
                }
                catch (Exception ex) when (IsRetryableLoginException(ex) && attempt < LoginMaxAttempts)
                {
                    var delay = TimeSpan.FromSeconds(attempt * 2);
                    _logger.LogWarning(
                        ex,
                        "BC Bid login attempt {Attempt}/{MaxAttempts} failed with a transient navigation error; retrying in {DelaySeconds} seconds.",
                        attempt,
                        LoginMaxAttempts,
                        delay.TotalSeconds);
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
            }

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

    private async Task RunLoginAttemptAsync(IPage page)
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
    }

    private static bool IsRetryableLoginException(Exception ex) =>
        ex is TimeoutException or PlaywrightException;

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
