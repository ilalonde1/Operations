#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal static class SafeF2kExporter
    {
        //  single-story 
        public static void Export(
            ExtractedGeometry geometry,
            string outputPath,
            HashSet<int>? excludedSlabs   = null,
            HashSet<int>? excludedLines   = null,
            HashSet<int>? excludedColumns = null,
            HashSet<(byte R, byte G, byte B)>? excludedColors = null,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings = null,
            ExportSettings? settings = null)
        {
            settings ??= new ExportSettings();
            var ic = CultureInfo.InvariantCulture;

            var (cx, cy) = F2kModelPrep.ComputeCentroid(new[] { geometry });
            if (cx == 0 && cy == 0 &&
                geometry.Slabs.Count == 0 &&
                geometry.Columns.Count == 0 &&
                geometry.Lines.Count == 0) return;

            var (xSlabs, xSlabColors, xLines, xColumns, xColumnBaseSizes, xDropCandidates) =
                F2kModelPrep.PrepareGeometry(geometry, cx, cy,
                    excludedSlabs, excludedLines, excludedColumns, excludedColors);

            var annotations = geometry.TextAnnotations
                .Select(a => (a.Text, a.X - cx, a.Y - cy)).ToList();

            var storyData = F2kModelPrep.BuildStoryModel(
                xSlabs, xSlabColors, xLines, xColumns, xColumnBaseSizes, xDropCandidates,
                annotations, colorSettings,
                idPrefix: "",
                elevationMm: 0,
                ic,
                settings.DropPanelThicknessMultiplier);

            var gridLines = StructuralGridGenerator.Generate(storyData.ColumnsForGrid);

            using var sw = new StreamWriter(outputPath, false, Encoding.ASCII);
            F2kWriter.WriteTables(sw, new[] { storyData }, colorSettings, settings, gridLines, ic);
        }

        //  multi-story 
        public static void Export(
            string outputPath,
            IReadOnlyList<(ExtractedGeometry Geom, string StoryName, double ElevationMm)> stories,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings = null,
            ExportSettings? settings = null)
        {
            if (stories.Count == 0) return;
            settings ??= new ExportSettings();
            var ic = CultureInfo.InvariantCulture;

            var (cx, cy) = F2kModelPrep.ComputeCentroid(
                System.Linq.Enumerable.Select(stories, s => s.Geom));

            var storyDataList = new List<F2kStoryData>(stories.Count);
            for (int i = 0; i < stories.Count; i++)
            {
                var (geom, _, elevationMm) = stories[i];
                string prefix = $"s{i + 1}_";

                var (xSlabs, xSlabColors, xLines, xColumns, xColumnBaseSizes, xDropCandidates) =
                    F2kModelPrep.PrepareGeometry(geom, cx, cy, null, null, null, null);

                var annotations = geom.TextAnnotations
                    .Select(a => (a.Text, a.X - cx, a.Y - cy)).ToList();

                storyDataList.Add(F2kModelPrep.BuildStoryModel(
                    xSlabs, xSlabColors, xLines, xColumns, xColumnBaseSizes, xDropCandidates,
                    annotations, colorSettings,
                    idPrefix: prefix,
                    elevationMm: elevationMm,
                    ic,
                    settings.DropPanelThicknessMultiplier));
            }

            var allColumns = System.Linq.Enumerable
                .SelectMany(storyDataList, s => s.ColumnsForGrid).ToList();
            var gridLines = StructuralGridGenerator.Generate(allColumns);

            using var sw = new StreamWriter(outputPath, false, Encoding.ASCII);
            F2kWriter.WriteTables(sw, storyDataList, colorSettings, settings, gridLines, ic);
        }
    }
}
