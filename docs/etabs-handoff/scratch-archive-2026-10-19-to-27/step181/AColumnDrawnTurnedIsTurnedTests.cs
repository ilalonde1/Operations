#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A column drawn turned is turned (intake step 178, 2026-10-25). The reader sized every column by its box on the page's
/// axes and the DXF wrote that box square to the page, so a column on a skewed grid went to ETABS square to the page and
/// oversized: on the engineer's models 34 of 31065's oblong columns stood 8 degrees off hers and 5 of 31130's 17.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a filled 12 x 30 drawn at 30 degrees - read at its own size with its turn, written turned, and read
/// back by the classifier as a 12 x 30 at 30 degrees; the same column square to the page keeps 0. WHAT IT DOES NOT: the
/// declared-size path's turn (the same Bearing of the same box), the ETABS angle (AColumnsAngIsTheBearingOfItsLongFace).
/// A fault it would NOT catch: a page whose y axis runs down, which would turn every column the other way - the reader
/// and the DXF share the page's frame, so walls and columns would turn together.
/// </remarks>
public sealed class AColumnDrawnTurnedIsTurnedTests
{
    private static RawSubpath Rectangle(double w, double d, double degrees)
    {
        double t = degrees * Math.PI / 180, ux = Math.Cos(t), uy = Math.Sin(t);
        (double, double) P(double a, double b) => (a * ux - b * uy, a * uy + b * ux);
        var pts = new List<(double X, double Y)> { P(-w / 2, -d / 2), P(w / 2, -d / 2), P(w / 2, d / 2), P(-w / 2, d / 2) };
        return new(pts, true, (0xD0, 0xD0, 0xD0), true, false, 0.5, false);
    }

    [Fact]
    public void ATurnedColumnIsReadAtItsOwnSizeAndTurnAndOneSquareToThePageIsNot()
    {
        var turned = WallFixture.Read([Rectangle(762, 305, 30)]).Geometry;   // 30" x 12" in mm
        Assert.Equal(30, Assert.Single(turned.ColumnTurnDegrees), 1);
        var (w, d) = Assert.Single(turned.ColumnSizes);
        Assert.Equal(762, Math.Max(w, d), 0);
        Assert.Equal(305, Math.Min(w, d), 0);

        var square = WallFixture.Read([Rectangle(762, 305, 0)]).Geometry;
        Assert.Equal(0, Assert.Single(square.ColumnTurnDegrees));
    }

    /// <summary>
    /// 31065 draws its columns on the 8-degree grid as a fill square to the page painted through a clip that IS the column,
    /// turned (the export's way, as its round columns and its slanted walls). The fill's box is 468 x 915 for a 350 x 875.
    /// </summary>
    [Fact]
    public void ASquareFillShownThroughATurnedClipIsTheClipTurned()
    {
        var clip = Rectangle(875, 350, 82) with { IsFilled = false, IsClipping = true, PathOrdinal = 0 };
        var cp = clip.Points;
        double x0 = cp.Min(p => p.X), x1 = cp.Max(p => p.X), y0 = cp.Min(p => p.Y), y1 = cp.Max(p => p.Y);
        var fill = new RawSubpath(new List<(double X, double Y)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1) }, true, (0xD0, 0xD0, 0xD0), true, false, 0.5, false)
            { PathOrdinal = 1 };
        var g = WallFixture.Read([clip, fill]).Geometry;
        var (w, d) = Assert.Single(g.ColumnSizes);
        Assert.Equal(875, Math.Max(w, d), 0);
        Assert.Equal(350, Math.Min(w, d), 0);
        Assert.Equal(82, ((Assert.Single(g.ColumnTurnDegrees) % 180) + 180) % 180, 1);
    }

    [Fact]
    public void ATurnedColumnLeavesTheReaderTurnedAndTheClassifierReadsItsBearing()
    {
        var g = new ExtractedGeometry();
        g.Columns.Add((5000, 5000)); g.ColumnColors.Add((0xD0, 0xD0, 0xD0)); g.ColumnIsAnnotation.Add(false);
        g.ColumnSizes.Add((762, 305)); g.ColumnIsRound.Add(false); g.ColumnTurnDegrees.Add(30);
        string path = Path.Combine(Path.GetTempPath(), $"turned-column-{Guid.NewGuid():N}.dxf");
        try
        {
            DxfExporter.Export(g, path, korLayers: true);
            var inches = DxfPlanReader.ReadSegments(path)
                .Select(s => s with { Start = new DxfPoint(s.Start.X / 25.4, s.Start.Y / 25.4), End = new DxfPoint(s.End.X / 25.4, s.End.Y / 25.4) })
                .ToList();
            var column = Assert.Single(StructuralPlanClassifier.Classify(inches, new PlanClassificationOptions()).Columns);
            Assert.InRange(column.Depth, 29.5, 30.5);
            Assert.InRange(column.Width, 11.5, 12.5);
            Assert.InRange(((column.AxisAngleDegrees % 180) + 180) % 180, 29, 31);
        }
        finally { File.Delete(path); }
    }
}
