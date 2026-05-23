#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Awards;

public sealed class VendorSiteCrawlService
{
    // Slug -> URL keyword candidates. Home is visited first; we discover nav links there,
    // then match against these to pick the best URL for each slug.
    private static readonly (string Slug, string[] Keywords)[] PageTargets =
    [
        ("home",     [""]),
        ("about",    ["about", "who-we-are", "company"]),
        ("services", ["services", "expertise", "what-we-do", "capabilities"]),
        ("projects", ["projects", "portfolio", "our-work", "case-studies", "experience"]),
        ("team",     ["team", "people", "leadership", "our-team", "staff"]),
        ("careers",  ["careers", "jobs", "join-us", "join-our-team", "opportunities"]),
        ("contact",  ["contact", "offices", "locations"]),
    ];

    private const int MaxPageTextChars = 30_000;
    private const int PolitenessDelayMs = 2500;
    private const int PageNavigationTimeoutMs = 30_000;

    private readonly PlaywrightBrowserPool _pool;
    private readonly IVendorSiteCrawlStore _store;
    private readonly ILogger<VendorSiteCrawlService> _logger;

    public VendorSiteCrawlService(
        PlaywrightBrowserPool pool,
        IVendorSiteCrawlStore store,
        ILogger<VendorSiteCrawlService> logger)
    {
        _pool = pool;
        _store = store;
        _logger = logger;
    }

    public sealed record BatchResult(int Attempted, int Ok, int Failed, int Blocked);

