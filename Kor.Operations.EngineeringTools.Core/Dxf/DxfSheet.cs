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

    /// <summary>
    /// The same view with its entities in the opposite order (audit F11, step 61): the differential
    /// <c>TheSameDrawingsInAnotherOrderBuildTheSameStructure</c> composes a set as it is and as this,
    /// and the two models must be the same structure — a nearest-node search, a tie, a "first" that
    /// depends on which entity came first is a fault this finds. The ENTITIES section's entities are
    /// reversed; everything else is left as it stands.
    /// </summary>
    public DxfSheet Reversed()
    {
        int start = -1, end = -1;
        for (int i = 0; i + 3 < Lines.Count; i += 2)
        {
            if (start < 0 && Lines[i].Trim() == "0" && Lines[i + 1].Trim() == "SECTION" && Lines[i + 2].Trim() == "2" && Lines[i + 3].Trim() == "ENTITIES") { start = i + 4; i += 2; continue; }
            if (start >= 0 && Lines[i].Trim() == "0" && Lines[i + 1].Trim() == "ENDSEC") { end = i; break; }
        }
        if (start < 0 || end < 0) return this;
        // an entity starts at a "0" code; a POLYLINE runs through its VERTEX entities to its SEQEND and is one entity here
        var entities = new List<List<string>>();
        bool inPolyline = false;
        for (int i = start; i < end; i += 2)
        {
            string code = Lines[i].Trim(), value = i + 1 < end ? Lines[i + 1].Trim() : "";
            bool opens = code == "0" && !(inPolyline && value is "VERTEX" or "SEQEND");
            if (opens || entities.Count == 0) entities.Add([]);
            if (code == "0" && value == "POLYLINE") inPolyline = true;
            if (code == "0" && value == "SEQEND") inPolyline = false;
            entities[^1].Add(Lines[i]);
            if (i + 1 < end) entities[^1].Add(Lines[i + 1]);
        }
        entities.Reverse();
        var lines = new List<string>(Lines.Count);
        lines.AddRange(Lines.Take(start));
        foreach (var e in entities) lines.AddRange(e);
        lines.AddRange(Lines.Skip(end));
        return this with { Lines = lines };
    }
}
