#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// Parses structural slab thickness annotations from raw PDF text strings.
    /// Recognises common formats used on Australian, Canadian, US and European drawings:
    ///   200 THK, 200mm, T=200, TS=200, t=200, 200 SLAB, 8" SLAB, 8"THK
    /// Returns thickness in millimetres.
    /// </summary>
    internal static class ThicknessAnnotationParser
    {
        // Patterns tried in order; first match wins.
        // All capture group 1 = numeric value, group 2 = unit (mm / " / blank)
        private static readonly (Regex Re, bool IsInches)[] _patterns = new[]
        {
            (new Regex(@"\bT[Ss]?\s*=\s*(\d+(?:\.\d+)?)\s*(mm|"")?", RegexOptions.IgnoreCase), false),
            (new Regex(@"\b(\d+(?:\.\d+)?)\s*(mm|"")?\s*THK(?:NESS)?\b", RegexOptions.IgnoreCase), false),
            (new Regex(@"\b(\d+(?:\.\d+)?)\s*(mm|"")?(?:\s+SLAB)\b", RegexOptions.IgnoreCase), false),
            (new Regex(@"\b(\d+(?:\.\d+)?)\s*mm\b", RegexOptions.IgnoreCase), false),
            (new Regex(@"\b(\d+(?:\.\d+)?)\s*""\s*(?:THK)?\b", RegexOptions.IgnoreCase), true),
        };

        /// <summary>
        /// Attempts to parse a thickness in mm from a raw annotation string.
        /// Returns null if no recognisable pattern is found or the value is out of range (50–2000 mm).
        /// </summary>
        public static double? TryParse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            foreach (var (re, isInches) in _patterns)
            {
                var m = re.Match(text);
                if (!m.Success) continue;
                if (!double.TryParse(m.Groups[1].Value,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double val))
                    continue;

                double mm = isInches ? val * 25.4 : val;
                if (mm >= 50 && mm <= 2000) return mm;
            }
            return null;
        }

        /// <summary>
        /// Given a list of annotation strings and their centroid positions (in the same
        /// coordinate space as the slab polygons), assigns a thickness (mm) to each slab
        /// by finding the nearest annotation whose text parses as a thickness and whose
        /// centroid lies within <paramref name="searchRadiusMm"/> of the slab centroid.
        /// Returns an array parallel to <paramref name="slabs"/>; null means no annotation found.
        /// </summary>
        public static double?[] AssignToSlabs(
            IReadOnlyList<List<(double X, double Y)>> slabs,
            IReadOnlyList<(string Text, double X, double Y)> annotations,
            double searchRadiusMm = 5000)
        {
            var result = new double?[slabs.Count];

            var parsed = new List<(double X, double Y, double ThicknessMm)>();
            foreach (var (text, ax, ay) in annotations)
            {
                var t = TryParse(text);
                if (t.HasValue) parsed.Add((ax, ay, t.Value));
            }

            if (parsed.Count == 0) return result;

            for (int i = 0; i < slabs.Count; i++)
            {
                var (cx, cy) = PolygonProcessor.PolygonAreaCentroid(slabs[i]);
                double bestDist = searchRadiusMm;
                double? bestThick = null;

                foreach (var (ax, ay, thick) in parsed)
                {
                    double d = Math.Sqrt((ax - cx) * (ax - cx) + (ay - cy) * (ay - cy));
                    if (d < bestDist) { bestDist = d; bestThick = thick; }
                }

                result[i] = bestThick;
            }
            return result;
        }
    }
}
