#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

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
        public double PageWidthPts  { get; set; }
        public double PageHeightPts { get; set; }
        public int    ScaleDenominator { get; set; }
        public int  PageCount    { get; set; }
        public int  RawPathCount { get; set; }
        public bool IsVectorPdf  { get; set; }
    }

    internal static class PdfGeometryExtractor
    {
        // PDF default unit is 1/72 inch. Convert to mm at the given scale.
        private const double PointsToMm = 25.4 / 72.0;

        private const double MinVertexDistanceMm  = 0.5;      // deduplicate near-identical points

        public static ExtractedGeometry Extract(
            string filePath,
            int    scaleDenominator,
            int    pageNumber          = 1,
            double slabMinDiagonalMm  = 1000.0,
            double lineMinLengthMm    = 200.0,
            bool   excludeGridLines   = false)
        {
            var result = new ExtractedGeometry();
            result.ScaleDenominator = scaleDenominator;
            double scale = scaleDenominator * PointsToMm;

            using var doc = PdfDocument.Open(filePath);
            var page = doc.GetPage(pageNumber);
            result.PageWidthPts  = page.Width;
            result.PageHeightPts = page.Height;
            result.PageCount = doc.NumberOfPages;

            // Collect all raw point lists (one per subpath) with closed flag
            var rawSubpaths = new List<(List<(double X, double Y)> Points, bool IsClosed)>();

            foreach (var pdfPath in page.ExperimentalAccess.Paths)
            {
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
                            for (int seg = 1; seg <= 8; seg++)
                            {
                                double t  = (double)seg / 8.0;
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
                                    Distance(pts[^1], (xMm, yMm)) > MinVertexDistanceMm)
                                    pts.Add((xMm, yMm));
                            }
                        }
                        else if (pt.HasValue)
                        {
                            double xMm = pt.Value.X * scale;
                            double yMm = pt.Value.Y * scale;
                            if (pts.Count == 0 ||
                                Distance(pts[^1], (xMm, yMm)) > MinVertexDistanceMm)
                                pts.Add((xMm, yMm));
                        }
                    }

                    if (pts.Count < 2) continue;

                    bool isClosed = subPath.Commands.OfType<PdfSubpath.Close>().Any();

                    // Also treat as closed if first and last points are near-identical
                    if (!isClosed && pts.Count >= 3 &&
                        Distance(pts[0], pts[^1]) < MinVertexDistanceMm * 4)
                    {
                        pts.RemoveAt(pts.Count - 1);
                        isClosed = true;
                    }

                    rawSubpaths.Add((pts, isClosed));
                }
            }

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
            foreach (var (pts, isClosed) in rawSubpaths)
            {
                if (isClosed)
                {
                    double diag = BoundingBoxDiagonal(pts);
                    if (diag >= slabMinDiagonalMm)
                        result.Slabs.Add(pts);
                    else
                        result.Columns.Add(Centroid(pts));
                }
                else
                {
                    double len = PathLength(pts);
                    if (len >= lineMinLengthMm)
                    {
                        if (excludeGridLines && pts.Count == 2 && len > gridThreshMm)
                            continue;
                        result.Lines.Add(pts);
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

        public static void ExportDxf(
            ExtractedGeometry geometry,
            string            outputPath,
            HashSet<int>?     excludedSlabs   = null,
            HashSet<int>?     excludedLines   = null,
            HashSet<int>?     excludedColumns = null)
        {
            var dxf = new DxfDocument(DxfVersion.AutoCad2007);

            var slabLayer   = new Layer("SLAB-OUTLINE") { Color = AciColor.Green };
            var linesLayer  = new Layer("LINES")        { Color = AciColor.Cyan };
            var colLayer    = new Layer("COLUMNS")      { Color = AciColor.Yellow };

            dxf.Layers.Add(slabLayer);
            dxf.Layers.Add(linesLayer);
            dxf.Layers.Add(colLayer);

            // Declare mm units so SAFE's import dialog defaults correctly
            dxf.DrawingVariables.InsUnits = netDxf.Units.DrawingUnits.Millimeters;

            // Center geometry near origin (SAFE requirement)
            // Weight centroid by path length so large slab outlines dominate,
            // not dense clusters of short annotation ticks
            double totalWeight = 0.0, sumX = 0.0, sumY = 0.0;

            foreach (var pts in geometry.Slabs)
            {
                double w = PathLength(pts);
                var (pcx, pcy) = Centroid(pts);
                sumX += pcx * w; sumY += pcy * w; totalWeight += w;
            }
            foreach (var (x, y) in geometry.Columns)
            {
                sumX += x; sumY += y; totalWeight += 1.0;
            }
            foreach (var pts in geometry.Lines)
            {
                double w = PathLength(pts);
                var (pcx, pcy) = Centroid(pts);
                sumX += pcx * w; sumY += pcy * w; totalWeight += w;
            }

            if (totalWeight == 0.0) return;
            double cx = sumX / totalWeight;
            double cy = sumY / totalWeight;

            System.Collections.Generic.List<(double X, double Y)> Center(
                System.Collections.Generic.List<(double X, double Y)> pts) =>
                pts.Select(p => (p.X - cx, p.Y - cy)).ToList();

            for (int i = 0; i < geometry.Slabs.Count; i++)
            {
                if (excludedSlabs?.Contains(i) == true) continue;
                var verts = Center(geometry.Slabs[i]).Select(p => new LwPolylineVertex(p.X, p.Y)).ToList();
                var poly = new LwPolyline(verts, true) { Layer = slabLayer };
                dxf.LwPolylines.Add(poly);
            }

            for (int i = 0; i < geometry.Columns.Count; i++)
            {
                if (excludedColumns?.Contains(i) == true) continue;
                var (x, y) = geometry.Columns[i];
                var pt = new Point(x - cx, y - cy, 0) { Layer = colLayer };
                dxf.Points.Add(pt);
            }

            for (int i = 0; i < geometry.Lines.Count; i++)
            {
                if (excludedLines?.Contains(i) == true) continue;
                var verts = Center(geometry.Lines[i]).Select(p => new LwPolylineVertex(p.X, p.Y)).ToList();
                var poly = new LwPolyline(verts, false) { Layer = linesLayer };
                dxf.LwPolylines.Add(poly);
            }

            dxf.Save(outputPath);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static double Distance((double X, double Y) a, (double X, double Y) b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double PathLength(List<(double X, double Y)> pts)
        {
            double len = 0;
            for (int i = 1; i < pts.Count; i++) len += Distance(pts[i - 1], pts[i]);
            return len;
        }

        private static double BoundingBoxDiagonal(List<(double X, double Y)> pts)
        {
            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            double dx = maxX - minX, dy = maxY - minY;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static (double X, double Y) Centroid(List<(double X, double Y)> pts)
            => (pts.Average(p => p.X), pts.Average(p => p.Y));
    }
}
