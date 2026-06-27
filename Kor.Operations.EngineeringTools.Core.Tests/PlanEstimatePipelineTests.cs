#nullable enable

using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class PlanEstimatePipelineTests
{
    private static readonly PlanProfile Bc = PlanProfile.BcModerate;

    [Fact]
    public void SlabPlate_PricesConcrete_PerFloorAndTotal()
    {
        var plate = new MeasuredPlate("L17-43", TakeoffElementType.Slab, null,
            AreaSqFt: 9597, DimensionIn: 8, Count: 28);

        var result = PlanEstimatePipeline.Run(new[] { plate }, Bc);

        var e = Assert.Single(result.Plates);
        Assert.Equal(236.963, e.ConcretePerFloorCuYd, 2);   // 9597 × 8/12 ÷ 27
        Assert.Equal(236.963 * 28, e.ConcreteTotalCuYd, 1);
        Assert.Equal(TakeoffConfidence.High, e.Check.Confidence);
        Assert.Equal(e.ConcreteTotalCuYd, result.TotalConcreteCuYd, 3);
    }

    [Fact]
    public void TransferSlab_IsPricedButFlaggedCritical()
    {
        var plate = new MeasuredPlate("L01", TakeoffElementType.Slab, null,
            AreaSqFt: 13_913, DimensionIn: 48, Count: 1);

        var result = PlanEstimatePipeline.Run(new[] { plate }, Bc);

        Assert.Equal(1, result.CriticalCount);
        Assert.True(result.TotalConcreteCuYd > 0, "transfer slab is still priced, just flagged");
        Assert.Contains(result.Plates[0].Check.Flags, f => f.Code == "SLAB_TOO_THICK");
    }

    [Fact]
    public void GrayFootprintWall_UsesAreaTimesHeight()
    {
        // wall: footprint 355 sq.ft × 73% × storey height 126" (10.5 ft)
        var plate = new MeasuredPlate("L17-43", TakeoffElementType.Wall, "shear",
            AreaSqFt: 355 * 0.73, DimensionIn: 126, Count: 1);

        var result = PlanEstimatePipeline.Run(new[] { plate }, Bc);

        // 259.15 sq.ft × 126/12 ÷ 27 = 100.8 cu.yd
        Assert.Equal(100.79, result.Plates[0].ConcretePerFloorCuYd, 1);
    }

    [Fact]
    public void TakeoffInputs_CarryLevelElementVariantAndTotalVolume()
    {
        var plate = new MeasuredPlate("L17-43", TakeoffElementType.Wall, "shear",
            AreaSqFt: 100, DimensionIn: 120, Count: 2, Grade: "C35");

        var result = PlanEstimatePipeline.Run(new[] { plate }, Bc);

        var input = Assert.Single(result.TakeoffInputs);
        Assert.Equal("L17-43", input.Level);
        Assert.Equal(TakeoffElementType.Wall, input.Element);
        Assert.Equal("shear", input.Variant);
        Assert.Equal("C35", input.Grade);
        Assert.Equal(result.Plates[0].ConcreteTotalCuYd, input.ConcreteVolume, 6);
    }

    [Fact]
    public void UnresolvedThickness_ContributesZero_StillFlaggedCritical()
    {
        var plate = new MeasuredPlate("Lx", TakeoffElementType.Slab, null, AreaSqFt: 9597, DimensionIn: 0);
        var result = PlanEstimatePipeline.Run(new[] { plate }, Bc);
        Assert.Equal(0, result.TotalConcreteCuYd, 6);
        Assert.True(result.Plates[0].Check.HasCritical);
    }

    [Fact]
    public void NonPositiveArea_NeverSubtractsFromTotals()
    {
        // a failed trace (area <= 0) must contribute 0, not a negative volume that subtracts.
        var good = new MeasuredPlate("A", TakeoffElementType.Slab, null, 9597, 8);
        var bad = new MeasuredPlate("B", TakeoffElementType.Slab, null, -100, 8);
        var result = PlanEstimatePipeline.Run(new[] { good, bad }, Bc);
        Assert.Equal(result.Plates[0].ConcreteTotalCuYd, result.TotalConcreteCuYd, 6);
        Assert.Equal(0, result.Plates[1].ConcreteTotalCuYd, 6);
    }

    [Fact]
    public void EndToEnd_FeedsStructuralTakeoffService_WithProfileRatios()
    {
        // The pipeline output prices through the existing report engine at the BC profile ratios.
        var plates = new[]
        {
            new MeasuredPlate("L17-43", TakeoffElementType.Slab, null, 9597, 8, 28),
            new MeasuredPlate("L17-43", TakeoffElementType.Wall, "shear", 259.15, 126, 28),
            new MeasuredPlate("L17-43", TakeoffElementType.Column, null, 95.85, 126, 28),
        };

        var result = PlanEstimatePipeline.Run(plates, Bc);
        var computed = StructuralTakeoffService.Compute(result.TakeoffInputs, Bc.ToImperialDensityTable());

        // slab rebar = slab concrete × 199 lb/cy
        double slabConcrete = result.Plates[0].ConcreteTotalCuYd;
        Assert.Equal(slabConcrete * 199, computed.RebarByElement[TakeoffElementType.Slab], 0);
        Assert.Equal(UnitSystem.Imperial, computed.Unit);
    }
}
