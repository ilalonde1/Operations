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
    private readonly ILogger<BcBidHistoricalScraper> _logger;

    public BcBidHistoricalScraper(
        PlaywrightBrowserPool pool,
        ILogger<BcBidHistoricalScraper> logger,
        BcBidCredentials credentials)
        : base(pool, logger)
    {
        _credentials = credentials;
        _logger = logger;
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

        await TryClearOpenStatusFilterAsync(page).ConfigureAwait(false);
        await TryFillIssueDateRangeAsync(page, sourceConfig).ConfigureAwait(false);

        var dropdown = await TryFindHistoricalDropdownAsync(page).ConfigureAwait(false);
        if (dropdown is null)
        {
            await TryWriteDiagnosticAsync(page, "BcBidHistorical-nofilter", ct).ConfigureAwait(false);
            _logger.LogInformation("Historical scraped {Types} types, {Candidates} candidates.", 0, 0);
            return Array.Empty<OpportunityCandidate>();
        }

        var historicalTypes = await DiscoverHistoricalTypesAsync(dropdown, sourceConfig).ConfigureAwait(false);
        if (historicalTypes.Count == 0)
        {
            await TryWriteDiagnosticAsync(page, "BcBidHistorical-nofilter", ct).ConfigureAwait(false);
            _logger.LogInformation("Historical scraped {Types} types, {Candidates} candidates.", 0, 0);
            return Array.Empty<OpportunityCandidate>();
        }

        var buyer = ResolveBuyer(sourceConfig);
        var baseUri = new Uri(source.BaseUrl);
        var candidates = new List<OpportunityCandidate>();
        var maxPages = ResolveInt(sourceConfig, "playwright.maxPages", DefaultMaxPages);
        var scrapedTypes = 0;
        var typesWithRows = 0;

        foreach (var historicalType in historicalTypes)
        {
            ct.ThrowIfCancellationRequested();

            if (!await TrySelectHistoricalTypeAsync(dropdown, historicalType.DataValue).ConfigureAwait(false))
            {
                _logger.LogWarning(
                    "BC Bid historical type {HistoricalType} ({DataValue}) could not be selected.",
                    historicalType.Text,
                    historicalType.DataValue);
                continue;
            }

            scrapedTypes++;

            // BC Bid resets Issue Date Min back to its 1899-12-31 default every
            // time the Historical Type changes. Status=Open also rebounds in
            // some types' flows. Re-apply both filters AFTER selecting the
            // type but BEFORE clicking Search to ensure the search uses our
            // intended constraints.
            await TryFillIssueDateRangeAsync(page, sourceConfig).ConfigureAwait(false);
            await TryClearOpenStatusFilterAsync(page).ConfigureAwait(false);

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
                _logger.LogWarning(
                    "BC Bid historical type {HistoricalType} ({DataValue}) returned no rows before timeout.",
                    historicalType.Text,
                    historicalType.DataValue);
                continue;
            }

            typesWithRows++;
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
                    "BC Bid historical pagination TRUNCATED at {MaxPages} page(s) for type {HistoricalType} — the portal offered another page; raise playwright.maxPages in the source config.",
                    maxPages, historicalType.Text);
                IngestionRunDiagnostics.AddWarning(
                    $"pagination truncated at {maxPages} page(s) for type '{historicalType.Text}' — portal offered more; raise playwright.maxPages");
            }
        }

        if (typesWithRows == 0)
        {
            await TryWriteDiagnosticAsync(page, "BcBidHistorical-norows", ct).ConfigureAwait(false);
        }

        if (candidates.Count == 0)
        {
            await TryWriteDiagnosticAsync(page, "BcBidHistorical-nocandidates", ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Historical scraped {Types} types, {Candidates} candidates.",
            scrapedTypes,
            candidates.Count);
        return candidates;
    }

    private static string ResolveBuyer(IReadOnlyDictionary<string, string> sourceConfig)
    {
        return sourceConfig.TryGetValue("bcbid.buyer", out var buyer)
            && !string.IsNullOrWhiteSpace(buyer)
            ? buyer.Trim()
            : "Province of British Columbia (BC Bid Historical)";
    }

    private async Task TryClearOpenStatusFilterAsync(IPage page)
    {
        // Actual markup of the active-filter "× Open" tag (verified via diagnostic
        // 2026-05-21):
        //   <ul class="values-container">
        //     <li class="ui label transition visible iv-tag" data-iv-role="tag" data-value="val">
        //       <span class="selected_label">Open</span>
        //       <button type="button" data-value="val" class="ui button borderless delete">
        //         <i class="fa-times icon"></i>
        //         <span class="sr-only">Delete "Open"</span>
        //       </button>
        //     </li>
        //   </ul>
        // Key identifiers: li.iv-tag containing span.selected_label with text
        // "Open", with a sibling button.ui.button.delete.
        var deleteLocators = new[]
        {
            page.Locator("li.iv-tag:has(span.selected_label:has-text('Open')) button.delete"),
            page.Locator("button.delete:has(span.sr-only:has-text('Delete \"Open\"'))"),
            page.Locator("li[data-iv-role='tag']:has-text('Open') button.delete"),
        };

        foreach (var deleteLocator in deleteLocators)
        {
            try
            {
                if (await deleteLocator.CountAsync().ConfigureAwait(false) == 0)
                {
                    continue;
                }

                await deleteLocator.First.ClickAsync(new LocatorClickOptions
                {
                    Timeout = PageWaitTimeoutMs,
                }).ConfigureAwait(false);
                try
                {
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                        new PageWaitForLoadStateOptions { Timeout = 5_000 }).ConfigureAwait(false);
                }
                catch (TimeoutException) { }

                _logger.LogInformation("BC Bid historical scrape cleared the pre-applied Open status tag.");
                return;
            }
            catch (TimeoutException)
            {
                continue;
            }
            catch (PlaywrightException)
            {
                continue;
            }
        }

        _logger.LogInformation("BC Bid historical scrape found no pre-applied Open status tag to clear (or selectors missed).");
    }

    private static async Task<ILocator?> TryFindHistoricalDropdownAsync(IPage page)
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

            foreach (var c in dropdownCandidates)
            {
                if (await c.CountAsync().ConfigureAwait(false) > 0)
                {
                    return c;
                }
            }
        }
        catch (TimeoutException) { }
        catch (PlaywrightException) { }

        return null;
    }

    private static async Task<List<(string DataValue, string Text)>> DiscoverHistoricalTypesAsync(
        ILocator dropdown,
        IReadOnlyDictionary<string, string> sourceConfig)
    {
        await dropdown.ScrollIntoViewIfNeededAsync().ConfigureAwait(false);
        await dropdown.ClickAsync(new LocatorClickOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);

        // Menu becomes visible after click. Items are <li>, NOT <div> (Ivalua/
        // Semantic-UI markup pattern verified 2026-05-21):
        //   <li data-iv-role="item" class="item" id="body_x_selNtypeCode_RFP"
        //       data-value="RFP" aria-selected="false" role="option">
        //     <span class="text">Invitation Tender (RFP)</span>
        //   </li>
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
            return new List<(string DataValue, string Text)>();
        }

        var typeFilter = ParseHistoricalTypeFilter(sourceConfig);
        var historicalTypes = new List<(string DataValue, string Text)>();
        var itemCount = await menuItems.CountAsync().ConfigureAwait(false);
        for (var i = 0; i < itemCount; i++)
        {
            var item = menuItems.Nth(i);
            var dataValue = (await item.GetAttributeAsync("data-value").ConfigureAwait(false))?.Trim();
            var text = (await item.InnerTextAsync().ConfigureAwait(false))?.Trim();
            if (string.IsNullOrWhiteSpace(dataValue)
                || string.IsNullOrWhiteSpace(text)
                || (typeFilter.Count > 0 && !typeFilter.Contains(dataValue)))
            {
                continue;
            }

            historicalTypes.Add((dataValue, text));
        }

        return historicalTypes;
    }

    private static HashSet<string> ParseHistoricalTypeFilter(IReadOnlyDictionary<string, string> sourceConfig)
    {
        var filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!sourceConfig.TryGetValue("bcbid.historicalTypeFilter", out var configuredFilter)
            || string.IsNullOrWhiteSpace(configuredFilter))
        {
            return filter;
        }

        foreach (var token in configuredFilter.Split(','))
        {
            var value = token.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                filter.Add(value);
            }
        }

        return filter;
    }

    private static async Task<bool> TrySelectHistoricalTypeAsync(ILocator dropdown, string dataValue)
    {
        try
        {
            var item = dropdown.Locator($"[role='option'][data-value='{dataValue}']");
            if (!await item.IsVisibleAsync().ConfigureAwait(false))
            {
                await dropdown.ClickAsync(new LocatorClickOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);
            }

            await item.ClickAsync(new LocatorClickOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    private static async Task TryFillIssueDateRangeAsync(
        IPage page,
        IReadOnlyDictionary<string, string> sourceConfig)
    {
        var min = sourceConfig.TryGetValue("playwright.issueDateMinFrom", out var configuredMin)
            && !string.IsNullOrWhiteSpace(configuredMin)
            ? configuredMin
            : "2015-04-01";
        await TryFillIssueDateInputAsync(page, "Min value", min).ConfigureAwait(false);

        var max = sourceConfig.TryGetValue("playwright.issueDateMaxTo", out var configuredMax)
            && !string.IsNullOrWhiteSpace(configuredMax)
            ? configuredMax
            : "2022-12-15";
        await TryFillIssueDateInputAsync(page, "Max value", max).ConfigureAwait(false);
    }

    private static async Task TryFillIssueDateInputAsync(IPage page, string placeholder, string value)
    {
        // Actual markup (verified via diagnostic):
        //   <input id="body_x_txtRfpBeginDate" aria-label="Issue Date (Minimum)"
        //          placeholder="Min value" value="1899-12-31" class="hasDatepicker" type="text">
        //   <input id="body_x_txtRfpEndDate"   aria-label="Issue Date (Maximum)"
        //          placeholder="Max value" class="hasDatepicker" type="text">
        // The aria-label is the most stable selector — IDs may drift if BC Bid
        // regenerates them. Placeholder works as fallback. Native FillAsync
        // sometimes does not register with the jQuery UI datepicker; if so the
        // type-and-press flow will fire change events the datepicker listens for.
        var ariaLabel = placeholder == "Min value" ? "Issue Date (Minimum)" : "Issue Date (Maximum)";
        var inputs = new[]
        {
            page.Locator($"input[aria-label='{ariaLabel}']"),
            page.Locator(placeholder == "Min value" ? "input#body_x_txtRfpBeginDate" : "input#body_x_txtRfpEndDate"),
            page.Locator($"label:has-text('Issue Date') ~ div input[placeholder='{placeholder}']"),
            page.Locator($"input[placeholder='{placeholder}']"),
        };

        foreach (var input in inputs)
        {
            try
            {
                if (await input.CountAsync().ConfigureAwait(false) == 0)
                {
                    continue;
                }

                await input.First.FillAsync(value, new LocatorFillOptions { Timeout = PageWaitTimeoutMs })
                    .ConfigureAwait(false);
                return;
            }
            catch (TimeoutException)
            {
                continue;
            }
            catch (PlaywrightException)
            {
                continue;
            }
        }
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
        var dumpedFirstRow = false;
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            // One-off diagnostic: dump the outerHTML of the very first row we see
            // so we can identify the correct detail-page anchor pattern. Drops a
            // single file per scrape session at most.
            if (!dumpedFirstRow)
            {
                dumpedFirstRow = true;
                try
                {
                    var dir = @"C:\ProgramData\KorOperations\Opportunities\diagnostics";
                    System.IO.Directory.CreateDirectory(dir);
                    var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
                    var path = System.IO.Path.Combine(dir, $"BcBidHistorical-firstrow-{stamp}.html");
                    var html = await row.EvaluateAsync<string>("el => el.outerHTML").ConfigureAwait(false);
                    await System.IO.File.WriteAllTextAsync(path, html ?? "(null)", ct).ConfigureAwait(false);
                }
                catch
                {
                    // diagnostic dump is best-effort; never break the scrape over it
                }
            }

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
        if (cells.Count < 3)
        {
            return null;
        }

        var sourceInternalId = (await row.GetAttributeAsync("data-id").ConfigureAwait(false))?.Trim();
        if (string.IsNullOrWhiteSpace(sourceInternalId)) sourceInternalId = null;

        var cellTexts = new List<string>(cells.Count);
        foreach (var cell in cells)
        {
            cellTexts.Add((await cell.InnerTextAsync().ConfigureAwait(false)).Trim());
        }

        // Column order verified from production scrape 2026-05-21 (rr22.png +
        // first-pass live ingest collapse to OpportunityKey="BCBIDHIS-Closed"):
        //   [0] Status (Open/Closed/Cancelled/Awarded — buyer-side workflow state)
        //   [1] Opportunity ID  (the real external reference)
        //   [2] Opportunity Description (the real title)
        //   [3] Commodities
        //   [4] Type
        //   [5] Issue Date          → PostedDateUtc
        //   [6] Closing Date        → SubmissionDeadlineUtc
        //   [7] Ends At
        //   [8] # of Amendments
        //   [9] Last Updated
        //   [10] Organization (Issued by)
        //   [11] Organization (Issued to)
        //   [12] Interested Vendor List Available
        var status = cellTexts[0];
        var externalReference = cellTexts[1];
        var title = cellTexts[2];
        if (string.IsNullOrWhiteSpace(externalReference) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var issuingOrg = cellTexts.Count > 10 && !string.IsNullOrWhiteSpace(cellTexts[10])
            ? cellTexts[10]
            : buyer;

        return new OpportunityCandidate
        {
            ExternalReference = externalReference,
            SourceInternalId = sourceInternalId,
            Title = title,
            Buyer = issuingOrg,
            Url = await ResolveRowUrlAsync(row, baseUri).ConfigureAwait(false),
            Description = cellTexts.Count > 3 && !string.IsNullOrWhiteSpace(cellTexts[3])
                ? cellTexts[3]
                : null,
            PostedDateUtc = cellTexts.Count > 5 ? ParseDate(cellTexts[5]) : null,
            SubmissionDeadlineUtc = cellTexts.Count > 6 ? ParseDate(cellTexts[6]) : null,
            ProjectProvince = "BC",
            Location = "BC",
            // Prepend status to RawJson so downstream queries can recover the
            // buyer-side workflow state (Open/Closed/Awarded etc.) without
            // re-parsing the cells.
            RawJson = $"status={status}|" + string.Join("|", cellTexts),
        };
    }

    /// <summary>
    /// Probe-only entry point used by <c>tools/ScraperProbe</c> to exercise
    /// <see cref="TryMapRowAsync"/> against a saved HTML file in seconds, without
    /// spinning up the full scraper / DB / worker stack. Not used by production
    /// code paths.
    /// </summary>
    public static Task<OpportunityCandidate?> TryMapRowForProbeAsync(
        IElementHandle row,
        Uri baseUri,
        string buyer)
        => TryMapRowAsync(row, baseUri, buyer);

    private static async Task<string> ResolveRowUrlAsync(IElementHandle row, Uri baseUri)
    {
        // BC Bid's historical grid uses anchors with hrefs that point to the
        // per-opportunity detail page. We accept anything that's NOT a list-page,
        // sort/postback control, or static asset.
        var anchors = await row.QuerySelectorAllAsync("a[href]").ConfigureAwait(false);
        foreach (var anchor in anchors)
        {
            var href = (await anchor.GetAttributeAsync("href").ConfigureAwait(false))?.Trim();
            if (string.IsNullOrWhiteSpace(href)) continue;
            if (href.StartsWith("#", StringComparison.Ordinal)) continue;
            if (href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) continue;
            // Known list-page paths — skip these, they're back-links not detail.
            if (href.Contains("request_browse_public", StringComparison.OrdinalIgnoreCase)) continue;
            if (href.Contains("contract_browse_public", StringComparison.OrdinalIgnoreCase)) continue;
            if (href.Contains("unverified_bids_browse_public", StringComparison.OrdinalIgnoreCase)) continue;
            // Grid sort / column-header / pagination postbacks.
            if (href.Contains("__doPostBack", StringComparison.OrdinalIgnoreCase)) continue;
            if (href.Contains("/sort/", StringComparison.OrdinalIgnoreCase)) continue;
            if (href.Contains("columnSort", StringComparison.OrdinalIgnoreCase)) continue;

            return href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? href
                : new Uri(baseUri, href).AbsoluteUri;
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
