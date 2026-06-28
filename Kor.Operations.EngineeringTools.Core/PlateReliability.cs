#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

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
        /// <summary>No thickness anywhere — the plate cannot be priced and must be flagged.</summary>
        None,
    }

    public enum Confidence { High, Medium, Low }

    /// <summary>One reason a plate is less than fully trusted — surfaced verbatim in the synopsis / orange cell.</summary>
    public readonly record struct ReliabilityFlag(string Code, string Detail);

    /// <summary>
    /// The per-plate trust verdict the product hangs on: "near-100% with anything unknown in orange."
    /// A plate's AREA is only as good as the poch&#233; that measured it, and its THICKNESS only as good
    /// as its source. This turns the measurement's own diagnostics into a High/Medium/Low call plus the
    /// human-readable reasons — so the on-screen synopsis can list the unsure areas and the xlsx can paint
    /// exactly those cells orange with a variable input, instead of shipping a silent guess. It is general:
    /// every signal is a property of the MEASUREMENT (how sealed, how fragmented, how it compares to its
    /// peers), never anything hard-coded to one drawing.
    /// </summary>
    public sealed record PlateReliability(Confidence Level, IReadOnlyList<ReliabilityFlag> Flags)
    {
        public bool NeedsReview => Level != Confidence.High;
    }

    public static class PlateReliabilityScorer
    {
        // A well-measured plate's enclosed concrete fills most of its located box; when the flood-fill
        // leaks through an open/curved boundary (a podium edge, a match line drawn light), little of the
        // box is enclosed and the area is a gross UNDER-count. Below LeakyFill the area is untrustworthy.
        public const double LeakyFillRatio = 0.55;
        public const double SoftFillRatio = 0.72;   // between soft and leaky: measurable but worth a look

        // A plate fractured into very many enclosed components (no dominant plate) usually means grid lines
        // or steps the clustering could not reunite — the largest-cluster area then under-counts.
        public const int FragmentedClusterCount = 40;

        // A plate far from its peers' area (same level group / tower band) is a likely locate error
        // (grabbed a sub-region, or a neighbouring plan) — flag both the small and the oversized outlier.
        public const double PeerAreaLowFactor = 0.6;
        public const double PeerAreaHighFactor = 1.6;

        /// <summary>
        /// Assess one plate. <paramref name="fillRatio"/> = enclosed (light+dark) area / located-box area
        /// (0..1); pass NaN when not computed. <paramref name="peerAreaRatio"/> = this plate's area /
        /// the median area of its peer group; pass NaN when there is no peer group (a 1:1 level).
        /// </summary>
        public static PlateReliability Assess(
            double fillRatio,
            int clusterCount,
            ThicknessSource thickness,
            bool degenerateBoxSubstituted,
            double peerAreaRatio)
        {
            var flags = new List<ReliabilityFlag>();
            var level = Confidence.High;
            void Demote(Confidence to) { if (to > level) level = to; }

            // ── thickness provenance ───────────────────────────────────────────────────────────
            switch (thickness)
            {
                case ThicknessSource.None:
                    flags.Add(new("THK_NONE", "No slab-thickness callout, and no sibling half to inherit from — plate cannot be priced."));
                    Demote(Confidence.Low);
                    break;
                case ThicknessSource.SynthesisFallback:
                    flags.Add(new("THK_SYNTH", "Thickness came from the image read, not an exact callout — verify against the drawing."));
                    Demote(Confidence.Medium);
                    break;
                case ThicknessSource.SiblingReconcile:
                    flags.Add(new("THK_SIBLING", "Thickness inherited from this level's other match-line half (no callout on this sheet)."));
                    Demote(Confidence.Medium);
                    break;
            }

            // ── locate / box quality ───────────────────────────────────────────────────────────
            if (degenerateBoxSubstituted)
            {
                flags.Add(new("BOX_DEGENERATE", "Locate box was implausibly small; area substituted from the tower median — verify the plate."));
                Demote(Confidence.Low);
            }

            // ── area measurement quality ───────────────────────────────────────────────────────
            if (!double.IsNaN(fillRatio))
            {
                if (fillRatio < LeakyFillRatio)
                {
                    flags.Add(new("AREA_LEAKY", $"Only {fillRatio:P0} of the located box encloses — an open/leaky slab boundary; the area is likely UNDER-measured."));
                    Demote(Confidence.Low);
                }
                else if (fillRatio < SoftFillRatio)
                {
                    flags.Add(new("AREA_SOFT", $"{fillRatio:P0} of the box encloses — boundary partly open; treat the area as approximate."));
                    Demote(Confidence.Medium);
                }
            }

            if (clusterCount >= FragmentedClusterCount)
            {
                flags.Add(new("AREA_FRAGMENTED", $"Plate split into {clusterCount} enclosed pieces — grid/steps not reunited; largest-cluster area may under-count."));
                Demote(Confidence.Medium);
            }

            // ── peer comparison ────────────────────────────────────────────────────────────────
            if (!double.IsNaN(peerAreaRatio))
            {
                if (peerAreaRatio < PeerAreaLowFactor)
                {
                    flags.Add(new("AREA_SMALL_VS_PEERS", $"Area is {peerAreaRatio:P0} of its peers' median — locate may have grabbed only a sub-region."));
                    Demote(Confidence.Low);
                }
                else if (peerAreaRatio > PeerAreaHighFactor)
                {
                    flags.Add(new("AREA_LARGE_VS_PEERS", $"Area is {peerAreaRatio:P0} of its peers' median — locate may have grabbed a neighbouring plan or open-to-below."));
                    Demote(Confidence.Medium);
                }
            }

            return new PlateReliability(level, flags);
        }
    }
}
