#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    public static class VolumeCalculator
    {
        public static TakeoffResult Compute(
            IReadOnlyList<TakeoffMeasurement> measurements,
            RebarDensityTable densities)
        {
            ArgumentNullException.ThrowIfNull(measurements);
            ArgumentNullException.ThrowIfNull(densities);

            var lines = measurements.Select(m => ComputeLine(m, densities)).ToList();
            var resolvedLines = lines.Where(l => !l.Unresolved).ToList();
            var reviewLines = resolvedLines.Where(l => l.Confidence == TakeoffConfidence.Review).ToList();

            return new TakeoffResult(
                lines,
                resolvedLines.Sum(l => l.ConcreteM3),
                resolvedLines.Sum(l => l.FormworkM2),
                resolvedLines.Sum(l => l.RebarKg) / 1000,
                SumBy(resolvedLines, l => l.GradeCode),
                SumBy(resolvedLines, l => l.Level),
                lines.Count(l => l.Unresolved),
                reviewLines.Count,
                reviewLines.Sum(l => l.ConcreteM3));
        }

        private static TakeoffLineResult ComputeLine(TakeoffMeasurement measurement, RebarDensityTable densities)
        {
            return measurement.ElementType switch
            {
                TakeoffElementType.Slab => ComputeSlab(measurement, densities),
                TakeoffElementType.DropPanel => ComputeDropPanel(measurement, densities),
                TakeoffElementType.Wall => ComputeWall(measurement, densities),
                TakeoffElementType.Beam => ComputeBeam(measurement, densities),
                TakeoffElementType.Column => ComputeColumn(measurement, densities),
                _ => Unresolved(measurement, "ElementType")
            };
        }

        private static TakeoffLineResult ComputeSlab(TakeoffMeasurement m, RebarDensityTable densities)
        {
            if (m.AreaMm2 is null)
                return Unresolved(m, nameof(TakeoffMeasurement.AreaMm2));

            if (m.ThicknessMm is null)
                return Unresolved(m, nameof(TakeoffMeasurement.ThicknessMm));

            var netAreaMm2 = m.AreaMm2.Value - m.OpeningAreaMm2;
            if (netAreaMm2 <= 0)
                return Excluded(m, "Slab net area <= 0 (openings meet or exceed slab area) - excluded from totals");

            var concreteM3 = netAreaMm2 * m.ThicknessMm.Value / 1e9;
            var formworkM2 = netAreaMm2 / 1e6;
            return Resolved(m, densities, concreteM3, formworkM2);
        }

        private static TakeoffLineResult ComputeDropPanel(TakeoffMeasurement m, RebarDensityTable densities)
        {
            if (m.AreaMm2 is null)
                return Unresolved(m, nameof(TakeoffMeasurement.AreaMm2));

            if (m.ThicknessMm is null)
                return Unresolved(m, nameof(TakeoffMeasurement.ThicknessMm));

            var concreteM3 = m.AreaMm2.Value * m.ThicknessMm.Value / 1e9;
            return Resolved(m, densities, concreteM3, formworkM2: 0);
        }

        private static TakeoffLineResult ComputeWall(TakeoffMeasurement m, RebarDensityTable densities)
        {
            if (m.LengthMm is null)
                return Unresolved(m, nameof(TakeoffMeasurement.LengthMm));

            if (m.WidthMm is null)
                return Unresolved(m, nameof(TakeoffMeasurement.WidthMm));

            if (m.StoreyHeightMm is null)
                return Unresolved(m, nameof(TakeoffMeasurement.StoreyHeightMm));

            var concreteM3 = m.LengthMm.Value * m.WidthMm.Value * m.StoreyHeightMm.Value / 1e9;
            var formworkM2 = 2 * m.LengthMm.Value * m.StoreyHeightMm.Value / 1e6;
            return Resolved(m, densities, concreteM3, formworkM2);
        }

        private static TakeoffLineResult ComputeBeam(TakeoffMeasurement m, RebarDensityTable densities)
        {
            if (m.LengthMm is null)
                return Unresolved(m, nameof(TakeoffMeasurement.LengthMm));

            if (m.WidthMm is null)
                return Unresolved(m, nameof(TakeoffMeasurement.WidthMm));

            if (m.DepthMm is null)
                return Unresolved(m, nameof(TakeoffMeasurement.DepthMm));

            var concreteM3 = m.LengthMm.Value * m.WidthMm.Value * m.DepthMm.Value / 1e9;
            var formworkM2 = (2 * m.LengthMm.Value * m.DepthMm.Value + m.LengthMm.Value * m.WidthMm.Value) / 1e6;
            return Resolved(m, densities, concreteM3, formworkM2);
        }

        private static TakeoffLineResult ComputeColumn(TakeoffMeasurement m, RebarDensityTable densities)
        {
            if (m.WidthMm is null)
                return Unresolved(m, nameof(TakeoffMeasurement.WidthMm));

            if (m.DepthMm is null)
                return Unresolved(m, nameof(TakeoffMeasurement.DepthMm));

            if (m.StoreyHeightMm is null)
                return Unresolved(m, nameof(TakeoffMeasurement.StoreyHeightMm));

            var concreteM3 = m.WidthMm.Value * m.DepthMm.Value * m.StoreyHeightMm.Value / 1e9;
            var formworkM2 = 2 * (m.WidthMm.Value + m.DepthMm.Value) * m.StoreyHeightMm.Value / 1e6;
            return Resolved(m, densities, concreteM3, formworkM2);
        }

        private static TakeoffLineResult Resolved(
            TakeoffMeasurement m,
            RebarDensityTable densities,
            double concreteM3,
            double formworkM2)
        {
            var density = m.RebarDensityOverrideKgPerM3 ?? densities.For(m.ElementType);
            var confidence = IsHighConfidence(m) ? TakeoffConfidence.High : TakeoffConfidence.Review;

            return new TakeoffLineResult(
                m.ElementType,
                m.Level,
                m.GradeCode,
                concreteM3,
                formworkM2,
                concreteM3 * density,
                RebarSource.Density,
                confidence,
                Unresolved: false,
                Note: null);
        }

        private static TakeoffLineResult Unresolved(TakeoffMeasurement m, string missingField)
            => Excluded(m, $"{m.ElementType} {missingField} unresolved - excluded from totals");

        private static TakeoffLineResult Excluded(TakeoffMeasurement m, string note)
        {
            return new TakeoffLineResult(
                m.ElementType,
                m.Level,
                m.GradeCode,
                ConcreteM3: 0,
                FormworkM2: 0,
                RebarKg: 0,
                RebarSource.Density,
                TakeoffConfidence.Review,
                Unresolved: true,
                note);
        }

        private static bool IsHighConfidence(TakeoffMeasurement m)
        {
            return m.ScaleConfirmed &&
                (m.ElementType != TakeoffElementType.Slab || m.OpeningsChecked);
        }

        private static IReadOnlyDictionary<string, double> SumBy(
            IEnumerable<TakeoffLineResult> lines,
            Func<TakeoffLineResult, string> keySelector)
        {
            return lines
                .GroupBy(keySelector)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.ConcreteM3));
        }
    }
}