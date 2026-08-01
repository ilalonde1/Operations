#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Ingestion.Scraping;

/// <summary>
/// Detail-page reader for LIVE (open) BC Bid opportunities. Where the listing
/// scrape (BcBidScraper) only captures title/buyer/close, this opens the
/// authenticated opportunity detail page and recovers the commodity/discipline
/// list, the official contact, and the RFx document references — the content the
/// pipeline was previously blind to.
///
/// Login is delegated to the proven <see cref="BcBidPlanTakerExtractor"/> flow so
/// there is one BCeID login path. The DOM-to-fields step is a pure function
/// (<see cref="ParseDetail"/>) so it is unit-tested against captured page text.
/// </summary>
public sealed class BcBidLiveDetailExtractor : ILiveOppDetailExtractor
{
    public string Name => "BCBID";
    public string UrlHostLike => "%bcbid.gov.bc.ca%";
    public bool RequiresLogin => true;
    public bool IsAvailable => _credentials.IsConfigured;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // UNSPSC-style commodity lines, e.g. "81101505 - Structural engineering".
    private static readonly Regex CommodityRx = new(
        @"\b((?:72|81)\d{6})\b[ \t]*[-–][ \t]*([^\r\n|]{2,60})",
        RegexOptions.Compiled);
    private static readonly Regex EmailRx = new(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
    private static readonly Regex PhoneRx = new(
        @"(?:\+?1[ .\-]?)?\(?\d{3}\)?[ .\-]?\d{3}[ .\-]?\d{4}", RegexOptions.Compiled);

    private readonly BcBidPlanTakerExtractor _login;
    private readonly BcBidCredentials _credentials;
    private readonly ILogger<BcBidLiveDetailExtractor> _logger;

    public BcBidLiveDetailExtractor(BcBidPlanTakerExtractor login, BcBidCredentials credentials, ILogger<BcBidLiveDetailExtractor> logger)
    {
        _login = login ?? throw new ArgumentNullException(nameof(login));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task LoginAsync(IPage page, CancellationToken ct) => _login.LoginAsync(page, ct);

    public async Task<LiveDetailResult?> ExtractAsync(IPage page, string detailUrl, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await page.GotoAsync(detailUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 45_000,
        }).ConfigureAwait(false);
        await page.WaitForTimeoutAsync(2500).ConfigureAwait(false);

        string json;
        try
        {
            json = await page.EvaluateAsync<string>(@"() => JSON.stringify({
                text: document.body ? document.body.innerText : '',
                links: Array.from(document.querySelectorAll('a[href]')).map(a => ({
                    text: (a.innerText || a.textContent || '').trim().substring(0, 200),
                    href: a.href || ''
                })).filter(l => l.href)
            })").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BC Bid detail evaluate failed for {Url}", detailUrl);
            return null;
        }

        PageDump? dump;
        try { dump = JsonSerializer.Deserialize<PageDump>(json, JsonOpts); }
        catch (JsonException ex) { _logger.LogWarning(ex, "BC Bid detail JSON parse failed for {Url}", detailUrl); return null; }
        if (dump is null) return null;

        var links = (dump.Links ?? Array.Empty<PageLink>())
            .Select(l => new DetailLink(l.Text ?? "", l.Href ?? ""))
            .ToList();
        return ParseDetail(dump.Text ?? "", links);
    }

    /// <summary>Pure DOM-text → structured fields. Unit-tested; no I/O.</summary>
    public static LiveDetailResult ParseDetail(string pageText, IReadOnlyList<DetailLink> links)
    {
        pageText ??= "";

        // Commodities (feed DisciplineClassifier). Keep "code - name" so the
        // classifier sees both the code and the discipline words.
        var commodities = new List<string>();
        var seenCode = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in CommodityRx.Matches(pageText))
        {
            var code = m.Groups[1].Value;
            if (seenCode.Add(code))
            {
                commodities.Add($"{code} - {m.Groups[2].Value.Trim()}");
            }
        }

        // Official contact: the issuing-authority email only. Reject BC Bid /
        // Bids&Tenders / generic system addresses, and never fall back to one — a
        // null contact is better than a misleading vendor-support address.
        string? email = EmailRx.Matches(pageText)
            .Select(m => m.Value)
            .FirstOrDefault(e => !IsSystemEmail(e));

        var contactName = ExtractContactName(pageText);
        var phone = ExtractContactPhone(pageText);

        // Documents: links that look like RFx document downloads.
        var docs = new List<DetailDocument>();
        var seenUrl = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in links)
        {
            if (string.IsNullOrWhiteSpace(l.Href)) continue;
            var href = l.Href;
            var text = l.Text ?? "";
            var looksDoc =
                Regex.IsMatch(href, @"\.(pdf|docx?|xlsx?|zip|rtf)(\?|$)", RegexOptions.IgnoreCase)
                || Regex.IsMatch(text, @"\.(pdf|docx?|xlsx?|zip|rtf)\b", RegexOptions.IgnoreCase)
                || href.Contains("download", StringComparison.OrdinalIgnoreCase)
                || href.Contains("document", StringComparison.OrdinalIgnoreCase)
                || href.Contains("attachment", StringComparison.OrdinalIgnoreCase)
                || href.Contains("blobId", StringComparison.OrdinalIgnoreCase);
            if (!looksDoc) continue;
            if (seenUrl.Add(href))
            {
                docs.Add(new DetailDocument(
                    string.IsNullOrWhiteSpace(text) ? "(document)" : text, href));
            }
        }

        // BC Bid discipline comes from the structured commodity codes, so Description
        // is left null (the classifier reads CommodityCodes).
        return new LiveDetailResult(commodities, null, contactName, email, phone, docs);
    }

    private static readonly string[] SystemEmailFragments =
    {
        "bcbid", "bidsandtenders", "@gov.bc.ca", "support@", "noreply", "no-reply",
        "donotreply", "do-not-reply", "enquiry", "helpdesk", "webmaster",
    };

    private static bool IsSystemEmail(string e)
    {
        foreach (var frag in SystemEmailFragments)
        {
            if (e.Contains(frag, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string? ExtractContactName(string text)
    {
        // BC Bid renders "... Contact First Name Contact Last Name Email ... <First> <Last> <email>".
        var m = Regex.Match(text,
            @"Contact\s+First\s+Name\s*Contact\s+Last\s+Name\s*Email[^\n]*\n\s*([A-Z][a-zA-Z'\-]+)\s+([A-Z][a-zA-Z'\-]+)",
            RegexOptions.IgnoreCase);
        if (m.Success) return (m.Groups[1].Value + " " + m.Groups[2].Value).Trim();
        return null;
    }

    private static string? ExtractContactPhone(string text)
    {
        var idx = text.IndexOf("Official Contact", StringComparison.OrdinalIgnoreCase);
        var scope = idx >= 0 ? text.Substring(idx, Math.Min(600, text.Length - idx)) : text;
        var m = PhoneRx.Match(scope);
        return m.Success ? m.Value : null;
    }

    private sealed record PageDump
    {
        public string? Text { get; init; }
        public PageLink[]? Links { get; init; }
    }

    private sealed record PageLink
    {
        public string? Text { get; init; }
        public string? Href { get; init; }
    }
}
