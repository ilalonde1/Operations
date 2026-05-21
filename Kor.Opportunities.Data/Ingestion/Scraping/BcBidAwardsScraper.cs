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
/// Scrapes BC Bid's Contract Awards tab. Login + nav pattern mirrors
/// BcBidScraper exactly; only the side-nav target and column-mapping differ.
/// </summary>
public sealed class BcBidAwardsScraper : PlaywrightScraperBase<AwardCandidate>, IAwardProvider
{
    private const int DefaultMaxPages = 10;
    private const int PageWaitTimeoutMs = 30_000;
    private const string LoginEntryUrl = "https://bcbid.gov.bc.ca/page.aspx/en/buy/homepage";

    private readonly BcBidCredentials _credentials;

    public BcBidAwardsScraper(
        PlaywrightBrowserPool pool,
        ILogger<BcBidAwardsScraper> logger,
        BcBidCredentials credentials)
        : base(pool, logger)
    {
        _credentials = credentials;
    }

    public override OpportunitySourceType SourceType => OpportunitySourceType.BcBidAwards;

    protected override async Task<IReadOnlyList<AwardCandidate>> ScrapeAsync(
        IPage page,
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        if (!_credentials.IsConfigured)
        {
            return Array.Empty<AwardCandidate>();
        }

        await LoginAsync(page, ct).ConfigureAwait(false);

        // Direct GET to the Contract Awards URL with the authenticated session.
        // (No "Contract Awards" nav link exists in the supplier dashboard;
        // user navigated this URL directly while logged in and got data.)
        await page.GotoAsync(source.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = PageWaitTimeoutMs,
        }).ConfigureAwait(false);

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

