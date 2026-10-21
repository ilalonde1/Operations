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
