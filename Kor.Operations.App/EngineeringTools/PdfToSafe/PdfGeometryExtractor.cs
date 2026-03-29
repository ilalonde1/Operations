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
        public double ThicknessMm { get; set; } = 200.0;
        public double SdlKPa      { get; set; } = 0.0;
        public double LiveKPa     { get; set; } = 0.0;
        public string GradeCode   { get; set; } = "C30";
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
                    if (diag >= slabMinDiagonalMm) { result.Slabs.Add(pts);           result.SlabColors.Add(color);   }
                    else                           { result.Columns.Add(Centroid(pts)); result.ColumnColors.Add(color); }
                }
                else
                {
                    double len = PathLength(pts);
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

            double scale = scaleDenominator * PointsToMm;
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
                    if (!PointInPolygon(point, geometry.Slabs[i]))
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
        {
            // ── Step 1: Centre all geometry near origin ───────────────────────
            double totalWeight = 0.0, sumX = 0.0, sumY = 0.0;
            foreach (var pts in geometry.Slabs)
            {
                double w = PathLength(pts); var (pcx, pcy) = Centroid(pts);
                sumX += pcx * w; sumY += pcy * w; totalWeight += w;
            }
            foreach (var (x, y) in geometry.Columns) { sumX += x; sumY += y; totalWeight += 1.0; }
            foreach (var pts in geometry.Lines)
            {
                double w = PathLength(pts); var (pcx, pcy) = Centroid(pts);
                sumX += pcx * w; sumY += pcy * w; totalWeight += w;
            }
            if (totalWeight == 0.0) return;
            double cx = sumX / totalWeight;
            double cy = sumY / totalWeight;

            List<(double X, double Y)> Ctr(List<(double X, double Y)> pts) =>
                pts.Select(p => (p.X - cx, p.Y - cy)).ToList();

            const double minSeg = 0.5;
            bool Ok(double x, double y) =>
                !double.IsNaN(x) && !double.IsInfinity(x) &&
                !double.IsNaN(y) && !double.IsInfinity(y);

            // ── Step 2: Filter & pre-collect exported geometry ────────────────
            // Doing this before writing so we can compute $EXTMIN/$EXTMAX for
            // the DXF HEADER, which helps SAFE zoom to content on import.
            List<(double X, double Y)> FilterPts(List<(double X, double Y)> raw)
            {
                var result = new List<(double X, double Y)>();
                foreach (var p in raw)
                {
                    if (!Ok(p.X, p.Y)) continue;
                    if (result.Count == 0 || Distance(result[^1], p) >= minSeg)
                        result.Add(p);
                }
                return result;
            }

            var xSlabs   = new List<List<(double X, double Y)>>();
            var xLines   = new List<List<(double X, double Y)>>();
            var xColumns = new List<(double X, double Y)>();

            for (int i = 0; i < geometry.Slabs.Count; i++)
            {
                if (excludedSlabs?.Contains(i) == true) continue;
                if (excludedColors != null && i < geometry.SlabColors.Count && excludedColors.Contains(geometry.SlabColors[i])) continue;
                var pts = FilterPts(Ctr(geometry.Slabs[i]));
                if (pts.Count >= 3) xSlabs.Add(pts);
            }
            for (int i = 0; i < geometry.Lines.Count; i++)
            {
                if (excludedLines?.Contains(i) == true) continue;
                if (excludedColors != null && i < geometry.LineColors.Count && excludedColors.Contains(geometry.LineColors[i])) continue;
                var pts = FilterPts(Ctr(geometry.Lines[i]));
                if (pts.Count >= 2) xLines.Add(pts);
            }
            for (int i = 0; i < geometry.Columns.Count; i++)
            {
                if (excludedColumns?.Contains(i) == true) continue;
                if (excludedColors != null && i < geometry.ColumnColors.Count && excludedColors.Contains(geometry.ColumnColors[i])) continue;
                var (colX, colY) = geometry.Columns[i];
                double px = colX - cx, py = colY - cy;
                if (Ok(px, py)) xColumns.Add((px, py));
            }

            // ── Step 3: Bounding box ($EXTMIN / $EXTMAX) ──────────────────────
            double bMinX = double.MaxValue, bMinY = double.MaxValue;
            double bMaxX = double.MinValue, bMaxY = double.MinValue;
            void Expand(double ex, double ey)
            {
                if (ex < bMinX) bMinX = ex; if (ex > bMaxX) bMaxX = ex;
                if (ey < bMinY) bMinY = ey; if (ey > bMaxY) bMaxY = ey;
            }
            foreach (var s in xSlabs)   foreach (var (ex,ey) in s) Expand(ex,ey);
            foreach (var l in xLines)   foreach (var (ex,ey) in l) Expand(ex,ey);
            foreach (var (ex,ey) in xColumns) Expand(ex,ey);
            if (bMinX > bMaxX) { bMinX = -1000; bMaxX = 1000; bMinY = -1000; bMaxY = 1000; }

            // ── Step 4: Write R12 (AC1009) DXF ───────────────────────────────
            // R12 is the most universally accepted by structural analysis tools.
            // Entities go directly in ENTITIES section — no BLOCKS needed.
            // Layer names SLAB / BEAM / COLUMN are the convention SAFE recognises.
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            using var sw = new StreamWriter(outputPath, false, System.Text.Encoding.ASCII);
            void G(int code, string val) { sw.WriteLine(code); sw.WriteLine(val); }
            void Num(int code, double v)  => G(code, v.ToString("F4", ic));

            // HEADER
            G(0,"SECTION"); G(2,"HEADER");
            G(9,"$ACADVER");  G(1,"AC1009");
            G(9,"$EXTMIN"); Num(10,bMinX); Num(20,bMinY); G(30,"0.0000");
            G(9,"$EXTMAX"); Num(10,bMaxX); Num(20,bMaxY); G(30,"0.0000");
            G(0,"ENDSEC");

            // TABLES
            G(0,"SECTION"); G(2,"TABLES");
            G(0,"TABLE"); G(2,"LTYPE"); G(70,"1");
            G(0,"LTYPE"); G(2,"CONTINUOUS"); G(70,"0"); G(3,"Solid line"); G(72,"65"); G(73,"0"); G(40,"0.0");
            G(0,"ENDTAB");
            G(0,"TABLE"); G(2,"LAYER"); G(70,"4");
            void WL(string n, int c) { G(0,"LAYER"); G(2,n); G(70,"0"); G(62,c.ToString()); G(6,"CONTINUOUS"); }
            WL("0",7); WL("SLAB",3); WL("BEAM",4); WL("COLUMN",2);
            G(0,"ENDTAB");
            G(0,"ENDSEC");

            // ENTITIES — POLYLINE/VERTEX/SEQEND (R12 style, widest compatibility)
            G(0,"SECTION"); G(2,"ENTITIES");

            void WritePolyline(string layer, List<(double X, double Y)> pts, bool closed)
            {
                G(0,"POLYLINE"); G(8,layer);
                G(66,"1");                        // vertices-follow flag
                G(70,closed ? "1" : "0");
                Num(10,0); Num(20,0); Num(30,0);  // polyline origin (required placeholder)
                foreach (var (vx,vy) in pts)
                {
                    G(0,"VERTEX"); G(8,layer);
                    Num(10,vx); Num(20,vy); Num(30,0);
                }
                G(0,"SEQEND"); G(8,layer);
            }

            foreach (var pts in xSlabs)   WritePolyline("SLAB", pts, true);
            foreach (var (px,py) in xColumns)
            {
                G(0,"POINT"); G(8,"COLUMN");
                Num(10,px); Num(20,py); Num(30,0);
            }
            foreach (var pts in xLines)   WritePolyline("BEAM", pts, false);

            G(0,"ENDSEC");
            G(0,"EOF");
        }

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
        {
            // ── Centre near origin ────────────────────────────────────────────
            double totalWeight = 0.0, sumX = 0.0, sumY = 0.0;
            foreach (var pts in geometry.Slabs)
            { double w = PathLength(pts); var (pcx,pcy) = Centroid(pts); sumX+=pcx*w; sumY+=pcy*w; totalWeight+=w; }
            foreach (var (x,y) in geometry.Columns) { sumX+=x; sumY+=y; totalWeight+=1.0; }
            foreach (var pts in geometry.Lines)
            { double w = PathLength(pts); var (pcx,pcy) = Centroid(pts); sumX+=pcx*w; sumY+=pcy*w; totalWeight+=w; }
            if (totalWeight == 0.0) return;
            double cx = sumX / totalWeight, cy = sumY / totalWeight;

            List<(double X, double Y)> Ctr(List<(double X, double Y)> pts) =>
                pts.Select(p => (p.X - cx, p.Y - cy)).ToList();

            const double minSeg = 0.5;
            bool Ok(double x, double y) =>
                !double.IsNaN(x) && !double.IsInfinity(x) &&
                !double.IsNaN(y) && !double.IsInfinity(y);

            List<(double X, double Y)> FilterPts(List<(double X, double Y)> raw)
            {
                var result = new List<(double X, double Y)>();
                foreach (var p in raw)
                {
                    if (!Ok(p.X, p.Y)) continue;
                    if (result.Count == 0 || Distance(result[^1], p) >= minSeg)
                        result.Add(p);
                }
                return result;
            }

            // ── Filter & collect ──────────────────────────────────────────────
            var xSlabs   = new List<List<(double X, double Y)>>();
            var xSlabColors = new List<(byte R, byte G, byte B)>();
            var xLines   = new List<List<(double X, double Y)>>();
            var xColumns = new List<(double X, double Y)>();

            for (int i = 0; i < geometry.Slabs.Count; i++)
            {
                if (excludedSlabs?.Contains(i) == true) continue;
                if (excludedColors != null && i < geometry.SlabColors.Count && excludedColors.Contains(geometry.SlabColors[i])) continue;
                var pts = FilterPts(Ctr(geometry.Slabs[i]));
                if (pts.Count >= 3)
                {
                    xSlabs.Add(pts);
                    xSlabColors.Add(i < geometry.SlabColors.Count ? geometry.SlabColors[i] : ((byte)0, (byte)0, (byte)0));
                }
            }
            for (int i = 0; i < geometry.Lines.Count; i++)
            {
                if (excludedLines?.Contains(i) == true) continue;
                if (excludedColors != null && i < geometry.LineColors.Count && excludedColors.Contains(geometry.LineColors[i])) continue;
                var pts = FilterPts(Ctr(geometry.Lines[i]));
                if (pts.Count >= 2) xLines.Add(pts);
            }
            for (int i = 0; i < geometry.Columns.Count; i++)
            {
                if (excludedColumns?.Contains(i) == true) continue;
                if (excludedColors != null && i < geometry.ColumnColors.Count && excludedColors.Contains(geometry.ColumnColors[i])) continue;
                var (colX, colY) = geometry.Columns[i];
                double px = colX - cx, py = colY - cy;
                if (Ok(px, py)) xColumns.Add((px, py));
            }

            // ── Simplify slab polygons (remove near-collinear vertices) ──────
            // PdfPig tessellates curves into many small segments. SAFE handles
            // polygons fine but benefits from clean corner-only outlines.
            // Douglas-Peucker with 5 mm tolerance keeps genuine corners and
            // removes arc-tessellation artefacts.
            for (int i = xSlabs.Count - 1; i >= 0; i--)
            {
                var s = DouglasPeucker(xSlabs[i], 5.0);
                if (s.Count >= 3) xSlabs[i] = s;
                else
                {
                    xSlabs.RemoveAt(i);
                    xSlabColors.RemoveAt(i);
                }
            }

            // ── Decompose polygons with >4 vertices into triangles ───────────
            // SAFE floor objects support a maximum of 4 vertices per element.
            // Fan triangulation from vertex 0 decomposes any N-gon into N-2 triangles.
            // Skip degenerate (zero/near-zero area) triangles — SAFE rejects them.
            static double TriArea((double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
                => 0.5 * Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y));

            var decomposed = new List<List<(double X, double Y)>>();
            var decomposedColors = new List<(byte R, byte G, byte B)>();
            for (int slabIdx = 0; slabIdx < xSlabs.Count; slabIdx++)
            {
                var pts = xSlabs[slabIdx];
                var color = xSlabColors[slabIdx];
                if (pts.Count <= 4)
                {
                    decomposed.Add(pts);
                    decomposedColors.Add(color);
                }
                else
                {
                    for (int i = 1; i < pts.Count - 1; i++)
                    {
                        if (TriArea(pts[0], pts[i], pts[i + 1]) > 100.0) // min 100 mm² (~10×10 mm)
                        {
                            decomposed.Add(new List<(double X, double Y)> { pts[0], pts[i], pts[i + 1] });
                            decomposedColors.Add(color);
                        }
                    }
                }
            }
            xSlabs = decomposed;
            xSlabColors = decomposedColors;

            // ── Unique point registry (round to 0.1 mm for dedup) ─────────────
            var pointOrder = new List<(string Name, double X, double Y)>();
            var pointMap   = new Dictionary<(long, long), string>();
            int ptCounter  = 0;
            var ic = System.Globalization.CultureInfo.InvariantCulture;

            string Pt(double xMm, double yMm)
            {
                var key = ((long)Math.Round(xMm * 10), (long)Math.Round(yMm * 10));
                if (!pointMap.TryGetValue(key, out string? name))
                {
                    name = $"P{++ptCounter}";
                    pointMap[key] = name;
                    pointOrder.Add((name, key.Item1 / 10.0, key.Item2 / 10.0));
                }
                return name;
            }

            // ── Build connectivity ────────────────────────────────────────────
            string GradeFor((byte R, byte G, byte B) color)
            {
                if (colorSettings != null && colorSettings.TryGetValue(color, out var s) &&
                    !string.IsNullOrWhiteSpace(s.GradeCode))
                    return s.GradeCode;
                return "C30";
            }

            string PropNameFor((byte R, byte G, byte B) color)
            {
                double thickness = 200.0;
                if (colorSettings != null && colorSettings.TryGetValue(color, out var settings) && settings.ThicknessMm > 0)
                    thickness = settings.ThicknessMm;
                var tStr = thickness.ToString("0.###", ic).Replace('.', '_');
                return $"S{tStr}{GradeFor(color)}";
            }

            var areas    = new List<(string Id, List<string> PtNames, List<(double X, double Y)> Coords, string PropName, (byte R, byte G, byte B) Color)>();
            var lineSegs = new List<(string Id, string J1, string J2, double LenMm)>();

            int aIdx = 0, lIdx = 0;
            for (int i = 0; i < xSlabs.Count; i++)
            {
                var pts = xSlabs[i];
                var names = pts.Select(p => Pt(p.X, p.Y)).ToList();
                var color = xSlabColors[i];
                areas.Add(($"A{++aIdx}", names, pts, PropNameFor(color), color));
            }
            foreach (var pts in xLines)
            {
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    string j1 = Pt(pts[i].X,   pts[i].Y);
                    string j2 = Pt(pts[i+1].X, pts[i+1].Y);
                    if (j1 != j2) lineSegs.Add(($"L{++lIdx}", j1, j2, Distance(pts[i], pts[i+1])));
                }
            }
            var columnPointNames = new List<string>();
            foreach (var (px, py) in xColumns)
                columnPointNames.Add(Pt(px, py));

            // ── Write SAFE v23.2 TABLE format ────────────────────────────────
            // Table names and field names verified against a live SAFE v23.2 export.
            // File → Import → SAFE v12.x reads this format and creates structural
            // Area objects (slabs) and Line objects (beams).
            // Units: N, mm, C.  Default slab: 200 mm C30 — change in SAFE under
            // Define → Slab Properties after import.
            using var sw = new StreamWriter(outputPath, false, System.Text.Encoding.ASCII);

            sw.WriteLine("$ Generated by KOR NewerForma - PDF to SAFE");
            sw.WriteLine();

            // PROGRAM CONTROL
            sw.WriteLine("TABLE:  \"PROGRAM CONTROL\"");
            sw.WriteLine("   ProgramName=SAFE   Version=23.0.0   CurrUnits=\"N, mm, C\"   MergeTol=1   ModelDatum=0");
            sw.WriteLine();

            var usedGrades = areas.Select(a => GradeFor(a.Color)).Distinct().OrderBy(g => g).ToList();
            if (usedGrades.Count == 0) usedGrades.Add("C30");

            var gradeProps = new Dictionary<string, (double E, double G, double Fc)>
            {
                { "C20", (29962, 12804, 20) },
                { "C25", (31476, 13451, 25) },
                { "C28", (32308, 13806, 28) },
                { "C30", (32837, 14033, 30) },
                { "C32", (33346, 14251, 32) },
                { "C35", (34077, 14563, 35) },
                { "C40", (35220, 15051, 40) },
                { "C50", (37278, 15930, 50) },
            };

            sw.WriteLine("TABLE:  \"MATERIAL PROPERTIES - GENERAL\"");
            foreach (var g in usedGrades)
                sw.WriteLine($"   Material={g}   Type=Concrete   SymType=Isotropic   Grade={g}   Color=Blue");
            sw.WriteLine();
            sw.WriteLine("TABLE:  \"MATERIAL PROPERTIES - BASIC MECHANICAL PROPERTIES\"");
            foreach (var g in usedGrades)
            {
                var (e, gMod, _) = gradeProps.TryGetValue(g, out var p) ? p : (32837, 14033, 30);
                sw.WriteLine($"   Material={g}   DensityType=Mass   UnitWeight=2.355E-05   UnitMass=2.4E-09   E1={e}   G12={gMod}   U12=0.17   A1=1E-05");
            }
            sw.WriteLine();
            sw.WriteLine("TABLE:  \"MATERIAL PROPERTIES - CONCRETE DATA\"");
            foreach (var g in usedGrades)
            {
                var (_, _, fc) = gradeProps.TryGetValue(g, out var p) ? p : (32837, 14033, 30);
                sw.WriteLine($"   Material={g}   Fc={fc}   LtWtConc=No   IsUserFr=No   SSCurveOpt=Simple   SSHysType=Kinematic");
            }
            sw.WriteLine();

            var uniqueSlabProps = areas
                .Select(a =>
                {
                    var settings = colorSettings != null && colorSettings.TryGetValue(a.Color, out var s)
                        ? s : new SlabColorSettings();
                    double thick = settings.ThicknessMm > 0 ? settings.ThicknessMm : 200.0;
                    string grade = !string.IsNullOrWhiteSpace(settings.GradeCode) ? settings.GradeCode : "C30";
                    return (a.PropName, ThicknessMm: thick, Grade: grade);
                })
                .Distinct()
                .OrderBy(p => p.PropName)
                .ToList();

            bool hasSdlLoads = areas.Any(a =>
                colorSettings != null &&
                colorSettings.TryGetValue(a.Color, out var s) &&
                s.SdlKPa > 0);
            bool hasLiveLoads = areas.Any(a =>
                colorSettings != null &&
                colorSettings.TryGetValue(a.Color, out var s) &&
                s.LiveKPa > 0);

            sw.WriteLine("TABLE:  \"LOAD PATTERN DEFINITIONS\"");
            sw.WriteLine("   Name=DEAD   Type=Dead   SelfWtMult=1");
            if (hasSdlLoads)  sw.WriteLine("   Name=SDL   Type=SuperDead   SelfWtMult=0");
            if (hasLiveLoads) sw.WriteLine("   Name=LIVE   Type=Live   SelfWtMult=0");
            sw.WriteLine();

            if (!string.IsNullOrEmpty(loadCombCode))
            {
                var combos = BuildLoadCombinations(loadCombCode, hasSdlLoads, hasLiveLoads, ic);
                if (combos.Count > 0)
                {
                    sw.WriteLine("TABLE:  \"LOAD COMBINATION DEFINITIONS\"");
                    foreach (var (name, _) in combos)
                        sw.WriteLine($"   Name={name}   Type=LinearAdd");
                    sw.WriteLine();

                    sw.WriteLine("TABLE:  \"LOAD COMBINATION CASES\"");
                    foreach (var (name, cases) in combos)
                        foreach (var (pat, sf) in cases)
                            sw.WriteLine($"   Name={name}   LoadPat={pat}   SF={sf.ToString("0.##", ic)}");
                    sw.WriteLine();
                }
            }

            sw.WriteLine("TABLE:  \"SLAB PROPERTY DEFINITIONS\"");
            foreach (var (propName, thicknessMm, grade) in uniqueSlabProps)
            {
                sw.WriteLine($"   Name={propName}   \"Modeling Type\"=Shell-Thick   \"Property Type\"=Slab   Material={grade}   \"Slab Thickness\"={thicknessMm.ToString("0.###", ic)} _");
                sw.WriteLine("        \"f11 Modifier\"=1   \"f22 Modifier\"=1   \"f12 Modifier\"=1 _");
                sw.WriteLine("        \"m11 Modifier\"=1   \"m22 Modifier\"=1   \"m12 Modifier\"=1 _");
                sw.WriteLine("        \"v13 Modifier\"=1   \"v23 Modifier\"=1 _");
                sw.WriteLine("        \"Mass Modifier\"=1   \"Weight Modifier\"=1   Orthotropic?=No");
            }
            sw.WriteLine();

            // POINT OBJECT CONNECTIVITY — verified field names from SAFE v23.2 export
            sw.WriteLine("TABLE:  \"POINT OBJECT CONNECTIVITY\"");
            foreach (var (name, xMm, yMm) in pointOrder)
                sw.WriteLine($"   UniqueName={name}   \"Is Auto Point\"=No   IsSpecial=No   X={xMm.ToString("F1", ic)}   Y={yMm.ToString("F1", ic)}   Z=0");
            sw.WriteLine();

            // FLOOR OBJECT CONNECTIVITY — verified table/field names from SAFE v23.2 export
            if (areas.Count > 0)
            {
                sw.WriteLine("TABLE:  \"FLOOR OBJECT CONNECTIVITY\"");
                foreach (var (id, ptNames, coords, _, _) in areas)
                {
                    // Compute perimeter and area (shoelace) for this polygon
                    double perim = 0;
                    double area2 = 0;
                    for (int j = 0; j < coords.Count; j++)
                    {
                        var a = coords[j];
                        var b = coords[(j + 1) % coords.Count];
                        perim  += Math.Sqrt((b.X-a.X)*(b.X-a.X) + (b.Y-a.Y)*(b.Y-a.Y));
                        area2  += a.X * b.Y - b.X * a.Y;
                    }
                    double areaVal = Math.Abs(area2) / 2.0;

                    var sb = new System.Text.StringBuilder();
                    sb.Append($"   \"Unique Name\"={id}");
                    for (int j = 0; j < ptNames.Count; j++)
                        sb.Append($"   UniquePt{j+1}={ptNames[j]}");
                    sb.Append($"   Perimeter={perim.ToString("F4", ic)}   Area={areaVal.ToString("F4", ic)}   GUID={Guid.NewGuid():D}");
                    sw.WriteLine(sb.ToString());
                }
                sw.WriteLine();

                // AREA ASSIGNMENTS - SECTION PROPERTIES — verified from SAFE v23.2 export
                sw.WriteLine("TABLE:  \"AREA ASSIGNMENTS - SECTION PROPERTIES\"");
                foreach (var (id, _, _, propName, _) in areas)
                    sw.WriteLine($"   UniqueName={id}   \"Section Property\"={propName}   \"Property Type\"=Slab");
                sw.WriteLine();

                if (columnPointNames.Count > 0)
                {
                    sw.WriteLine("TABLE:  \"JOINT ASSIGNMENTS - RESTRAINTS\"");
                    foreach (var pointName in columnPointNames)
                        sw.WriteLine($"   UniqueName={pointName}   U1=Yes   U2=Yes   U3=Yes   R1=No   R2=No   R3=No");
                    sw.WriteLine();
                }

                var areaLoads = new List<(string AreaId, string Pattern, double Value)>();
                foreach (var (id, _, _, _, color) in areas)
                {
                    if (colorSettings == null || !colorSettings.TryGetValue(color, out var settings))
                        continue;

                    double sdlValue = settings.SdlKPa * 0.001;
                    double liveValue = settings.LiveKPa * 0.001;

                    if (sdlValue > 0) areaLoads.Add((id, "SDL", sdlValue));
                    if (liveValue > 0) areaLoads.Add((id, "LIVE", liveValue));
                }

                if (areaLoads.Count > 0)
                {
                    sw.WriteLine("TABLE:  \"AREA LOAD ASSIGNMENTS - UNIFORM\"");
                    foreach (var (areaId, pattern, value) in areaLoads)
                        sw.WriteLine($"   UniqueName={areaId}   LoadPat={pattern}   Dir=Gravity   Value={value.ToString("0.###", ic)}");
                }
                sw.WriteLine();
            }

            // NULL LINE OBJECT CONNECTIVITY — verified from SAFE v23.2 export
            if (lineSegs.Count > 0)
            {
                sw.WriteLine("TABLE:  \"NULL LINE OBJECT CONNECTIVITY\"");
                foreach (var (id, j1, j2, lenMm) in lineSegs)
                    sw.WriteLine($"   \"Unique Name\"={id}   UniquePtI={j1}   UniquePtJ={j2}   Length={lenMm.ToString("F4", ic)}   GUID={Guid.NewGuid():D}");
                sw.WriteLine();
            }

            sw.WriteLine("END TABLE DATA");
        }

        public static void ExportF2k(
            string outputPath,
            ExtractedGeometry geom,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings = null,
            string? loadCombCode = null)
            => ExportF2k(outputPath,
                new[] { (geom, "Story 1", 0.0) },
                colorSettings, loadCombCode);

        public static void ExportF2k(
            string outputPath,
            IReadOnlyList<(ExtractedGeometry Geom, string StoryName, double ElevationMm)> stories,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings = null,
            string? loadCombCode = null)
        {
            if (stories.Count == 0) return;

            double totalWeight = 0.0, sumX = 0.0, sumY = 0.0;
            foreach (var (geom, _, _) in stories)
            {
                foreach (var pts in geom.Slabs)
                {
                    double w = PathLength(pts);
                    var (pcx, pcy) = Centroid(pts);
                    sumX += pcx * w;
                    sumY += pcy * w;
                    totalWeight += w;
                }
                foreach (var (x, y) in geom.Columns)
                {
                    sumX += x;
                    sumY += y;
                    totalWeight += 1.0;
                }
                foreach (var pts in geom.Lines)
                {
                    double w = PathLength(pts);
                    var (pcx, pcy) = Centroid(pts);
                    sumX += pcx * w;
                    sumY += pcy * w;
                    totalWeight += w;
                }
            }
            if (totalWeight == 0.0) return;
            double cx = sumX / totalWeight, cy = sumY / totalWeight;

            List<(double X, double Y)> Ctr(List<(double X, double Y)> pts) =>
                pts.Select(p => (p.X - cx, p.Y - cy)).ToList();

            const double minSeg = 0.5;
            bool Ok(double x, double y) =>
                !double.IsNaN(x) && !double.IsInfinity(x) &&
                !double.IsNaN(y) && !double.IsInfinity(y);

            List<(double X, double Y)> FilterPts(List<(double X, double Y)> raw)
            {
                var result = new List<(double X, double Y)>();
                foreach (var p in raw)
                {
                    if (!Ok(p.X, p.Y)) continue;
                    if (result.Count == 0 || Distance(result[^1], p) >= minSeg)
                        result.Add(p);
                }
                return result;
            }

            static double TriArea((double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
                => 0.5 * Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y));

            var ic = System.Globalization.CultureInfo.InvariantCulture;

            string GradeFor((byte R, byte G, byte B) color)
            {
                if (colorSettings != null && colorSettings.TryGetValue(color, out var s) &&
                    !string.IsNullOrWhiteSpace(s.GradeCode))
                    return s.GradeCode;
                return "C30";
            }

            string PropNameFor((byte R, byte G, byte B) color)
            {
                double thickness = 200.0;
                if (colorSettings != null && colorSettings.TryGetValue(color, out var settings) && settings.ThicknessMm > 0)
                    thickness = settings.ThicknessMm;
                var tStr = thickness.ToString("0.###", ic).Replace('.', '_');
                return $"S{tStr}{GradeFor(color)}";
            }

            var pointOrder = new List<(string Name, double X, double Y, double Z)>();
            var areas = new List<(string Id, List<string> PtNames, List<(double X, double Y)> Coords, string PropName, (byte R, byte G, byte B) Color)>();
            var lineSegs = new List<(string Id, string J1, string J2, double LenMm)>();
            var columnPointNames = new List<string>();

            for (int storyIndex = 0; storyIndex < stories.Count; storyIndex++)
            {
                var (geometry, _, elevationMm) = stories[storyIndex];
                var xSlabs = new List<List<(double X, double Y)>>();
                var xSlabColors = new List<(byte R, byte G, byte B)>();
                var xLines = new List<List<(double X, double Y)>>();
                var xColumns = new List<(double X, double Y)>();

                for (int i = 0; i < geometry.Slabs.Count; i++)
                {
                    var pts = FilterPts(Ctr(geometry.Slabs[i]));
                    if (pts.Count >= 3)
                    {
                        xSlabs.Add(pts);
                        xSlabColors.Add(i < geometry.SlabColors.Count ? geometry.SlabColors[i] : ((byte)0, (byte)0, (byte)0));
                    }
                }
                for (int i = 0; i < geometry.Lines.Count; i++)
                {
                    var pts = FilterPts(Ctr(geometry.Lines[i]));
                    if (pts.Count >= 2)
                        xLines.Add(pts);
                }
                for (int i = 0; i < geometry.Columns.Count; i++)
                {
                    var (colX, colY) = geometry.Columns[i];
                    double px = colX - cx, py = colY - cy;
                    if (Ok(px, py))
                        xColumns.Add((px, py));
                }

                for (int i = xSlabs.Count - 1; i >= 0; i--)
                {
                    var s = DouglasPeucker(xSlabs[i], 5.0);
                    if (s.Count >= 3) xSlabs[i] = s;
                    else
                    {
                        xSlabs.RemoveAt(i);
                        xSlabColors.RemoveAt(i);
                    }
                }

                var decomposed = new List<List<(double X, double Y)>>();
                var decomposedColors = new List<(byte R, byte G, byte B)>();
                for (int slabIdx = 0; slabIdx < xSlabs.Count; slabIdx++)
                {
                    var pts = xSlabs[slabIdx];
                    var color = xSlabColors[slabIdx];
                    if (pts.Count <= 4)
                    {
                        decomposed.Add(pts);
                        decomposedColors.Add(color);
                    }
                    else
                    {
                        for (int i = 1; i < pts.Count - 1; i++)
                        {
                            if (TriArea(pts[0], pts[i], pts[i + 1]) > 100.0)
                            {
                                decomposed.Add(new List<(double X, double Y)> { pts[0], pts[i], pts[i + 1] });
                                decomposedColors.Add(color);
                            }
                        }
                    }
                }
                xSlabs = decomposed;
                xSlabColors = decomposedColors;

                var pointMap = new Dictionary<(long, long, long), string>();
                int ptCounter = 0;

                string Pt(double xMm, double yMm)
                {
                    var key = ((long)Math.Round(xMm * 10), (long)Math.Round(yMm * 10), (long)Math.Round(elevationMm * 10));
                    if (!pointMap.TryGetValue(key, out string? name))
                    {
                        name = $"s{storyIndex + 1}_J{++ptCounter}";
                        pointMap[key] = name;
                        pointOrder.Add((name, key.Item1 / 10.0, key.Item2 / 10.0, key.Item3 / 10.0));
                    }
                    return name;
                }

                int areaCounter = 0;
                for (int i = 0; i < xSlabs.Count; i++)
                {
                    var pts = xSlabs[i];
                    var color = xSlabColors[i];
                    areas.Add(($"s{storyIndex + 1}_A{++areaCounter}", pts.Select(p => Pt(p.X, p.Y)).ToList(), pts, PropNameFor(color), color));
                }

                int lineCounter = 0;
                foreach (var pts in xLines)
                {
                    for (int i = 0; i < pts.Count - 1; i++)
                    {
                        string j1 = Pt(pts[i].X, pts[i].Y);
                        string j2 = Pt(pts[i + 1].X, pts[i + 1].Y);
                        if (j1 != j2)
                            lineSegs.Add(($"s{storyIndex + 1}_L{++lineCounter}", j1, j2, Distance(pts[i], pts[i + 1])));
                    }
                }

                foreach (var (px, py) in xColumns)
                    columnPointNames.Add(Pt(px, py));
            }

            using var sw = new StreamWriter(outputPath, false, System.Text.Encoding.ASCII);

            sw.WriteLine("$ Generated by KOR NewerForma - PDF to SAFE");
            sw.WriteLine();
            sw.WriteLine("TABLE:  \"PROGRAM CONTROL\"");
            sw.WriteLine("   ProgramName=SAFE   Version=23.0.0   CurrUnits=\"N, mm, C\"   MergeTol=1   ModelDatum=0");
            sw.WriteLine();

            var usedGrades = areas.Select(a => GradeFor(a.Color)).Distinct().OrderBy(g => g).ToList();
            if (usedGrades.Count == 0) usedGrades.Add("C30");

            var gradeProps = new Dictionary<string, (double E, double G, double Fc)>
            {
                { "C20", (29962, 12804, 20) },
                { "C25", (31476, 13451, 25) },
                { "C28", (32308, 13806, 28) },
                { "C30", (32837, 14033, 30) },
                { "C32", (33346, 14251, 32) },
                { "C35", (34077, 14563, 35) },
                { "C40", (35220, 15051, 40) },
                { "C50", (37278, 15930, 50) },
            };

            sw.WriteLine("TABLE:  \"MATERIAL PROPERTIES - GENERAL\"");
            foreach (var g in usedGrades)
                sw.WriteLine($"   Material={g}   Type=Concrete   SymType=Isotropic   Grade={g}   Color=Blue");
            sw.WriteLine();
            sw.WriteLine("TABLE:  \"MATERIAL PROPERTIES - BASIC MECHANICAL PROPERTIES\"");
            foreach (var g in usedGrades)
            {
                var (e, gMod, _) = gradeProps.TryGetValue(g, out var p) ? p : (32837, 14033, 30);
                sw.WriteLine($"   Material={g}   DensityType=Mass   UnitWeight=2.355E-05   UnitMass=2.4E-09   E1={e}   G12={gMod}   U12=0.17   A1=1E-05");
            }
            sw.WriteLine();
            sw.WriteLine("TABLE:  \"MATERIAL PROPERTIES - CONCRETE DATA\"");
            foreach (var g in usedGrades)
            {
                var (_, _, fc) = gradeProps.TryGetValue(g, out var p) ? p : (32837, 14033, 30);
                sw.WriteLine($"   Material={g}   Fc={fc}   LtWtConc=No   IsUserFr=No   SSCurveOpt=Simple   SSHysType=Kinematic");
            }
            sw.WriteLine();

            var uniqueSlabProps = areas
                .Select(a =>
                {
                    var settings = colorSettings != null && colorSettings.TryGetValue(a.Color, out var s)
                        ? s : new SlabColorSettings();
                    double thick = settings.ThicknessMm > 0 ? settings.ThicknessMm : 200.0;
                    string grade = !string.IsNullOrWhiteSpace(settings.GradeCode) ? settings.GradeCode : "C30";
                    return (a.PropName, ThicknessMm: thick, Grade: grade);
                })
                .Distinct()
                .OrderBy(p => p.PropName)
                .ToList();

            bool hasSdlLoads = areas.Any(a =>
                colorSettings != null &&
                colorSettings.TryGetValue(a.Color, out var s) &&
                s.SdlKPa > 0);
            bool hasLiveLoads = areas.Any(a =>
                colorSettings != null &&
                colorSettings.TryGetValue(a.Color, out var s) &&
                s.LiveKPa > 0);

            sw.WriteLine("TABLE:  \"LOAD PATTERN DEFINITIONS\"");
            sw.WriteLine("   Name=DEAD   Type=Dead   SelfWtMult=1");
            if (hasSdlLoads) sw.WriteLine("   Name=SDL   Type=SuperDead   SelfWtMult=0");
            if (hasLiveLoads) sw.WriteLine("   Name=LIVE   Type=Live   SelfWtMult=0");
            sw.WriteLine();

            if (!string.IsNullOrEmpty(loadCombCode))
            {
                var combos = BuildLoadCombinations(loadCombCode, hasSdlLoads, hasLiveLoads, ic);
                if (combos.Count > 0)
                {
                    sw.WriteLine("TABLE:  \"LOAD COMBINATION DEFINITIONS\"");
                    foreach (var (name, _) in combos)
                        sw.WriteLine($"   Name={name}   Type=LinearAdd");
                    sw.WriteLine();

                    sw.WriteLine("TABLE:  \"LOAD COMBINATION CASES\"");
                    foreach (var (name, cases) in combos)
                        foreach (var (pat, sf) in cases)
                            sw.WriteLine($"   Name={name}   LoadPat={pat}   SF={sf.ToString("0.##", ic)}");
                    sw.WriteLine();
                }
            }

            sw.WriteLine("TABLE:  \"SLAB PROPERTY DEFINITIONS\"");
            foreach (var (propName, thicknessMm, grade) in uniqueSlabProps)
            {
                sw.WriteLine($"   Name={propName}   \"Modeling Type\"=Shell-Thick   \"Property Type\"=Slab   Material={grade}   \"Slab Thickness\"={thicknessMm.ToString("0.###", ic)} _");
                sw.WriteLine("        \"f11 Modifier\"=1   \"f22 Modifier\"=1   \"f12 Modifier\"=1 _");
                sw.WriteLine("        \"m11 Modifier\"=1   \"m22 Modifier\"=1   \"m12 Modifier\"=1 _");
                sw.WriteLine("        \"v13 Modifier\"=1   \"v23 Modifier\"=1 _");
                sw.WriteLine("        \"Mass Modifier\"=1   \"Weight Modifier\"=1   Orthotropic?=No");
            }
            sw.WriteLine();

            sw.WriteLine("TABLE:  \"POINT OBJECT CONNECTIVITY\"");
            foreach (var (name, xMm, yMm, zMm) in pointOrder)
                sw.WriteLine($"   UniqueName={name}   \"Is Auto Point\"=No   IsSpecial=No   X={xMm.ToString("F1", ic)}   Y={yMm.ToString("F1", ic)}   Z={zMm.ToString("0.###", ic)}");
            sw.WriteLine();

            if (areas.Count > 0)
            {
                sw.WriteLine("TABLE:  \"FLOOR OBJECT CONNECTIVITY\"");
                foreach (var (id, ptNames, coords, _, _) in areas)
                {
                    double perim = 0;
                    double area2 = 0;
                    for (int j = 0; j < coords.Count; j++)
                    {
                        var a = coords[j];
                        var b = coords[(j + 1) % coords.Count];
                        perim += Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                        area2 += a.X * b.Y - b.X * a.Y;
                    }
                    double areaVal = Math.Abs(area2) / 2.0;

                    var areaLine = new System.Text.StringBuilder();
                    areaLine.Append($"   \"Unique Name\"={id}");
                    for (int j = 0; j < ptNames.Count; j++)
                        areaLine.Append($"   UniquePt{j + 1}={ptNames[j]}");
                    areaLine.Append($"   Perimeter={perim.ToString("F4", ic)}   Area={areaVal.ToString("F4", ic)}   GUID={Guid.NewGuid():D}");
                    sw.WriteLine(areaLine.ToString());
                }
                sw.WriteLine();

                sw.WriteLine("TABLE:  \"AREA ASSIGNMENTS - SECTION PROPERTIES\"");
                foreach (var (id, _, _, propName, _) in areas)
                    sw.WriteLine($"   UniqueName={id}   \"Section Property\"={propName}   \"Property Type\"=Slab");
                sw.WriteLine();
            }

            if (columnPointNames.Count > 0)
            {
                sw.WriteLine("TABLE:  \"JOINT ASSIGNMENTS - RESTRAINTS\"");
                foreach (var pointName in columnPointNames)
                    sw.WriteLine($"   UniqueName={pointName}   U1=Yes   U2=Yes   U3=Yes   R1=No   R2=No   R3=No");
                sw.WriteLine();
            }

            var areaLoads = new List<(string AreaId, string Pattern, double Value)>();
            foreach (var (id, _, _, _, color) in areas)
            {
                if (colorSettings == null || !colorSettings.TryGetValue(color, out var settings))
                    continue;

                double sdlValue = settings.SdlKPa * 0.001;
                double liveValue = settings.LiveKPa * 0.001;

                if (sdlValue > 0) areaLoads.Add((id, "SDL", sdlValue));
                if (liveValue > 0) areaLoads.Add((id, "LIVE", liveValue));
            }

            if (areaLoads.Count > 0)
            {
                sw.WriteLine("TABLE:  \"AREA LOAD ASSIGNMENTS - UNIFORM\"");
                foreach (var (areaId, pattern, value) in areaLoads)
                    sw.WriteLine($"   UniqueName={areaId}   LoadPat={pattern}   Dir=Gravity   Value={value.ToString("0.###", ic)}");
                sw.WriteLine();
            }

            if (lineSegs.Count > 0)
            {
                sw.WriteLine("TABLE:  \"NULL LINE OBJECT CONNECTIVITY\"");
                foreach (var (id, j1, j2, lenMm) in lineSegs)
                    sw.WriteLine($"   \"Unique Name\"={id}   UniquePtI={j1}   UniquePtJ={j2}   Length={lenMm.ToString("F4", ic)}   GUID={Guid.NewGuid():D}");
                sw.WriteLine();
            }

            sw.WriteLine("END TABLE DATA");
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static List<(string Name, List<(string Pat, double SF)> Cases)> BuildLoadCombinations(
            string code, bool hasSdl, bool hasLive,
            System.Globalization.CultureInfo _)
        {
            var result = new List<(string Name, List<(string Pat, double SF)> Cases)>();

            void Add(string name, params (string Pat, double SF)[] cases)
            {
                var filtered = cases
                    .Where(c => c.Pat == "DEAD"
                             || (c.Pat == "SDL" && hasSdl)
                             || (c.Pat == "LIVE" && hasLive))
                    .ToList();
                if (filtered.Count > 0)
                    result.Add((name, filtered));
            }

            switch (code)
            {
                case "AS/NZS":
                    Add("1.35G", ("DEAD", 1.35), ("SDL", 1.35));
                    Add("1.2G+1.5Q", ("DEAD", 1.2), ("SDL", 1.2), ("LIVE", 1.5));
                    Add("1.2G+0.6Q", ("DEAD", 1.2), ("SDL", 1.2), ("LIVE", 0.6));
                    Add("G+Q", ("DEAD", 1.0), ("SDL", 1.0), ("LIVE", 1.0));
                    break;
                case "ASCE7":
                    Add("1.4D", ("DEAD", 1.4), ("SDL", 1.4));
                    Add("1.2D+1.6L", ("DEAD", 1.2), ("SDL", 1.2), ("LIVE", 1.6));
                    Add("1.2D+1.0L", ("DEAD", 1.2), ("SDL", 1.2), ("LIVE", 1.0));
                    Add("D+L", ("DEAD", 1.0), ("SDL", 1.0), ("LIVE", 1.0));
                    break;
                case "EC0":
                    Add("1.35G+1.5Q", ("DEAD", 1.35), ("SDL", 1.35), ("LIVE", 1.5));
                    Add("1.35G+1.05Q", ("DEAD", 1.35), ("SDL", 1.35), ("LIVE", 1.05));
                    Add("G+1.5Q", ("DEAD", 1.0), ("SDL", 1.0), ("LIVE", 1.5));
                    Add("G+Q", ("DEAD", 1.0), ("SDL", 1.0), ("LIVE", 1.0));
                    break;
            }
            return result;
        }

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

        // Douglas-Peucker polyline simplification.
        // Removes points whose perpendicular deviation from the chord is < epsilon mm.
        private static List<(double X, double Y)> DouglasPeucker(
            List<(double X, double Y)> pts, double epsilon)
        {
            if (pts.Count <= 3) return pts;
            double maxDist = 0.0; int maxIdx = 1;
            for (int i = 1; i < pts.Count - 1; i++)
            {
                double d = PerpendicularDist(pts[i], pts[0], pts[^1]);
                if (d > maxDist) { maxDist = d; maxIdx = i; }
            }
            if (maxDist > epsilon)
            {
                var left  = DouglasPeucker(pts[..(maxIdx + 1)], epsilon);
                var right = DouglasPeucker(pts[maxIdx..], epsilon);
                return left.Take(left.Count - 1).Concat(right).ToList();
            }
            return new List<(double X, double Y)> { pts[0], pts[^1] };
        }

        private static double PerpendicularDist(
            (double X, double Y) pt,
            (double X, double Y) a,
            (double X, double Y) b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-10) return Distance(pt, a);
            return Math.Abs(dy * pt.X - dx * pt.Y + b.X * a.Y - b.Y * a.X) / len;
        }

        private static bool PointInPolygon((double X, double Y) pt, List<(double X, double Y)> poly)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if ((poly[i].Y > pt.Y) != (poly[j].Y > pt.Y) &&
                    pt.X < (poly[j].X - poly[i].X) * (pt.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                    inside = !inside;
            }
            return inside;
        }
    }
}
