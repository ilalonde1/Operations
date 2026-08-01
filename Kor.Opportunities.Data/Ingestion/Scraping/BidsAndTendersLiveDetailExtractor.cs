#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Ingestion.Scraping;

/// <summary>
/// Detail-page reader for LIVE Bids&amp;Tenders opportunities (all municipal
/// tenants: *.bidsandtenders.ca). The listing scrape stores NO description for
/// B&amp;T, so the highest-value field here is the tender Description — which feeds
/// the discipline classifier and gives the scope. The page is PUBLIC (no login).
/// Bid documents are register-gated (no stable public URLs), so they are
/// deliberately not captured rather than persisted with fake URLs.
///
/// DOM-to-fields is a pure function (<see cref="ParseDetail"/>) — unit-tested
/// against a captured real B&amp;T page.
/// </summary>
public sealed class BidsAndTendersLiveDetailExtractor : ILiveOppDetailExtractor
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static readonly Regex DescriptionRx = new(
        @"Description:\s*(.+?)\s*(?:Bid Document Access:|Trade Agreements:|Categories:|Submit a Question|Documents\b)",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex EmailRx = new(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);

    private readonly ILogger<BidsAndTendersLiveDetailExtractor> _logger;

    public BidsAndTendersLiveDetailExtractor(ILogger<BidsAndTendersLiveDetailExtractor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "BIDSTENDERS";
    public string UrlHostLike => "%bidsandtenders.ca%";
    public bool RequiresLogin => false;
    public bool IsAvailable => true;

    public Task LoginAsync(IPage page, CancellationToken ct) => Task.CompletedTask;

    public async Task<LiveDetailResult?> ExtractAsync(IPage page, string detailUrl, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await page.GotoAsync(detailUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 45_000,
        }).ConfigureAwait(false);
        await page.WaitForTimeoutAsync(2000).ConfigureAwait(false);

        string text;
        try
        {
            text = await page.EvaluateAsync<string>(
                "() => document.body ? document.body.innerText : ''").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "B&T detail evaluate failed for {Url}", detailUrl);
            return null;
        }
        return ParseDetail(text);
    }

    /// <summary>Pure DOM-text → fields. Unit-tested; no I/O.</summary>
    public static LiveDetailResult ParseDetail(string pageText)
    {
        pageText ??= "";

        string? description = null;
        var m = DescriptionRx.Match(pageText);
        if (m.Success)
        {
            var d = Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim();
            if (d.Length >= 20) description = d.Length > 4000 ? d[..4000] : d;
        }

        // A buyer/authority email if the page exposes one (many B&T pages don't —
        // contact is behind registration). Reject the bidsandtenders system address.
        string? email = null;
        foreach (Match em in EmailRx.Matches(pageText))
        {
            var e = em.Value;
            if (!e.Contains("bidsandtenders", StringComparison.OrdinalIgnoreCase)
                && !e.Contains("support@", StringComparison.OrdinalIgnoreCase))
            {
                email = e;
                break;
            }
        }

        return new LiveDetailResult(
            System.Array.Empty<string>(), description, null, email, null,
            System.Array.Empty<DetailDocument>());
    }
}
