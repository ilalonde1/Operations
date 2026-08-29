#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Globalization;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal sealed record RawSubpath(
        List<(double X, double Y)> Points,
        bool IsClosed,
        (byte R, byte G, byte B) Color,
        bool IsFilled,
        bool IsStroked,
        double LineWidth,
        bool IsAnnotation);

    internal static class PdfGeometryParser
    {
        public static List<RawSubpath>
            ParsePage(Page page, double scale)
        {
            double? minPointDistance = scale > 0
                ? PdfToSafeConstants.MinVertexDistanceMm / scale
                : null;
            double? closeDistance = scale > 0
                ? PdfToSafeConstants.MinVertexDistanceMm * 4.0 / scale
                : null;

            var pageRead = VectorPageReader.ReadPage(
                page,
                includeAnnotations: true,
                curveSegments: PdfToSafeConstants.BezierSegments,
                minPointDistance: minPointDistance,
                closeDistance: closeDistance);

            return pageRead.Paths
                .Select(path => new RawSubpath(
                    path.Points.Select(p => (p.X * scale, p.Y * scale)).ToList(),
                    path.IsClosed,
                    path.Color,
                    path.IsFilled,
                    path.IsStroked,
                    path.LineWidth,
                    path.IsAnnotation))
                .ToList();
        }

        public static int? DetectScale(string filePath, int pageNumber = 1)
        {
            try
            {
                string? note = SheetScaleReader.FromPage(VectorPageReader.ReadPage(filePath, pageNumber));
                double? metresPerPoint = PlanGeometry.MetresPerPixel(note, renderDpi: 72);
                if (metresPerPoint is not double mpp || mpp <= 0)
                    return null;

                int denominator = (int)Math.Round(mpp * 1000.0 / PdfToSafeConstants.PointsToMm);
                return denominator > 0 ? denominator : null;
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("PdfGeometryParser: scale detection failed: " + ex.Message); }
            return null;
        }

        public static List<(string Text, double X, double Y)> ExtractTextAnnotations(Page page, double scale)
        {
            var result = new List<(string Text, double X, double Y)>();
            foreach (var word in page.GetWords())
            {
                if (string.IsNullOrWhiteSpace(word.Text))
                    continue;

                var box = word.BoundingBox;
                double xMm = ((box.BottomLeft.X + box.TopRight.X) / 2.0) * scale;
                double yMm = ((box.BottomLeft.Y + box.TopRight.Y) / 2.0) * scale;
                result.Add((word.Text, xMm, yMm));
            }
            return result;
        }

        public static List<(string Text, double X, double Y)> ExtractMarkupTextAnnotations(Page page, double scale)
        {
            var result = new List<(string Text, double X, double Y)>();
            foreach (var ann in page.ExperimentalAccess.GetAnnotations())
            {
                string text = ann.Content?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var rect = ann.Rectangle;
                double xMm = ((rect.BottomLeft.X + rect.TopRight.X) / 2.0) * scale;
                double yMm = ((rect.BottomLeft.Y + rect.TopRight.Y) / 2.0) * scale;
                result.Add((text, xMm, yMm));
            }
            return result;
        }

        public static Dictionary<(byte R, byte G, byte B), double> ExtractThicknessHints(
            string filePath,
            int    pageNumber,
            int    scaleDenominator,
            ExtractedGeometry geometry)
        {
            var results = new Dictionary<(byte R, byte G, byte B), double>();
            if (geometry.Slabs.Count == 0 || geometry.SlabColors.Count == 0)
                return results;

            double scale = scaleDenominator * PdfToSafeConstants.PointsToMm;
            var candidatesByColor = new Dictionary<(byte R, byte G, byte B), List<int>>();

            static int? ParseThickness(string text)
            {
                string t = text.Trim().ToUpperInvariant();

                var m = Regex.Match(t, @"^(\d{2,4})\s*(THK|THICK|MM|T)$");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int v1) && v1 is >= 50 and <= 1000) return v1;

                m = Regex.Match(t, @"^[TDH]=(\d{2,4})$");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int v2) && v2 is >= 50 and <= 1000) return v2;

                m = Regex.Match(t, @"^(\d{2,4})\s*(SLAB|FLAT|FS|PT|RC|TOPPING)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int v3) && v3 is >= 50 and <= 1000) return v3;

                m = Regex.Match(t, @"^(RC|FS|PT|S|T|H|D|WT|FL)(\d{2,4})$");
                if (m.Success && int.TryParse(m.Groups[2].Value, out int v4) && v4 is >= 50 and <= 1000) return v4;

                m = Regex.Match(t, @"^(\d{2,4})(RC|PT|FS)$");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int v5) && v5 is >= 50 and <= 1000) return v5;

                if (Regex.IsMatch(t, @"^\d{3}$") && int.TryParse(t, out int bare) && bare is >= 100 and <= 500)
                    return bare;

                return null;
            }

            using var doc = PdfDocument.Open(filePath);
            var page = doc.GetPage(pageNumber);
            foreach (var word in page.GetWords())
            {
                int? thickness = ParseThickness(word.Text);
                if (!thickness.HasValue)
                    continue;

                var box = word.BoundingBox;
                double xMm = ((box.BottomLeft.X + box.TopRight.X) / 2.0) * scale;
                double yMm = ((box.BottomLeft.Y + box.TopRight.Y) / 2.0) * scale;
                var point = (X: xMm, Y: yMm);

                for (int i = 0; i < geometry.Slabs.Count && i < geometry.SlabColors.Count; i++)
                {
                    if (!PolygonProcessor.PointInPolygon(point, geometry.Slabs[i]))
                        continue;

                    var color = geometry.SlabColors[i];
                    if (!candidatesByColor.TryGetValue(color, out var list))
                    {
                        list = new List<int>();
                        candidatesByColor[color] = list;
                    }
                    list.Add(thickness.Value);
                    break;
                }
            }

            foreach (var (color, values) in candidatesByColor)
            {
                if (values.Count == 0) continue;
                double chosen = values.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
                results[color] = chosen;
            }

            return results;
        }

    }
}
