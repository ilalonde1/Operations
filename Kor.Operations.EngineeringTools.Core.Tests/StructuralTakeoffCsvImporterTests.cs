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
