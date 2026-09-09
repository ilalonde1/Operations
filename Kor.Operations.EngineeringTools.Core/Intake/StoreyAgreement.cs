using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// The drawings' storeys against the model's. For each storey the drawings state between two named
/// levels, the model's height for the same pair is the difference of the two named elevations —
/// not the model's own "storey below", because a site model interleaves several buildings' levels
/// and the storey under LEVEL 10 in the list may be another building's roof.
/// </summary>
/// <remarks>
/// A check, not a replacement: the model is not changed by this. Measured 2026-09-08 on 31168 with
/// the engineer's own reference model — see the brief-26 record for the numbers. WHAT IT DOES NOT:
/// a level the two sides name differently (the drawing's L1 against the model's LEVEL 1 MEZZ), a
/// prefixed name the level reader does not take (B-LEVEL 37), and a building whose levels the
/// model has under another building's names.
/// </remarks>
public static class StoreyAgreement
{
    public const double WithinMm = 25.0;

    /// <summary>One storey the drawings state; the model's height for the same pair when both levels exist there.</summary>
    public sealed record Row(string Level, string LevelBelow, double DrawingMm, double? ModelMm, int Sheets)
    {
        public double? DeltaMm => ModelMm is null ? null : DrawingMm - ModelMm;
        public bool Within => DeltaMm is double d && Math.Abs(d) <= WithinMm;
    }

    public sealed record Result(IReadOnlyList<Row> Rows, int ElevationSheets, int SheetsWithStoreys)
    {
        public int Matched => Rows.Count(r => r.ModelMm is not null);
        public int WithinTolerance => Rows.Count(r => r.Within);
        public IReadOnlyList<Row> Off => Rows.Where(r => r.ModelMm is not null && !r.Within).ToList();
        public IReadOnlyList<Row> DrawingOnly => Rows.Where(r => r.ModelMm is null).ToList();

        /// <summary>One line for a report or a warning list.</summary>
        public string Summary()
        {
            if (Rows.Count == 0)
                return $"Storeys: the drawings' {ElevationSheets} section/elevation sheet(s) yielded no storey heights (no ratio scale on the sheet, or no level ladder).";
            string off = Off.Count == 0 ? "" : "; off: " + string.Join(", ", Off.Take(6).Select(r => $"{r.Level}->{r.LevelBelow} drawing {r.DrawingMm:0} vs model {r.ModelMm:0} mm")) + (Off.Count > 6 ? ", ..." : "");
            string unmatched = DrawingOnly.Count == 0 ? "" : $"; {DrawingOnly.Count} pair(s) the model does not name both ends of: " + string.Join(", ", DrawingOnly.Take(6).Select(r => $"{r.Level}->{r.LevelBelow}")) + (DrawingOnly.Count > 6 ? ", ..." : "");
            return $"Storeys: the drawings' wall elevations state {Rows.Count} storeys on {SheetsWithStoreys} of {ElevationSheets} section/elevation sheets; "
                 + $"{Matched} match the model by both level names, {WithinTolerance} of those within {WithinMm:0} mm{off}{unmatched}.";
        }
    }

    /// <summary>Compare a set's storey table with a model's storeys; <paramref name="unitInInches"/> is the model's length unit.</summary>
    public static Result Compare(SetStoreys.Table table, IReadOnlyList<StoryLevel> stories, double unitInInches)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(stories);
        var elevationMm = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in stories)
        {
            string key = ScheduleTakeoff.NormalizeLevel(s.Name);
            if (!elevationMm.ContainsKey(key)) elevationMm[key] = s.Elevation * unitInInches * 25.4;
        }
        var rows = table.Storeys.Select(st =>
        {
            double? model = elevationMm.TryGetValue(st.Level, out double top) && elevationMm.TryGetValue(st.LevelBelow, out double below)
                ? top - below : null;
            return new Row(st.Level, st.LevelBelow, st.HeightMm, model, st.Sheets);
        }).ToList();
        return new Result(rows, table.ElevationSheets, table.SheetsWithStoreys);
    }
}
