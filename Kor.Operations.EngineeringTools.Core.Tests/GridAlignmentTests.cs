using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The numbers here are 31168's, read off the drawings and off the engineer's own reference model
/// on 2026-08-31 -- not invented for the test. If the fingerprint stops matching, the alignment is
/// wrong about a real building.
/// </summary>
public class GridAlignmentTests
{
    /// <summary>The 19 X grids of 31168-reference-SHELL.e2k, in inches.</summary>
    private static readonly double[] ReferenceX =
    {
        -1379.574, -1286.193, -1144.948, -818.9482, -492.9482, -166.9482, 120.0485, 159.0485,
        334.0485, 522.0485, 716.5485, 848.0485, 934.5485, 1174.048, 1500.048, 1826.048,
        2152.148, 2478.148, 2605.148,
    };

    /// <summary>Its two Y grids -- labelled R and A, the site's extremes.</summary>
    private static readonly double[] ReferenceY = { 2439.5, 5232.0 };

    /// <summary>
    /// The constant-y grid lines on LEVEL P1 PLAN - CONCRETE OUTLINE.dxf. Nineteen of them, whose
    /// spacings are the reference X spacings in reverse.
    /// </summary>
    private static readonly double[] DrawingConstantY =
    {
        27086.7, 27213.7, 27539.7, 27865.7, 28191.7, 28517.7, 28757.2, 28843.7, 28975.2,
        29169.7, 29357.7, 29532.7, 29571.7, 29858.7, 30184.7, 30510.7, 30836.7, 30977.9, 31071.3,
    };

    /// <summary>Its constant-x grid lines. Sixteen drawn; the model labels only the two extremes.</summary>
    private static readonly double[] DrawingConstantX =
    {
        38845.2, 39108.7, 39388.7, 39399.7, 39669.7, 39679.7, 39959.7, 40239.7,
        40460.2, 40740.2, 40932.7, 40974.7, 41054.7, 41061.7, 41341.7, 41637.7,
    };

    private static List<DxfSegment> SiteGridLinework()
    {
        var segments = new List<DxfSegment>();
        foreach (double y in DrawingConstantY)
            segments.Add(new DxfSegment("JBP_G_GRID-1", new DxfPoint(38800, y), new DxfPoint(41700, y)));
        foreach (double x in DrawingConstantX)
            segments.Add(new DxfSegment("JBP_G_GRID-1", new DxfPoint(x, 27000), new DxfPoint(x, 31100)));
        return segments;
    }

    [Fact]
    [Trait("Speed", "Fast")]
    public void TheDrawingIsRecognisedAsHerGridTurnedNinetyDegrees()
    {
        var fit = GridAlignment.Solve(SiteGridLinework(), ReferenceX, ReferenceY);

        Assert.NotNull(fit);
        Assert.Equal(90.0, fit!.Frame.RotationDegrees);

        // X = 29691.7 - y  and  Y = x - 36405.7, derived from the grid fingerprint and checked
        // against both spans: 3984.6 drawn against 3984.7 modelled.
        // Within an inch: grid coordinates are drafted to a tenth and the fit averages them.
        Assert.InRange(fit.Frame.OffsetX, 29691.7 - 1.0, 29691.7 + 1.0);
        Assert.InRange(fit.Frame.OffsetY, -36405.7 - 1.0, -36405.7 + 1.0);

        Assert.Equal(19, fit.MatchedX);
        Assert.Equal(2, fit.MatchedY);
    }

    [Fact]
    [Trait("Speed", "Fast")]
    public void TheFrameCarriesTheDrawingOntoTheEngineersGrid()
    {
        var fit = GridAlignment.Solve(SiteGridLinework(), ReferenceX, ReferenceY);

        // The corner of the drawing's grid lands on grid 1 / grid R, to within a drafted inch.
        var corner = fit!.Frame.Apply(new DxfPoint(DrawingConstantX[0], DrawingConstantY[^1]));
        Assert.InRange(corner.X, ReferenceX[0] - 1.0, ReferenceX[0] + 1.0);
        Assert.InRange(corner.Y, ReferenceY[0] - 1.0, ReferenceY[0] + 1.0);

        // And the far corner on grid 19 / grid A. Both ends, or it is a translation that happens
        // to suit one of them.
        var far = fit.Frame.Apply(new DxfPoint(DrawingConstantX[^1], DrawingConstantY[0]));
        Assert.InRange(far.X, ReferenceX[^1] - 1.0, ReferenceX[^1] + 1.0);
        Assert.InRange(far.Y, ReferenceY[^1] - 1.0, ReferenceY[^1] + 1.0);
    }

    [Fact]
    [Trait("Speed", "Fast")]
    public void ADrawingAlreadyOnTheGridIsLeftWhereItIs()
    {
        // Same linework, already in model coordinates: the answer must be no rotation and no move,
        // because a drawing this tool has not had to touch must not be touched.
        var segments = new List<DxfSegment>();
        foreach (double x in ReferenceX)
            segments.Add(new DxfSegment("S-GRID", new DxfPoint(x, 2400), new DxfPoint(x, 5300)));
        foreach (double y in ReferenceY)
            segments.Add(new DxfSegment("S-GRID", new DxfPoint(-1400, y), new DxfPoint(2650, y)));

        var fit = GridAlignment.Solve(segments, ReferenceX, ReferenceY);

        Assert.NotNull(fit);
        Assert.Equal(0.0, fit!.Frame.RotationDegrees);
        Assert.InRange(fit.Frame.OffsetX, -1.0, 1.0);
        Assert.InRange(fit.Frame.OffsetY, -1.0, 1.0);
    }

    [Fact]
    [Trait("Speed", "Fast")]
    public void WithNoGridToMatchItRefusesRatherThanGuesses()
    {
        // A wrong rotation looks deliberate and is worse than none, so too little evidence must
        // return nothing at all rather than a best effort.
        var noGrid = new List<DxfSegment>
        {
            new("JBP_V-WALL", new DxfPoint(0, 0), new DxfPoint(500, 0)),
            new("JBP_V_COL", new DxfPoint(0, 0), new DxfPoint(0, 500)),
        };

        Assert.Null(GridAlignment.Solve(noGrid, ReferenceX, ReferenceY));
        Assert.Null(GridAlignment.Solve(SiteGridLinework(), Array.Empty<double>(), Array.Empty<double>()));
    }
}
