#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Tokens;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// The takeoff tool's OWN front-end: reads the NATIVE vector content of a structural drawing page —
    /// the exact text (each word with its position) and the line/polygon geometry — straight out of the
    /// PDF, with no rasterization and no OCR. Numbers come out exact, not guessed from pixels.
    ///
    /// PdfToSafe projects this same page read into its own model when it needs page geometry plus
    /// opt-in Bluebeam annotation geometry. The default read remains native page content only.
    /// </summary>
    public static class VectorPageReader
    {
        /// <summary>One extracted word: its text and its bounding box, in PDF points (origin bottom-left).</summary>
        public readonly record struct TextToken(string Text, double Cx, double Cy, double MinX, double MinY, double MaxX, double MaxY)
        {
            public double Width  => MaxX - MinX;
            public double Height => MaxY - MinY;
        }

        /// <summary>One vector subpath: its points and bounding box, in PDF points.</summary>
        public readonly record struct GeomPath(
            IReadOnlyList<(double X, double Y)> Points,
            bool IsClosed, bool IsFilled, bool IsStroked,
            double MinX, double MinY, double MaxX, double MaxY)
        {
            public (byte R, byte G, byte B) Color { get; init; }
            public bool IsAnnotation { get; init; }
            public double LineWidth { get; init; }
            public double Width       => MaxX - MinX;
            public double Height      => MaxY - MinY;
            public double DiagonalLen => Math.Sqrt(Width * Width + Height * Height);
        }

        public sealed record PageContent(
            int PageNumber,
            double WidthPts,
            double HeightPts,
            IReadOnlyList<TextToken> Words,
            IReadOnlyList<GeomPath> Paths);

        /// <summary>One reconstructed line of text: words sharing a baseline, joined left-to-right.</summary>
        public readonly record struct TextLine(string Text, double Y, double MinX, double MaxX);

        /// <summary>
        /// Group the page's words into readable lines (same baseline, ordered left→right, top→bottom).
        /// This is the estimator-readable form — callouts like «10" SLAB», sheet titles, schedule rows —
        /// far better fuel for synthesis than a bag of tokens. Baseline tolerance is ~6pt.
        /// </summary>
        public static IReadOnlyList<TextLine> ReadTextLines(PageContent page)
        {
            ArgumentNullException.ThrowIfNull(page);
            return page.Words
                .GroupBy(w => Math.Round(w.Cy / 6.0))
                .Select(g =>
                {
                    var ordered = g.OrderBy(w => w.Cx).ToList();
                    return new TextLine(
                        string.Join(" ", ordered.Select(w => w.Text)),
                        ordered[0].Cy, ordered.Min(w => w.MinX), ordered.Max(w => w.MaxX));
                })
                .OrderByDescending(l => l.Y)
                .ToList();
        }

        /// <summary>Open the PDF and read one page (1-based).</summary>
        public static PageContent ReadPage(string pdfPath, int pageNumber)
        {
            using var doc = PdfDocument.Open(pdfPath);
            return ReadPage(doc.GetPage(pageNumber));
        }

        public static PageContent ReadPage(
            string pdfPath,
            int pageNumber,
            bool includeAnnotations,
            int curveSegments = 0,
            double? minPointDistance = null,
            double? closeDistance = null)
        {
            using var doc = PdfDocument.Open(pdfPath);
            return ReadPage(doc.GetPage(pageNumber), includeAnnotations, curveSegments, minPointDistance, closeDistance);
        }

        public static PageContent ReadPage(Page page)
            => ReadPage(page, includeAnnotations: false);

        public static PageContent ReadPage(
            Page page,
            bool includeAnnotations,
            int curveSegments = 0,
            double? minPointDistance = null,
            double? closeDistance = null)
        {
            ArgumentNullException.ThrowIfNull(page);

            // ── Text: every word with its bounding box ──────────────────────────
            // CAD-exported drawings place each glyph individually with no inter-word spaces, so the
            // default whitespace word-splitter yields single characters. The nearest-neighbour
            // extractor groups glyphs by proximity + baseline (and handles rotated text), so we get
            // real tokens like "WALL", "LEVEL", "30", "MPa".
            var words = new List<TextToken>();
            foreach (var w in page.GetWords(NearestNeighbourWordExtractor.Instance))
            {
                if (string.IsNullOrWhiteSpace(w.Text)) continue;
                var bb = w.BoundingBox;
                double minX = Math.Min(bb.BottomLeft.X, bb.TopRight.X);
                double minY = Math.Min(bb.BottomLeft.Y, bb.TopRight.Y);
                double maxX = Math.Max(bb.BottomLeft.X, bb.TopRight.X);
                double maxY = Math.Max(bb.BottomLeft.Y, bb.TopRight.Y);
                words.Add(new TextToken(w.Text, (minX + maxX) / 2.0, (minY + maxY) / 2.0, minX, minY, maxX, maxY));
            }

            // ── Geometry: every vector subpath, points + bbox ───────────────────
            var paths = new List<GeomPath>();
            foreach (var pdfPath in page.ExperimentalAccess.Paths)
            {
                bool isFilled  = pdfPath.IsFilled;
                bool isStroked = pdfPath.IsStroked;
                double lineWidth = pdfPath.LineWidth;
                var pathColor = PathToColor(pdfPath);

                foreach (var sub in pdfPath)
                {
                    var pts = new List<(double X, double Y)>();
                    foreach (var cmd in sub.Commands)
                    {
                        switch (cmd)
                        {
                            case PdfSubpath.Move m:
                                AddPoint(pts, m.Location, minPointDistance);
                                break;
                            case PdfSubpath.Line l:
                                AddPoint(pts, l.To, minPointDistance);
                                break;
                            case PdfSubpath.CubicBezierCurve b:
                                if (curveSegments > 0)
                                {
                                    for (int seg = 1; seg <= curveSegments; seg++)
                                    {
                                        double t = (double)seg / curveSegments;
                                        double mt = 1.0 - t;
                                        double x = mt * mt * mt * b.StartPoint.X
                                                 + 3 * mt * mt * t * b.FirstControlPoint.X
                                                 + 3 * mt * t * t * b.SecondControlPoint.X
                                                 + t * t * t * b.EndPoint.X;
                                        double y = mt * mt * mt * b.StartPoint.Y
                                                 + 3 * mt * mt * t * b.FirstControlPoint.Y
                                                 + 3 * mt * t * t * b.SecondControlPoint.Y
                                                 + t * t * t * b.EndPoint.Y;
                                        AddPoint(pts, (x, y), minPointDistance);
                                    }
                                }
                                else
                                {
                                    AddPoint(pts, b.EndPoint, minPointDistance);
                                }
                                break;
                        }
                    }
                    if (pts.Count < 2) continue;

                    bool isClosed = sub.Commands.OfType<PdfSubpath.Close>().Any();
                    // Treat a polyline whose ends nearly meet as closed (common for slab outlines).
                    if (!isClosed && pts.Count >= 3)
                    {
                        if (closeDistance is double cd && Distance(pts[0], pts[^1]) < cd)
                        {
                            pts.RemoveAt(pts.Count - 1);
                            isClosed = true;
                        }
                        else if (closeDistance is null &&
                                 Math.Abs(pts[0].X - pts[^1].X) < 0.5 &&
                                 Math.Abs(pts[0].Y - pts[^1].Y) < 0.5)
                        {
                            isClosed = true;
                        }
                    }

                    paths.Add(ToGeomPath(pts, isClosed, isFilled, isStroked, pathColor, lineWidth, isAnnotation: false));
                }
            }

            if (includeAnnotations)
                paths.AddRange(ReadAnnotationPaths(page));

            return new PageContent(page.Number, page.Width, page.Height, words, paths);
        }

        private static void AddPoint(List<(double X, double Y)> pts, PdfPoint p, double? minPointDistance)
            => AddPoint(pts, (p.X, p.Y), minPointDistance);

        private static void AddPoint(List<(double X, double Y)> pts, (double X, double Y) p, double? minPointDistance)
        {
            if (minPointDistance is double d &&
                pts.Count > 0 &&
                Distance(pts[^1], p) <= d)
                return;

            pts.Add(p);
        }

        private static GeomPath ToGeomPath(
            IReadOnlyList<(double X, double Y)> pts,
            bool isClosed,
            bool isFilled,
            bool isStroked,
            (byte R, byte G, byte B) color,
            double lineWidth,
            bool isAnnotation)
        {
            double minX = pts.Min(p => p.X), minY = pts.Min(p => p.Y);
            double maxX = pts.Max(p => p.X), maxY = pts.Max(p => p.Y);
            return new GeomPath(pts, isClosed, isFilled, isStroked, minX, minY, maxX, maxY)
            {
                Color = color,
                LineWidth = lineWidth,
                IsAnnotation = isAnnotation
            };
        }

        private static IReadOnlyList<GeomPath> ReadAnnotationPaths(Page page)
        {
            var result = new List<GeomPath>();
            var rawPageOrigin = ReadRawPageOrigin(page);

            foreach (var ann in page.ExperimentalAccess.GetAnnotations())
            {
                try
                {
                    var annColor = AnnotationToColor(ann);
                    var dict = ann.AnnotationDictionary;
                    var rect = ann.Rectangle;

                    var vertPts = ReadAnnotVertices(dict, rawPageOrigin);
                    if (vertPts.Count >= 3)
                    {
                        int rotation = 0;
                        if (dict.Data.TryGetValue("Rotation", out var rotToken) && rotToken is NumericToken rotNum)
                            rotation = (int)rotNum.Data;
                        if (rotation != 0)
                        {
                            double cx = (rect.BottomLeft.X + rect.TopRight.X) / 2.0;
                            double cy = (rect.BottomLeft.Y + rect.TopRight.Y) / 2.0;
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
                        result.Add(ToGeomPath(vertPts, isClosed, isClosed, true, annColor, 1, true));
                        continue;
                    }

                    switch (ann.Type)
                    {
                        case AnnotationType.Square:
                        case AnnotationType.Polygon:
                        {
                            var pts = ReadAppearanceGeometry(dict);
                            if (pts.Count < 3)
                            {
                                double x0 = rect.BottomLeft.X, y0 = rect.BottomLeft.Y;
                                double x1 = rect.TopRight.X, y1 = rect.TopRight.Y;
                                pts = new List<(double X, double Y)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };
                            }
                            result.Add(ToGeomPath(pts, true, true, true, annColor, 1, true));
                            break;
                        }
                        case AnnotationType.Circle:
                        {
                            var bboxPts = ReadAppearanceGeometry(dict);
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
                                cx = (rect.BottomLeft.X + rect.TopRight.X) / 2.0;
                                cy = (rect.BottomLeft.Y + rect.TopRight.Y) / 2.0;
                                rr = Math.Max(Math.Abs(rect.TopRight.X - rect.BottomLeft.X),
                                              Math.Abs(rect.TopRight.Y - rect.BottomLeft.Y)) / 2.0;
                            }
                            var pts = new List<(double X, double Y)>();
                            for (int seg = 0; seg < 16; seg++)
                            {
                                double ang = 2 * Math.PI * seg / 16;
                                pts.Add((cx + rr * Math.Cos(ang), cy + rr * Math.Sin(ang)));
                            }
                            result.Add(ToGeomPath(pts, true, true, true, annColor, 1, true));
                            break;
                        }
                        case AnnotationType.PolyLine:
                        {
                            break;
                        }
                        case AnnotationType.Line:
                        {
                            var pts = ReadAnnotLine(dict, rawPageOrigin);
                            if (pts.Count >= 2)
                                result.Add(ToGeomPath(pts, false, false, true, annColor, 1, true));
                            break;
                        }
                        case AnnotationType.Ink:
                        {
                            foreach (var ink in ReadAnnotInkList(dict, rawPageOrigin))
                                if (ink.Count >= 2)
                                    result.Add(ToGeomPath(ink, false, false, true, annColor, 1, true));
                            break;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("VectorPageReader: skipped annotation: " + ex.Message); }
            }

            return result;
        }

        private static (byte R, byte G, byte B) PathToColor(PdfPath path)
        {
            IColor? c = path.IsFilled ? path.FillColor : (path.IsStroked ? path.StrokeColor : null);
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

        private static (byte R, byte G, byte B) AnnotationToColor(Annotation ann)
        {
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

        private readonly record struct RawPageOrigin(double X, double Y)
        {
            public (double X, double Y) ToPageSpace(double rawX, double rawY) =>
                (rawX - X, rawY - Y);
        }

        private static RawPageOrigin ReadRawPageOrigin(Page page)
        {
            if (TryRawBoxOrigin(page.Dictionary, "MediaBox", out var origin) ||
                TryRawBoxOrigin(page.Dictionary, "CropBox", out origin))
            {
                return origin;
            }

            return new RawPageOrigin(0, 0);
        }

        private static bool TryRawBoxOrigin(DictionaryToken dict, string key, out RawPageOrigin origin)
        {
            origin = default;
            if (!dict.Data.TryGetValue(key, out var token) || token is not ArrayToken arr)
                return false;

            var ns = arr.Data.OfType<NumericToken>().Select(n => (double)n.Data).ToList();
            if (ns.Count < 2)
                return false;

            origin = new RawPageOrigin(ns[0], ns[1]);
            return true;
        }

        private static List<(double X, double Y)> ReadAppearanceGeometry(DictionaryToken dict)
        {
            var pts = new List<(double X, double Y)>();
            try
            {
                if (!dict.Data.TryGetValue("AP", out var apToken)) return pts;
                if (apToken is not DictionaryToken apDict) return pts;
                if (!apDict.Data.TryGetValue("N", out var nToken)) return pts;
                if (nToken is not StreamToken stream) return pts;

                var streamDict = stream.StreamDictionary;

                double[] bbox = Array.Empty<double>();
                if (streamDict.Data.TryGetValue("BBox", out var bboxToken) && bboxToken is ArrayToken bboxArr)
                    bbox = bboxArr.Data.OfType<NumericToken>().Select(n => (double)n.Data).ToArray();

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

                var reMatch = Regex.Match(content, @"(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+re");
                if (reMatch.Success)
                {
                    double rx = double.Parse(reMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                    double ry = double.Parse(reMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                    double rw = double.Parse(reMatch.Groups[3].Value, CultureInfo.InvariantCulture);
                    double rh = double.Parse(reMatch.Groups[4].Value, CultureInfo.InvariantCulture);

                    if (bbox.Length >= 4 && bbox[2] - bbox[0] > 0 && bbox[3] - bbox[1] > 0)
                    {
                        double pageX0 = bbox[0] + rx;
                        double pageY0 = bbox[1] + ry;
                        double pageX1 = bbox[0] + rx + rw;
                        double pageY1 = bbox[1] + ry + rh;

                        pts.Add((pageX0, pageY0));
                        pts.Add((pageX1, pageY0));
                        pts.Add((pageX1, pageY1));
                        pts.Add((pageX0, pageY1));
                        return pts;
                    }
                    else
                    {
                        double x0 = rx, y0 = ry;
                        double x1 = rx + rw, y1 = ry + rh;
                        pts.Add((x0, y0));
                        pts.Add((x1, y0));
                        pts.Add((x1, y1));
                        pts.Add((x0, y1));
                        return pts;
                    }
                }

                if (bbox.Length >= 4)
                {
                    double x0 = bbox[0], y0 = bbox[1];
                    double x1 = bbox[2], y1 = bbox[3];
                    if (Math.Abs(x1 - x0) > 1 || Math.Abs(y1 - y0) > 1)
                    {
                        pts.Add((x0, y0));
                        pts.Add((x1, y0));
                        pts.Add((x1, y1));
                        pts.Add((x0, y1));
                    }
                }
            }
            catch (Exception) { }
            return pts;
        }

        private static List<(double X, double Y)> ReadAnnotVertices(
            DictionaryToken dict,
            RawPageOrigin rawPageOrigin = default)
        {
            var pts = new List<(double X, double Y)>();
            if (!dict.Data.TryGetValue("Vertices", out var token)) return pts;

            ArrayToken? arr = token as ArrayToken;
            if (arr is null)
            {
                System.Diagnostics.Trace.TraceInformation(
                    $"ReadAnnotVertices: Vertices token is {token.GetType().Name}, not ArrayToken");
                return pts;
            }

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
                pts.Add(rawPageOrigin.ToPageSpace(numbers[i], numbers[i + 1]));
            return pts;
        }

        private static List<(double X, double Y)> ReadAnnotLine(
            DictionaryToken dict,
            RawPageOrigin rawPageOrigin = default)
        {
            var pts = new List<(double X, double Y)>();
            if (!dict.Data.TryGetValue("L", out var token) || !(token is ArrayToken arr)) return pts;
            var ns = arr.Data.OfType<NumericToken>().Select(n => (double)n.Data).ToList();
            if (ns.Count >= 4)
            {
                pts.Add(rawPageOrigin.ToPageSpace(ns[0], ns[1]));
                pts.Add(rawPageOrigin.ToPageSpace(ns[2], ns[3]));
            }
            return pts;
        }

        private static IEnumerable<List<(double X, double Y)>> ReadAnnotInkList(
            DictionaryToken dict,
            RawPageOrigin rawPageOrigin = default)
        {
            if (!dict.Data.TryGetValue("InkList", out var token) || !(token is ArrayToken outer)) yield break;
            foreach (var inner in outer.Data.OfType<ArrayToken>())
            {
                var pts = new List<(double X, double Y)>();
                var ns = inner.Data.OfType<NumericToken>().Select(n => (double)n.Data).ToList();
                for (int i = 0; i + 1 < ns.Count; i += 2)
                    pts.Add(rawPageOrigin.ToPageSpace(ns[i], ns[i + 1]));
                if (pts.Count >= 2) yield return pts;
            }
        }

        private static double Distance((double X, double Y) a, (double X, double Y) b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
