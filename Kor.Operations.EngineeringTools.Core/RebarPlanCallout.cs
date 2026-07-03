#nullable enable
using System;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.RebarChange
{
    /// <summary>
    /// The CSA metric PLAN call-out grammar: <c>[count-][C]{size}M{length} [@ {spacing}]</c> —
    /// "36-15M4700 @ 125" (36 bars, 15M, 4700 mm long, at 125 spacing), "2-15M6000 @ 700",
    /// "16-C25M4000 @ 150", "C15M1200 @ 350", "35M4500 @ 300". This is how reinforcing is called
    /// up ON THE PLANS (slab bottom/top steel, extra bars) — as opposed to the schedule-intensity
    /// form "15M @ 200" (no length) and the bar-list form "16-15M13.9" (feet-inch length), which
    /// have their own grammars. The glued mm length is what makes a plan call-out both matchable
    /// and WEIGHABLE: count × length × CSA bar mass, no other measurement needed.
    ///
    /// One definition shared by the text extractor, the overlay boxer and the weigher, so they can
    /// never disagree about what a plan call-out is. Guards are physical, not tuned: bar size must
    /// be a real CSA bar; length 500–20000 mm (shortest practical bar to longest coupled run — and
    /// requiring ≥3 glued digits already excludes the intensity/bar-list forms); spacing kept in
    /// the key only when 50–900 mm (outside that, the "@ n" is a detail reference, not a spacing).
    /// </summary>
    public static class RebarPlanCallout
    {
        // Text form, for page-string scans: count and C optional, mm length glued to the size,
        // optional "@ spacing" (possibly spaced). Case-sensitive: bar suffix is uppercase M —
        // lowercase "mm" in dimensions like "(115mm)" must not match.
        public static readonly Regex TextRe = new(
            @"(?<![\d.])(?:(\d{1,3})-)?(C)?(\d{2})M(\d{3,5})(?:\s*@\s*(\d{2,4}))?\b",
            RegexOptions.Compiled);

        // Word forms, for positioned word-walks: the whole call-out in one token, or the
        // qty-size-length token with the spacing following as separate "@" / "125" words.
        public static readonly Regex WordFull = new(
            @"^(?:(\d{1,3})-)?(C)?(\d{2})M(\d{3,5})@(\d{2,4})$", RegexOptions.Compiled);
        public static readonly Regex WordStart = new(
            @"^(?:(\d{1,3})-)?(C)?(\d{2})M(\d{3,5})@?$", RegexOptions.Compiled);

        // Normalized key form ("36-15M4700@125", "C15M1200@350", "6-35M9000"), for the weigher.
        private static readonly Regex KeyRe = new(
            @"^(?:(\d{1,3})-)?(C)?(\d{2})M(\d{3,5})(?:@(\d{2,4}))?$", RegexOptions.Compiled);

        public const int LengthMinMm = 500, LengthMaxMm = 20000;
        public const int SpacingMinMm = 50, SpacingMaxMm = 900;

        public readonly record struct Parsed(int? Count, bool Continuous, int SizeM, int LengthMm, int? SpacingMm)
        {
            /// <summary>The canonical diff key: count-C-size-length, with spacing only when plausible.</summary>
            public string Key =>
                (Count is int q ? $"{q}-" : "") + (Continuous ? "C" : "") + $"{SizeM}M{LengthMm}"
                + (SpacingMm is int s ? $"@{s}" : "");

            /// <summary>Bar length in metres — the quantity the weigher multiplies by count × kg/m.</summary>
            public double LengthM => LengthMm / 1000.0;
        }

        /// <summary>Validate + normalize a regex match (any of the forms above; group order is
        /// identical across them). Null when the size is not a real CSA bar or the length is not a
        /// physical bar length. An implausible spacing drops out of the key rather than killing the
        /// call-out — the qty-size-length is the identity anchor.</summary>
        public static Parsed? FromGroups(Match m)
        {
            int size = int.Parse(m.Groups[3].Value);
            if (!RebarWeightEstimator.BarMassKgM.ContainsKey(size)) return null;
            int len = int.Parse(m.Groups[4].Value);
            if (len < LengthMinMm || len > LengthMaxMm) return null;
            int? count = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : null;
            if (count is <= 0) return null;
            int? spacing = m.Groups[5].Success ? int.Parse(m.Groups[5].Value) : null;
            if (spacing is int sp && (sp < SpacingMinMm || sp > SpacingMaxMm)) spacing = null;
            return new Parsed(count, m.Groups[2].Success, size, len, spacing);
        }

        /// <summary>Parse a canonical key back into its parts (for weighing), or null if the key is
        /// not a plan call-out (intensity "15M@200" has no glued length; bar-list "16-15M13.9" has a
        /// feet-inch dot).</summary>
        public static Parsed? ParseKey(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            var m = KeyRe.Match(key);
            return m.Success ? FromGroups(m) : null;
        }

        /// <summary>Weight in pounds of ONE instance of this call-out: count × length × CSA kg/m,
        /// converted to lb. Null when unweighable (no bar count — e.g. a continuous "C15M1200@350",
        /// whose bar count depends on the run extent the call-out doesn't state).</summary>
        public static double? WeightLb(Parsed c) =>
            c.Count is int q
                ? q * c.LengthM * RebarWeightEstimator.BarMassKgM[c.SizeM] * 2.2046226218
                : null;
    }
}
