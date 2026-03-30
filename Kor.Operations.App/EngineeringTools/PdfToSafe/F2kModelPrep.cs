#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal static class F2kModelPrep
    {
        internal static (
            List<List<(double X, double Y)>> Slabs,
            List<(byte R, byte G, byte B)> SlabColors,
            List<List<(double X, double Y)>> Lines,
            List<(double X, double Y)> Columns,
            List<(double WidthMm, double DepthMm)> ColumnBaseSizes,
            List<List<(double X, double Y)>> DropPanelCandidates
        ) PrepareGeometry(
            ExtractedGeometry geometry,
            double cx, double cy,
            HashSet<int>? excludedSlabs,
            HashSet<int>? excludedLines,
            HashSet<int>? excludedColumns,
            HashSet<(byte R, byte G, byte B)>? excludedColors)
        {
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
            var xSlabColors = new List<(byte R, byte G, byte B)>();
            var xLines = new List<List<(double X, double Y)>>();
            var xColumns = new List<(double X, double Y)>();
            var xColumnBaseSizes = new List<(double WidthMm, double DepthMm)>();
            var xDropPanelCandidates = new List<List<(double X, double Y)>>();

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
                if (Ok(px, py))
                {
                    xColumns.Add((px, py));
                    xColumnBaseSizes.Add(i < geometry.ColumnSizes.Count ? geometry.ColumnSizes[i] : (0, 0));
                }
            }
            foreach (var poly in geometry.DropPanelCandidates)
            {
                var pts = FilterPts(Ctr(poly));
                if (pts.Count < 3) continue;
                var s = PolygonProcessor.DouglasPeucker(pts, PdfToSafeConstants.DouglasPeuckerEpsilonMm);
                if (s.Count >= 3) xDropPanelCandidates.Add(s);
            }

            return (xSlabs, xSlabColors, xLines, xColumns, xColumnBaseSizes, xDropPanelCandidates);
        }
    }
}
