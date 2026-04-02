#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Tokens;

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
            var rawSubpaths = new List<RawSubpath>();

            foreach (var pdfPath in page.ExperimentalAccess.Paths)
            {
                var pathColor = PathToColor(pdfPath);
                bool isFilled = pdfPath.IsFilled;
                bool isStroked = pdfPath.IsStroked;
                double lineWidth = pdfPath.LineWidth;
                foreach (var subPath in pdfPath)
                {
                    var pts = new List<(double X, double Y)>();

                    foreach (var cmd in subPath.Commands)
                    {
                        PdfPoint? pt = cmd switch
                        {
                            PdfSubpath.Move  m => m.Location,
                            PdfSubpath.Line  l => l.To,
                            _                  => null
                        };

                        if (cmd is PdfSubpath.CubicBezierCurve b)
                        {
                            for (int seg = 1; seg <= PdfToSafeConstants.BezierSegments; seg++)
                            {
                                double t  = (double)seg / PdfToSafeConstants.BezierSegments;
                                double mt = 1.0 - t;
                                double xMm = (mt*mt*mt * b.StartPoint.X
                                            + 3*mt*mt*t * b.FirstControlPoint.X
                                            + 3*mt*t*t  * b.SecondControlPoint.X
                                            + t*t*t     * b.EndPoint.X) * scale;
                                double yMm = (mt*mt*mt * b.StartPoint.Y
                                            + 3*mt*mt*t * b.FirstControlPoint.Y
                                            + 3*mt*t*t  * b.SecondControlPoint.Y
                                            + t*t*t     * b.EndPoint.Y) * scale;
                                if (pts.Count == 0 ||
                                    PolygonProcessor.Distance(pts[^1], (xMm, yMm)) > PdfToSafeConstants.MinVertexDistanceMm)
                                    pts.Add((xMm, yMm));
                            }
                        }
                        else if (pt.HasValue)
                        {
                            double xMm = pt.Value.X * scale;
                            double yMm = pt.Value.Y * scale;
                            if (pts.Count == 0 ||
                                PolygonProcessor.Distance(pts[^1], (xMm, yMm)) > PdfToSafeConstants.MinVertexDistanceMm)
                                pts.Add((xMm, yMm));
                        }
                    }

                    if (pts.Count < 2) continue;

                    bool isClosed = subPath.Commands.OfType<PdfSubpath.Close>().Any();

                    if (!isClosed && pts.Count >= 3 &&
                        PolygonProcessor.Distance(pts[0], pts[^1]) < PdfToSafeConstants.MinVertexDistanceMm * 4)
                    {
                        pts.RemoveAt(pts.Count - 1);
                        isClosed = true;
                    }

                    rawSubpaths.Add(new RawSubpath(pts, isClosed, pathColor, isFilled, isStroked, lineWidth, IsAnnotation: false));
                }
            }

            foreach (var ann in page.ExperimentalAccess.GetAnnotations())
            {
                try
                {
                    var annColor = AnnotationToColor(ann);
                    var dict     = ann.AnnotationDictionary;
                    var rect     = ann.Rectangle;

                    switch (ann.Type)
                    {
                        case AnnotationType.Square:
                        {
                            double x0 = rect.BottomLeft.X * scale, y0 = rect.BottomLeft.Y * scale;
                            double x1 = rect.TopRight.X   * scale, y1 = rect.TopRight.Y   * scale;
                            var pts = new List<(double X, double Y)> { (x0,y0),(x1,y0),(x1,y1),(x0,y1) };
                            rawSubpaths.Add(new RawSubpath(pts, true, annColor, IsFilled: true, IsStroked: true, LineWidth: 1, IsAnnotation: true));
                            break;
                        }
                        case AnnotationType.Circle:
                        {
                            double cx = ((rect.BottomLeft.X + rect.TopRight.X) / 2.0) * scale;
                            double cy = ((rect.BottomLeft.Y + rect.TopRight.Y) / 2.0) * scale;
                            double rr = Math.Max(Math.Abs(rect.TopRight.X - rect.BottomLeft.X),
                                                 Math.Abs(rect.TopRight.Y - rect.BottomLeft.Y)) / 2.0 * scale;
                            var pts = new List<(double X, double Y)>();
                            for (int seg = 0; seg < 16; seg++)
                            {
                                double ang = 2 * Math.PI * seg / 16;
                                pts.Add((cx + rr * Math.Cos(ang), cy + rr * Math.Sin(ang)));
                            }
                            rawSubpaths.Add(new RawSubpath(pts, true, annColor, IsFilled: true, IsStroked: true, LineWidth: 1, IsAnnotation: true));
                            break;
                        }
                        case AnnotationType.Polygon:
                        {
                            var pts = ReadAnnotVertices(dict, scale);
                            if (pts.Count >= 3) rawSubpaths.Add(new RawSubpath(pts, true, annColor, IsFilled: true, IsStroked: true, LineWidth: 1, IsAnnotation: true));
                            break;
                        }
                        case AnnotationType.PolyLine:
                        case AnnotationType.Line:
                        {
                            var pts = ann.Type == AnnotationType.Line ? ReadAnnotLine(dict, scale) : ReadAnnotVertices(dict, scale);
                            if (pts.Count >= 2) rawSubpaths.Add(new RawSubpath(pts, false, annColor, IsFilled: false, IsStroked: true, LineWidth: 1, IsAnnotation: true));
                            break;
                        }
                        case AnnotationType.Ink:
                        {
                            foreach (var ink in ReadAnnotInkList(dict, scale))
                                if (ink.Count >= 2) rawSubpaths.Add(new RawSubpath(ink, false, annColor, IsFilled: false, IsStroked: true, LineWidth: 1, IsAnnotation: true));
                            break;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("PdfGeometryParser: skipped annotation: " + ex.Message); }
            }

            return rawSubpaths;
        }

        public static int? DetectScale(string filePath, int pageNumber = 1)
        {
            var validScales = new HashSet<int> { 20, 25, 33, 50, 75, 100, 125, 150, 200, 250, 500, 1000 };
            try
            {
                using var doc = PdfDocument.Open(filePath);
                var page = doc.GetPage(pageNumber);
                var text = string.Join(" ", page.GetWords().Select(w => w.Text));
                foreach (Match m in Regex.Matches(text, @"1\s*[:/]\s*(\d{2,4})"))
                    if (int.TryParse(m.Groups[1].Value, out int s) && validScales.Contains(s))
                        return s;
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

        internal static (byte R, byte G, byte B) PathToColor(PdfPath path)
        {
            IColor? c = path.IsFilled ? path.FillColor : (path.IsStroked ? path.StrokeColor : null);
            return ToQuantizedRgb(c);
        }

        internal static (byte R, byte G, byte B) ToQuantizedRgb(IColor? color)
        {
            double r = 0, g = 0, b = 0;
            if      (color is RGBColor rgb)   { r = rgb.R; g = rgb.G; b = rgb.B; }
            else if (color is CMYKColor cmyk) { var t = cmyk.ToRGBValues(); r = t.Item1; g = t.Item2; b = t.Item3; }
            else if (color is GrayColor gray) { r = g = b = gray.Gray; }
            return ((byte)((int)(r * 255) & 0xF0),
                    (byte)((int)(g * 255) & 0xF0),
                    (byte)((int)(b * 255) & 0xF0));
        }

        internal static (byte R, byte G, byte B) AnnotationToColor(Annotation ann)
        {
            if (ann.AnnotationDictionary.Data.TryGetValue("C", out var token) && token is ArrayToken arr)
            {
                var ns = arr.Data.OfType<NumericToken>().Select(n => (double)n.Data).ToList();
                double r = 0, g = 0, b = 0;
                if      (ns.Count == 1) { r = g = b = ns[0]; }
                else if (ns.Count == 3) { r = ns[0]; g = ns[1]; b = ns[2]; }
                else if (ns.Count == 4) { double k = ns[3]; r = (1-ns[0])*(1-k); g = (1-ns[1])*(1-k); b = (1-ns[2])*(1-k); }
                return ((byte)((int)(r * 255) & 0xF0),
                        (byte)((int)(g * 255) & 0xF0),
                        (byte)((int)(b * 255) & 0xF0));
            }
            return (0, 0, 0);
        }

        internal static List<(double X, double Y)> ReadAnnotVertices(DictionaryToken dict, double scale)
        {
            var pts = new List<(double X, double Y)>();
            if (!dict.Data.TryGetValue("Vertices", out var token) || !(token is ArrayToken arr)) return pts;
            var ns = arr.Data.OfType<NumericToken>().Select(n => (double)n.Data).ToList();
            for (int i = 0; i + 1 < ns.Count; i += 2)
                pts.Add((ns[i] * scale, ns[i + 1] * scale));
            return pts;
        }

        internal static List<(double X, double Y)> ReadAnnotLine(DictionaryToken dict, double scale)
        {
            var pts = new List<(double X, double Y)>();
            if (!dict.Data.TryGetValue("L", out var token) || !(token is ArrayToken arr)) return pts;
            var ns = arr.Data.OfType<NumericToken>().Select(n => (double)n.Data).ToList();
            if (ns.Count >= 4) { pts.Add((ns[0] * scale, ns[1] * scale)); pts.Add((ns[2] * scale, ns[3] * scale)); }
            return pts;
        }

        internal static IEnumerable<List<(double X, double Y)>> ReadAnnotInkList(DictionaryToken dict, double scale)
        {
            if (!dict.Data.TryGetValue("InkList", out var token) || !(token is ArrayToken outer)) yield break;
            foreach (var inner in outer.Data.OfType<ArrayToken>())
            {
                var pts = new List<(double X, double Y)>();
                var ns = inner.Data.OfType<NumericToken>().Select(n => (double)n.Data).ToList();
                for (int i = 0; i + 1 < ns.Count; i += 2) pts.Add((ns[i] * scale, ns[i + 1] * scale));
                if (pts.Count >= 2) yield return pts;
            }
        }
    }
}
