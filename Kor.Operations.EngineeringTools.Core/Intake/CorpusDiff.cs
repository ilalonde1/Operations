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
/// two builds does); a set whose placement or composition changed to the same COUNTS (SameCounts says
/// counts, not sameness); the sheet rows (reading is judged by the per-sheet column sum, which the
/// verb prints beside the classes).
/// </remarks>
public static class CorpusDiff
{
    /// <summary>
    /// The classes, in the order the first change is looked for. SameCounts is what its name says (the second
    /// audit's finding 7: a set whose one column came from another sheet at another place, with every count the
    /// same, is here - the ledger holds counts, not members); Views is a set whose sheets read differed.
    /// </summary>
    public enum Change { NewModel, LostModel, Storeys, Views, Placement, Composition, SameCounts }

    public sealed record Mover(string Job, Change Change, CorpusAnalyzer.SetRow Before, CorpusAnalyzer.SetRow After)
    {
        public int Columns => (After.Columns ?? 0) - (Before.Columns ?? 0);
        public int Walls => (After.Walls ?? 0) - (Before.Walls ?? 0);
        public int Plates => (After.StoreysWithPlate ?? 0) - (Before.StoreysWithPlate ?? 0);
        /// <summary>A yardstick judged the set in both runs: columns of ours or of hers on a shared storey (a complete recall miss counts; the second audit's finding 8).</summary>
        public bool HasYardstick => (Before.OursCompared > 0 || Before.TheirsCompared > 0) && (After.OursCompared > 0 || After.TheirsCompared > 0);
        public int Within100 => (After.OursWithin100 ?? 0) - (Before.OursWithin100 ?? 0);
        /// <summary>The share of ours within 100 mm of one of hers, 0 where none of ours was judged (a complete recall miss).</summary>
        public static double Share(CorpusAnalyzer.SetRow r) => r.OursCompared > 0 ? (double)(r.OursWithin100 ?? 0) / r.OursCompared.Value : 0;
        /// <summary>The verdict is by SHARE, not by count (the second audit's finding 9: 8 of 64 -> 5 of 10 is better): +1 better, -1 worse, 0 the same to half a point.</summary>
        public int Verdict => Share(After) - Share(Before) is double d && Math.Abs(d) < 0.005 ? 0 : Share(After) > Share(Before) ? 1 : -1;
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
        if (!x.HasModel) return Change.SameCounts;
        if ((x.StoreysBuilt ?? 0) != (y.StoreysBuilt ?? 0)) return Change.Storeys;
        if (x.SheetsWritten != y.SheetsWritten || x.PlanSheets != y.PlanSheets) return Change.Views;
        if ((x.SheetsPlaced ?? 0) != (y.SheetsPlaced ?? 0)) return Change.Placement;
        if ((x.Columns ?? 0) != (y.Columns ?? 0) || (x.Walls ?? 0) != (y.Walls ?? 0) || (x.StoreysWithPlate ?? 0) != (y.StoreysWithPlate ?? 0)) return Change.Composition;
        return Change.SameCounts;
    }
}
