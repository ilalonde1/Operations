#nullable enable

using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using WB = Kor.Operations.EngineeringTools.QuantityTakeoff.ScheduleTakeoff.WallBand;
using CB = Kor.Operations.EngineeringTools.QuantityTakeoff.ScheduleTakeoff.ColumnBand;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class ScheduleTakeoffTests
{
    [Theory]
    [InlineData("LEVEL 08", "L8")]
    [InlineData("  level   01 ", "L1")]
    [InlineData("LEVEL 7", "L7")]
    [InlineData("L07", "L7")]
    [InlineData("LEVEL P1", "P1")]      // parkade prefixed with LEVEL folds to the bare mark
    [InlineData("P1 MEZZ", "P1 MEZZ")]
    [InlineData("L17", "L17")]
    public void NormalizeLevel_CanonicalisesLevelPrefixAndPadding(string input, string expected)
        => Assert.Equal(expected, ScheduleTakeoff.NormalizeLevel(input));

    [Fact]
    public void NormalizeLevel_LevelNAndLN_MatchSameKey()
        => Assert.Equal(ScheduleTakeoff.NormalizeLevel("LEVEL 7"), ScheduleTakeoff.NormalizeLevel("L7"));

    private static readonly string[] Levels3 = { "L3", "L2", "L1" };
    // storey height is supplied to the module in INCHES (StoreyIn); 120 in = 10 ft.
    private static double[] StoreyIn(int n, double inches) => Enumerable.Repeat(inches, n).ToArray();

    [Fact]
    public void Wall_SingleBand_PricesLengthTimesThicknessTimesStorey()
    {
        // One 10 ft wall, 12" thick, over 3 levels at 10 ft storey.
        // per level: 10ft × (12/12)ft = 10 sq ft × 10ft / 27 = 3.7037 cy; × 3 = 11.111 cy
        var lengths = new Dictionary<string, double> { ["W1"] = 10 };
        var bands = new[] { new WB("W1", "L3", "L1", 12) };
        var r = ScheduleTakeoff.ComputeWall(Levels3, StoreyIn(3, 120), lengths, bands);

        Assert.Equal(1, r.BandsApplied);   // one band, covering 3 levels
        Assert.Equal(0, r.BandsSkipped);
        Assert.Equal(11.111, r.TotalCuYd, 2);
        Assert.All(r.PerLevel, p => Assert.Equal(10.0, p.FootprintSqFt, 3));
    }

    [Fact]
    public void Wall_ThicknessSteps_ApplyPerBand()
    {
        // W1: 12" at L3, 24" at L2-L1. Footprints: 10, 20, 20 sq ft.
        var lengths = new Dictionary<string, double> { ["W1"] = 10 };
        var bands = new[] { new WB("W1", "L3", "L3", 12), new WB("W1", "L2", "L1", 24) };
        var r = ScheduleTakeoff.ComputeWall(Levels3, StoreyIn(3, 120), lengths, bands);

        Assert.Equal(2, r.BandsApplied);
        Assert.Equal(10.0, r.PerLevel[0].FootprintSqFt, 3);
        Assert.Equal(20.0, r.PerLevel[1].FootprintSqFt, 3);
        Assert.Equal(20.0, r.PerLevel[2].FootprintSqFt, 3);
    }

    [Fact]
    public void Wall_FloorKeyword_ExtendsBandToBottom()
    {
        var lengths = new Dictionary<string, double> { ["W1"] = 10 };
        var bands = new[] { new WB("W1", "L2", "FLOOR", 12) };   // L2 down to the base
        var r = ScheduleTakeoff.ComputeWall(Levels3, StoreyIn(3, 120), lengths, bands);

        Assert.Equal(0.0, r.PerLevel[0].FootprintSqFt, 3);   // L3 not covered
        Assert.Equal(10.0, r.PerLevel[1].FootprintSqFt, 3);  // L2
        Assert.Equal(10.0, r.PerLevel[2].FootprintSqFt, 3);  // L1 (== FLOOR bottom)
    }

    [Fact]
    public void Wall_MarkWithoutLength_IsSkipped_NotGuessed()
    {
        // W2 is in the schedule but has no key-plan length → must not be priced.
        var lengths = new Dictionary<string, double> { ["W1"] = 10 };
        var bands = new[] { new WB("W1", "L3", "L1", 12), new WB("W2", "L3", "L1", 30) };
        var r = ScheduleTakeoff.ComputeWall(Levels3, StoreyIn(3, 120), lengths, bands);

        Assert.Equal(1, r.BandsApplied);
        Assert.Equal(1, r.BandsSkipped);
        Assert.Equal(1, r.MarksPriced);
    }

    [Fact]
    public void Wall_PaddedLevelLabels_MatchUnpadded()
    {
        var levels = new[] { "LEVEL 3", "LEVEL 2", "LEVEL 1" };
        var lengths = new Dictionary<string, double> { ["W1"] = 10 };
        var bands = new[] { new WB("W1", "LEVEL 03", "LEVEL 01", 12) };  // zero-padded in the schedule
        var r = ScheduleTakeoff.ComputeWall(levels, StoreyIn(3, 120), lengths, bands);
        Assert.Equal(0, r.BandsSkipped);
        Assert.Equal(3, r.PerLevel.Count(p => p.FootprintSqFt > 0));
    }

    [Fact]
    public void Wall_UnknownLevel_IsSkipped()
    {
        var lengths = new Dictionary<string, double> { ["W1"] = 10 };
        var bands = new[] { new WB("W1", "L99", "L97", 12) };
        var r = ScheduleTakeoff.ComputeWall(Levels3, StoreyIn(3, 120), lengths, bands);
        Assert.Equal(0, r.BandsApplied);
        Assert.Equal(1, r.BandsSkipped);
        Assert.Equal(0.0, r.TotalCuYd, 3);
    }

    [Fact]
    public void Wall_PartialCoverage_LeavesUncoveredLevelsAtZero()
    {
        // A "Part 1 only" band reaches the bottom two levels; the top level must read 0 footprint so the
        // caller can fall back to gray-fill there instead of silently losing the wall (audit C2).
        var lengths = new Dictionary<string, double> { ["W1"] = 10 };
        var bands = new[] { new WB("W1", "L2", "L1", 12) };   // L3 uncovered
        var r = ScheduleTakeoff.ComputeWall(Levels3, StoreyIn(3, 120), lengths, bands);
        Assert.Equal(0.0, r.PerLevel[0].FootprintSqFt, 3);    // L3 — uncovered, caller keeps gray-fill
        Assert.True(r.PerLevel[1].FootprintSqFt > 0);          // L2 — covered
        Assert.True(r.PerLevel[2].FootprintSqFt > 0);          // L1 — covered
    }

    [Fact]
    public void Wall_ZeroStoreyHeight_FootprintReported_ButZeroConcrete()
    {
        // Footprint is still reported (so coverage is visible) but concrete is 0 when no storey height.
        var lengths = new Dictionary<string, double> { ["W1"] = 10 };
        var bands = new[] { new WB("W1", "L3", "L1", 12) };
        var r = ScheduleTakeoff.ComputeWall(Levels3, StoreyIn(3, 0), lengths, bands);
        Assert.True(r.PerLevel.All(p => p.FootprintSqFt > 0));
        Assert.Equal(0.0, r.TotalCuYd, 3);
    }

    [Fact]
    public void Wall_OverlappingBands_LastWriteWins_Documented()
    {
        // Two bands for the same mark/level: the later one in iteration order wins (documented behaviour).
        var lengths = new Dictionary<string, double> { ["W1"] = 10 };
        var bands = new[] { new WB("W1", "L3", "L1", 12), new WB("W1", "L3", "L1", 24) };
        var r = ScheduleTakeoff.ComputeWall(Levels3, StoreyIn(3, 120), lengths, bands);
        Assert.Equal(20.0, r.PerLevel[0].FootprintSqFt, 3);   // 10 ft × 24"/12 = 20 sq ft (the 24" band)
    }

    [Fact]
    public void Column_PricesWidthTimesDepthPerCoveredLevel()
    {
        // C1 = 24"×16" over L3-L1: footprint 384/144 = 2.6667 sq ft each level.
        var bands = new[] { new CB("C1", "L3", "L1", 24, 16) };
        var r = ScheduleTakeoff.ComputeColumn(Levels3, StoreyIn(3, 120), bands);
        Assert.Equal(2.6667, r.PerLevel[0].FootprintSqFt, 3);
        Assert.Equal(3, r.PerLevel.Count(p => p.FootprintSqFt > 0));
    }

    [Fact]
    public void Column_SingleDimension_TreatedAsSquare()
    {
        // Only one dim read (24") → 24×24.
        var bands = new[] { new CB("C1", "L3", "L1", 24, 0) };
        var r = ScheduleTakeoff.ComputeColumn(Levels3, StoreyIn(3, 120), bands);
        Assert.Equal(576.0 / 144.0, r.PerLevel[0].FootprintSqFt, 3);
    }

    [Fact]
    public void Column_TwoColumns_FootprintsSum()
    {
        var bands = new[] { new CB("C1", "L3", "L1", 24, 24), new CB("C2", "L3", "L1", 24, 24) };
        var r = ScheduleTakeoff.ComputeColumn(Levels3, StoreyIn(3, 120), bands);
        Assert.Equal(2, r.MarksPriced);
        Assert.Equal(2 * 576.0 / 144.0, r.PerLevel[0].FootprintSqFt, 3);
    }

    [Fact]
    public void VaryingStoreyHeights_AreHonoured()
    {
        var lengths = new Dictionary<string, double> { ["W1"] = 10 };
        var bands = new[] { new WB("W1", "L3", "L1", 12) };
        var storeysIn = new[] { 144.0, 120.0, 108.0 };   // inches per level (12, 10, 9 ft)
        var r = ScheduleTakeoff.ComputeWall(Levels3, storeysIn, lengths, bands);
        // footprint 10 sq ft each; cy = 10 × (storeyIn/12) / 27
        Assert.Equal(10 * 12.0 / 27.0, r.PerLevel[0].ConcreteCuYd, 3);
        Assert.Equal(10 * 9.0 / 27.0, r.PerLevel[2].ConcreteCuYd, 3);
    }
}
