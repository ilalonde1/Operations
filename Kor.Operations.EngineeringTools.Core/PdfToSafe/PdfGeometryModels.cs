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

    /// <summary>
    /// The line labelled MATCH LINE that a plan too wide for one sheet was split on (intake step 22),
    /// in mm. Written to the DXF on a MATCH layer so the ETABS side joins the sheets that share it.
    /// </summary>
    public sealed record PlanMatchLine((double X, double Y) Start, (double X, double Y) End);

    public sealed class ExtractedGeometry
    {
        /// <summary>The sheet's match lines (step 22); usually none or one.</summary>
        public List<PlanMatchLine> MatchLines { get; } = new();
        /// <summary>The sheet's named grid axes (Intake step 8). Written to the DXF's GRID layer.</summary>
        public List<GridAxis> GridAxes { get; } = new();
        public List<WallPanel> Walls { get; } = new();
        /// <summary>Openings knocked out of walls; each wall with one is in <see cref="Walls"/> as its piers.</summary>
        public List<Doorway> Doorways { get; } = new();
        /// <summary>Line index to wall index for lines that are a wall's two faces (step 20); the wall is in <see cref="Walls"/>, the lines are not beams.</summary>
        public Dictionary<int, int> WallFaceLines { get; } = new();
        /// <summary>
        /// Index in <see cref="Walls"/> of the first wall read from face lines (step 20); the face
        /// walls follow the filled ones. Equal to <c>Walls.Count</c> when there are none. A face
        /// line may serve several walls (piers along one outer face), so this, not the count of
        /// <see cref="WallFaceLines"/>, says how many walls the faces made.
        /// </summary>
        public int FirstFaceWall { get; set; }
        /// <summary>
        /// Line index to slab index for lines that are a floor's edge (step 24): the closed loop
        /// they make is in <see cref="Slabs"/> and the lines are not written as beams. A line may
        /// serve one loop only.
        /// </summary>
        public Dictionary<int, int> SlabEdgeLines { get; } = new();
        /// <summary>Index in <see cref="Slabs"/> of the first slab recovered from a loop of lines (step 24); those follow the filled ones.</summary>
        public int FirstEdgeSlab { get; set; }
        /// <summary>A FACE DRAWN IN PIECES IS ONE FACE (intake step 38): how many pieces were absorbed into the lines they lay on — the count the sheet reports, so a joined face is not a silent change.</summary>
        public int LinePiecesJoined { get; set; }
        /// <summary>Stroke width of each line as the PDF drew it, parallel to <see cref="Lines"/>; the pen a face was drawn with.</summary>
        public List<double> LineWidths { get; } = new();
        public List<(byte R, byte G, byte B)> WallColors { get; } = new();
        public List<bool> WallIsAnnotation { get; } = new();
        /// <summary>
        /// A WALL IS WHAT ITS TAG SAYS IT IS (intake step 33). Parallel to <see cref="Walls"/>: the
        /// assembly code the plan tags the wall with (S8.1, C12), read from the nearest tag within
        /// reach, or null where the plan tags nothing near it. The code's material comes from the
        /// set's assembly schedule (<see cref="Intake.AssemblySchedule"/>); a wall whose type is a
        /// partition goes to the DXF's KOR_PARTITION layer, which the model does not read.
        /// </summary>
        public List<string?> WallTypeCodes { get; } = new();
        /// <summary>Parallel to <see cref="Walls"/>: true when the wall's tagged type is a partition (stud, gypsum).</summary>
        public List<bool> WallIsPartition { get; } = new();
        /// <summary>A DIMENSION STRING IS NOT A WALL (intake step 35). Parallel to <see cref="Walls"/>: true when the outline holds a length written along it, which no wall does (<see cref="Intake.DimensionStrings.StandDownWalls"/>); not written, not tagged, not counted as a wall.</summary>
        public List<bool> WallIsDimensionString { get; } = new();
        /// <summary>Every assembly-code tag on the sheet, in millimetres, whether or not a wall took it — written to KOR_WALLTYPE.</summary>
        public List<(string Code, double X, double Y)> WallTypeTags { get; } = new();
        /// <summary>Filled loops with wall-proportioned boxes but more than four vertices; not split.</summary>
        public int WallRibbonsNotSplit { get; set; }

        /// <summary>
        /// Why the planar arrangement of the slab-edge candidates was refused (step 78), or null where it
        /// was built: a degenerate embedding PlanarRings will not resolve by arrival order. The storey
        /// then has whatever rings the chain walk closes, and the report says so.
        /// </summary>
        public string? SlabEdgeArrangementRefused { get; set; }

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
        /// <summary>A PATTERN'S CELLS ABUT; A COLUMN STANDS ALONE (intake step 37): the column-sized closed shapes that stood edge to edge with another and left the columns, centre and size, so the overlay can draw them and a person can check they were a fill pattern and not two members touching.</summary>
        public List<((double X, double Y) Centre, double WidthMm, double DepthMm)> PatternCells { get; } = new();
        /// <summary>A TARGET'S QUADRANTS ARE NOT COLUMNS (intake step 49): the pairs of one-size filled shapes that met only at a corner - the two filled quadrants of a spot-elevation target - and left the columns, centre and size, for the overlay.</summary>
        public List<((double X, double Y) Centre, double WidthMm, double DepthMm)> SymbolQuadrants { get; } = new();
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
        /// THE GRID IS DRAWN WITH ONE PEN (intake step 53): the strokes lying along a grid axis that are heavier
        /// than the grid's own pen - a tendon or a beam drawn on the grid line - kept APART from <see cref="Lines"/>
        /// so that no wall reader sees them (until step 53 they were the grid, and nothing saw them at all), and
        /// read by the tendon reader, which chains them with the lines. Not exported.
        /// </summary>
        public List<List<(double X, double Y)>> StrokesOnGrid { get; } = new();

        /// <summary>
        /// The filled triangles the column reader refused as symbols (step 58): a section mark's or a leader's
        /// arrowhead, its centre and its size in mm. A line ending at one is a section cut or a leader, not a
        /// slab edge (step 79).
        /// </summary>
        public List<(double X, double Y, double SizeMm)> Arrowheads { get; } = new();
        /// <summary>A TENDON'S ANCHOR IS NOT A COLUMN (intake step 48). Parallel to <see cref="Columns"/>: true when the column's footprint holds the end of a line labelled with a force (<see cref="Intake.TendonAnchors"/>); not written, not counted as a column.</summary>
        public List<bool> ColumnIsTendonAnchor { get; } = new();
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
