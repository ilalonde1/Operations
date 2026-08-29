#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// Parallel-indexed text-annotation matches for each bucket in an
    /// <see cref="ExtractedGeometry"/>. Null entries mean "no nearby text
    /// callout matched". All values are in millimetres.
    /// </summary>
    internal sealed record AnnotationResolution(
        double?[] SlabThicknessMm,
        (double WidthMm, double DepthMm)?[] ColumnSectionMm,
        (double WidthMm, double DepthMm)?[] LineSectionMm);

    /// <summary>
    /// Thin façade over the three existing annotation parsers that returns
    /// the combined text-to-shape resolution in a single call. Keeps the
    /// parsers as the single source of truth (no duplication) and lets UI
    /// + AI + export consumers share identical results.
    /// </summary>
    internal static class AnnotationResolver
    {
        public static AnnotationResolution Resolve(ExtractedGeometry geo)
        {
            if (geo is null) throw new System.ArgumentNullException(nameof(geo));

            var annotations = geo.TextAnnotations
                .ConvertAll(a => (a.Text, a.X, a.Y));

            var slabThick = ThicknessAnnotationParser.AssignToSlabs(geo.Slabs, annotations);
            var colSec    = ColumnSectionParser.AssignToColumns(geo.Columns, annotations);
            var lineSec   = BeamSectionParser.AssignToLines(geo.Lines, annotations);

            return new AnnotationResolution(slabThick, colSec, lineSec);
        }
    }
}
