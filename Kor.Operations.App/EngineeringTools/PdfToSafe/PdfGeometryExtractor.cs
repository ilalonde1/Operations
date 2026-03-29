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
    internal sealed class ExtractedGeometry
    {
        // Each slab: ordered list of (X,Y) in mm, ready for a closed polyline
        public List<List<(double X, double Y)>> Slabs { get; } = new();
        // Each column: centroid (X,Y) in mm
        public List<(double X, double Y)> Columns { get; } = new();
        // Each line element: list of (X,Y) in mm (open polyline)
        public List<List<(double X, double Y)>> Lines { get; } = new();
        public List<(byte R, byte G, byte B)> SlabColors   { get; } = new();
        public List<(byte R, byte G, byte B)> ColumnColors { get; } = new();
        public List<(byte R, byte G, byte B)> LineColors   { get; } = new();
        public double PageWidthPts  { get; set; }
        public double PageHeightPts { get; set; }
        public int    ScaleDenominator { get; set; }
        public int  PageCount    { get; set; }
        public int  RawPathCount { get; set; }
        public bool IsVectorPdf  { get; set; }
    }

    internal sealed class SlabColorSettings
    {
        public double ThicknessMm { get; set; } = PdfToSafeConstants.DefaultThicknessMm;
        public double SdlKPa      { get; set; } = 0.0;
        public double LiveKPa     { get; set; } = 0.0;
        public string GradeCode   { get; set; } = PdfToSafeConstants.DefaultGradeCode;
    }

    internal static class PdfGeometryExtractor
    {
        public static ExtractedGeometry Extract(
            string filePath,
            int    scaleDenominator,
            int    pageNumber          = 1,
            double slabMinDiagonalMm  = PdfToSafeConstants.DefaultSlabMinDiagonalMm,
            double lineMinLengthMm    = PdfToSafeConstants.DefaultLineMinLengthMm,
            bool   excludeGridLines   = false)
        {
            var result = new ExtractedGeometry();
            result.ScaleDenominator = scaleDenominator;
            double scale = scaleDenominator * PdfToSafeConstants.PointsToMm;

            using var doc = PdfDocument.Open(filePath);
            var page = doc.GetPage(pageNumber);
            result.PageWidthPts  = page.Width;
            result.PageHeightPts = page.Height;
            result.PageCount = doc.NumberOfPages;

            // Collect all raw point lists (one per subpath) with closed flag
            var rawSubpaths = new List<(List<(double X, double Y)> Points, bool IsClosed, (byte R, byte G, byte B) Color)>();

            foreach (var pdfPath in page.ExperimentalAccess.Paths)
            {
                var pathColor = PathToColor(pdfPath);
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
                            // Tessellate cubic Bezier into 8 segments
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

                    // Also treat as closed if first and last points are near-identical
                    if (!isClosed && pts.Count >= 3 &&
                        PolygonProcessor.Distance(pts[0], pts[^1]) < PdfToSafeConstants.MinVertexDistanceMm * 4)
                    {
                        pts.RemoveAt(pts.Count - 1);
                        isClosed = true;
                    }

                    rawSubpaths.Add((pts, isClosed, pathColor));
                }
            }

            // Also extract PDF annotation geometry (Bluebeam / Acrobat markup shapes)
            try
            {
                foreach (var ann in page.ExperimentalAccess.GetAnnotations())
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
                            var pts = new List<(double X, double Y)>
                                { (x0,y0),(x1,y0),(x1,y1),(x0,y1) };
                            rawSubpaths.Add((pts, true, annColor));
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
                            rawSubpaths.Add((pts, true, annColor));
                            break;
                        }
                        case AnnotationType.Polygon:
                        {
                            var pts = ReadAnnotVertices(dict, scale);
                            if (pts.Count >= 3) rawSubpaths.Add((pts, true, annColor));
                            break;
                        }
                        case AnnotationType.PolyLine:
                        case AnnotationType.Line:
                        {
                            var pts = ann.Type == AnnotationType.Line
                                ? ReadAnnotLine(dict, scale)
                                : ReadAnnotVertices(dict, scale);
                            if (pts.Count >= 2) rawSubpaths.Add((pts, false, annColor));
                            break;
                        }
                        case AnnotationType.Ink:
                        {
                            foreach (var ink in ReadAnnotInkList(dict, scale))
                                if (ink.Count >= 2) rawSubpaths.Add((ink, false, annColor));
                            break;
                        }
                    }
                }
            }
            catch { /* annotation extraction is best-effort */ }

            result.RawPathCount = rawSubpaths.Count;

            // Improved vector detection: require paths with real geometry,
            // not just annotation ticks or a handful of border lines
            int meaningfulCount = rawSubpaths.Count(s =>
                s.Points.Count > 3 ||
                (s.IsClosed && BoundingBoxDiagonal(s.Points) > 10.0));
            result.IsVectorPdf = meaningfulCount >= 5;

            if (rawSubpaths.Count == 0)
                return result;

            double pageWidthMm  = result.PageWidthPts  * scale;
            double pageHeightMm = result.PageHeightPts * scale;
            double gridThreshMm = Math.Max(pageWidthMm, pageHeightMm) * 0.6;

            // Classify
            foreach (var (pts, isClosed, color) in rawSubpaths)
            {
                if (isClosed)
                {
                    double diag = BoundingBoxDiagonal(pts);
                    if (diag >= slabMinDiagonalMm) { result.Slabs.Add(pts);                           result.SlabColors.Add(color);   }
                    else                           { result.Columns.Add(PolygonProcessor.Centroid(pts)); result.ColumnColors.Add(color); }
                }
                else
                {
                    double len = PolygonProcessor.PathLength(pts);
                    if (len >= lineMinLengthMm)
                    {
                        if (excludeGridLines && pts.Count == 2 && len > gridThreshMm)
                            continue;
                        result.Lines.Add(pts); result.LineColors.Add(color);
                    }
                }
            }

            return result;
        }

        public static int? DetectScale(string filePath, int pageNumber = 1)
        {
            var validScales = new HashSet<int>
                { 20, 25, 33, 50, 75, 100, 125, 150, 200, 250, 500, 1000 };
            try
            {
                using var doc = PdfDocument.Open(filePath);
                var page = doc.GetPage(pageNumber);
                var text = string.Join(" ", page.GetWords().Select(w => w.Text));
                foreach (Match m in Regex.Matches(text, @"1\s*[:\s]\s*(\d{2,4})"))
                    if (int.TryParse(m.Groups[1].Value, out int s) && validScales.Contains(s))
                        return s;
            }
            catch { }
            return null;
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
                double chosen = values
                    .GroupBy(v => v)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key)
                    .First()
                    .Key;
                results[color] = chosen;
            }

            return results;
        }

        public static void ExportDxf(
            ExtractedGeometry geometry,
            string            outputPath,
            HashSet<int>?     excludedSlabs   = null,
            HashSet<int>?     excludedLines   = null,
            HashSet<int>?     excludedColumns = null,
            HashSet<(byte R, byte G, byte B)>? excludedColors = null)
            => DxfExporter.Export(geometry, outputPath, excludedSlabs, excludedLines, excludedColumns, excludedColors);

        // ── F2K export (SAFE native ASCII format) ────────────────────────────
        // File → Import → SAFE v12.x in SAFE v23 reads this directly and creates
        // actual slab / beam / column structural objects — no DXF needed.
        // Coordinates are output in millimetres; ensure SAFE model units are N-mm
        // (or scale manually after import using Edit → Scale Objects).
        public static void ExportF2k(
            ExtractedGeometry geometry,
            string            outputPath,
            HashSet<int>?     excludedSlabs   = null,
            HashSet<int>?     excludedLines   = null,
            HashSet<int>?     excludedColumns = null,
            HashSet<(byte R, byte G, byte B)>? excludedColors = null,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings = null,
            string? loadCombCode = null)
            => SafeF2kExporter.Export(geometry, outputPath, excludedSlabs, excludedLines, excludedColumns, excludedColors, colorSettings, loadCombCode);
        public static void ExportF2k(
            string outputPath,
            IReadOnlyList<(ExtractedGeometry Geom, string StoryName, double ElevationMm)> stories,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings = null,
            string? loadCombCode = null)
            => SafeF2kExporter.Export(outputPath, stories, colorSettings, loadCombCode);
        public static void ExportE2k(
            string outputPath,
            ExtractedGeometry geom,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings = null)
            => SafeE2kExporter.Export(outputPath, geom, colorSettings);

        public static void ExportE2k(
            string outputPath,
            IReadOnlyList<(ExtractedGeometry Geom, string StoryName, double ElevationMm)> stories,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings = null)
            => SafeE2kExporter.Export(outputPath, stories, colorSettings);

        // ── helpers ──────────────────────────────────────────────────────────

        private static double BoundingBoxDiagonal(List<(double X, double Y)> pts)
        {
            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            double dx = maxX - minX, dy = maxY - minY;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static (byte R, byte G, byte B) AnnotationToColor(Annotation ann)
        {
            // /C array: [gray], [r g b], or [c m y k] (0-1 values)
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

        private static List<(double X, double Y)> ReadAnnotVertices(DictionaryToken dict, double scale)
        {
            var pts = new List<(double X, double Y)>();
            if (!dict.Data.TryGetValue("Vertices", out var token) || !(token is ArrayToken arr)) return pts;
            var ns = arr.Data.OfType<NumericToken>().Select(n => (double)n.Data).ToList();
            for (int i = 0; i + 1 < ns.Count; i += 2)
                pts.Add((ns[i] * scale, ns[i + 1] * scale));
            return pts;
        }

        private static List<(double X, double Y)> ReadAnnotLine(DictionaryToken dict, double scale)
        {
            var pts = new List<(double X, double Y)>();
            if (!dict.Data.TryGetValue("L", out var token) || !(token is ArrayToken arr)) return pts;
            var ns = arr.Data.OfType<NumericToken>().Select(n => (double)n.Data).ToList();
            if (ns.Count >= 4) { pts.Add((ns[0]*scale, ns[1]*scale)); pts.Add((ns[2]*scale, ns[3]*scale)); }
            return pts;
        }

        private static IEnumerable<List<(double X, double Y)>> ReadAnnotInkList(DictionaryToken dict, double scale)
        {
            if (!dict.Data.TryGetValue("InkList", out var token) || !(token is ArrayToken outer)) yield break;
            foreach (var inner in outer.Data.OfType<ArrayToken>())
            {
                var pts = new List<(double X, double Y)>();
                var ns = inner.Data.OfType<NumericToken>().Select(n => (double)n.Data).ToList();
                for (int i = 0; i + 1 < ns.Count; i += 2) pts.Add((ns[i]*scale, ns[i+1]*scale));
                if (pts.Count >= 2) yield return pts;
            }
        }

        private static (byte R, byte G, byte B) PathToColor(PdfPath path)
        {
            IColor? c = path.IsStroked ? path.StrokeColor
                                       : (path.IsFilled ? path.FillColor : null);
            return ToQuantizedRgb(c);
        }

        private static (byte R, byte G, byte B) ToQuantizedRgb(IColor? color)
        {
            double r = 0, g = 0, b = 0;
            if      (color is RGBColor rgb)   { r = rgb.R; g = rgb.G; b = rgb.B; }
            else if (color is CMYKColor cmyk) { var t = cmyk.ToRGBValues(); r = t.Item1; g = t.Item2; b = t.Item3; }
            else if (color is GrayColor gray) { r = g = b = gray.Gray; }
            return ((byte)((int)(r * 255) & 0xF0),
                    (byte)((int)(g * 255) & 0xF0),
                    (byte)((int)(b * 255) & 0xF0));
        }

    }
}

