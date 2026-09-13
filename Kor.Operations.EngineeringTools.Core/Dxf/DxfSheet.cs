using System.Globalization;

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
public sealed record DxfSheet(string Name, IReadOnlyList<string> Lines)
{
    /// <summary>
    /// The same view drawn elsewhere on the page: every X (group codes 10 and 11) moved by
    /// <paramref name="dx"/> and every Y (20 and 21) by <paramref name="dy"/>, the header's extents
    /// and insertion base with them, in the view's own unit. The differential of intake step 56 —
    /// the same drawings shifted on the page build the same structure — composes a set as it is
    /// and as this, and the two models must differ by the shift alone. Nothing else is touched:
    /// a value that does not parse as a number is left as it stands.
    /// </summary>
    public DxfSheet Shifted(double dx, double dy)
    {
        var moved = new List<string>(Lines.Count);
        for (int i = 0; i < Lines.Count; i++)
        {
            string line = Lines[i];
            if (i + 1 < Lines.Count && i % 2 == 0)
            {
                string code = line.Trim();
                double by = code is "10" or "11" ? dx : code is "20" or "21" ? dy : 0;
                if (by != 0 && double.TryParse(Lines[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                {
                    moved.Add(line);
                    moved.Add((v + by).ToString("F4", CultureInfo.InvariantCulture));
                    i++;
                    continue;
                }
            }
            moved.Add(line);
        }
        return this with { Lines = moved };
    }
}
