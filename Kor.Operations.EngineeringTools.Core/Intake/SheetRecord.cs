using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.Intake;

public sealed record ScheduleTable(string Heading, string Kind, IReadOnlyList<ScheduleRow> Rows);
public sealed record ScheduleRow(string Mark, IReadOnlyDictionary<string, string> Cells, string Route);
public sealed record PlanMark(string Text, double X, double Y);
/// <summary>
/// An annotation with text: its Bluebeam type, the words, the author, and where it sits on the page
/// in PDF points (the annotation rectangle's centre and size), so an instruction can be placed on
/// the grid and beside the member it is about (intake step 17).
/// </summary>
public sealed record MarkupNote(string Type, string Text, string Author, int PageNumber)
{
    public double Cx { get; init; }
    public double Cy { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

/// <summary>One retained page read. Content, furniture, grid and marks are in PDF points; geometry is in mm.</summary>
public sealed record SheetRecord(
    int PageNumber, double WidthPts, double HeightPts, int Rotation,
    string? SheetNumber, string? BookmarkTitle, string SheetType,
    string? Level, string? Zone,
    string? ScaleNote, int? ScaleDenominator,
    ExtractedGeometry Geometry,
    IReadOnlyList<ScheduleTable> Schedules,
    IReadOnlyList<PlanMark> Marks,
    IReadOnlyList<SlabThicknessZoner.Callout> ThicknessCallouts,
    GridBubbles.Grid Grid,
    SheetFurniture.Set Furniture,
    IReadOnlyList<MarkupNote> Markup,
    int Links,
    VectorPageReader.PageContent Content,
    IReadOnlyList<PathFate> PathFates,
    IReadOnlyList<WordFate> WordFates)
{
    public SheetContext Context { get; init; } = new();

    /// <summary>
    /// Every annotation but the links, with or without words — the ticks Bluebeam writes as ink
    /// with nothing in them are the drafter's replies (intake step 18). <see cref="Markup"/> is
    /// the subset with words, as the ledger counts it.
    /// </summary>
    public IReadOnlyList<MarkupNote> Annotations { get; init; } = Array.Empty<MarkupNote>();

    public PlanScheduleAgreement? ColumnAgreement { get; init; }
    public string? ColumnAgreementError { get; init; }

    /// <summary>
    /// The title block's labelled fields as the drafter wrote them — SHEET TITLE, SHEET NUMBER,
    /// SCALE, PROJECT NO, DRAWN BY, CHK'D BY, PROJECT TITLE, REV … (<see cref="TitleBlockFields"/>).
    /// Empty for a block without the labels.
    /// </summary>
    public IReadOnlyDictionary<string, string> TitleBlock { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What the title block's SCALE field says, verbatim — "1/8" = 1'-0"", "1 : 100", "AS NOTED",
    /// "As indicated". <see cref="ScaleNote"/> is the ratio when one parses; this is the statement
    /// either way, because "AS NOTED" on a details sheet is information, not a missing value.
    /// </summary>
    public string? ScaleStatement { get; init; }

    /// <summary>
    /// The title block states two different scales. <see cref="ScaleNote"/> is then null by refusal,
    /// and the SCALE field was not used as a fallback (audit F8).
    /// </summary>
    public bool ScaleConflict { get; init; }

    /// <summary>
    /// The spread-footing marks the plan places, with their positions in millimetres, as the footing
    /// reader used them — the report counts these and re-reads nothing (audit F9).
    /// </summary>
    public IReadOnlyList<FootingOutlines.MarkLabel> FootingLabels { get; init; } = Array.Empty<FootingOutlines.MarkLabel>();

    /// <summary>
    /// Every dimension string on the sheet typed with its value in millimetres, and for those between
    /// grid axes, the span and whether the written length agrees with the axes' spacing at the
    /// sheet's scale (<see cref="DimensionStrings"/>). Empty when no scale was requested.
    /// </summary>
    public IReadOnlyList<DimensionStrings.Dimension> Dimensions { get; init; } = Array.Empty<DimensionStrings.Dimension>();

    /// <summary>
    /// Storey heights read off a section or elevation sheet: the distance between consecutive level
    /// lines of its level ladder at the sheet's stated scale (<see cref="StoreyLadder"/>). Empty on any
    /// other sheet type, and on an elevation with no ratio scale or fewer than three level lines.
    /// </summary>
    public IReadOnlyList<StoreyLadder.Storey> Storeys { get; init; } = Array.Empty<StoreyLadder.Storey>();
}

/// <summary>Page facts captured during intake so reporting never opens or reinterprets a PDF.</summary>
public sealed record SheetContext
{
    public bool OutlinesPresent { get; init; }
    public int ScheduleHeadings { get; init; }
    public IReadOnlyDictionary<string, int> AnnotationTypes { get; init; } = new Dictionary<string, int>();
    public int LinksWithTarget { get; init; }
    public int Images { get; init; }
    public int NonHorizontalLetters { get; init; }
    public int InvisibleLetters { get; init; }
    public int NonRgbLetters { get; init; }
    public int ClippingOperations { get; init; }
    public int Fonts { get; init; }
    public int AnnotationPaths { get; init; }
    public int NoInkPaths { get; init; }
    public int PaperPaths { get; init; }
    /// <summary>Column schedule rows the reader offered whose mark is not shaped like a mark ("PC9ETON:", "EXTENTS"); not rows.</summary>
    public int ScheduleRowsNotMarks { get; init; }
    public int InkedPaths { get; init; }
    public IReadOnlySet<int> InkedPathIndices { get; init; } = new HashSet<int>();
}
