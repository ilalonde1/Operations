#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal static class DxfExporter
    {
        public static void Export(
            ExtractedGeometry geometry,
            string outputPath,
            HashSet<int>? excludedSlabs = null,
            HashSet<int>? excludedLines = null,
            HashSet<int>? excludedColumns = null,
            HashSet<(byte R, byte G, byte B)>? excludedColors = null)
        {
            double totalWeight = 0.0, sumX = 0.0, sumY = 0.0;
            foreach (var pts in geometry.Slabs)
            {
                double w = PolygonProcessor.PathLength(pts); var (pcx, pcy) = PolygonProcessor.Centroid(pts);
                sumX += pcx * w; sumY += pcy * w; totalWeight += w;
            }
            foreach (var (x, y) in geometry.Columns) { sumX += x; sumY += y; totalWeight += 1.0; }
            foreach (var pts in geometry.Lines)
            {
                double w = PolygonProcessor.PathLength(pts); var (pcx, pcy) = PolygonProcessor.Centroid(pts);
                sumX += pcx * w; sumY += pcy * w; totalWeight += w;
            }
            if (totalWeight == 0.0) return;
            double cx = sumX / totalWeight;
            double cy = sumY / totalWeight;

            List<(double X, double Y)> Ctr(List<(double X, double Y)> pts) =>
                pts.Select(p => (p.X - cx, p.Y - cy)).ToList();

            const double minSeg = PdfToSafeConstants.MinVertexDistanceMm;
            bool Ok(double x, double y) =>
                !double.IsNaN(x) && !double.IsInfinity(x) &&
                !double.IsNaN(y) && !double.IsInfinity(y);

            List<(double X, double Y)> FilterPts(List<(double X, double Y)> raw)
            {
                var result = new List<(double X, double Y)>();
                foreach (var p in raw)
                {
                    if (!Ok(p.X, p.Y)) continue;
                    if (result.Count == 0 || PolygonProcessor.Distance(result[^1], p) >= minSeg)
                        result.Add(p);
                }
                return result;
            }

            var xSlabs = new List<List<(double X, double Y)>>();
            var xLines = new List<List<(double X, double Y)>>();
            // Parallel to xLines: was this line wall-hinted?
            var xLineIsWall = new List<bool>();
            var xColumns = new List<(double X, double Y)>();
            // Parallel to xColumns: bounding-box sizes for footprint rectangles.
            var xColumnSizes = new List<(double W, double D)>();

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
                if (pts.Count >= 2)
                {
                    xLines.Add(pts);
                    xLineIsWall.Add(i < geometry.LineSectionHints.Count && geometry.LineSectionHints[i] is not null);
                }
            }
            for (int i = 0; i < geometry.Columns.Count; i++)
            {
                if (excludedColumns?.Contains(i) == true) continue;
                if (excludedColors != null && i < geometry.ColumnColors.Count && excludedColors.Contains(geometry.ColumnColors[i])) continue;
                var (colX, colY) = geometry.Columns[i];
                double px = colX - cx, py = colY - cy;
                if (Ok(px, py))
                {
                    xColumns.Add((px, py));
                    xColumnSizes.Add(i < geometry.ColumnSizes.Count ? geometry.ColumnSizes[i] : (400.0, 400.0));
                }
            }

            double bMinX = double.MaxValue, bMinY = double.MaxValue;
            double bMaxX = double.MinValue, bMaxY = double.MinValue;
            void Expand(double ex, double ey)
            {
                if (ex < bMinX) bMinX = ex; if (ex > bMaxX) bMaxX = ex;
                if (ey < bMinY) bMinY = ey; if (ey > bMaxY) bMaxY = ey;
            }
            foreach (var s in xSlabs) foreach (var (ex, ey) in s) Expand(ex, ey);
            foreach (var l in xLines) foreach (var (ex, ey) in l) Expand(ex, ey);
            foreach (var (ex, ey) in xColumns) Expand(ex, ey);
            if (bMinX > bMaxX) { bMinX = -1000; bMaxX = 1000; bMinY = -1000; bMaxY = 1000; }

            var ic = CultureInfo.InvariantCulture;
            using var sw = new StreamWriter(outputPath, false, Encoding.ASCII);
            void G(int code, string val) { sw.WriteLine(code); sw.WriteLine(val); }
            void Num(int code, double v) => G(code, v.ToString("F4", ic));

            G(0, "SECTION"); G(2, "HEADER");
            G(9, "$ACADVER"); G(1, "AC1009");
            G(9, "$EXTMIN"); Num(10, bMinX); Num(20, bMinY); G(30, "0.0000");
            G(9, "$EXTMAX"); Num(10, bMaxX); Num(20, bMaxY); G(30, "0.0000");
            G(0, "ENDSEC");

            G(0, "SECTION"); G(2, "TABLES");
            G(0, "TABLE"); G(2, "LTYPE"); G(70, "1");
            G(0, "LTYPE"); G(2, "CONTINUOUS"); G(70, "0"); G(3, "Solid line"); G(72, "65"); G(73, "0"); G(40, "0.0");
            G(0, "ENDTAB");
            G(0, "TABLE"); G(2, "LAYER"); G(70, "5");
            void WL(string n, int c) { G(0, "LAYER"); G(2, n); G(70, "0"); G(62, c.ToString()); G(6, "CONTINUOUS"); }
            WL("0", 7); WL("SLAB", 3); WL("BEAM", 4); WL("COLUMN", 2); WL("WALL", 1);
            G(0, "ENDTAB");
            G(0, "ENDSEC");

            G(0, "SECTION"); G(2, "ENTITIES");

            void WritePolyline(string layer, List<(double X, double Y)> pts, bool closed)
            {
                G(0, "POLYLINE"); G(8, layer);
                G(66, "1");
                G(70, closed ? "1" : "0");
                Num(10, 0); Num(20, 0); Num(30, 0);
                foreach (var (vx, vy) in pts)
                {
                    G(0, "VERTEX"); G(8, layer);
                    Num(10, vx); Num(20, vy); Num(30, 0);
                }
                G(0, "SEQEND"); G(8, layer);
            }

            foreach (var pts in xSlabs) WritePolyline("SLAB", pts, true);

            // Columns: footprint rectangles from the parallel xColumnSizes list.
            for (int i = 0; i < xColumns.Count; i++)
            {
                var (px, py) = xColumns[i];
                double hw = xColumnSizes[i].W / 2.0;
                double hd = xColumnSizes[i].D / 2.0;
                var rect = new List<(double X, double Y)>
                {
                    (px - hw, py - hd), (px + hw, py - hd),
                    (px + hw, py + hd), (px - hw, py + hd)
                };
                WritePolyline("COLUMN", rect, true);
            }

            // Lines: WALL or BEAM layer from the parallel xLineIsWall list.
            for (int i = 0; i < xLines.Count; i++)
            {
                WritePolyline(xLineIsWall[i] ? "WALL" : "BEAM", xLines[i], false);
            }

            G(0, "ENDSEC");
            G(0, "EOF");
        }
    }
}
