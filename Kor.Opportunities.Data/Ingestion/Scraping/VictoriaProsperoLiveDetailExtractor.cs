#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Ingestion.Scraping;

/// <summary>
/// Detail-page reader for the City of Victoria's Tempest "OurCity / Prospero"
/// development tracker (tender.victoria.ca), which is where every row from the
/// <c>Victoria_DevelopmentApplications</c> ArcGIS source links to.
///
/// WHY THIS IS THE VALUABLE ONE. The ArcGIS layer gives the site, the purpose
/// and the CITY PLANNER — a regulator. This page gives the APPLICATION CONTACT:
/// the applicant's own agent, which is the developer, their architect or their
/// planning consultant. That is the person a BD lead actually calls, and it
/// exists nowhere else in any feed we ingest. It also lists the submitted plan
/// sets, whose title blocks name the consultant team.
///
/// The page is PUBLIC (no login) and static HTML. Fields hang off stable
/// ASP.NET control ids (ctl00_FeaturedContent_*), not off layout classes, so
/// parsing is anchored to those rather than to the DOM shape.
///
/// ⚠ The applicant email is JS-obfuscated — a char-pair array reassembled by
/// document.write in an explicit index order. Under Playwright the script has
/// already run and a plain mailto is present, but <see cref="ParseDetail"/>
/// decodes the raw form too so it can be unit-tested against a captured page
/// and still works if the page is ever read without a browser.
///
/// DOM-to-fields is a pure function (<see cref="ParseDetail"/>), tested against
/// a real captured page — same shape as the Bids&amp;Tenders extractor.
/// </summary>
public sealed class VictoriaProsperoLiveDetailExtractor : ILiveOppDetailExtractor
{
    private const string IdPrefix = "ctl00_FeaturedContent_";

    private static readonly Regex EmailRx = new(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // var a = new Array('EL','EY','@W',...);
    private static readonly Regex ObfuscatedArrayRx = new(
        @"new\s+Array\(\s*(?<items>(?:'[^']*'\s*,\s*)*'[^']*')\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ArrayItemRx = new(@"'([^']*)'", RegexOptions.Compiled);

    // ...+a[11]+a[0]+a[1]+... — the reassembly order, which is NOT sequential.
    private static readonly Regex ArrayIndexRx = new(@"a\[(\d+)\]", RegexOptions.Compiled);

    private static readonly Regex AnchorRx = new(
        @"<a\s+[^>]*href\s*=\s*[""'](?<href>[^""']+)[""'][^>]*>(?<text>.*?)</a>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex TagRx = new(@"<[^>]+>", RegexOptions.Compiled | RegexOptions.Singleline);

    private readonly ILogger<VictoriaProsperoLiveDetailExtractor> _logger;

    public VictoriaProsperoLiveDetailExtractor(ILogger<VictoriaProsperoLiveDetailExtractor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "VICPROSPERO";

    public string UrlHostLike => "%tender.victoria.ca%";

    public bool RequiresLogin => false;

    public bool IsAvailable => true;

    public Task LoginAsync(IPage page, CancellationToken ct) => Task.CompletedTask;

    public async Task<LiveDetailResult?> ExtractAsync(IPage page, string detailUrl, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await page.GotoAsync(detailUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 45_000,
        }).ConfigureAwait(false);

        string html;
        try
        {
            html = await page.ContentAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Prospero: could not read page content for {Url}.", detailUrl);
            return null;
        }

        var result = ParseDetail(html, detailUrl);
        if (result is null)
        {
            _logger.LogWarning("Prospero: no recognisable application block at {Url}.", detailUrl);
        }

        return result;
    }

    /// <summary>
    /// Pure DOM-to-fields. Returns null when the page carries no application
    /// block at all (a search page, an error page, a withdrawn file).
    /// </summary>
    internal static LiveDetailResult? ParseDetail(string html, string detailUrl)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var applicant = InnerHtmlById(html, IdPrefix + "ApplicantLabel");
        var purpose = TextById(html, IdPrefix + "PurposeLabel");
        var addresses = TextById(html, IdPrefix + "AddressesLabel");
        var appType = TextById(html, IdPrefix + "ApplicationTypeLabel");

        if (applicant is null && purpose is null && appType is null)
        {
            return null;
        }

        var (contactName, contactPhone, contactEmail) = ParseApplicant(applicant);

        // Purpose is the scope; the address list matters because a rezoning that
        // spans nine parcels is nine addresses and the ArcGIS row may have been
        // capped. Both together are what the discipline classifier reads.
        var description = Compose(appType, purpose, addresses);

        return new LiveDetailResult(
            CommodityCodes: Array.Empty<string>(),
            Description: description,
            ContactName: contactName,
            ContactEmail: contactEmail,
            ContactPhone: contactPhone,
            Documents: ParseDocuments(html, detailUrl));
    }

