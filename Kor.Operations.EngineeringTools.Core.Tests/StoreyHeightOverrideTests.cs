#nullable enable

using System.Collections.Generic;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The clean-at-source storey-height override: when the caller supplies a real per-level floor-to-floor
/// height (from the architectural set), the takeoff prices that level's verticals at it; otherwise it falls
/// back to the typical, flagged. Storey height is NOT machine-readable off the structural plans, so this is
/// how an exact height gets in — verified here on the pure resolution logic, independent of raster/vision.
/// </summary>
public sealed class StoreyHeightOverrideTests
{
    [Theory]
    [InlineData("1 NORTH", "L1")]
    [InlineData("LEVEL 2 SOUTH TOWER", "L2")]
    [InlineData("LEVEL 19", "L19")]
    [InlineData("LEVEL P1", "P1")]
    [InlineData("P2", "P2")]
    public void NormalizeLevelKey_strips_tower_and_canonicalizes(string label, string key)
        => Assert.Equal(key, SlabTakeoffEngine.NormalizeLevelKey(label));

    [Fact]
    public void One_supplied_height_per_floor_serves_both_towers()
    {
        // Architectural gives the floor-to-floor once per LEVEL; "1 NORTH" and "1 SOUTH" must both resolve to it.
        var map = SlabTakeoffEngine.NormalizeHeightMap(new Dictionary<string, double> { ["LEVEL 1"] = 144 }); // 12 ft
        var (north, kn) = SlabTakeoffEngine.ResolveStoreyHeightIn("1 NORTH", map, fallbackIn: 126);
        var (south, ks) = SlabTakeoffEngine.ResolveStoreyHeightIn("1 SOUTH", map, fallbackIn: 126);
        Assert.Equal(144, north); Assert.True(kn);
        Assert.Equal(144, south); Assert.True(ks);
    }

    [Fact]
    public void A_level_without_a_supplied_height_falls_back_to_the_typical_flagged()
    {
        var map = SlabTakeoffEngine.NormalizeHeightMap(new Dictionary<string, double> { ["P1"] = 156 }); // 13 ft parkade
        var (p1, p1Known) = SlabTakeoffEngine.ResolveStoreyHeightIn("P1", map, fallbackIn: 126);
        var (l5, l5Known) = SlabTakeoffEngine.ResolveStoreyHeightIn("LEVEL 5", map, fallbackIn: 126);
        Assert.Equal(156, p1); Assert.True(p1Known);     // parkade override applied
        Assert.Equal(126, l5); Assert.False(l5Known);    // not supplied → typical, flagged
    }

    [Fact]
    public void Null_or_empty_or_nonpositive_map_is_treated_as_no_override()
    {
        Assert.Null(SlabTakeoffEngine.NormalizeHeightMap(null));
        Assert.Null(SlabTakeoffEngine.NormalizeHeightMap(new Dictionary<string, double>()));
        Assert.Null(SlabTakeoffEngine.NormalizeHeightMap(new Dictionary<string, double> { ["LEVEL 1"] = 0, ["P1"] = -5 }));
        var (h, known) = SlabTakeoffEngine.ResolveStoreyHeightIn("LEVEL 1", null, fallbackIn: 126);
        Assert.Equal(126, h); Assert.False(known);
    }

    [Fact]
    public void Last_value_wins_when_two_labels_normalize_to_the_same_floor()
    {
        // "LEVEL 1" and "1 NORTH" are the same floor; the map collapses them (last wins) rather than throwing.
        var map = SlabTakeoffEngine.NormalizeHeightMap(new Dictionary<string, double> { ["LEVEL 1"] = 144, ["1 NORTH"] = 150 });
        Assert.NotNull(map);
        Assert.Single(map!);
        var (h, known) = SlabTakeoffEngine.ResolveStoreyHeightIn("LEVEL 1", map, fallbackIn: 126);
        Assert.Equal(150, h); Assert.True(known);
    }
}
