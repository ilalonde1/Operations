#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// Parses beam and wall cross-section dimensions from PDF text annotations.
    /// Recognises:
    ///   300x600, 300X600, 300600              width  depth (mm)
    ///   B1-300x600, B1 300x600                 labelled beam
    ///   W400xD800, w=300 d=600                 explicit W/D
    ///   200W, W=300                             width only  depth assumed 2 width
    ///   200 WALL, 200THK WALL, 200RC            wall  W=D=thickness
    ///   12"x24", 8"x16"                         imperial inches
    /// Returns (WidthMm, DepthMm).
    /// </summary>
    internal static class BeamSectionParser
    {
        private const double MinMm = 50, MaxMm = 3000;

        private static readonly (Regex Re, bool IsInches, bool IsWall, bool IsWidthOnly)[] _patterns =
        {
            (new Regex(@"\bW\s*[=x]?\s*(\d+(?:\.\d+)?)\s*[,\s]+D\s*[=x]?\s*(\d+(?:\.\d+)?)\s*(mm)?",
                RegexOptions.IgnoreCase), false, false, false),
            (new Regex(@"(?:[A-Za-z]\d*[-\s]?)?(\d+(?:\.\d+)?)\s*[xX]\s*(\d+(?:\.\d+)?)\s*(mm)?",
                RegexOptions.IgnoreCase), false, false, false),
            (new Regex(@"(\d+(?:\.\d+)?)""\s*[xX]\s*(\d+(?:\.\d+)?)""",
                RegexOptions.IgnoreCase), true, false, false),
            (new Regex(@"\b(\d+(?:\.\d+)?)\s*(mm)?\s*(?:THK\s*)?(?:WALL|RC\s*WALL|RC)\b",
                RegexOptions.IgnoreCase), false, true, false),
            (new Regex(@"\b(?:W\s*=\s*|(\d+(?:\.\d+)?)\s*W\b)(\d+(?:\.\d+)?)?",
                RegexOptions.IgnoreCase), false, false, true),
        };

        /// <summary>
        /// Attempts to parse (WidthMm, DepthMm) from a text annotation.
        /// Returns null if no pattern matches or values are out of range.
        /// </summary>
        public static (double WidthMm, double DepthMm)? TryParse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            foreach (var (re, isInches, isWall, isWidthOnly) in _patterns)
            {
                var m = re.Match(text);
                if (!m.Success) continue;

                if (isWall)
                {
                    if (!double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double t)) continue;
                    if (t >= MinMm && t <= MaxMm) return (t, t);
                    continue;
                }

                if (isWidthOnly)
                {
                    // "W=300"  group 2 has value; "300W"  group 1 has value
                    string valStr = m.Groups[1].Success && m.Groups[1].Value.Length > 0
                        ? m.Groups[1].Value
                        : m.Groups[2].Value;
                    if (!double.TryParse(valStr, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double wOnly))
                        continue;
                    double dOnly = wOnly * 2.0;
                    if (wOnly >= MinMm && wOnly <= MaxMm && dOnly <= MaxMm)
                        return (wOnly, dOnly);
                    continue;
                }

                if (m.Groups[2].Success &&
                    double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double w) &&
                    double.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double d))
                {
                    if (isInches) { w *= 25.4; d *= 25.4; }
                    if (w >= MinMm && w <= MaxMm && d >= MinMm && d <= MaxMm)
                        return (Math.Min(w, d), Math.Max(w, d));
                }
            }
            return null;
        }

        /// <summary>
        /// For each polyline in <paramref name="polylines"/>, finds the nearest text annotation
        /// within <paramref name="searchRadiusMm"/> of the polyline midpoint that parses as a
        /// beam section. Returns null entries where no annotation was found.
        /// </summary>
        public static (double WidthMm, double DepthMm)?[] AssignToLines(
            IReadOnlyList<List<(double X, double Y)>> polylines,
            IReadOnlyList<(string Text, double X, double Y)> annotations,
            double searchRadiusMm = 4000)
        {
            var result = new (double, double)?[polylines.Count];

            var parsed = new List<(double X, double Y, double W, double D)>();
            foreach (var (text, ax, ay) in annotations)
            {
                var s = TryParse(text);
                if (s.HasValue) parsed.Add((ax, ay, s.Value.WidthMm, s.Value.DepthMm));
            }
            if (parsed.Count == 0) return result;

            for (int i = 0; i < polylines.Count; i++)
            {
                var pts = polylines[i];
                if (pts.Count == 0) continue;

                double totalLen = 0;
                for (int j = 1; j < pts.Count; j++)
                    totalLen += PolygonProcessor.Distance(pts[j - 1], pts[j]);
                double half = totalLen / 2.0, acc = 0;
                (double X, double Y) mid = pts[pts.Count / 2];
                for (int j = 1; j < pts.Count; j++)
                {
                    double segLen = PolygonProcessor.Distance(pts[j - 1], pts[j]);
                    if (acc + segLen >= half)
                    {
                        double t = (half - acc) / segLen;
                        mid = (pts[j - 1].X + t * (pts[j].X - pts[j - 1].X),
                               pts[j - 1].Y + t * (pts[j].Y - pts[j - 1].Y));
                        break;
                    }
                    acc += segLen;
                }

                double bestDist = searchRadiusMm;
                (double, double)? best = null;
                foreach (var (ax, ay, w, d) in parsed)
                {
                    double dist = Math.Sqrt((ax - mid.X) * (ax - mid.X) + (ay - mid.Y) * (ay - mid.Y));
                    if (dist < bestDist) { bestDist = dist; best = (w, d); }
                }
                result[i] = best;
            }
            return result;
        }
    }
}
