using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// What a drawing SET schedules, read once before its sheets are: the assembly cards (step 32,
/// <see cref="AssemblySchedule"/>) and every column size any sheet's column schedule declares. One
/// pass over the pages, where <c>AssemblySchedule.ReadSet</c> was one pass for the cards alone.
/// </summary>
/// <remarks>
/// The column sizes are set-wide because a schedule is: 31130 schedules its columns on their own
/// sheet and draws them on post-tensioning plans where tendons end over them, and the tendon rule
/// (step 48) judging by the plan sheet's own schedule — it has none — stood ten real 14x36 columns
/// down on L16 (2026-09-12). A size the set declares anywhere is a column wherever it is drawn.
/// WHAT IT DOES NOT: a size the set never schedules (a stub, a post) — the tendon rule alone judges
/// those; a schedule the reader cannot parse (ColumnScheduleReader's own limits).
/// </remarks>
public static class SetSchedules
{
    public sealed record Read(IReadOnlyList<AssemblySchedule.Assembly> Assemblies, IReadOnlyList<ColumnScheduleRow> ColumnRows);

    public static Read Of(string pdfPath, IReadOnlyList<string> structuralWords, IReadOnlyList<string> partitionWords)
    {
        ArgumentNullException.ThrowIfNull(pdfPath);
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var facts = DocumentFacts.From(doc);
        var assemblies = new List<AssemblySchedule.Assembly>();
        var columns = new List<ColumnScheduleRow>();
        for (int page = 1; page <= facts.Pages; page++)
        {
            VectorPageReader.PageContent content;
            try { content = VectorPageReader.ReadPage(doc.GetPage(page)); } catch { continue; }
            if (content.Words.Count == 0) continue;
            // the cards, exactly as AssemblySchedule.ReadSet read them: on a sheet whose title says SCHEDULE
            string? bookmark = facts.Bookmarks.TryGetValue(page, out var bt) ? bt : null;
            var fields = TitleBlockFields.Read(content);
            string? fieldTitle = fields.TryGetValue("SHEET TITLE", out var ft) ? ft : fields.TryGetValue("DRAWING TITLE", out ft) ? ft : null;
            string? title = bookmark ?? fieldTitle;
            if (title is not null && title.Contains("SCHEDULE", StringComparison.OrdinalIgnoreCase))
                assemblies.AddRange(AssemblySchedule.Read(content, title, page, structuralWords, partitionWords));
            // the column schedule, on whatever sheet carries one (31202 puts it on the plan sheet itself)
            try { columns.AddRange(ColumnScheduleReader.ReadSchedule(content)); } catch { /* no table the reader can use on this page */ }
        }
        return new Read(assemblies, columns);
    }
}
