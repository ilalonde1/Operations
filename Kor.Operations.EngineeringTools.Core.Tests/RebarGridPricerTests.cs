#nullable enable
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.RebarChange;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class RebarGridPricerTests
{
    // A minimal two-page "sheet": page 1 carries the sheet number + base grid, page 2 is a
    // cross-referenced detail so the own-sheet (rarest token) rule resolves to S2.02.1.2.
    private static List<string> Issue(string baseGrid) => new()
    {
        $"S2.02.1.2  PARKING LEVEL P2 PLAN - SLAB REINFORCING\nR/W {baseGrid}  S5.03 typ.",
        "S5.03 S5.03 S5.03 typical detail sheet referenced everywhere",
    };

    [Fact]
    public void Detects350To375BaseGridReduction()
    {
        var before = Issue("15M @ 350 EACH WAY BOT. CONT.");
        var after = Issue("15M @ 375 EACH WAY BOT. CONT.");

        var result = RebarGridPricer.Compare(before, after, beforeLabel: "IFT", afterLabel: "IFC");

        var change = Assert.Single(result.Changes);
        Assert.Equal("S2.02.1.2", change.Sheet);
        Assert.Equal("Slab grid", change.Kind);
        Assert.Equal(350, change.Before!.SpacingMm);
        Assert.Equal(375, change.After!.SpacingMm);
        Assert.True(change.DeltaAsKgPerM2 < 0, "spacing opening up = steel saved (negative)");
    }

    [Fact]
    public void PricesByAreaMatchingHandCalc()
    {
        var before = Issue("15M @ 350 EACH WAY BOT. CONT.");
        var after = Issue("15M @ 375 EACH WAY BOT. CONT.");
        var areas = new Dictionary<string, double> { ["S2.02.1.2"] = 3142 }; // P2 = 785.45 m3 / 0.25 m

        var result = RebarGridPricer.Compare(before, after, areas, beforeLabel: "IFT", afterLabel: "IFC");
        var change = result.Changes.Single();

        // 15M @ 350->375 each-way bottom over 3142 m2 ~= 1,880 kg / 4,150 lb saved.
        Assert.NotNull(change.DeltaKg);
        Assert.InRange(change.DeltaLb!.Value, -4400, -3900);
        Assert.Equal(1, result.PricedCount);
    }

    [Fact]
    public void WeightNeutralRestructureShowsNearZeroDelta()
    {
        // 15M@300 each-way bottom -> 10M@300 each-way top&bottom carries the same steel.
        var before = Issue("15M @ 300 EACH WAY BOT.");
        var after = Issue("10M @ 300 EACH WAY T&B");

        var change = RebarGridPricer.Compare(before, after).Changes.Single();

        Assert.True(System.Math.Abs(change.DeltaAsKgPerM2) < 0.05,
            $"expected ~0 kg/m2, got {change.DeltaAsKgPerM2}");
    }
}
