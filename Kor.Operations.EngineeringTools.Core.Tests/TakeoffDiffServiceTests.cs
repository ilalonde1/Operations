#nullable enable

using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class TakeoffDiffServiceTests
{
    [Fact]
    public void IdenticalInputsProduceMatchedZeroDelta()
    {
        var line = Line("L1", TakeoffElementType.Slab, "C30", concreteM3: 10, rebarKg: 1000);

        var diff = TakeoffDiffService.Compare(new[] { line }, new[] { line });

        var result = Assert.Single(diff.Lines);
        Assert.Equal(0.000, result.ConcreteDeltaM3, 3);
        Assert.Equal(0.000, result.FormworkDeltaM2, 3);
        Assert.Equal(0.000, result.RebarDeltaKg, 3);
        Assert.Equal(TakeoffDiffStatus.Matched, result.Status);
        Assert.Equal(0.000, diff.TotalConcreteDeltaM3, 3);
        Assert.Equal(0.000, diff.TotalFormworkDeltaM2, 3);
        Assert.Equal(0.000, diff.TotalRebarDeltaTonnes, 3);
        Assert.Empty(diff.AddedLevels);
        Assert.Empty(diff.RemovedLevels);
        Assert.False(diff.BasisMismatch);
    }

    [Fact]
    public void IncreaseProducesPositiveMatchedDelta()
    {
        var before = Line("L1", TakeoffElementType.Slab, "C30", concreteM3: 10, rebarKg: 1000);
        var after = Line("L1", TakeoffElementType.Slab, "C30", concreteM3: 14, rebarKg: 1400);

        var diff = TakeoffDiffService.Compare(new[] { before }, new[] { after });

        var result = Assert.Single(diff.Lines);
        Assert.Equal(4.000, result.ConcreteDeltaM3, 3);
        Assert.Equal(4.000, diff.TotalConcreteDeltaM3, 3);
        Assert.Equal(0.400, diff.TotalRebarDeltaTonnes, 3);
        Assert.Equal(TakeoffDiffStatus.Matched, result.Status);
    }

    [Fact]
    public void AddedElementHasZeroBeforeAndPositiveDelta()
    {
        var after = Line("L1", TakeoffElementType.Wall, "C30", concreteM3: 3.75);

        var diff = TakeoffDiffService.Compare(
            System.Array.Empty<TakeoffLineResult>(),
            new[] { after });

        var result = Assert.Single(diff.Lines);
        Assert.Equal(TakeoffDiffStatus.Added, result.Status);
        Assert.Equal(0.000, result.ConcreteBeforeM3, 3);
        Assert.Equal(3.750, result.ConcreteDeltaM3, 3);
    }

    [Fact]
    public void RemovedElementHasZeroAfterAndNegativeDelta()
    {
        var before = Line("L19", TakeoffElementType.Column, "C40", concreteM3: 1.08);

        var diff = TakeoffDiffService.Compare(
            new[] { before },
            System.Array.Empty<TakeoffLineResult>());

        var result = Assert.Single(diff.Lines);
        Assert.Equal(TakeoffDiffStatus.Removed, result.Status);
        Assert.Equal(0.000, result.ConcreteAfterM3, 3);
        Assert.Equal(-1.080, result.ConcreteDeltaM3, 3);
    }

    [Fact]
    public void AddedAndRemovedLevelsAreReported()
    {
        var before = Line("Roof", TakeoffElementType.Slab, "C30", concreteM3: 10);
        var after = Line("P1P", TakeoffElementType.Slab, "C30", concreteM3: 10);

        var diff = TakeoffDiffService.Compare(new[] { before }, new[] { after });

        Assert.Contains("P1P", diff.AddedLevels);
        Assert.Contains("Roof", diff.RemovedLevels);
    }

    [Fact]
    public void UnresolvedLinesAreExcludedFromDiffAndLevels()
    {
        var before = Line("Roof", TakeoffElementType.Slab, "C30", concreteM3: 10, unresolved: true);
        var after = Line("P1P", TakeoffElementType.Wall, "C30", concreteM3: 3.75, unresolved: true);

        var diff = TakeoffDiffService.Compare(new[] { before }, new[] { after });

        Assert.Empty(diff.Lines);
        Assert.Equal(0.000, diff.TotalConcreteDeltaM3, 3);
        Assert.Equal(0.000, diff.TotalFormworkDeltaM2, 3);
        Assert.Equal(0.000, diff.TotalRebarDeltaTonnes, 3);
        Assert.Empty(diff.AddedLevels);
        Assert.Empty(diff.RemovedLevels);
    }

    [Fact]
    public void DuplicateKeysAreAggregatedBeforeComparison()
    {
        var before = Line("L2", TakeoffElementType.Slab, "C30", concreteM3: 8);
        var afterA = Line("L2", TakeoffElementType.Slab, "C30", concreteM3: 5);
        var afterB = Line("L2", TakeoffElementType.Slab, "C30", concreteM3: 7);

        var diff = TakeoffDiffService.Compare(new[] { before }, new[] { afterA, afterB });

        var result = Assert.Single(diff.Lines);
        Assert.Equal(TakeoffDiffStatus.Matched, result.Status);
        Assert.Equal(8.000, result.ConcreteBeforeM3, 3);
        Assert.Equal(12.000, result.ConcreteAfterM3, 3);
        Assert.Equal(4.000, result.ConcreteDeltaM3, 3);
    }

    [Fact]
    public void BasisMismatchRequiresBothBasisValuesAndDifferentText()
    {
        Assert.True(TakeoffDiffService.Compare(NoLines(), NoLines(), "A", "B").BasisMismatch);
        Assert.False(TakeoffDiffService.Compare(NoLines(), NoLines(), "A", "A").BasisMismatch);
        Assert.False(TakeoffDiffService.Compare(NoLines(), NoLines(), "A", null).BasisMismatch);
        Assert.False(TakeoffDiffService.Compare(NoLines(), NoLines(), null, null).BasisMismatch);
    }

    [Fact]
    public void MixedGradesOnSameLevelAndElementRemainSeparateLines()
    {
        var c30 = Line("L5", TakeoffElementType.Slab, "C30", concreteM3: 10);
        var c45 = Line("L5", TakeoffElementType.Slab, "C45", concreteM3: 6);

        var diff = TakeoffDiffService.Compare(NoLines(), new[] { c45, c30 });

        Assert.Equal(2, diff.Lines.Count);
        Assert.Contains(diff.Lines, l => l.Level == "L5" && l.ElementType == TakeoffElementType.Slab && l.GradeCode == "C30");
        Assert.Contains(diff.Lines, l => l.Level == "L5" && l.ElementType == TakeoffElementType.Slab && l.GradeCode == "C45");
    }

    private static IReadOnlyList<TakeoffLineResult> NoLines() => System.Array.Empty<TakeoffLineResult>();

    private static TakeoffLineResult Line(
        string level,
        TakeoffElementType elementType,
        string gradeCode,
        double concreteM3,
        double formworkM2 = 0,
        double rebarKg = 0,
        bool unresolved = false)
    {
        return new TakeoffLineResult(
            elementType,
            level,
            gradeCode,
            concreteM3,
            formworkM2,
            rebarKg,
            RebarSource.Density,
            TakeoffConfidence.High,
            unresolved,
            Note: null);
    }
}