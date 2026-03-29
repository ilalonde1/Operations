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
            var result = new ExtractedGeometry();
            result.ScaleDenominator = scaleDenominator;
            double scale = scaleDenominator * PdfToSafeConstants.PointsToMm;

            using var doc = PdfDocument.Open(filePath);
            var page = doc.GetPage(pageNumber);
            result.PageWidthPts  = page.Width;
            result.PageHeightPts = page.Height;
            result.PageCount     = doc.NumberOfPages;

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
            string? loadCombCode = null)
            => SafeF2kExporter.Export(geometry, outputPath, excludedSlabs, excludedLines, excludedColumns, excludedColors, colorSettings, loadCombCode);
        public static void ExportF2k(
            string outputPath,
            IReadOnlyList<(ExtractedGeometry Geom, string StoryName, double ElevationMm)> stories,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings = null,
            string? loadCombCode = null)
            => SafeF2kExporter.Export(outputPath, stories, colorSettings, loadCombCode);
        public static void ExportE2k(
            string outputPath,
            ExtractedGeometry geom,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings = null)
            => EtabsE2kExporter.Export(outputPath, geom, colorSettings);

        public static void ExportE2k(
            string outputPath,
            IReadOnlyList<(ExtractedGeometry Geom, string StoryName, double ElevationMm)> stories,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings = null)
            => EtabsE2kExporter.Export(outputPath, stories, colorSettings);

        // ── helpers ──────────────────────────────────────────────────────────

    }
}

