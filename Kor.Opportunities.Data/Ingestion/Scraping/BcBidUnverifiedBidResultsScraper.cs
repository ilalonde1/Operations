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
using Kor.Opportunities.Data.Bids;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Ingestion.Scraping;

/// <summary>
/// Scrapes BC Bid's Unverified Bid Results tab as pre-award competitive
/// intelligence. Bidder identities and prices remain detail-page enrichment.
/// </summary>
public sealed class BcBidUnverifiedBidResultsScraper
    : PlaywrightScraperBase<AwardCandidate>, IAwardProvider
{
    private const int DefaultMaxPages = 50;
    private const int PageWaitTimeoutMs = 30_000;
    private const string LoginEntryUrl = "https://bcbid.gov.bc.ca/page.aspx/en/buy/homepage";

    private static readonly Regex TimezoneSuffixRegex = new(
        @"\s*\((?:PDT|PST)\)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // BC Bid renders amounts with a SPACE after $ — "$ 1,929,626.00" — so the
    // regex must allow optional whitespace between $ and the digits.
    private static readonly Regex BidAmountRegex = new(
        @"\$\s*[\d,]+(?:\.\d{2})?",
        RegexOptions.Compiled);

    private static readonly Regex AddressHintRegex = new(
        @"(?:\b[A-Z]\d[A-Z]\s?\d[A-Z]\d\b|\b(?:Alberta|British\s+Columbia|Manitoba|New\s+Brunswick|Newfoundland|Nova\s+Scotia|Ontario|Prince\s+Edward\s+Island|Quebec|Saskatchewan|Yukon|Northwest\s+Territories|Nunavut)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RankRegex = new(
        @"^\s*(?<rank>\d{1,2})\s*$",
        RegexOptions.Compiled);

    private readonly BcBidCredentials _credentials;
    private readonly IOpportunityBidStore? _bidStore;
    private readonly ILogger<BcBidUnverifiedBidResultsScraper> _logger;

    public BcBidUnverifiedBidResultsScraper(
        PlaywrightBrowserPool pool,
        ILogger<BcBidUnverifiedBidResultsScraper> logger,
        BcBidCredentials credentials)
        : this(pool, logger, credentials, bidStore: null)
    {
    }

    public BcBidUnverifiedBidResultsScraper(
        PlaywrightBrowserPool pool,
        ILogger<BcBidUnverifiedBidResultsScraper> logger,
        BcBidCredentials credentials,
        IOpportunityBidStore? bidStore)
        : base(pool, logger)
    {
        _credentials = credentials;
        _bidStore = bidStore;
        _logger = logger;
    }

    public override OpportunitySourceType SourceType => OpportunitySourceType.BcBidUnverifiedBidResults;

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

        await page.GotoAsync(source.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = PageWaitTimeoutMs,
        }).ConfigureAwait(false);

        var openingDateMin = sourceConfig.TryGetValue("playwright.openingDateMinFrom", out var configuredMin)
            && !string.IsNullOrWhiteSpace(configuredMin)
            ? configuredMin
            : "2020-01-01";
        try
        {
            // Unverified Bid Results has ONE Min/Max pair only — the
            // "Unverified Bid Results Publish Date" filter. (Contract Awards
            // has TWO pairs and uses Nth(1); copy-paste from that scraper
            // missed this distinction — verified via diagnostic 2026-05-21.)
            // The page returns "0 Record(s) — please define at least one
            // filter criteria" until at least one filter is applied, so
            // filling this is required to surface rows.
            await page.Locator("input[placeholder='Min value']").Nth(0)
                .FillAsync(openingDateMin, new LocatorFillOptions { Timeout = PageWaitTimeoutMs })
                .ConfigureAwait(false);
        }
        catch (TimeoutException) { }

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
            await page.WaitForSelectorAsync("tr[id*='_grd_tr_']",
                new PageWaitForSelectorOptions
                {
                    Timeout = PageWaitTimeoutMs,
                    State = WaitForSelectorState.Attached,
                }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await TryWriteDiagnosticAsync(page, "BcBidUnverified-norows", ct).ConfigureAwait(false);
            return Array.Empty<AwardCandidate>();
        }

        var baseUri = new Uri(source.BaseUrl);
        var candidates = new List<AwardCandidate>();
        var maxPages = ResolveInt(sourceConfig, "playwright.maxPages", DefaultMaxPages);

        for (var pageNum = 1; pageNum <= maxPages; pageNum++)
        {
            ct.ThrowIfCancellationRequested();

            var pageCandidates = await ExtractPageAsync(page, baseUri, ct).ConfigureAwait(false);
            candidates.AddRange(pageCandidates);

            if (!await TryAdvanceToNextPageAsync(page, ct).ConfigureAwait(false))
            {
                break;
            }
        }

        if (candidates.Count == 0)
        {
            await TryWriteDiagnosticAsync(page, "BcBidUnverified-nocandidates", ct).ConfigureAwait(false);
        }
        else
        {
            await EnrichBidDetailsAsync(page, source, sourceConfig, candidates, ct).ConfigureAwait(false);
        }

        return candidates;
    }

    private async Task EnrichBidDetailsAsync(
        IPage page,
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        IReadOnlyList<AwardCandidate> candidates,
        CancellationToken ct)
    {
        if (_bidStore is null)
        {
            _logger.LogWarning(
                "BC Bid Unverified Stage 2 skipped for {SourceName}: no IOpportunityBidStore is available.",
                source.Name);
            return;
        }

        var forceRescrape = sourceConfig.TryGetValue("bcbid.unverified.forceRescrape", out var forceValue)
            && string.Equals(forceValue, "true", StringComparison.OrdinalIgnoreCase);
        var maxDetailLookups = Math.Max(
            0,
            ResolveInt(sourceConfig, "bcbid.unverified.maxDetailLookups", 300));

        var enriched = 0;
        var skippedAlreadyEnriched = 0;
        var failed = 0;
        var detailLookups = 0;
        var firstDetailLookup = true;

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(candidate.ExternalReference))
            {
                continue;
            }

            try
            {
                if (!forceRescrape)
                {
                    var existingBidderCount = await _bidStore
                        .ListBidderCountForAsync(source.Id, candidate.ExternalReference, ct)
                        .ConfigureAwait(false);
                    if (existingBidderCount > 0)
                    {
                        skippedAlreadyEnriched++;
                        _logger.LogDebug(
                            "BC Bid Unverified Stage 2 skip {ExternalReference}: already enriched.",
                            candidate.ExternalReference);
                        continue;
                    }
                }

                if (detailLookups >= maxDetailLookups)
                {
                    _logger.LogInformation(
                        "BC Bid Unverified Stage 2 detail lookup cap {MaxDetailLookups} reached for {SourceName}.",
                        maxDetailLookups,
                        source.Name);
                    break;
                }

                detailLookups++;
                var stored = await TryEnrichBidDetailAsync(
                        page,
                        source,
                        candidate,
                        dumpFirstDetailPage: firstDetailLookup,
                        ct: ct)
                    .ConfigureAwait(false);
                firstDetailLookup = false;

                if (stored > 0)
                {
                    enriched++;
                }
                else
                {
                    failed++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(
                    ex,
                    "BC Bid Unverified Stage 2 failed for {ExternalReference}.",
                    candidate.ExternalReference);
            }
        }

        _logger.LogInformation(
            "Stage 2 enriched {Enriched} of {Total} candidates ({SkippedAlreadyEnriched} skipped, {Failed} failed).",
            enriched,
            candidates.Count,
            skippedAlreadyEnriched,
            failed);
    }

    private async Task<int> TryEnrichBidDetailAsync(
        IPage page,
        OpportunitySource source,
        AwardCandidate candidate,
        bool dumpFirstDetailPage,
        CancellationToken ct)
    {
        await page.GotoAsync(source.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = PageWaitTimeoutMs,
        }).ConfigureAwait(false);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
            new PageWaitForLoadStateOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);

        if (!await TryFillOpportunityIdAsync(page, candidate.ExternalReference).ConfigureAwait(false))
        {
            _logger.LogWarning(
                "BC Bid Unverified Stage 2 could not find Opportunity ID input for {ExternalReference}.",
                candidate.ExternalReference);
            await TryWriteDiagnosticAsync(page, "BcBidUnverifiedDetail-noinput", ct).ConfigureAwait(false);
            return 0;
        }

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
        catch (TimeoutException ex)
        {
            _logger.LogWarning(
                ex,
                "BC Bid Unverified Stage 2 search timed out for {ExternalReference}.",
                candidate.ExternalReference);
            return 0;
        }

        if (dumpFirstDetailPage)
        {
            await TryWriteDiagnosticAsync(page, "BcBidUnverifiedDetail-firstpage", ct).ConfigureAwait(false);
        }

        try
        {
            await page.WaitForSelectorAsync("tr[id*='_grd_tr_']",
                new PageWaitForSelectorOptions
                {
                    Timeout = PageWaitTimeoutMs,
                    State = WaitForSelectorState.Attached,
                }).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(
                ex,
                "BC Bid Unverified Stage 2 returned no bid rows for {ExternalReference}.",
                candidate.ExternalReference);
            return 0;
        }

        var baseUri = new Uri(source.BaseUrl);
        var rows = await page.QuerySelectorAllAsync("tr[id*='_grd_tr_']").ConfigureAwait(false);
        var stored = 0;
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            var bid = await TryMapBidRowAsync(row, source, candidate.ExternalReference, baseUri)
                .ConfigureAwait(false);
            if (bid is null)
            {
                continue;
            }

            await _bidStore!.UpsertAsync(bid, ct).ConfigureAwait(false);
            stored++;
        }

        if (stored == 0)
        {
            _logger.LogWarning(
                "BC Bid Unverified Stage 2 parsed no bidder rows for {ExternalReference}.",
                candidate.ExternalReference);
        }

        return stored;
    }

    private static async Task<bool> TryFillOpportunityIdAsync(IPage page, string externalReference)
    {
        var locators = new[]
        {
            page.GetByLabel("Opportunity ID"),
            page.Locator("input[name*='OpportunityId' i]"),
            page.Locator("input[id*='opp' i][id*='id' i]"),
        };

        foreach (var locator in locators)
        {
            try
            {
                if (await locator.CountAsync().ConfigureAwait(false) == 0)
                {
                    continue;
                }

                await locator.First.FillAsync(externalReference, new LocatorFillOptions
                {
                    Timeout = PageWaitTimeoutMs,
                }).ConfigureAwait(false);
                return true;
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

        return false;
    }

    private static async Task<OpportunityBid?> TryMapBidRowAsync(
        IElementHandle row,
        OpportunitySource source,
        string externalReference,
        Uri baseUri)
    {
        var cells = await row.QuerySelectorAllAsync(":scope > td").ConfigureAwait(false);
        if (cells.Count == 0)
        {
            return null;
        }

        var cellTexts = new List<string>(cells.Count);
        foreach (var cell in cells)
        {
            cellTexts.Add((await cell.InnerTextAsync().ConfigureAwait(false)).Trim());
        }

        // Stage 2 detail-view column layout (verified 2026-05-21 via diagnostic):
        //   0 Opportunity ID
        //   1 Opportunity Description
        //   2 Issuing Organization
        //   3 Closing Date and Time (Pacific Time)
        //   4 Opening Date and Time (Pacific Time)
        //   5 Supplier Location           <-- bidder address
        //   6 Supplier Name               <-- BidderName
        //   7 Bid amount/rank             <-- $ + optional rank
        // When the page is still in Stage 1 list mode (e.g. detail filter
        // didn't transition), rows have only 5 cells — skip those.
        string? bidderName;
        string? bidderAddress;
        decimal? bidAmount;
        int? bidderRank;
        if (cellTexts.Count >= 8)
        {
            bidderAddress = string.IsNullOrWhiteSpace(cellTexts[5]) ? null : cellTexts[5];
            bidderName    = string.IsNullOrWhiteSpace(cellTexts[6]) ? null : cellTexts[6];
            bidAmount     = ParseBidAmount(cellTexts[7]);
            bidderRank    = ParseRankFromCombined(cellTexts[7]);
        }
        else
        {
            // Fallback heuristic for non-8-column rows (defensive — e.g. layout drift).
            bidderName    = FindBidderName(cellTexts, externalReference);
            bidderAddress = FindBidderAddress(cellTexts);
            bidAmount     = FindBidAmount(cellTexts);
            bidderRank    = FindBidderRank(cellTexts);
        }

        if (string.IsNullOrWhiteSpace(bidderName))
        {
            return null;
        }

        var anchor = await row.QuerySelectorAsync("a[href]").ConfigureAwait(false);
        var href = anchor is null
            ? null
            : (await anchor.GetAttributeAsync("href").ConfigureAwait(false))?.Trim();
        var sourceUrl = string.IsNullOrWhiteSpace(href)
            ? baseUri.AbsoluteUri
            : new Uri(baseUri, href).AbsoluteUri;

        return new OpportunityBid
        {
            OpportunitySourceId = source.Id,
            ExternalReference = externalReference,
            BidderName = bidderName,
            BidAmount = bidAmount,
            BidCurrency = "CAD",
            BidderRank = bidderRank,
            BidderAddress = bidderAddress,
            SourceUrl = sourceUrl,
            RawJson = string.Join("|", cellTexts),
        };
    }

    private static decimal? ParseBidAmount(string cell)
    {
        var match = BidAmountRegex.Match(cell);
        if (!match.Success) return null;
        var cleaned = match.Value.Replace("$", "").Replace(",", "").Replace(" ", "").Trim();
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : (decimal?)null;
    }

    // The "Bid amount/rank" column sometimes contains "$ 1,929,626.00 (1)" or
    // "$ 1,929,626.00  1" with a trailing rank number. Extract the integer
    // that's NOT part of the dollar amount.
    private static int? ParseRankFromCombined(string cell)
    {
        var amountMatch = BidAmountRegex.Match(cell);
        var afterAmount = amountMatch.Success
            ? cell[(amountMatch.Index + amountMatch.Length)..]
            : cell;
        var rankMatch = Regex.Match(afterAmount, @"\b(?<rank>\d{1,2})\b");
        if (!rankMatch.Success) return null;
        return int.TryParse(rankMatch.Groups["rank"].Value, out var rank) && rank is >= 1 and <= 99
            ? rank
            : (int?)null;
    }

    private static string? FindBidderName(IReadOnlyList<string> cellTexts, string externalReference)
    {
        foreach (var text in cellTexts)
        {
            var candidate = text.Trim();
            if (candidate.Length < 4
                || string.Equals(candidate, externalReference, StringComparison.OrdinalIgnoreCase)
                || BidAmountRegex.IsMatch(candidate)
                || AddressHintRegex.IsMatch(candidate)
                || FindBidderRank(new[] { candidate }) is not null
                || ParseDate(candidate) is not null)
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private static decimal? FindBidAmount(IReadOnlyList<string> cellTexts)
    {
        foreach (var text in cellTexts)
        {
            var match = BidAmountRegex.Match(text);
            if (!match.Success)
            {
                continue;
            }

            var cleaned = match.Value.Replace("$", "").Replace(",", "").Trim();
            if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                return amount;
            }
        }

        return null;
    }

    private static int? FindBidderRank(IReadOnlyList<string> cellTexts)
    {
        foreach (var text in cellTexts)
        {
            var match = RankRegex.Match(text);
            if (!match.Success
                || !int.TryParse(match.Groups["rank"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rank)
                || rank is < 1 or > 99)
            {
                continue;
            }

            return rank;
        }

        return null;
    }

    private static string? FindBidderAddress(IReadOnlyList<string> cellTexts)
    {
        foreach (var text in cellTexts)
        {
            if (AddressHintRegex.IsMatch(text))
            {
                return text.Trim();
            }
        }

        return null;
    }

    private static async Task<List<AwardCandidate>> ExtractPageAsync(
        IPage page,
        Uri baseUri,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var rows = await page.QuerySelectorAllAsync("tr[id*='_grd_tr_']").ConfigureAwait(false);
        var result = new List<AwardCandidate>(rows.Count);
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            var candidate = await TryMapRowAsync(row, baseUri).ConfigureAwait(false);
            if (candidate is not null)
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    private static async Task<AwardCandidate?> TryMapRowAsync(IElementHandle row, Uri baseUri)
    {
        var cells = await row.QuerySelectorAllAsync(":scope > td").ConfigureAwait(false);
        if (cells.Count < 4)
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

        var anchor = await row.QuerySelectorAsync("a[href]").ConfigureAwait(false);
        var href = anchor is null
            ? null
            : (await anchor.GetAttributeAsync("href").ConfigureAwait(false))?.Trim();
        var sourceUrl = string.IsNullOrWhiteSpace(href)
            ? baseUri.AbsoluteUri
            : new Uri(baseUri, href).AbsoluteUri;

        var issuingOrganization = cellTexts.Count > 2 ? cellTexts[2] : null;
        var openingDate = cellTexts.Count > 4 ? cellTexts[4] : null;

        return new AwardCandidate
        {
            ExternalReference = externalReference,
            Title = title,
            SolicitationType = null,
            AwardingOrganization = string.IsNullOrWhiteSpace(issuingOrganization)
                ? "Unknown"
                : issuingOrganization,
            AwardedToOrganization = "Pending - Bid Opened",
            ContractValue = null,
            ContractCurrency = "CAD",
            AwardedAtUtc = ParseDate(openingDate),
            IssuingLocation = null,
            SupplierAddress = null,
            ContactEmail = null,
            ContractNumber = null,
            SourceUrl = sourceUrl,
            RawJson = string.Join("|", cellTexts),
        };
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

            // BCeID's logon page (sfs7.gov.bc.ca) doesn't use <label for> - labels
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
