#nullable enable

using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class VolumeCalculatorTests
{
    [Fact]
    public void SlabComputesConcreteFormworkAndRebar()
    {
        var result = ComputeSingle(new TakeoffMeasurement(
            TakeoffElementType.Slab,
            Level: "L1",
            GradeCode: "C30",
            AreaMm2: 1e8,
            ThicknessMm: 200,
            ScaleConfirmed: true,
            OpeningsChecked: true));

        Assert.Equal(20.000, result.ConcreteM3, 3);
        Assert.Equal(100.000, result.FormworkM2, 3);
        Assert.Equal(2000.000, result.RebarKg, 3);
        Assert.Equal(TakeoffConfidence.High, result.Confidence);
    }

    [Fact]
    public void SlabSubtractsOpeningsFromConcreteAndFormwork()
    {
        var result = ComputeSingle(new TakeoffMeasurement(
            TakeoffElementType.Slab,
            Level: "L1",
            GradeCode: "C30",
            AreaMm2: 1e8,
            OpeningAreaMm2: 9e6,
            ThicknessMm: 200,
            ScaleConfirmed: true,
            OpeningsChecked: true));

        Assert.Equal(18.200, result.ConcreteM3, 3);
        Assert.Equal(91.000, result.FormworkM2, 3);
        Assert.Equal(1820.000, result.RebarKg, 3);
    }

    [Fact]
    public void ColumnComputesConcreteFormworkAndRebar()
    {
        var result = ComputeSingle(new TakeoffMeasurement(
            TakeoffElementType.Column,
            Level: "L1",
            GradeCode: "C40",
            WidthMm: 600,
            DepthMm: 600,
            StoreyHeightMm: 3000,
            ScaleConfirmed: true));

        Assert.Equal(1.080, result.ConcreteM3, 3);
        Assert.Equal(7.200, result.FormworkM2, 3);
        Assert.Equal(270.000, result.RebarKg, 3);
    }

    [Fact]
    public void WallComputesConcreteFormworkAndRebar()
    {
        var result = ComputeSingle(new TakeoffMeasurement(
            TakeoffElementType.Wall,
            Level: "L1",
            GradeCode: "C30",
            LengthMm: 5000,
            WidthMm: 250,
            StoreyHeightMm: 3000,
            ScaleConfirmed: true));

        Assert.Equal(3.750, result.ConcreteM3, 3);
        Assert.Equal(30.000, result.FormworkM2, 3);
        Assert.Equal(450.000, result.RebarKg, 3);
    }

    [Fact]
    public void BeamComputesConcreteFormworkAndRebar()
    {
        var result = ComputeSingle(new TakeoffMeasurement(
            TakeoffElementType.Beam,
            Level: "L1",
            GradeCode: "C30",
            LengthMm: 6000,
            WidthMm: 400,
            DepthMm: 700,
            ScaleConfirmed: true));

        Assert.Equal(1.680, result.ConcreteM3, 3);
        Assert.Equal(10.800, result.FormworkM2, 3);
        Assert.Equal(336.000, result.RebarKg, 3);
    }

    [Fact]
    public void DropPanelComputesIncrementalConcreteWithNoFormwork()
    {
        var result = ComputeSingle(new TakeoffMeasurement(
            TakeoffElementType.DropPanel,
            Level: "L1",
            GradeCode: "C30",
            AreaMm2: 4e6,
            ThicknessMm: 100,
            ScaleConfirmed: true));

        Assert.Equal(0.400, result.ConcreteM3, 3);
        Assert.Equal(0.000, result.FormworkM2, 3);
    }

    [Fact]
    public void MissingRequiredDimensionProducesUnresolvedLineAndExcludesTotals()
    {
        var result = VolumeCalculator.Compute(
            new[]
            {
                new TakeoffMeasurement(
                    TakeoffElementType.Slab,
                    Level: "L1",
                    GradeCode: "C30",
                    AreaMm2: 1e8,
                    ThicknessMm: null,
                    ScaleConfirmed: true,
                    OpeningsChecked: true)
            },
            RebarDensityTable.Default);

        var line = Assert.Single(result.Lines);
        Assert.True(line.Unresolved);
        Assert.Equal(0.000, line.ConcreteM3, 3);
        Assert.Equal(0.000, result.TotalConcreteM3, 3);
        Assert.Contains("ThicknessMm", line.Note);
    }

    [Fact]
    public void UnconfirmedScaleMarksResolvedSlabForReviewButIncludesItInTotals()
    {
        var result = VolumeCalculator.Compute(
            new[]
            {
                new TakeoffMeasurement(
                    TakeoffElementType.Slab,
                    Level: "L1",
                    GradeCode: "C30",
                    AreaMm2: 1e8,
                    ThicknessMm: 200,
                    ScaleConfirmed: false,
                    OpeningsChecked: true)
            },
            RebarDensityTable.Default);

        var line = Assert.Single(result.Lines);
        Assert.Equal(TakeoffConfidence.Review, line.Confidence);
        Assert.Equal(20.000, result.TotalConcreteM3, 3);
        Assert.Equal(1, result.ReviewCount);
        Assert.Equal(line.ConcreteM3, result.ReviewConcreteM3, 3);
    }

    [Fact]
    public void DensityOverrideReplacesDefaultDensity()
    {
        var result = ComputeSingle(new TakeoffMeasurement(
            TakeoffElementType.Slab,
            Level: "L1",
            GradeCode: "C30",
            AreaMm2: 1e8,
            ThicknessMm: 200,
            RebarDensityOverrideKgPerM3: 350,
            ScaleConfirmed: true,
            OpeningsChecked: true));

        Assert.Equal(7000.000, result.RebarKg, 3);
    }

    [Fact]
    public void TotalsAndGroupingIncludeResolvedLines()
    {
        var result = VolumeCalculator.Compute(
            new[]
            {
                new TakeoffMeasurement(
                    TakeoffElementType.Slab,
                    Level: "L1",
                    GradeCode: "C30",
                    AreaMm2: 1e8,
                    ThicknessMm: 200,
                    ScaleConfirmed: true,
                    OpeningsChecked: true),
                new TakeoffMeasurement(
                    TakeoffElementType.Slab,
                    Level: "L2",
                    GradeCode: "C40",
                    AreaMm2: 1e8,
                    ThicknessMm: 100,
                    ScaleConfirmed: true,
                    OpeningsChecked: true)
            },
            RebarDensityTable.Default);

        Assert.Equal(30.000, result.TotalConcreteM3, 3);
        Assert.Equal(20.000, result.ConcreteByGradeM3["C30"], 3);
        Assert.Equal(10.000, result.ConcreteByGradeM3["C40"], 3);
        Assert.Equal(20.000, result.ConcreteByLevelM3["L1"], 3);
        Assert.Equal(10.000, result.ConcreteByLevelM3["L2"], 3);
        Assert.Equal(3.000, result.TotalRebarTonnes, 3);
    }

    private static TakeoffLineResult ComputeSingle(TakeoffMeasurement measurement)
    {
        var result = VolumeCalculator.Compute(new[] { measurement }, RebarDensityTable.Default);
        return Assert.Single(result.Lines);
    }
}