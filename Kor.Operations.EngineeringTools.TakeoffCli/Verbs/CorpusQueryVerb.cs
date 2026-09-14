// QUESTIONS TO THE LEDGER, no PDF opened (completion plan WP2, 2026-09-11; the prototypes were
// docs/etabs-handoff/corpus/plan_titles.py and set_sheets.py). The ledger is what corpus-analyze wrote
// (%LOCALAPPDATA%\Temp\kor-drawings\corpus\ledger-sets.csv and ledger-sheets.csv, or a banked copy under
// docs/etabs-handoff/corpus/ given with --ledger <dir>); analysis.IntakeSet/IntakeSheet hold the same rows
// once migration 083 is applied.
//   takeoff corpus-query summary                     the population in one table: built, why not, views on the grid, plates, yardsticks
//   takeoff corpus-query no-model                    every set without a model, grouped by the reason it gave
//   takeoff corpus-query plan-titles                 how the plan sheets name their storeys: with/without a level, and the words the nameless titles repeat
//   takeoff corpus-query set <job> [<job> ...]       one set's sheets: page, number, level, scale, view written, placed
//   takeoff corpus-query yardsticks                  every set measured against the engineer's own model, worst first
//   takeoff corpus-query diff <before-sets.csv>      what moved between that banked run and this ledger, set by set, classed by the first thing that changed
// Add --ledger <dir> to any of them.
internal static class CorpusQueryVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("corpus-query", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: takeoff corpus-query summary|no-model|plan-titles|set <job>...|yardsticks|diff <before-sets.csv> [--ledger <dir>]"); return 1; }
        string dir = Path.Combine(DrawingMirror.Root, "corpus");
        var rest = new List<string>();
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i].Equals("--ledger", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) dir = args[++i];
            else rest.Add(args[i]);
        }
        // --ledger names the analyzer's folder, or a banked sets CSV (docs/etabs-handoff/corpus/ledger-sets-<date>-<run>.csv)
        // whose sheets file sits beside it under the same suffix
        string setsPath = File.Exists(dir) ? dir : Path.Combine(dir, "ledger-sets.csv");
        string sheetsPath = File.Exists(dir) ? Path.Combine(Path.GetDirectoryName(dir) ?? ".", Path.GetFileName(dir).Replace("ledger-sets", "ledger-sheets", StringComparison.OrdinalIgnoreCase)) : Path.Combine(dir, "ledger-sheets.csv");
        if (!File.Exists(setsPath)) { Console.Error.WriteLine($"No ledger at {setsPath}; run corpus-analyze first, or give --ledger <dir|sets.csv>."); return 2; }
        var sets = CorpusAnalyzer.ReadSets(setsPath);
        var sheets = File.Exists(sheetsPath) ? CorpusAnalyzer.ReadSheets(sheetsPath) : [];
        Console.WriteLine($"ledger {dir}: {sets.Count} sets, {sheets.Count} sheets" + (sets.Count > 0 ? $", run {sets[0].RunAtUtc:yyyy-MM-dd HH:mm} UTC" : ""));

        switch (args[1].ToLowerInvariant())
        {
            case "summary": return Summary(sets, sheets);
            case "no-model": return NoModel(sets);
            case "plan-titles": return PlanTitles(sheets);
            case "set": return Sets(rest, sheets, sets);
            case "yardsticks": return Yardsticks(sets);
            case "diff": return Diff(rest, sets, sheets);
            default: Console.Error.WriteLine($"Unknown question '{args[1]}'."); return 1;
        }
    }

    private static int Summary(IReadOnlyList<CorpusAnalyzer.SetRow> sets, IReadOnlyList<CorpusAnalyzer.SheetRow> sheets)
    {
        int built = sets.Count(s => s.HasModel);
        var plans = sheets.Where(s => s.SheetType == "plan").ToList();
        var views = plans.Where(s => s.DxfFiles is not null).ToList();
        Console.WriteLine($"  sets that build a model      {built} of {sets.Count}");
        foreach (var g in sets.Where(s => !s.HasModel).GroupBy(s => Reason(s)).OrderByDescending(g => g.Count()))
            Console.WriteLine($"    no model: {g.Key,-48} {g.Count()}");
        Console.WriteLine($"  pages read                   {sets.Sum(s => s.Pages)}; plans {plans.Count}; failed {sets.Sum(s => s.SheetsFailed)}");
        Console.WriteLine($"  plan views written           {views.Count}; placed on the grid by name {views.Count(v => v.Placed == true)} ({Pct(views.Count(v => v.Placed == true), views.Count)})");
        int storeys = sets.Where(s => s.HasModel).Sum(s => s.StoreysBuilt ?? 0), plated = sets.Where(s => s.HasModel).Sum(s => s.StoreysWithPlate ?? 0);
        Console.WriteLine($"  storeys built                {storeys}; with a plate {plated} ({Pct(plated, storeys)})");
        Console.WriteLine($"  walls / columns              {sets.Sum(s => s.Walls ?? 0)} / {sets.Sum(s => s.Columns ?? 0)}");
        // a set where she modelled columns on a shared storey and none of ours stand inside her footprint is a complete
        // recall miss, counted in "theirs within 100 mm" (audit F19, step 61), not filtered out with the sets that share no storey
        var y = sets.Where(s => s.OursCompared > 0 || s.TheirsCompared > 0).ToList();
        Console.WriteLine($"  yardsticks                   {y.Count} sets ({y.Count(s => s.OursCompared > 0)} with ours inside her footprint): ours within 100 mm {Pct(y.Sum(s => s.OursWithin100 ?? 0), y.Sum(s => s.OursCompared ?? 0))}, theirs within 100 mm {Pct(y.Sum(s => s.TheirsWithin100 ?? 0), y.Sum(s => s.TheirsCompared ?? 0))}");
        Console.WriteLine($"  time                         {TimeSpan.FromSeconds(sets.Sum(s => s.Seconds)):h\\:mm\\:ss} of building, summed");
        return 0;
    }

    private static string Reason(CorpusAnalyzer.SetRow s)
    {
        string e = s.ModelError ?? s.Error ?? "(no reason recorded)";
        // the composer's first sentence is the reason; the rest is its evidence
        int cut = e.IndexOfAny([':', ';', '(']);
        return (cut > 8 ? e[..cut] : e).Trim();
    }

    private static int NoModel(IReadOnlyList<CorpusAnalyzer.SetRow> sets)
    {
        foreach (var g in sets.Where(s => !s.HasModel).GroupBy(Reason).OrderByDescending(g => g.Count()))
        {
            Console.WriteLine($"  {g.Count()} × {g.Key}");
            foreach (var s in g.OrderBy(s => s.Job, StringComparer.Ordinal))
                Console.WriteLine($"      {s.Job,-10} {s.Pages,3} pages, {s.PlanSheets,3} plans, {s.StoreysRead,3} storeys read   {(s.ModelError ?? s.Error ?? "")[..Math.Min(90, (s.ModelError ?? s.Error ?? "").Length)]}");
        }
        return 0;
    }

    private static int PlanTitles(IReadOnlyList<CorpusAnalyzer.SheetRow> sheets)
    {
        var plans = sheets.Where(s => s.SheetType == "plan").ToList();
        // the question is the composer's: which written views can PlanSheetNaming put on no storey at all —
        // no level number, no parkade level, not a roof, not a foundation — and what do THEIR names say
        var views = plans.Where(s => s.DxfFiles is not null).SelectMany(s => CorpusAnalyzer.DxfFilesOf(s.DxfFiles).Select(f => (Sheet: s, File: f))).Where(v => v.File.Length > 0).ToList();
        var nameless = views.Where(v =>
        {
            var info = PlanSheetNaming.Parse(v.File);
            return !info.HasPlacement && info.MezzanineLevels.Count == 0;
        }).ToList();
        Console.WriteLine($"  {plans.Count} plan pages; {plans.Count(s => s.Level is not null)} carry a level from the reader; {views.Count} views written; {nameless.Count} views the composer can put on no storey by name ({nameless.Select(v => v.Sheet.Job).Distinct().Count()} sets)");
        var words = new Dictionary<string, int>(StringComparer.Ordinal);
        var titles = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, file) in nameless)
        {
            string t = Path.GetFileNameWithoutExtension(file).ToUpperInvariant();
            if (t.Length > 0) titles[t] = titles.GetValueOrDefault(t) + 1;
            foreach (Match m in Regex.Matches(t, @"[A-Z][A-Z/]+"))
                if (m.Value.Length > 2) words[m.Value] = words.GetValueOrDefault(m.Value) + 1;
        }
        Console.WriteLine($"  the words those {nameless.Count} view names repeat");
        foreach (var (w, n) in words.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(40))
            Console.WriteLine($"    {n,5}  {w}");
        Console.WriteLine("  and the whole titles most repeated");
        foreach (var (t, n) in titles.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(40))
            Console.WriteLine($"    {n,5}  {t}");
        return 0;
    }

    private static int Sets(List<string> jobs, IReadOnlyList<CorpusAnalyzer.SheetRow> sheets, IReadOnlyList<CorpusAnalyzer.SetRow> sets)
    {
        if (jobs.Count == 0) { Console.Error.WriteLine("Usage: takeoff corpus-query set <job> [<job> ...]"); return 1; }
        foreach (string job in jobs)
        {
            var set = sets.FirstOrDefault(s => s.Job.Equals(job, StringComparison.OrdinalIgnoreCase));
            var rs = sheets.Where(s => s.Job.Equals(job, StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.Page).ToList();
            if (set is null && rs.Count == 0) { Console.WriteLine($"{job}: not in the ledger"); continue; }
            var plans = rs.Where(s => s.SheetType == "plan").ToList();
            Console.WriteLine($"{job}: {rs.Count} pages, {plans.Count} plans; with a sheet number {plans.Count(s => s.SheetNumber is not null)}, with a level {plans.Count(s => s.Level is not null)}, with a title {plans.Count(s => s.Title is not null)}, with a view {plans.Count(s => s.DxfFiles is not null)}"
                + (set is null ? "" : set.HasModel ? $"; model: {set.StoreysBuilt} storeys, {set.Walls} walls, {set.Columns} columns, {set.SheetsPlaced}/{set.SheetsWritten} placed" : $"; no model: {set.ModelError ?? set.Error}"));
            foreach (var r in rs)
                Console.WriteLine($"   p{r.Page:00}  {r.SheetType,-9} {r.SheetNumber ?? "-",-10} level={r.Level ?? "-",-8} scale={r.ScaleNote ?? "-",-14} placed={(r.Placed is null ? "-" : r.Placed.Value ? "yes" : "no"),-4} {(r.DxfFiles ?? r.Title ?? "")[..Math.Min(70, (r.DxfFiles ?? r.Title ?? "").Length)]}");
        }
        return 0;
    }

    private static int Yardsticks(IReadOnlyList<CorpusAnalyzer.SetRow> sets)
    {
        var y = sets.Where(s => s.Yardstick is not null).OrderBy(s => s.OursCompared > 0 ? (double)(s.OursWithin100 ?? 0) / s.OursCompared.Value : -1).ToList();
        Console.WriteLine($"  {y.Count} sets have the engineer's own model; {y.Count(s => s.OursCompared > 0)} share a storey with columns");
        Console.WriteLine($"  {"job",-10} {"storeys",7} {"shared",6} {"frame",-8} {"ours≤100",9} {"theirs≤100",10}  note");
        foreach (var s in y)
            Console.WriteLine($"  {s.Job,-10} {s.YardstickStoreys,7} {s.SharedStoreys,6} {(s.FrameFromGrids == true ? "grids" : s.FrameFromGrids == false ? $"cols/{s.FrameSupport}" : "-"),-8} {Pct(s.OursWithin100 ?? 0, s.OursCompared ?? 0),9} {Pct(s.TheirsWithin100 ?? 0, s.TheirsCompared ?? 0),10}  {s.YardstickNote}");
        return 0;
    }

    /// <summary>
    /// What moved between a banked run and this ledger (step 59): every set in one class by the first thing
    /// that changed - a model gained or lost, its storeys, which sheets stand on the grid, or with both the same
    /// the composition - with the columns, walls and plates each class moved and the yardstick's verdict where a
    /// set has one. Reading is judged apart, by the per-sheet column sum of the two sheet ledgers.
    /// </summary>
    private static int Diff(List<string> rest, IReadOnlyList<CorpusAnalyzer.SetRow> after, IReadOnlyList<CorpusAnalyzer.SheetRow> afterSheets)
    {
        if (rest.Count != 1 || !File.Exists(rest[0])) { Console.Error.WriteLine("Usage: takeoff corpus-query diff <before-sets.csv> [--ledger <dir|after-sets.csv>]"); return 1; }
        var before = CorpusAnalyzer.ReadSets(rest[0]);
        string beforeSheetsPath = Path.Combine(Path.GetDirectoryName(rest[0]) ?? ".", Path.GetFileName(rest[0]).Replace("ledger-sets", "ledger-sheets", StringComparison.OrdinalIgnoreCase));
        var beforeSheets = File.Exists(beforeSheetsPath) ? CorpusAnalyzer.ReadSheets(beforeSheetsPath) : [];
        var r = CorpusDiff.Compare(before, after);
        Console.WriteLine($"  before {rest[0]}: {before.Count} sets, {before.Count(s => s.HasModel)} with a model, run {before.FirstOrDefault()?.RunAtUtc:yyyy-MM-dd HH:mm} UTC");
        Console.WriteLine($"  after  {after.Count} sets, {after.Count(s => s.HasModel)} with a model");
        if (r.OnlyBefore.Count > 0) Console.WriteLine($"  only before ({r.OnlyBefore.Count}): {string.Join(" ", r.OnlyBefore)}");
        if (r.OnlyAfter.Count > 0) Console.WriteLine($"  only after ({r.OnlyAfter.Count}): {string.Join(" ", r.OnlyAfter)}");
        if (beforeSheets.Count > 0 && afterSheets.Count > 0)
            Console.WriteLine($"  reading (per-sheet sums over {beforeSheets.Count} -> {afterSheets.Count} sheets): columns {beforeSheets.Sum(s => s.Columns)} -> {afterSheets.Sum(s => s.Columns)}, walls {beforeSheets.Sum(s => s.Walls)} -> {afterSheets.Sum(s => s.Walls)}, placed {beforeSheets.Count(s => s.Placed == true)} -> {afterSheets.Count(s => s.Placed == true)}");
        Console.WriteLine($"  {"class",-12} {"sets",5} {"columns",9} {"walls",8} {"plates",7}  {"yardstick by SHARE better / worse / same",-40} ours within 100 mm before -> after (theirs)");
        foreach (var c in new[] { CorpusDiff.Change.NewModel, CorpusDiff.Change.LostModel, CorpusDiff.Change.Storeys, CorpusDiff.Change.Views, CorpusDiff.Change.Placement, CorpusDiff.Change.Composition, CorpusDiff.Change.SameCounts })
        {
            var m = r.Of(c).ToList();
            var y = m.Where(x => x.HasYardstick).ToList();
            string verdict = $"{y.Count(x => x.Verdict > 0)} / {y.Count(x => x.Verdict < 0)} / {y.Count(x => x.Verdict == 0)}";
            Console.WriteLine($"  {c,-12} {m.Count,5} {m.Sum(x => x.Columns),9:+#;-#;0} {m.Sum(x => x.Walls),8:+#;-#;0} {m.Sum(x => x.Plates),7:+#;-#;0}  {verdict,-40} {y.Sum(x => x.Before.OursWithin100 ?? 0)} of {y.Sum(x => x.Before.OursCompared ?? 0)} -> {y.Sum(x => x.After.OursWithin100 ?? 0)} of {y.Sum(x => x.After.OursCompared ?? 0)} (theirs {y.Sum(x => x.Before.TheirsWithin100 ?? 0)} of {y.Sum(x => x.Before.TheirsCompared ?? 0)} -> {y.Sum(x => x.After.TheirsWithin100 ?? 0)} of {y.Sum(x => x.After.TheirsCompared ?? 0)})");
        }
        Console.WriteLine("  movers, largest column change first:");
        Console.WriteLine($"  {"job",-10} {"class",-12} {"storeys",9} {"placed",11} {"columns",15} {"walls",15} {"plates",9}  yardstick within 100 mm");
        foreach (var m in r.Movers.Where(m => m.Change is not CorpusDiff.Change.SameCounts).OrderByDescending(m => Math.Abs(m.Columns)).ThenBy(m => m.Job))
        {
            string storeys = $"{m.Before.StoreysBuilt}->{m.After.StoreysBuilt}", placed = $"{m.Before.SheetsPlaced}->{m.After.SheetsPlaced}/{m.After.SheetsWritten}";
            string columns = $"{m.Before.Columns}->{m.After.Columns}", walls = $"{m.Before.Walls}->{m.After.Walls}", plates = $"{m.Before.StoreysWithPlate}->{m.After.StoreysWithPlate}";
            string yard = m.HasYardstick ? $"{m.Before.OursWithin100} of {m.Before.OursCompared} -> {m.After.OursWithin100} of {m.After.OursCompared}" : "-";
            Console.WriteLine($"  {m.Job,-10} {m.Change,-12} {storeys,9} {placed,11} {columns,15} {walls,15} {plates,9}  {yard}");
        }
        return 0;
    }

    private static string Pct(int n, int of) => of == 0 ? "-" : $"{100.0 * n / of:0}% ({n}/{of})";
}
