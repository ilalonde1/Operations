using Kor.Operations.EngineeringTools.PdfToSafe;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// A RECTANGLE NO SCHEDULE DECLARES, LONGER THAN 24 IN AND TWICE AS LONG AS WIDE, IS A WALL PIER (intake step 99,
/// 2026-09-16). The engineers' review from the corpus (<c>corpus-disagreements</c>, run 26): of our columns her
/// models have no partner for, 622 over 37 sets STAND ON A WALL SHE MODELLED - a column to us, a pier to her - and
/// 483 of the 554 with a section named are 24 in or longer on their long side (14 x 40, 16 x 48, 23 x 54 on 31130;
/// 30 x 52 on 30993). That is her ruling W1 in the index - walls 24 in and over are walls - drawn as a filled
/// rectangle the column reader took whole. The set's own column schedule is the other half of the rule: 31130
/// schedules 14 x 36 and models it as a column, 31098 schedules 12 x 24 the same, and a size the schedule declares
/// is a column whatever its length. So: a filled rectangle whose size the sheet's or the set's column schedule
/// declares stays a column; one no schedule declares, longer than 24 in and at least twice as long as wide, is a
/// wall the rectangle's length and thickness, modelled as the wall panel she would draw. A set with no column
/// schedule at all is judged by the size alone, which is what her ruling says.
/// WHAT IT DOES NOT: a pier drawn with the wall's own hatch (the wall reader's, step 28); a square pier; a
/// rectangle the schedule declares with a varying size ("VARIES"), which counts as declared.
/// </summary>
public static class WallPiers
{
    /// <summary>Her W1: a wall is 24 in and over. The row dxf.pdf.pier-min-long-side-mm (migration 094); this is its compiled default.</summary>
    public const double DefaultPierMinLongSideMm = 609.6;
    /// <summary>A pier is at least twice as long as it is thick; a 12 x 24 is a column she schedules. The row dxf.pdf.pier-min-aspect (migration 094).</summary>
    public const double DefaultPierMinAspect = 2.0;

    /// <summary>
    /// Marks every undeclared long rectangle as a pier (<see cref="ExtractedGeometry.ColumnIsWallPier"/>, parallel to
    /// the columns), adds a wall panel for each, and returns how many stood down.
    /// </summary>
    public static int StandDownColumns(ExtractedGeometry geometry, IReadOnlyList<bool>? sizeIsDeclared,
        double pierMinLongSideMm = DefaultPierMinLongSideMm, double pierMinAspect = DefaultPierMinAspect)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        geometry.ColumnIsWallPier.Clear();
        int stoodDown = 0;
        for (int i = 0; i < geometry.Columns.Count; i++)
        {
            var (cx, cy) = geometry.Columns[i];
            var (w, d) = i < geometry.ColumnSizes.Count ? geometry.ColumnSizes[i] : (0.0, 0.0);
            bool declared = sizeIsDeclared is not null && i < sizeIsDeclared.Count && sizeIsDeclared[i];
            bool anchor = i < geometry.ColumnIsTendonAnchor.Count && geometry.ColumnIsTendonAnchor[i];
            double longSide = Math.Max(w, d), shortSide = Math.Min(w, d);
            bool pier = !declared && !anchor && shortSide > 0 && longSide > pierMinLongSideMm && longSide >= pierMinAspect * shortSide;
            geometry.ColumnIsWallPier.Add(pier);
            if (!pier) continue;
            stoodDown++;
            // the wall panel she would draw: the rectangle's long axis, its short side the thickness
            bool alongX = w >= d;
            (double X, double Y) s = alongX ? (cx - w / 2, cy) : (cx, cy - d / 2);
            (double X, double Y) e = alongX ? (cx + w / 2, cy) : (cx, cy + d / 2);
            var outline = new List<(double X, double Y)> { (cx - w / 2, cy - d / 2), (cx + w / 2, cy - d / 2), (cx + w / 2, cy + d / 2), (cx - w / 2, cy + d / 2) };
            geometry.Walls.Add(new WallPanel(outline, s, e, shortSide));
            geometry.WallColors.Add(i < geometry.ColumnColors.Count ? geometry.ColumnColors[i] : ((byte)0, (byte)0, (byte)0));
            geometry.WallIsAnnotation.Add(i < geometry.ColumnIsAnnotation.Count && geometry.ColumnIsAnnotation[i]);
        }
        return stoodDown;
    }
}
