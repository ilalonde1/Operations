#nullable enable
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Ingestion.Scraping;

/// <summary>
/// Detail-page reader for LIVE MERX / DCC solicitations (merx.com). Public pages,
/// no login, and — unlike Bonfire — readable with the pool's plain Chromium
/// (verified: MERX does not WAF a real headless browser). Recovers the full
/// description (→ discipline) and the real issuing contact (name / phone / email —
/// e.g. a DCC Pacific contracting officer), which is high-value for defence work.
///
/// Document links and the Plan Holders list live behind tabs (a click); v1
/// captures the reliably-visible description + contact. DOM-to-fields is a pure
/// function (<see cref="ParseDetail"/>) — unit-tested against a real MERX page.
/// </summary>
public sealed class MerxDccLiveDetailExtractor : ILiveOppDetailExtractor
{
    private static readonly Regex DescriptionRx = new(
        @"\bDescription\s+(.+?)\s+(?:\.\.\.\s*)?(?:See more|Dates\b|Bid Submission)",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ContactRx = new(
        @"Contact Information\s+([A-Za-z][A-Za-z.'\-]+(?:\s+[A-Za-z.'\-]+){0,3}?)\s+((?:\+?1[-. ]?)?\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4})?\s*([A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,})",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private readonly ILogger<MerxDccLiveDetailExtractor> _logger;

    public MerxDccLiveDetailExtractor(ILogger<MerxDccLiveDetailExtractor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "MERXDCC";
    public string UrlHostLike => "%merx.com%";
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
            _logger.LogWarning(ex, "MERX detail evaluate failed for {Url}", detailUrl);
            return null;
        }
        return ParseDetail(text);
    }

    /// <summary>Pure DOM-text → fields. Unit-tested; no I/O.</summary>
    public static LiveDetailResult ParseDetail(string pageText)
    {
        pageText ??= "";

        string? description = null;
        var dm = DescriptionRx.Match(pageText);
        if (dm.Success)
        {
            var d = Regex.Replace(dm.Groups[1].Value, @"\s+", " ").Trim();
            if (d.Length >= 15) description = d.Length > 4000 ? d[..4000] : d;
        }

        string? name = null, phone = null, email = null;
        var cm = ContactRx.Match(pageText);
        if (cm.Success)
        {
            name = Clean(cm.Groups[1].Value);
            if (cm.Groups[2].Success) phone = Clean(cm.Groups[2].Value);
            email = Clean(cm.Groups[3].Value);
        }

        return new LiveDetailResult(
            System.Array.Empty<string>(), description, name, email, phone,
            System.Array.Empty<DetailDocument>());
    }

    private static string? Clean(string s)
    {
        s = s.Trim();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
