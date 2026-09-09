#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;
using Frame = Kor.Operations.EngineeringTools.Dxf.AnnotationOverlay.Frame;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A sheet from the stick file sits on the model's grid by the names of its axes (intake step 15):
/// each sheet gets its own frame, matched name to name against the model's GRIDS table, in the
/// model's unit, at whichever quarter turn the names agree.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a grid-layer text names the line whose end it sits at, and not one far away or
/// a word too long to be a name; a millimetre sheet lands on an inch model at 0° with the frame
/// carrying a named point onto its grid; a sheet drawn a quarter turn from the model is turned;
/// too few names, and names whose positions disagree, give no fit; the GRIDS table written from
/// two sheets' axes through their frames. WHAT IT DOES NOT: a real sheet against a real model —
/// TheStickFileBuildsAModelOnItsGridTests does that on 31168 — and a mirrored sheet, which the
/// frame cannot express.
/// </remarks>
public sealed class ASheetSitsOnTheModelsGridByNameTests
{
    private const double MmPerIn = 25.4, S = 1 / MmPerIn;   // drawing in mm, model in inches

    private static DxfSegment V(double x, double y0 = 0, double y1 = 20000) => new("GRID", new DxfPoint(x, y0), new DxfPoint(x, y1));
    private static DxfSegment H(double y, double x0 = 0, double x1 = 30000) => new("GRID", new DxfPoint(x0, y), new DxfPoint(x1, y));
    private static DxfPositionedTag T(string text, double x, double y) => new(text, new DxfPoint(x, y), "GRID", text) { Height = 300 };

    [Fact]
    public void AGridTextNamesTheLineWhoseEndItSitsAt()
    {
        var axes = GridAlignment.NamedAxes(
            [V(1000), V(4000), H(2000)],
            [T("1", 1000, 20000), T("2", 4000, 0), T("R", 0, 2000), T("9", 12000, 20000), T("GRID", 4000, 20000)]);
        Assert.Equal(3, axes.Count);
        Assert.Contains(axes, a => a.Name == "1" && a.Vertical && a.At == 1000);
        Assert.Contains(axes, a => a.Name == "2" && a.Vertical && a.At == 4000);
        Assert.Contains(axes, a => a.Name == "R" && !a.Vertical && a.At == 2000);
    }

    [Fact]
    public void AMillimetreSheetLandsOnAnInchModelByName()
    {
        var axes = new List<GridAlignment.NamedAxis> { new("1", true, 1000), new("2", true, 3540), new("3", true, 9000), new("R", false, 2000) };
        var model = new List<GridAlignment.ReferenceGrid>
        {
            new("1", true, -100), new("2", true, -100 + 2540 * S), new("3", true, -100 + 8000 * S), new("9", true, 500),
            new("R", false, 50), new("A", false, 400),
        };
        var fit = GridAlignment.SolveByName(axes, model, S);
        Assert.NotNull(fit);
        Assert.Equal(0, fit!.Frame.RotationDegrees);
        Assert.Equal(3, fit.MatchedX);
        Assert.Equal(1, fit.MatchedY);
        var onGrid = fit.Frame.Apply(new DxfPoint(1000 * S, 2000 * S));          // the corner of axes 1 and R, scaled first
        Assert.Equal(-100, onGrid.X, 3);
        Assert.Equal(50, onGrid.Y, 3);
    }

    [Fact]
    public void ASheetDrawnAQuarterTurnFromTheModelIsTurned()
    {
        // the sheet's vertical lines carry the model's Y labels: it is drawn to plan north, a
        // quarter turn from the model — turning the sheet +90° about the origin puts 1 left of 2
        // only if 2 sits BELOW 1 on the sheet (x' = -y), and A above R only if A is right of R (y' = x)
        var axes = new List<GridAlignment.NamedAxis> { new("R", true, 1000), new("A", true, 5000), new("1", false, 6000), new("2", false, 2000) };
        var model = new List<GridAlignment.ReferenceGrid> { new("1", true, 0), new("2", true, 4000), new("R", false, 100), new("A", false, 4100) };
        var fit = GridAlignment.SolveByName(axes, model);
        Assert.NotNull(fit);
        Assert.Equal(4, fit!.MatchedX + fit.MatchedY);
        Assert.Equal(90, fit.Frame.RotationDegrees);
        // the corner of R (x=1000) and 1 (y=6000) lands where the model draws R and 1
        var corner = fit.Frame.Apply(new DxfPoint(1000, 6000));
        Assert.Equal(0, corner.X, 3);
        Assert.Equal(100, corner.Y, 3);
    }

    [Fact]
    public void TooFewNamesOrDisagreeingPositionsGiveNoFit()
    {
        var model = new List<GridAlignment.ReferenceGrid> { new("1", true, 0), new("2", true, 100), new("3", true, 250), new("R", false, 50) };
        Assert.Null(GridAlignment.SolveByName([new("1", true, 0), new("R", false, 0)], model));
        // three names on X but their spacings are nothing like the model's: only one agrees with the median
        Assert.Null(GridAlignment.SolveByName([new("1", true, 0), new("2", true, 900), new("3", true, 950), new("R", false, 0)], model));
    }

    [Fact]
    public void TheGridsTableIsTheSheetsOwnAxesThroughTheirFrames()
    {
        var a = new List<GridAlignment.NamedAxis> { new("1", true, 1000), new("2", true, 3540), new("R", false, 2000) };
        var b = new List<GridAlignment.NamedAxis> { new("1", true, 1000 + 25.4 * 0.2), new("R", false, 2000) };
        var frameA = new Frame(0, -100 - 1000 * S, 50 - 2000 * S);
        var frameB = new Frame(0, -100 - 1000 * S, 50 - 2000 * S);
        var lines = GridAlignment.GridLines([((IReadOnlyList<GridAlignment.NamedAxis>)a, frameA, S), (b, frameB, S)]);
        Assert.Equal(4, lines.Count);
        Assert.StartsWith("  GRIDSYSTEM \"G1\"", lines[0]);
        Assert.Contains(lines, l => l.Contains("LABEL \"1\"  DIR \"X\"  COORD -100 ") || l.Contains("LABEL \"1\"  DIR \"X\"  COORD -99.9"));
        Assert.Contains(lines, l => l.Contains("LABEL \"2\"  DIR \"X\"  COORD 0 ") || l.Contains("LABEL \"2\"  DIR \"X\"  COORD -0 "));
        Assert.Contains(lines, l => l.Contains("LABEL \"R\"  DIR \"Y\"  COORD 50 "));
    }
}
