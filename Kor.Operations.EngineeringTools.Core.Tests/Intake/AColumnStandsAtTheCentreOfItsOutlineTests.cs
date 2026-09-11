#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A column stands at the centre of its outline (intake step 39). A closed subpath from the PDF
/// carries its corners once — the close is a command, not a repeated point — and a centroid weighted
/// over the three drawn edges without the fourth put every column read by shape off centre towards
/// its last-drawn side: a 400 x 400 column 67 mm over, a 900 x 1200 pattern cell 180 mm. Against the
/// Revit route's 31168 model, frames matched by grid name, the median column residual went from
/// 100 mm to 18 mm and the share within 50 mm from 7% to 71% (2026-09-10).
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a four-corner rectangle's centroid at its centre whether or not the first
/// corner is repeated; a two-point polyline (not a ring) unchanged; a column read by shape placed at
/// the centre of its box. WHAT IT DOES NOT: the yardstick numbers above (`columns_vs_yardstick.py`,
/// run by hand); the DXF side's own PlanLoop.Centroid, which closes its ring already.
/// </remarks>
public sealed class AColumnStandsAtTheCentreOfItsOutlineTests
{
    [Fact]
    public void AClosedRingsCentroidIsItsCentreWithOrWithoutTheRepeatedCorner()
    {
        var once = new List<(double X, double Y)> { (0, 0), (900, 0), (900, 1200), (0, 1200) };
        var repeated = new List<(double X, double Y)> { (0, 0), (900, 0), (900, 1200), (0, 1200), (0, 0) };
        var c1 = PolygonProcessor.Centroid(once);
        var c2 = PolygonProcessor.Centroid(repeated);
        Assert.Equal(450, c1.X, 1e-6); Assert.Equal(600, c1.Y, 1e-6);
        Assert.Equal(450, c2.X, 1e-6); Assert.Equal(600, c2.Y, 1e-6);
    }

    [Fact]
    public void ATwoPointPolylineIsNotARing()
    {
        var c = PolygonProcessor.Centroid([(0, 0), (1000, 0)]);
        Assert.Equal(500, c.X, 1e-6); Assert.Equal(0, c.Y, 1e-6);
    }

    [Fact]
    public void AColumnReadByShapeStandsAtTheCentreOfItsBox()
    {
        var fates = new List<PathFate>();
        var g = FateFixture.Classify([FateFixture.Rect(400, 400, 63000, 30000)], fates);
        var column = Assert.Single(g.Columns);
        Assert.Equal(63200, column.X, 1e-6); Assert.Equal(30200, column.Y, 1e-6);
    }
}