    /// <summary>
    /// The applicant block is "NAME&lt;br/&gt;Telephone: &lt;a href=tel:…&gt;…&lt;/a&gt;&lt;br/&gt;Email: …".
    /// The name is whatever precedes the first line break.
    /// </summary>
    private static (string? Name, string? Phone, string? Email) ParseApplicant(string? applicantHtml)
    {
        if (string.IsNullOrWhiteSpace(applicantHtml))
        {
            return (null, null, null);
        }

        var firstBreak = applicantHtml.IndexOf("<br", StringComparison.OrdinalIgnoreCase);
        var namePart = firstBreak >= 0 ? applicantHtml[..firstBreak] : applicantHtml;
        var name = Clean(StripTags(namePart));

        string? phone = null;
        var tel = Regex.Match(applicantHtml, @"tel:([^""'>\s]+)", RegexOptions.IgnoreCase);
        if (tel.Success)
        {
            phone = Clean(WebUtility.HtmlDecode(tel.Groups[1].Value));
        }
        else
        {
            var telText = Regex.Match(StripTags(applicantHtml), @"Telephone:\s*([0-9()\-.\s+x]{7,})", RegexOptions.IgnoreCase);
            if (telText.Success)
            {
                phone = Clean(telText.Groups[1].Value);
            }
        }

        var email = DecodeEmail(applicantHtml);

        return (string.IsNullOrWhiteSpace(name) ? null : name, phone, email);
    }

    /// <summary>
    /// Takes a plain mailto/text address when one is present (Playwright has run
    /// the script), otherwise reassembles the obfuscated char-pair array in the
    /// index order the page's own document.write uses.
    /// </summary>
    internal static string? DecodeEmail(string fragmentHtml)
    {
        // The script body itself contains the pieces, so look at the non-script
        // text first to avoid matching a fragment of the array.
        var withoutScript = Regex.Replace(fragmentHtml, @"(?is)<script.*?</script>", " ");
        var plain = EmailRx.Match(withoutScript);
        if (plain.Success)
        {
            return plain.Value.Trim();
        }

        var arr = ObfuscatedArrayRx.Match(fragmentHtml);
        if (!arr.Success)
        {
            return null;
        }

        var items = ArrayItemRx.Matches(arr.Groups["items"].Value)
            .Select(m => m.Groups[1].Value)
            .ToArray();
        if (items.Length == 0)
        {
            return null;
        }

        // Reassembly order comes from the concatenation AFTER the array literal.
        var tail = fragmentHtml[(arr.Index + arr.Length)..];
        var order = ArrayIndexRx.Matches(tail)
            .Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        if (order.Count == 0)
        {
            return null;
        }

        // document.write emits the address twice (href then link text); one pass
        // through the array is enough.
        var sb = new StringBuilder();
        var seen = new HashSet<int>();
        foreach (var idx in order)
        {
            if (idx < 0 || idx >= items.Length || !seen.Add(idx))
            {
                continue;
            }

            sb.Append(items[idx]);
        }

        var candidate = sb.ToString().Trim();
        return EmailRx.IsMatch(candidate) ? candidate : null;
    }

    private static IReadOnlyList<DetailDocument> ParseDocuments(string html, string detailUrl)
    {
        var container = InnerHtmlById(html, IdPrefix + "documentsContainer", isSpan: false);
        if (container is null)
        {
            return Array.Empty<DetailDocument>();
        }

        var docs = new List<DetailDocument>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in AnchorRx.Matches(container))
        {
            var href = WebUtility.HtmlDecode(m.Groups["href"].Value).Trim();
            var text = Clean(StripTags(m.Groups["text"].Value));

            // Only the file downloads; the section also carries in-page nav.
            if (string.IsNullOrWhiteSpace(text)
                || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                || href.IndexOf("FileDownload.aspx", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var abs = ToAbsolute(href, detailUrl);
            if (abs is not null && seen.Add(abs))
            {
                docs.Add(new DetailDocument(text, abs));
            }
        }

        return docs;
    }

    private static string? ToAbsolute(string href, string detailUrl)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out var already))
        {
            return already.ToString();
        }

        return Uri.TryCreate(detailUrl, UriKind.Absolute, out var b)
               && Uri.TryCreate(b, href, out var abs)
            ? abs.ToString()
            : null;
    }

    private static string? Compose(params string?[] parts)
    {
        var kept = parts
            .Select(Clean)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();

        return kept.Length == 0 ? null : string.Join("  ·  ", kept);
    }

    /// <summary>Inner markup of the element carrying <paramref name="id"/>.</summary>
    private static string? InnerHtmlById(string html, string id, bool isSpan = true)
    {
        var anchor = html.IndexOf($"id=\"{id}\"", StringComparison.OrdinalIgnoreCase);
        if (anchor < 0)
        {
            anchor = html.IndexOf($"id='{id}'", StringComparison.OrdinalIgnoreCase);
            if (anchor < 0)
            {
                return null;
            }
        }

        var open = html.IndexOf('>', anchor);
        if (open < 0)
        {
            return null;
        }

        // Spans here never nest; the documents container does, so for it just take
        // a generous window — the anchor regex filters to FileDownload links.
        if (isSpan)
        {
            var close = html.IndexOf("</span>", open, StringComparison.OrdinalIgnoreCase);
            return close < 0 ? null : html[(open + 1)..close];
        }

        var end = Math.Min(html.Length, open + 40_000);
        return html[(open + 1)..end];
    }

    private static string? TextById(string html, string id)
        => Clean(StripTags(InnerHtmlById(html, id) ?? string.Empty));

    private static string StripTags(string html)
        => WebUtility.HtmlDecode(TagRx.Replace(html.Replace("<br", " <br", StringComparison.OrdinalIgnoreCase), " "));

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var flat = Regex.Replace(value.Replace('\r', ' ').Replace('\n', ' '), @"\s{2,}", " ").Trim();
        return flat.Length == 0 ? null : flat;
    }
}
