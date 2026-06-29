#nullable enable

using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>How a plate's slab thickness was determined — drives both pricing and confidence.</summary>
    public enum ThicknessSource
    {
        /// <summary>Read from the drawing's own exact "N&quot; SLAB" callout — the trustworthy case.</summary>
        Callout,
        /// <summary>Inherited from the sibling match-line half of the same level (the half had no callout).</summary>
        SiblingReconcile,
        /// <summary>The synthesised image read — a fallback where no callout text exists. Soft.</summary>
        SynthesisFallback,
        /// <summary>No thickness anywhere — handled by the reconciler's THK_UNRESOLVED flag, not here.</summary>
        None,
    }

    /// <summary>
    /// The MEASUREMENT-quality half of the diligence engine: how much to trust the AREA the poch&#233;
    /// measured and the THICKNESS provenance — the signals <see cref="PlanReconciler"/> (which checks
    /// pricing/structural plausibility) does not see. It emits into the SAME <see cref="PlanFlag"/>
    /// vocabulary so both fold into one <see cref="PlanCheck"/> and one orange treatment — never a
    /// parallel confidence system. Every signal is a property of the measurement (how sealed, how
    /// fragmented, how it compares to its peers, where the thickness came from), never hard-coded to
    /// one drawing — so it generalises to any set.
    /// </summary>
    public static class PlateReliabilityScorer
    {
        // How densely the measured plate fills its OWN bounding box (largest-cluster area / bbox area).
        // A solid, well-sealed slab fills most of its extent; a leaky/open boundary (the L01 podium)
        // leaks to exterior and leaves a sparse skeleton spanning a large bbox -> low fill -> the area is
        // a gross UNDER-count. (NB: this is fill-of-own-extent, NOT enclosed/located-box — the latter only
        // measures the loose synthesis box's margin and cannot tell a good plate from a bad one.)
        public const double LeakyFillRatio = 0.30;
        public const double SoftFillRatio = 0.45;   // between soft and leaky: measurable but worth a look

        // A plate fractured into very many enclosed components (no dominant plate) usually means grid lines
        // or steps the clustering could not reunite — the largest-cluster area then under-counts.
        public const int FragmentedClusterCount = 40;

        // A plate far from its peers' area (same level group / tower band) is a likely locate error
        // (grabbed a sub-region, or a neighbouring plan / open-to-below) — flag both small and oversized.
        public const double PeerAreaLowFactor = 0.6;
        public const double PeerAreaHighFactor = 1.6;

        // Geometrically irregular levels whose plan area the poché under/over-measures even when the
        // measured cluster looks locally clean — the under-count comes from a too-small/leaky LOCATE, not
        // a sparse cluster (the L01 podium and ROOF both scored "dense" yet were 3x off). An estimator
        // verifies these by level type. Word-boundary matched; "L17"/"13" never trip the level-1 rule.
        private static readonly Regex ComplexLevelRx = new(
            @"\b(transfer|podium|ground|grade|roof|penthouse|ph\d?|mezz(?:anine)?|0?1|l0?1|lvl\s*0?1|level\s*0?1)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>True if a level's geometry makes its plan-area measurement locate-prone (podium /
        /// transfer / ground / roof / mezzanine / the level-1 podium).</summary>
        public static bool IsComplexGeometryLevel(string? levelLabel)
            => !string.IsNullOrWhiteSpace(levelLabel) && ComplexLevelRx.IsMatch(levelLabel);

        /// <summary>
        /// Measurement-quality flags for one plate. <paramref name="fillRatio"/> = enclosed (light+dark)
        /// area / located-box area (0..1); pass NaN when not computed. <paramref name="peerAreaRatio"/> =
        /// this plate's area / the median area of its peer group; pass NaN when there is no peer group.
        /// Returns only the flags this layer owns; the caller merges them with the reconciler's via
        /// <see cref="PlanCheck.From"/>.
        /// </summary>
        public static IReadOnlyList<PlanFlag> MeasurementFlags(
            double fillRatio,
            int clusterCount,
            ThicknessSource thickness,
            bool degenerateBoxSubstituted,
            double peerAreaRatio,
            string? levelLabel = null)
        {
            var flags = new List<PlanFlag>();

            // Geometrically irregular level: the area is locate-prone even when the cluster looks clean.
            if (IsComplexGeometryLevel(levelLabel))
                flags.Add(new(PlanFlagSeverity.Review, "AREA_COMPLEX_LEVEL",
                    $"'{levelLabel}' is a podium/transfer/roof/mezzanine — an irregular plan the poché can mis-bound even when the measured plate looks clean; verify the area against the drawing."));

            // ── thickness provenance (None is the reconciler's THK_UNRESOLVED; not duplicated here) ──
            if (thickness == ThicknessSource.SynthesisFallback)
                flags.Add(new(PlanFlagSeverity.Review, "THK_SYNTH",
                    "Thickness came from the image read, not an exact callout — verify against the drawing."));
            else if (thickness == ThicknessSource.SiblingReconcile)
                flags.Add(new(PlanFlagSeverity.Info, "THK_SIBLING",
                    "Thickness inherited from this level's other match-line half (no callout on this sheet)."));

            // ── locate / box quality ───────────────────────────────────────────────────────────
            if (degenerateBoxSubstituted)
                flags.Add(new(PlanFlagSeverity.Review, "BOX_DEGENERATE",
                    "Locate box was implausibly small; area substituted from the tower median — verify the plate."));

            // ── area measurement quality ───────────────────────────────────────────────────────
            if (!double.IsNaN(fillRatio))
            {
                if (fillRatio < LeakyFillRatio)
                    flags.Add(new(PlanFlagSeverity.Review, "AREA_LEAKY",
                        $"Measured plate fills only {fillRatio:P0} of its own extent — an open/leaky slab boundary leaked to exterior; the area is likely UNDER-measured."));
                else if (fillRatio < SoftFillRatio)
                    flags.Add(new(PlanFlagSeverity.Info, "AREA_SOFT",
                        $"Plate fills {fillRatio:P0} of its extent — boundary partly open or an L-shaped/voided plate; treat the area as approximate."));
            }

            if (clusterCount >= FragmentedClusterCount)
                flags.Add(new(PlanFlagSeverity.Info, "AREA_FRAGMENTED",
                    $"Plate split into {clusterCount} enclosed pieces — grid/steps not reunited; largest-cluster area may under-count."));

            // ── peer comparison ────────────────────────────────────────────────────────────────
            if (!double.IsNaN(peerAreaRatio))
            {
                if (peerAreaRatio < PeerAreaLowFactor)
                    flags.Add(new(PlanFlagSeverity.Review, "AREA_SMALL_VS_PEERS",
                        $"Area is {peerAreaRatio:P0} of its peers' median — locate may have grabbed only a sub-region."));
                else if (peerAreaRatio > PeerAreaHighFactor)
                    flags.Add(new(PlanFlagSeverity.Review, "AREA_LARGE_VS_PEERS",
                        $"Area is {peerAreaRatio:P0} of its peers' median — locate may have grabbed a neighbouring plan or open-to-below."));
            }

            return flags;
        }
    }
}
