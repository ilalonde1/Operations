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
        /// <summary>
        /// Writes the geometry as a DXF.
        ///
        /// <paramref name="layerByColour"/> changes what a LAYER means, and it exists because of a
        /// request this exporter could not serve: "I would like to get a dxf for this pdf. The tower
        /// outline, the red markups I did and the balcony outline." Not one of those three is a
        /// beam, a column or a slab, so the classifier's layering — the default below — merges all
        /// three into SLAB and BEAM and hands back something no one can pick apart.
        ///
        /// All three ARE colours. A draughtsman separates his drawing by colour and pen, the parser
        /// keeps that per shape, and this discarded it on the way out. With this set, each source
        /// colour becomes its own layer (PDF-F00000 and so on), so the separation the drawing was
        /// made with survives into AutoCAD and the engineer picks what he wants by turning layers
        /// off. On Parcel 11 that is eleven layers, and his red markup is exactly one of them.
        ///
        /// Off by default: every existing caller wants the structural layering and gets it unchanged.
        /// </summary>
        public static void Export(
            ExtractedGeometry geometry,
            string outputPath,
            HashSet<int>? excludedSlabs = null,
            HashSet<int>? excludedLines = null,
            HashSet<int>? excludedColumns = null,
            HashSet<(byte R, byte G, byte B)>? excludedColors = null,
            bool layerByColour = false)
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
            // Parallel to each of the three, the colour the shape was drawn in. Carried whether or
            // not it is used, so the three lists cannot fall out of step with it.
            var xSlabColours = new List<(byte R, byte G, byte B)>();
            var xLineColours = new List<(byte R, byte G, byte B)>();
            var xColumnColours = new List<(byte R, byte G, byte B)>();
            var black = ((byte)0, (byte)0, (byte)0);

            for (int i = 0; i < geometry.Slabs.Count; i++)
            {
                if (excludedSlabs?.Contains(i) == true) continue;
                if (excludedColors != null && i < geometry.SlabColors.Count && excludedColors.Contains(geometry.SlabColors[i])) continue;
                var pts = FilterPts(Ctr(geometry.Slabs[i]));
                if (pts.Count >= 3)
                {
                    xSlabs.Add(pts);
                    xSlabColours.Add(i < geometry.SlabColors.Count ? geometry.SlabColors[i] : black);
                }
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
                    xLineColours.Add(i < geometry.LineColors.Count ? geometry.LineColors[i] : black);
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
                    xColumnColours.Add(i < geometry.ColumnColors.Count ? geometry.ColumnColors[i] : black);
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
            // A layer per source colour, or the four structural ones. Named PDF-RRGGBB so the layer
            // says which pen it came off, and given the nearest AutoCAD colour index so it still
            // LOOKS like the drawing when it opens.
            static string ColourLayer((byte R, byte G, byte B) c) => $"PDF-{c.R:X2}{c.G:X2}{c.B:X2}";

            static int NearestAci((byte R, byte G, byte B) c)
            {
                (int Aci, byte R, byte G, byte B)[] basics =
                {
                    (1, 255, 0, 0), (2, 255, 255, 0), (3, 0, 255, 0), (4, 0, 255, 255),
                    (5, 0, 0, 255), (6, 255, 0, 255), (7, 255, 255, 255), (8, 128, 128, 128),
                    (9, 192, 192, 192), (250, 51, 51, 51),
                };
                int best = 7;
                double bestDistance = double.MaxValue;
                foreach (var (aci, r, g, b) in basics)
                {
                    double d = (c.R - r) * (c.R - r) + (c.G - g) * (c.G - g) + (c.B - b) * (c.B - b);
                    if (d < bestDistance) { bestDistance = d; best = aci; }
                }
                return best;
            }

            var colourLayers = new List<(byte R, byte G, byte B)>();
            if (layerByColour)
            {
                var seen = new HashSet<(byte R, byte G, byte B)>();
                foreach (var c in xSlabColours.Concat(xColumnColours).Concat(xLineColours))
                    if (seen.Add(c)) colourLayers.Add(c);
            }

            G(0, "TABLE"); G(2, "LAYER"); G(70, (layerByColour ? colourLayers.Count + 1 : 5).ToString(ic));
            void WL(string n, int c) { G(0, "LAYER"); G(2, n); G(70, "0"); G(62, c.ToString()); G(6, "CONTINUOUS"); }
            WL("0", 7);
            if (layerByColour)
            {
                foreach (var c in colourLayers) WL(ColourLayer(c), NearestAci(c));
            }
            else
            {
                WL("SLAB", 3); WL("BEAM", 4); WL("COLUMN", 2); WL("WALL", 1);
            }
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

            for (int i = 0; i < xSlabs.Count; i++)
                WritePolyline(layerByColour ? ColourLayer(xSlabColours[i]) : "SLAB", xSlabs[i], true);

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
                WritePolyline(layerByColour ? ColourLayer(xColumnColours[i]) : "COLUMN", rect, true);
            }

            // Lines: WALL or BEAM layer from the parallel xLineIsWall list.
            for (int i = 0; i < xLines.Count; i++)
            {
                WritePolyline(
                    layerByColour ? ColourLayer(xLineColours[i]) : (xLineIsWall[i] ? "WALL" : "BEAM"),
                    xLines[i], false);
            }

            G(0, "ENDSEC");
            G(0, "EOF");
        }
    }
}
