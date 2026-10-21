#nullable enable
using System.Net;
using System.Text.RegularExpressions;

namespace Kor.Opportunities.Data.Ingestion;

/// <summary>
/// A posting's description as a reader sees it. Feeds such as CivicInfo's RSS carry HTML inside the text
/// ("Request for Proposals&lt;br/&gt;Posting expires…", a mailto anchor); shown raw, the tags read as code.
/// Line breaks and paragraphs become new lines, every other tag goes, entities are decoded, a link keeps its text.
/// </summary>
public static class PostingText
{
    private static readonly Regex Break = new(@"<\s*(br|/p|/div|/li)\s*/?\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Tag = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"[ \t ]+", RegexOptions.Compiled);
    private static readonly Regex BlankLines = new(@"\n\s*\n+", RegexOptions.Compiled);

    private static readonly Regex Word = new(@"[A-Za-z][A-Za-z'’]*", RegexOptions.Compiled);
    private static readonly HashSet<string> Small = new(StringComparer.OrdinalIgnoreCase)
        { "a", "an", "and", "as", "at", "by", "for", "from", "in", "of", "on", "or", "the", "to", "with" };
    private static readonly HashSet<string> Prefixes = new(StringComparer.OrdinalIgnoreCase) { "pre", "non", "sub", "co", "re", "new", "two", "all" };

    /// <summary>
    /// A tender title as a person would write it. Many portals post titles in capitals ("REQUEST FOR PROPOSALS (RFP) FOR
    /// ENGINEERING CONSULTANT SERVICES FOR EDMONTON"); a capitalised word of four or more letters becomes title case, small
    /// joining words go lower case, and short capitalised words (RFP, PQR, AB, MDB, K) are left alone as acronyms.
    /// Titles already in mixed case are returned unchanged.
    /// </summary>
    public static string DisplayTitle(string? title)
    {
        var t = (title ?? "").Trim();
        var letters = t.Count(char.IsLetter);
        if (letters == 0 || t.Count(char.IsUpper) < letters * 0.6) return t;
        var first = true;
        return Word.Replace(t, m =>
        {
            var w = m.Value;
            var isFirst = first;
            first = false;
            if (w.Length <= 3 && !Small.Contains(w) && !Prefixes.Contains(w)) return w;
            if (!isFirst && Small.Contains(w)) return w.ToLowerInvariant();
            return char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant();
        });
    }

    public static string ToPlain(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        // Decode first when the markup itself arrived escaped (&lt;br&gt;), so it is recognised as markup.
        var s = text.Contains("&lt;", System.StringComparison.Ordinal) ? WebUtility.HtmlDecode(text) : text;
        s = Break.Replace(s, "\n");
        s = Tag.Replace(s, "");
        s = WebUtility.HtmlDecode(s);
        s = Spaces.Replace(s.Replace("\r", ""), " ");
        s = BlankLines.Replace(s, "\n");
        return string.Join("\n", s.Split('\n').Select(l => l.Trim())).Trim();
    }
}
