using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Kor.Opportunities.Knowledge.Readers;

/// <summary>HTML to text without a parser dependency: scripts and styles dropped, block tags become line breaks, entities decoded, headings kept as section refs.</summary>
public static partial class HtmlText
{
    public sealed record Section(string? Heading, string Text);

    public static string Title(string html)
    {
        var m = TitleTag().Match(html);
        return m.Success ? WebUtility.HtmlDecode(Collapse(m.Groups[1].Value)).Trim() : "";
    }

    /// <summary>Split on h1–h3; the text before the first heading is a section with no heading.</summary>
    public static List<Section> Sections(string html)
    {
        html = HeadTag().Replace(html, " ");
        html = ScriptStyle().Replace(html, " ");
        html = Comments().Replace(html, " ");
        var sections = new List<Section>();
        var matches = HeadingTag().Matches(html);
        if (matches.Count == 0)
        {
            var whole = ToText(html);
            if (whole.Length > 0) sections.Add(new Section(null, whole));
            return sections;
        }
        var lead = ToText(html[..matches[0].Index]);
        if (lead.Length > 0) sections.Add(new Section(null, lead));
        for (var i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            var end = i + 1 < matches.Count ? matches[i + 1].Index : html.Length;
            // a heading is one line whatever its markup: a number over a title in two spans (Routledge's Schumpeter,
            // 63 refs), a <br> inside an <h2> (econlib's Jevons and Marshall, 109) came through with the break in the ref
            var heading = OneLine(ToText(m.Groups["h"].Value));
            var body = ToText(html[(m.Index + m.Length)..end]);
            if (body.Length > 0) sections.Add(new Section(heading.Length > 0 ? heading : null, body));
        }
        return sections;
    }

    public static string ToText(string html)
    {
        var t = BlockTags().Replace(html, "\n");
        t = AnyTag().Replace(t, " ");
        t = WebUtility.HtmlDecode(t);
        t = t.Replace(' ', ' ');
        t = SpaceRuns().Replace(t, " ");
        t = LineRuns().Replace(t, "\n\n");
        return string.Join("\n", t.Split('\n').Select(l => l.Trim())).Trim();
    }

    private static string Collapse(string s) => SpaceRuns().Replace(AnyTag().Replace(s, " "), " ");

    /// <summary>Every run of whitespace, line breaks included, to one space.</summary>
    public static string OneLine(string s) => AllWhite().Replace(s, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex AllWhite();

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleTag();
    [GeneratedRegex(@"<(script|style|noscript)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptStyle();
    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comments();
    [GeneratedRegex(@"<head\b[^>]*>.*?</head\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HeadTag();
    [GeneratedRegex(@"<h[1-3]\b[^>]*>(?<h>.*?)</h[1-3]\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HeadingTag();
    [GeneratedRegex(@"</?(p|div|br|li|ul|ol|tr|table|blockquote|h[1-6]|section|article|header|footer|dd|dt|dl|pre|hr)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTags();
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();
    [GeneratedRegex(@"[ \t\r\f]+")]
    private static partial Regex SpaceRuns();
    [GeneratedRegex(@"\n(\s*\n)+")]
    private static partial Regex LineRuns();
}

/// <summary>One HTML file, or a directory of them read in natural name order (econlib's one-page-per-chapter books). Ref = the page's heading, or its title.</summary>
public sealed class HtmlReader : IReader
{
    public async Task<IReadOnlyList<Chunk>> ReadAsync(string path, CancellationToken ct = default)
    {
        var files = Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*.htm*").OrderBy(f => f, NaturalOrder.Instance).ToList()
            : [path];
        // each page's title, less the site suffix ("… | Open Yale Courses", "… - Econlib"); it prefixes the refs only when it varies page to page
        var pages = new List<(string File, string Html, string Label)>();
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            var html = await File.ReadAllTextAsync(f, Encoding.UTF8, ct);
            pages.Add((f, html, PageLabel(HtmlText.Title(html))));
        }
        var prefix = pages.Select(p => p.Label).Distinct().Count() > 1;

        var chunks = new List<Chunk>();
        foreach (var (f, html, label) in pages)
        {
            foreach (var s in HtmlText.Sections(html))
            {
                if (IsSiteChrome(s)) continue;
                var heading = s.Heading;
                var reference = prefix && label.Length > 0
                    ? (heading is null ? label : $"{label} · {heading}")
                    : heading ?? (label.Length > 0 ? label : Path.GetFileNameWithoutExtension(f));
                foreach (var piece in Chunking.Split(s.Text))
                    chunks.Add(new Chunk(reference, piece));
            }
        }
        return chunks;
    }

    /// <summary>
    /// A section that is the site's furniture, not the book: a footer or menu reads as many short lines
    /// with almost no sentences (econlib's "Liberty Fund, Inc. / About / Contact Us / Sign up for our
    /// newsletter" block sat under every chapter page - 140 passages of it in Marshall for a day). A
    /// section is chrome when it has eight or more lines, they average under 30 characters, and fewer than
    /// a fifth of them end in a sentence - by characters, not lines, because econlib puts a paragraph number
    /// ("III.5") on its own line before every paragraph and a line count alone threw away half of Jevons.
    /// Or when it is short and carries a newsletter or copyright line and nothing else.
    /// </summary>
    public static bool IsSiteChrome(HtmlText.Section s)
    {
        var lines = s.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return true;
        var sentences = lines.Count(l => l.Length > 20 && (l.EndsWith('.') || l.EndsWith('?') || l.EndsWith('!') || l.EndsWith(';') || l.EndsWith(':')));
        var averageLine = lines.Average(l => l.Length);
        if (lines.Length >= 8 && averageLine < 30 && sentences * 5 < lines.Length) return true;
        var text = s.Text;
        return text.Length < 400 && (text.Contains("newsletter", StringComparison.OrdinalIgnoreCase) || text.Contains("All Rights Reserved", StringComparison.OrdinalIgnoreCase) || text.Contains("Privacy Policy", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>"ECON 159 - Lecture 1 - Introduction | Open Yale Courses" → "ECON 159 - Lecture 1 - Introduction"; "Principles of Economics - Econlib" → "Principles of Economics".</summary>
    public static string PageLabel(string title)
    {
        var t = title.Trim();
        foreach (var sep in new[] { " | ", " – ", " - " })
        {
            var i = t.LastIndexOf(sep, StringComparison.Ordinal);
            if (i > 0 && t.Length - i - sep.Length <= 30) { t = t[..i].Trim(); break; }
        }
        return t;
    }
}
