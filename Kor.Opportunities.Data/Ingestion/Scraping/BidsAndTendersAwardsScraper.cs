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
/// Scrapes Bids&amp;Tenders Awarded status tabs into award candidates. The
/// platform renders the same Fuel UX row-pair shape as the open-opportunity
/// scraper: a data row followed by an action row with the detail link.
/// </summary>
public sealed class BidsAndTendersAwardsScraper : PlaywrightScraperBase<AwardCandidate>, IAwardProvider
{
    private const int DefaultMaxPages = 10;
    private const int PageWaitTimeoutMs = 30_000;
    private const int RowWaitTimeoutMs = 15_000;
    private const int AwardedClickTimeoutMs = 5_000;

    private static readonly Regex TimezoneSuffixRegex =
        new(@"\s*\([A-Z]{2,5}\)\s*$", RegexOptions.Compiled);

    private static readonly Regex VendorRegex =
        new(@"^[A-Z][A-Za-z &.,'-]{3,80}$", RegexOptions.Compiled);

    private static readonly Regex ContractValueRegex =
        new(@"\$[\d,]+(?:\.\d{2})?", RegexOptions.Compiled);

    public BidsAndTendersAwardsScraper(
        PlaywrightBrowserPool pool,
        ILogger<BidsAndTendersAwardsScraper> logger)
        : base(pool, logger)
    {
    }

    public override OpportunitySourceType SourceType => OpportunitySourceType.BidsAndTendersAwards;

    protected override async Task<IReadOnlyList<AwardCandidate>> ScrapeAsync(
        IPage page,
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        await page.GotoAsync(source.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = PageWaitTimeoutMs,
        }).ConfigureAwait(false);

        if (!await WaitForRowsAsync(page).ConfigureAwait(false))
        {
            await TryWriteDiagnosticAsync(page, "BidsAndTendersAwards-norows", ct).ConfigureAwait(false);
            return Array.Empty<AwardCandidate>();
        }

        if (await IsEmptyPlaceholderAsync(page).ConfigureAwait(false))
        {
            return Array.Empty<AwardCandidate>();
        }

        if (!await TrySelectAwardedAsync(page).ConfigureAwait(false))
        {
            await TryWriteDiagnosticAsync(page, "BidsAndTendersAwards-noclick", ct).ConfigureAwait(false);
            return Array.Empty<AwardCandidate>();
        }

        // Default page size is 25; bump to the max (100) so we get more of the
        // historical award set in fewer pagination clicks. Same Bootstrap-
        // dropdown pattern as the status filter:
        //   <button id="limit-results-btn" class="dropdown-toggle">25</button>
        //   <ul class="dropdown-menu">
        //     <li data-value="100"><a href="#">100</a></li>
        await TrySelectMaxPageSizeAsync(page).ConfigureAwait(false);

