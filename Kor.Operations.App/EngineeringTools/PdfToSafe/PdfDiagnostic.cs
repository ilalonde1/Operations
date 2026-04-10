#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// Diagnostic utility to trace the full extraction pipeline for a PDF
    /// and dump results to a text file. Use to debug geometry issues.
    /// </summary>
    internal static class PdfDiagnostic
    {
        public static void TraceExtraction(string pdfPath, string outputPath, int scaleDenominator = 100, int pageNumber = 1)
        {
            using var sw = new StreamWriter(outputPath, false);
            sw.WriteLine($"=== PdfDiagnostic Trace ===");
            sw.WriteLine($"PDF: {pdfPath}");
            sw.WriteLine($"Scale: 1:{scaleDenominator}");
            sw.WriteLine($"Page: {pageNumber}");
            sw.WriteLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sw.WriteLine();

            // Step 1: Parse raw subpaths
            double scale = scaleDenominator * PdfToSafeConstants.PointsToMm;
            using var doc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
            var page = doc.GetPage(pageNumber);
            double pageWidthMm = page.Width * scale;
            double pageHeightMm = page.Height * scale;

            sw.WriteLine($"Page size: {page.Width:F1} x {page.Height:F1} pts = {pageWidthMm:F0} x {pageHeightMm:F0} mm");

            var rawSubpaths = PdfGeometryParser.ParsePage(page, scale);
            sw.WriteLine($"Raw subpaths: {rawSubpaths.Count}");
            sw.WriteLine();

            // Step 2: Categorize raw subpaths
            int annotCount = rawSubpaths.Count(s => s.IsAnnotation);
            int contentCount = rawSubpaths.Count - annotCount;
            int filledCount = rawSubpaths.Count(s => s.IsFilled);
            int strokedOnlyCount = rawSubpaths.Count(s => s.IsStroked && !s.IsFilled);
            int closedCount = rawSubpaths.Count(s => s.IsClosed);
            int openCount = rawSubpaths.Count - closedCount;

            sw.WriteLine("=== RAW SUBPATH BREAKDOWN ===");
            sw.WriteLine($"  Annotations: {annotCount}");
            sw.WriteLine($"  Page content: {contentCount}");
            sw.WriteLine($"  Filled: {filledCount}");
            sw.WriteLine($"  Stroked-only: {strokedOnlyCount}");
            sw.WriteLine($"  Closed: {closedCount}");
            sw.WriteLine($"  Open: {openCount}");
            sw.WriteLine();

            // Step 3: Color breakdown
            var colorGroups = rawSubpaths.GroupBy(s => s.Color)
                .OrderByDescending(g => g.Count())
                .ToList();
            sw.WriteLine($"=== COLORS ({colorGroups.Count} distinct) ===");
            foreach (var g in colorGroups)
            {
                var c = g.Key;
                int annot = g.Count(s => s.IsAnnotation);
                int content = g.Count() - annot;
                int filled = g.Count(s => s.IsFilled);
                int closed = g.Count(s => s.IsClosed);
                sw.WriteLine($"  ({c.R,3},{c.G,3},{c.B,3}) #{c.R:X2}{c.G:X2}{c.B:X2}: {g.Count(),5} total ({annot} annot, {content} content, {filled} filled, {closed} closed)");
            }
            sw.WriteLine();

            // Step 3b: Raw PdfPig annotation info (type + dictionary keys)
            sw.WriteLine("=== RAW PDFPIG ANNOTATIONS ===");
            foreach (var (ann, idx) in page.ExperimentalAccess.GetAnnotations().Select((a, i) => (a, i)))
            {
                try
                {
                    var d = ann.AnnotationDictionary;
                    string keys = string.Join(", ", d.Data.Keys);
                    bool hasVerts = d.Data.ContainsKey("Vertices");
                    string vertType = hasVerts ? d.Data["Vertices"].GetType().Name : "N/A";
                    sw.WriteLine($"  [{idx}] PdfPig type={ann.Type}, keys=[{keys}], hasVertices={hasVerts} ({vertType})");
                }
                catch (Exception ex) { sw.WriteLine($"  [{idx}] ERROR: {ex.Message}"); }
            }
            sw.WriteLine();

            // Step 4: Annotation details
            var annotations = rawSubpaths.Where(s => s.IsAnnotation).ToList();
            sw.WriteLine($"=== ANNOTATION DETAIL ({annotations.Count}) ===");
            var annotColorGroups = annotations.GroupBy(s => s.Color).OrderByDescending(g => g.Count());
            foreach (var g in annotColorGroups)
            {
                var c = g.Key;
                var sizes = g.Select(s =>
                {
                    double minX = s.Points.Min(p => p.X), maxX = s.Points.Max(p => p.X);
                    double minY = s.Points.Min(p => p.Y), maxY = s.Points.Max(p => p.Y);
                    return (W: maxX - minX, H: maxY - minY, Diag: GeometryFilterService.BoundingBoxDiagonal(s.Points));
                }).ToList();
                sw.WriteLine($"  ({c.R,3},{c.G,3},{c.B,3}): {g.Count()} shapes");
                sw.WriteLine($"    Closed: {g.Count(s => s.IsClosed)}, Open: {g.Count(s => !s.IsClosed)}");
                sw.WriteLine($"    Size range: {sizes.Min(s => s.Diag):F0}mm - {sizes.Max(s => s.Diag):F0}mm diagonal");
                sw.WriteLine($"    Width range: {sizes.Min(s => s.W):F0}mm - {sizes.Max(s => s.W):F0}mm");
                sw.WriteLine($"    Height range: {sizes.Min(s => s.H):F0}mm - {sizes.Max(s => s.H):F0}mm");
                // Show all individual shapes with coordinates
                foreach (var (s, idx) in g.Select((s, i) => (s, i)))
                {
                    double minX = s.Points.Min(p => p.X), maxX = s.Points.Max(p => p.X);
                    double minY = s.Points.Min(p => p.Y), maxY = s.Points.Max(p => p.Y);
                    double diag = GeometryFilterService.BoundingBoxDiagonal(s.Points);
                    string ptsStr = string.Join("; ", s.Points.Select(p => $"({p.X:F1},{p.Y:F1})"));
                    sw.WriteLine($"    [{idx}] {s.Points.Count} pts, {(s.IsClosed ? "closed" : "open")}, {(maxX - minX):F0}x{(maxY - minY):F0}mm, diag={diag:F0}mm, filled={s.IsFilled}");
                    sw.WriteLine($"         pts: {ptsStr}");
                }
            }
            sw.WriteLine();

            // Step 5: Run classification
            var result = new ExtractedGeometry();
            result.ScaleDenominator = scaleDenominator;
            result.PageWidthPts = page.Width;
            result.PageHeightPts = page.Height;
            GeometryFilterService.Classify(rawSubpaths, result,
                PdfToSafeConstants.DefaultSlabMinDiagonalMm,
                PdfToSafeConstants.DefaultLineMinLengthMm,
                false, pageWidthMm, pageHeightMm);

            sw.WriteLine("=== CLASSIFIED GEOMETRY ===");
            sw.WriteLine($"  Slabs: {result.Slabs.Count}");
            sw.WriteLine($"  Columns: {result.Columns.Count}");
            sw.WriteLine($"  Lines: {result.Lines.Count}");
            sw.WriteLine($"  Drop panel candidates: {result.DropPanelCandidates.Count}");
            sw.WriteLine();

            // Slab details
            if (result.Slabs.Count > 0)
            {
                sw.WriteLine("=== SLAB DETAIL ===");
                for (int i = 0; i < Math.Min(result.Slabs.Count, 20); i++)
                {
                    var pts = result.Slabs[i];
                    var c = result.SlabColors[i];
                    double diag = GeometryFilterService.BoundingBoxDiagonal(pts);
                    double area = PolygonProcessor.PolygonAreaMm2(pts);
                    sw.WriteLine($"  Slab[{i}]: ({c.R},{c.G},{c.B}) {pts.Count} pts, diag={diag:F0}mm, area={area:F0}mm²");
                }
                if (result.Slabs.Count > 20) sw.WriteLine($"  ... and {result.Slabs.Count - 20} more");
            }

            // Column details
            if (result.Columns.Count > 0)
            {
                sw.WriteLine("=== COLUMN DETAIL ===");
                var colColorGroups = Enumerable.Range(0, result.Columns.Count)
                    .GroupBy(i => result.ColumnColors[i])
                    .OrderByDescending(g => g.Count());
                foreach (var g in colColorGroups)
                {
                    var c = g.Key;
                    var colSizes = g.Select(i => result.ColumnSizes[i]).ToList();
                    sw.WriteLine($"  ({c.R},{c.G},{c.B}): {g.Count()} columns, size range {colSizes.Min(s => s.WidthMm):F0}x{colSizes.Min(s => s.DepthMm):F0} to {colSizes.Max(s => s.WidthMm):F0}x{colSizes.Max(s => s.DepthMm):F0}mm");
                }
            }

            // Line details
            if (result.Lines.Count > 0)
            {
                sw.WriteLine("=== LINE DETAIL ===");
                var lineColorGroups = Enumerable.Range(0, result.Lines.Count)
                    .GroupBy(i => result.LineColors[i])
                    .OrderByDescending(g => g.Count());
                foreach (var g in lineColorGroups)
                {
                    var c = g.Key;
                    var lengths = g.Select(i => PolygonProcessor.PathLength(result.Lines[i])).ToList();
                    sw.WriteLine($"  ({c.R},{c.G},{c.B}): {g.Count()} lines, length range {lengths.Min():F0}-{lengths.Max():F0}mm");
                }
            }

            sw.WriteLine();
            sw.WriteLine("=== DONE ===");
        }
    }
}
