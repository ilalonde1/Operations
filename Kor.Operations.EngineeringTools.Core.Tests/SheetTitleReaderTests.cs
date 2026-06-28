#nullable enable

using System.Collections.Generic;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class SheetTitleReaderTests
{
    [Fact]
    public void Reads_level_and_zone_from_a_match_line_plan_title()
    {
        // The title block text collides onto a note baseline on these dense CAD sheets — as seen on the
        // real Coronation parkade sheets — but the pattern still matches at the end of the line.
        var lines = new List<string> { "NOTE: ELECTRICAL OPENINGS SLEEVES. SIZE, LOCATION LEVEL P4 PLAN NORTH" };
        var t = SheetTitleReader.FromLines(lines);
        Assert.NotNull(t);
        Assert.Equal("P4", t!.Level);
        Assert.Equal("NORTH", t.Zone);
        Assert.Equal("P4|NORTH", t.Key);
        Assert.Equal("P4 NORTH", t.Display);
    }

    [Fact]
    public void Dominant_title_wins_over_a_stray_cross_reference()
    {
        // The sheet is LEVEL P4 PLAN SOUTH; a lone cross-reference to another plan must not hijack it.
        var lines = new List<string>
        {
            "SEE LEVEL P7 PLAN FOR RAMP CONTINUATION",
            "LEVEL P4 PLAN SOUTH",
            "REFER TO LEVEL P4 PLAN SOUTH SHEAR WALL ZONE SCHEDULE",
        };
        var t = SheetTitleReader.FromLines(lines);
        Assert.Equal("P4", t!.Level);
        Assert.Equal("SOUTH", t.Zone);
    }

    [Fact]
    public void Zoneless_floor_plan_has_null_zone_and_no_trailing_space_in_display()
    {
        var t = SheetTitleReader.FromLines(new List<string> { "LEVEL 12 FLOOR PLAN" });
        Assert.Equal("12", t!.Level);
        Assert.Null(t.Zone);
        Assert.Equal("12", t.Display);
        Assert.Equal("12|", t.Key);
    }

    [Fact]
    public void Returns_null_when_no_plan_title_present()
    {
        Assert.Null(SheetTitleReader.FromLines(new List<string> { "SHEAR WALL SCHEDULE", "PARKADE COLUMN SCHEDULE" }));
        Assert.Null(SheetTitleReader.FromLines(null));
        Assert.Null(SheetTitleReader.FromLines(new List<string>()));
    }

    [Fact]
    public void Distinct_zones_of_the_same_level_have_distinct_keys()
    {
        // The invariant that makes NORTH+SOUTH sum (distinct keys) while a re-drawn half dedupes (same key).
        var north = SheetTitleReader.FromLines(new List<string> { "LEVEL P5 PLAN NORTH" })!;
        var south = SheetTitleReader.FromLines(new List<string> { "LEVEL P5 PLAN SOUTH" })!;
        var northAgain = SheetTitleReader.FromLines(new List<string> { "LEVEL P5 PLAN NORTH" })!;
        Assert.NotEqual(north.Key, south.Key);
        Assert.Equal(north.Key, northAgain.Key);
    }
}
