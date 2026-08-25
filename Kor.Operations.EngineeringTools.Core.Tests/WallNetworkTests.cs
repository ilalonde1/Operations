using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Walls carry force between them only where they share a joint.
/// </summary>
public class WallNetworkTests
{
    /// <summary>
    /// Two walls that cross are both cut at the crossing.
    ///
    /// The connector cut a wall where another wall's END landed on it -- a T -- and left a crossing
    /// alone when neither wall ended there. Two walls then passed through each other with no joint
    /// in common: in ETABS, two shells that happen to overlap and carry nothing between them.
    ///
    /// The engineer, on a crossing at LEVEL P2: "two walls that are joined on the drawings should
    /// be shells in etabs, that intersect in one joint."
    /// </summary>
    [Fact]
    public void TwoWallsThatCrossAreBothCutAtTheCrossing()
    {
        // A cross: one wall east-west, one north-south, meeting in the middle of both.
        var walls = new[]
        {
            new WallAxis(new DxfPoint(0, 100), new DxfPoint(200, 100), 12, "JBP_V-WALL"),
            new WallAxis(new DxfPoint(100, 0), new DxfPoint(100, 200), 12, "JBP_V-WALL"),
        };

        var connected = WallNetwork.Connect(walls);

        // Four panels now, not two: each wall is cut where the other passes through it.
        Assert.Equal(4, connected.Count);

        // And every one of them has an end at the crossing, which is the joint they share.
        var crossing = new DxfPoint(100, 100);
        Assert.Equal(4, connected.Count(w =>
            w.Start.DistanceTo(crossing) < 0.01 || w.End.DistanceTo(crossing) < 0.01));
    }

    /// <summary>
    /// A wall that merely passes near another is not joined to it. Cutting on proximity would put a
    /// joint in the middle of a wall that runs past a corner, and split a member the drawing shows
    /// as one.
    /// </summary>
    [Fact]
    public void WallsThatDoNotMeetAreLeftWhole()
    {
        var walls = new[]
        {
            new WallAxis(new DxfPoint(0, 0), new DxfPoint(200, 0), 12, "JBP_V-WALL"),
            new WallAxis(new DxfPoint(100, 400), new DxfPoint(100, 600), 12, "JBP_V-WALL"),
        };

        var connected = WallNetwork.Connect(walls);

        Assert.Equal(2, connected.Count);
    }
}
