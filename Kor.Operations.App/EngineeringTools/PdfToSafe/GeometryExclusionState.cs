#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal sealed class GeometryExclusionState
    {
        public HashSet<int> Slabs { get; } = new();
        public HashSet<int> Lines { get; } = new();
        public HashSet<int> Columns { get; } = new();
        public HashSet<(byte R, byte G, byte B)> Colors { get; } = new();

        // Per-element type overrides (right-click on preview shape).
        // Key = element index in the original ExtractedGeometry list.
        // Value = target type string ("Slab", "Beam", "Column", "Ignore").
        public Dictionary<int, string> SlabTypeOverrides { get; } = new();
        public Dictionary<int, string> LineTypeOverrides { get; } = new();
        public Dictionary<int, string> ColumnTypeOverrides { get; } = new();

        // Per-element section/thickness overrides written by the AI vision pass.
        // Key = element index in the original ExtractedGeometry list.
        // Take priority over text-annotation matches in AnnotationResolver during export.
        public Dictionary<int, double> SlabThicknessOverridesMm { get; } = new();
        public Dictionary<int, (double WidthMm, double DepthMm)> ColumnSectionOverridesMm { get; } = new();
        public Dictionary<int, (double WidthMm, double DepthMm)> LineSectionOverridesMm { get; } = new();

        public void Clear()
        {
            Slabs.Clear(); Lines.Clear(); Columns.Clear(); Colors.Clear();
            SlabTypeOverrides.Clear(); LineTypeOverrides.Clear(); ColumnTypeOverrides.Clear();
            SlabThicknessOverridesMm.Clear(); ColumnSectionOverridesMm.Clear(); LineSectionOverridesMm.Clear();
        }

        public bool HasIndexExclusions => Slabs.Count > 0 || Lines.Count > 0 || Columns.Count > 0;

        public bool IsSlabExcluded(int i, IReadOnlyList<(byte R, byte G, byte B)> slabColors)
            => Slabs.Contains(i) || (slabColors.Count > i && Colors.Contains(slabColors[i]));

        public bool IsLineExcluded(int i, IReadOnlyList<(byte R, byte G, byte B)> lineColors)
            => Lines.Contains(i) || (lineColors.Count > i && Colors.Contains(lineColors[i]));

        public bool IsColumnExcluded(int i, IReadOnlyList<(byte R, byte G, byte B)> columnColors)
            => Columns.Contains(i) || (columnColors.Count > i && Colors.Contains(columnColors[i]));

        /// <summary>
        /// Returns a copy of <paramref name="geometry"/> with elements whose color is in
        /// <see cref="Colors"/> removed. Returns the original instance unchanged if Colors is empty.
        /// </summary>
        public ExtractedGeometry FilterGeometry(ExtractedGeometry geometry)
        {
            if (Colors.Count == 0)
                return geometry;

            var filtered = new ExtractedGeometry
            {
                PageWidthPts = geometry.PageWidthPts,
                PageHeightPts = geometry.PageHeightPts,
                ScaleDenominator = geometry.ScaleDenominator,
                PageCount = geometry.PageCount,
                RawPathCount = geometry.RawPathCount,
                IsVectorPdf = geometry.IsVectorPdf
            };

            for (int i = 0; i < geometry.Slabs.Count; i++)
            {
                var color = i < geometry.SlabColors.Count ? geometry.SlabColors[i] : ((byte)255, (byte)255, (byte)255);
                if (Colors.Contains(color)) continue;
                filtered.Slabs.Add(geometry.Slabs[i]);
                filtered.SlabColors.Add(color);
            }
            for (int i = 0; i < geometry.Lines.Count; i++)
            {
                var color = i < geometry.LineColors.Count ? geometry.LineColors[i] : ((byte)0, (byte)0, (byte)0);
                if (Colors.Contains(color)) continue;
                filtered.Lines.Add(geometry.Lines[i]);
                filtered.LineColors.Add(color);
                filtered.LineSectionHints.Add(
                    i < geometry.LineSectionHints.Count ? geometry.LineSectionHints[i] : null);
            }
            for (int i = 0; i < geometry.Columns.Count; i++)
            {
                var color = i < geometry.ColumnColors.Count ? geometry.ColumnColors[i] : ((byte)0, (byte)0, (byte)0);
                if (Colors.Contains(color)) continue;
                filtered.Columns.Add(geometry.Columns[i]);
                filtered.ColumnColors.Add(color);
            }
            return filtered;
        }
    }
}
