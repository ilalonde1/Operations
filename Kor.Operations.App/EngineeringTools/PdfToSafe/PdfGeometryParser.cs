#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Globalization;
using System.IO.Compression;
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

                    // ── Universal Vertices check ────────────────────────
                    // Bluebeam annotations are /Subtype/Polygon with /Vertices
                    // but PdfPig may classify them as Square, Circle, etc.
                    // Always check for /Vertices first regardless of type.
                    var vertPts = ReadAnnotVertices(dict, scale);
                    if (vertPts.Count >= 3)
                    {
                        // Apply /Rotation if present (Bluebeam wall elements use Rotation=90).
                        // Vertices are stored un-rotated; rotate around the Rect center.
                        int rotation = 0;
                        if (dict.Data.TryGetValue("Rotation", out var rotToken) && rotToken is NumericToken rotNum)
                            rotation = (int)rotNum.Data;
                        if (rotation != 0)
                        {
                            double cx = ((rect.BottomLeft.X + rect.TopRight.X) / 2.0) * scale;
                            double cy = ((rect.BottomLeft.Y + rect.TopRight.Y) / 2.0) * scale;
                            double rad = rotation * Math.PI / 180.0;
                            double cos = Math.Cos(rad), sin = Math.Sin(rad);
                            vertPts = vertPts.Select(p =>
                            {
                                double dx = p.X - cx, dy = p.Y - cy;
                                return (cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
                            }).ToList();
                        }

                        bool isClosed = ann.Type != AnnotationType.PolyLine &&
                                        ann.Type != AnnotationType.Line;
                        rawSubpaths.Add(new RawSubpath(vertPts, isClosed, annColor,
                            IsFilled: isClosed, IsStroked: true, LineWidth: 1, IsAnnotation: true));
                        continue;
                    }

                    // ── Type-specific fallbacks ─────────────────────────
                    switch (ann.Type)
                    {
                        case AnnotationType.Square:
                        case AnnotationType.Polygon:
                        {
                            var pts = ReadAppearanceGeometry(dict, scale);
                            if (pts.Count < 3)
                            {
                                double x0 = rect.BottomLeft.X * scale, y0 = rect.BottomLeft.Y * scale;
                                double x1 = rect.TopRight.X   * scale, y1 = rect.TopRight.Y   * scale;
                                pts = new List<(double X, double Y)> { (x0,y0),(x1,y0),(x1,y1),(x0,y1) };
                            }
                            rawSubpaths.Add(new RawSubpath(pts, true, annColor, IsFilled: true, IsStroked: true, LineWidth: 1, IsAnnotation: true));
                            break;
                        }
                        case AnnotationType.Circle:
                        {
                            var bboxPts = ReadAppearanceGeometry(dict, scale);
                            double cx, cy, rr;
                            if (bboxPts.Count >= 4)
                            {
                                double bx0 = bboxPts[0].X, by0 = bboxPts[0].Y;
                                double bx1 = bboxPts[2].X, by1 = bboxPts[2].Y;
                                cx = (bx0 + bx1) / 2.0;
                                cy = (by0 + by1) / 2.0;
                                rr = Math.Max(Math.Abs(bx1 - bx0), Math.Abs(by1 - by0)) / 2.0;
                            }
                            else
                            {
                                cx = ((rect.BottomLeft.X + rect.TopRight.X) / 2.0) * scale;
                                cy = ((rect.BottomLeft.Y + rect.TopRight.Y) / 2.0) * scale;
                                rr = Math.Max(Math.Abs(rect.TopRight.X - rect.BottomLeft.X),
                                              Math.Abs(rect.TopRight.Y - rect.BottomLeft.Y)) / 2.0 * scale;
                            }
                            var pts = new List<(double X, double Y)>();
                            for (int seg = 0; seg < 16; seg++)
                            {
                                double ang = 2 * Math.PI * seg / 16;
                                pts.Add((cx + rr * Math.Cos(ang), cy + rr * Math.Sin(ang)));
                            }
                            rawSubpaths.Add(new RawSubpath(pts, true, annColor, IsFilled: true, IsStroked: true, LineWidth: 1, IsAnnotation: true));
                            break;
                        }
                        case AnnotationType.PolyLine:
                        {
                            // Vertices already handled above; this is fallback
                            break;
                        }
                        case AnnotationType.Line:
                        {
                            var pts = ReadAnnotLine(dict, scale);
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
            // Prefer /IC (interior/fill color) for filled shapes — this is what
            // the user sees. Fall back to /C (border/stroke color).
            foreach (var key in new[] { "IC", "C" })
            {
                if (ann.AnnotationDictionary.Data.TryGetValue(key, out var token) && token is ArrayToken arr)
                {
                    var ns = arr.Data.OfType<NumericToken>().Select(n => (double)n.Data).ToList();
                    if (ns.Count == 0) continue;
                    double r = 0, g = 0, b = 0;
                    if      (ns.Count == 1) { r = g = b = ns[0]; }
                    else if (ns.Count == 3) { r = ns[0]; g = ns[1]; b = ns[2]; }
                    else if (ns.Count == 4) { double k = ns[3]; r = (1-ns[0])*(1-k); g = (1-ns[1])*(1-k); b = (1-ns[2])*(1-k); }
                    return ((byte)((int)(r * 255) & 0xF0),
                            (byte)((int)(g * 255) & 0xF0),
                            (byte)((int)(b * 255) & 0xF0));
                }
            }
            return (0, 0, 0);
        }

        /// <summary>
        /// Extracts the actual drawn geometry from the annotation's normal appearance
        /// stream (/AP /N). Parses the PDF content stream for drawing operators:
        ///   - 're' (rectangle): x y w h re
        ///   - 'm'/'l' (move/line): path commands
        /// This is the definitive source — it's exactly what the PDF viewer renders.
        /// Falls back to BBox if stream parsing fails.
        /// </summary>
        internal static List<(double X, double Y)> ReadAppearanceGeometry(DictionaryToken dict, double scale)
        {
            var pts = new List<(double X, double Y)>();
            try
            {
                if (!dict.Data.TryGetValue("AP", out var apToken)) return pts;
                if (apToken is not DictionaryToken apDict) return pts;
                if (!apDict.Data.TryGetValue("N", out var nToken)) return pts;
                if (nToken is not StreamToken stream) return pts;

                var streamDict = stream.StreamDictionary;

                // Read the BBox (defines the stream's coordinate space)
                double[] bbox = Array.Empty<double>();
                if (streamDict.Data.TryGetValue("BBox", out var bboxToken) && bboxToken is ArrayToken bboxArr)
                    bbox = bboxArr.Data.OfType<NumericToken>().Select(n => (double)n.Data).ToArray();

                // Decode the stream content
                byte[] rawData = stream.Data.ToArray();
                string? filterName = null;
                if (streamDict.Data.TryGetValue("Filter", out var filterToken))
                {
                    filterName = filterToken switch
                    {
                        NameToken n => n.Data,
                        ArrayToken a => a.Data.OfType<NameToken>().FirstOrDefault()?.Data,
                        _ => null
                    };
                }

                byte[] decoded;
                if (string.Equals(filterName, "FlateDecode", StringComparison.OrdinalIgnoreCase))
                {
                    using var ms = new MemoryStream(rawData);
                    // Skip zlib header (2 bytes) then inflate
                    ms.ReadByte(); ms.ReadByte();
                    using var ds = new DeflateStream(ms, CompressionMode.Decompress);
                    using var result = new MemoryStream();
                    ds.CopyTo(result);
                    decoded = result.ToArray();
                }
                else
                {
                    decoded = rawData;
                }

                string content = System.Text.Encoding.ASCII.GetString(decoded);

                // Look for 're' (rectangle) command: x y w h re
                var reMatch = Regex.Match(content, @"(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+re");
                if (reMatch.Success)
                {
                    double rx = double.Parse(reMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                    double ry = double.Parse(reMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                    double rw = double.Parse(reMatch.Groups[3].Value, CultureInfo.InvariantCulture);
                    double rh = double.Parse(reMatch.Groups[4].Value, CultureInfo.InvariantCulture);

                    // The re coordinates are in the stream's BBox space.
                    // Map from BBox space to page space using the BBox and Rect.
                    if (bbox.Length >= 4 && bbox[2] - bbox[0] > 0 && bbox[3] - bbox[1] > 0)
                    {
                        // BBox defines the stream coordinate space.
                        // Map stream coords → page coords via BBox.
                        // If BBox origin matches stream content, BBox IS page coords.
                        double bboxW = bbox[2] - bbox[0];
                        double bboxH = bbox[3] - bbox[1];

                        // Transform: pageX = bbox[0] + (streamX / bboxLocalW) * bboxW
                        // But if BBox = [pageX0, pageY0, pageX1, pageY1], stream coords
                        // are relative to BBox origin.
                        double pageX0 = (bbox[0] + rx) * scale;
                        double pageY0 = (bbox[1] + ry) * scale;
                        double pageX1 = (bbox[0] + rx + rw) * scale;
                        double pageY1 = (bbox[1] + ry + rh) * scale;

                        pts.Add((pageX0, pageY0));
                        pts.Add((pageX1, pageY0));
                        pts.Add((pageX1, pageY1));
                        pts.Add((pageX0, pageY1));
                        return pts;
                    }
                    else
                    {
                        // No BBox — assume stream coords are page coords
                        double x0 = rx * scale, y0 = ry * scale;
                        double x1 = (rx + rw) * scale, y1 = (ry + rh) * scale;
                        pts.Add((x0, y0));
                        pts.Add((x1, y0));
                        pts.Add((x1, y1));
                        pts.Add((x0, y1));
                        return pts;
                    }
                }

                // No 're' found — try BBox directly as page coordinates
                if (bbox.Length >= 4)
                {
                    double x0 = bbox[0] * scale, y0 = bbox[1] * scale;
                    double x1 = bbox[2] * scale, y1 = bbox[3] * scale;
                    if (Math.Abs(x1 - x0) > 1 || Math.Abs(y1 - y0) > 1)
                    {
                        pts.Add((x0, y0));
                        pts.Add((x1, y0));
                        pts.Add((x1, y1));
                        pts.Add((x0, y1));
                    }
                }
            }
            catch (Exception) { /* non-standard path data — fall back to Rect */ }
            return pts;
        }

        internal static List<(double X, double Y)> ReadAnnotVertices(DictionaryToken dict, double scale)
        {
            var pts = new List<(double X, double Y)>();
            if (!dict.Data.TryGetValue("Vertices", out var token)) return pts;

            // Handle both direct ArrayToken and indirect references
            ArrayToken? arr = token as ArrayToken;
            if (arr is null)
            {
                // PdfPig may wrap the array in other token types — try to extract numbers
                // from whatever token structure is present
                System.Diagnostics.Trace.TraceInformation(
                    $"ReadAnnotVertices: Vertices token is {token.GetType().Name}, not ArrayToken");
                return pts;
            }

            // Extract numbers — handle both NumericToken and other numeric representations
            var numbers = new List<double>();
            foreach (var item in arr.Data)
            {
                if (item is NumericToken num)
                    numbers.Add((double)num.Data);
                else if (item is IndirectReferenceToken)
                    System.Diagnostics.Trace.TraceInformation(
                        $"ReadAnnotVertices: array item is IndirectReferenceToken");
            }

            for (int i = 0; i + 1 < numbers.Count; i += 2)
                pts.Add((numbers[i] * scale, numbers[i + 1] * scale));
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
