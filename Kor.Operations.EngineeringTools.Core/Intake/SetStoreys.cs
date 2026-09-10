using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using UglyToad.PdfPig;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// A set's storey table: the union of its section and elevation sheets' level ladders, reconciled
/// by the pair of level names each storey runs between. Several sheets state the same storey; the
/// table carries the median and how far the sheets disagree.
/// </summary>
/// <remarks>
/// Read light: one vector read per page, the sheet typed as the intake types it (bookmark, then the
/// SHEET TITLE field, then the title text), the ladder read only on section/elevation sheets. No
/// classification, so a 60-sheet set takes seconds, not the minutes the full ledger takes. WHAT IT
/// DOES NOT: an AS NOTED sheet (its views carry their own scale — nothing is read there), a name the
/// level reader mangles, and two buildings' ladders on one sheet (the busiest column wins).
/// </remarks>
public static class SetStoreys
{
    /// <summary>One storey of the set: the level above, the level below, the median drawn height, how many sheets stated it, and their spread.</summary>
    public sealed record SetStorey(string Level, string LevelBelow, double HeightMm, int Sheets, double SpreadMm);

    public sealed record Table(IReadOnlyList<SetStorey> Storeys, int ElevationSheets, int SheetsWithStoreys);

    /// <summary>Sheets stating one storey more than this apart are a disagreement worth a line.</summary>
    public const double AgreeMm = 25.0;

    public static Table Read(string pdfPath)
    {
        ArgumentNullException.ThrowIfNull(pdfPath);
        using var doc = PdfDocument.Open(pdfPath);
        var facts = DocumentFacts.From(doc);
        var perSheet = new List<(int Page, IReadOnlyList<StoreyLadder.Storey> Storeys)>();
        int elevationSheets = 0;
        for (int page = 1; page <= facts.Pages; page++)
        {
            VectorPageReader.PageContent content;
            try { content = VectorPageReader.ReadPage(doc.GetPage(page)); } catch { continue; }
            if (content.Words.Count == 0) continue;
            string? bookmark = facts.Bookmarks.TryGetValue(page, out var bt) ? bt : null;
            var fields = TitleBlockFields.Read(content);
            string? fieldTitle = fields.TryGetValue("SHEET TITLE", out var ft) ? ft : fields.TryGetValue("DRAWING TITLE", out ft) ? ft : null;
            string type = DrawingIntake.FirstTyped(bookmark, fieldTitle);
            if (type is "other" or "unknown")
            {
                string? titleText = null;
                try { titleText = SheetTitleReader.TitleText(content); } catch { }
                type = DrawingIntake.FirstTyped(titleText);
            }
            if (type != "section/elevation") continue;
            elevationSheets++;
            string? scale = null;
            try { scale = SheetScaleReader.FromPage(content); } catch { }
            scale ??= SheetScaleReader.RatioOf(fields.TryGetValue("SCALE", out var sf) ? sf : null);
            var storeys = StoreyLadder.Read(content, scale, ViewCaptions.Read(content));
            if (storeys.Count > 0) perSheet.Add((page, storeys));
        }
        return Reconcile(perSheet, elevationSheets);
    }

    /// <summary>
    /// One row per (level, level below) pair in first-seen order: the median of what the sheets
    /// state, how many stated it, and the spread between the smallest and largest statement.
    /// </summary>
    public static Table Reconcile(IReadOnlyList<(int Page, IReadOnlyList<StoreyLadder.Storey> Storeys)> perSheet, int elevationSheets)
    {
        ArgumentNullException.ThrowIfNull(perSheet);
        var order = new List<(string, string)>();
        var heights = new Dictionary<(string, string), List<double>>();
        foreach (var (_, storeys) in perSheet)
            foreach (var s in storeys)
            {
                var key = (s.Level, s.LevelBelow);
                if (!heights.TryGetValue(key, out var list)) { heights[key] = list = new List<double>(); order.Add(key); }
                list.Add(s.HeightMm);
            }
        var rows = new List<SetStorey>();
        foreach (var key in order)
        {
            var hs = heights[key]; hs.Sort();
            double median = hs.Count % 2 == 1 ? hs[hs.Count / 2] : (hs[hs.Count / 2 - 1] + hs[hs.Count / 2]) / 2.0;
            rows.Add(new SetStorey(key.Item1, key.Item2, median, hs.Count, hs[^1] - hs[0]));
        }
        return new Table(rows, elevationSheets, perSheet.Count);
    }

    /// <summary>A level and its elevation above the lowest stated level, in millimetres; and how it got there.</summary>
    public sealed record Level(string Name, double ElevationMm, string From);

    /// <summary>The levels a set states, chained from its lowest level at 0, and what the chain had to say for itself.</summary>
    public sealed record Chain(IReadOnlyList<Level> Levels, IReadOnlyList<string> Bases, IReadOnlyList<string> Unchained,
        IReadOnlyList<string> NamedTwice, IReadOnlyList<string> Breaks, double? TypicalMm);

