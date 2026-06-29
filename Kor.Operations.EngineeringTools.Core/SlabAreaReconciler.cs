#nullable enable

using System.Collections.Generic;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>Which signal the reconciled slab area came from — drives both the number and the flag.</summary>
    public enum AreaBasis
    {
        GridConfirmed,      // grid envelope, confirmed by an agreeing poché — the trustworthy case
        GridPocheDisagree,  // grid used, but poché disagreed (poché leaked, or grabbed a neighbour) — review
        GridOnly,           // grid used, no poché cross-check available
        PocheOnly,          // no usable grid; poché used unconfirmed — review
        Unresolved,         // neither signal produced an area — orange, hand to adjudication
    }

    /// <summary>One plate's reconciled area and the diligence flags that explain it.</summary>
    public sealed record AreaConsensus(
        double AreaSqFt,
        AreaBasis Basis,
        double? GridNetSqFt,
        double? PocheSqFt,
        IReadOnlyList<PlanFlag> Flags);

    /// <summary>
    /// The "pegs converge" step for slab AREA. The structural-grid envelope is the stable ANCHOR (it does
    /// not wander run-to-run the way the raster poché does); the poché is the independent CROSS-CHECK. When
    /// they agree the area is trustworthy (green); when the poché is far off — a leaky/sparse plate that
    /// under-measured, or a box that grabbed a neighbour — the grid stands but the plate is flagged for
    /// review; when only one signal exists, that one is used and noted. Confidence EMERGES from agreement
    /// rather than being asserted, and every outcome folds into the existing <see cref="PlanFlag"/> system.
    /// </summary>
    public static class SlabAreaReconciler
    {
        /// <summary>Gross grid envelope → NET slab area: the plate fills most, not all, of its grid
        /// rectangle (core, shafts, balconies, re-entrants). Calibrated ≈0.92 from the Revit answer key;
        /// exposed so a job can be tuned against one hand-checked level, like the rebar density.</summary>
        public const double DefaultNetFactor = 0.92;

        // The poché is noisy, so the agreement band is generous: within this fraction of the grid-derived
        // net area, the two signals are "confirming each other"; outside it, the poché is not trustworthy.
        public const double AgreeLo = 0.70;
        public const double AgreeHi = 1.30;

        public static AreaConsensus Reconcile(GridFrame? grid, double scaleDenom, double? pocheSqFt,
            double netFactor = DefaultNetFactor)
        {
            var flags = new List<PlanFlag>();
            double? gridNet = grid is { IsUsable: true } ? grid.EnvelopeSqFt(scaleDenom) * netFactor : null;

            // A sheet drawn with more than one plan: the grid envelope is ONE plan's — note it so the floor
            // count / plan split is verified rather than silently trusted.
            if (grid?.MultiPlan == true)
                flags.Add(new(PlanFlagSeverity.Info, "AREA_MULTIPLAN",
                    "Sheet carries more than one plan; the grid envelope is a single plan — verify the plan/floor count."));

            if (gridNet is double gn && pocheSqFt is double pp && pp > 0)
            {
                double ratio = pp / gn;
                if (ratio >= AgreeLo && ratio <= AgreeHi)
                    return new(gn, AreaBasis.GridConfirmed, gn, pp, flags);   // both signals agree → trust

                if (ratio < AgreeLo)
                    flags.Add(new(PlanFlagSeverity.Review, "AREA_POCHE_LOW",
                        $"Poché area ({pp:N0} sqft) is only {ratio:P0} of the grid-derived {gn:N0} — a leaky/sparse plate that under-measured; the grid envelope was used."));
                else
                    flags.Add(new(PlanFlagSeverity.Review, "AREA_POCHE_HIGH",
                        $"Poché area ({pp:N0} sqft) is {ratio:P0} of the grid-derived {gn:N0} — the box may have grabbed a neighbouring plan; the grid envelope was used."));
                return new(gn, AreaBasis.GridPocheDisagree, gn, pp, flags);
            }

            if (gridNet is double g2)
            {
                flags.Add(new(PlanFlagSeverity.Info, "AREA_GRID_ONLY",
                    "Grid envelope used; no poché cross-check was available for this plate."));
                return new(g2, AreaBasis.GridOnly, g2, null, flags);
            }

            if (pocheSqFt is double p2 && p2 > 0)
            {
                flags.Add(new(PlanFlagSeverity.Review, "AREA_NO_GRID",
                    "No usable structural grid was read; the poché area was used without a cross-check — verify the plate."));
                return new(p2, AreaBasis.PocheOnly, null, p2, flags);
            }

            flags.Add(new(PlanFlagSeverity.Review, "AREA_UNRESOLVED",
                "Neither the structural grid nor the poché produced an area — the plate could not be measured; resolve before relying on it."));
            return new(0, AreaBasis.Unresolved, null, null, flags);
        }
    }
}
