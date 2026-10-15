using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Util;

namespace Kor.Opportunities.Knowledge.Readers;

/// <summary>A PDF with a text layer, read with PdfPig. Chunks are page groups; Ref is "pp. a–b". Throws NoTextLayerException on a scan.</summary>
public sealed class PdfTextReader(int pagesPerChunk = 3) : IReader
{
    public Task<IReadOnlyList<Chunk>> ReadAsync(string path, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pagesPerChunk);
        using var doc = PdfDocument.Open(path);
        var pages = new List<(int Number, string Text)>();
        foreach (var page in doc.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            pages.Add((page.Number, PageText(page)));
        }
        var withText = pages.Count(p => p.Text.Length > 20);
        if (pages.Count > 0 && withText < pages.Count * 0.5)
            throw new NoTextLayerException(path, pages.Count, withText);
        // a text layer whose words still run together after word segmentation is no text layer: nothing can be
        // searched or embedded in it. Report it as unreadable; this library has no OCR.
        if (Unspaced(pages.Where(p => p.Text.Length > 20).Select(p => p.Text)))
            throw new NoTextLayerException(path, pages.Count, 0);

        var chunks = new List<Chunk>();
        for (var i = 0; i < pages.Count; i += pagesPerChunk)
        {
            var group = pages.Skip(i).Take(pagesPerChunk).Where(p => p.Text.Length > 0).ToList();
            if (group.Count == 0) continue;
            var text = string.Join("\n\n", group.Select(p => p.Text));
            var reference = group.Count == 1 ? $"p. {group[0].Number}" : $"pp. {group[0].Number}–{group[^1].Number}";
            foreach (var piece in Chunking.Split(text))
                chunks.Add(new Chunk(reference, piece));
        }
        return Task.FromResult<IReadOnlyList<Chunk>>(chunks);
    }

    /// <summary>
    /// JoeBrain's content-order extraction segments words by glyph position, unlike
    /// page.Text's raw glyph concatenation. Remove shadow double-draws before word
    /// extraction when present, then fold ligatures and unnamed font symbols.
    /// </summary>
    public static string PageText(Page page)
    {
        var letters = page.Letters;
        var keep = ShadowFreeIndices(letters.Select(l => (l.Value, l.GlyphRectangle.Left, l.GlyphRectangle.Bottom, l.PointSize)).ToList());
        // With no double-draws, keep PdfPig's content-order extraction.
        if (keep.Count == letters.Count) return Ligatures(ContentOrderTextExtractor.GetText(page).Trim());
        var kept = keep.Select(i => letters[i]).ToList();
        var words = DefaultWordExtractor.Instance.GetWords(kept);
        return Ligatures(TextFromWords(words).Trim());
    }

    /// <summary>
    /// Keep one copy of a glyph painted twice at nearly the same position.
    /// Tolerance is the larger of the point threshold and the font-size fraction,
    /// preserving real repeated letters that sit farther apart.
    /// </summary>
    public static List<int> ShadowFreeIndices(IReadOnlyList<(string Text, double X, double Y, double Size)> letters)
    {
        var keep = new List<int>(letters.Count);
        var seen = new Dictionary<string, List<(double X, double Y)>>(StringComparer.Ordinal);
        for (var i = 0; i < letters.Count; i++)
        {
            var (text, x, y, size) = letters[i];
            var tol = Math.Max(ShadowTolerancePt, ShadowToleranceOfSize * size);
            if (seen.TryGetValue(text, out var prior))
            {
                var dup = false;
                foreach (var (px, py) in prior)
                    if (Math.Abs(px - x) <= tol && Math.Abs(py - y) <= tol) { dup = true; break; }
                if (dup) continue;
                prior.Add((x, y));
            }
            else seen[text] = [(x, y)];
            keep.Add(i);
        }
        return keep;
    }

    /// <summary>Max offset in PDF points at which a same glyph is a double-draw at body sizes: a shadow or fake-bold offset is a fraction of a stroke width; the next real glyph is a glyph's width away.</summary>
    public const double ShadowTolerancePt = 1.0;
    /// <summary>The offset as a share of the glyph's point size for large type (a 28-point title's shadow at 1.14 pt); the narrowest glyph advance is about a third of the size, so this stays well under a real repeat.</summary>
    public const double ShadowToleranceOfSize = 0.06;

    /// <summary>Words in content order to text: a space between words on a line, a line break when the baseline moves by more than half a glyph's height.</summary>
    public static string TextFromWords(IEnumerable<Word> words)
    {
        var sb = new StringBuilder();
        double? lastBottom = null, lastHeight = null;
        foreach (var w in words)
        {
            var bb = w.BoundingBox;
            if (sb.Length > 0)
                sb.Append(lastBottom is { } lb && Math.Abs(bb.Bottom - lb) > 0.5 * Math.Max(1, lastHeight ?? bb.Height) ? '\n' : ' ');
            sb.Append(w.Text);
            lastBottom = bb.Bottom; lastHeight = bb.Height;
        }
        return sb.ToString();
    }

    /// <summary>True when the text's "words" average more than 20 characters - the spaces are missing, so it is OCR noise, not prose.</summary>
    public static bool Unspaced(IEnumerable<string> pageTexts)
    {
        long chars = 0, words = 0;
        foreach (var t in pageTexts)
        {
            var w = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            words += w.Length; chars += w.Sum(x => x.Length);
        }
        return words > 0 && chars / (double)words > 20;
    }

    /// <summary>LaTeX-set PDFs carry the fi/fl/ff ligatures as single code points ("ﬁrm"), which search and the embedder read as a different word; fold them.</summary>
    public static string Ligatures(string s) =>
        PrivateUse(s.IndexOfAny(['ﬀ', 'ﬁ', 'ﬂ', 'ﬃ', 'ﬄ']) < 0 ? s
        : s.Replace("ﬀ", "ff").Replace("ﬁ", "fi").Replace("ﬂ", "fl").Replace("ﬃ", "ffi").Replace("ﬄ", "ffl"));

    /// <summary>
    /// Map the unambiguous Symbol/Wingdings private-use codes from JoeBrain's reader.
    /// Other private-use characters become U+FFFD rather than an invented symbol.
    /// </summary>
    public static string PrivateUse(string s)
    {
        if (!s.Any(ch => ch >= '' && ch <= '')) return s;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch < '' || ch > '') { sb.Append(ch); continue; }
            sb.Append(ch switch
            {
                '' or '' or '' or '' or '' or '' => "• ",   // Symbol / Wingdings bullets and ticks
                '' or '' or '' => "→",
                '' or '' => "←",
                '' or '' => "↔",
                '' or '' => "↑",
                '' or '' => "↓",
                '' => "×", '' => "÷", '' => "±", '' => "≤", '' => "≥", '' => "≠", '' => "≈",
                '' => "∞", '' => "…", '' => "°", '' => "·", '' or '' => " ",
                '' => "α", '' => "β", '' => "δ", '' => "ε", '' => "λ", '' => "μ", '' => "π", '' => "ρ", '' => "σ", '' => "τ", '' => "θ", '' => "ω",
                '' => "Δ", '' => "Σ", '' => "Π", '' => "Ω",
                _ => "�",
            });
        }
        return sb.ToString();
    }

}
