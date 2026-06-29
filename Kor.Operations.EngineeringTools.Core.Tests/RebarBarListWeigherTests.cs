#nullable enable

using System.Collections.Generic;
using Kor.Operations.EngineeringTools.RebarChange;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class RebarBarListWeigherTests
{
    [Fact]
    public void Parses_quantity_size_and_feet_inches_length()
    {
        var c = RebarBarListWeigher.Parse("16-15M13.9")!.Value;
        Assert.Equal(16, c.Qty);
        Assert.Equal(15, c.SizeM);
        Assert.False(c.Continuous);
        Assert.Equal(13.75, c.LengthFt, 3);          // 13'-9" = 13.75 ft (NOT 13.9)

        var c2 = RebarBarListWeigher.Parse("8-C20M9.10")!.Value;   // 9'-10" — the inch field, not a decimal
        Assert.Equal(8, c2.Qty);
        Assert.True(c2.Continuous);
        Assert.Equal(9 + 10 / 12.0, c2.LengthFt, 3);
    }

    [Fact]
    public void Weighs_a_callout_by_qty_length_and_csa_mass()
    {
        // 16 bars × 13.75 ft × (1.570 kg/m × 0.671969 lb/ft) ≈ 232 lb.
        double lb = RebarBarListWeigher.WeightLb(RebarBarListWeigher.Parse("16-15M13.9")!.Value);
        Assert.InRange(lb, 231, 233);
    }

    [Fact]
    public void Intensity_keys_and_unreal_bars_do_not_parse()
    {
        Assert.Null(RebarBarListWeigher.Parse("15M@200"));   // intensity call-out, not bar-list
        Assert.Null(RebarBarListWeigher.Parse("12M34.5"));   // 12M is not a real Canadian bar
    }

    [Fact]
    public void Continuous_without_a_quantity_is_unweighable_not_guessed()
    {
        var callouts = new Dictionary<string, int>
        {
            ["16-15M13.9"] = 2,   // weighed: 2 × ~232
            ["C15M3.11"] = 1,     // continuous, no qty → unweighable, contributes 0 lb
        };
        var w = RebarBarListWeigher.Weigh(callouts);
        Assert.InRange(w.WeightLb, 462, 466);
        Assert.Equal(2, w.WeighedCallouts);
        Assert.Equal(1, w.UnweighableCallouts);
    }
}
