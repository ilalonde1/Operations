using System.Text;
using System.Text.RegularExpressions;

namespace Kor.Opportunities.Knowledge.Readers;

public static partial class Chunking
{
    /// <summary>Passages over this many characters are split on paragraph boundaries (one embedding per passage).</summary>
    public const int MaxChars = 1500;

    /// <summary>Split a long text on blank lines into pieces no longer than <paramref name="maxChars"/>, never mid-paragraph unless a paragraph is itself too long.</summary>
    public static IEnumerable<string> Split(string text, int maxChars = MaxChars)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChars);
        text = Normalise(text);
        if (text.Length <= maxChars) { if (text.Length > 0) yield return text; yield break; }

        var paragraphs = BlankLines().Split(text);
        var sb = new StringBuilder();
        foreach (var p0 in paragraphs)
        {
            var p = p0.Trim();
            if (p.Length == 0) continue;
            if (p.Length > maxChars)
            {
                if (sb.Length > 0) { yield return sb.ToString().Trim(); sb.Clear(); }
                foreach (var piece in SplitLong(p, maxChars)) yield return piece;
                continue;
            }
            if (sb.Length + p.Length + 2 > maxChars && sb.Length > 0) { yield return sb.ToString().Trim(); sb.Clear(); }
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(p);
        }
        if (sb.Length > 0) yield return sb.ToString().Trim();
    }

    private static IEnumerable<string> SplitLong(string p, int maxChars)
    {
        // a single over-long paragraph: cut at sentence ends
        var sentences = SentenceEnd().Split(p);
        var sb = new StringBuilder();
        foreach (var s in sentences)
        {
            if (sb.Length + s.Length + 1 > maxChars && sb.Length > 0) { yield return sb.ToString().Trim(); sb.Clear(); }
            if (s.Length > maxChars)
            {
                // Preserve JoeBrain's paragraph/sentence preference, then prefer a word
                // boundary to its fixed-width fallback. A single overlong token must split.
                var remaining = s.Trim();
                while (remaining.Length > maxChars)
                {
                    var cut = maxChars;
                    while (cut > 0 && !char.IsWhiteSpace(remaining[cut])) cut--;
                    if (cut == 0) cut = maxChars;
                    yield return remaining[..cut].TrimEnd();
                    remaining = remaining[cut..].TrimStart();
                }
                if (remaining.Length > 0) yield return remaining;
                continue;
            }
            sb.Append(s).Append(' ');
        }
        if (sb.Length > 0) yield return sb.ToString().Trim();
    }

    public static string Normalise(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    [GeneratedRegex(@"\n[ \t]*\n+")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z""'(])")]
    private static partial Regex SentenceEnd();
}
