#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A MATCH LINE CLOSES THE OUTLINE OF A PART PLAN (intake step 117, 2026-09-17). A plan too wide for one sheet is cut on
/// a match line and drawn as two halves; on each half the floor's outline is open where the seam runs - no slab edge is
/// drawn there because the floor continues on the other sheet - so no cell closed and the half read no plate: 70061-01's
/// P1 north read 507 sq ft of her 59,027 (its west and north are walls, its east and south the match lines); with the
/// match line in the arrangement 31,093, and the south half 30,106. The reader already knew the match line (the
/// furniture reads the words MATCH LINE and the line under them, and lines on it take the MatchLine fate); the composer
/// already joins floors across the seam (MatchLineSheetJoin); the arrangement had never seen the line.
/// WHAT THIS COVERS: a floor whose west, east and south edges are drawn and whose north side is the sheet's match line
/// (the fixture's, at y = 48,000), cut by one line so the slab pass starts: nothing without the rule (both cells open to
/// the page), one 12 x 8 m plate to the match line with it. WHAT IT DOES NOT: the join of the two halves into one floor
/// (MatchLineSheetJoin's own tests); the real sets (the six-set gate; 70061-01 and 31065's P1/P2 on the corpus).
/// </summary>
public sealed class AMatchLineClosesAPartPlansOutlineTests
{
    private const double X0 = 40000, Y0 = 40000, X1 = 52000, Seam = 48000;   // the floor, 12 x 8 m, its north side on the match line

    private static RawSubpath Line(double x0, double y0, double x1, double y1) => FateFixture.Line(x0, y0, x1, y1);
    private static RawSubpath Column(double x, double y) => FateFixture.Rect(600, 800, x, y);
    private static ExtractedGeometry Read(params RawSubpath[] paths) => FateFixture.Classify(paths.ToList(), new List<PathFate>());
    private static double AreaM2(List<(double X, double Y)> ring) => Math.Abs(PolygonProcessor.PolygonAreaMm2(ring)) / 1e6;

    [Fact]
    public void TheHalfReadsToTheMatchLine()
    {
        var g = Read(
            Line(X0, Y0, X0, Seam), Line(X1, Y0, X1, Seam), Line(X0, Y0, X1, Y0),   // west, east, south; nothing drawn on the seam
            Line(46000, Y0, 46000, Seam),                                             // a line across the floor: two cells, both open to the page without the seam
            Column(42000, 42000), Column(49000, 42000), Column(42000, 46000), Column(49000, 46000));
        var plate = Assert.Single(g.Slabs);
        Assert.InRange(AreaM2(plate), 96 * 0.99, 96 * 1.01);   // 12 x 8 m, to the match line
    }
}
