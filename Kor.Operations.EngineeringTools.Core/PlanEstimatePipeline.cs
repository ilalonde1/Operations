#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// One plate after geometry measurement, ready to price: its area is already measured (slab/mat
    /// plan area, or wall/column gray footprint), its governing dimension read off the drawing, and
    /// the number of identical floors it stands in for. Areas are in square feet, dimensions in
    /// inches — the imperial units the structural takeoff and density profiles use.
    /// </summary>
    public sealed record MeasuredPlate(
        string Level,
        TakeoffElementType Element,
        string? Variant,
        double AreaSqFt,
        double DimensionIn,        // slab/mat thickness, or wall/column storey height
        int Count = 1,
        string Grade = "",
        bool ScaleConfirmed = true,
        double? RebarLbPerCyOverride = null,
        // ── measurement-quality diagnostics (drive the reliability flags; defaults = "not assessed") ──
        double FillRatio = double.NaN,                                   // enclosed area / located-box area
        int ClusterCount = 0,                                           // enclosed-region fragment count
        ThicknessSource ThicknessSource = ThicknessSource.Callout,      // where the thickness came from
        bool DegenerateBox = false,                                     // area substituted from peer median
        double PeerAreaRatio = double.NaN);                             // area / peer-group median area

    /// <summary>A priced plate: its per-floor and total concrete, plus the diligence verdict.</summary>
    public sealed record PlateEstimate(
        MeasuredPlate Plate,
        double ConcretePerFloorCuYd,
        double ConcreteTotalCuYd,
        PlanCheck Check);

    /// <summary>The whole-building estimate: priced plates, the takeoff inputs the report consumes,
    /// totals, and the diligence tallies that tell the estimator where to look.</summary>
    public sealed record PlanEstimateResult(
        IReadOnlyList<PlateEstimate> Plates,
        IReadOnlyList<StructuralTakeoffInput> TakeoffInputs,
        double TotalConcreteCuYd,
        int CriticalCount,
        int ReviewCount);

    /// <summary>
    /// Orchestrates the deterministic spine of the stickfile → takeoff pipeline: turn measured plates
    /// into priced concrete, run each through <see cref="PlanReconciler"/>, and emit the
    /// <see cref="StructuralTakeoffInput"/> rows the existing report generator turns into the
    /// orange-celled xlsx. No image or LLM work here — measurement (Layer 1) and classification/
    /// thickness reading (Layer 2) happen upstream; this is where it all reconciles into a takeoff.
    /// </summary>
    public static class PlanEstimatePipeline
    {
        // square feet × (inches/12 → feet) = cubic feet; ÷ 27 = cubic yards.
        private const double CubicFeetPerCubicYard = 27.0;

        public static PlanEstimateResult Run(IReadOnlyList<MeasuredPlate> plates, PlanProfile profile)
        {
            ArgumentNullException.ThrowIfNull(plates);
            ArgumentNullException.ThrowIfNull(profile);

            var estimates = new List<PlateEstimate>(plates.Count);
            var inputs = new List<StructuralTakeoffInput>(plates.Count);

            foreach (var p in plates)
            {
                // Only price physically meaningful geometry. A failed trace (area <= 0) or an
                // unread thickness (<= 0) contributes 0 to the totals rather than a negative or
                // garbage volume — it is still reconciled below and surfaced as a flag.
                double perFloor = p.AreaSqFt > 0 && p.DimensionIn > 0
                    ? p.AreaSqFt * (p.DimensionIn / 12.0) / CubicFeetPerCubicYard
                    : 0.0;
                double total = perFloor * Math.Max(0, p.Count);
                // Two complementary diligence sources fold into ONE check: pricing/structural plausibility
                // (PlanReconciler) and measurement quality (how the area/thickness were actually obtained).
                var pricingFlags = PlanReconciler.Check(
                    p.Element, p.AreaSqFt, p.DimensionIn, profile, p.ScaleConfirmed, p.RebarLbPerCyOverride, p.Level).Flags;
                var measurementFlags = PlateReliabilityScorer.MeasurementFlags(
                    p.FillRatio, p.ClusterCount, p.ThicknessSource, p.DegenerateBox, p.PeerAreaRatio, p.Level);
                var check = PlanCheck.From(pricingFlags.Concat(measurementFlags).ToList());

                estimates.Add(new PlateEstimate(p, perFloor, total, check));
                inputs.Add(new StructuralTakeoffInput(p.Level, p.Element, p.Variant, total, FormworkArea: 0, p.Grade));
            }

            return new PlanEstimateResult(
                estimates,
                inputs,
                estimates.Sum(e => e.ConcreteTotalCuYd),
                estimates.Count(e => e.Check.HasCritical),
                estimates.Count(e => e.Check.Confidence == TakeoffConfidence.Review));
        }
    }
}
