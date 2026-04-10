#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal sealed record F2kStoryData(
        List<(string Name, double X, double Y, double Z)> PointOrder,
        List<(string Id, List<string> PtNames, List<(double X, double Y)> Coords,
              string PropName, double ThicknessMm, string GradeCode,
              (byte R, byte G, byte B) Color)> Areas,
        List<(string Id, List<string> PtNames, List<(double X, double Y)> Coords,
              string PropName, double ThicknessMm, string GradeCode)> DropAreas,
        List<(string Id, string J1, string J2, double LenMm, string? SecName)> LineSegs,
        List<string> ColumnPointNames,
        List<(string SecName, double W, double D)> ColumnSections,
        List<(string OpeningId, string ParentAreaId, List<string> PtNames)> OpeningRows,
        List<List<(double X, double Y)>> SlabsForStrips,
        List<(double X, double Y)> ColumnsForGrid
    );

    internal static class F2kModelPrep
    {
        internal static (double Cx, double Cy) ComputeCentroid(
            IEnumerable<ExtractedGeometry> geometries)
        {
            double totalWeight = 0, sumX = 0, sumY = 0;
            foreach (var geometry in geometries)
            {
                foreach (var pts in geometry.Slabs)
                {
                    double w = PolygonProcessor.PolygonAreaMm2(pts);
                    var (pcx, pcy) = PolygonProcessor.PolygonAreaCentroid(pts);
                    sumX += pcx * w;
                    sumY += pcy * w;
                    totalWeight += w;
                }
                foreach (var (x, y) in geometry.Columns)
                {
                    sumX += x;
                    sumY += y;
                    totalWeight += 1.0;
                }
                foreach (var pts in geometry.Lines)
                {
                    double w = PolygonProcessor.PathLength(pts);
                    var (pcx, pcy) = PolygonProcessor.Centroid(pts);
                    sumX += pcx * w;
                    sumY += pcy * w;
                    totalWeight += w;
                }
            }
            if (totalWeight == 0) return (0, 0);
            return (sumX / totalWeight, sumY / totalWeight);
        }

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

        internal static F2kStoryData BuildStoryModel(
            List<List<(double X, double Y)>> xSlabs,
            List<(byte R, byte G, byte B)> xSlabColors,
            List<List<(double X, double Y)>> xLines,
            List<(double X, double Y)> xColumns,
            List<(double WidthMm, double DepthMm)> xColumnBaseSizes,
            List<List<(double X, double Y)>> xDropPanelCandidates,
            IReadOnlyList<(string Text, double X, double Y)> annotations,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings,
            string idPrefix,
            double elevationMm,
            CultureInfo ic,
            double dropPanelThicknessMultiplier = 1.5)
        {
            (xSlabs, xSlabColors) = PolygonProcessor.ProcessSlabs(xSlabs, xSlabColors);

            var dropPanels = PolygonProcessor.DetectDropPanels(xSlabs, xColumns, xDropPanelCandidates);

            static bool SamePolygon(List<(double X, double Y)> a, List<(double X, double Y)> b)
            {
                if (a.Count != b.Count) return false;
                for (int i = 0; i < a.Count; i++)
                {
                    if (Math.Abs(a[i].X - b[i].X) > 1e-6 || Math.Abs(a[i].Y - b[i].Y) > 1e-6)
                        return false;
                }
                return true;
            }

            var dropPanelChildIndices = new HashSet<int>();
            foreach (var (_, _, poly) in dropPanels)
            {
                for (int i = 0; i < xSlabs.Count; i++)
                {
                    if (SamePolygon(xSlabs[i], poly))
                    {
                        dropPanelChildIndices.Add(i);
                        break;
                    }
                }
            }

            // Automatic opening detection is disabled — for Bluebeam markup, inner
            // polygons are structural elements (columns, walls), not slab holes.
            // Users can explicitly set shapes to "Opening" type via right-click.
            var openingPairs = new List<(int ParentIndex, int ChildIndex)>();
            var childIndices = new HashSet<int>(dropPanelChildIndices);

            var annotThick = ThicknessAnnotationParser.AssignToSlabs(xSlabs, annotations);
            var annotCols = ColumnSectionParser.AssignToColumns(xColumns, annotations);
            var annotBeams = BeamSectionParser.AssignToLines(xLines, annotations);

            var pointMap = new Dictionary<(long, long, long), string>();
            int ptCounter = 0;
            var pointOrder = new List<(string, double, double, double)>();
            string Pt(double xMm, double yMm)
            {
                var key = ((long)Math.Round(xMm * 10), (long)Math.Round(yMm * 10), (long)Math.Round(elevationMm * 10));
                if (!pointMap.TryGetValue(key, out string? name))
                {
                    name = idPrefix == "" ? $"P{++ptCounter}" : $"{idPrefix}J{++ptCounter}";
                    pointMap[key] = name;
                    pointOrder.Add((name, key.Item1 / 10.0, key.Item2 / 10.0, key.Item3 / 10.0));
                }
                return name;
            }

            string GradeFor((byte R, byte G, byte B) color)
            {
                if (colorSettings != null && colorSettings.TryGetValue(color, out var s) &&
                    !string.IsNullOrWhiteSpace(s.GradeCode))
                    return s.GradeCode;
                return PdfToSafeConstants.DefaultGradeCode;
            }

            (string PropName, double ThicknessMm, string GradeCode) SlabPropertyFor((byte R, byte G, byte B) color, int slabIdx)
            {
                string grade = GradeFor(color);
                double thickness = PdfToSafeConstants.DefaultThicknessMm;
                if (slabIdx >= 0 && slabIdx < annotThick.Length && annotThick[slabIdx].HasValue)
                    thickness = annotThick[slabIdx]!.Value;
                else if (colorSettings != null && colorSettings.TryGetValue(color, out var s) && s.ThicknessMm > 0)
                    thickness = s.ThicknessMm;
                string propName = $"S{thickness.ToString("0.###", ic)}{grade}".Replace('.', '_');
                return (propName, thickness, grade);
            }

            var areas = new List<(string Id, List<string> PtNames, List<(double X, double Y)> Coords, string PropName, double ThicknessMm, string GradeCode, (byte R, byte G, byte B) Color)>();
            var dropAreas = new List<(string Id, List<string> PtNames, List<(double X, double Y)> Coords, string PropName, double ThicknessMm, string GradeCode)>();
            var lineSegs = new List<(string Id, string J1, string J2, double LenMm, string? SecName)>();
            var columnPointNames = new List<string>();
            var xColumnSections = new List<(string SecName, double W, double D)>();
            var openingRows = new List<(string OpeningId, string ParentAreaId, List<string> PtNames)>();

            int aIdx = 0;
            var slabIndexToAreaId = new Dictionary<int, string>();
            var slabPropertiesByIndex = new Dictionary<int, (string PropName, double ThicknessMm, string GradeCode)>();
            for (int i = 0; i < xSlabs.Count; i++)
            {
                if (childIndices.Contains(i)) continue;
                var pts = xSlabs[i];
                var names = pts.Select(p => Pt(p.X, p.Y)).ToList();
                var color = xSlabColors[i];
                var prop = SlabPropertyFor(color, i);
                string areaId = idPrefix == "" ? $"A{++aIdx}" : $"{idPrefix}A{++aIdx}";
                slabIndexToAreaId[i] = areaId;
                slabPropertiesByIndex[i] = prop;
                areas.Add((areaId, names, pts, prop.PropName, prop.ThicknessMm, prop.GradeCode, color));
            }

            int dropIdx = 0;
            foreach (var (slabIdx, _, poly) in dropPanels)
            {
                if (!slabPropertiesByIndex.TryGetValue(slabIdx, out var baseProp))
                    continue;
                double dropThickness = Math.Round(baseProp.ThicknessMm * dropPanelThicknessMultiplier, 3);
                string grade = baseProp.GradeCode;
                string propName = $"S{dropThickness.ToString("0.###", ic)}{grade}".Replace('.', '_');
                var names = poly.Select(p => Pt(p.X, p.Y)).ToList();
                string dropId = idPrefix == "" ? $"DROP{++dropIdx}" : $"{idPrefix}DROP{++dropIdx}";
                dropAreas.Add((dropId, names, poly, propName, dropThickness, grade));
            }

            int lIdx = 0;
            for (int li = 0; li < xLines.Count; li++)
            {
                var pts = xLines[li];
                string? secName = null;
                if (annotBeams[li].HasValue)
                {
                    var (w, d) = annotBeams[li]!.Value;
                    secName = $"B{(int)Math.Round(w)}x{(int)Math.Round(d)}";
                }

                for (int i = 0; i < pts.Count - 1; i++)
                {
                    string j1 = Pt(pts[i].X, pts[i].Y);
                    string j2 = Pt(pts[i + 1].X, pts[i + 1].Y);
                    if (j1 != j2)
                    {
                        string segId = idPrefix == "" ? $"L{++lIdx}" : $"{idPrefix}L{++lIdx}";
                        lineSegs.Add((segId, j1, j2, PolygonProcessor.Distance(pts[i], pts[i + 1]), secName));
                    }
                }
            }

            foreach (var (px, py) in xColumns)
                columnPointNames.Add(Pt(px, py));
            for (int i = 0; i < xColumns.Count; i++)
            {
                double w, d;
                if (annotCols[i].HasValue)
                    (w, d) = annotCols[i]!.Value;
                else if (i < xColumnBaseSizes.Count &&
                         xColumnBaseSizes[i].WidthMm >= 100 &&
                         xColumnBaseSizes[i].DepthMm >= 100)
                    (w, d) = (xColumnBaseSizes[i].WidthMm, xColumnBaseSizes[i].DepthMm);
                else
                    (w, d) = (500, 500);

                string secName = $"C{(int)Math.Round(w)}x{(int)Math.Round(d)}";
                xColumnSections.Add((secName, w, d));
            }

            // Deduplicate columns that mapped to the same point (coincident after rounding)
            var seenColPts = new HashSet<string>();
            for (int i = columnPointNames.Count - 1; i >= 0; i--)
            {
                if (!seenColPts.Add(columnPointNames[i]))
                {
                    columnPointNames.RemoveAt(i);
                    xColumnSections.RemoveAt(i);
                }
            }

            int oIdx = 0;
            foreach (var (parentSlabIdx, childSlabIdx) in openingPairs)
            {
                if (!slabIndexToAreaId.TryGetValue(parentSlabIdx, out string? parentId)) continue;
                var pts = xSlabs[childSlabIdx];
                var names = pts.Select(p => Pt(p.X, p.Y)).ToList();
                string openingId = idPrefix == "" ? $"O{++oIdx}" : $"{idPrefix}O{++oIdx}";
                openingRows.Add((openingId, parentId, names));
            }

            return new F2kStoryData(
                pointOrder, areas, dropAreas, lineSegs,
                columnPointNames, xColumnSections, openingRows,
                xSlabs,
                xColumns
            );
        }
    }
}
