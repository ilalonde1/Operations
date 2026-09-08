#nullable enable
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// Read one page of a drawing PDF and classify its geometry into slabs, columns and lines.
    /// </summary>
    /// <remarks>
    /// This is the stage that existed only inside the WPF app until 2 September. The reading was
    /// already shared — <see cref="VectorPageReader"/> in this project — but the step that turns
    /// paths into structure, and the DXF writer after it, were reachable only by clicking through a
    /// dialog. So a job whose drawings arrive as a PDF could not be taken off from a script.
    ///
    /// ⚠ THE FLAG THAT MATTERS IS <paramref name="annotationsOnly"/>. It defaults to true, because
    /// PdfToSafe was built for the Bluebeam workflow where the engineer's redlines ARE the model
    /// and the architect's base drawing underneath is noise. In that mode page content is skipped
    /// entirely.
    ///
    /// Until this class existed no caller anywhere passed it — two mentions in the whole solution,
    /// its declaration and its use — so a CLEAN ISSUED DRAWING SET, which carries no markup at all,
    /// read as completely empty and said nothing about why. 31130-01's stick file is that case: all
    /// 60 pages carry vector geometry, 326 to 19,520 subpaths each, and all 60 returned nothing.
    ///
    /// Passing false reads the drawing itself. On 31130's S2.01.2 at 1:96 that gives 52 columns
    /// whose sizes match the PARKADE COLUMN SCHEDULE printed on the same sheet — 12x24 (PC1) 17
    /// times, 24x24 (PC2) 15, 24x30 (PC5) 9.
    ///
    /// WHAT THIS DOES NOT DO: it does not filter out the sheet border, the title block or the
    /// schedule tables, which are page content too and classify as slabs and beams. A caller
    /// wanting only the plan must window the result. Nor does it pick the scale — pass it, or ask
    /// <see cref="SheetScaleReader"/>.
    /// </remarks>
    public static class PdfPlanReader
    {
        /// <summary>Turn the page's subpaths into mm-space geometry, before classification.</summary>
        public static List<RawSubpath> ParsePage(Page page, double scale)
            => ParsePage(page, scale, out _);

        /// <summary>The same, keeping the page read so its text and rules can say where the furniture is.</summary>
        /// <param name="keptSubpathOrdinals">See <see cref="VectorPageReader.ReadPage(Page, bool, int, double?, double?, IList{int}?)"/>: which subpaths survived this read's thinning.</param>
        public static List<RawSubpath> ParsePage(Page page, double scale, out VectorPageReader.PageContent pageRead, IList<int>? keptSubpathOrdinals = null)
        {
            double? minPointDistance = scale > 0
                ? PdfToSafeConstants.MinVertexDistanceMm / scale
                : null;
            double? closeDistance = scale > 0
                ? PdfToSafeConstants.MinVertexDistanceMm * 4.0 / scale
                : null;

            pageRead = VectorPageReader.ReadPage(
                page,
                includeAnnotations: true,
                curveSegments: PdfToSafeConstants.BezierSegments,
                minPointDistance: minPointDistance,
                closeDistance: closeDistance,
                keptSubpathOrdinals: keptSubpathOrdinals);

            return pageRead.Paths
                .Select(path => new RawSubpath(
                    path.Points.Select(p => (p.X * scale, p.Y * scale)).ToList(),
                    path.IsClosed,
                    path.Color,
                    path.IsFilled,
                    path.IsStroked,
                    path.LineWidth,
                    path.IsAnnotation))
                .ToList();
        }

        /// <param name="annotationsOnly">
        /// true (the default, and what the app has always done): only Bluebeam markup counts as
        /// structure. false: read the drawing's own linework. See the remarks on this class —
        /// a clean issued set needs false or it reads as empty.
        /// </param>
        public static ExtractedGeometry Read(
            string filePath,
            int    scaleDenominator,
            int    pageNumber        = 1,
            bool   annotationsOnly   = true,
            double slabMinDiagonalMm = PdfToSafeConstants.DefaultSlabMinDiagonalMm,
            double lineMinLengthMm   = PdfToSafeConstants.DefaultLineMinLengthMm,
            bool   excludeGridLines  = false)
        {
            using var doc = PdfDocument.Open(filePath);
            return Read(doc, scaleDenominator, pageNumber, annotationsOnly,
                        slabMinDiagonalMm, lineMinLengthMm, excludeGridLines);
        }

        /// <summary>
        /// Read a page with the numbers this JOB states, rather than the ones this build was written
        /// with. See <see cref="PdfIntakeOptions"/>.
        /// </summary>
        /// <remarks>
        /// ⚠ <paramref name="options"/> comes BEFORE <paramref name="annotationsOnly"/> so the flag
        /// keeps its default. It defaults to true on every overload, deliberately — see the remarks
        /// on this class — and a new overload that made it a required positional argument quietly
        /// dropped that guarantee. ThePlanReaderForwardsTheFlagItWasGivenTests caught it.
        /// </remarks>
        public static ExtractedGeometry Read(
            string filePath,
            int    scaleDenominator,
            int    pageNumber,
            PdfIntakeOptions options,
            bool   annotationsOnly  = true,
            bool   excludeGridLines = false)
        {
            ArgumentNullException.ThrowIfNull(options);
            using var doc = PdfDocument.Open(filePath);
            return Read(doc, scaleDenominator, pageNumber, options, annotationsOnly, excludeGridLines);
        }

        /// <summary>Overload for a document already open, so a sweep parses once.</summary>
        public static ExtractedGeometry Read(
            PdfDocument doc,
            int    scaleDenominator,
            int    pageNumber,
            PdfIntakeOptions options,
            bool   annotationsOnly  = true,
            bool   excludeGridLines = false)
        {
            ArgumentNullException.ThrowIfNull(doc);
            ArgumentNullException.ThrowIfNull(options);

            var result = new ExtractedGeometry { ScaleDenominator = scaleDenominator };
            double scale = scaleDenominator * PdfToSafeConstants.PointsToMm;

            var page = doc.GetPage(pageNumber);
            result.PageWidthPts  = page.Width;
            result.PageHeightPts = page.Height;
            result.PageCount     = doc.NumberOfPages;

            var rawSubpaths = ParsePage(page, scale, out var pageRead);
            result.RawPathCount = rawSubpaths.Count;

            int meaningfulCount = rawSubpaths.Count(s =>
                s.Points.Count > 3 ||
                (s.IsClosed && GeometryFilterService.BoundingBoxDiagonal(s.Points) > 10.0));
            result.IsVectorPdf = meaningfulCount >= 5;

            if (rawSubpaths.Count == 0) return result;

            var footingPieces = ReadFootings(rawSubpaths, pageRead, result, annotationsOnly);
            GeometryFilterService.Classify(rawSubpaths, result,
                options.SlabMinDiagonalMm, options.LineMinLengthMm, excludeGridLines,
                result.PageWidthPts * scale, result.PageHeightPts * scale,
                annotationsOnly,
                options.ColumnMaxSizeMm, options.ColumnMinDimMm, options.ColumnMaxAspect,
                Furniture(pageRead, scale, options.AgreementToleranceMm),
                minWallThicknessMm: options.MinWallThicknessMm, maxWallThicknessMm: options.MaxWallThicknessMm,
                minWallLengthMm: options.MinWallLengthMm, minWallAspect: options.MinWallAspect,
                footingPieces: footingPieces);

            return result;
        }

        /// <summary>The sheet's furniture, grid and declared column sizes, in the mm space the subpaths are in.</summary>
        private static SheetFurniture.Set Furniture(VectorPageReader.PageContent pageRead, double scale, double sizeToleranceMm = PlanAgreesWithItsSchedule.DefaultToleranceMm)
            => SheetFurniture.On(pageRead, sizeToleranceMm).Scaled(scale);

        /// <summary>Overload for a document already open, so a sweep pays the parse cost once.</summary>
        public static ExtractedGeometry Read(
            PdfDocument doc,
            int    scaleDenominator,
            int    pageNumber        = 1,
            bool   annotationsOnly   = true,
            double slabMinDiagonalMm = PdfToSafeConstants.DefaultSlabMinDiagonalMm,
            double lineMinLengthMm   = PdfToSafeConstants.DefaultLineMinLengthMm,
            bool   excludeGridLines  = false)
        {
            var result = new ExtractedGeometry { ScaleDenominator = scaleDenominator };
            double scale = scaleDenominator * PdfToSafeConstants.PointsToMm;

            var page = doc.GetPage(pageNumber);
            result.PageWidthPts  = page.Width;
            result.PageHeightPts = page.Height;
            result.PageCount     = doc.NumberOfPages;

            var rawSubpaths = ParsePage(page, scale, out var pageRead);
            result.RawPathCount = rawSubpaths.Count;

            int meaningfulCount = rawSubpaths.Count(s =>
                s.Points.Count > 3 ||
                (s.IsClosed && GeometryFilterService.BoundingBoxDiagonal(s.Points) > 10.0));
            result.IsVectorPdf = meaningfulCount >= 5;

            if (rawSubpaths.Count == 0) return result;

            var footingPieces = ReadFootings(rawSubpaths, pageRead, result, annotationsOnly);
            GeometryFilterService.Classify(rawSubpaths, result,
                slabMinDiagonalMm, lineMinLengthMm, excludeGridLines,
                result.PageWidthPts * scale, result.PageHeightPts * scale,
                annotationsOnly,
                furniture: Furniture(pageRead, scale),
                footingPieces: footingPieces);

            return result;
        }

        /// <summary>
        /// The footings the page's dashed outlines close against its FOUNDATION SCHEDULE, added to the
        /// result, and the raw-path indices of their dashes so the classifier records them as such.
        /// Not in markup-only mode, where page content is not read. Null when the sheet schedules no
        /// spread footing.
        /// </summary>
        public static IReadOnlyDictionary<int, int>? ReadFootings(
            IReadOnlyList<RawSubpath> rawSubpaths, VectorPageReader.PageContent pageRead, ExtractedGeometry result, bool annotationsOnly)
        {
            if (annotationsOnly) return null;
            IReadOnlyList<FootingScheduleReader.FootingType> types;
            try { types = FootingScheduleReader.ReadSchedule(pageRead).Types; } catch { return null; }
            if (types.Count == 0) return null;
            var (footings, pieces) = Intake.FootingOutlines.Read(rawSubpaths, types);
            result.Footings.AddRange(footings);
            return pieces.Count > 0 ? pieces : null;
        }

        /// <summary>How many of this page's subpaths came from markup rather than the drawing.</summary>
        /// <remarks>
        /// The one number that explains an empty result: zero here means markup-only mode can
        /// never return anything for this page, however good the drawing is.
        /// </remarks>
        public static int AnnotationCount(string filePath, int pageNumber, int scaleDenominator)
        {
            using var doc = PdfDocument.Open(filePath);
            return ParsePage(doc.GetPage(pageNumber),
                             scaleDenominator * PdfToSafeConstants.PointsToMm)
                   .Count(p => p.IsAnnotation);
        }
    }
}
