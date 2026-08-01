#nullable enable

using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class TakeoffUnitsTests
{
    [Fact]
    public void DensityMetricToImperial_GoesUp_NotDown()
    {
        // 120 kg/m³ is ~202 lb/yd³. The classic foot-gun applies 0.593276 the wrong way and
        // gets ~71 — this test fails loudly if the direction is ever flipped.
        double lbPerYd3 = TakeoffUnits.Density(120, UnitSystem.Metric, UnitSystem.Imperial);
        Assert.InRange(lbPerYd3, 200.0, 205.0);
    }

    [Fact]
    public void DensityImperialToMetric_UsesTheSmallFactor()
    {
        // 700 lb/yd³ (Lindley column ratio) ≈ 415 kg/m³.
        double kgPerM3 = TakeoffUnits.Density(700, UnitSystem.Imperial, UnitSystem.Metric);
        Assert.InRange(kgPerM3, 413.0, 417.0);
    }

    [Theory]
    [InlineData(120.0)]
    [InlineData(700.0)]
    [InlineData(0.0)]
    public void DensityRoundTripsBothWays(double kgPerM3)
    {
        double there = TakeoffUnits.Density(kgPerM3, UnitSystem.Metric, UnitSystem.Imperial);
        double back = TakeoffUnits.Density(there, UnitSystem.Imperial, UnitSystem.Metric);
        Assert.Equal(kgPerM3, back, 6);
    }

    [Fact]
    public void MassVolumeArea_KnownConversions()
    {
        Assert.Equal(2204.6226, TakeoffUnits.Mass(1000, UnitSystem.Metric, UnitSystem.Imperial), 3);
        Assert.Equal(1.3079506, TakeoffUnits.Volume(1, UnitSystem.Metric, UnitSystem.Imperial), 6);
        Assert.Equal(10.763910, TakeoffUnits.Area(1, UnitSystem.Metric, UnitSystem.Imperial), 5);
    }

    [Fact]
    public void SameUnitIsIdentity()
    {
        Assert.Equal(42.0, TakeoffUnits.Density(42, UnitSystem.Metric, UnitSystem.Metric));
        Assert.Equal(42.0, TakeoffUnits.Volume(42, UnitSystem.Imperial, UnitSystem.Imperial));
    }
}
