#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Ingestion.Scraping;

/// <summary>
/// Listing scraper for Tempest "OurCity / Prospero" development trackers — the
/// system Victoria, Saanich and View Royal all license, and the counterpart to
/// <see cref="VictoriaProsperoLiveDetailExtractor"/>, which already reads the
/// detail page for ANY Tempest host.
///
/// WHY IT IS WORTH A SCRAPER. Saanich, Langford and Colwood are the three
/// biggest permit markets in Greater Victoria — Q1 2026 housing starts 195, 171
/// and 143 against the City of Victoria's 117 — and none of them was wired.
/// Saanich publishes no ArcGIS layer and no open-data feed; its tracker is the
/// only public route, and it is ASP.NET WebForms.
///
/// Rows carry stable semantic classes, the same way the detail page carries
/// stable control ids: .search_folderNo, .search_address, .search_type,
/// .search_purpose, plus "Application Date:" and "Status:" in the row body.
/// Parsing is anchored to those, not to layout.
///
/// ⚠ PAGINATION IS A PARTIAL POSTBACK, not a navigation. The house idiom
/// elsewhere (click Next, then WaitForLoadState(NetworkIdle)) is not safe here:
/// an UpdatePanel refresh can settle the network before the results table has
/// re-rendered, which silently re-scrapes the same page. So advancing waits for
/// the FIRST FOLDER NUMBER TO CHANGE, and treats "unchanged" as the end of the
/// list rather than as success.
/// </summary>
public sealed class TempestProsperoScraper : PlaywrightScraperBase<OpportunityCandidate>, IOpportunityProvider
{
    private const int DefaultMaxPages = 40;
    private const int PageWaitTimeoutMs = 45_000;
    private const int RowWaitTimeoutMs = 20_000;
    private const int PostbackSettleMs = 15_000;

    private const string ResultSelector = "div.form-result";
    private const string FolderSelector = ".search_folderNo";

    private readonly ILogger<TempestProsperoScraper> _logger;

    public TempestProsperoScraper(PlaywrightBrowserPool pool, ILogger<TempestProsperoScraper> logger)
        : base(pool, logger)
    {
        _logger = logger;
    }

    public override OpportunitySourceType SourceType => OpportunitySourceType.TempestProspero;

    protected override async Task<IReadOnlyList<OpportunityCandidate>> ScrapeAsync(
        IPage page,
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        var buyer = Cfg(sourceConfig, "prospero.buyer") ?? source.Name;
        var city = Cfg(sourceConfig, "prospero.cityOverride");
        var province = Cfg(sourceConfig, "prospero.provinceOverride") ?? "BC";
        var maxPages = ResolveInt(sourceConfig, "playwright.maxPages", DefaultMaxPages);

        await page.GotoAsync(source.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = PageWaitTimeoutMs,
        }).ConfigureAwait(false);

        try
        {
            await page.WaitForSelectorAsync(ResultSelector, new PageWaitForSelectorOptions
            {
                Timeout = RowWaitTimeoutMs,
                State = WaitForSelectorState.Attached,
            }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // A tracker with nothing open is a legitimate empty result, not a
            // failure — same distinction the bids&tenders scraper makes.
            _logger.LogWarning("Prospero {Source}: no result rows rendered.", source.Name);
            return Array.Empty<OpportunityCandidate>();
        }

        var byRef = new Dictionary<string, OpportunityCandidate>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        var truncated = false;

        for (var pageNum = 1; pageNum <= maxPages; pageNum++)
        {
            ct.ThrowIfCancellationRequested();

            var firstFolder = await FirstFolderAsync(page).ConfigureAwait(false);

            foreach (var c in await ExtractPageAsync(page, source.BaseUrl, buyer, city, province).ConfigureAwait(false))
            {
                var key = c.ExternalReference!;
                if (byRef.TryAdd(key, c))
                {
                    order.Add(key);
                }
            }

            if (!await TryAdvanceAsync(page, firstFolder, ct).ConfigureAwait(false))
            {
                break;
            }

            if (pageNum == maxPages)
            {
                truncated = true;
            }
        }

        if (truncated)
        {
            _logger.LogWarning(
                "Prospero {Source} pagination TRUNCATED at {MaxPages} page(s); raise playwright.maxPages.",
                source.Name, maxPages);
            IngestionRunDiagnostics.AddWarning(
                $"Prospero pagination truncated at {maxPages} page(s) — the tracker offered more; raise playwright.maxPages");
        }

        _logger.LogInformation(
            "Prospero {Source}: {Count} distinct application(s).", source.Name, order.Count);

        return order.Select(k => byRef[k]).ToList();
    }

    private static async Task<string?> FirstFolderAsync(IPage page)
    {
        var first = page.Locator($"{ResultSelector} {FolderSelector}").First;
        try
        {
            return (await first.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 5_000 })
                .ConfigureAwait(false))?.Trim();
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<OpportunityCandidate>> ExtractPageAsync(
        IPage page,
        string baseUrl,
        string buyer,
        string? city,
        string? province)
    {
        var rows = await page.QuerySelectorAllAsync(ResultSelector).ConfigureAwait(false);
        var result = new List<OpportunityCandidate>(rows.Count);

        foreach (var row in rows)
        {
            var folder = await TextAsync(row, FolderSelector).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            var address = Collapse(await TextAsync(row, ".search_address").ConfigureAwait(false));
            var type = Collapse(await TextAsync(row, ".search_type").ConfigureAwait(false));
            var purpose = Collapse(await TextAsync(row, ".search_purpose").ConfigureAwait(false));
            var body = Collapse(await row.InnerTextAsync().ConfigureAwait(false));

            var title = string.IsNullOrWhiteSpace(type)
                ? (address ?? folder!)
                : $"{type} — {address ?? folder!}";

            result.Add(new OpportunityCandidate
            {
                Title = Trim(title, 400)!,
                Buyer = buyer,
                Location = address,
                Url = DetailUrl(baseUrl, folder!),
                Description = Trim(purpose ?? address, 4000),
                PostedDateUtc = ParseApplicationDate(body),
                ExternalReference = Trim(folder!.Trim(), 200),
                ProjectCity = city,
                ProjectProvince = province,
                RawJson = null,
            });
        }

        return result;
    }

