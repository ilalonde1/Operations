#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// Parses column cross-section dimensions from PDF text annotations.
    /// Recognises:
    ///   600x600, 600X600, 600600          rectangular (WD in mm)
    ///   C1-600x800, C1 600x800             labelled rectangular
    ///   Ø500, D=500, dia=500, R500         circular (diameter  square equivalent stored as W=D=dia)
    ///   8"x8", 18"x24"                     imperial inches
    /// Returns (WidthMm, DepthMm); for circular columns W=D=diameter.
    /// </summary>
    internal static class ColumnSectionParser
    {
        private static readonly (Regex Re, bool IsInches)[] _rectPatterns =
        {
            (new Regex(@"(?:[A-Za-z]\d*[-\s]?)?(\d+(?:\.\d+)?)\s*[xX]\s*(\d+(?:\.\d+)?)\s*(mm)?",
                RegexOptions.IgnoreCase), false),
            (new Regex(@"(\d+(?:\.\d+)?)""\s*[xX]\s*(\d+(?:\.\d+)?)""",
                RegexOptions.IgnoreCase), true),
        };

        private static readonly (Regex Re, bool IsInches)[] _circPatterns =
        {
            (new Regex(@"[ØøΦ]\s*(\d+(?:\.\d+)?)\s*(mm)?", RegexOptions.IgnoreCase), false),
            (new Regex(@"\b(?:D|dia(?:meter)?)\s*=?\s*(\d+(?:\.\d+)?)\s*(mm)?",
                RegexOptions.IgnoreCase), false),
            (new Regex(@"\bR\s*=?\s*(\d+(?:\.\d+)?)\s*(mm)?",
                RegexOptions.IgnoreCase), false),
            (new Regex(@"(\d+(?:\.\d+)?)""?\s*(?:dia(?:meter)?|circ)",
                RegexOptions.IgnoreCase), true),
        };

        private const double MinMm = 100, MaxMm = 3000;

        public static (double WidthMm, double DepthMm)? TryParse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            foreach (var (re, isInches) in _rectPatterns)
            {
                var m = re.Match(text);
                if (!m.Success) continue;
                if (!double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double w)) continue;
                if (!double.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double d)) continue;
                if (isInches) { w *= 25.4; d *= 25.4; }
                if (w >= MinMm && w <= MaxMm && d >= MinMm && d <= MaxMm)
                    return (w, d);
            }

            int circIdx = 0;
            foreach (var (re, isInches) in _circPatterns)
            {
                var m = re.Match(text);
                if (!m.Success) { circIdx++; continue; }
                if (!double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double val)) { circIdx++; continue; }
                if (isInches) val *= 25.4;
                if (circIdx == 2) val *= 2.0;
                if (val >= MinMm && val <= MaxMm)
                    return (val, val);
                circIdx++;
            }

            return null;
        }

        /// <summary>
        /// For each column, finds the nearest text annotation within
        /// <paramref name="searchRadiusMm"/> that parses as a column section.
        /// Returns null entries where no annotation was found.
        /// </summary>
        public static (double WidthMm, double DepthMm)?[] AssignToColumns(
            IReadOnlyList<(double X, double Y)> columns,
            IReadOnlyList<(string Text, double X, double Y)> annotations,
            double searchRadiusMm = 3000)
        {
            var result = new (double, double)?[columns.Count];

            var parsed = new List<(double X, double Y, double W, double D)>();
            foreach (var (text, ax, ay) in annotations)
            {
                var s = TryParse(text);
                if (s.HasValue) parsed.Add((ax, ay, s.Value.WidthMm, s.Value.DepthMm));
            }
            if (parsed.Count == 0) return result;

            for (int i = 0; i < columns.Count; i++)
            {
                double bestDist = searchRadiusMm;
                (double, double)? best = null;
                foreach (var (ax, ay, w, d) in parsed)
                {
                    double dist = Math.Sqrt(
                        (ax - columns[i].X) * (ax - columns[i].X) +
                        (ay - columns[i].Y) * (ay - columns[i].Y));
                    if (dist < bestDist) { bestDist = dist; best = (w, d); }
                }
                result[i] = best;
            }
            return result;
        }
    }
}
