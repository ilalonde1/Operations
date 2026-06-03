#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Ingestion.Scraping;

/// <summary>
/// Detail-page extractor for APC's "Interested suppliers" section.
/// Single responsibility: given a posting URL, return the list of firm
/// names that registered interest. Reusable from both the one-shot
/// backfill tool (tools/ApcInterestBackfill) and the recurring Worker
/// job (ApcInterestEnrichmentJob).
///
/// Selector contract (verified via tools/ApcInterestProbe against
/// AB-2026-04147 on 2026-06-03):
///   - Page anchor #interested-suppliers marks the section
///   - Each supplier is a div.supplier-card
///   - First non-empty line of the card's innerText is the firm name
///   - Subsequent lines are service descriptors + optional contact info
///
/// Resilient to: empty list (returns empty), JS-render delay (scrolls +
/// waits 2.5s), and partial-load (catches per-card exceptions, keeps going).
/// </summary>
public sealed class ApcInterestExtractor
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<ApcInterestExtractor> _logger;

    public ApcInterestExtractor(ILogger<ApcInterestExtractor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Navigates to <paramref name="postingUrl"/> on the supplied IPage and
    /// returns the supplier-card rows. The page should be a fresh IPage
    /// (callers reuse a context but typically a fresh page per posting).
    /// </summary>
    public async Task<IReadOnlyList<ScrapedSupplier>> ExtractAsync(
        IPage page,
        string postingUrl,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await page.GotoAsync(postingUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 45_000,
        }).ConfigureAwait(false);
        await page.WaitForTimeoutAsync(2500).ConfigureAwait(false);

        // Force-scroll to the section to trigger any lazy-render
        try
        {
            await page.EvaluateAsync(
                "() => document.querySelector('#interested-suppliers')?.scrollIntoView({behavior:'instant',block:'start'})"
                ).ConfigureAwait(false);
            await page.WaitForTimeoutAsync(1500).ConfigureAwait(false);
        }
        catch (PlaywrightException ex)
        {
            _logger.LogDebug(ex, "Scroll-into-view failed for {Url}; continuing", postingUrl);
        }

        var json = await page.EvaluateAsync<string>(@"() => {
            const cards = Array.from(document.querySelectorAll('div.supplier-card'));
            const out = [];
            for (const card of cards) {
                const raw = (card.innerText || '').trim();
                if (!raw) continue;
                const lines = raw.split(/\r?\n/).map(s => s.trim()).filter(s => s);
                if (lines.length === 0) continue;
                out.push({
                    name: lines[0],
                    descriptor: lines.slice(1).join(' | '),
                    rawText: raw,
                });
            }
            return JSON.stringify(out);
        }").ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(json) || json == "[]")
        {
            return Array.Empty<ScrapedSupplier>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ScrapedSupplier[]>(json, JsonOpts);
            return parsed ?? Array.Empty<ScrapedSupplier>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse supplier list JSON for {Url}", postingUrl);
            return Array.Empty<ScrapedSupplier>();
        }
    }
}

public sealed record ScrapedSupplier(string Name, string? Descriptor, string RawText);
