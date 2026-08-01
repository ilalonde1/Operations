#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.HistoricalOpportunities;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Ingestion.Scraping;

/// <summary>
/// Walks <c>HistoricalOpportunities</c> rows where DetailScrapedAtUtc IS NULL,
/// visits each row's DetailUrl in headless Playwright, extracts enrichment via
/// <see cref="BcBidHistoricalDetailExtractor"/>, writes results back, and
/// registers discovered documents for later download.
///
/// Authenticates once per batch. Per-row failures are logged but don't abort
/// the batch.
/// </summary>
public sealed class BcBidHistoricalEnrichmentService
{
    private const int PageWaitTimeoutMs = 30_000;
    private const string LoginEntryUrl = "https://bcbid.gov.bc.ca/page.aspx/en/buy/homepage";

    private static readonly Regex MoneyRegex = new(
        @"(?<num>[\d,]+(?:\.\d+)?)",
        RegexOptions.Compiled);

    private readonly PlaywrightBrowserPool _pool;
    private readonly BcBidCredentials _credentials;
    private readonly IHistoricalOpportunityStore _store;
    private readonly IHistoricalOpportunityDocumentStore _documentStore;
    private readonly ILogger<BcBidHistoricalEnrichmentService> _logger;

    public BcBidHistoricalEnrichmentService(
        PlaywrightBrowserPool pool,
        BcBidCredentials credentials,
        IHistoricalOpportunityStore store,
        IHistoricalOpportunityDocumentStore documentStore,
        ILogger<BcBidHistoricalEnrichmentService> logger)
    {
        _pool = pool;
        _credentials = credentials;
        _store = store;
        _documentStore = documentStore;
        _logger = logger;
    }

    public sealed record EnrichmentBatchResult(int Attempted, int Enriched, int Failed);

    public async Task<EnrichmentBatchResult> EnrichBatchAsync(int batchSize, CancellationToken ct)
    {
        if (!_credentials.IsConfigured)
        {
            _logger.LogWarning("BcBid credentials not configured; enrichment skipped.");
            return new EnrichmentBatchResult(0, 0, 0);
        }

        var pending = await _store.ListPendingEnrichmentAsync(batchSize, ct).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            _logger.LogInformation("No HistoricalOpportunities pending enrichment.");
            return new EnrichmentBatchResult(0, 0, 0);
        }

        _logger.LogInformation("Enriching batch of {Count} historical rows.", pending.Count);

        await using var context = await _pool.AcquireContextAsync(ct).ConfigureAwait(false);
        var page = await context.NewPageAsync().ConfigureAwait(false);

        await LoginAsync(page, ct).ConfigureAwait(false);

        var enriched = 0;
        var failed = 0;
        foreach (var row in pending)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await page.GotoAsync(row.DetailUrl, new PageGotoOptions
                {
                    Timeout = PageWaitTimeoutMs,
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                }).ConfigureAwait(false);

                var extraction = await BcBidHistoricalDetailExtractor.ExtractAsync(page, ct).ConfigureAwait(false);
                var payload = ToPayload(extraction);
                await _store.UpdateEnrichmentAsync(row.Id, payload, ct).ConfigureAwait(false);

                if (extraction.Documents.Count > 0)
                {
                    await _documentStore.UpsertManyAsync(
                        row.Id,
                        extraction.Documents
                            .Select(d => new DiscoveredDocument(d.FileName, AbsoluteUrl(row.DetailUrl, d.SourceUrl)))
                            .ToList(),
                        ct).ConfigureAwait(false);
                }

