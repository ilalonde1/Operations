#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Ingestion.Providers;

/// <summary>
/// BC Housing's own project pages, bchousing.org/projects-partners/projects/{slug}.
///
/// BC Housing rarely builds directly: it owns the land, picks a development partner
/// and an operator (a non-profit society, often), and publishes each project with a
/// dated development timeline whose steps are marked completed, ongoing or pending.
/// That makes the page earlier and richer than the city permit record for the same
/// site — the permit feed carries "564 Fifth Street"; this page carries ~200 homes,
/// M'akola as operator, rezoning done and the building permit due 2027/28.
///
/// The sitemap lists every project page, so the listing page's "Show more" never
/// has to be driven. One candidate per page; the slug is the ExternalReference.
/// The timeline is written into Description, so a step moving from pending to
/// ongoing changes the hash and lands as a new observation.
///
/// Config keys (all optional):
///   bchousing.sitemapUrl   default https://www.bchousing.org/sitemap.xml
///   bchousing.delayMs      pause between page requests, default 1000
///   bchousing.maxPages     safety cap, default 400
/// </summary>
public sealed class BcHousingProjectsProvider : IOpportunityProvider
{
    public const string DefaultSitemapUrl = "https://www.bchousing.org/sitemap.xml";
    private const string ProjectPath = "/projects-partners/projects/";

    private static readonly Regex LocRx = new(@"<loc>\s*([^<\s]+)\s*</loc>", RegexOptions.Compiled);
    private static readonly Regex WsRx = new(@"\s+", RegexOptions.Compiled);

    // A street address as these pages write it: "564 Fifth Street", "2661-2671 Gladys
    // Avenue", "45867 Trethewey Avenue". Read from the name first, then the overview.
    private static readonly Regex AddressRx = new(
        @"\b\d{1,6}(?:\s*[-–]\s*\d{1,6})?\s+(?:[A-Z0-9][\w'.]*\s+){0,4}?(?:Street|Avenue|Road|Drive|Crescent|Way|Boulevard|Highway|Place|Diversion|Court|Lane|Gate|Terrace)\b(?:\s+(?:North|South|East|West))?",
        RegexOptions.Compiled);

    // The sentences that name who is behind the project: the operator, the partner,
    // the Nation whose territory it is on. Read as whole sentences, never as a
    // guessed firm name — a wrong firm on a lead is worse than none.
    private static readonly Regex PartyRx = new(
        @"\b(Society|Association|Foundation|Nation|operator|partner|partnership|Ltd|Inc\.|Developments?|Construction|Co-?op(?:erative)?|will (?:own|operate|manage))\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] ShareLabels = { "Twitter", "Facebook", "LinkedIn", "Email", "Share" };

    private readonly HttpClient _http;
    private readonly ILogger<BcHousingProjectsProvider> _log;

    public BcHousingProjectsProvider(HttpClient http, ILogger<BcHousingProjectsProvider> log)
    {
        _http = http;
        _log = log;
    }

    public OpportunitySourceType SourceType => OpportunitySourceType.BcHousingProjects;

    public async Task<IReadOnlyList<OpportunityCandidate>> FetchAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        var sitemapUrl = Get(sourceConfig, "bchousing.sitemapUrl") ?? DefaultSitemapUrl;
        var delayMs = int.TryParse(Get(sourceConfig, "bchousing.delayMs"), out var d) ? Math.Max(0, d) : 1000;
        var maxPages = int.TryParse(Get(sourceConfig, "bchousing.maxPages"), out var m) ? Math.Max(1, m) : 400;

        var sitemap = await GetStringAsync(sitemapUrl, ct).ConfigureAwait(false);
        var urls = ProjectUrls(sitemap);
        if (urls.Count == 0)
        {
            // Zero project pages means the site moved, not that BC Housing stopped building.
            throw new InvalidOperationException($"BC Housing sitemap at {sitemapUrl} lists no {ProjectPath} pages — the site's shape changed.");
        }

        if (urls.Count > maxPages)
        {
            IngestionRunDiagnostics.AddWarning($"BC Housing sitemap lists {urls.Count} project pages; capped at {maxPages}.");
            urls = urls.Take(maxPages).ToList();
        }

        var results = new List<OpportunityCandidate>();
        var failed = 0;
        foreach (var url in urls)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var html = await GetStringAsync(url, ct).ConfigureAwait(false);
                var page = Parse(html, url);
                if (page is not null)
                {
                    results.Add(ToCandidate(page));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                _log.LogWarning(ex, "BC Housing: could not read {Url}.", url);
            }

            if (delayMs > 0)
            {
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }

        if (failed > 0)
        {
            IngestionRunDiagnostics.AddWarning($"BC Housing: {failed} of {urls.Count} project page(s) could not be read.");
        }

        _log.LogInformation(
            "BC Housing {Source}: {Kept} project(s) from {Total} sitemap page(s); {Failed} failed; {WithTimeline} with a timeline.",
            source.Name, results.Count, urls.Count, failed, results.Count(r => r.Description?.Contains("Now:", StringComparison.Ordinal) == true));

        return results;
    }

