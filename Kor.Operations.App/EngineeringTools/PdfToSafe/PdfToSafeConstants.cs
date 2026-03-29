namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal static class PdfToSafeConstants
    {
        // Geometry processing
        public const double PointsToMm = 25.4 / 72.0;
        public const double MinVertexDistanceMm = 0.5;
        public const double DouglasPeuckerEpsilonMm = 5.0;
        public const double MinTriangleAreaMm2 = 100.0;
        public const double DefaultSlabMinDiagonalMm = 1000.0;
        public const double DefaultLineMinLengthMm = 200.0;
        public const double DefaultThicknessMm = 200.0;

        // Preview rendering
        public const int PreviewBitmapWidth = 1800;
        public const int BezierSegments = 8;

        // AI service
        public const int AiHttpTimeoutSeconds = 90;
        public const int AiMaxTokens = 512;

        // Export defaults
        public const string DefaultGradeCode = "C30";
        public const string DefaultConcreteGrade = "C30";
    }
}
