#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal sealed record ExtractionParams(
        string FilePath,
        int Scale,
        double SlabMinDiagonalMm,
        double LineMinLengthMm,
        bool ExcludeGridLines);

    internal static class GeometryExportOrchestrator
    {
        public static async Task<List<(ExtractedGeometry Geom, string StoryName, double ElevationMm)>>
            BuildStoryGeometriesAsync(
                ExtractionParams p,
                IReadOnlyList<StoryDefinition> stories,
                GeometryExclusionState exclusions,
                Action<string>? onProgress = null)
        {
            var result = new List<(ExtractedGeometry, string, double)>();
            for (int i = 0; i < stories.Count; i++)
            {
                var story = stories[i];
                int page = Math.Max(1, story.PageNumber);
                onProgress?.Invoke($"Extracting story {i + 1}/{stories.Count}...");
                var extracted = await Task.Run(() =>
                    PdfGeometryExtractor.Extract(p.FilePath, p.Scale, page,
                        p.SlabMinDiagonalMm, p.LineMinLengthMm, p.ExcludeGridLines));
                result.Add((exclusions.FilterGeometry(extracted), story.Name, story.ElevationMm));
            }
            return result;
        }

        public static async Task<ExtractedGeometry> GetExportGeometryAsync(
            ExtractionParams p,
            int pageNumber,
            ExtractedGeometry cached,
            bool hasIndexExclusions)
        {
            if (hasIndexExclusions)
                return cached;

            return await Task.Run(() =>
                PdfGeometryExtractor.Extract(p.FilePath, p.Scale, pageNumber,
                    p.SlabMinDiagonalMm, p.LineMinLengthMm, p.ExcludeGridLines));
        }

        public static int CountVisibleSlabs(ExtractedGeometry geom, GeometryExclusionState excl)
            => Enumerable.Range(0, geom.Slabs.Count)
                .Count(i => !excl.Slabs.Contains(i) &&
                            !(i < geom.SlabColors.Count && excl.Colors.Contains(geom.SlabColors[i])));

        public static int CountVisibleLines(ExtractedGeometry geom, GeometryExclusionState excl)
            => Enumerable.Range(0, geom.Lines.Count)
                .Count(i => !excl.Lines.Contains(i) &&
                            !(i < geom.LineColors.Count && excl.Colors.Contains(geom.LineColors[i])));

        public static int CountVisibleColumns(ExtractedGeometry geom, GeometryExclusionState excl)
            => Enumerable.Range(0, geom.Columns.Count)
                .Count(i => !excl.Columns.Contains(i) &&
                            !(i < geom.ColumnColors.Count && excl.Colors.Contains(geom.ColumnColors[i])));

        public static int EstimateVisiblePointCount(ExtractedGeometry geom, GeometryExclusionState excl)
        {
            var pts = new HashSet<(long X, long Y)>();
            for (int i = 0; i < geom.Slabs.Count; i++)
            {
                if (excl.Slabs.Contains(i)) continue;
                if (i < geom.SlabColors.Count && excl.Colors.Contains(geom.SlabColors[i])) continue;
                foreach (var p in geom.Slabs[i])
                    pts.Add(((long)Math.Round(p.X * 10), (long)Math.Round(p.Y * 10)));
            }
            for (int i = 0; i < geom.Lines.Count; i++)
            {
                if (excl.Lines.Contains(i)) continue;
                if (i < geom.LineColors.Count && excl.Colors.Contains(geom.LineColors[i])) continue;
                foreach (var p in geom.Lines[i])
                    pts.Add(((long)Math.Round(p.X * 10), (long)Math.Round(p.Y * 10)));
            }
            for (int i = 0; i < geom.Columns.Count; i++)
            {
                if (excl.Columns.Contains(i)) continue;
                if (i < geom.ColumnColors.Count && excl.Colors.Contains(geom.ColumnColors[i])) continue;
                var pt = geom.Columns[i];
                pts.Add(((long)Math.Round(pt.X * 10), (long)Math.Round(pt.Y * 10)));
            }
            return pts.Count;
        }
    }
}
