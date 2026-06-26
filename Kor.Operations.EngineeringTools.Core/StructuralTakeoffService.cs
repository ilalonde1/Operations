#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>One concrete-quantity input row for the absolute takeoff: a level/element/variant
    /// with its concrete volume (and optional formwork area), in the takeoff's unit system.</summary>
    public sealed record StructuralTakeoffInput(
        string Level,
        TakeoffElementType Element,
        string? Variant,
        double ConcreteVolume,
        double FormworkArea = 0,
        string Grade = "");

    public sealed record StructuralTakeoffLine(
        string Level,
        TakeoffElementType Element,
        string? Variant,
        string Grade,
        double ConcreteVolume,
        double RebarWeight,
        double FormworkArea,
        double DensityUsed);

    public sealed record StructuralTakeoffResult(
        IReadOnlyList<StructuralTakeoffLine> Lines,
        double TotalConcreteVolume,
        double TotalRebarWeight,
        double TotalFormworkArea,
        IReadOnlyDictionary<TakeoffElementType, double> RebarByElement,
        IReadOnlyDictionary<string, double> RebarByLevel,
        UnitSystem Unit);

    /// <summary>
    /// Absolute (single-issue) structural quantity takeoff. Concrete volume comes straight from the
    /// model schedule; reinforcing = concrete volume × the standard density per element/variant —
    /// the same ratio method KOR uses by hand (validated to reproduce the Lindley takeoff exactly).
    /// Deterministic: same inputs → same numbers. No LLM in the measurement path.
    /// </summary>
    public static class StructuralTakeoffService
    {
        public static StructuralTakeoffResult Compute(
            IReadOnlyList<StructuralTakeoffInput> inputs,
            StructuralDensityTable densities)
        {
            ArgumentNullException.ThrowIfNull(inputs);
            ArgumentNullException.ThrowIfNull(densities);

            var lines = inputs.Select(i =>
            {
                double density = densities.For(i.Element, i.Variant);
                double rebar = i.ConcreteVolume * density;
                return new StructuralTakeoffLine(
                    i.Level, i.Element, i.Variant, i.Grade,
                    i.ConcreteVolume, rebar, i.FormworkArea, density);
            }).ToList();

            return new StructuralTakeoffResult(
                lines,
                lines.Sum(l => l.ConcreteVolume),
                lines.Sum(l => l.RebarWeight),
                lines.Sum(l => l.FormworkArea),
                lines.GroupBy(l => l.Element).ToDictionary(g => g.Key, g => g.Sum(l => l.RebarWeight)),
                lines.GroupBy(l => l.Level).ToDictionary(g => g.Key, g => g.Sum(l => l.RebarWeight)),
                densities.Unit);
        }
    }
}