    /// <summary>
    /// Clicks Next and waits for the FIRST FOLDER NUMBER to change. Returns false
    /// when there is no enabled Next, or when the list did not move — which on a
    /// partial postback is the only reliable end-of-list signal.
    /// </summary>
    private async Task<bool> TryAdvanceAsync(IPage page, string? firstFolderBefore, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // ⚠ The Next control is an anchor marked hidden="hidden", driven by the
        // page's own nextPagePagination() helper — so a visibility check skips it
        // and pagination silently stops after one page. That is exactly what
        // happened on the first live run against Saanich: 20 rows, one page, no
        // error. Use the page's OWN mechanism rather than simulating a click on a
        // hidden element: call its helper, and fall back to the __doPostBack the
        // anchor's href already carries.
        var advanced = false;
        try
        {
            advanced = await page.EvaluateAsync<bool>(
                @"() => {
                    if (typeof nextPagePagination === 'function') { nextPagePagination(); return true; }
                    const a = document.querySelector(""[id$='NextPageButton']"");
                    if (a && typeof __doPostBack === 'function') {
                        const m = /__doPostBack\('([^']+)'/.exec(a.getAttribute('href') || '');
                        if (m) { __doPostBack(m[1], ''); return true; }
                        a.click(); return true;
                    }
                    return false;
                }").ConfigureAwait(false);
        }
        catch (PlaywrightException ex)
        {
            _logger.LogDebug(ex, "Prospero: next-page invocation failed.");
        }

        if (advanced)
        {
            var settle = DateTime.UtcNow.AddMilliseconds(PostbackSettleMs);
            while (DateTime.UtcNow < settle)
            {
                ct.ThrowIfCancellationRequested();
                await page.WaitForTimeoutAsync(400).ConfigureAwait(false);
                var moved = await FirstFolderAsync(page).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(moved)
                    && !string.Equals(moved, firstFolderBefore, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        var locators = new[]
        {
            page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Next", Exact = false }),
            page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Next", Exact = false }),
        };

        foreach (var locator in locators)
        {
            try
            {
                if (await locator.CountAsync().ConfigureAwait(false) == 0)
                {
                    continue;
                }

                var next = locator.First;
                if (!await next.IsVisibleAsync().ConfigureAwait(false)
                    || !await next.IsEnabledAsync().ConfigureAwait(false))
                {
                    continue;
                }

                await next.ClickAsync(new LocatorClickOptions { Timeout = RowWaitTimeoutMs }).ConfigureAwait(false);

                var deadline = DateTime.UtcNow.AddMilliseconds(PostbackSettleMs);
                while (DateTime.UtcNow < deadline)
                {
                    ct.ThrowIfCancellationRequested();
                    await page.WaitForTimeoutAsync(400).ConfigureAwait(false);
                    var now = await FirstFolderAsync(page).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(now)
                        && !string.Equals(now, firstFolderBefore, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                // Clicked, and the list never moved. Treat as the end rather than
                // scraping the same page again for the rest of the page budget.
                return false;
            }
            catch (TimeoutException)
            {
                continue;
            }
        }

        return false;
    }

    private static async Task<string?> TextAsync(IElementHandle row, string selector)
    {
        var el = await row.QuerySelectorAsync(selector).ConfigureAwait(false);
        return el is null ? null : await el.InnerTextAsync().ConfigureAwait(false);
    }

    /// <summary>Reads "Application Date:  Feb 21, 2025" out of the row body.</summary>
    internal static DateTimeOffset? ParseApplicationDate(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var m = System.Text.RegularExpressions.Regex.Match(
            body,
            @"Application\s+Date:\s*([A-Za-z]{3,9}\s+\d{1,2},\s*\d{4})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success)
        {
            return null;
        }

        return DateTime.TryParse(
            m.Groups[1].Value,
            CultureInfo.GetCultureInfo("en-CA"),
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? new DateTimeOffset(parsed, TimeSpan.Zero)
            : null;
    }

    /// <summary>
    /// Details.aspx sits beside Search.aspx, so the detail url is derived from
    /// the configured search url — which is what makes this work for any Tempest
    /// host without another config key.
    /// </summary>
    internal static string DetailUrl(string searchUrl, string folderNumber)
    {
        var baseUri = new Uri(searchUrl);
        var dir = baseUri.GetLeftPart(UriPartial.Path);
        var lastSlash = dir.LastIndexOf('/');
        if (lastSlash > 0)
        {
            dir = dir[..lastSlash];
        }

        return $"{dir}/Details.aspx?folderNumber={Uri.EscapeDataString(folderNumber.Trim())}";
    }

    private static string? Collapse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var flat = System.Text.RegularExpressions.Regex.Replace(
            value.Replace('\r', ' ').Replace('\n', ' '), @"\s{2,}", " ").Trim();
        return flat.Length == 0 ? null : flat;
    }

    private static string? Cfg(IReadOnlyDictionary<string, string> cfg, string key)
        => cfg.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    private static int ResolveInt(IReadOnlyDictionary<string, string> cfg, string key, int dflt)
        => int.TryParse(Cfg(cfg, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0
            ? v
            : dflt;

    private static string? Trim(string? value, int max)
        => value is null ? null : (value.Length <= max ? value : value[..max]);
}
