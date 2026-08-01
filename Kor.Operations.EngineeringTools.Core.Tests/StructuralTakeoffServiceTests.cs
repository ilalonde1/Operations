#nullable enable

using System.Collections.Generic;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class StructuralTakeoffServiceTests
{
    // The proof: feed the verified Lindley element volumes through volume × KOR standard density
    // and reproduce Jim's actual reinforcing totals (lb) to within 0.1%.
    [Fact]
    public void ReproducesLindleyImperialTotals()
    {
        var inputs = new List<StructuralTakeoffInput>
        {
            new("All", TakeoffElementType.Column, null, 2822.5),
            new("All", TakeoffElementType.Wall, "Shear", 7725.5),
            new("All", TakeoffElementType.Wall, "Other", 2126.8),
            new("Fdn", TakeoffElementType.Foundation, "132in", 1245),
            new("Fdn", TakeoffElementType.Foundation, "84in", 1926),
            new("Fdn", TakeoffElementType.Foundation, "60in", 2940),
        };

        var result = StructuralTakeoffService.Compute(inputs, StructuralDensityTable.KorImperialDefault);

        Assert.Equal(UnitSystem.Imperial, result.Unit);
        AssertWithin(result.Lines[0].RebarWeight, 1_975_750, 0.001); // columns
        AssertWithin(result.Lines[1].RebarWeight, 3_862_750, 0.001); // shear walls
        AssertWithin(result.Lines[2].RebarWeight, 606_129, 0.001);   // other walls
        Assert.Equal(1_369_500, result.Lines[3].RebarWeight, 0);     // mat 132" exact
        Assert.Equal(924_480, result.Lines[4].RebarWeight, 0);       // mat 84" exact
        Assert.Equal(1_058_400, result.Lines[5].RebarWeight, 0);     // mat 60" exact
    }

    [Fact]
    public void RebarByElementRollsUpWallVariants()
    {
        var inputs = new List<StructuralTakeoffInput>
        {
            new("L1", TakeoffElementType.Wall, "Shear", 100), // 100 × 500 = 50,000
            new("L1", TakeoffElementType.Wall, "Other", 100), // 100 × 285 = 28,500
        };
        var result = StructuralTakeoffService.Compute(inputs, StructuralDensityTable.KorImperialDefault);
        Assert.Equal(78_500, result.RebarByElement[TakeoffElementType.Wall], 0);
        Assert.Equal(78_500, result.TotalRebarWeight, 0);
    }

    [Fact]
    public void UnknownVariantFallsBackToElementDefault()
    {
        // Wall "Core" isn't in the table → falls back to the Wall element default (285 imperial).
        var result = StructuralTakeoffService.Compute(
            new List<StructuralTakeoffInput> { new("L1", TakeoffElementType.Wall, "Core", 10) },
            StructuralDensityTable.KorImperialDefault);
        Assert.Equal(2_850, result.Lines[0].RebarWeight, 0);
        Assert.Equal(285, result.Lines[0].DensityUsed, 0);
    }

    [Fact]
    public void MetricDefaultIsTheImperialTableConverted()
    {
        var metric = StructuralDensityTable.KorMetricDefault;
        // 700 lb/yd³ column ratio → ~415 kg/m³.
        Assert.InRange(metric.For(TakeoffElementType.Column), 413.0, 417.0);
        Assert.Equal(UnitSystem.Metric, metric.Unit);

        var result = StructuralTakeoffService.Compute(
            new List<StructuralTakeoffInput> { new("L1", TakeoffElementType.Column, null, 10) },
            metric);
        Assert.Equal(10 * metric.For(TakeoffElementType.Column), result.Lines[0].RebarWeight, 6);
        Assert.Equal(UnitSystem.Metric, result.Unit);
    }

    private static void AssertWithin(double actual, double expected, double tolFraction)
        => Assert.InRange(actual, expected * (1 - tolFraction), expected * (1 + tolFraction));
}
