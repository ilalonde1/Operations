#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// The geometry model shared by every reader that turns a drawing PDF into structure.
    /// </summary>
    /// <remarks>
    /// These types lived in the WPF app until 2 September, next to the window that displays them.
    /// Nothing about them is WPF — they are lists of points — but their being in that project meant
    /// the CLI could not reach the classifier or the DXF writer, so a drawing could only be turned
    /// into geometry by a person clicking through a dialog. Moved here so `takeoff pdf-takeoff` can
    /// do it, and so the reading, classifying and writing stages all sit beside VectorPageReader.
    ///
    /// The namespace is unchanged, so the app's own files resolve them from here with no edit.
    /// </remarks>
    public readonly record struct TextAnnotation(
        string Text,
        double X,
        double Y,
        double LeftX,
        double BottomY,
        double HeightMm)
    {
        public TextAnnotation(string text, double x, double y)
            : this(text, x, y, x, y, 0.0)
        {
        }

        public void Deconstruct(out string text, out double x, out double y)
        {
            text = Text;
            x = X;
            y = Y;
        }
    }

    /// <summary>One subpath as the PDF drew it, in mm, before anything decides what it is.</summary>
    public sealed record RawSubpath(
        List<(double X, double Y)> Points,
        bool IsClosed,
        (byte R, byte G, byte B) Color,
        bool IsFilled,
        bool IsStroked,
        double LineWidth,
        bool IsAnnotation)
    {
        /// <summary>A clip (W n): draws nothing, limits what the paths after it show. See VectorPageReader.GeomPath.</summary>
        public bool IsClipping { get; init; }
        /// <summary>The page path this subpath came from, in content order; -1 when unknown (fixtures).</summary>
        public int PathOrdinal { get; init; } = -1;
    }

    /// <summary>A cut wall's outline, centreline and thickness, all in millimetres.</summary>
    /// <summary>A spread footing: the box its dashed outline closes, the schedule mark that sizes it, and that size.</summary>
    public sealed record FootingOutline(string Mark, IReadOnlyList<(double X, double Y)> Outline,
                                        (double X, double Y) Centre, double LengthMm, double WidthMm, double DepthMm)
    {
        /// <summary>
        /// The plan's own label for this mark names this footing: it is the label's nearest footing and
        /// stands in the box or within half the footing's size of its edge (inside on metric sets,
        /// 254–461 mm beneath on 31130). False is a box of a scheduled size that no label claims — a
        /// sump the size of an F1 reads as F1 — and the ledger counts those.
        /// </summary>
        public bool LabelledOnThePlan { get; init; }
    }

    public sealed record WallPanel(IReadOnlyList<(double X, double Y)> Outline,
        (double X, double Y) Start, (double X, double Y) End, double ThicknessMm);

    /// <summary>
    /// A named grid axis in millimetres: a vertical axis is the line x = <see cref="AtMm"/> (ETABS
    /// DIR "X"), a horizontal one y = AtMm. The name is the bubble's label; the same name on two
    /// sheets is the same line (GridBubbles.Axis, scaled).
    /// </summary>
    public sealed record GridAxis(string Name, bool Vertical, double AtMm);

    /// <summary>
    /// An opening knocked out of a wall with a paper-coloured fill (Intake step 14): its extent along
    /// the wall's axis in mm, the wall's thickness, and the index of the first pier the wall became.
    /// </summary>
    public sealed record Doorway((double X, double Y) Start, (double X, double Y) End, double ThicknessMm, int FirstPier)
    {
        public double LengthMm => Math.Sqrt(Math.Pow(End.X - Start.X, 2) + Math.Pow(End.Y - Start.Y, 2));
    }

    public sealed class ExtractedGeometry
    {
        /// <summary>The sheet's named grid axes (Intake step 8). Written to the DXF's GRID layer.</summary>
        public List<GridAxis> GridAxes { get; } = new();
        public List<WallPanel> Walls { get; } = new();
        /// <summary>Openings knocked out of walls; each wall with one is in <see cref="Walls"/> as its piers.</summary>
        public List<Doorway> Doorways { get; } = new();
        /// <summary>Line index to wall index for lines that are a wall's two faces (step 20); the wall is in <see cref="Walls"/>, the lines are not beams.</summary>
        public Dictionary<int, int> WallFaceLines { get; } = new();
        /// <summary>Stroke width of each line as the PDF drew it, parallel to <see cref="Lines"/>; the pen a face was drawn with.</summary>
        public List<double> LineWidths { get; } = new();
        public List<(byte R, byte G, byte B)> WallColors { get; } = new();
        public List<bool> WallIsAnnotation { get; } = new();
        /// <summary>Filled loops with wall-proportioned boxes but more than four vertices; not split.</summary>
        public int WallRibbonsNotSplit { get; set; }

        /// <summary>Footings read as dashed rectangles of a scheduled size (Intake.FootingOutlines). Outline in mm.</summary>
        public List<FootingOutline> Footings { get; } = new();
        // Each slab: ordered list of (X,Y) in mm, ready for a closed polyline
        public List<List<(double X, double Y)>> Slabs { get; } = new();
        // Each column: centroid (X,Y) in mm
        public List<(double X, double Y)> Columns { get; } = new();
        // Each line element: list of (X,Y) in mm (open polyline)
        public List<List<(double X, double Y)>> Lines { get; } = new();
        public List<(byte R, byte G, byte B)> SlabColors   { get; } = new();
        public List<(byte R, byte G, byte B)> ColumnColors { get; } = new();
        /// <summary>
        /// Bounding-box dimensions (Width, Depth in mm) for each detected column,
        /// parallel to <see cref="Columns"/>. Derived from the column polygon footprint.
        /// Width = X extent, Depth = Y extent.
        /// </summary>
        public List<(double WidthMm, double DepthMm)> ColumnSizes { get; } = new();
        public List<(byte R, byte G, byte B)> LineColors   { get; } = new();

        /// <summary>
        /// Parallel to Slabs / Columns / Lines: did this shape come from a MARKUP ANNOTATION, or
        /// from the page the architect drew?
        ///
        /// It is the only exact way to tell an engineer's red from an architect's red, and on
        /// Parcel 11 they are the same red — #F00000 carries Omar's shear walls AND the property
        /// line sweeping round the site. Colour cannot separate those; origin can, and the parser
        /// already knew it and threw it away. Every one of that sheet's 5 red closed shapes and 37
        /// red wall segments is an annotation; 488 of its 489 red lines are the boundary.
        /// </summary>
        public List<bool> SlabIsAnnotation   { get; } = new();
        public List<bool> ColumnIsAnnotation { get; } = new();
        public List<bool> LineIsAnnotation   { get; } = new();
        /// <summary>
        /// Optional cross-section hints parallel to <see cref="Lines"/>. Populated when a
        /// slab polygon is reclassified as a wall/beam and its intended beam section is
        /// derived from the polygon's bounding box. Null entries mean "no hint — fall
        /// back to text-annotation parsing (BeamSectionParser)". Not populated by the
        /// initial extraction; only by PdfGeometryExtractor.ReclassifyByColor.
        /// </summary>
        public List<(double WidthMm, double DepthMm)?> LineSectionHints { get; set; } = new();
        public List<List<(double X, double Y)>> DropPanelCandidates { get; set; } = new();
        public double PageWidthPts  { get; set; }
        public double PageHeightPts { get; set; }
        public int    ScaleDenominator { get; set; }
        public int  PageCount    { get; set; }
        public int  RawPathCount { get; set; }
        public bool IsVectorPdf  { get; set; }
        /// <summary>
        /// Raw text annotations extracted from the PDF page, with their centroid
        /// positions in the same mm coordinate space as Slabs/Lines/Columns.
        /// Populated during extraction when text parsing is enabled.
        /// </summary>
        public List<TextAnnotation> TextAnnotations { get; set; } = new();
    }

    public sealed class SlabColorSettings
    {
        public string ElementType { get; set; } = "Slab";
        public double ThicknessMm { get; set; } = PdfToSafeConstants.DefaultThicknessMm;
        public double SdlKPa      { get; set; } = 0.0;
        public double LiveKPa     { get; set; } = 0.0;
        public string GradeCode   { get; set; } = PdfToSafeConstants.DefaultGradeCode;
    }
}
