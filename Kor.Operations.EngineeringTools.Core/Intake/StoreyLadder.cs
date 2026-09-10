using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// A storey height is the distance between two level lines on an elevation drawn to scale. The level
/// ladder (<see cref="ScheduleGridReader.ReadLevelLadder"/>) gives the level lines' y in points; the
/// sheet's stated scale turns each gap into millimetres.
/// </summary>
/// <remarks>
/// Measured 2026-09-08 before this was written (`takeoff elev-scan`, `takeoff e2k-storeys`): on
/// 31168's SHEAR WALL ELEVATIONS - BLDG B (p37, 1/8" = 1'-0") the ladder gives 2,946 mm for the
/// typical tower storey and 5,336 mm for LEVEL 3 → LEVEL 2; the engineer's own model states 116 in
/// (2,946 mm) and 210 in (5,334 mm). Within 2 mm on the storeys the two name alike. Storey heights
/// had been taken from the reference model only; the drawings state them on every wall elevation.
///
/// WHAT IT COVERS: a sheet typed section/elevation, with a level ladder of at least
/// <see cref="MinRows"/> rows and a stated ratio scale. WHAT IT DOES NOT: a schedule sheet's level
/// column (its rows are a table's pitch, not a drawing's — the caller must not apply this to a
/// schedule), a ladder whose names the level reader mangles ("LEVEL 1 - CONCRETE" reads as a level
/// named CONCRETE on 31130 p53 — a level reader finding, recorded, not fixed here), two views on
/// one sheet with different ladders (the busiest column wins, as the level reader has it), and an
/// elevation with no stated scale.
/// </remarks>
public static class StoreyLadder
{
    /// <summary>One storey: the level line above, the one below, and the height between them.</summary>
    public sealed record Storey(string Level, string LevelBelow, double HeightMm, double YPts);

    /// <summary>A ladder of fewer rows is a caption or a table fragment, not an elevation's levels.</summary>
    public const int MinRows = 3;

    /// <summary>Storey heights top → bottom, or empty when the sheet states no ratio scale or has no ladder.</summary>
    public static IReadOnlyList<Storey> Read(VectorPageReader.PageContent page, string? scaleNote)
        => Read(page, scaleNote, Array.Empty<ViewCaptions.Caption>());

    /// <summary>
    /// As above; when the sheet states no ratio (AS NOTED), the view's own caption under the ladder
    /// supplies it (brief 28: 31138's wall elevations read nothing until this).
    /// </summary>
    public static IReadOnlyList<Storey> Read(VectorPageReader.PageContent page, string? scaleNote, IReadOnlyList<ViewCaptions.Caption> captions)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(captions);

        // EVERY COLUMN OF THE LADDER (intake step 25). An elevation taller than its sheet is drawn
        // in strips side by side, and above the storeys the buildings share each tower's levels
        // are labelled for it (B-LEVEL 27); one column read the lower strip and lost the rest, and
        // 31168's towers above L19 had no storey to land on.
        var storeys = new List<Storey>();
        foreach (var ladder in ScheduleGridReader.ReadLevelLadders(page, MinRows))
        {
            string? scale = scaleNote;
            if (string.IsNullOrWhiteSpace(scale))
            {
                double ladderX = page.Words.Where(w => (string.Equals(w.Text, "LEVEL", StringComparison.OrdinalIgnoreCase) || w.Text.EndsWith("LEVEL", StringComparison.OrdinalIgnoreCase))
                        && ladder.Any(r => Math.Abs(r.Y - w.Cy) <= 10)).Select(w => w.Cx).DefaultIfEmpty(0).Average();
                scale = ViewCaptions.For(captions, ladderX, ladder.Min(r => r.Y));
            }
            if (string.IsNullOrWhiteSpace(scale)) continue;
            if (PlanGeometry.MetresPerPixel(scale, 72) is not double metresPerPoint || metresPerPoint <= 0) continue;
            var rows = ladder.OrderByDescending(r => r.Y).ToList();
            for (int i = 0; i + 1 < rows.Count; i++)
                storeys.Add(new Storey(rows[i].Normalized, rows[i + 1].Normalized, (rows[i].Y - rows[i + 1].Y) * metresPerPoint * 1000.0, rows[i].Y));
        }
        // a strip's ladder is drawn on both sides of it, so the same storey comes from two columns
        // with the same lines: one statement per sheet, the first column's
        return storeys
            .GroupBy(s => (s.Level, s.LevelBelow))
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>The most common height to the nearest 5 mm, the "typical storey" a set repeats.</summary>
    public static double? Typical(IReadOnlyList<Storey> storeys)
        => storeys.Count == 0 ? null
            : storeys.GroupBy(s => Math.Round(s.HeightMm / 5.0) * 5.0).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
}
