namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    public static class PdfToSafeConstants
    {
        // Geometry processing
        public const double PointsToMm = 25.4 / 72.0;
        public const double MinVertexDistanceMm = 0.5;
        public const double DouglasPeuckerEpsilonMm = 5.0;
        public const double DefaultSlabMinDiagonalMm = 1000.0;
        public const double DefaultLineMinLengthMm = 200.0;
        public const double DefaultColumnMinDimensionMm = 200.0;
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

        // Export model setup defaults
        public const double DefaultMeshSizeMm = 500.0;
        public const double DefaultStripSpacingMm = 1800.0;
        public const string DefaultDesignCode = "CSA A23.3-19";
    
        /// <summary>How tall annotation text is drawn ON PAPER, before the drawing scale is applied.
        /// 2.5 mm is the ordinary height a drawing is annotated at; at 1:96 that is 240 mm on the
        /// model. Used for markup /Contents, which — unlike a page word — has no box of its own.</summary>
        public const double PaperTextHeightMm = 2.5;

}
}