        var buyer = ResolveBuyer(source, sourceConfig);
        var candidates = new List<AwardCandidate>();
        var baseUri = new Uri(source.BaseUrl);
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
            await TryWriteDiagnosticAsync(page, "BidsAndTendersAwards-nocandidates", ct).ConfigureAwait(false);
        }

        return candidates;
    }

    private static string ResolveBuyer(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig)
    {
        return sourceConfig.TryGetValue("bidsandtenders.buyer", out var configuredBuyer)
            && !string.IsNullOrWhiteSpace(configuredBuyer)
            ? configuredBuyer.Trim()
            : source.Name;
    }

    private static async Task<bool> WaitForRowsAsync(IPage page)
    {
        try
        {
            await page.WaitForSelectorAsync("tbody tr", new PageWaitForSelectorOptions
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

    private static async Task<bool> IsEmptyPlaceholderAsync(IPage page)
    {
        return await page.QuerySelectorAsync("tbody tr.empty").ConfigureAwait(false) is not null;
    }

    private static async Task<bool> TrySelectAwardedAsync(IPage page)
    {
        // Bids&Tenders status filter is a Bootstrap dropdown at #searchfilter.
        // Markup (verified 2026-05-21 via diagnostic dump):
        //   <div id="searchfilter" class="btn-group selectlist repeater-filters">
        //     <button class="dropdown-toggle" data-toggle="dropdown">
        //       <span class="selected-label">Open</span>     <-- default
        //     <ul class="dropdown-menu">
        //       <li data-value="Awarded"><a href="#" aria-label="Awarded">Awarded</a></li>
        // The <li> items are hidden until the toggle button opens the menu, so
        // we must click the toggle first, then the option.
        try
        {
            var toggle = page.Locator("#searchfilter button.dropdown-toggle, #searchfilter [data-toggle='dropdown']").First;
            if (await toggle.CountAsync().ConfigureAwait(false) == 0)
            {
                return false;
            }
            await toggle.ClickAsync(new LocatorClickOptions { Timeout = AwardedClickTimeoutMs }).ConfigureAwait(false);

            var awardedItem = page.Locator("#searchfilter li[data-value='Awarded'] a").First;
            if (await awardedItem.CountAsync().ConfigureAwait(false) == 0)
            {
                return false;
            }
            await awardedItem.ClickAsync(new LocatorClickOptions { Timeout = AwardedClickTimeoutMs }).ConfigureAwait(false);

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
            {
                Timeout = PageWaitTimeoutMs,
            }).ConfigureAwait(false);

            return await WaitForAwardRowsAsync(page).ConfigureAwait(false);
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

    private static async Task TrySelectMaxPageSizeAsync(IPage page)
    {
        try
        {
            var toggle = page.Locator("#limit-results-btn").First;
            if (await toggle.CountAsync().ConfigureAwait(false) == 0)
            {
                return;
            }
            await toggle.ClickAsync(new LocatorClickOptions { Timeout = AwardedClickTimeoutMs }).ConfigureAwait(false);

            var opt = page.Locator("li[data-value='100'] a").First;
            if (await opt.CountAsync().ConfigureAwait(false) == 0)
            {
                return;
            }
            await opt.ClickAsync(new LocatorClickOptions { Timeout = AwardedClickTimeoutMs }).ConfigureAwait(false);

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
            {
                Timeout = PageWaitTimeoutMs,
            }).ConfigureAwait(false);

            // NetworkIdle is unreliable on the Angular SPA — the loader can
            // still be visible after NetworkIdle fires (verified in production
            // diagnostic 2026-05-21). Wait for the spinner to actually hide.
            try
            {
                await page.WaitForSelectorAsync(".repeater-loader", new PageWaitForSelectorOptions
                {
                    Timeout = RowWaitTimeoutMs,
                    State = WaitForSelectorState.Hidden,
                }).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Loader may not exist on some tenant skins — that's fine.
            }

            // Then confirm the new result set rendered.
            await WaitForAwardRowsAsync(page).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Best-effort — falls back to default page size if anything fails.
        }
        catch (PlaywrightException)
        {
            // Same fallback for selector/click variants.
        }
    }

    private static async Task<bool> WaitForAwardRowsAsync(IPage page)
    {
        try
        {
            await page.WaitForSelectorAsync("tbody tr td strong", new PageWaitForSelectorOptions
            {
                Timeout = RowWaitTimeoutMs,
                State = WaitForSelectorState.Attached,
            }).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return await IsEmptyPlaceholderAsync(page).ConfigureAwait(false);
        }
    }

    private static async Task<List<AwardCandidate>> ExtractPageAsync(
        IPage page,
        Uri baseUri,
        string buyer,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var rows = await page.QuerySelectorAllAsync("tbody > tr").ConfigureAwait(false);
        var result = new List<AwardCandidate>();

        for (var i = 0; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var strong = await rows[i].QuerySelectorAsync("td strong").ConfigureAwait(false);
            if (strong is null)
            {
                continue;
            }

            var bidName = NormalizeText(await strong.InnerTextAsync().ConfigureAwait(false));
            var cells = await rows[i].QuerySelectorAllAsync(":scope > td").ConfigureAwait(false);
            var cellTexts = new List<string>(cells.Count);
            foreach (var cell in cells)
            {
                cellTexts.Add(NormalizeText(await cell.InnerTextAsync().ConfigureAwait(false)));
            }

            string? sourceUrl = null;
            if (i + 1 < rows.Count)
            {
                var detailLink = await rows[i + 1]
                    .QuerySelectorAsync("a[href*='/Tender/Detail/']")
                    .ConfigureAwait(false);
                var href = detailLink is null
                    ? null
                    : (await detailLink.GetAttributeAsync("href").ConfigureAwait(false))?.Trim();
                if (!string.IsNullOrWhiteSpace(href) && Uri.TryCreate(baseUri, href, out var absolute))
                {
                    sourceUrl = new UriBuilder(absolute) { Fragment = string.Empty }.Uri.AbsoluteUri;
                }
                i++;
            }

            var (externalReference, title) = ParseBidName(bidName);
            if (string.IsNullOrWhiteSpace(externalReference)
                || string.IsNullOrWhiteSpace(title)
                || string.IsNullOrWhiteSpace(sourceUrl))
            {
                continue;
            }

            result.Add(new AwardCandidate
            {
                ExternalReference = externalReference,
                Title = title,
                SolicitationType = null,
                AwardingOrganization = buyer,
                AwardedToOrganization = FindAwardedToOrganization(cellTexts, bidName) ?? "Unknown",
                ContractValue = FindContractValue(cellTexts),
                ContractCurrency = "CAD",
                AwardedAtUtc = ParseClosingDate(cellTexts.Count > 2 ? cellTexts[2] : null),
                IssuingLocation = null,
                SupplierAddress = null,
                ContactEmail = null,
                ContractNumber = null,
                SourceUrl = sourceUrl,
                RawJson = string.Join("|", cellTexts),
            });
        }

        return result;
    }

    private static string? FindAwardedToOrganization(IReadOnlyList<string> cellTexts, string bidName)
    {
        var valueCellIndex = -1;
        for (var i = 0; i < cellTexts.Count; i++)
        {
            if (ContractValueRegex.IsMatch(cellTexts[i]))
            {
                valueCellIndex = i;
                break;
            }
        }

        var candidates = new List<(int Index, string Text)>();
        for (var i = 0; i < cellTexts.Count; i++)
        {
            var text = cellTexts[i];
            if (!VendorRegex.IsMatch(text)
                || string.Equals(text, bidName, StringComparison.OrdinalIgnoreCase)
                || IsStatusText(text)
                || ParseClosingDate(text) is not null)
            {
                continue;
            }

            candidates.Add((i, text));
        }

        if (valueCellIndex >= 0)
        {
            var beforeValue = candidates
                .Where(candidate => candidate.Index < valueCellIndex)
                .OrderByDescending(candidate => candidate.Index)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(beforeValue.Text))
            {
                return beforeValue.Text;
            }
        }

        return candidates.Count == 0 ? null : candidates[0].Text;
    }

    private static decimal? FindContractValue(IReadOnlyList<string> cellTexts)
    {
        foreach (var text in cellTexts)
        {
            var match = ContractValueRegex.Match(text);
            if (!match.Success)
            {
                continue;
            }

            var cleaned = match.Value.Replace("$", "").Replace(",", "").Trim();
            if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static (string? ExternalReference, string Title) ParseBidName(string bidName)
    {
        var separator = bidName.IndexOf(" - ", StringComparison.Ordinal);
        if (separator > 0 && separator + 3 < bidName.Length)
        {
            var externalReference = bidName[..separator].Trim();
            var title = bidName[(separator + 3)..].Trim();
            return (string.IsNullOrWhiteSpace(externalReference) ? null : externalReference, title);
        }

        return (null, bidName);
    }

    private static DateTimeOffset? ParseClosingDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var clean = TimezoneSuffixRegex.Replace(text, string.Empty).Trim();
        return DateTimeOffset.TryParse(
            clean,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static bool IsStatusText(string text)
    {
        return string.Equals(text, "All", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Planned", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Open", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Closed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Unofficial Results", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Awarded", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Cancelled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Terminated", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Archived", StringComparison.OrdinalIgnoreCase);
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

    private static async Task TryWriteDiagnosticAsync(IPage page, string stem, CancellationToken ct)
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
                Path = Path.Combine(diagnosticsDir, $"{stem}-{stamp}.png"),
                FullPage = true,
            }).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(diagnosticsDir, $"{stem}-{stamp}.url.txt"),
                page.Url,
                ct).ConfigureAwait(false);
            var html = await page.ContentAsync().ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(diagnosticsDir, $"{stem}-{stamp}.html"),
                html,
                ct).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort diagnostic only.
        }
    }
}
