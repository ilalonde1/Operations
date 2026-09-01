using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// "Which storeys have no floor plate" must be read off the members, and a member is an ASSIGN.
/// </summary>
/// <remarks>
/// It grouped by the object's FIRST storey. The stack merge gives one object a label for its whole
/// height, so a wall standing on B-LEVEL 27 through B-LEVEL 41 counted entirely on B-LEVEL 27 and
/// every other storey it stands on disappeared from the reading.
///
/// On the shipped 31168 site model that put four storeys in the report — A-LEVEL 35, B-LEVEL 28,
/// B-LEVEL 27, A-LEVEL 1 — where the file has three: B-LEVEL 41, A-LEVEL 35, B-LEVEL 28. Two
/// invented and one missed, in the sentence telling the engineer which storeys need a slab.
///
/// ⚠ THE MERGE DIFFERENTIAL CANNOT SEE THIS ONE. It is wrong with the merge on AND off — unmerged,
/// each object has one storey and storeys[0] is right by accident. That is the case rule 11 warns
/// about: a differential is blind to a fault present in both runs, so this is an invariant on the
/// finished file instead.
/// </remarks>
public class PlatelessStoreysAreTheFilesNotTheLabelsTests
{
    /// <summary>
    /// One merged wall object standing on three storeys, with a plate on only the lowest. The two
    /// storeys above it have members and no plate and must both be named.
    /// </summary>
    private static string[] MergedWallOverThreeStoreys() =>
    [
        "$ STORIES - IN SEQUENCE FROM TOP",
        "  STORY \"B-LEVEL 41\"  HEIGHT 120",
        "  STORY \"B-LEVEL 40\"  HEIGHT 120",
        "  STORY \"B-LEVEL 39\"  HEIGHT 120",
        "  STORY \"Base\"  HEIGHT 0",
        "$ POINT COORDINATES",
        "  POINT \"KP1\"  0  0",
        "  POINT \"KP2\"  120  0",
        "  POINT \"KP3\"  120  120",
        "  POINT \"KP4\"  0  120",
        "$ LINE CONNECTIVITIES",
        "  LINE  \"KW1\"  PANEL  \"KP1\"  \"KP2\"  1",
        "$ AREA CONNECTIVITIES",
        "  AREA  \"KW1\"  PANEL  1  \"KP1\"  \"KP2\"  \"KP2\"  \"KP1\"  1",
        "  AREA  \"KF1\"  FLOOR  4  \"KP1\"  \"KP2\"  \"KP3\"  \"KP4\"",
        "$ AREA ASSIGNS",
        "  AREAASSIGN  \"KW1\"  \"B-LEVEL 39\"  SECTION \"KOR-W8\"",
        "  AREAASSIGN  \"KW1\"  \"B-LEVEL 40\"  SECTION \"KOR-W8\"",
        "  AREAASSIGN  \"KW1\"  \"B-LEVEL 41\"  SECTION \"KOR-W8\"",
        "  AREAASSIGN  \"KF1\"  \"B-LEVEL 39\"  SECTION \"KOR-S8\"",
    ];

    [Fact]
    public void EveryStoreyTheWallStandsOnIsJudgedOnItsOwn()
    {
        var gaps = E2kDocument.Parse(MergedWallOverThreeStoreys()).FloorGapDetails();

        Assert.Equal(new[] { "B-LEVEL 41", "B-LEVEL 40" }, gaps.FloorsWithNoPlate);
    }

    /// <summary>The storey that does have a plate under its wall is not named.</summary>
    [Fact]
    public void TheStoreyWithAPlateIsNotNamed()
    {
        var gaps = E2kDocument.Parse(MergedWallOverThreeStoreys()).FloorGapDetails();

        Assert.DoesNotContain("B-LEVEL 39", gaps.FloorsWithNoPlate);
    }

    /// <summary>
    /// The same model written the old way — one object per storey — must give the same answer.
    /// Whether the labels were merged is not a property of the building.
    /// </summary>
    [Fact]
    public void UnmergedGivesTheSameAnswerAsMerged()
    {
        string[] unmerged =
        [
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"B-LEVEL 41\"  HEIGHT 120",
            "  STORY \"B-LEVEL 40\"  HEIGHT 120",
            "  STORY \"B-LEVEL 39\"  HEIGHT 120",
            "  STORY \"Base\"  HEIGHT 0",
            "$ POINT COORDINATES",
            "  POINT \"KP1\"  0  0",
            "  POINT \"KP2\"  120  0",
            "  POINT \"KP3\"  120  120",
            "  POINT \"KP4\"  0  120",
            "$ AREA CONNECTIVITIES",
            "  AREA  \"KW1\"  PANEL  1  \"KP1\"  \"KP2\"  \"KP2\"  \"KP1\"  1",
            "  AREA  \"KW2\"  PANEL  1  \"KP1\"  \"KP2\"  \"KP2\"  \"KP1\"  1",
            "  AREA  \"KW3\"  PANEL  1  \"KP1\"  \"KP2\"  \"KP2\"  \"KP1\"  1",
            "  AREA  \"KF1\"  FLOOR  4  \"KP1\"  \"KP2\"  \"KP3\"  \"KP4\"",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"KW1\"  \"B-LEVEL 39\"  SECTION \"KOR-W8\"",
            "  AREAASSIGN  \"KW2\"  \"B-LEVEL 40\"  SECTION \"KOR-W8\"",
            "  AREAASSIGN  \"KW3\"  \"B-LEVEL 41\"  SECTION \"KOR-W8\"",
            "  AREAASSIGN  \"KF1\"  \"B-LEVEL 39\"  SECTION \"KOR-S8\"",
        ];

        var merged = E2kDocument.Parse(MergedWallOverThreeStoreys()).FloorGapDetails();
        var plain = E2kDocument.Parse(unmerged).FloorGapDetails();

        Assert.Equal(plain.FloorsWithNoPlate, merged.FloorsWithNoPlate);
    }
}
