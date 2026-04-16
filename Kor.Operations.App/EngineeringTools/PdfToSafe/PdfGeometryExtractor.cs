#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UglyToad.PdfPig;

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
        /// <summary>
        /// Bounding-box dimensions (Width, Depth in mm) for each detected column,
        /// parallel to <see cref="Columns"/>. Derived from the column polygon footprint.
        /// Width = X extent, Depth = Y extent.
        /// </summary>
        public List<(double WidthMm, double DepthMm)> ColumnSizes { get; } = new();
        public List<(byte R, byte G, byte B)> LineColors   { get; } = new();
        /// <summary>
        /// Optional cross-section hints parallel to <see cref="Lines"/>. Populated when a
        /// slab polygon is reclassified as a wall/beam and its intended beam section is
        /// derived from the polygon's bounding box. Null entries mean "no hint — fall
        /// back to text-annotation parsing (BeamSectionParser)". Not populated by the
        /// initial extraction; only by <see cref="PdfGeometryExtractor.ReclassifyByColor"/>.
        /// </summary>
        public List<(double WidthMm, double DepthMm)?> LineSectionHints { get; set; } = new();
        public List<List<(double X, double Y)>> DropPanelCandidates { get; set; } = new();
        public double PageWidthPts  { get; set; }
        public double PageHeightPts { get; set; }
        public int    ScaleDenominator { get; set; }
        public int  PageCount    { get; set; }
        public int  RawPathCount { get; set; }
        public bool IsVectorPdf  { get; set; }
        /// <summary>
        /// Raw text annotations extracted from the PDF page, with their centroid
        /// positions in the same mm coordinate space as Slabs/Lines/Columns.
        /// Populated during extraction when text parsing is enabled.
        /// </summary>
        public List<(string Text, double X, double Y)> TextAnnotations { get; set; } = new();
    }

    internal sealed class SlabColorSettings
    {
        public string ElementType { get; set; } = "Slab";
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
            using var stream = System.IO.File.OpenRead(filePath);
            return Extract(stream, scaleDenominator, pageNumber, slabMinDiagonalMm, lineMinLengthMm, excludeGridLines);
        }

        /// <summary>
        /// Stream overload for unit tests and in-memory synthetic PDF fixtures.
        /// The caller is responsible for seeking the stream to position 0 if needed.
        /// </summary>
        public static ExtractedGeometry Extract(
            System.IO.Stream pdfStream,
            int    scaleDenominator,
            int    pageNumber          = 1,
            double slabMinDiagonalMm  = PdfToSafeConstants.DefaultSlabMinDiagonalMm,
            double lineMinLengthMm    = PdfToSafeConstants.DefaultLineMinLengthMm,
            bool   excludeGridLines   = false)
        {
            var result = new ExtractedGeometry();
            result.ScaleDenominator = scaleDenominator;
            double scale = scaleDenominator * PdfToSafeConstants.PointsToMm;

            using var doc = PdfDocument.Open(pdfStream);
            var page = doc.GetPage(pageNumber);
            result.PageWidthPts  = page.Width;
            result.PageHeightPts = page.Height;
            result.PageCount     = doc.NumberOfPages;
            result.TextAnnotations = PdfGeometryParser.ExtractTextAnnotations(page, scale);

            var rawSubpaths = PdfGeometryParser.ParsePage(page, scale);

            result.RawPathCount = rawSubpaths.Count;

            int meaningfulCount = rawSubpaths.Count(s =>
                s.Points.Count > 3 ||
                (s.IsClosed && GeometryFilterService.BoundingBoxDiagonal(s.Points) > 10.0));
            result.IsVectorPdf = meaningfulCount >= 5;

            if (rawSubpaths.Count == 0) return result;

            double pageWidthMm  = result.PageWidthPts  * scale;
            double pageHeightMm = result.PageHeightPts * scale;
            GeometryFilterService.Classify(rawSubpaths, result,
                slabMinDiagonalMm, lineMinLengthMm,
                excludeGridLines, pageWidthMm, pageHeightMm);

            return result;
        }

        public static int? DetectScale(string f, int p = 1)
            => PdfGeometryParser.DetectScale(f, p);

        public static Dictionary<(byte R, byte G, byte B), double> ExtractThicknessHints(
            string filePath,
            int    pageNumber,
            int    scaleDenominator,
            ExtractedGeometry geometry)
            => PdfGeometryParser.ExtractThicknessHints(filePath, pageNumber, scaleDenominator, geometry);

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
            ExportSettings? settings = null)
            => SafeF2kExporter.Export(geometry, outputPath, excludedSlabs, excludedLines, excludedColumns, excludedColors, colorSettings, settings);
        public static void ExportF2k(
            string outputPath,
            IReadOnlyList<(ExtractedGeometry Geom, string StoryName, double ElevationMm)> stories,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings = null,
            ExportSettings? settings = null)
            => SafeF2kExporter.Export(outputPath, stories, colorSettings, settings);
        public static void ExportE2k(
            string outputPath,
            ExtractedGeometry geom,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings = null,
            ExportSettings? settings = null)
            => EtabsE2kExporter.Export(outputPath, geom, colorSettings, settings);

        public static void ExportE2k(
            string outputPath,
            IReadOnlyList<(ExtractedGeometry Geom, string StoryName, double ElevationMm)> stories,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings = null,
            ExportSettings? settings = null)
            => EtabsE2kExporter.Export(outputPath, stories, colorSettings, settings);

        // ── helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a new <see cref="ExtractedGeometry"/> with elements moved between
        /// Slabs/Lines/Columns based on per-element overrides (highest priority) then
        /// per-color <see cref="SlabColorSettings.ElementType"/>. Original is never mutated.
        /// </summary>
        public static ExtractedGeometry ReclassifyByColor(
            ExtractedGeometry original,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings,
            Dictionary<int, string>? slabTypeOverrides = null,
            Dictionary<int, string>? lineTypeOverrides = null,
            Dictionary<int, string>? columnTypeOverrides = null)
        {
            bool hasColorOverrides = colorSettings is not null && colorSettings.Count > 0;
            bool hasElementOverrides = (slabTypeOverrides?.Count ?? 0) > 0
                                    || (lineTypeOverrides?.Count ?? 0) > 0
                                    || (columnTypeOverrides?.Count ?? 0) > 0;

            if (!hasColorOverrides && !hasElementOverrides)
                return original;

            string TypeFor(int index, string elementKind, (byte R, byte G, byte B) color, string fallback)
            {
                // Priority 1: per-element override
                Dictionary<int, string>? overrides = elementKind switch
                {
                    "slab" => slabTypeOverrides,
                    "line" => lineTypeOverrides,
                    "column" => columnTypeOverrides,
                    _ => null
                };
                if (overrides is not null && overrides.TryGetValue(index, out var elementType))
                    return elementType;

                // Priority 2: per-color type
                if (colorSettings is not null && colorSettings.TryGetValue(color, out var cs))
                    return cs.ElementType;

                // Priority 3: original classification
                return fallback;
            }

            // Fast path: check if any override actually changes something
            if (!hasElementOverrides)
            {
                bool anyColorChange = false;
                foreach (var (color, cs) in colorSettings!)
                {
                    string type = cs.ElementType;
                    // Types that, when matching the element's current bucket,
                    // are a no-op. Wall always causes a change (not natively
                    // classified), so never skip it here.
                    if (string.Equals(type, "Slab", StringComparison.OrdinalIgnoreCase) && original.SlabColors.Contains(color)) continue;
                    if (string.Equals(type, "Beam", StringComparison.OrdinalIgnoreCase) && original.LineColors.Contains(color)) continue;
                    if (string.Equals(type, "Column", StringComparison.OrdinalIgnoreCase) && original.ColumnColors.Contains(color)) continue;
                    if (!string.Equals(type, "Slab",    StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(type, "Beam",    StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(type, "Column",  StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(type, "Wall",    StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(type, "Opening", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(type, "Ignore",  StringComparison.OrdinalIgnoreCase))
                        continue;
                    anyColorChange = true;
                    break;
                }
                if (!anyColorChange) return original;
            }

            var result = new ExtractedGeometry
            {
                PageWidthPts = original.PageWidthPts,
                PageHeightPts = original.PageHeightPts,
                ScaleDenominator = original.ScaleDenominator,
                PageCount = original.PageCount,
                RawPathCount = original.RawPathCount,
                IsVectorPdf = original.IsVectorPdf,
                TextAnnotations = original.TextAnnotations,
                DropPanelCandidates = original.DropPanelCandidates
            };

            // Helper: every Lines.Add must also push a parallel LineSectionHints
            // entry (null = no hint, or (W,D) for reclassifier-supplied sections).
            // Keeps the two lists strictly parallel throughout reclassification.
            void AddLine(
                List<(double X, double Y)> pts,
                (byte R, byte G, byte B) col,
                (double WidthMm, double DepthMm)? hint)
            {
                result.Lines.Add(pts);
                result.LineColors.Add(col);
                result.LineSectionHints.Add(hint);
            }

            // ── Slabs ────────────────────────────────────────────────
            for (int i = 0; i < original.Slabs.Count; i++)
            {
                var color = i < original.SlabColors.Count
                    ? original.SlabColors[i] : ((byte)0, (byte)0, (byte)0);
                string type = TypeFor(i, "slab", color, "Slab");

                if (string.Equals(type, "Column", StringComparison.OrdinalIgnoreCase))
                {
                    var pts = original.Slabs[i];
                    double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
                    double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
                    double w = maxX - minX, d = maxY - minY;
                    double minDim = Math.Min(w, d), maxDim = Math.Max(w, d);

                    // Guardrail: a "column" produced from a large or elongated
                    // slab polygon (e.g. a 2.8m x 9.7m core wall) creates an
                    // absurd point section in SAFE. Auto-route those to the
                    // Wall path (centerline + beam section from the polygon's
                    // minor dim) so they still export as visible, structurally
                    // meaningful frame elements.
                    const double columnMaxSideMm = 2000.0;
                    const double columnMaxAspect = 2.5;
                    bool columnSectionIsSane =
                        maxDim <= columnMaxSideMm &&
                        (minDim <= 0 || maxDim <= columnMaxAspect * minDim);

                    if (!columnSectionIsSane)
                    {
                        AddLineFromWallReduction(pts, color, AddLine);
                    }
                    else
                    {
                        result.Columns.Add(PolygonProcessor.Centroid(pts));
                        result.ColumnColors.Add(color);
                        result.ColumnSizes.Add((w, d));
                    }
                }
                else if (string.Equals(type, "Wall", StringComparison.OrdinalIgnoreCase))
                {
                    // Explicit wall: reduce polygon to centerline + beam section
                    // from the minor bbox dim.
                    AddLineFromWallReduction(original.Slabs[i], color, AddLine);
                }
                else if (string.Equals(type, "Beam", StringComparison.OrdinalIgnoreCase))
                {
                    // Plain beam: preserve the polygon as a polyline (no section
                    // hint — BeamSectionParser may still match a text callout).
                    AddLine(original.Slabs[i], color, null);
                }
                else if (string.Equals(type, "Opening", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(type, "Ignore",  StringComparison.OrdinalIgnoreCase))
                {
                    // Explicitly suppressed — do not include in output.
                }
                else
                {
                    result.Slabs.Add(original.Slabs[i]);
                    result.SlabColors.Add(color);
                }
            }

            // ── Lines ────────────────────────────────────────────────
            // Collect slab-typed lines per color for chain assembly.
            var slabLinesByColor = new Dictionary<(byte R, byte G, byte B),
                List<List<(double X, double Y)>>>();

            for (int i = 0; i < original.Lines.Count; i++)
            {
                var color = i < original.LineColors.Count
                    ? original.LineColors[i] : ((byte)0, (byte)0, (byte)0);
                string type = TypeFor(i, "line", color, "Beam");

                if (string.Equals(type, "Column", StringComparison.OrdinalIgnoreCase))
                {
                    var pts = original.Lines[i];
                    var midpoint = PolygonProcessor.Centroid(pts);
                    double len = PolygonProcessor.PathLength(pts);
                    double estSize = Math.Max(len, 500);
                    result.Columns.Add(midpoint);
                    result.ColumnColors.Add(color);
                    result.ColumnSizes.Add((estSize, estSize));
                }
                else if (string.Equals(type, "Slab", StringComparison.OrdinalIgnoreCase))
                {
                    if (!slabLinesByColor.TryGetValue(color, out var list))
                    {
                        list = new List<List<(double X, double Y)>>();
                        slabLinesByColor[color] = list;
                    }
                    list.Add(original.Lines[i]);
                }
                else if (string.Equals(type, "Opening", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(type, "Ignore",  StringComparison.OrdinalIgnoreCase))
                {
                    // Explicitly suppressed — do not include in output.
                }
                else
                {
                    // Pass through a line's own hint if the source had one
                    // (chain-assembly orphans won't have any; explicit lines
                    // from extraction don't either).
                    var hint = i < original.LineSectionHints.Count
                        ? original.LineSectionHints[i] : null;
                    AddLine(original.Lines[i], color, hint);
                }
            }

            // Chain assembly: connect slab-typed line segments into closed polygons.
            foreach (var (color, segments) in slabLinesByColor)
            {
                var (closedPolygons, orphanSegments) = ChainLinesIntoSlabs(segments);
                foreach (var poly in closedPolygons)
                {
                    result.Slabs.Add(poly);
                    result.SlabColors.Add(color);
                }
                foreach (var seg in orphanSegments)
                {
                    // Orphan with ≥3 distinct points → user drew this region to
                    // flag as a slab (typical case: balcony / cantilever extension
                    // whose polyline doesn't chain into the main perimeter).
                    // Treat it as its own slab polygon — SAFE auto-closes the
                    // last→first edge on import. Two-point orphans can't form a
                    // polygon, so they stay as beam-style lines.
                    if (seg.Count >= 3 && PolygonProcessor.Distance(seg[0], seg[^1]) > 1.0)
                    {
                        result.Slabs.Add(seg);
                        result.SlabColors.Add(color);
                    }
                    else
                    {
                        AddLine(seg, color, null);
                    }
                }
            }

            // ── Columns ──────────────────────────────────────────────
            for (int i = 0; i < original.Columns.Count; i++)
            {
                var color = i < original.ColumnColors.Count
                    ? original.ColumnColors[i] : ((byte)0, (byte)0, (byte)0);
                string colType = TypeFor(i, "column", color, "Column");
                if (string.Equals(colType, "Opening", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(colType, "Ignore",  StringComparison.OrdinalIgnoreCase))
                    continue;
                // Cannot convert a point to polygon or polyline — keep as column
                result.Columns.Add(original.Columns[i]);
                result.ColumnColors.Add(color);
                result.ColumnSizes.Add(i < original.ColumnSizes.Count
                    ? original.ColumnSizes[i] : (0, 0));
            }

            return result;
        }

        /// <summary>
        /// Reduces a wall-shaped slab polygon to a 2-point centerline line
        /// with an associated beam section hint, and invokes the supplied
        /// <paramref name="addLine"/> callback with both.
        /// </summary>
        private static void AddLineFromWallReduction(
            List<(double X, double Y)> polygon,
            (byte R, byte G, byte B) color,
            Action<List<(double X, double Y)>, (byte R, byte G, byte B), (double WidthMm, double DepthMm)?> addLine)
        {
            var (start, end, wallThicknessMm, sectionDepthMm) =
                PolygonProcessor.ReducePolygonToWallCenterline(polygon);
            var centerline = new List<(double X, double Y)> { start, end };
            addLine(centerline, color, (wallThicknessMm, sectionDepthMm));
        }

        /// <summary>
        /// Chains line segments into closed polygons (slabs) by matching endpoints.
        /// Returns (closedPolygons, orphanSegments) — orphans stay as lines.
        /// </summary>
        private static (List<List<(double X, double Y)>> Closed, List<List<(double X, double Y)>> Orphans)
            ChainLinesIntoSlabs(List<List<(double X, double Y)>> segments)
        {
            const double tolerance = 1.0; // mm

            static long Key(double v) => (long)Math.Round(v);
            static (long, long) PtKey((double X, double Y) p) => (Key(p.X), Key(p.Y));

            // Each segment has a start and end point. Build adjacency.
            // adjacency[roundedPoint] → list of (segmentIndex, isStart)
            var adjacency = new Dictionary<(long, long), List<(int Idx, bool IsStart)>>();
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                if (seg.Count < 2) continue;
                var startKey = PtKey(seg[0]);
                var endKey = PtKey(seg[^1]);

                if (!adjacency.TryGetValue(startKey, out var startList))
                {
                    startList = new List<(int, bool)>();
                    adjacency[startKey] = startList;
                }
                startList.Add((i, true));

                if (!adjacency.TryGetValue(endKey, out var endList))
                {
                    endList = new List<(int, bool)>();
                    adjacency[endKey] = endList;
                }
                endList.Add((i, false));
            }

            var visited = new HashSet<int>();
            var closedPolygons = new List<List<(double X, double Y)>>();
            var orphanSegments = new List<List<(double X, double Y)>>();

            for (int seed = 0; seed < segments.Count; seed++)
            {
                if (visited.Contains(seed) || segments[seed].Count < 2) continue;
                visited.Add(seed);

                // Build chain forward from seed's end, backward from seed's start
                var chain = new List<List<(double X, double Y)>> { segments[seed] };

                // Walk forward: from the end of the last segment in chain
                while (true)
                {
                    var lastSeg = chain[^1];
                    var tipKey = PtKey(lastSeg[^1]);
                    if (!adjacency.TryGetValue(tipKey, out var neighbors)) break;

                    int found = -1;
                    bool foundIsStart = false;
                    foreach (var (idx, isStart) in neighbors)
                    {
                        if (visited.Contains(idx)) continue;
                        // Check actual distance (not just rounded key)
                        var candidatePt = isStart ? segments[idx][0] : segments[idx][^1];
                        if (PolygonProcessor.Distance(lastSeg[^1], candidatePt) <= tolerance)
                        {
                            found = idx;
                            foundIsStart = isStart;
                            break;
                        }
                    }
                    if (found < 0) break;

                    visited.Add(found);
                    var nextSeg = segments[found];
                    if (!foundIsStart)
                    {
                        // The matched point is the end → reverse the segment so we walk start→end
                        nextSeg = new List<(double X, double Y)>(nextSeg);
                        nextSeg.Reverse();
                    }
                    chain.Add(nextSeg);
                }

                // Walk backward: from the start of the first segment in chain
                while (true)
                {
                    var firstSeg = chain[0];
                    var tipKey = PtKey(firstSeg[0]);
                    if (!adjacency.TryGetValue(tipKey, out var neighbors)) break;

                    int found = -1;
                    bool foundIsStart = false;
                    foreach (var (idx, isStart) in neighbors)
                    {
                        if (visited.Contains(idx)) continue;
                        var candidatePt = isStart ? segments[idx][0] : segments[idx][^1];
                        if (PolygonProcessor.Distance(firstSeg[0], candidatePt) <= tolerance)
                        {
                            found = idx;
                            foundIsStart = isStart;
                            break;
                        }
                    }
                    if (found < 0) break;

                    visited.Add(found);
                    var prevSeg = segments[found];
                    if (foundIsStart)
                    {
                        // Matched point is start → reverse so the end connects to our start
                        prevSeg = new List<(double X, double Y)>(prevSeg);
                        prevSeg.Reverse();
                    }
                    chain.Insert(0, prevSeg);
                }

                // Assemble chain vertices (skip duplicate junction points)
                var polygon = new List<(double X, double Y)>();
                for (int c = 0; c < chain.Count; c++)
                {
                    var seg = chain[c];
                    int startIdx = (c == 0) ? 0 : 1; // skip first point of subsequent segments (duplicate of previous end)
                    for (int p = startIdx; p < seg.Count; p++)
                        polygon.Add(seg[p]);
                }

                // Check if closed: last point ≈ first point
                bool isClosed = polygon.Count >= 3 &&
                    PolygonProcessor.Distance(polygon[0], polygon[^1]) <= tolerance;

                if (isClosed)
                {
                    // Remove the duplicate closing point
                    polygon.RemoveAt(polygon.Count - 1);
                    closedPolygons.Add(polygon);
                }
                else
                {
                    // Return original segments (not the assembled chain) as orphans
                    foreach (var seg in chain)
                    {
                        // Find and return the original segment
                        orphanSegments.Add(seg);
                    }
                }
            }

            return (closedPolygons, orphanSegments);
        }
    }
}

