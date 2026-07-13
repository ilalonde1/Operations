#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Ingestion.Scraping;

/// <summary>
/// Detail-page reader for LIVE MERX / DCC solicitations (merx.com). Readable
/// with the pool's plain Chromium (verified: MERX does not WAF a real headless
/// browser). Anonymous pass recovers the description (→ discipline) and the
/// real issuing contact (e.g. a DCC Pacific contracting officer).
///
/// v2 (2026-07-13, DCC Organization subscription): when MerxCredentials are
/// configured, LoginAsync signs in on /public/authentication/login
/// (j_username / j_password — form verified live) and ExtractAsync also reads
/// the two login-walled tabs, which are plain URLs, no clicks needed:
///   ?innerTabId=docs-items    → solicitation documents (DetailDocument links)
///   ?innerTabId=docs-request  → document-request list (plan holders →
///                               InterestedFirms, persisted via the
///                               interested-firm store, never as documents)
/// Login failure degrades to the anonymous pass — never a dead extractor.
/// DOM-to-fields for the base page stays a pure function (<see cref="ParseDetail"/>).
/// </summary>
public sealed class MerxDccLiveDetailExtractor : ILiveOppDetailExtractor
{
    private static readonly Regex DescriptionRx = new(
        @"\bDescription\s+(.+?)\s+(?:\.\.\.\s*)?(?:See more|Dates\b|Bid Submission)",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ContactRx = new(
        @"Contact Information\s+([A-Za-z][A-Za-z.'\-]+(?:\s+[A-Za-z.'\-]+){0,3}?)\s+((?:\+?1[-. ]?)?\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4})?\s*([A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,})",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private const string LoginUrl = "https://www.merx.com/public/authentication/login";
    private const int MaxDocuments = 60;
    private const int MaxInterestedFirms = 300;

    private readonly MerxCredentials _credentials;
    private readonly ILogger<MerxDccLiveDetailExtractor> _logger;
    private bool _loggedIn;

    public MerxDccLiveDetailExtractor(
        MerxCredentials credentials,
        ILogger<MerxDccLiveDetailExtractor> logger)
    {
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "MERXDCC";
    public string UrlHostLike => "%merx.com%";
    public bool RequiresLogin => _credentials.IsConfigured;

    // Always available: without creds this extractor still delivers the
    // anonymous description + contact pass (its original v1 behaviour).
    public bool IsAvailable => true;

    public async Task LoginAsync(IPage page, CancellationToken ct)
    {
        _loggedIn = false;
        if (!_credentials.IsConfigured) return;

        try
        {
            await page.GotoAsync(LoginUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 45_000,
            }).ConfigureAwait(false);

            // Cookie banner steals clicks when present; reject non-essential.
            try
            {
                var reject = page.GetByText("Reject all non-essential cookies").First;
                if (await reject.IsVisibleAsync().ConfigureAwait(false))
                {
                    await reject.ClickAsync(new LocatorClickOptions { Timeout = 5_000 }).ConfigureAwait(false);
                }
            }
            catch { /* banner absent — fine */ }

            await page.Locator("input[name='j_username']:visible").First
                .FillAsync(_credentials.Username, new LocatorFillOptions { Timeout = 15_000 }).ConfigureAwait(false);
            await page.Locator("input[name='j_password']:visible").First
                .FillAsync(_credentials.Password, new LocatorFillOptions { Timeout = 15_000 }).ConfigureAwait(false);
            await page.Locator("button[type='submit']:visible").First
                .ClickAsync(new LocatorClickOptions { Timeout = 15_000 }).ConfigureAwait(false);

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                new PageWaitForLoadStateOptions { Timeout = 30_000 }).ConfigureAwait(false);

            // Signed-in chrome drops the login link (#loginLinkCustom).
            _loggedIn = await page.EvaluateAsync<bool>(
                "() => !document.querySelector('#loginLinkCustom')").ConfigureAwait(false);

            if (_loggedIn)
            {
                _logger.LogInformation("MERX login OK for {User}", _credentials.Username);
            }
            else
            {
                _logger.LogWarning("MERX login did not stick (login link still present) — continuing anonymous");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "MERX login failed — continuing anonymous");
        }
    }

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

        var baseResult = ParseDetail(text);
        if (!_loggedIn) return baseResult;

        var docs = await ReadDocumentsTabAsync(page, detailUrl, ct).ConfigureAwait(false);
        var firms = await ReadPlanHoldersTabAsync(page, detailUrl, ct).ConfigureAwait(false);
        return baseResult with { Documents = docs, InterestedFirms = firms };
    }

