#nullable enable

using System.Collections.Generic;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    public enum RebarSource
    {
        Density,
        Modeled
    }

    public enum TakeoffConfidence
    {
        High,
        Review
    }

    public sealed class RebarDensityTable
    {
        public IReadOnlyDictionary<TakeoffElementType, double> KgPerM3 { get; init; } =
            new Dictionary<TakeoffElementType, double>
            {
                [TakeoffElementType.Slab] = 100,
                [TakeoffElementType.Wall] = 120,
                [TakeoffElementType.Beam] = 200,
                [TakeoffElementType.Column] = 250,
                [TakeoffElementType.DropPanel] = 100,
                [TakeoffElementType.Foundation] = 110
            };

        public static RebarDensityTable Default => new();

        public double For(TakeoffElementType t) => KgPerM3.TryGetValue(t, out var v) ? v : 0;
    }

    public sealed record TakeoffLineResult(
        TakeoffElementType ElementType,
        string Level,
        string GradeCode,
        double ConcreteM3,
        double FormworkM2,
        double RebarKg,
        RebarSource RebarSource,
        TakeoffConfidence Confidence,
        bool Unresolved,
        string? Note,
        string? Change = null,
        string? Category = null);

    public sealed record TakeoffResult(
        IReadOnlyList<TakeoffLineResult> Lines,
        double TotalConcreteM3,
        double TotalFormworkM2,
        double TotalRebarTonnes,
        IReadOnlyDictionary<string, double> ConcreteByGradeM3,
        IReadOnlyDictionary<string, double> ConcreteByLevelM3,
        int UnresolvedCount,
        int ReviewCount,
        double ReviewConcreteM3);
}
