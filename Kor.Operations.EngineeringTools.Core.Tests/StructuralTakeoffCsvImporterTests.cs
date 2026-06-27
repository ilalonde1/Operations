#nullable enable

using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class StructuralTakeoffCsvImporterTests
{
    [Fact]
    public void ReadsVariantAndVolumeForDensityLookup()
    {
        const string csv =
            "Level,Element,Variant,ConcreteVolume,FormworkArea,Grade\n" +
            "P1,Slab,parking,120,800,C30\n" +
            "L2,Wall,shear,40,,C40\n" +
            "Roof,Slab,,30,200,C30\n";

        var inputs = StructuralTakeoffCsvImporter.Import(csv);

        Assert.Equal(3, inputs.Count);
        Assert.Equal("parking", inputs[0].Variant);
        Assert.Equal(120, inputs[0].ConcreteVolume);
        Assert.Equal(800, inputs[0].FormworkArea);
        Assert.Equal(TakeoffElementType.Wall, inputs[1].Element);
        Assert.Equal("shear", inputs[1].Variant);
        Assert.Null(inputs[2].Variant); // blank variant -> element default

        // Drives the per-variant density: parking slab (170) differs from the slab default which
        // would also be 170, but shear wall (500) differs from other-wall (285).
        var result = StructuralTakeoffService.Compute(inputs, StructuralDensityTable.KorImperialDefault);
        var wall = result.Lines.Single(l => l.Element == TakeoffElementType.Wall);
        Assert.Equal(500, wall.DensityUsed);
        Assert.Equal(40 * 500, wall.RebarWeight);
    }

    [Fact]
    public void BeamAndDropPanelGetNonZeroDensity()
    {
        // Regression: the floor-framing element types must not silently resolve to 0 reinforcing.
        foreach (var t in new[] { TakeoffElementType.Beam, TakeoffElementType.DropPanel })
        {
            Assert.True(StructuralDensityTable.KorImperialDefault.For(t) > 0, $"{t} imperial density is 0");
            Assert.True(StructuralDensityTable.KorMetricDefault.For(t) > 0, $"{t} metric density is 0");
        }
    }

    [Fact]
    public void VariantNormalizationIgnoresInternalSpaces()
    {
        // "96 in" must resolve to the 96in foundation key (1100), not fall back to the default (480).
        var table = StructuralDensityTable.KorImperialDefault;
        Assert.Equal(table.For(TakeoffElementType.Foundation, "96in"),
                     table.For(TakeoffElementType.Foundation, "96 in"));
        Assert.Equal(1100, table.For(TakeoffElementType.Foundation, "96 IN"));
    }

    [Fact]
    public void SkipsBlankVolumeRowsAndIsHeaderOrderIndependent()
    {
        const string csv =
            "Grade,ConcreteM3,Element,Level\n" +
            "C30,,Slab,P1\n" +          // blank volume -> skipped
            "C40,55,Column,L1\n";

        var inputs = StructuralTakeoffCsvImporter.Import(csv);
        Assert.Single(inputs);
        Assert.Equal(TakeoffElementType.Column, inputs[0].Element);
        Assert.Equal(55, inputs[0].ConcreteVolume);
    }
}
