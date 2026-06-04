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
using Kor.Opportunities.Data.Awards;
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
        @"(?:\b[A-Z]\d[A-Z]\s?\d[A-Z]\d\b|\b(?:Alberta|British\s+Columbia|Manitoba|New\s+Brunswick|Newfoundland|Nova\s+Scotia|Ontario|Prince\s+Edward\s+Island|Quebec|Saskatchewan|Yukon|Northwest\s+Territories|Nunavut)\b|,\s*(?:AB|BC|MB|NB|NL|NS|NT|NU|ON|PE|QC|SK|YT)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BidderPlaceholderRegex = new(
        @"^(?:not publicly disclosed|n/a|tba|tbd|unknown)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CompanyKeywordRegex = new(
        @"\b(?:CORP(?:ORATION)?|LTD|LIMITED|INC(?:ORPORATED)?|LLP|LLC|GROUP|COMPANY|CO|CONTRACTING|CONSTRUCTION|CONSULTANTS?|CONSULTING|ENGINEERING|SERVICES|HOLDINGS|ENTERPRISES|RESOURCES|SOLUTIONS|SYSTEMS|INDUSTRIES|TECHNOLOGIES|LANDSCAPING|EXCAVATING|EQUIPMENT|MECHANICAL|ELECTRICAL|ROOFING|PAVING|PLUMBING|HVAC)\.?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RankRegex = new(
        @"^\s*(?<rank>\d{1,2})\s*$",
        RegexOptions.Compiled);

    private readonly BcBidCredentials _credentials;
    private readonly IOpportunityBidStore? _bidStore;
    private readonly CanonicalOrgResolver? _canonicalResolver;
    private readonly ILogger<BcBidUnverifiedBidResultsScraper> _logger;

    public BcBidUnverifiedBidResultsScraper(
        PlaywrightBrowserPool pool,
        ILogger<BcBidUnverifiedBidResultsScraper> logger,
        BcBidCredentials credentials)
        : this(pool, logger, credentials, bidStore: null, canonicalResolver: null)
    {
    }

    public BcBidUnverifiedBidResultsScraper(
        PlaywrightBrowserPool pool,
        ILogger<BcBidUnverifiedBidResultsScraper> logger,
        BcBidCredentials credentials,
        IOpportunityBidStore? bidStore)
        : this(pool, logger, credentials, bidStore, canonicalResolver: null)
    {
    }

    public BcBidUnverifiedBidResultsScraper(
        PlaywrightBrowserPool pool,
        ILogger<BcBidUnverifiedBidResultsScraper> logger,
        BcBidCredentials credentials,
        IOpportunityBidStore? bidStore,
        CanonicalOrgResolver? canonicalResolver)
        : base(pool, logger)
    {
        _credentials = credentials;
        _bidStore = bidStore;
        _canonicalResolver = canonicalResolver;
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

            var bid = await TryMapBidRowAsync(row, source, candidate.ExternalReference, baseUri, ct)
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

    private async Task<OpportunityBid?> TryMapBidRowAsync(
        IElementHandle row,
        OpportunitySource source,
        string externalReference,
        Uri baseUri,
        CancellationToken ct)
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

        var anchor = await row.QuerySelectorAsync("a[href]").ConfigureAwait(false);
        var href = anchor is null
            ? null
            : (await anchor.GetAttributeAsync("href").ConfigureAwait(false))?.Trim();
        var sourceUrl = string.IsNullOrWhiteSpace(href)
            ? baseUri.AbsoluteUri
            : new Uri(baseUri, href).AbsoluteUri;

        return await CreateOpportunityBidAsync(
                cellTexts,
                source,
                externalReference,
                sourceUrl,
                _canonicalResolver,
                _logger,
                ct)
            .ConfigureAwait(false);
    }

    internal static async Task<OpportunityBid?> CreateOpportunityBidAsync(
        IReadOnlyList<string> cellTexts,
        OpportunitySource source,
        string externalReference,
        string sourceUrl,
        CanonicalOrgResolver? canonicalResolver,
        ILogger? logger,
        CancellationToken ct)
    {
        var mapping = MapBidCells(cellTexts, externalReference);
        var bidderName = mapping.BidderName;

        if (string.IsNullOrWhiteSpace(bidderName))
        {
            return null;
        }

        var bidderCanonicalOrgId = await ResolveBidderCanonicalOrgIdAsync(
                canonicalResolver,
                logger,
                bidderName,
                externalReference,
                ct)
            .ConfigureAwait(false);

        return new OpportunityBid
        {
            OpportunitySourceId = source.Id,
            ExternalReference = externalReference,
            BidderName = bidderName,
            BidderCanonicalOrgId = bidderCanonicalOrgId,
            BidAmount = mapping.BidAmount,
            BidCurrency = "CAD",
            BidderRank = mapping.BidderRank,
            BidderAddress = mapping.BidderAddress,
            SourceUrl = sourceUrl,
            RawJson = string.Join("|", cellTexts),
        };
    }

    private static async Task<long?> ResolveBidderCanonicalOrgIdAsync(
        CanonicalOrgResolver? canonicalResolver,
        ILogger? logger,
        string bidderName,
        string externalReference,
        CancellationToken ct)
    {
        if (canonicalResolver is null)
        {
            return null;
        }

        try
        {
            return await canonicalResolver.ResolveAsync(
                    bidderName,
                    OrgKinds.Vendor,
                    "BcBidUnverified.Bidder",
                    ct,
                    allowCreate: true,
                    minConfidenceForCreate: 70)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(
                ex,
                "BC Bid Unverified bidder canonical resolution failed for {ExternalReference} bidder {BidderName}.",
                externalReference,
                bidderName);
            return null;
        }
    }

    internal static BidRowMapping MapBidCells(IReadOnlyList<string> cellTexts, string externalReference)
    {
        var issuingOrganization = cellTexts.Count > 2 ? cellTexts[2] : null;
        var bidderCells = GetBidderDetailCells(cellTexts);

        // BC Timber Sales has shipped at least three Stage 2 bidder layouts:
        //   A: 5 Supplier Location, 6 Supplier Name,     7 Amount
        //   B: 5 Supplier Name,     6 Supplier Location, 7 Amount
        //   C: 5 Amount,            6 Supplier Location, 7 Supplier Name
        // Content-driven extraction is safer than trusting these positions:
        // the stable signal is the cell content (currency, location text,
        // company-name terms), not the supplier/name/location ordering.
        // Reference: DQ audit 2026-06-03 + SQL re-parse at
        // C:\Users\ilalonde\AppData\Local\Temp\fix_bids.ps1 produced 55.5%
        // canonical-resolution rate (was 5.2%); this scraper rewrite should
        // preserve that bar going forward.
        var amountCell = FindBidAmountCell(bidderCells);
        var bidAmount = amountCell is null ? null : ParseBidAmount(amountCell);
        var bidderRank = amountCell is null
            ? FindBidderRank(bidderCells)
            : ParseRankFromCombined(amountCell);

        return new BidRowMapping(
            FindBidderName(bidderCells, externalReference, issuingOrganization),
            FindBidderAddress(bidderCells),
            bidAmount,
            bidderRank);
    }

    internal readonly record struct BidRowMapping(
        string? BidderName,
        string? BidderAddress,
        decimal? BidAmount,
        int? BidderRank);

    private static List<string> GetBidderDetailCells(IReadOnlyList<string> cellTexts)
    {
        var start = cellTexts.Count >= 8 ? 5 : 0;
        var bidderCells = new List<string>(Math.Max(0, cellTexts.Count - start));
        for (var i = start; i < cellTexts.Count; i++)
        {
            bidderCells.Add(cellTexts[i]);
        }

        return bidderCells;
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

    private static string? FindBidderName(
        IReadOnlyList<string> cellTexts,
        string externalReference,
        string? issuingOrganization)
    {
        string? fallback = null;
        foreach (var text in cellTexts)
        {
            var candidate = text.Trim();
            if (BidderPlaceholderRegex.IsMatch(candidate))
            {
                return null;
            }

            if (candidate.Length < 4
                || IsSameText(candidate, externalReference)
                || IsSameText(candidate, issuingOrganization)
                || BidAmountRegex.IsMatch(candidate)
                || AddressHintRegex.IsMatch(candidate)
                || FindBidderRank(new[] { candidate }) is not null
                || ParseDate(candidate) is not null)
            {
                continue;
            }

            if (CompanyKeywordRegex.IsMatch(candidate))
            {
                return candidate;
            }

            fallback ??= candidate;
        }

        return fallback;
    }

    private static decimal? FindBidAmount(IReadOnlyList<string> cellTexts)
    {
        var amountCell = FindBidAmountCell(cellTexts);
        return amountCell is null ? null : ParseBidAmount(amountCell);
    }

    private static string? FindBidAmountCell(IReadOnlyList<string> cellTexts)
    {
        string? amountCell = null;
        foreach (var text in cellTexts)
        {
            var match = BidAmountRegex.Match(text);
            if (!match.Success)
            {
                continue;
            }

            amountCell = text;
        }

        return amountCell;
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

    private static bool IsSameText(string left, string? right)
        => !string.IsNullOrWhiteSpace(right)
            && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

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
