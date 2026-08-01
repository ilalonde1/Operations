#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// Layers AI-supplied per-element section/thickness overrides on top of the
    /// text-annotation matches produced by <see cref="AnnotationResolver"/>.
    /// Override priority: AI override > text annotation > color-level default.
    /// Returns a new <see cref="AnnotationResolution"/>; the input is unchanged.
    /// </summary>
    internal static class AnnotationOverrideMerger
    {
        public static AnnotationResolution Merge(
            AnnotationResolution textBased,
            IReadOnlyDictionary<int, double> slabThicknessOverridesMm,
            IReadOnlyDictionary<int, (double WidthMm, double DepthMm)> columnSectionOverridesMm,
            IReadOnlyDictionary<int, (double WidthMm, double DepthMm)> lineSectionOverridesMm)
        {
            var slab = (double?[])textBased.SlabThicknessMm.Clone();
            for (int i = 0; i < slab.Length; i++)
                if (slabThicknessOverridesMm.TryGetValue(i, out var v)) slab[i] = v;

            var col = ((double WidthMm, double DepthMm)?[])textBased.ColumnSectionMm.Clone();
            for (int i = 0; i < col.Length; i++)
                if (columnSectionOverridesMm.TryGetValue(i, out var v)) col[i] = v;

            var line = ((double WidthMm, double DepthMm)?[])textBased.LineSectionMm.Clone();
            for (int i = 0; i < line.Length; i++)
                if (lineSectionOverridesMm.TryGetValue(i, out var v)) line[i] = v;

            return new AnnotationResolution(slab, col, line);
        }
    }
}
