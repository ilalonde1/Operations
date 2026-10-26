using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A double-height member is modelled on both floors it passes.
/// </summary>
/// <remarks>
/// Andrea, 1 Sep 2026: "some columns are double height. In that case they should be modelled on both
/// floors. For example here; L2 and Mezz level" — "otherwise they're just hanging from L2". And
/// "BTW this rule that I explained in 1. for columns, it's the same for walls too."
///
/// WHAT THIS COVERS: a stack with exactly one empty storey between two it occupies, columns and
/// walls, generated members only, never across a building boundary.
///
/// WHAT IT DOES NOT: a gap of two or more storeys is deliberately left open — 31138 has a wall
/// absent on nine consecutive storeys and filling that would invent nine floors of structure. It
/// also cannot see a member missing from the TOP or BOTTOM of its own run, because a stack has no
/// way to know it should have continued.
/// </remarks>
public class DoubleHeightMembersStandOnBothFloorsTests
{
    private static string[] Model(params string[] assigns)
    {
        var lines = new List<string>
        {
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"LEVEL 2\"  HEIGHT 120",
            "  STORY \"LEVEL 1 MEZZ\"  HEIGHT 120",
            "  STORY \"LEVEL 1\"  HEIGHT 120",
            "  STORY \"LEVEL P1\"  HEIGHT 120",
            "  STORY \"Base\"  HEIGHT 0",
            "$ POINT COORDINATES",
            "  POINT \"KP1\"  100  200",
            "$ LINE CONNECTIVITIES",
            "  LINE  \"KC1\"  COLUMN  \"KP1\"  \"KP1\"  1",
            "$ LINE ASSIGNS",
        };
        lines.AddRange(assigns);
        return lines.ToArray();
    }

    private static IReadOnlyList<string> StoreysOf(string[] lines, string obj)
    {
        var doc = E2kDocument.Parse(lines);
        doc.ModelDoubleHeightMembersOnBothFloors();
        return doc.StoreysByObject().TryGetValue(obj, out var on) ? on : Array.Empty<string>();
    }

    /// <summary>Her example: a column on L2 and L1, hanging over the mezzanine.</summary>
    [Fact]
    public void AColumnThroughTheMezzanineIsModelledOnTheMezzanineToo()
    {
        var lines = Model(
            "  LINEASSIGN  \"KC1\"  \"LEVEL 2\"  SECTION \"KOR-C24x24\"",
            "  LINEASSIGN  \"KC1\"  \"LEVEL 1\"  SECTION \"KOR-C24x24\"");

        var on = StoreysOf(lines, "KC1");

        Assert.Contains("LEVEL 1 MEZZ", on);
        Assert.Equal(3, on.Count);
    }

    /// <summary>
    /// Step 180: one column read off two sheets that register 2 in apart is one stack. 31065's ramp-edge columns stand on
    /// P2-P1 51 mm from L2's, so the storey between was never seen as a gap. WHAT THIS COVERS: two stacks 2 in apart with no
    /// storey in common close their one-storey gap at a 6 in reach; with no reach, or 12 in apart, they stay two. WHAT IT
    /// DOES NOT: walls (keyed by both ends, not merged); two stacks that share a storey (kept apart by the code - a
    /// three-storey model cannot pose that case without one stack holding a gap of its own, which the old rule fills).
    /// </summary>
    [Fact]
    public void OneColumnReadOffTwoSheetsAFewInchesApartIsOneStack()
    {
        string[] Two(double dx, params string[] extra) => new List<string>
        {
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"LEVEL 2\"  HEIGHT 120", "  STORY \"LEVEL 1\"  HEIGHT 120", "  STORY \"LEVEL P1\"  HEIGHT 120", "  STORY \"Base\"  HEIGHT 0",
            "$ POINT COORDINATES", "  POINT \"KP1\"  100  200", $"  POINT \"KP2\"  {100 + dx}  200",
            "$ LINE CONNECTIVITIES", "  LINE  \"KC1\"  COLUMN  \"KP1\"  \"KP1\"  1", "  LINE  \"KC2\"  COLUMN  \"KP2\"  \"KP2\"  1",
            "$ LINE ASSIGNS",
            "  LINEASSIGN  \"KC1\"  \"LEVEL P1\"  SECTION \"KOR-C24x24\"",
            "  LINEASSIGN  \"KC2\"  \"LEVEL 2\"  SECTION \"KOR-C24x24\"",
        }.Concat(extra).ToArray();
        int Filled(string[] lines, double reach) => E2kDocument.Parse(lines).ModelDoubleHeightMembersOnBothFloors(reach);

        Assert.Equal(1, Filled(Two(2), 6));      // one column, read 2 in apart: LEVEL 1 between is filled
        Assert.Equal(0, Filled(Two(2), 0));      // no reach: two stacks, as before
        Assert.Equal(0, Filled(Two(12), 6));     // a foot apart: two columns
    }

