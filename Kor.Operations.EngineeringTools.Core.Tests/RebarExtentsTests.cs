#nullable enable

using System.Collections.Generic;
using Kor.Operations.EngineeringTools.RebarChange;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>The fusion seam: measured takeoff extents → per-sheet slab areas for the ΔAs pricer.
/// Synthetic inputs from drawing conventions, not the validation building.</summary>
public sealed class RebarExtentsTests
{
    [Fact]
    public void Level_labels_parse_including_bands_and_parkade()
    {
        var (tw, floors) = RebarExtents.ParseLevelLabel("6-18 NORTH (x13)");
        Assert.Equal("NORTH", tw);
        Assert.Equal(13, floors.Count);
        Assert.Contains("L6", floors);
        Assert.Contains("L18", floors);

        var (tw2, floors2) = RebarExtents.ParseLevelLabel("P3");
        Assert.Null(tw2);
        Assert.Equal(new[] { "P3" }, floors2);
    }

    [Fact]
    public void Json_roundtrip_keeps_measured_areas()
    {
        var json = RebarExtents.ToJson(new[]
        {
            new RebarExtents.LevelExtent("P2", null, new List<string> { "P2" }, 20500),
            new RebarExtents.LevelExtent("3 SOUTH", "SOUTH", new List<string> { "L3" }, 8100),
        });
        var back = RebarExtents.FromJson(json);
        Assert.Equal(2, back.Count);
        Assert.Equal(20500, back[0].SlabSqFtPerFloor);
        Assert.Equal("SOUTH", back[1].Tower);
    }

    [Fact]
    public void Sheets_map_to_their_floor_areas_by_title()
    {
        var extents = RebarExtents.FromJson(RebarExtents.ToJson(new[]
        {
            new RebarExtents.LevelExtent("P2", null, new List<string> { "P2" }, 21528),          // 2,000 m2
            new RebarExtents.LevelExtent("3 SOUTH", "SOUTH", new List<string> { "L3" }, 10764),  // 1,000 m2
        }));
        var sheets = new[]
        {
            ("S9.02.2", "PARKING LEVEL P2 PLAN - SLAB REINFORCING - NORTH"),
            ("S9.14.2", "ST -LEVEL 3 PLAN - SLAB REINFORCING"),
            ("S9.99", "SHEAR WALL SCHEDULE - SOUTH TOWER"),   // no floor in title -> stays manual
        };
        var areas = RebarExtents.SlabAreasM2BySheet(sheets, extents);

        Assert.Equal(2000, areas["S9.02.2"], 0);   // parkade extent (no tower) serves a NORTH-half sheet
        Assert.Equal(1000, areas["S9.14.2"], 0);   // ST shorthand resolves to the SOUTH extent
        Assert.False(areas.ContainsKey("S9.99"));  // schedules get no area — grids stay orange/manual
    }

    [Fact]
    public void A_tower_sheet_never_takes_another_towers_extent()
    {
        var extents = RebarExtents.FromJson(RebarExtents.ToJson(new[]
        {
            new RebarExtents.LevelExtent("5 NORTH", "NORTH", new List<string> { "L5" }, 10764),
        }));
        var areas = RebarExtents.SlabAreasM2BySheet(
            new[] { ("S9.16.2", "ST -LEVEL 5 PLAN - SLAB REINFORCING") }, extents);
        Assert.Empty(areas);   // SOUTH sheet, only a NORTH extent exists -> unpriced, not borrowed
    }
}
