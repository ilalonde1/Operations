#nullable enable
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.RebarChange
{
    /// <summary>
    /// Turns the BAR-LIST call-outs <see cref="RebarCalloutExtractor"/> reads ("16-15M13.9" = 16 bars,
    /// 15M, 13'-9") into a steel WEIGHT, so the change-detector can also report lb per sheet/issue — the
    /// number Griffin's manual calculators produce by hand. Weight = quantity × bar length × CSA bar mass
    /// (reusing <see cref="RebarWeightEstimator.BarMassKgM"/>, not a second copy of the masses).
    ///
    /// HONEST SCOPE: a sum of the readable, QUANTITY-BEARING bar call-outs only. It is NOT a full per-element
    /// rebar model — it does not add mat-by-area, perimeter hooks, stud rails, stirrups or hairpins the way a
    /// manual takeoff does — so it will not match a hand calc's total. A CONTINUOUS call-out with no bar count
    /// ("C15M3.11") cannot be weighed from a length alone and is reported as an unweighable residual, never guessed.
    /// </summary>
    public static class RebarBarListWeigher
    {
        // qty - [C] - sizeM - feet . inches  (the exact token RebarCalloutExtractor keys on, upper-cased).
        private static readonly Regex KeyRe =
            new(@"^(?:(\d{1,3})-)?(C)?(\d{2})M(\d{1,2})\.(\d{1,2})$", RegexOptions.Compiled);

        private const double KgPerM_to_LbPerFt = 0.671969; // 1 kg/m = 0.671969 lb/ft

        public readonly record struct BarCallout(int? Qty, int SizeM, double LengthFt, bool Continuous);

        /// <summary>Parse a bar-list call-out key, or null if it is not one (an intensity "15M@200" returns
        /// null). Length is FEET-INCHES: "13.9" = 13'-9", "9.10" = 9'-10" — the trailing field is inches
        /// (0–11), not a decimal foot (the real "9.10"/"3.11" tokens prove that reading).</summary>
        public static BarCallout? Parse(string key)
        {
            var m = KeyRe.Match(key);
            if (!m.Success) return null;
            int size = int.Parse(m.Groups[3].Value);
            if (!RebarWeightEstimator.BarMassKgM.ContainsKey(size)) return null;   // not a real Canadian bar
            int inches = int.Parse(m.Groups[5].Value);
            if (inches > 11) return null;                                          // not a feet-inches length we trust
            double lengthFt = int.Parse(m.Groups[4].Value) + inches / 12.0;
            int? qty = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : (int?)null;
            return new BarCallout(qty, size, lengthFt, m.Groups[2].Success);
        }

        /// <summary>Weight (lb) of one call-out instance: qty × length × CSA mass. 0 when no quantity.</summary>
        public static double WeightLb(BarCallout c) =>
            c.Qty is int q ? q * c.LengthFt * RebarWeightEstimator.BarMassKgM[c.SizeM] * KgPerM_to_LbPerFt : 0;

        public readonly record struct SheetWeight(double WeightLb, int WeighedCallouts, int UnweighableCallouts);

        /// <summary>Total bar-list weight for one sheet's call-out multiset (count × per-call-out weight),
        /// plus how many call-outs could not be weighed (continuous with no bar count).</summary>
        public static SheetWeight Weigh(IReadOnlyDictionary<string, int> callouts)
        {
            double lb = 0; int weighed = 0, unweighable = 0;
            foreach (var kv in callouts)
            {
                var c = Parse(kv.Key);
                if (c is null) continue;                                   // intensity key — not a bar-list weight
                if (c.Value.Qty is null) { unweighable += kv.Value; continue; }
                lb += kv.Value * WeightLb(c.Value);
                weighed += kv.Value;
            }
            return new SheetWeight(lb, weighed, unweighable);
        }
    }
}
