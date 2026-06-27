#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>Severity of a reconciliation flag raised against a measured plate.</summary>
    public enum PlanFlagSeverity
    {
        /// <summary>Worth noting; does not by itself lower confidence.</summary>
        Info,
        /// <summary>The estimator must confirm this value before trusting the line.</summary>
        Review,
        /// <summary>The number is almost certainly wrong as-is (e.g. a "slab" thick enough to be a transfer).</summary>
        Critical
    }

    /// <summary>A single diligence finding about one measured plate.</summary>
    public sealed record PlanFlag(PlanFlagSeverity Severity, string Code, string Message);

    /// <summary>Outcome of reconciling one measured plate: a confidence and the findings behind it.</summary>
    public sealed record PlanCheck(TakeoffConfidence Confidence, IReadOnlyList<PlanFlag> Flags)
    {
        public bool HasCritical => Flags.Any(f => f.Severity == PlanFlagSeverity.Critical);
    }

    /// <summary>
    /// Per-project calibration: regional reinforcing norms (lb/cu.yd) and the plausible-thickness
    /// envelopes used to sanity-check measurements. These are the orange calibration cells the
    /// estimator tunes. Seeded from KOR's own QTOs — BC towers run roughly half the West-Coast-US
    /// reinforcing of high-seismic San Diego work, which is exactly why a single universal ratio
    /// cannot be assumed and the profile is per-project.
    /// </summary>
    public sealed record PlanProfile(
        string Name,
        double SlabRebarLbPerCy,
        double WallRebarLbPerCy,
        double ColumnRebarLbPerCy,
        double FoundationRebarLbPerCy,
        double SuspendedSlabMaxThicknessIn = 24.0)
    {
        /// <summary>BC / moderate seismic — measured off Coronation Heights Tower 1 (2026-06).</summary>
        public static PlanProfile BcModerate { get; } =
            new("BC-moderate", SlabRebarLbPerCy: 199, WallRebarLbPerCy: 375, ColumnRebarLbPerCy: 385, FoundationRebarLbPerCy: 150);

        /// <summary>San Diego / high seismic — seeded off the Lindley (Milano) takeoff.</summary>
        public static PlanProfile SdHighSeismic { get; } =
            new("SD-high-seismic", SlabRebarLbPerCy: 290, WallRebarLbPerCy: 500, ColumnRebarLbPerCy: 700, FoundationRebarLbPerCy: 480);

        public static PlanProfile ByName(string? name) => (name ?? "").Trim().ToLowerInvariant() switch
        {
            "sd-high-seismic" or "sd" or "sandiego" or "san-diego" => SdHighSeismic,
            _ => BcModerate,
        };

        /// <summary>Reinforcing density (lb/cu.yd) for an element under this profile. Beams fold into
        /// the floor/slab ratio — matching <see cref="ToImperialDensityTable"/> so the plausibility
        /// band is checked against the same ratio the report actually prices the beam at.</summary>
        public double RebarLbPerCy(TakeoffElementType element) => element switch
        {
            TakeoffElementType.Wall => WallRebarLbPerCy,
            TakeoffElementType.Column => ColumnRebarLbPerCy,
            TakeoffElementType.Foundation => FoundationRebarLbPerCy,
            _ => SlabRebarLbPerCy, // Slab, DropPanel, Beam
        };

        /// <summary>
        /// This profile expressed as the imperial (lb/yd³) <see cref="StructuralDensityTable"/> the
        /// takeoff report consumes, so the generated xlsx prices reinforcing at the project's own
        /// regional ratios (the orange calibration cells) rather than the firm-wide defaults.
        /// </summary>
        public StructuralDensityTable ToImperialDensityTable() => new(
            UnitSystem.Imperial,
            new Dictionary<(TakeoffElementType, string?), double>
            {
                [(TakeoffElementType.Slab, null)] = SlabRebarLbPerCy,
                [(TakeoffElementType.DropPanel, null)] = SlabRebarLbPerCy,
                [(TakeoffElementType.Beam, null)] = SlabRebarLbPerCy,
                [(TakeoffElementType.Wall, null)] = WallRebarLbPerCy,
                [(TakeoffElementType.Wall, "shear")] = WallRebarLbPerCy,
                [(TakeoffElementType.Wall, "other")] = WallRebarLbPerCy,
                [(TakeoffElementType.Column, null)] = ColumnRebarLbPerCy,
                [(TakeoffElementType.Foundation, null)] = FoundationRebarLbPerCy,
            });
    }

    /// <summary>
    /// The diligence engine — the deterministic core of "the app grinds to find the truth". It does
    /// not just accept a measurement; it interrogates it the way an estimator would and surfaces
    /// doubt instead of inventing a number. The signature check is the one learned on Coronation:
    /// a 13,913 sq.ft "slab" that prices to 2,059 cu.yd can only be ~4 ft thick — it is a transfer
    /// slab, not a suspended slab, and a uniform-thickness assumption would miss it several-fold.
    /// </summary>
    public static class PlanReconciler
    {
        /// <summary>
        /// Reconcile one measured plate against structural plausibility and the project profile.
        /// </summary>
        /// <param name="element">Element kind the plate was classified as.</param>
        /// <param name="areaSqFt">Measured plan area (slabs/foundations) or footprint (walls/columns).</param>
        /// <param name="thicknessIn">Read slab/mat thickness, or wall/column least dimension, in inches.</param>
        /// <param name="profile">Project calibration profile.</param>
        /// <param name="scaleConfirmed">True if the drawing scale was cross-checked (title block vs grid).</param>
        /// <param name="rebarLbPerCyOverride">Optional per-plate reinforcing override to sanity-check.</param>
        public static PlanCheck Check(
            TakeoffElementType element,
            double areaSqFt,
            double thicknessIn,
            PlanProfile profile,
            bool scaleConfirmed = true,
            double? rebarLbPerCyOverride = null,
            string? levelLabel = null)
        {
            ArgumentNullException.ThrowIfNull(profile);
            var flags = new List<PlanFlag>();

            if (!(areaSqFt > 0))
                flags.Add(new(PlanFlagSeverity.Critical, "AREA_NONPOSITIVE",
                    "Measured area is zero or negative — the geometry trace failed; do not trust this line."));

            if (!(thicknessIn > 0))
                flags.Add(new(PlanFlagSeverity.Critical, "THK_UNRESOLVED",
                    "Thickness/dimension is unresolved — no callout was read; volume cannot be trusted."));

            // The Coronation L01 lesson: a suspended slab cannot be this thick — it is a transfer slab or mat.
            if ((element == TakeoffElementType.Slab || element == TakeoffElementType.DropPanel)
                && thicknessIn > profile.SuspendedSlabMaxThicknessIn)
                flags.Add(new(PlanFlagSeverity.Critical, "SLAB_TOO_THICK",
                    $"Slab thickness {thicknessIn:0.#}\" exceeds the {profile.SuspendedSlabMaxThicknessIn:0}\" suspended-slab limit — " +
                    "this is a transfer slab or mat foundation, not a suspended slab. Confirm the thickness zones; " +
                    "the volume swings several-fold if this is wrong."));

            // The OTHER half of the L01 lesson: a transfer/podium/mat level often shows a thin nominal
            // P/T-slab callout (e.g. 8–12") while built-up transfer or drop zones on the SAME plate run
            // several feet — and a thin reading sails under the SLAB_TOO_THICK limit unnoticed. When the
            // level looks transfer-prone and the slab reads thin, flag it so the thick zones get read.
            if ((element == TakeoffElementType.Slab || element == TakeoffElementType.Foundation)
                && thicknessIn > 0 && thicknessIn <= profile.SuspendedSlabMaxThicknessIn
                && IsTransferProneLevel(levelLabel))
                flags.Add(new(PlanFlagSeverity.Review, "TRANSFER_LEVEL_THK",
                    $"'{levelLabel}' is a transfer/podium/mat level — the {thicknessIn:0.#}\" plan callout may be only the " +
                    "nominal slab; confirm whether built-up transfer or drop zones thicken it (the L01 case ran 48\")."));

            if (!scaleConfirmed)
                flags.Add(new(PlanFlagSeverity.Review, "SCALE_UNCONFIRMED",
                    "Drawing scale taken from the title block only — not cross-checked against the grid spacing."));

            if (rebarLbPerCyOverride is double ovr && ovr > 0)
            {
                double norm = profile.RebarLbPerCy(element);
                if (ovr > norm * 2.0 || ovr < norm * 0.5)
                    flags.Add(new(PlanFlagSeverity.Review, "RATIO_OUT_OF_BAND",
                        $"Reinforcing {ovr:0} lb/cu.yd is far from the {profile.Name} norm of {norm:0} for {element} — confirm the ratio."));
            }

            // High only when nothing beyond informational was raised; any Review/Critical → Review.
            var confidence = flags.All(f => f.Severity == PlanFlagSeverity.Info)
                ? TakeoffConfidence.High
                : TakeoffConfidence.Review;
            return new PlanCheck(confidence, flags);
        }

        // Lowest occupied/structural levels and explicit transfer keywords — where built-up transfer
        // slabs, mats, and podium drops live. Word-boundary matched so "L17" never trips the "L1" rule.
        private static readonly System.Text.RegularExpressions.Regex TransferProne = new(
            @"\b(transfer|podium|parkade|basement|ground|grade|mezz(?:anine)?|mat|p[0-9]|b[0-9]|l0?1|lvl\s*0?1|level\s*0?1)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>True if a level label denotes a transfer-prone level (podium/mat/lowest floors).</summary>
        public static bool IsTransferProneLevel(string? levelLabel)
            => !string.IsNullOrWhiteSpace(levelLabel) && TransferProne.IsMatch(levelLabel);

        /// <summary>
        /// Cross-checks two independent scale estimates (e.g. title-block note vs measured grid
        /// spacing). Returns a flag if they disagree by more than <paramref name="tolerance"/>
        /// (fractional), else null. This is the app refusing to trust a single scale source.
        /// </summary>
        public static PlanFlag? ScaleAgreement(double metresPerPixelTitle, double metresPerPixelGrid, double tolerance = 0.03)
        {
            if (metresPerPixelTitle <= 0 || metresPerPixelGrid <= 0)
                return new PlanFlag(PlanFlagSeverity.Critical, "SCALE_INVALID", "A scale estimate is non-positive.");

            double diff = Math.Abs(metresPerPixelTitle - metresPerPixelGrid)
                / Math.Min(metresPerPixelTitle, metresPerPixelGrid);
            return diff > tolerance
                ? new PlanFlag(PlanFlagSeverity.Critical, "SCALE_DISAGREE",
                    $"Title-block scale and grid-measured scale disagree by {diff:P0} — resolve before measuring (half-size print or wrong scale note).")
                : null;
        }
    }
}