    private static string TabUrl(string detailUrl, string tabId)
        => detailUrl + (detailUrl.Contains('?') ? "&" : "?") + "innerTabId=" + tabId;

    private async Task<IReadOnlyList<DetailDocument>> ReadDocumentsTabAsync(
        IPage page, string detailUrl, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            await page.GotoAsync(TabUrl(detailUrl, "docs-items"), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 45_000,
            }).ConfigureAwait(false);
            await page.WaitForTimeoutAsync(1500).ConfigureAwait(false);

            // Anchors inside the docs tab body (container id verified live);
            // fall back to any file-looking link in main content if the tab
            // container is renamed. Names come from the link text or the row.
            var raw = await page.EvaluateAsync<string[][]>(@"() => {
                const scope = document.querySelector('#docs-itemsAbstractTabBody') || document.body;
                const seen = new Set();
                const out = [];
                for (const a of scope.querySelectorAll('a[href]')) {
                    const href = a.href || '';
                    if (!href || seen.has(href)) continue;
                    const looksDoc = /download|document|attachment|\.pdf|\.zip|\.docx?|\.xlsx?|\.dwg/i.test(href);
                    if (!looksDoc) continue;
                    if (/innerTabId=|authentication|solicitations\/(open|awards)\b/i.test(href)) continue;
                    seen.add(href);
                    const name = (a.innerText || a.title || '').trim().replace(/\s+/g, ' ');
                    out.push([name || 'document', href]);
                }
                return out;
            }").ConfigureAwait(false);

            var docs = raw
                .Where(x => x.Length == 2 && !string.IsNullOrWhiteSpace(x[1]))
                .Take(MaxDocuments)
                .Select(x => new DetailDocument(x[0], x[1]))
                .ToList();

            if (raw.Length > MaxDocuments)
            {
                _logger.LogWarning("MERX docs tab: {Total} links found, capped at {Cap} for {Url}",
                    raw.Length, MaxDocuments, detailUrl);
            }

            return docs;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "MERX docs tab read failed for {Url}", detailUrl);
            return Array.Empty<DetailDocument>();
        }
    }

    private async Task<IReadOnlyList<string>> ReadPlanHoldersTabAsync(
        IPage page, string detailUrl, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            await page.GotoAsync(TabUrl(detailUrl, "docs-request"), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 45_000,
            }).ConfigureAwait(false);
            await page.WaitForTimeoutAsync(1500).ConfigureAwait(false);

            // First table cell per row inside the request-list tab body =
            // supplier name (first page only; count vs header logged so a
            // paginated tail is never silently 'covered').
            var names = await page.EvaluateAsync<string[]>(@"() => {
                const scope = document.querySelector('#docs-requestAbstractTabBody') || document.body;
                const out = [];
                for (const tr of scope.querySelectorAll('table tr')) {
                    const td = tr.querySelector('td');
                    if (!td) continue;
                    const name = (td.innerText || '').trim().replace(/\s+/g, ' ');
                    if (name.length >= 3 && name.length <= 200) out.push(name);
                }
                return out;
            }").ConfigureAwait(false);

            var firms = names
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxInterestedFirms)
                .ToList();

            if (firms.Count > 0)
            {
                _logger.LogInformation("MERX plan-holder tab: {Count} firms captured for {Url}", firms.Count, detailUrl);
            }

            return firms;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "MERX plan-holder tab read failed for {Url}", detailUrl);
            return Array.Empty<string>();
        }
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