    /// <summary>Project page urls from the sitemap, one per slug, in sitemap order.</summary>
    public static IReadOnlyList<string> ProjectUrls(string sitemapXml)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (Match match in LocRx.Matches(sitemapXml ?? ""))
        {
            var url = match.Groups[1].Value.Trim();
            var at = url.IndexOf(ProjectPath, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                continue;
            }

            var slug = url[(at + ProjectPath.Length)..].Trim('/');
            if (slug.Length == 0 || slug.Contains('/'))
            {
                continue;
            }

            if (seen.Add(slug))
            {
                list.Add(url);
            }
        }

        return list;
    }

    /// <summary>One project page, read. Null when the page has no project heading.</summary>
    public static BcHousingProject? Parse(string html, string url)
    {
        var doc = new HtmlParser().ParseDocument(html ?? "");
        var main = doc.QuerySelector("main") ?? doc.Body;
        var h1 = main?.QuerySelector("h1");
        if (main is null || h1 is null)
        {
            return null;
        }

        var title = Clean(h1.TextContent);
        var (city, name) = SplitTitle(title);
        var slug = url.TrimEnd('/');
        slug = slug[(slug.LastIndexOf('/') + 1)..].ToLowerInvariant();

        var timeline = new List<BcHousingTimelineStep>();
        foreach (var item in main.QuerySelectorAll(".bch-timeline-item"))
        {
            var status = item.QuerySelector(".bch-timeline-item--status")?.ClassList
                .FirstOrDefault(c => c is "completed" or "ongoing" or "pending") ?? "";
            var when = Clean(item.QuerySelector(".field--name-field-header-title")?.TextContent ?? "");
            var textEl = item.QuerySelector(".bch-timeline-item--text");
            var steps = textEl is null
                ? new List<string>()
                : textEl.QuerySelectorAll("li, p").Select(e => Clean(e.TextContent)).Where(s => s.Length > 0).Distinct().ToList();
            if (steps.Count == 0 && textEl is not null && Clean(textEl.TextContent).Length > 0)
            {
                steps.Add(Clean(textEl.TextContent));
            }

            timeline.Add(new BcHousingTimelineStep(when, status, steps));
        }

        // The overview: paragraphs and list items, minus the share buttons and the timeline
        // (read above). The folded sections stay, under their headings: the operator and
        // partner sentences live in them ("Provincial Redevelopment Program", "Resources").
        var overview = new List<string>();
        foreach (var el in main.QuerySelectorAll("p, li, .bch-accordion-item-header"))
        {
            if (el.Closest("h1") is not null || el.Closest(".bch-timeline-item") is not null)
            {
                continue;
            }

            if (el.ClassList.Contains("bch-accordion-item-header"))
            {
                var head = Clean(el.TextContent);
                if (head.Length > 0 && !head.Contains("timeline", StringComparison.OrdinalIgnoreCase))
                {
                    overview.Add("## " + head);
                }

                continue;
            }

            var text = Clean(el.TextContent);
            if (text.Length == 0 || ShareLabels.Contains(text, StringComparer.OrdinalIgnoreCase) || overview.Contains(text))
            {
                continue;
            }

            if (text.StartsWith("Please email", StringComparison.OrdinalIgnoreCase) || text.StartsWith("Learn more about", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            overview.Add(el.LocalName == "li" ? "- " + text : text);
        }

        var links = new List<string>();
        foreach (var a in main.QuerySelectorAll("a[href]"))
        {
            var href = a.GetAttribute("href") ?? "";
            if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                && !href.Contains("bchousing.org", StringComparison.OrdinalIgnoreCase)
                && !Regex.IsMatch(href, @"twitter|facebook|linkedin|//x\.com|mailto:", RegexOptions.IgnoreCase))
            {
                var label = Clean(a.TextContent);
                links.Add(label.Length > 0 ? $"{label} <{href}>" : href);
            }
        }

        var overviewText = string.Join("\n", overview);
        var address = AddressRx.Match(name) is { Success: true } n ? n.Value
            : AddressRx.Match(overviewText) is { Success: true } o ? o.Value : null;
        var parties = Sentences(overviewText).Where(s => PartyRx.IsMatch(s)).Distinct().Take(4).ToList();

        return new BcHousingProject(slug, url, title, city, name, address, overviewText, parties, timeline, links.Distinct().ToList());
    }

    /// <summary>The candidate the ingestion pipeline stores. The current and next steps
    /// lead the description, so they survive the 4,000-character cap.</summary>
    public static OpportunityCandidate ToCandidate(BcHousingProject p)
    {
        var sb = new StringBuilder();
        sb.Append("BC Housing project.");
        if (p.Current is { } now)
        {
            sb.Append(" Now: ").Append(now.Describe()).Append('.');
        }

        if (p.Next is { } next)
        {
            sb.Append(" Next: ").Append(next.Describe()).Append('.');
        }

        sb.AppendLine();
        if (p.Parties.Count > 0)
        {
            sb.Append("Who: ").AppendLine(string.Join(" ", p.Parties));
        }

        if (p.Timeline.Count > 0)
        {
            sb.Append("Timeline: ").AppendLine(string.Join("; ", p.Timeline.Select(t => $"[{t.Status}] {t.Describe()}")));
        }

        sb.AppendLine(p.Overview);
        if (p.Links.Count > 0)
        {
            sb.Append("Links: ").AppendLine(string.Join("; ", p.Links));
        }

        var description = sb.ToString().Trim();
        return new OpportunityCandidate
        {
            Title = Trim(p.Title, 400) ?? p.Slug,
            Buyer = "BC Housing",
            Location = Trim(p.Address is null ? p.City : $"{p.Address}, {p.City}", 400),
            Url = p.Url,
            Description = Trim(description, 4000),
            ProjectCity = Trim(p.City, 200),
            ProjectProvince = "BC",
            ExternalReference = Trim(p.Slug, 200),
            SourceInternalId = p.Slug,
        };
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Kor.Opportunities.Worker/1.0 (+ilalonde@korstructural.com)");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static (string City, string Name) SplitTitle(string title)
    {
        foreach (var sep in new[] { " – ", " — ", " - " })
        {
            var at = title.IndexOf(sep, StringComparison.Ordinal);
            if (at > 0)
            {
                return (title[..at].Trim(), title[(at + sep.Length)..].Trim());
            }
        }

        return (title, title);
    }

    private static IEnumerable<string> Sentences(string text)
        => Regex.Split(text ?? "", @"(?<=[.!?])\s+|\n")
            .Select(s => s.Trim().TrimStart('-', ' '))
            .Where(s => s.Length > 12);

    private static string Clean(string s)
        => WsRx.Replace((s ?? "").Replace(' ', ' '), " ").Trim();

    private static string? Get(IReadOnlyDictionary<string, string> cfg, string key)
        => cfg.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    private static string? Trim(string? s, int max)
        => string.IsNullOrWhiteSpace(s) ? null : (s.Length <= max ? s : s[..max]);
}

/// <summary>One dated step of a BC Housing development timeline.</summary>
public sealed record BcHousingTimelineStep(string When, string Status, IReadOnlyList<string> Steps)
{
    public string Describe() => (When.Length > 0 ? When + ": " : "") + string.Join("; ", Steps);
}

/// <summary>A BC Housing project page as read.</summary>
public sealed record BcHousingProject(
    string Slug,
    string Url,
    string Title,
    string City,
    string Name,
    string? Address,
    string Overview,
    IReadOnlyList<string> Parties,
    IReadOnlyList<BcHousingTimelineStep> Timeline,
    IReadOnlyList<string> Links)
{
    public BcHousingTimelineStep? Current => Timeline.FirstOrDefault(t => t.Status == "ongoing");
    public BcHousingTimelineStep? Next => Timeline.FirstOrDefault(t => t.Status == "pending");
}
