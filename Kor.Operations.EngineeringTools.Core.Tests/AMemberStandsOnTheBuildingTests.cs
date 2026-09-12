using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Every member stands on the building (2026-09-12). A generated joint farther from the middle
/// half of the joints than that span is wide is not in the building: 31202's eight 18x18 columns
/// at (-3.2 km, 3.3 km), a bow-tie's centroid, shipped in every banked model of the set and made
/// the render draw the building as a dot. Nothing saw them because nothing looked past the counts.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a 12x12 grid of joints with one joint kilometres away is refused with the
/// joint named; the same grid without it passes; a model of fewer than eight joints is not judged
/// (a two-column test model has no "building" to measure). WHAT IT DOES NOT: a member a few metres
/// off the sheet — inside the reach — passes; a model whose far joints are the MAJORITY (a set
/// placed in two frames) has its middle half in the wrong place and reports the building as far.
/// </remarks>
public sealed class AMemberStandsOnTheBuildingTests
{
    private static string[] Model(int side, params (double X, double Y)[] extra)
    {
        var lines = new List<string>
        {
            "$ UNITS",
            "  UNITS  \"KIP\"  \"IN\"  \"F\"",
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"LEVEL 2\"  HEIGHT 120",
            "  STORY \"Base\"  HEIGHT 0",
            "$ POINT COORDINATES",
        };
        int n = 1;
        for (int i = 0; i < side; i++)
            for (int j = 0; j < side; j++)
                lines.Add($"  POINT \"KP{n++}\"  {i * 240}  {j * 240}");
        foreach (var (x, y) in extra)
            lines.Add($"  POINT \"KP{n++}\"  {x}  {y}");
        lines.Add("$ LINE CONNECTIVITIES");
        lines.Add("$ LINE ASSIGNS");
        return lines.ToArray();
    }

    [Fact]
    public void AJointKilometresFromTheBuildingIsRefusedByName()
    {
        var v = ShippedModelInvariants.Check(Model(12, (-127233, 128050)));

        var far = Assert.Single(v, x => x.Rule == "member-outside-the-building");
        Assert.Contains("KP145", far.Where);
        Assert.StartsWith("1 joint(s)", far.What);
    }

    [Fact]
    public void AGridOfJointsIsTheBuilding()
    {
        var v = ShippedModelInvariants.Check(Model(12));

        Assert.DoesNotContain(v, x => x.Rule == "member-outside-the-building");
    }

    [Fact]
    public void AJointJustOffTheGridIsStillTheBuilding()
    {
        // 240 in past the last column line, within the middle half's own width
        var v = ShippedModelInvariants.Check(Model(12, (12 * 240, 5 * 240)));

        Assert.DoesNotContain(v, x => x.Rule == "member-outside-the-building");
    }

    [Fact]
    public void TooFewJointsToBeABuildingIsNotJudged()
    {
        var v = ShippedModelInvariants.Check(Model(2, (-127233, 128050)));

        Assert.DoesNotContain(v, x => x.Rule == "member-outside-the-building");
    }
}
