namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// One plan view as the composer reads it, held in memory: the name the office's export would give
/// the file (<see cref="PlanSheetNaming"/> reads the storey from it) and the DXF's lines, exactly
/// as <c>DxfExporter.ExportLines</c> wrote them. The in-memory handoff between the two halves of
/// the PDF route (completion plan WP4, 2026-09-11): the intake hands these to
/// <see cref="DxfToEtabsService"/> through <see cref="DxfToEtabsRequest.Sheets"/>, and a file is
/// written only where a DXF is wanted as an outlet. The DXF text is the interchange because every
/// reader in the composer already takes it as lines and the office's own exports arrive as it; the
/// typed geometry set behind it is the next refinement, not this one.
/// </summary>
public sealed record DxfSheet(string Name, IReadOnlyList<string> Lines);
