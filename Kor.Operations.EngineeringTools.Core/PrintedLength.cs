#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// A length as a drawing PRINTS it, turned into millimetres. One parser, for every schedule.
    /// </summary>
    /// <remarks>
    /// The repo already handled units on the geometry side — <c>PlanClassificationOptions.InUnitOf</c>
    /// rescales every threshold into whatever unit the drawing is drawn in. What did not exist was a
    /// reader for units as TEXT: nothing turned the string <c>4' - 0"</c> into a number.
    ///
    /// The gap was invisible because each schedule reader wrote its own size pattern, and the first
    /// one written happened to be metric — it required a 3-4 digit integer, so it read
    /// <c>2500 x 2500 x 900 DEEP</c> and could not read <c>4' - 0" x 4' - 0" x 26" DEEP</c>.
    ///
    /// Measured over five KOR jobs on 2026-09-02, the deterministic footing takeoff returned a number
    /// on ONE: 31065, at 1,174 cy, the only metric set. 31138 and 31130 print imperial and returned
    /// 0 cy each, silently — indistinguishable from a job with no footings.
    ///
    /// So the unit is not a property of the tool. It is a property of the drawing, it is printed on
    /// the drawing, and it is read here rather than assumed anywhere.
    ///
    /// WHAT THIS PARSES: feet-and-inches with the usual separators and quote glyphs (4'-0", 4' - 0",
    /// 4’-0”), bare inches (26", 26.5", 3 1/2"), bare feet (12'), and a bare number, which is taken
    /// as millimetres because that is what an unmarked number on a metric schedule means.
    ///
    /// WHAT IT DOES NOT: it does not guess. Text carrying no unit mark and no plausible metric
    /// magnitude is refused rather than assumed — assuming is how 4 becomes 4 mm. It also does not
    /// know what a number MEANS: which of three numbers is the depth is the schedule's business, and
    /// stays with the reader that knows the table.
    /// </remarks>
    public static class PrintedLength
    {
        public const double MmPerInch = 25.4;
        public const double MmPerFoot = 304.8;

        // Anchored at the start and deliberately NOT at the end: a schedule cell reads
        // "26\" DEEP 7-20M@3.6 EACH WAY BOT." and the length is only its first token.
        private static readonly Regex FeetLead = new(
            @"^\s*(?<ft>\d+(?:\.\d+)?)\s*['’′]\s*(?:[-–]\s*)?(?:(?<in>\d+(?:\.\d+)?)(?:\s+(?<n>\d+)\s*/\s*(?<d>\d+))?\s*[""”″]?)?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex InchLead = new(
            @"^\s*(?:(?<in>\d+(?:\.\d+)?)\s*)?(?:(?<n>\d+)\s*/\s*(?<d>\d+)\s*)?[""”″]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex BareLead = new(
            @"^\s*(?<mm>\d{2,5}(?:\.\d+)?)\s*(?:mm)?\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static double D(Group g) =>
            g.Success ? double.Parse(g.Value, CultureInfo.InvariantCulture) : 0.0;

        private static double Fraction(Match m) =>
            m.Groups["d"].Success && D(m.Groups["d"]) != 0
                ? D(m.Groups["n"]) / D(m.Groups["d"])
                : 0.0;

        /// <summary>
        /// The length written at the START of this text, in millimetres, ignoring whatever follows.
        /// Null when the text does not begin with a length this can read.
        /// </summary>
        public static double? LeadingMm(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var m = FeetLead.Match(text);
            if (m.Success)
                return D(m.Groups["ft"]) * MmPerFoot + (D(m.Groups["in"]) + Fraction(m)) * MmPerInch;

            m = InchLead.Match(text);
            if (m.Success && (m.Groups["in"].Success || m.Groups["n"].Success))
                return (D(m.Groups["in"]) + Fraction(m)) * MmPerInch;

            m = BareLead.Match(text);
            if (m.Success) return D(m.Groups["mm"]);

            return null;
        }

        /// <summary>Millimetres, for text that is a length and nothing else.</summary>
        public static double? TryParseMm(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string s = text.Trim();
            // reject anything with a letter other than a trailing "mm", so prose cannot parse
            if (Regex.IsMatch(s, @"[A-Za-ln-z]", RegexOptions.CultureInvariant)) return null;
            return LeadingMm(s);
        }

        /// <summary>The same, in inches — the unit the DXF and ETABS sides work in.</summary>
        public static double? TryParseInches(string? text)
            => LeadingMm(text) is double mm ? mm / MmPerInch : null;

        /// <summary>
        /// Split a printed size — <c>4' - 0" x 4' - 0" x 26" DEEP ...</c> or
        /// <c>2500 x 2500 x 900 DEEP</c> or <c>18" WIDE x 12" DEEP</c> — into its parts, in mm.
        /// </summary>
        /// <remarks>
        /// Reads left to right and stops at the first part that is not a length, so the reinforcing
        /// text that follows the last dimension ends the size rather than defeating it. Returns null
        /// if fewer than two parts read, which is not a size.
        /// </remarks>
        public static IReadOnlyList<double>? TryParseSizeMm(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            string[] parts = Regex.Split(text, @"\s*[xX×]\s*");
            var mm = new List<double>(parts.Length);
            foreach (string part in parts)
            {
                if (LeadingMm(part) is not double v) break;
                mm.Add(v);
            }
            return mm.Count >= 2 ? mm : null;
        }

        // ── finding a size INSIDE a row, wherever the schedule put it ────────────────────────────
        //
        // Splitting a whole row on "x" and reading left to right assumes the size is the first thing
        // in it. 31130 prints MARK | STRENGTH | SIZE, so the row reads "45 MPa 12\" x 24\"" and the
        // leading 45 parses as 45 mm — giving a 45 x 610 column, which is then rejected as
        // implausible and the row is lost silently. 31168 prints MARK | SIZE | STRENGTH and works.
        // One practice's column order should not decide whether a schedule reads.
        //
        // So the size is found by anchoring on the × and requiring BOTH sides to be lengths that
        // carry a unit mark, or to be the 3-4 digit integers a metric schedule prints. A bare "45"
        // beside "MPa" is neither.
        // A fractional inch is written "28 1/2"" or "1/2"" — a whole, a space, a fraction, the mark.
        // Without the fraction alternative, PL2's "28 1/2" x 36"" on 31138 matched from the 2 of
        // its fraction as 2" x 36", read as 51 x 914 mm, and was refused as implausible, silently.
        private const string LenPattern =
            @"(?:\d+(?:\.\d+)?\s*['’′]\s*(?:[-–]\s*)?(?:\d+(?:\.\d+)?(?:\s+\d+\s*/\s*\d+)?)?\s*[""”″]?" +   // 4' - 0", 4' - 6 1/2"
            @"|\d+(?:\.\d+)?(?:\s+\d+\s*/\s*\d+)?\s*[""”″]" +                                            // 26", 28 1/2"
            @"|\d+\s*/\s*\d+\s*[""”″]" +                                                                  // 1/2"
            @"|\d{3,4})";                                                                                 // 2500

        // an optional cell keyword between a dimension and the next ×, as in 18" WIDE x 12" DEEP
        private const string Gap = @"(?:\s+(?:WIDE|DEEP|DP|THK|THICK))?\s*[xX×]\s*";

        private static readonly Regex SizeAnywhere = new(
            "(?<a>" + LenPattern + ")" + Gap + "(?<b>" + LenPattern + ")" +
            "(?:" + Gap + "(?<c>" + LenPattern + "))?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// The first printed size anywhere in this text, in millimetres, whatever sits around it.
        /// </summary>
        public static IReadOnlyList<double>? TryFindSizeMm(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            foreach (Match m in SizeAnywhere.Matches(text))
            {
                if (LeadingMm(m.Groups["a"].Value) is not double a) continue;
                if (LeadingMm(m.Groups["b"].Value) is not double b) continue;

                var dims = new List<double> { a, b };
                if (m.Groups["c"].Success && LeadingMm(m.Groups["c"].Value) is double c)
                    dims.Add(c);
                return dims;
            }
            return null;
        }
    }
}
