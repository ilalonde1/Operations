// Every line of a set's text that mentions any of the given words, with the pages it is on — the reading
// instrument for "what does the drawing CALL this?" before a rule is written about it (rule 9: ground
// truth first). Case-insensitive; a regex is a word too (quote it). Prints "count pages text", the most
// repeated first, one line per distinct text, so a phrase the set repeats on every section ("T/O ELEV.
// OVERRUN") stands out from one that appears once. Built 2026-09-10 to learn what 31170's sections name
// L7 and L8 (the ROOF PLAN was landing on the elevator overrun); ported from
// docs/etabs-handoff/pdf_words_near.py (WP2, 2026-09-11) onto VectorPageReader's text lines.
//   takeoff vector-find <pdf> WORD [WORD ...] [--pages a-b]
internal static class VectorFindVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("vector-find", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff vector-find <pdf> WORD [WORD ...] [--pages a-b]"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
        var words = new List<string>();
        int first = 1, last = int.MaxValue;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i].Equals("--pages", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var m = Regex.Match(args[++i], @"^(\d+)(?:-(\d+))?$");
                if (!m.Success) { Console.Error.WriteLine("--pages wants a-b, e.g. 3-9."); return 2; }
                first = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                last = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : first;
            }
            else words.Add(args[i]);
        }
        if (words.Count == 0) { Console.Error.WriteLine("Give at least one word."); return 2; }
        var rx = new Regex(string.Join("|", words), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var hits = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
        using var doc = UglyToad.PdfPig.PdfDocument.Open(args[1]);
        int pages = doc.NumberOfPages;
        for (int p = first; p <= Math.Min(last, pages); p++)
        {
            var content = VectorPageReader.ReadPage(doc.GetPage(p));
            foreach (string text in Phrases(content))
            {
                if (!rx.IsMatch(text)) continue;
                if (!hits.TryGetValue(text, out var set)) hits[text] = set = [];
                set.Add(p);
            }
        }
        Console.WriteLine($"{Path.GetFileName(args[1])}: {hits.Count} distinct line(s) mention {string.Join(" | ", words)} on pages {first}..{Math.Min(last, pages)}");
        foreach (var (text, ps) in hits.OrderByDescending(h => h.Value.Count).ThenBy(h => h.Key, StringComparer.Ordinal))
        {
            string shown = string.Join(",", ps.Take(8)) + (ps.Count > 8 ? "..." : "");
            Console.WriteLine($"{ps.Count,3}  p{shown,-24}  {(text.Length > 110 ? text[..110] : text)}");
        }
        return 0;
    }

    /// <summary>
    /// The page's text as PHRASES: words on one baseline, left to right, broken where the gap to the
    /// next word is wider than two and a half of its heights — so a sheet of notes in three columns
    /// gives three phrases per baseline, not one line across the page (VectorPageReader.ReadTextLines
    /// joins the whole baseline, which is what the readers want and what this question does not).
    /// </summary>
    private static IEnumerable<string> Phrases(VectorPageReader.PageContent content)
    {
        foreach (var baseline in content.Words.GroupBy(w => Math.Round(w.Cy / 6.0)))
        {
            var run = new List<string>();
            VectorPageReader.TextToken? last = null;
            foreach (var w in baseline.OrderBy(w => w.Cx))
            {
                if (last is { } l && w.MinX - l.MaxX > 2.5 * Math.Max(l.Height, 1.0))
                {
                    yield return string.Join(" ", run);
                    run.Clear();
                }
                run.Add(w.Text.Trim());
                last = w;
            }
            if (run.Count > 0) yield return string.Join(" ", run);
        }
    }
}