    public async Task<BatchResult> CrawlBatchAsync(int batchSize, int maxAttempts, CancellationToken ct)
    {
        var websites = await _store.ListPendingWebsitesAsync(batchSize, maxAttempts, ct).ConfigureAwait(false);
        if (websites.Count == 0) return new BatchResult(0, 0, 0, 0);

        _logger.LogInformation("VendorSiteCrawl: batch of {Count} vendor site(s).", websites.Count);

        var ok = 0;
        var failed = 0;
        var blocked = 0;
        await using var lease = await _pool.AcquireContextAsync(ct).ConfigureAwait(false);
        var context = lease.Context;

        foreach (var website in websites)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var normalized = NormalizeUrl(website);
                if (normalized is null)
                {
                    await _store.RecordFailureAsync(website, "failed", "Could not normalize URL", ct).ConfigureAwait(false);
                    failed++;
                    continue;
                }

                if (!await IsRobotsAllowedAsync(normalized, ct).ConfigureAwait(false))
                {
                    await _store.RecordFailureAsync(website, "no_robots", "Disallowed by robots.txt", ct).ConfigureAwait(false);
                    failed++;
                    continue;
                }

                var capture = await CrawlSiteAsync(context, normalized, ct).ConfigureAwait(false);
                if (capture is null)
                {
                    await _store.RecordFailureAsync(website, "blocked", "Cloudflare/anti-bot challenge or unreachable", ct).ConfigureAwait(false);
                    blocked++;
                    continue;
                }

                await _store.RecordCaptureAsync(website, capture, ct).ConfigureAwait(false);
                ok++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VendorSiteCrawl failed for {Site}: {Msg}", website, ex.Message);
                try
                {
                    await _store.RecordFailureAsync(website, "failed", ex.Message, ct).ConfigureAwait(false);
                }
                catch
                {
                }

                failed++;
            }
        }

        return new BatchResult(websites.Count, ok, failed, blocked);
    }

    private async Task<RawSiteCapture?> CrawlSiteAsync(IBrowserContext context, string homeUrl, CancellationToken ct)
    {
        var page = await context.NewPageAsync().ConfigureAwait(false);
        try
        {
            var home = await TryNavigateAndCaptureAsync(page, homeUrl).ConfigureAwait(false);
            if (home is null) return null;
            if (LooksLikeCloudflareChallenge(home.Text)) return null;

            var navHrefs = await DiscoverNavLinksAsync(page, homeUrl).ConfigureAwait(false);

            var chosen = new Dictionary<string, string> { ["home"] = homeUrl };
            foreach (var (slug, keywords) in PageTargets.Skip(1))
            {
                var match = navHrefs.FirstOrDefault(href => keywords.Any(k =>
                    href.Contains("/" + k, StringComparison.OrdinalIgnoreCase) ||
                    href.EndsWith("/" + k, StringComparison.OrdinalIgnoreCase) ||
                    href.EndsWith("/" + k + "/", StringComparison.OrdinalIgnoreCase)));
                if (match is not null) chosen[slug] = match;
            }

            var pages = new List<PageCapture> { home };
            foreach (var kv in chosen.Where(kv => kv.Key != "home"))
            {
                await Task.Delay(PolitenessDelayMs, ct).ConfigureAwait(false);
                var capture = await TryNavigateAndCaptureAsync(page, kv.Value).ConfigureAwait(false);
                if (capture is not null) pages.Add(capture);
            }

            return new RawSiteCapture(pages, navHrefs);
        }
        finally
        {
            await page.CloseAsync().ConfigureAwait(false);
        }
    }

    private static async Task<PageCapture?> TryNavigateAndCaptureAsync(IPage page, string url)
    {
        try
        {
            var resp = await page.GotoAsync(url, new PageGotoOptions
            {
                Timeout = PageNavigationTimeoutMs,
                WaitUntil = WaitUntilState.DOMContentLoaded,
            }).ConfigureAwait(false);

            if (resp is null || !resp.Ok) return null;

            var title = await page.TitleAsync().ConfigureAwait(false);
            var text = await page.EvaluateAsync<string>("() => document.body ? document.body.innerText : ''")
                .ConfigureAwait(false) ?? "";

            text = Regex.Replace(text, @"\s+", " ").Trim();
            if (text.Length > MaxPageTextChars) text = text.Substring(0, MaxPageTextChars);

            return new PageCapture(url, title, text);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<List<string>> DiscoverNavLinksAsync(IPage page, string homeUrl)
    {
        var hrefs = await page.EvaluateAsync<string[]>(@"() => {
            const out = new Set();
            document.querySelectorAll('a[href]').forEach(a => {
                const h = a.getAttribute('href');
                if (h) out.add(h);
            });
            return Array.from(out);
        }").ConfigureAwait(false);

        var home = new Uri(homeUrl);
        var list = new List<string>();
        foreach (var h in hrefs ?? Array.Empty<string>())
        {
            if (!Uri.TryCreate(home, h, out var abs)) continue;
            if (!string.Equals(abs.Host, home.Host, StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(abs.GetLeftPart(UriPartial.Path));
        }

        return list.Distinct().ToList();
    }

    private static bool LooksLikeCloudflareChallenge(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        return text.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Checking your browser", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Verify you are human", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Attention Required", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeUrl(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var trimmed = input.Trim();
        if (!trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != "http" && uri.Scheme != "https") return null;
        return new UriBuilder(uri.Scheme, uri.Host).Uri.ToString();
    }

    private static async Task<bool> IsRobotsAllowedAsync(string homeUrl, CancellationToken ct)
    {
        try
        {
            var u = new Uri(homeUrl);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("KOR-Operations-BD-Crawler/1.0 (+ilalonde@korstructural.com)");
            var robots = await http.GetStringAsync(new Uri(u, "/robots.txt"), ct).ConfigureAwait(false);
            var lines = robots.Split('\n');
            var inStarBlock = false;
            foreach (var raw in lines)
            {
                var line = raw.Split('#')[0].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("User-agent:", StringComparison.OrdinalIgnoreCase))
                {
                    inStarBlock = line.Substring("User-agent:".Length).Trim() == "*";
                }
                else if (inStarBlock && line.StartsWith("Disallow:", StringComparison.OrdinalIgnoreCase))
                {
                    var path = line.Substring("Disallow:".Length).Trim();
                    if (path == "/") return false;
                }
            }

            return true;
        }
        catch
        {
            return true; // no robots.txt or fetch failed: assume allowed
        }
    }
}
