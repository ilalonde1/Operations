#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A column drawn with a curve is round (intake step 173, 2026-10-21). 31065 draws its 450 mm round columns (C16, C18,
/// C20, "450 Ø") as filled Bezier circles; the PDF walk flattened them to polygons with no word of the curve, the DXF
/// carried every column as a four-point rectangle, and the model priced each as a 450 mm square - 27% over.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: the reader's test - curve-drawn, square to a tenth, filling about pi/4 of its box - for a circle,
/// the same polygon not drawn with a curve, and a radiused square drawn with curves; and the whole hand-off, reader flag
/// to a full-circle ARC in the DXF to the classifier's arc rule, ending in a round footprint. WHAT IT DOES NOT: the page
/// walk's own setting of the flag from a real PDF (the six-set gate on 31065 is that), nor the e2k writer's circular
/// section (E2kGeometryComposer's own tests). A fault it would NOT catch: a round column drawn as a stroked ring with no
/// fill, which never becomes a column on the PDF side at all.
/// </remarks>
public sealed class AColumnDrawnWithACurveIsRoundTests
{
    private static List<(double X, double Y)> Circle(double r, int n = 32)
        => Enumerable.Range(0, n).Select(k => (r * Math.Cos(2 * Math.PI * k / n), r * Math.Sin(2 * Math.PI * k / n))).ToList();

    /// <summary>A 450 mm square with 60 mm radius corners, 8 points to each arc: fills about 0.98 of its box.</summary>
    private static List<(double X, double Y)> RadiusedSquare()
    {
        var pts = new List<(double X, double Y)>();
        const double h = 225, r = 60;
        foreach (var (cx, cy, a0) in new[] { (h - r, h - r, 0.0), (-(h - r), h - r, 90.0), (-(h - r), -(h - r), 180.0), (h - r, -(h - r), 270.0) })
            for (int k = 0; k <= 8; k++)
            {
                double a = (a0 + 90.0 * k / 8) * Math.PI / 180;
                pts.Add((cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
            }
        return pts;
    }

    private static RawSubpath Filled(List<(double X, double Y)> pts, bool curve)
        => new(pts, true, (0xD0, 0xD0, 0xD0), true, false, 0.5, false) { HasCurve = curve };

    [Fact]
    public void ACircleDrawnWithCurvesIsRoundAndTheSameShapeOrARadiusedSquareIsNot()
    {
        Assert.True(Assert.Single(WallFixture.Read([Filled(Circle(225), curve: true)]).Geometry.ColumnIsRound));
        Assert.False(Assert.Single(WallFixture.Read([Filled(Circle(225), curve: false)]).Geometry.ColumnIsRound));
        Assert.False(Assert.Single(WallFixture.Read([Filled(RadiusedSquare(), curve: true)]).Geometry.ColumnIsRound));
    }

    [Fact]
    public void ARoundColumnLeavesTheReaderAsAnArcAndTheClassifierModelsItRound()
    {
        var g = new ExtractedGeometry();
        g.Columns.Add((5000, 5000)); g.ColumnColors.Add((0xD0, 0xD0, 0xD0)); g.ColumnIsAnnotation.Add(false);
        g.ColumnSizes.Add((450, 450)); g.ColumnIsRound.Add(true);
        g.Columns.Add((9000, 5000)); g.ColumnColors.Add((0xD0, 0xD0, 0xD0)); g.ColumnIsAnnotation.Add(false);
        g.ColumnSizes.Add((450, 450)); g.ColumnIsRound.Add(false);
        string path = Path.Combine(Path.GetTempPath(), $"round-column-{Guid.NewGuid():N}.dxf");
        try
        {
            DxfExporter.Export(g, path, korLayers: true);
            // the DXF is in millimetres and the classifier reads inches, as the pipeline scales it
            var inches = DxfPlanReader.ReadSegments(path)
                .Select(s => s with { Start = new DxfPoint(s.Start.X / 25.4, s.Start.Y / 25.4), End = new DxfPoint(s.End.X / 25.4, s.End.Y / 25.4) })
                .ToList();
            Assert.Contains(inches, s => s.FromCurve);
            var plan = StructuralPlanClassifier.Classify(inches, new PlanClassificationOptions());
            Assert.Equal(2, plan.Columns.Count);
            var round = Assert.Single(plan.Columns, c => c.IsRound);
            Assert.InRange(round.Width, 17.3, 18.1);   // 450 mm
            Assert.Single(plan.Columns, c => !c.IsRound);
        }
        finally { File.Delete(path); }
    }
}
