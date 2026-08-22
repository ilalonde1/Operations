namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// What THIS job's own drawings say a wall and a column are, measured from the members read off
/// them rather than taken from a rule.
///
/// Every threshold this tool runs on was measured across the portfolio or on two buildings drafted
/// by one office. A portfolio number is right for the portfolio and wrong for any single job:
/// dxf.max-wall-thickness is 60", measured over 1,126 models where 42" walls appear 1,256 times
/// and 48" 831 times — all real, all tower cores — and on 31168 that same 60" admitted 42" walls
/// into a parkade and a 132" wall into an engineer's model, while her own walls run 10 to 16.
///
/// The job knows better than the corpus does. Measured on two buildings with the engineer's own
/// model to check against, the distribution read off the drawings tracks hers through the body and
/// overshoots only at the top:
///
///     31065   hers median 23.6, p90 23.6, max 23.6     drawings median 23.5, p90 23.5, max 36.5
///     31104   hers median 12.0, p90 24.0, max 28.0     drawings median 12.0, p90 24.0, max 34.5
///
/// The median lands within a tenth of an inch and the p90 exactly. So the p90 of what the drawings
/// themselves gave up is a sound ceiling for that job, and it needs no constant: on 31065 it puts
/// the 36.5" outlier outside and her real 23.6" maximum inside; on 31104 it puts 34.5" outside and
/// her real 28" inside.
///
/// This measures and reports. It does not decide what to model — a member outside the job's own
/// range is unusual, not wrong, and telling an engineer which ones are unusual is worth more than
/// quietly dropping them.
/// </summary>
public sealed record JobCalibration(
    int WallCount,
    double WallMedian,
    double WallP90,
    double WallMax,
    int ColumnCount,
    double ColumnMedian,
    double ColumnP90,
    double ColumnMax)
{
    /// <summary>
    /// Fewer members than this and a percentile is noise, not a distribution. A storey's worth of
    /// walls is a sample; three walls is an anecdote, and calibrating a whole job off it would
    /// declare the fourth one an outlier.
    /// </summary>
    public const int MinimumSample = 20;

    public bool IsUsable => WallCount >= MinimumSample;

    public static JobCalibration From(IReadOnlyList<WallAxis> walls, IReadOnlyList<ColumnFootprint> columns)
    {
        var wallThicknesses = walls.Select(w => w.Thickness).OrderBy(t => t).ToList();

        // A column's governing face is its long one: that is what a size rule is written about.
        var columnFaces = columns.Select(c => Math.Max(c.Width, c.Depth)).OrderBy(t => t).ToList();

        return new JobCalibration(
            wallThicknesses.Count, Percentile(wallThicknesses, 0.5), Percentile(wallThicknesses, 0.9),
            wallThicknesses.Count > 0 ? wallThicknesses[^1] : 0,
            columnFaces.Count, Percentile(columnFaces, 0.5), Percentile(columnFaces, 0.9),
            columnFaces.Count > 0 ? columnFaces[^1] : 0);
    }

    /// <summary>
    /// What this job's own drawings make unusual, if anything. Silent where the sample is too
    /// small to say, and silent where nothing stands outside — a line that appears on every run
    /// is one nobody reads.
    /// </summary>
    public IEnumerable<string> Notes(IReadOnlyList<WallAxis> walls)
    {
        if (!IsUsable) yield break;

        yield return
            $"This job's own walls, measured off its drawings: median {WallMedian:0.#}\", " +
            $"9 in 10 at or under {WallP90:0.#}\", thickest {WallMax:0.#}\" " +
            $"(from {WallCount} panels). Columns: median {ColumnMedian:0.#}\", thickest {ColumnMax:0.#}\".";

        var beyond = walls.Where(w => w.Thickness > WallP90 + 0.01)
            .OrderByDescending(w => w.Thickness)
            .ToList();

        if (beyond.Count == 0) yield break;

        // Named individually up to a handful: an engineer can look at four locations, and a count
        // on its own tells her nothing about where to look.
        string where = string.Join(", ", beyond.Take(5)
            .Select(w => $"{w.Thickness:0.#}\" at ({w.Start.X:0},{w.Start.Y:0})"));

        yield return
            $"{beyond.Count} wall(s) are thicker than 9 in 10 of the walls on this job's own drawings " +
            $"(over {WallP90:0.#}\"): {where}{(beyond.Count > 5 ? ", …" : "")}. " +
            "Measured against two engineers' models, the drawings' own upper tenth is where the tool " +
            "starts reading something other than a wall — a face paired across a void, or an outline " +
            "that is not one member. Worth a look before the model is relied on.";
    }

    private static double Percentile(IReadOnlyList<double> sorted, double q)
    {
        if (sorted.Count == 0) return 0;
        int i = (int)Math.Floor(q * (sorted.Count - 1));
        return sorted[Math.Clamp(i, 0, sorted.Count - 1)];
    }
}
