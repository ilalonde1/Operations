using Kor.Operations.EngineeringTools.PdfToSafe;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// What a sheet carries as objects, in one light record: the view of a <see cref="SheetRecord"/>
/// that the comparison (Reissue Impact) and the checks read, and that a test can build by hand.
/// Geometry in millimetres at the sheet's scale, as the record carries it.
/// </summary>
public sealed record SheetObjects(
    string? SheetNumber,
    int PageNumber,
    string SheetType,
    string? Level,
    IReadOnlyList<(double X, double Y)> Columns,
    IReadOnlyList<(double WidthMm, double DepthMm)> ColumnSizes,
    IReadOnlyList<WallPanel> Walls,
    IReadOnlyList<FootingOutline> Footings,
    IReadOnlyList<GridAxis> GridAxes,
    IReadOnlyList<StoreyLadder.Storey> Storeys,
    IReadOnlyList<ScheduleTable> Schedules)
{
    /// <summary>The page's height in PDF points, so a change can be painted back onto the page.</summary>
    public double PageHeightPts { get; init; }

    public static SheetObjects From(SheetRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var g = record.Geometry;
        return new SheetObjects(record.SheetNumber, record.PageNumber, record.SheetType, record.Level,
            g.Columns, g.ColumnSizes, g.Walls, g.Footings, g.GridAxes, record.Storeys, record.Schedules)
        { PageHeightPts = record.HeightPts };
    }

    /// <summary>An empty sheet of this number and page, for a sheet one issue has and the other does not.</summary>
    public static SheetObjects Empty(string? sheetNumber, int pageNumber) => new(sheetNumber, pageNumber, "plan", null,
        Array.Empty<(double, double)>(), Array.Empty<(double, double)>(), Array.Empty<WallPanel>(), Array.Empty<FootingOutline>(),
        Array.Empty<GridAxis>(), Array.Empty<StoreyLadder.Storey>(), Array.Empty<ScheduleTable>());
}
