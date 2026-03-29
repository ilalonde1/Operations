#nullable enable
using System;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal sealed class CoordinateTransformer
    {
        private readonly double _mmToCanvas;
        private readonly double _pageH;
        private readonly double _ptsPerMm;

        public CoordinateTransformer(double canvasWidth, double pageWidthPts, double pageHeightPts, int scaleDenominator)
        {
            _ptsPerMm = 1.0 / (scaleDenominator * PdfToSafeConstants.PointsToMm);
            _mmToCanvas = _ptsPerMm * (canvasWidth / pageWidthPts);
            _pageH = pageHeightPts;
        }

        public (double X, double Y) ToCanvas(double xMm, double yMm) =>
            (xMm * _mmToCanvas,
             (_pageH - yMm * _ptsPerMm) * (_mmToCanvas / _ptsPerMm));

        public static int SuggestScale(double pixelDist, double canvasWidth, double pageWidthPts, double knownMm)
        {
            double linePts = (pixelDist / canvasWidth) * pageWidthPts;
            double raw = knownMm / (linePts * PdfToSafeConstants.PointsToMm);
            return Math.Max(1, (int)Math.Round(raw));
        }
    }
}
