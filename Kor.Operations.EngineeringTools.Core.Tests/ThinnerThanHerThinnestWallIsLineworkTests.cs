using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// An outline on a wall layer thinner than the engineer's thinnest wall is linework, not structure.
/// </summary>
/// <remarks>
/// The floor is measured off her own models rather than chosen. Across 736 wall members in her
/// 31138 model the thinnest is SIX INCHES — 6·27, 8·91, 10·9, 12·145, 14·52, 16·24, 18·78, 20·10,
/// 24·192, 30·108 — and her 31168 shell agrees as far as it goes, thinnest 10.
///
/// The outlines the reader refuses on 31168's wall layer measure 3.1 to 3.4 in, all 37 of them.
/// Nothing lies between 3.4 and 6.0, which is what makes a 4-inch floor safe rather than arbitrary.
///
/// WHAT THIS COVERS: the thickness floor, both sides of it. WHAT IT DOES NOT: whether the outline
/// is on a wall layer at all, and whether a ring thick enough to be a wall is one — a stair nosing
/// is 12 in wide and is not a wall.
/// </remarks>
public class ThinnerThanHerThinnestWallIsLineworkTests
{
    /// <summary>A ribbon: two faces <paramref name="thickness"/> apart, running 240 in.</summary>
    private static PlanLoop Ribbon(double thickness) => new(
        "JBP_V-WALL",
        new[]
        {
            new DxfPoint(0, 0),
            new DxfPoint(240, 0),
            new DxfPoint(240, thickness),
            new DxfPoint(0, thickness),
        },
        closedExactly: true);

    private static readonly PlanClassificationOptions Rules = new();

    [Fact]
    public void ThreeInchesOfMaterialIsLineworkAndMakesNoWall()
    {
        Assert.Empty(WallOutlineDecomposer.Decompose(Ribbon(3.2), Rules));
    }

    [Fact]
    public void TwoInchesIsLineworkToo()
    {
        Assert.Empty(WallOutlineDecomposer.Decompose(Ribbon(2.0), Rules));
    }

    /// <summary>
    /// Six inches is the thinnest wall in her 31138 model, so it must survive — this is the side of
    /// the floor that loses structure silently if it is set too high.
    /// </summary>
    [Fact]
    public void HerThinnestRealWallSurvives()
    {
        var walls = WallOutlineDecomposer.Decompose(Ribbon(6.0), Rules);

        var one = Assert.Single(walls);
        Assert.Equal(6.0, one.Thickness, 1);
    }

    /// <summary>And the default floor is below it, which is what leaves the gap.</summary>
    [Fact]
    public void TheFloorSitsBelowAnythingSheDraws()
    {
        Assert.True(Rules.MinWallThickness < 6.0,
            $"dxf.min-wall-thickness is {Rules.MinWallThickness}; her thinnest wall is 6 in, so a "
            + "floor at or above 6 would refuse walls she models.");

        Assert.True(Rules.MinWallThickness > 3.4,
            $"dxf.min-wall-thickness is {Rules.MinWallThickness}; the linework it exists to refuse "
            + "measures up to 3.4 in on 31168.");
    }
}
