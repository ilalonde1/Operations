#nullable enable
namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// What moved between two corpus runs, set by set (intake step 59, 2026-09-13). Two ledgers of the
/// same corpus are laid side by side and every set that built in both is put in ONE class by the first
/// thing that changed: its storeys, then which sheets stand on the grid, then - with the same storeys and
/// the same placement - what the composer made of the same sheets. Run 7 against run 8 (steps 47-57)
/// was read that way: 29 sets new, 27 with more storeys (step 47's words), 13 with a different
/// placement, and 117 with the same storeys and the same placement whose columns, walls or plates
/// moved - the page frame (step 54) stacking the sheets that stand on no grid where the drafter put
/// them on the page, where the content centroid had stacked them before. The six-set gate could not
/// see that class: all six sets stand on grids.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: the class of every set, the sums of columns, walls and plates each class moved,
/// and the yardstick's verdict on the sets that have one (within 100 mm, before and after). WHAT IT
/// DOES NOT: why a set moved (the ledger holds counts, not members - `dxf-inspect --members` on the
/// two builds does); a set whose placement changed to the same COUNT of sheets but different sheets
/// (SheetsPlaced is a count); the sheet rows (reading is judged by the per-sheet column sum, which the
/// verb prints beside the classes).
/// </remarks>
public static class CorpusDiff
{
    public enum Change { NewModel, LostModel, Storeys, Placement, Composition, Unchanged }

    public sealed record Mover(string Job, Change Change, CorpusAnalyzer.SetRow Before, CorpusAnalyzer.SetRow After)
    {
        public int Columns => (After.Columns ?? 0) - (Before.Columns ?? 0);
        public int Walls => (After.Walls ?? 0) - (Before.Walls ?? 0);
        public int Plates => (After.StoreysWithPlate ?? 0) - (Before.StoreysWithPlate ?? 0);
        public bool HasYardstick => Before.OursCompared > 0 && After.OursCompared > 0;
        public int Within100 => (After.OursWithin100 ?? 0) - (Before.OursWithin100 ?? 0);
    }

    public sealed record Report(IReadOnlyList<Mover> Movers, IReadOnlyList<string> OnlyBefore, IReadOnlyList<string> OnlyAfter)
    {
        public IEnumerable<Mover> Of(Change c) => Movers.Where(m => m.Change == c);
    }

    /// <summary>Every set in either run, classed by the first thing that changed between the two.</summary>
    public static Report Compare(IReadOnlyList<CorpusAnalyzer.SetRow> before, IReadOnlyList<CorpusAnalyzer.SetRow> after)
    {
        var a = before.ToDictionary(s => s.Job, StringComparer.OrdinalIgnoreCase);
        var b = after.ToDictionary(s => s.Job, StringComparer.OrdinalIgnoreCase);
        var movers = new List<Mover>();
        foreach (var job in a.Keys.Intersect(b.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(j => j, StringComparer.OrdinalIgnoreCase))
            movers.Add(new Mover(job, Classify(a[job], b[job]), a[job], b[job]));
        return new Report(movers,
            a.Keys.Except(b.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(j => j).ToList(),
            b.Keys.Except(a.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(j => j).ToList());
    }

    public static Change Classify(CorpusAnalyzer.SetRow x, CorpusAnalyzer.SetRow y)
    {
        if (!x.HasModel && y.HasModel) return Change.NewModel;
        if (x.HasModel && !y.HasModel) return Change.LostModel;
        if (!x.HasModel) return Change.Unchanged;
        if ((x.StoreysBuilt ?? 0) != (y.StoreysBuilt ?? 0)) return Change.Storeys;
        if ((x.SheetsPlaced ?? 0) != (y.SheetsPlaced ?? 0)) return Change.Placement;
        if ((x.Columns ?? 0) != (y.Columns ?? 0) || (x.Walls ?? 0) != (y.Walls ?? 0) || (x.StoreysWithPlate ?? 0) != (y.StoreysWithPlate ?? 0)) return Change.Composition;
        return Change.Unchanged;
    }
}