                enriched++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(
                    ex,
                    "Enrichment failed for HistoricalOpportunity {Id} ({Key}).",
                    row.Id,
                    row.OpportunityKey);
            }
        }

        _logger.LogInformation(
            "Enrichment batch complete: attempted={A} enriched={E} failed={F}",
            pending.Count,
            enriched,
            failed);
        return new EnrichmentBatchResult(pending.Count, enriched, failed);
    }

    private async Task LoginAsync(IPage page, CancellationToken ct)
    {
        await page.GotoAsync(LoginEntryUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = PageWaitTimeoutMs,
        }).ConfigureAwait(false);

        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions
        {
            Name = "Login",
            Exact = true,
        }).First.ClickAsync(new LocatorClickOptions
        {
            Timeout = PageWaitTimeoutMs,
        }).ConfigureAwait(false);

        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions
        {
            NameRegex = new Regex(
                @"Business\s+or\s+Basic\s+BCeID",
                RegexOptions.IgnoreCase),
        }).First.ClickAsync(new LocatorClickOptions
        {
            Timeout = PageWaitTimeoutMs,
        }).ConfigureAwait(false);

        await page.WaitForURLAsync(
            url => url.Contains("logon.gov.bc.ca", StringComparison.OrdinalIgnoreCase)
                || url.Contains("bceid", StringComparison.OrdinalIgnoreCase),
            new PageWaitForURLOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);

        await page.Locator("input[type='text']:visible")
            .First.FillAsync(_credentials.Username, new LocatorFillOptions { Timeout = PageWaitTimeoutMs })
            .ConfigureAwait(false);
        await page.Locator("input[type='password']:visible")
            .First.FillAsync(_credentials.Password, new LocatorFillOptions { Timeout = PageWaitTimeoutMs })
            .ConfigureAwait(false);

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions
        {
            Name = "Continue",
        }).First.ClickAsync(new LocatorClickOptions
        {
            Timeout = PageWaitTimeoutMs,
        }).ConfigureAwait(false);

        await page.WaitForURLAsync(
            url => url.Contains("bcbid.gov.bc.ca", StringComparison.OrdinalIgnoreCase)
                && !url.Contains("logon", StringComparison.OrdinalIgnoreCase),
            new PageWaitForURLOptions { Timeout = PageWaitTimeoutMs }).ConfigureAwait(false);

        try
        {
            await page.WaitForLoadStateAsync(
                LoadState.NetworkIdle,
                new PageWaitForLoadStateOptions { Timeout = 10_000 }).ConfigureAwait(false);
        }
        catch (TimeoutException) { }
    }

    private static HistoricalOpportunityEnrichmentPayload ToPayload(
        BcBidHistoricalDetailExtractor.DetailExtraction d)
    {
        var (estAmount, estCcy) = ParseMoney(d.EstimatedAmountText);
        var fullDesc = ComposeFullDescriptionWithMeta(d);

        return new HistoricalOpportunityEnrichmentPayload
        {
            Commodities = Truncate(d.Commodities, 1000),
            AmendmentCount = d.AmendmentCount,
            FullDescription = fullDesc,
            EstimatedValue = estAmount,
            EstimatedValueCurrency = estCcy,
        };
    }

    private static string? ComposeFullDescriptionWithMeta(BcBidHistoricalDetailExtractor.DetailExtraction d)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(d.NoticeType)) parts.Add($"Notice Type: {d.NoticeType}");
        if (!string.IsNullOrWhiteSpace(d.InitialDurationMonths))
        {
            parts.Add($"Initial Duration (months): {d.InitialDurationMonths}");
        }

        if (!string.IsNullOrWhiteSpace(d.EstimatedAmountText))
        {
            parts.Add($"Estimated Amount: {d.EstimatedAmountText}");
        }

        if (!string.IsNullOrWhiteSpace(d.FullDescription)) parts.Add(d.FullDescription);
        return parts.Count == 0 ? null : string.Join("\n\n", parts);
    }

    private static (decimal?, string?) ParseMoney(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        var m = MoneyRegex.Match(text);
        if (!m.Success) return (null, null);
        var num = m.Groups["num"].Value.Replace(",", "", StringComparison.Ordinal);
        if (!decimal.TryParse(num, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
        {
            return (null, null);
        }

        return (v, "CAD");
    }

    private static string AbsoluteUrl(string baseUrl, string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return href;
        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return href;
        var baseUri = new Uri(baseUrl);
        return new Uri(baseUri, href).AbsoluteUri;
    }

    private static string? Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));
}
