#nullable enable

using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class BuildingRollupTests
{
    [Theory]
    [InlineData("L16", new[] { "L16" })]
    [InlineData("Level 3", new[] { "L3" })]
    [InlineData("L17-28", new[] { "L17", "L18", "L19", "L20", "L21", "L22", "L23", "L24", "L25", "L26", "L27", "L28" })]
    [InlineData("P5-P1", new[] { "P5", "P4", "P3", "P2", "P1" })]
    [InlineData("L4 (Layout 1: L3-10)", new[] { "L4" })]                 // parenthetical layout note ignored
    [InlineData("L5 (Layout 5 - Levels 5, 8, 11)", new[] { "L5" })]
    [InlineData("L34 Roof Top Amenity (Slab Reinforcing)", new[] { "L34·M" })] // roof → modifier-tagged
    public void ParseFloors_ReadsLeadingLevelTokenOrRange(string label, string[] expected)
    {
        Assert.Equal(expected, PlanGeometry_ParseFloors(label));
    }

    private static string[] PlanGeometry_ParseFloors(string label)
        => BuildingRollup.ParseFloors(label).ToArray();

    [Fact]
    public void Assign_CleanNonOverlappingBands_AreUnchanged()
    {
        // Coronation style: a 12-floor band + a single floor. Each owns its own floors, nothing drops.
        var slabs = new[]
        {
            new BuildingRollup.SlabRef(0, "L17-28", 9520, 0.88),
            new BuildingRollup.SlabRef(1, "L16", 9513, 0.88),
        };
        var owned = BuildingRollup.AssignSlabFloors(slabs);
        Assert.Equal(12, owned[0]);
        Assert.Equal(1, owned[1]);
    }

    [Fact]
    public void Assign_OverlappingLayoutsOfSameFloor_CollapseToOne()
    {
        // Onyx style: the SAME level L4 drawn under several overlapping layout notes. Exactly one plate
        // owns L4; the rest own nothing and drop — instead of L4 being counted 8+5+9+9 times.
        var slabs = new[]
        {
            new BuildingRollup.SlabRef(0, "L4 (Layout 1: L3-10)", 6650, 0.75),
            new BuildingRollup.SlabRef(1, "L4 (Layout 2: L4,6,9,12,14)", 5854, 0.65),
            new BuildingRollup.SlabRef(2, "L4 (Layout 3: L4,6,8)", 1279, 0.62),
            new BuildingRollup.SlabRef(3, "L4", 7370, 0.82),   // highest confidence → owns L4
        };
        var owned = BuildingRollup.AssignSlabFloors(slabs);
        Assert.Equal(1, owned.Values.Sum());     // L4 counted exactly once across all four
        Assert.Equal(1, owned[3]);               // the most-confident plate owns it
        Assert.Equal(0, owned[0]);
        Assert.Equal(0, owned[1]);
        Assert.Equal(0, owned[2]);
    }

    [Fact]
    public void Assign_ConcreteOutlineAndReinforcingCopies_CountFloorOnce()
    {
        // The same level on a 'Concrete Outline' sheet and a 'Slab Reinforcing' sheet is one pour.
        var slabs = new[]
        {
            new BuildingRollup.SlabRef(0, "L3 (Concrete Outline)", 7835, 0.80),
            new BuildingRollup.SlabRef(1, "L3 (Slab Reinforcing)", 7700, 0.78),
        };
        var owned = BuildingRollup.AssignSlabFloors(slabs);
        Assert.Equal(1, owned.Values.Sum());
    }

    [Fact]
    public void Assign_SpecificSingleFloor_BeatsBandForThatFloor()
    {
        // A band L4-12 plus a specific L8 sheet: L8 is owned by the specific plate, the band keeps the
        // other 8 floors — so the floor is measured from its own sheet, never double-counted.
        var slabs = new[]
        {
            new BuildingRollup.SlabRef(0, "L4-12", 9000, 0.80),  // 9 floors
            new BuildingRollup.SlabRef(1, "L8", 8800, 0.84),     // specific
        };
        var owned = BuildingRollup.AssignSlabFloors(slabs);
        Assert.Equal(8, owned[0]);   // L4-7, L9-12
        Assert.Equal(1, owned[1]);   // L8
    }

    [Fact]
    public void Assign_PartialFragment_DoesNotStealFloorFromFullBand()
    {
        // Coronation tower: an enlarged-core/partial plan for L30 (~1,600 sq.ft) sits at the same level
        // as the typical-floor band L29-38 (~9,000 sq.ft/floor). The fragment must NOT win L30 — the band
        // keeps all 10 floors and the fragment owns nothing (its concrete is inside the band's measure).
        var slabs = new[]
        {
            new BuildingRollup.SlabRef(0, "L29-38", 9053, 0.87),  // full typical-floor band, 10 floors
            new BuildingRollup.SlabRef(1, "L30", 1602, 0.82),     // partial/enlarged plan, much smaller
        };
        var owned = BuildingRollup.AssignSlabFloors(slabs);
        Assert.Equal(10, owned[0]);  // band keeps every floor including L30
        Assert.Equal(0, owned[1]);   // fragment drops
    }

    [Fact]
    public void Assign_ComparableSpecific_StillBeatsBand()
    {
        // Guard the refinement doesn't over-reach: a single-floor sheet whose area is comparable to the
        // band's per-floor area is a real full-floor plan and must still win its floor.
        var slabs = new[]
        {
            new BuildingRollup.SlabRef(0, "L4-12", 9000, 0.80),  // 9 floors
            new BuildingRollup.SlabRef(1, "L8", 8800, 0.84),     // comparable area → still authoritative
        };
        var owned = BuildingRollup.AssignSlabFloors(slabs);
        Assert.Equal(8, owned[0]);
        Assert.Equal(1, owned[1]);
    }

    [Fact]
    public void Assign_ResolvedThickness_BeatsZeroThicknessTwin()
    {
        // Coronation L39-41: two copies of the same band, one read at 8" and one whose thickness callout
        // was missed (0"). The 0" copy prices to no concrete, so the 8" copy must win even when the 0"
        // copy has marginally higher area/confidence — otherwise the whole band reads as 0 cy.
        var slabs = new[]
        {
            new BuildingRollup.SlabRef(0, "L39-41", 9188, 0.83, 0),    // thickness unresolved
            new BuildingRollup.SlabRef(1, "L39-41", 8887, 0.82, 8),    // 8" — must own all 3 floors
        };
        var owned = BuildingRollup.AssignSlabFloors(slabs);
        Assert.Equal(0, owned[0]);
        Assert.Equal(3, owned[1]);
    }

    [Fact]
    public void Assign_PartialOnlyFloor_StillCountedOnce()
    {
        // If a floor has ONLY partial plans (no full band competing), the largest still owns it — a small
        // top-of-tower setback floor isn't dropped just because it's small in absolute terms.
        var slabs = new[]
        {
            new BuildingRollup.SlabRef(0, "L45", 1500, 0.82),
            new BuildingRollup.SlabRef(1, "L45 (Reinforcing)", 1400, 0.80),
        };
        var owned = BuildingRollup.AssignSlabFloors(slabs);
        Assert.Equal(1, owned.Values.Sum());   // counted exactly once, not zero
    }

    [Fact]
    public void Assign_UnparseableLevel_CountedOnce_NotDropped()
    {
        var slabs = new[] { new BuildingRollup.SlabRef(0, "Roof Amenity Pavilion", 1200, 0.7) };
        var owned = BuildingRollup.AssignSlabFloors(slabs);
        Assert.Equal(1, owned[0]);
    }

    [Fact]
    public void Assign_MezzanineNotMergedWithPlainLevel()
    {
        var slabs = new[]
        {
            new BuildingRollup.SlabRef(0, "P1", 17000, 0.82),
            new BuildingRollup.SlabRef(1, "P1 Mezz", 6000, 0.80),
        };
        var owned = BuildingRollup.AssignSlabFloors(slabs);
        Assert.Equal(1, owned[0]);
        Assert.Equal(1, owned[1]);   // distinct floor, both kept
    }
}
