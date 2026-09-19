using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// AN INSTRUMENT, NOT A GATE (2026-09-18, step 125): the plan-view titles <see cref="SheetViews.Titles"/> finds on one
/// page of one PDF, and the underlined lines it passed over — written to
/// <c>TestResults/sheet-views/&lt;pdf stem&gt;-p&lt;page&gt;.txt</c>. Built for 30838's S2.28, whose upper title wraps to two
/// lines; the durable form is a <c>takeoff sheet-views</c> verb. Runs only when <c>KOR_VIEWS_PDF</c> and
/// <c>KOR_VIEWS_PAGE</c> are set:
/// <code>KOR_VIEWS_PDF=&lt;stick file&gt; KOR_VIEWS_PAGE=49 dotnet test --filter FullyQualifiedName~SheetViewsProbe</code>
/// </summary>
[Trait("Speed", "Slow")]
[Collection(SheetNamingVocabularyCollection.Name)]
public sealed class SheetViewsProbe
{
    [Fact]
    public void ListTheViewsOfOnePage()
    {
        string? pdf = Environment.GetEnvironmentVariable("KOR_VIEWS_PDF");
        string? pageText = Environment.GetEnvironmentVariable("KOR_VIEWS_PAGE");
        if (string.IsNullOrWhiteSpace(pdf) || !int.TryParse(pageText, out int page)) return;
        // the set build's vocabulary (KorStandards, when the connection is set), so NamesAPlan judges as the build does
        string? conn = Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(conn)) _ = PdfIntakeOptions.For(conn);
        var content = VectorPageReader.ReadPage(pdf, page);
        var lines = new List<string> { $"{Path.GetFileName(pdf)} p{page}: {content.WidthPts:0} x {content.HeightPts:0} pt, {content.Words.Count} words, {content.Paths.Count} paths" };
        var views = SheetViews.Titles(content);
        lines.Add($"views found: {views.Count}");
        foreach (var v in views) lines.Add($"  view \"{v.Title}\" x {v.MinXPts:0}..{v.MaxXPts:0} underline y {v.YPts:0.0} pt (from the bottom)");
        // every horizontal two-point stroke wide enough to be an underline, with the words just above it
        lines.Add("underline-shaped strokes (x0..x1 @ y) and the words within 1.5 line-heights above:");
        foreach (var p in content.Paths)
        {
            if (!p.IsStroked || p.IsFilled || p.IsAnnotation || p.Points.Count != 2) continue;
            var a = p.Points[0]; var b = p.Points[1];
            if (Math.Abs(b.Y - a.Y) > 1.0 || Math.Abs(b.X - a.X) < 100) continue;
            double x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X), y = (a.Y + b.Y) / 2;
            var words = content.Words.Where(w => w.MinX <= x1 && w.MaxX >= x0 && w.MinY >= y - 2 && w.MinY <= y + 1.5 * (w.MaxY - w.MinY) + 2)
                .OrderBy(w => w.MinX).Select(w => w.Text).ToList();
            if (words.Count == 0) continue;
            lines.Add($"  {x0:0}..{x1:0} @ {y:0.0}: {string.Join(" ", words)}");
        }
        // the filled rectangles that could be a drawn-thick underline, for the case the reader has them as fills
        int thickRules = content.Paths.Count(p => p.IsFilled && !p.IsAnnotation && p.Points.Count is 4 or 5
            && p.Points.Max(q => q.X) - p.Points.Min(q => q.X) >= 100 && p.Points.Max(q => q.Y) - p.Points.Min(q => q.Y) <= 3);
        lines.Add($"filled rules (rectangles 100+ pt wide, 3 pt or less tall): {thickRules}");
        string dir = Path.Combine(AppContext.BaseDirectory, "TestResults", "sheet-views");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(pdf)}-p{page}.txt"), lines);
    }
}
