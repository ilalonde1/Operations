#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using netDxf;
using netDxf.Entities;
using netDxf.Tables;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Geometry;

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
    }

    internal static class PdfGeometryExtractor
    {
        // PDF default unit is 1/72 inch. Convert to mm at the given scale.
        private const double PointsToMm = 25.4 / 72.0;

        // Classification thresholds (real-world mm after scale conversion)
        private const double SlabMinDiagonalMm   = 1000.0;   // closed path > 1 m diagonal → slab
        private const double LineMinLengthMm      = 200.0;    // open path > 200 mm → keep
        private const double MinVertexDistanceMm  = 0.5;      // deduplicate near-identical points

        public static ExtractedGeometry Extract(string filePath, int scaleDenominator)
        {
            var result = new ExtractedGeometry();
            double scale = scaleDenominator * PointsToMm;

            using var doc = PdfDocument.Open(filePath);
            var page = doc.GetPage(1);
            double pageHeightPts = page.Height;

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
                            Move  m => m.Location,
                            Line  l => l.To,
                            _       => null
                        };

                        // For BezierCurve take only the endpoint
                        if (cmd is BezierCurve b)
                            pt = b.EndPoint;

                        if (pt.HasValue)
                        {
                            double xMm = pt.Value.X * scale;
                            double yMm = pt.Value.Y * scale;

                            // Skip near-duplicate points
                            if (pts.Count == 0 ||
                                Distance(pts[^1], (xMm, yMm)) > MinVertexDistanceMm)
                            {
                                pts.Add((xMm, yMm));
                            }
                        }
                    }

                    if (pts.Count < 2) continue;

                    bool isClosed = subPath.Commands.OfType<Close>().Any();

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

            if (rawSubpaths.Count == 0)
                return result;

            // Center geometry near origin using the bounding box centroid
            double allX = rawSubpaths.SelectMany(s => s.Points).Average(p => p.X);
            double allY = rawSubpaths.SelectMany(s => s.Points).Average(p => p.Y);

            var centered = rawSubpaths.Select(s =>
            (
                Points: s.Points.Select(p => (X: p.X - allX, Y: p.Y - allY)).ToList(),
                s.IsClosed
            )).ToList();

            // Classify
            foreach (var (pts, isClosed) in centered)
            {
                if (isClosed)
                {
                    double diag = BoundingBoxDiagonal(pts);
                    if (diag >= SlabMinDiagonalMm)
                        result.Slabs.Add(pts);
                    else
                        result.Columns.Add(Centroid(pts));
                }
                else
                {
                    double len = PathLength(pts);
                    if (len >= LineMinLengthMm)
                        result.Lines.Add(pts);
                }
            }

            return result;
        }

        public static void ExportDxf(ExtractedGeometry geometry, string outputPath)
        {
            var dxf = new DxfDocument(DxfVersion.AutoCad2007);

            var slabLayer   = new Layer("SLAB-OUTLINE") { Color = AciColor.Green };
            var linesLayer  = new Layer("LINES")        { Color = AciColor.Cyan };
            var colLayer    = new Layer("COLUMNS")      { Color = AciColor.Yellow };

            dxf.Layers.Add(slabLayer);
            dxf.Layers.Add(linesLayer);
            dxf.Layers.Add(colLayer);

            foreach (var pts in geometry.Slabs)
            {
                var verts = pts.Select(p => new LwPolylineVertex(p.X, p.Y)).ToList();
                var poly = new LwPolyline(verts, true) { Layer = slabLayer };
                dxf.Entities.Add(poly);
            }

            foreach (var (x, y) in geometry.Columns)
            {
                var pt = new Point(x, y, 0) { Layer = colLayer };
                dxf.Entities.Add(pt);
            }

            foreach (var pts in geometry.Lines)
            {
                var verts = pts.Select(p => new LwPolylineVertex(p.X, p.Y)).ToList();
                var poly = new LwPolyline(verts, false) { Layer = linesLayer };
                dxf.Entities.Add(poly);
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
