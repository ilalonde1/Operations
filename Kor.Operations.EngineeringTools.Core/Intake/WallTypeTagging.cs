using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// A WALL IS WHAT ITS TAG SAYS IT IS (intake step 33).
///
/// On a set with an assembly schedule (<see cref="AssemblySchedule"/>), the plans tag their walls
/// with the schedule's codes — on 31170, 887 tags on the enlarged 1/4" plans, 635 of them steel
/// stud. A tag is a word on the plan equal to a code; a wall takes the nearest tag within reach
/// of its axis; the code's material comes from the card. A wall whose type is a partition is not
/// structure: it goes to the DXF's KOR_PARTITION layer, which the model does not read, and the
/// report counts it. A wall with no tag near it keeps being modelled as it was, and is counted as
/// untagged — the report says what it could not decide, it does not guess.
///
/// WHAT IT COVERS: a tag beside a wall; the nearest of two tags; a tag out of reach leaving the
/// wall untagged; a partition's code and a concrete code from the same schedule. WHAT IT DOES NOT:
/// a tag on another sheet (the 1/8" floor plan's walls are untagged; the enlargement tags them, and
/// DXF carries no way to pass that across sheets — the case for the composer reading the intake's
/// record directly); a tag with a leader pointing further than the reach; a wall drawn as two face
/// lines whose tag sits between them (the axis is between the faces, so that one is within reach).
/// </summary>
public static class WallTypeTagging
{
    /// <summary>How far from a wall's axis its tag may stand, in millimetres. On 31170's 1/4" plans the tags sit within about 600 mm; a bay is 3 m or more.</summary>
    public const double ReachMm = 1200;

    public static void Apply(ExtractedGeometry geometry, VectorPageReader.PageContent content,
        SheetFurniture.Set furniture, IReadOnlyList<AssemblySchedule.Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(assemblies);

        geometry.WallTypeCodes.Clear();
        geometry.WallIsPartition.Clear();
        geometry.WallTypeTags.Clear();

        var byCode = new Dictionary<string, AssemblySchedule.Assembly>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in assemblies)
            if (!byCode.ContainsKey(a.Code)) byCode[a.Code] = a;
        if (byCode.Count == 0 || geometry.ScaleDenominator <= 0)
        {
            for (int i = 0; i < geometry.Walls.Count; i++) { geometry.WallTypeCodes.Add(null); geometry.WallIsPartition.Add(false); }
            return;
        }

        double scale = geometry.ScaleDenominator * PdfToSafeConstants.PointsToMm;
        foreach (var w in content.Words)
        {
            string t = w.Text.Trim();
            if (!byCode.ContainsKey(t)) continue;
            if (furniture.IsFurniture(w.Cx, w.Cy)) continue;
            geometry.WallTypeTags.Add((byCode[t].Code, w.Cx * scale, w.Cy * scale));
        }

        for (int i = 0; i < geometry.Walls.Count; i++)
        {
            var wall = geometry.Walls[i];
            string? best = null;
            double bestD = double.MaxValue;
            foreach (var tag in geometry.WallTypeTags)
            {
                double d = DistanceToSegment(tag.X, tag.Y, wall.Start, wall.End);
                if (d < bestD) { bestD = d; best = tag.Code; }
            }
            if (best is not null && bestD <= ReachMm)
            {
                geometry.WallTypeCodes.Add(best);
                geometry.WallIsPartition.Add(byCode[best].Material == AssemblySchedule.Material.Stud);
            }
            else
            {
                geometry.WallTypeCodes.Add(null);
                geometry.WallIsPartition.Add(false);
            }
        }
    }

    private static double DistanceToSegment(double px, double py, (double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        double t = len2 <= 0 ? 0 : Math.Clamp(((px - a.X) * dx + (py - a.Y) * dy) / len2, 0, 1);
        double cx = a.X + t * dx, cy = a.Y + t * dy;
        return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
    }
}
