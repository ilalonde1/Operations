#nullable enable
namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal sealed record ExportSummaryRow(
        string Name,
        string Type,
        string Grade,
        string Thickness,
        string Sdl,
        string Live,
        string Quantity,
        double SlabAreaM2,
        double BeamLengthM,
        int ColumnCount,
        string ColorHex);
}
