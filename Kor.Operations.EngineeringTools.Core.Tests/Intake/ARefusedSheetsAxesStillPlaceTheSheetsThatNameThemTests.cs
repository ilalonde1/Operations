#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A refused sheet's axes still place the sheets that name them (intake step 51). A building drawn
/// as WEST and EAST halves on separate sheets, split on a bay, shares no X axis name across the
/// seam: 31130's west sheets carry 1–16, its east sheets 17–28, and the east half sat in its page
/// frame on top of the west. The DESIGN LOAD PLAN draws 1–16 and 17–28 in one view — refused as a
/// plan, rightly — at half the plans' scale. Its fit is solved at its own scale, from the ratio of
/// its spacing to the model's over the widest pair of shared names, and the axes only it draws are
/// carried into the grid for the east sheets to be placed by.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a carrier at half scale fits by name with the ratio in the note; the axes it
/// alone draws land where the model has them; the directions disagreeing on the ratio refuse; a
/// direction sharing one name takes the other's ratio. WHAT IT DOES NOT: the composer's loop that chooses carriers
/// and places the east sheets through them (31130 in the six-set gate measures it); a carrier drawn
/// a quarter turn from the model.
/// </remarks>
public sealed class ARefusedSheetsAxesStillPlaceTheSheetsThatNameThemTests
{
    // the model's grid, from the west half: X 1..4 a bay of 10 m apart, Y A, B
    private static readonly List<GridAlignment.ReferenceGrid> Model =
    [
        new("1", true, 0), new("2", true, 10000), new("3", true, 20000), new("4", true, 30000),
        new("A", false, 0), new("B", false, 8000),
    ];

    [Fact]
    public void AKeyPlanAtHalfScaleFitsByNameAndLendsTheAxesOnlyItDraws()
    {
        // the key plan draws the same axes at half spacing, offset on its page, plus 5 and 6 past the seam
        var carrier = new List<GridAlignment.NamedAxis>
        {
            new("1", true, 1000), new("2", true, 6000), new("3", true, 11000), new("4", true, 16000), new("5", true, 21000), new("6", true, 26000),
            new("A", false, 500), new("B", false, 4500),
        };
        var own = GridAlignment.SolveByNameAtOwnScale(carrier, Model);
        Assert.NotNull(own);
        Assert.Equal(2.0, own!.Value.Scale, 1e-9);
        Assert.Equal(4, own.Value.Fit.MatchedX);
        Assert.Equal(2, own.Value.Fit.MatchedY);
        Assert.Contains("at its own scale, 2 times", own.Value.Fit.Note);

        var lent = GridAlignment.Carried(carrier, own.Value.Fit.Frame, own.Value.Scale);
        Assert.Equal(40000, lent.Single(g => g.Label == "5").Coord, 1e-6);
        Assert.Equal(50000, lent.Single(g => g.Label == "6").Coord, 1e-6);
        Assert.Equal(8000, lent.Single(g => g.Label == "B").Coord, 1e-6);
    }

    [Fact]
    public void DirectionsThatDisagreeOnTheScaleAreNoFit()
    {
        var carrier = new List<GridAlignment.NamedAxis>
        {
            new("1", true, 0), new("2", true, 5000), new("3", true, 10000),
            new("A", false, 0), new("B", false, 8000),      // Y at full scale, X at half: not one sheet's scale
        };
        Assert.Null(GridAlignment.SolveByNameAtOwnScale(carrier, Model));
    }

    [Fact]
    public void TheScaleComesFromTheDirectionThatSharesTwoNames()
    {
        var carrier = new List<GridAlignment.NamedAxis> { new("1", true, 0), new("2", true, 5000), new("A", false, 0), new("Z", false, 4000) };
        // Y shares only A: no ratio from Y; X's ratio (2) stands, and A confirms the fit in Y
        var own = GridAlignment.SolveByNameAtOwnScale(carrier, Model);
        Assert.NotNull(own);
        Assert.Equal(2.0, own!.Value.Scale, 1e-9);
        Assert.Equal(2, own.Value.Fit.MatchedX);
        Assert.Equal(1, own.Value.Fit.MatchedY);
    }
}