    /// <summary>"L13", "B-L27": a numbered level, with the building it is named for in front.</summary>
    private static readonly Regex Numbered = new(@"^(?<b>[A-Z]{1,2}-)?L(?<n>\d{1,3})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// THE LEVELS, chained from the lowest stated level at 0 (intake step 25, from the pdf-levels
    /// verb where it lived untested).
    ///
    /// A level named twice with two different levels below it is one name for two storeys; the
    /// first stated wins and the rest are reported. A storey stated from one level to another that
    /// skips names — LEVEL 13 drawn straight above LEVEL 3 with a break line between them, which
    /// is how a drafter draws a run of typical storeys once (31065's S3.14; 31138's LEVEL 17 over
    /// LEVEL 7) — is a BREAK, not a height: the drawn gap is the break's, and the levels it skips
    /// stand at the set's typical storey each, said so in <see cref="Level.From"/>. A storey from
    /// one building's level to another's is nobody's and is not chained.
    /// </summary>
    public static Chain Levels(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var twice = table.Storeys.GroupBy(s => s.Level, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} ({string.Join(" / ", g.Select(s => $"over {s.LevelBelow} {s.HeightMm:0} mm"))})").ToList();
        // one statement per name: the first that stays in its own building, else the first
        var first = table.Storeys.GroupBy(s => s.Level, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.FirstOrDefault(s => SameBuilding(s.Level, s.LevelBelow)) ?? g.First(), StringComparer.OrdinalIgnoreCase);

        // the typical storey: the height the set repeats most, among storeys that are not breaks
        double? typical = null;
        var plain = table.Storeys.Where(s => Skipped(s.Level, s.LevelBelow) is null && SameBuilding(s.Level, s.LevelBelow)).ToList();
        if (plain.Count > 0)
            typical = plain.GroupBy(s => Math.Round(s.HeightMm / 5.0) * 5.0).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;

        // the base is a level that is below something and above nothing the set states
        var bases = table.Storeys.Select(s => s.LevelBelow).Where(b => !first.ContainsKey(b)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var elevation = new Dictionary<string, (double Mm, string From)>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in bases) elevation[b] = (0, "the lowest stated level");
        var breaks = new List<string>();

        for (bool moved = true; moved;)
        {
            moved = false;
            foreach (var s in first.Values)
            {
                if (elevation.ContainsKey(s.Level) || !elevation.TryGetValue(s.LevelBelow, out var below)) continue;
                if (!SameBuilding(s.Level, s.LevelBelow)) continue;
                var skipped = Skipped(s.Level, s.LevelBelow);
                if (skipped is null)
                {
                    elevation[s.Level] = (below.Mm + s.HeightMm, $"{s.HeightMm:0} mm over {s.LevelBelow}, drawn");
                    moved = true;
                    continue;
                }
                if (typical is not double t) continue;
                // a break: the levels it skips at the typical storey, and the level above them too
                var (prefix, from, to) = skipped.Value;
                double mm = below.Mm;
                for (int n = from + 1; n <= to; n++)
                {
                    string name = $"{prefix}L{n}";
                    // a level in the run another sheet drew keeps its drawn elevation, and the
                    // run continues from it
                    if (elevation.TryGetValue(name, out var drawn)) { mm = drawn.Mm; continue; }
                    mm += t;
                    elevation[name] = (mm, $"{t:0} mm typical storey, not drawn: the elevation breaks between {s.LevelBelow} and {s.Level}");
                }
                breaks.Add($"{s.Level} drawn {s.HeightMm:0} mm over {s.LevelBelow}: a break, {to - from - 1} level(s) between them at the typical {t:0} mm");
                moved = true;
            }
        }

        var levels = elevation.OrderBy(kv => kv.Value.Mm).Select(kv => new Level(kv.Key, kv.Value.Mm, kv.Value.From)).ToList();
        var unchained = table.Storeys.Where(s => !elevation.ContainsKey(s.Level)).Select(s => $"{s.Level} over {s.LevelBelow}").Distinct().ToList();
        return new Chain(levels, bases, unchained, twice, breaks, typical);

        // a numbered level over a numbered level of the same building more than one name apart:
        // the prefix, and the two numbers
        static (string Prefix, int From, int To)? Skipped(string level, string below)
        {
            var a = Numbered.Match(level.Trim()); var b = Numbered.Match(below.Trim());
            if (!a.Success || !b.Success) return null;
            int to = int.Parse(a.Groups["n"].Value), from = int.Parse(b.Groups["n"].Value);
            if (to - from <= 1) return null;
            return (a.Groups["b"].Value.ToUpperInvariant(), from, to);
        }

        // both named for a building and not the same one: a tower's level over another tower's
        static bool SameBuilding(string level, string below)
        {
            var a = Numbered.Match(level.Trim()); var b = Numbered.Match(below.Trim());
            if (!a.Success || !b.Success) return true;
            string pa = a.Groups["b"].Value, pb = b.Groups["b"].Value;
            return pa.Length == 0 || pb.Length == 0 || string.Equals(pa, pb, StringComparison.OrdinalIgnoreCase);
        }
    }
}
