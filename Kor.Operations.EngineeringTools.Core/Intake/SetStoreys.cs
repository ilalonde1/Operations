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
            var storeys = StoreyLadder.Read(content, scale);
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
}
