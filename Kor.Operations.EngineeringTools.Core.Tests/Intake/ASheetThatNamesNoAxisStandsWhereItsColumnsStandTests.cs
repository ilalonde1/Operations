#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A sheet that names no axis stands where its columns stand (intake step 55). 31168's LEVEL 35 PLAN -
/// BLDG A names two axes and its LEVEL 36 none, and both stayed "in their own frame" — wherever the
/// page put them, which was near the tower while the DXF's frame was the drawn content's centroid
/// and fifty metres off once the frame was the page's (§61: the core read as 18 walls in one frame
/// and 8 in the other). A tower's columns stack: the displacement most of a sheet's columns share
/// with the columns already placed is its frame.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a sheet's columns offset from the placed ones by one vector fit with that
/// vector and the support in the note, with strays in both sets ignored; fewer than four members
/// refuse, and so do members of which fewer than half agree; six at a spacing the placed grid does
/// not have refuse; the fit is at 0 degrees; a placed member standing on thirty storeys is one
/// place, and cannot outvote the columns (31168's 2,056 stacked wall corners). WHAT IT DOES NOT: a
/// sheet drawn a quarter turn from the model; the composer's loop that lends a placed sheet's
/// columns to the next (31168 in the six-set gate measures it); which storey the sheet feeds.
/// </remarks>
public sealed class ASheetThatNamesNoAxisStandsWhereItsColumnsStandTests
{
    private static List<DxfPoint> Grid(double x0, double y0, int nx, int ny, double bay = 8000) =>
        Enumerable.Range(0, nx).SelectMany(i => Enumerable.Range(0, ny).Select(j => new DxfPoint(x0 + i * bay, y0 + j * bay))).ToList();

    [Fact]
    public void TheDisplacementMostColumnsShareIsTheFrame()
    {
        // the tower's twelve columns, placed; the top plan draws ten of them in its own page frame, 40 m away, plus a stray
        var placed = Grid(50000, 40000, 4, 3);
        var sheet = Grid(10000, 5000, 4, 3).Take(10).Append(new DxfPoint(30000, 30000)).ToList();
        var fit = GridAlignment.SolveByColumns(sheet, placed);
        Assert.NotNull(fit);
        Assert.Equal(0, fit!.Frame.RotationDegrees);
        Assert.Equal(40000, fit.Frame.OffsetX, 1e-6);
        Assert.Equal(35000, fit.Frame.OffsetY, 1e-6);
        Assert.Contains("10 of its 11 columns and walls", fit.Note);
    }

    [Fact]
    public void TooFewColumnsOrNoSharedDisplacementIsNoFit()
    {
        var placed = Grid(50000, 40000, 4, 3);
        Assert.Null(GridAlignment.SolveByColumns(Grid(10000, 5000, 3, 1), placed));                      // three: a coincidence
        // seven members of which only three stand over placed ones: fewer than half agree, no fit
        var mostly = Grid(10000, 5000, 3, 1).Concat(Enumerable.Range(0, 4).Select(i => new DxfPoint(70000 + i * 3333, 1234 + i * 777))).ToList();
        Assert.Null(GridAlignment.SolveByColumns(mostly, placed));
        // six columns at a spacing the placed grid does not have: no bin gathers six
        var odd = Enumerable.Range(0, 6).Select(i => new DxfPoint(1000 + i * 3333, 2000 + i * 1111)).ToList();
        Assert.Null(GridAlignment.SolveByColumns(odd, placed));
    }

    [Fact]
    public void APlacedMemberOnThirtyStoreysIsOnePlace()
    {
        // twelve columns placed once, and one wall corner placed thirty times (a panel per storey) at a spot
        // where a single stray of the sheet's happens to land under a different displacement: 30 pairs on
        // that spot must not outvote the 12 columns' displacement
        var placed = Grid(50000, 40000, 4, 3).Concat(Enumerable.Repeat(new DxfPoint(90000, 90000), 30)).ToList();
        var sheet = Grid(10000, 5000, 4, 3).Append(new DxfPoint(30000, 30000)).ToList();
        var fit = GridAlignment.SolveByColumns(sheet, placed);
        Assert.NotNull(fit);
        Assert.Equal(40000, fit!.Frame.OffsetX, 1e-6);
        Assert.Equal(35000, fit.Frame.OffsetY, 1e-6);
        Assert.Contains("12 of its 13 columns and walls", fit.Note);
    }

    [Fact]
    public void AMillimetreSheetRegistersOnAnInchModel()
    {
        const double s = 1 / 25.4;
        var placed = Grid(2000, 1500, 3, 3, bay: 8000 * s);               // inches
        var sheet = Grid(100000, 60000, 3, 3);                              // millimetres, the same nine columns
        var fit = GridAlignment.SolveByColumns(sheet, placed, s);
        Assert.NotNull(fit);
        Assert.Equal(2000 - 100000 * s, fit!.Frame.OffsetX, 1e-6);
        Assert.Equal(1500 - 60000 * s, fit.Frame.OffsetY, 1e-6);
    }

    [Fact]
    public void RepeatedSheetPointsAreOnePlaceAndTwoPlacesThatFitAlikeAreRefused()
    {
        // four wall axes meeting at one junction are one point of the quorum, not four (Codex audit 2026-09-13, F5)
        var junction = Enumerable.Repeat(new DxfPoint(10000, 5000), 4).ToList();
        var placedFour = Enumerable.Repeat(new DxfPoint(50000, 40000), 4).Concat(Grid(50000, 40000, 2, 2)).ToList();
        Assert.Null(GridAlignment.SolveByColumns(junction, placedFour));
        // two towers with one core plan fifty metres apart: the same four columns fit both, and neither is chosen (F4)
        var towers = Grid(50000, 40000, 2, 2).Concat(Grid(100000, 40000, 2, 2)).ToList();
        Assert.Null(GridAlignment.SolveByColumns(Grid(10000, 5000, 2, 2), towers, 1.0, out string why));
        Assert.Contains("two places fit alike", why);
    }

    [Fact]
    public void ADisplacementStraddlingABinEdgeStillWins()
    {
        // five columns whose displacement, (40,050, 35,000), sits on a 100 mm bin edge and splits three-and-two across
        // it; four strays elsewhere share one whole bin. The fullest bin alone would have won for the strays (Codex
        // audit 2026-09-13, F9); every bin near the top is refined and judged by support
        var placed = Grid(50050, 40000, 5, 1).Concat(Grid(80000, 60000, 4, 1)).ToList();
        var sheet = new[] { new DxfPoint(10000 - 30, 5000), new DxfPoint(18000 - 30, 5000), new DxfPoint(26000 - 30, 5000), new DxfPoint(34000 + 30, 5000), new DxfPoint(42000 + 30, 5000) }
            .Concat(Grid(20000, 20000, 4, 1)).ToList();
        var fit = GridAlignment.SolveByColumns(sheet, placed, 1.0, out string why);
        Assert.NotNull(fit);
        Assert.InRange(fit!.Frame.OffsetX, 40050 - 60, 40050 + 60);
        Assert.Equal(35000, fit.Frame.OffsetY, 1e-6);
        Assert.Contains("5 of its 9", fit.Note);
    }
}
