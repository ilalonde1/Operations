using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A file may not name an object it never defines. ETABS refuses the line and asks the engineer.
/// </summary>
/// <remarks>
/// Written after it happened twice in one evening, both self-inflicted and both found by the
/// engineer opening the file rather than by anything here.
///
/// Dropping 42 null areas — ones ETABS itself will not read back — left their AREAASSIGN lines
/// behind. Fixing that left their AREALOAD lines behind:
///
///     Error reading line 9261. Line Ignored.
///     "AREALOAD "A13" "P1" TYPE "UNIFF" DIR "GRAV" LC "Live" FVAL 0.3472222"
///
/// One dialog per line, 74 of them, before she could look at the model at all. The model was
/// structurally fine both times, which is the point: every other invariant reads what the file
/// CONTAINS, and none of them reads whether the file is internally consistent.
///
/// WHAT THIS COVERS: any AREA* or LINE* line naming an object with no connectivity row.
/// WHAT IT DOES NOT: a POINT named by a connectivity row that does not exist — orphaned points are
/// covered elsewhere — and anything ETABS refuses for a reason other than a missing object, which
/// is most of what ETABS refuses. Only ETABS knows the rest.
/// </remarks>
public class NothingNamesAnObjectThatIsNotThereTests
{
    private static string[] Model(params string[] extra)
    {
        var lines = new List<string>
        {
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"P1\"  HEIGHT 120",
            "  STORY \"Base\"  HEIGHT 0",
            "$ POINT COORDINATES",
            "  POINT \"1\"  0  0",
            "  POINT \"2\"  120  0",
            "  POINT \"3\"  120  120",
            "  POINT \"4\"  0  120",
            "$ AREA CONNECTIVITIES",
            "  AREA \"KF1\"  FLOOR  4  \"1\"  \"2\"  \"3\"  \"4\"",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"KF1\"  \"P1\"  SECTION \"KOR-S8\"",
        };
        lines.AddRange(extra);
        return lines.ToArray();
    }

    private static IEnumerable<ModelViolation> Dangling(string[] lines)
        => ShippedModelInvariants.Check(lines)
            .Where(x => x.Rule == "names-an-object-that-is-not-there");

    /// <summary>The exact line ETABS refused: a load on an area that was dropped.</summary>
    [Fact]
    public void ALoadOnAnAreaThatWasDroppedIsCaught()
    {
        var caught = Assert.Single(Dangling(Model(
            "$ AREA LOADS",
            "  AREALOAD  \"A13\"  \"P1\"  TYPE \"UNIFF\"  DIR \"GRAV\"  LC \"Live\"  FVAL 0.3472222")));

        Assert.Equal("A13", caught.Where);
        Assert.True(caught.BlocksPublishing,
            "the engineer cannot open the model without clicking through it, so it must not publish");
    }

    /// <summary>And the first version of the same mistake: the assign left behind.</summary>
    [Fact]
    public void AnAssignOnAnAreaThatWasDroppedIsCaught()
    {
        var caught = Assert.Single(Dangling(Model(
            "  AREAASSIGN  \"F134\"  \"P1\"  SECTION \"None\"")));

        Assert.Equal("F134", caught.Where);
    }

    [Fact]
    public void ALineAssignOnAMemberThatIsNotThereIsCaughtToo()
    {
        var caught = Assert.Single(Dangling(Model(
            "$ LINE ASSIGNS",
            "  LINEASSIGN  \"KC99\"  \"P1\"  SECTION \"KOR-C24x24\"")));

        Assert.Equal("KC99", caught.Where);
    }

    /// <summary>A file whose references all resolve says nothing.</summary>
    [Fact]
    public void AConsistentFileIsSilent()
    {
        Assert.Empty(Dangling(Model()));
    }
}
