#nullable enable

using System.Collections.Generic;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class FloorMultiplierTests
{
    [Theory]
    [InlineData("STUD RAIL SCHEDULE - LEVEL 4 - 13 SLABS", 13, 10)]   // plan named after the top floor
    [InlineData("LEVEL 14 - 15 SLABS", 15, 2)]
    [InlineData("LEVEL 17 - 28 SLAB REINFORCING", 28, 12)]
    [InlineData("LEVEL 42 - 43 PLAN - CONCRETE OUTLINE", 43, 2)]
    [InlineData("STUD RAIL SCHEDULE - LEVEL 44 - 45 SLABS", 44, 2)]   // plan named after the bottom floor
    [InlineData("LEVEL 2 - 3 SLABS", 3, 2)]
    public void Counts_floors_in_the_band_containing_the_representative_level(string line, int rep, int expected)
    {
        Assert.Equal(expected, FloorMultiplier.CountForLevel(new[] { line }, rep));
    }

    [Fact]
    public void Returns_one_when_no_band_contains_the_level()
    {
        // A forward reference to another band must not multiply this level.
        Assert.Equal(1, FloorMultiplier.CountForLevel(new[] { "LEVEL 39 - 41 PLAN -" }, 38));
        Assert.Equal(1, FloorMultiplier.CountForLevel(new[] { "LEVEL 1 PLAN", "SOME NOTE" }, 1));
        Assert.Equal(1, FloorMultiplier.CountForLevel(new List<string>(), 5));
        Assert.Equal(1, FloorMultiplier.CountForLevel(null, 5));
    }

    [Fact]
    public void A_stray_band_for_another_level_does_not_pollute()
    {
        // Pooled text for level 13 may also mention level 2-3; only the band containing 13 applies.
        var pooled = new[] { "LEVEL 2 - 3 SLABS", "STUD RAIL SCHEDULE - LEVEL 4 - 13 SLABS" };
        Assert.Equal(10, FloorMultiplier.CountForLevel(pooled, 13));
        Assert.Equal(2, FloorMultiplier.CountForLevel(pooled, 3));
    }

    [Fact]
    public void Widest_containing_band_wins_and_implausible_spans_are_ignored()
    {
        // An over-wide "band" (e.g. a whole-building reference) is noise and must not multiply.
        Assert.Equal(1, FloorMultiplier.CountForLevel(new[] { "LEVEL 1 - 60 GENERAL" }, 5));
        // Of two real containing bands, the widest (more inclusive typical plan) is taken.
        Assert.Equal(10, FloorMultiplier.CountForLevel(new[] { "LEVEL 10 - 13", "LEVEL 4 - 13" }, 13));
    }

    [Fact]
    public void Handles_en_dash_and_em_dash()
    {
        Assert.Equal(10, FloorMultiplier.CountForLevel(new[] { "LEVEL 4 – 13 SLABS" }, 13)); // en
        Assert.Equal(10, FloorMultiplier.CountForLevel(new[] { "LEVEL 4 — 13 SLABS" }, 13)); // em
    }
}
