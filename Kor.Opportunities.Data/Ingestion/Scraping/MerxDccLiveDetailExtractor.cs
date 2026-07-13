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

        // Resolve BOTH tab AJAX endpoints from the detail page BEFORE leaving
        // it (navigating to one tab page loses the other's anchor). A missing
        // anchor = the solicitation has no such tab (APNs) — quiet skip.
        var docsAjaxUrl = await GetTabAjaxUrlAsync(page, "docs-itemsAbstractTabBody").ConfigureAwait(false);
        var requestAjaxUrl = await GetTabAjaxUrlAsync(page, "docs-requestAbstractTabBody").ConfigureAwait(false);

        var docs = docsAjaxUrl is null
            ? Array.Empty<DetailDocument>()
            : await ReadDocumentsTabAsync(page, docsAjaxUrl, detailUrl, ct).ConfigureAwait(false);
        var firms = requestAjaxUrl is null
            ? Array.Empty<string>()
            : await ReadPlanHoldersTabAsync(page, requestAjaxUrl, detailUrl, ct).ConfigureAwait(false);
        return baseResult with { Documents = docs, InterestedFirms = firms };
    }

    /// <summary>
    /// The tab anchors carry data-ajax-url (e.g. /public/solicitations/&lt;internal
    /// id&gt;/abstract/docs-request) — a page that renders the tab's content on
    /// load. Navigating there directly is the ONLY reliable route: the
    /// ?innerTabId= URL param never fires the content AJAX, and clicking races
    /// the tab-view's JS binding (both verified live 2026-07-13). Null when the
    /// tab doesn't exist on this solicitation (e.g. APNs).
    /// </summary>
    private static async Task<string?> GetTabAjaxUrlAsync(IPage page, string tabBodyId)
    {
        var anchor = page.Locator($"a[aria-controls='{tabBodyId}']").First;
        if (await anchor.CountAsync().ConfigureAwait(false) == 0) return null;
        var rel = await anchor.GetAttributeAsync("data-ajax-url").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(rel)) return null;
        return new Uri(new Uri("https://www.merx.com/"), rel).ToString();
    }

    private async Task<IReadOnlyList<DetailDocument>> ReadDocumentsTabAsync(
        IPage page, string ajaxUrl, string detailUrl, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var fragment = await FetchFragmentAsync(page, ajaxUrl).ConfigureAwait(false);
            var docs = ParseDocumentsFragment(fragment);
            if (docs.Count >= MaxDocuments)
            {
                _logger.LogWarning("MERX docs tab: capped at {Cap} links for {Url}", MaxDocuments, detailUrl);
            }
            if (docs.Count > 0)
            {
                _logger.LogInformation("MERX docs tab: {Count} documents for {Url}", docs.Count, detailUrl);
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
        IPage page, string ajaxUrl, string detailUrl, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var fragment = await FetchFragmentAsync(page, ajaxUrl).ConfigureAwait(false);
            var firms = ParsePlanHoldersFragment(fragment);
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

    /// <summary>Fetches the tab endpoint from INSIDE the logged-in page (same
    /// cookies, same origin). The response is page chrome plus an inline
    /// script `$("#innerTabContent").html('&lt;escaped markup&gt;')` — the content
    /// exists only as that JS string literal, which is why neither URL
    /// navigation nor tab clicks ever rendered it (three live iterations,
    /// 2026-07-13). Decoding it is pure C# below — unit-testable, no DOM race.</summary>
    private static Task<string> FetchFragmentAsync(IPage page, string ajaxUrl)
        => page.EvaluateAsync<string>(
            "async url => { const r = await fetch(url, { credentials: 'include' }); return await r.text(); }",
            ajaxUrl);

    private static readonly Regex InnerTabHtmlRx = new(
        @"\$\(""#innerTabContent""\)\.html\('([\s\S]*?)'\);",
        RegexOptions.Compiled);

    private static readonly Regex DocAnchorRx = new(
        @"<a[^>]+href=""(?<href>[^""]*(?:view-document|download)[^""]*)""[^>]*>(?<name>[^<]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RequestTableRx = new(
        @"id=""documentRequesTable""[\s\S]*?</table>",
        RegexOptions.Compiled);

    private static readonly Regex FirstCellRx = new(
        @"<tr[^>]*>\s*<td[^>]*>(?<cell>[\s\S]*?)</td>",
        RegexOptions.Compiled);

    /// <summary>Decodes the JS single-quoted string literal MERX embeds the tab
    /// markup in (<, \/, \', \n, \t, \\). Pure; unit-tested.</summary>
    internal static string? DecodeInnerTabHtml(string fragment)
    {
        var m = InnerTabHtmlRx.Match(fragment ?? "");
        if (!m.Success) return null;
        var s = m.Groups[1].Value;
        s = Regex.Replace(s, @"\\u([0-9a-fA-F]{4})", x =>
            ((char)Convert.ToInt32(x.Groups[1].Value, 16)).ToString());
        return s.Replace("\\/", "/").Replace("\\'", "'").Replace("\\n", "\n")
                .Replace("\\t", "\t").Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    /// <summary>Pure fragment → document links. Only real file links
    /// (view-document / download) survive; page chrome cannot leak in because
    /// only the decoded tab markup is scanned.</summary>
    internal static List<DetailDocument> ParseDocumentsFragment(string fragment)
    {
        var html = DecodeInnerTabHtml(fragment);
        if (html is null) return new List<DetailDocument>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var docs = new List<DetailDocument>();
        foreach (Match m in DocAnchorRx.Matches(html))
        {
            if (docs.Count >= MaxDocuments) break;
            var href = WebUtilityDecode(m.Groups["href"].Value.Trim());
            if (href.Length == 0 || !seen.Add(href)) continue;
            var url = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? href
                : new Uri(new Uri("https://www.merx.com/"), href).ToString();
            var name = Regex.Replace(WebUtilityDecode(m.Groups["name"].Value), @"\s+", " ").Trim();
            docs.Add(new DetailDocument(name.Length > 0 ? name : "document", url));
        }

        return docs;
    }

    /// <summary>Pure fragment → plan-holder org names, strictly from
    /// #documentRequesTable (sic — MERX's own id; first column is
    /// organizationName). First page only; the header count may be larger.</summary>
    internal static List<string> ParsePlanHoldersFragment(string fragment)
    {
        var html = DecodeInnerTabHtml(fragment);
        if (html is null) return new List<string>();

        var table = RequestTableRx.Match(html);
        if (!table.Success) return new List<string>();

        var firms = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in FirstCellRx.Matches(table.Value))
        {
            if (firms.Count >= MaxInterestedFirms) break;
            var name = Regex.Replace(
                WebUtilityDecode(Regex.Replace(m.Groups["cell"].Value, "<[^>]+>", " ")),
                @"\s+", " ").Trim();
            if (name.Length < 3 || name.Length > 200) continue;
            if (name.Equals("No entries", StringComparison.OrdinalIgnoreCase)) continue;
            if (seen.Add(name)) firms.Add(name);
        }

        return firms;
    }

    private static string WebUtilityDecode(string s)
        => System.Net.WebUtility.HtmlDecode(s ?? "");

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