        try
        {
            await page.WaitForSelectorAsync("tr[id^='body_x_grid_grd_tr_']",
                new PageWaitForSelectorOptions
                {
                    Timeout = PageWaitTimeoutMs,
                    State = WaitForSelectorState.Attached,
                }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Diagnostic dump so we can see what the page renders.
            try
            {
                var diagDir = System.IO.Path.Combine(
                    Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData",
                    "KorOperations", "Opportunities", "diagnostics");
                System.IO.Directory.CreateDirectory(diagDir);
                var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = System.IO.Path.Combine(diagDir, $"BcBidAwards-norows-{stamp}.png"),
                    FullPage = true,
                }).ConfigureAwait(false);
                await System.IO.File.WriteAllTextAsync(
                    System.IO.Path.Combine(diagDir, $"BcBidAwards-norows-{stamp}.url.txt"),
                    page.Url, ct).ConfigureAwait(false);
            }
            catch { }
            return Array.Empty<AwardCandidate>();
        }

        var baseUri = new Uri(source.BaseUrl);
        var candidates = new List<AwardCandidate>();
        var maxPages = ResolveInt(sourceConfig, "playwright.maxPages", DefaultMaxPages);

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

    private static async Task<List<AwardCandidate>> ExtractPageAsync(
        IPage page,
        Uri baseUri,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var rows = await page.QuerySelectorAllAsync("tr[id^='body_x_grid_grd_tr_']").ConfigureAwait(false);
        var result = new List<AwardCandidate>(rows.Count);
        foreach (var row in rows)
        {
            var candidate = await TryMapAwardRowAsync(row, baseUri).ConfigureAwait(false);
            if (candidate is not null)
            {
                result.Add(candidate);
            }
        }
        return result;
    }

    /// <summary>
    /// Column order on BC Bid's Contract Awards grid (1-indexed visually):
    ///   1 OpportunityID | 2 OpportunityDescription | 3 OpportunityType
    ///   4 IssuingOrganization | 5 IssuingLocation | 6 ContractNumber
    ///   7 ContactEmail | 8 ContractValue | 9 SuccessfulSupplier
    ///   10 SupplierAddress | 11 AwardDate
    /// </summary>
    private static async Task<AwardCandidate?> TryMapAwardRowAsync(IElementHandle row, Uri baseUri)
    {
        var cells = await row.QuerySelectorAllAsync(":scope > td").ConfigureAwait(false);
        if (cells.Count < 11) return null;

        var externalRef = (await row.GetAttributeAsync("data-id").ConfigureAwait(false))?.Trim();
        if (string.IsNullOrWhiteSpace(externalRef))
        {
            externalRef = (await cells[0].InnerTextAsync().ConfigureAwait(false)).Trim();
        }
        if (string.IsNullOrWhiteSpace(externalRef)) return null;

        var title = (await cells[1].InnerTextAsync().ConfigureAwait(false)).Trim();
        if (string.IsNullOrWhiteSpace(title)) return null;

        var anchor = await row.QuerySelectorAsync("a[href]").ConfigureAwait(false);
        var href = anchor is null ? null : (await anchor.GetAttributeAsync("href").ConfigureAwait(false))?.Trim();
        var url = string.IsNullOrWhiteSpace(href)
            ? baseUri.AbsoluteUri
            : new Uri(baseUri, href).AbsoluteUri;

        var solicitationType = (await cells[2].InnerTextAsync().ConfigureAwait(false)).Trim();
        var issuingOrg = (await cells[3].InnerTextAsync().ConfigureAwait(false)).Trim();
        var issuingLoc = (await cells[4].InnerTextAsync().ConfigureAwait(false)).Trim();
        var contractNumber = (await cells[5].InnerTextAsync().ConfigureAwait(false)).Trim();
        var contactEmail = (await cells[6].InnerTextAsync().ConfigureAwait(false)).Trim();

        var valueText = (await cells[7].InnerTextAsync().ConfigureAwait(false)).Trim();
        var contractValue = ParseDecimal(valueText);

        var successfulSupplier = (await cells[8].InnerTextAsync().ConfigureAwait(false)).Trim();
        var supplierAddress = (await cells[9].InnerTextAsync().ConfigureAwait(false)).Trim();
        var awardDateText = (await cells[10].InnerTextAsync().ConfigureAwait(false)).Trim();
        var awardedAt = ParseDate(awardDateText);

        return new AwardCandidate
        {
            ExternalReference = externalRef,
            Title = title,
            SolicitationType = string.IsNullOrEmpty(solicitationType) ? null : solicitationType,
            AwardingOrganization = string.IsNullOrEmpty(issuingOrg) ? "Unknown" : issuingOrg,
            AwardedToOrganization = string.IsNullOrEmpty(successfulSupplier) ? "Unknown" : successfulSupplier,
            ContractValue = contractValue,
            ContractCurrency = "CAD",
            AwardedAtUtc = awardedAt,
            IssuingLocation = string.IsNullOrEmpty(issuingLoc) ? null : issuingLoc,
            SupplierAddress = string.IsNullOrEmpty(supplierAddress) ? null : supplierAddress,
            ContactEmail = string.IsNullOrEmpty(contactEmail) ? null : contactEmail,
            ContractNumber = string.IsNullOrEmpty(contractNumber) ? null : contractNumber,
            SourceUrl = url,
            RawJson = null,
        };
    }

    private static decimal? ParseDecimal(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleaned = raw.Replace("$", "").Replace(",", "").Trim();
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static DateTimeOffset? ParseDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value
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

    private async Task<bool> LoginAsync(IPage page, CancellationToken ct)
    {
        if (!_credentials.IsConfigured) return false;

        await page.GotoAsync(LoginEntryUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = PageWaitTimeoutMs,
        }).ConfigureAwait(false);

        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Login", Exact = true })
            .First.ClickAsync(new LocatorClickOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);

        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions
        {
            NameRegex = new System.Text.RegularExpressions.Regex(
                @"Business\s+or\s+Basic\s+BCeID",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase),
        }).First.ClickAsync(new LocatorClickOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);

        await page.WaitForURLAsync(
            url => url.Contains("logon.gov.bc.ca", StringComparison.OrdinalIgnoreCase)
                || url.Contains("bceid", StringComparison.OrdinalIgnoreCase),
            new PageWaitForURLOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);

        await page.Locator("input[type='text']:visible").First
            .FillAsync(_credentials.Username, new LocatorFillOptions { Timeout = PageWaitTimeoutMs })
            .ConfigureAwait(false);
        await page.Locator("input[type='password']:visible").First
            .FillAsync(_credentials.Password, new LocatorFillOptions { Timeout = PageWaitTimeoutMs })
            .ConfigureAwait(false);

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Continue" })
            .First.ClickAsync(new LocatorClickOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);

        await page.WaitForURLAsync(
            url => url.Contains("bcbid.gov.bc.ca", StringComparison.OrdinalIgnoreCase)
                && !url.Contains("logon", StringComparison.OrdinalIgnoreCase),
            new PageWaitForURLOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);

        try
        {
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                new PageWaitForLoadStateOptions { Timeout = 10_000 }).ConfigureAwait(false);
        }
        catch { }

        return true;
    }
}