    /// <summary>The filled assign copies the real one, so it carries the same section.</summary>
    [Fact]
    public void TheFilledStoreyCarriesTheSameSectionAsTheFloorAbove()
    {
        var doc = E2kDocument.Parse(Model(
            "  LINEASSIGN  \"KC1\"  \"LEVEL 2\"  SECTION \"KOR-C24x24\"",
            "  LINEASSIGN  \"KC1\"  \"LEVEL 1\"  SECTION \"KOR-C24x24\""));

        Assert.Equal(1, doc.ModelDoubleHeightMembersOnBothFloors());

        string path = Path.Combine(Path.GetTempPath(), $"kor-dh-{Guid.NewGuid():N}.e2k");
        try
        {
            doc.Save(path);
            string written = File.ReadAllText(path);
            Assert.Contains("\"KC1\"  \"LEVEL 1 MEZZ\"  SECTION \"KOR-C24x24\"", written, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// A gap of two or more is a member the reader missed, not a double-height one. 31138 has a
    /// wall absent on nine consecutive storeys; filling it would invent nine floors of structure.
    /// </summary>
    [Fact]
    public void AGapOfTwoStoreysIsLeftAlone()
    {
        var lines = Model(
            "  LINEASSIGN  \"KC1\"  \"LEVEL 2\"  SECTION \"KOR-C24x24\"",
            "  LINEASSIGN  \"KC1\"  \"LEVEL P1\"  SECTION \"KOR-C24x24\"");

        var on = StoreysOf(lines, "KC1");

        Assert.Equal(2, on.Count);
        Assert.DoesNotContain("LEVEL 1 MEZZ", on);
        Assert.DoesNotContain("LEVEL 1", on);
    }

    /// <summary>A member already on every storey it passes gains nothing.</summary>
    [Fact]
    public void AContiguousStackIsUntouched()
    {
        var doc = E2kDocument.Parse(Model(
            "  LINEASSIGN  \"KC1\"  \"LEVEL 2\"  SECTION \"KOR-C24x24\"",
            "  LINEASSIGN  \"KC1\"  \"LEVEL 1 MEZZ\"  SECTION \"KOR-C24x24\"",
            "  LINEASSIGN  \"KC1\"  \"LEVEL 1\"  SECTION \"KOR-C24x24\""));

        Assert.Equal(0, doc.ModelDoubleHeightMembersOnBothFloors());
    }

    /// <summary>
    /// Hers are hers. 31138's W20 stands on L21 and then L09 down to L03, and that is how she drew
    /// it — this tool does not add storeys to the engineer's own members.
    /// </summary>
    [Fact]
    public void HerOwnMembersAreNotFilledIn()
    {
        var doc = E2kDocument.Parse(Model(
            "  LINEASSIGN  \"W20\"  \"LEVEL 2\"  SECTION \"W12\"",
            "  LINEASSIGN  \"W20\"  \"LEVEL 1\"  SECTION \"W12\""));

        Assert.Equal(0, doc.ModelDoubleHeightMembersOnBothFloors());
    }

    /// <summary>
    /// A PLATE does not rise through a storey, so a stack of floor plates is never filled. Written
    /// after "K" alone caught KF as well as KW and KC, and put a slab on the storey between two
    /// floors — FloorPlatesAreTheSizeTheEngineerMadeThem caught it in the first full run.
    /// </summary>
    /// <summary>
    /// A WOOD FLOOR HOLDS A MEMBER UP THOUGH IT IS NO PLATE (step 184, 2026-10-26). Step 184 writes no concrete plate on a
    /// floor the set frames in wood; read as "no floor", the carry-down took every wood member above it for hanging and
    /// carried it onto the plate a storey lower - 171 invented members on 18 storeys of the 21 wood-frame sets, measured on
    /// the finished files. The same column, carried without the wood floor and left where it was drawn with it.
    /// </summary>
    [Fact]
    public void AWoodFloorHoldsAMemberUpThoughItIsNoPlate()
    {
        string[] Lines() => new[]
        {
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"LEVEL 3\"  HEIGHT 120",
            "  STORY \"LEVEL 2\"  HEIGHT 120",
            "  STORY \"LEVEL 1\"  HEIGHT 120",
            "  STORY \"Base\"  HEIGHT 0",
            "$ POINT COORDINATES",
            "  POINT \"KP1\"  0  0",
            "  POINT \"KP2\"  100  0",
            "  POINT \"KP3\"  100  100",
            "  POINT \"KP4\"  0  100",
            "  POINT \"KP5\"  50  50",
            "$ LINE CONNECTIVITIES",
            "  LINE  \"KC1\"  COLUMN  \"KP5\"  \"KP5\"  1",
            "$ AREA CONNECTIVITIES",
            "  AREA  \"KF1\"  FLOOR  4  \"KP1\"  \"KP2\"  \"KP3\"  \"KP4\"",
            "$ LINE ASSIGNS",
            "  LINEASSIGN  \"KC1\"  \"LEVEL 3\"  SECTION \"KOR-C12x12\"",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"KF1\"  \"LEVEL 1\"  SECTION \"KOR-S8\"",
        };

        var carried = E2kDocument.Parse(Lines());
        carried.ModelDoubleHeightMembersOnBothFloors();
        Assert.Equal(new[] { "LEVEL 2", "LEVEL 3" }, carried.StoreysByObject()["KC1"].OrderBy(s => s, StringComparer.Ordinal).ToArray());

        var held = E2kDocument.Parse(Lines());
        held.ModelDoubleHeightMembersOnBothFloors(woodFloored: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "LEVEL 2" });
        Assert.Equal(new[] { "LEVEL 3" }, held.StoreysByObject()["KC1"].ToArray());
    }

    /// <summary>
    /// ... BUT A WOOD FLOOR IS NOT A LANDING (step 184, replay 3). Taken as one, 31082's roof wall KW228 was carried down
    /// through L5 - whose plate does not reach it - onto the wood L4. The carry-down is for a member reaching a SLAB; a
    /// member over no plate above a wood floor is left where it was drawn, as it was before step 184 took the plate away.
    /// </summary>
    [Fact]
    public void AWoodFloorIsNotALandingForAMemberCarriedDown()
    {
        var doc = E2kDocument.Parse(new[]
        {
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"LEVEL 3\"  HEIGHT 120",
            "  STORY \"LEVEL 2\"  HEIGHT 120",
            "  STORY \"LEVEL 1\"  HEIGHT 120",
            "  STORY \"Base\"  HEIGHT 0",
            "$ POINT COORDINATES",
            "  POINT \"KP1\"  0  0",
            "  POINT \"KP2\"  100  0",
            "  POINT \"KP3\"  100  100",
            "  POINT \"KP4\"  0  100",
            "  POINT \"KP5\"  500  500",
            "$ LINE CONNECTIVITIES",
            "  LINE  \"KC1\"  COLUMN  \"KP5\"  \"KP5\"  1",
            "$ AREA CONNECTIVITIES",
            "  AREA  \"KF1\"  FLOOR  4  \"KP1\"  \"KP2\"  \"KP3\"  \"KP4\"",
            "$ LINE ASSIGNS",
            "  LINEASSIGN  \"KC1\"  \"LEVEL 3\"  SECTION \"KOR-C12x12\"",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"KF1\"  \"LEVEL 2\"  SECTION \"KOR-S8\"",      // a plate on LEVEL 2, nowhere near the column
        });

        doc.ModelDoubleHeightMembersOnBothFloors(woodFloored: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "LEVEL 1" });

        Assert.Equal(new[] { "LEVEL 3" }, doc.StoreysByObject()["KC1"].ToArray());
    }

    [Fact]
    public void AStackOfFloorPlatesIsNeverFilled()
    {
        var doc = E2kDocument.Parse(new[]
        {
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"LEVEL 2\"  HEIGHT 120",
            "  STORY \"LEVEL 1 MEZZ\"  HEIGHT 120",
            "  STORY \"LEVEL 1\"  HEIGHT 120",
            "  STORY \"Base\"  HEIGHT 0",
            "$ POINT COORDINATES",
            "  POINT \"KP1\"  0  0",
            "  POINT \"KP2\"  100  0",
            "  POINT \"KP3\"  100  100",
            "  POINT \"KP4\"  0  100",
            "$ AREA CONNECTIVITIES",
            "  AREA  \"KF1\"  FLOOR  4  \"KP1\"  \"KP2\"  \"KP3\"  \"KP4\"",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"KF1\"  \"LEVEL 2\"  SECTION \"KOR-S8\"",
            "  AREAASSIGN  \"KF1\"  \"LEVEL 1\"  SECTION \"KOR-S8\"",
        });

        Assert.Equal(0, doc.ModelDoubleHeightMembersOnBothFloors());
    }

    /// <summary>
    /// On the site list one plan point carries the YMCA's column and a tower's, and the storey
    /// between belongs to neither. All three must be the same building, or none.
    /// </summary>
    [Fact]
    public void AGapAcrossABuildingBoundaryIsNotFilled()
    {
        // TWO buildings in the list, which is the only situation the guard is for. With one tag
        // there is nothing to confuse and the guard is deliberately off — that is what left a core
        // wall hanging over LEVEL 2 in the building-C file.
        var lines = new[]
        {
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"B-LEVEL 4\"  HEIGHT 120",
            "  STORY \"C-LEVEL 3\"  HEIGHT 120",
            "  STORY \"LEVEL 3\"  HEIGHT 6",
            "  STORY \"C-LEVEL 2\"  HEIGHT 120",
            "  STORY \"Base\"  HEIGHT 0",
            "$ POINT COORDINATES",
            "  POINT \"KP1\"  100  200",
            "$ LINE CONNECTIVITIES",
            "  LINE  \"KC1\"  COLUMN  \"KP1\"  \"KP1\"  1",
            "$ LINE ASSIGNS",
            "  LINEASSIGN  \"KC1\"  \"C-LEVEL 3\"  SECTION \"KOR-C24x24\"",
            "  LINEASSIGN  \"KC1\"  \"C-LEVEL 2\"  SECTION \"KOR-C24x24\"",
        };

        var doc = E2kDocument.Parse(lines);

        Assert.Equal(0, doc.ModelDoubleHeightMembersOnBothFloors());
    }
}
