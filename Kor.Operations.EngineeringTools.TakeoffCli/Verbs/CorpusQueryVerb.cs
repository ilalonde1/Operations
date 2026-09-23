// QUESTIONS TO THE LEDGER, no PDF opened (completion plan WP2, 2026-09-11; the prototypes were
// docs/etabs-handoff/corpus/plan_titles.py and set_sheets.py). The ledger is what corpus-analyze wrote
// (%LOCALAPPDATA%\Temp\kor-drawings\corpus\ledger-sets.csv and ledger-sheets.csv, or a banked copy under
// docs/etabs-handoff/corpus/ given with --ledger <dir>); analysis.IntakeSet/IntakeSheet hold the same rows
// once migration 083 is applied.
//   takeoff corpus-query summary                     the population in one table: built, why not, views on the grid, plates, yardsticks
//   takeoff corpus-query no-model                    every set without a model, grouped by the reason it gave
//   takeoff corpus-query plan-titles                 how the plan sheets name their storeys: with/without a level, and the words the nameless titles repeat
//   takeoff corpus-query set <job> [<job> ...]       one set's sheets: page, number, level, scale, view written, placed
//   takeoff corpus-query columns <job>              each composed DXF view's column origins and wall containment
//   takeoff corpus-query plates [<job> ...]         every built set's storeys with and without a plate, and where each missing plate was lost (WP6a item 2)
//   takeoff corpus-query yardsticks                  every set measured against the engineer's own model, worst first
//   takeoff corpus-query frames                      what stacks by its page frame and not by a grid: the F11 blast radius, upper bound
//   takeoff corpus-query dropped [<job> ...]         every plan sheet that read slabs and gave the model no storey, classed by why - the twins first
//   takeoff corpus-query diff <before-sets.csv>      what moved between that banked run and this ledger, set by set, classed by the first thing that changed
//   takeoff corpus-query storeys [<job> ...]       every built set's storey names classed: the ladder's shapes, roofs by word, sub-levels, elevations, GARBAGE (item 7 is done at 0)
//   takeoff corpus-query grid-names [<job> ...]   what the sets call their grids: every grid-layer text, taken as a name or refused (step 89: A1-5 is a name)
//   takeoff corpus-query pages <job> [--runs a,b]    one set's pages run against run, from analysis.IntakeSheet: the pages placed differently, on what storeys (default: the last two runs)
// Add --ledger <dir> to any of them.
internal static class CorpusQueryVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("corpus-query", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: takeoff corpus-query summary|no-model|plan-titles|set <job>...|columns <job>|plates [<job>...]|yardsticks|frames|dropped [<job>...]|diff <before-sets.csv>|pages <job> [--runs a,b]|storeys|grid-names [<job>...] [--ledger <dir>]"); return 1; }
        if (args[1].Equals("pages", StringComparison.OrdinalIgnoreCase)) return Pages(args.Skip(2).ToList());
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
        // Column inspection can also use a retained work folder after its ledger has been moved.
        if (args[1].Equals("columns", StringComparison.OrdinalIgnoreCase))
            return Columns(rest, File.Exists(dir) ? Path.GetDirectoryName(Path.GetFullPath(dir)) ?? "." : dir);
        if (args[1].Equals("plates", StringComparison.OrdinalIgnoreCase))
            return Plates(rest, File.Exists(dir) ? Path.GetDirectoryName(Path.GetFullPath(dir)) ?? "." : dir);
        if (args[1].Equals("storeys", StringComparison.OrdinalIgnoreCase))
            return Storeys(rest, File.Exists(dir) ? Path.GetDirectoryName(Path.GetFullPath(dir)) ?? "." : dir);
        if (args[1].Equals("grid-names", StringComparison.OrdinalIgnoreCase))
            return GridNames(rest, File.Exists(dir) ? Path.GetDirectoryName(Path.GetFullPath(dir)) ?? "." : dir);
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
            case "frames": return Frames(sets, sheets);
            case "dropped": return Dropped(rest, sheets);
            case "diff": return Diff(rest, sets, sheets);
            default: Console.Error.WriteLine($"Unknown question '{args[1]}'."); return 1;
        }
    }

    private static int Summary(IReadOnlyList<CorpusAnalyzer.SetRow> sets, IReadOnlyList<CorpusAnalyzer.SheetRow> sheets)
    {
        int built = sets.Count(s => s.HasModel);
        var plans = sheets.Where(s => s.SheetType == "plan").ToList();
        var views = plans.Where(s => s.DxfFiles is not null).ToList();
        // 17 job folders hold a byte-identical copy of another job's stick file (00904-01's): not ours to build, so the
        // honest denominator is the sets with a stick file of their own (WP6a item 5, 2026-09-16)
        int othersFile = sets.Count(s => !s.HasModel && (s.ModelError ?? s.Error ?? "").StartsWith(CorpusAnalyzer.AnotherJobsFileReason, StringComparison.Ordinal));
        Console.WriteLine($"  sets that build a model      {built} of {sets.Count}" + (othersFile > 0 ? $" ({built} of {sets.Count - othersFile} with a stick file of their own; {othersFile} hold another job's)" : ""));
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

    /// <summary>
    /// STOREYS NAMED AS THE SET NAMES THEM (WP6a item 7): every built set's storey names in the work folder, classed
    /// by StoreyNameClass - the ladder's own shapes, roofs and mezzanines by word, elevations as names, and garbage
    /// (a title's words taken for a storey), the garbage listed set by set. Item 7 is done when garbage is 0.
    /// </summary>
    private static int Storeys(List<string> jobs, string root)
    {
        var dirs = Directory.EnumerateDirectories(root)
            .Where(d => File.Exists(Path.Combine(d, "out.e2k")))
            .Where(d => jobs.Count == 0 || jobs.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
        if (dirs.Count == 0) { Console.Error.WriteLine($"No built sets under '{root}'."); return 2; }
        var byKind = new Dictionary<StoreyNameClass.Kind, int>();
        var examples = new Dictionary<StoreyNameClass.Kind, List<string>>();
        var garbage = new List<(string Job, string Name)>();
        int storeys = 0;
        foreach (string d in dirs)
        {
            string job = Path.GetFileName(d);
            foreach (var s in E2kDocument.Load(Path.Combine(d, "out.e2k")).ReadStories())
            {
                storeys++;
                var kind = StoreyNameClass.Classify(s.Name);
                byKind[kind] = byKind.GetValueOrDefault(kind) + 1;
                var list = examples.TryGetValue(kind, out var l) ? l : examples[kind] = new List<string>();
                if (!list.Contains(s.Name) && list.Count < 12) list.Add(s.Name);
                if (kind == StoreyNameClass.Kind.Garbage) garbage.Add((job, s.Name));
            }
        }
        Console.WriteLine($"  {dirs.Count} built sets, {storeys} storeys (Base included), by the kind of name:");
        foreach (var kind in Enum.GetValues<StoreyNameClass.Kind>())
            Console.WriteLine($"    {kind,-15} {byKind.GetValueOrDefault(kind),6}   {string.Join(", ", examples.GetValueOrDefault(kind) ?? [])}");
        Console.WriteLine($"  garbage names (a title's words taken for a storey): {garbage.Count}" + (garbage.Count > 0 ? "" : " - item 7's finish line"));
        foreach (var (job, name) in garbage) Console.WriteLine($"    {job}  \"{name}\"");
        return 0;
    }

    /// <summary>
    /// WHAT THE SETS CALL THEIR GRIDS (step 89, 2026-09-16): every grid-layer text of every written plan DXF under the
    /// work dir, split by whether <see cref="GridAlignment.IsGridName"/> takes it as a name — per set the names taken,
    /// the ones refused, and examples of each. The measurement behind "a grid name may carry the building's tag" (16 of
    /// 296 sets write A1-5, E-P1, 0-11 …); the refused column is where the next class of name shows itself.
    /// </summary>
    private static int GridNames(List<string> jobs, string root)
    {
        var dirs = Directory.EnumerateDirectories(root)
            .Where(d => Directory.Exists(Path.Combine(d, "dxf")))
            .Where(d => jobs.Count == 0 || jobs.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
        if (dirs.Count == 0) { Console.Error.WriteLine($"No read sets under '{root}'."); return 2; }
        var rows = new List<(string Job, int Taken, int Refused, List<string> TakenNames, List<string> RefusedNames)>();
        foreach (string d in dirs)
        {
            var taken = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var refused = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string dxf in Directory.EnumerateFiles(Path.Combine(d, "dxf"), "*.dxf"))
                foreach (var t in DxfPlanReader.ReadPositionedTags(dxf))
                {
                    if (!t.Layer.Contains("GRID", StringComparison.OrdinalIgnoreCase)) continue;
                    string text = t.Text.Trim();
                    if (text.Length == 0) continue;
                    var names = GridAlignment.GridNamesIn(text);
                    if (names.Count == 0) refused[text] = refused.GetValueOrDefault(text) + 1;
                    foreach (string name in names) taken[name] = taken.GetValueOrDefault(name) + 1;
                }
            if (taken.Count + refused.Count == 0) continue;
            rows.Add((Path.GetFileName(d), taken.Values.Sum(), refused.Values.Sum(),
                taken.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(), refused.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList()));
        }
        int prefixed = rows.Count(r => r.TakenNames.Any(n => n.Contains('-')));
        Console.WriteLine($"  {rows.Count} of {dirs.Count} read sets write text on a grid layer; {prefixed} name a grid with the building's tag (A1-5); " +
                          $"{rows.Count(r => r.Refused > 0)} carry text the name rule refuses");
        Console.WriteLine($"  {"job",-10} {"taken",6} {"refused",8}  names taken (first 10) | refused (most frequent first, 6)");
        foreach (var r in rows.OrderByDescending(r => r.Refused).ThenBy(r => r.Job, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"  {r.Job,-10} {r.Taken,6} {r.Refused,8}  {string.Join(" ", r.TakenNames.Take(10))} | {string.Join(" ", r.RefusedNames.Take(6))}");
        return 0;
    }

    /// <summary>
    /// One set's pages, run against run, from the database (analysis.IntakeSheet): the pages the runs placed
    /// differently, each run's placed flag and storeys, so "run N placed more sheets" can be read page by page.
    /// </summary>
    private static int Pages(List<string> rest)
    {
        string? job = null; var prefixes = new List<string>(); int last = 2; bool all = false;
        for (int i = 0; i < rest.Count; i++)
        {
            if (rest[i].Equals("--runs", StringComparison.OrdinalIgnoreCase) && i + 1 < rest.Count) prefixes.AddRange(rest[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            else if (rest[i].Equals("--last", StringComparison.OrdinalIgnoreCase) && i + 1 < rest.Count) last = int.Parse(rest[++i], CultureInfo.InvariantCulture);
            else if (rest[i].Equals("--all", StringComparison.OrdinalIgnoreCase)) all = true;
            else job ??= rest[i];
        }
        if (job is null) { Console.Error.WriteLine("Usage: takeoff corpus-query pages <job> [--runs <id-prefix>,<id-prefix>] [--last N] [--all]"); return 1; }
        var rows = CorpusAnalyzer.ReadPagesAcrossRuns(job, prefixes, last, null, out string note);
        if (rows.Count == 0) { Console.Error.WriteLine($"{job}: no page rows ({note})."); return 2; }
        var runs = rows.Select(r => (r.RunId, r.RunAtUtc)).Distinct().OrderBy(r => r.RunAtUtc).ToList();
        Console.WriteLine($"{job}: {note}");
        foreach (var (id, at) in runs)
            Console.WriteLine($"   run {id.ToString()[..8]}  {at:yyyy-MM-dd HH:mm} UTC  pages {rows.Count(r => r.RunId == id)}, placed {rows.Count(r => r.RunId == id && r.Placed == true)}");
        var differ = CorpusAnalyzer.PagesThatDiffer(rows);
        var differing = differ.Select(d => d.Page).ToHashSet();
        var shown = all
            ? rows.GroupBy(r => r.Page).OrderBy(g => g.Key).Select(g => (Page: g.Key, ByRun: (IReadOnlyList<CorpusAnalyzer.PageRun?>)runs.Select(x => g.FirstOrDefault(r => r.RunId == x.RunId)).ToList())).ToList()
            : differ.ToList();
        Console.WriteLine(all ? $"   every page; {differ.Count} differ between the runs (*)" : $"   {differ.Count} page(s) placed differently between the runs");
        foreach (var (page, byRun) in shown)
        {
            var first = byRun.FirstOrDefault(r => r is not null);
            string title = first?.DxfFiles ?? first?.Title ?? "";
            Console.WriteLine($"   {(differing.Contains(page) ? "*" : " ")}p{page:00} {first?.SheetNumber ?? "-",-10} {title[..Math.Min(60, title.Length)]}");
            for (int i = 0; i < byRun.Count; i++)
            {
                var r = byRun[i];
                Console.WriteLine(r is null
                    ? $"        {runs[i].RunId.ToString()[..8]}  absent"
                    : $"        {runs[i].RunId.ToString()[..8]}  placed={(r.Placed is null ? "-" : r.Placed.Value ? "yes" : "no"),-4} storeys={r.Storeys ?? "-",-24} cols={r.Columns,4} walls={r.Walls,4} slabs={r.Slabs,3}  title=\"{r.Title}\"");
            }
        }
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

    /// <summary>
    /// A PLATE ON EVERY STOREY (WP6a item 2): every built set in the work folder, its storeys classed by
    /// PlateCoverage - a plate; no sheet placed on the storey; sheets placed but no closed ring read; rings read but no
    /// plate composed - the corpus totals first, then the sets with the most storeys without a plate.
    /// </summary>
    private static int Plates(List<string> jobs, string root)
    {
        var dirs = Directory.EnumerateDirectories(root)
            .Where(d => File.Exists(Path.Combine(d, "out.e2k")) && File.Exists(Path.Combine(d, "sheets.csv")))
            .Where(d => jobs.Count == 0 || jobs.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
        if (dirs.Count == 0) { Console.Error.WriteLine($"No built sets under '{root}'."); return 2; }
        var totals = new Dictionary<PlateCoverage.Class, int>();
        var perSet = new List<(string Job, int Storeys, int Plate, int NoSheet, int NoRing, int RingsNoPlate)>();
        foreach (string d in dirs)
        {
            var e2k = File.ReadLines(Path.Combine(d, "out.e2k"));
            var sheets = CorpusAnalyzer.ReadSheets(Path.Combine(d, "sheets.csv"));
            var storeys = PlateCoverage.Classify(e2k, sheets);
            foreach (var st in storeys) totals[st.Class] = totals.GetValueOrDefault(st.Class) + 1;
            perSet.Add((Path.GetFileName(d), storeys.Count, storeys.Count(s => s.Class == PlateCoverage.Class.Plate),
                storeys.Count(s => s.Class == PlateCoverage.Class.NoSheetPlaced), storeys.Count(s => s.Class == PlateCoverage.Class.NoRingRead),
                storeys.Count(s => s.Class == PlateCoverage.Class.RingsReadNoPlate)));
            if (jobs.Count > 0)
                foreach (var st in storeys)
                    Console.WriteLine($"  {Path.GetFileName(d)} {st.Name,-14} {st.Class,-18} plates {st.Plates} sheets placed {st.PlacedSheets} rings read {st.RingsRead}");
        }
        int all = totals.Values.Sum();
        Console.WriteLine($"{dirs.Count} built sets, {all} storeys:");
        foreach (var c in new[] { PlateCoverage.Class.Plate, PlateCoverage.Class.NoSheetPlaced, PlateCoverage.Class.NoRingRead, PlateCoverage.Class.RingsReadNoPlate })
            Console.WriteLine($"  {c,-18} {totals.GetValueOrDefault(c),6}  ({Pct(totals.GetValueOrDefault(c), all)})");
        Console.WriteLine("  the sets with the most storeys without a plate:");
        Console.WriteLine($"  {"job",-10} {"storeys",7} {"plate",6} {"no sheet",9} {"no ring",8} {"rings, no plate",15}");
        foreach (var s in perSet.OrderByDescending(s => s.Storeys - s.Plate).ThenBy(s => s.Job, StringComparer.Ordinal).Take(jobs.Count > 0 ? perSet.Count : 25))
            Console.WriteLine($"  {s.Job,-10} {s.Storeys,7} {s.Plate,6} {s.NoSheet,9} {s.NoRing,8} {s.RingsNoPlate,15}");
        return 0;
    }

    private static int Columns(List<string> jobs, string root)
    {
        if (jobs.Count != 1 || jobs[0].Length == 0 || !jobs[0].All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
        {
            Console.Error.WriteLine("Usage: takeoff corpus-query columns <job> [--ledger <dir|sets.csv>]");
            return 1;
        }
        string job = jobs[0];
        // corpus-analyze writes one work folder per job under the corpus/ledger root.
        string dxf = Path.Combine(root, job, "dxf");
        if (!Directory.Exists(dxf)) { Console.Error.WriteLine($"No dxf work folder at '{dxf}'."); return 2; }
        var files = Directory.EnumerateFiles(dxf, "*.dxf", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0) { Console.Error.WriteLine($"No DXF views in '{dxf}'."); return 2; }

        Console.WriteLine($"{job}: {files.Count} views from {dxf}; counts are per-sheet column footprints, before storey replication");
        var rows = new List<(string Branch, bool InsideWall)>();
        int failed = 0;
        foreach (string file in files)
        {
            try { rows.AddRange(DxfInspectVerb.InspectColumns(file)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidDataException)
            {
                failed++;
                Console.Error.WriteLine($"{Path.GetFileName(file)}: column inspection failed: {ex.Message}");
            }
        }
        Console.WriteLine($"branch                         inside a wall    not inside    total{(failed > 0 ? " (PARTIAL)" : "")}");
        foreach (var group in rows.GroupBy(r => r.Branch).OrderBy(g => g.Key, StringComparer.Ordinal))
            Console.WriteLine($"{group.Key,-30} {group.Count(r => r.InsideWall),13} {group.Count(r => !r.InsideWall),13} {group.Count(),8}");
        Console.WriteLine($"{"TOTAL",-30} {rows.Count(r => r.InsideWall),13} {rows.Count(r => !r.InsideWall),13} {rows.Count,8}");
        int wallFragments = rows.Count(r => r.InsideWall && r.Branch is "short-wall-layer-loop" or "nothing-paired-up" or "standalone-stub");
        Console.WriteLine($"wall-origin columns inside a wall: {wallFragments}{(failed > 0 ? " (PARTIAL)" : "")}");
        Console.WriteLine($"unknown origins: {rows.Count(r => r.Branch == "unknown")}; failed sheets: {failed}");
        return failed == 0 ? 0 : 2;
    }

    private static int Yardsticks(IReadOnlyList<CorpusAnalyzer.SetRow> sets)
    {
        var y = sets.Where(s => s.Yardstick is not null).OrderBy(s => s.OursCompared > 0 ? (double)(s.OursWithin100 ?? 0) / s.OursCompared.Value : -1).ToList();
        Console.WriteLine($"  {y.Count} sets have the engineer's own model; {y.Count(s => s.OursCompared > 0)} share a storey with columns");
        Console.WriteLine($"  {"job",-10} {"storeys",7} {"shared",6} {"frame",-8} {"ours≤100",9} {"theirs≤100",10} {"her model",-10} {"older by",8}  note");
        foreach (var s in y)
            Console.WriteLine($"  {s.Job,-10} {s.YardstickStoreys,7} {s.SharedStoreys,6} {(s.FrameFromGrids == true ? "grids" : s.FrameFromGrids == false ? $"cols/{s.FrameSupport}" : "-"),-8} {Pct(s.OursWithin100 ?? 0, s.OursCompared ?? 0),9} {Pct(s.TheirsWithin100 ?? 0, s.TheirsCompared ?? 0),10} {(s.YardstickWritten is { } w ? w.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "-"),-10} {(s.YardstickAgeDays is { } d ? $"{d} d" : "-"),8}  {s.YardstickNote}");
        return 0;
    }

    /// <summary>
    /// A STOREY NAMED BY TWO SHEETS KEEPS ONE OF THEM (2026-09-23). This lists every plan sheet that read at
    /// least one slab and gave the model no storey, and classes it by why - the class that matters last, so it
    /// is what the eye lands on: the sheet stood on the model's grid, it states its level, and ANOTHER sheet of
    /// the same job states that same level and took the storey. On run 43 that is 110 sheets and 2,460 slabs.
    ///
    /// It is one fault wearing three faces: 30990-01 draws each parkade level twice, Tower A and Tower B, and
    /// Tower B's outline sheet is placed, reads its slabs and vanishes because Tower A named P3 first; 01379-01
    /// loses 16 sheets the same way, as PLAN B and PLAN C of a level whose PLAN A took it; and the "storeys that
    /// read nothing" gap is partly made of both.
    ///
    /// WHAT THIS COVERS: plan sheets, per job, from the sheet ledger alone - no PDF opened, no DXF read.
    /// WHAT IT DOES NOT: it cannot say the twin's floor was DIFFERENT from the one that was kept (two prints of
    /// one plan are a twin here and losing one costs nothing); it counts slabs read, not square feet, because
    /// the sheet row holds no area; and a sheet that read no slab at all is left out, though it may still have
    /// had a floor to give. A same-class fault it would NOT catch: two sheets naming one storey where BOTH are
    /// dropped, since neither has a twin that took it - those land in the "nobody took that level" class below.
    ///
    /// ⚠ AND THE LEVEL IT CLASSES BY IS NOT THE LEVEL THE COMPOSER PLACED BY (2026-09-23). The ledger's `level`
    /// is <c>SheetTitleReader</c>'s reading of the PAGE's printed title; the composer places by
    /// <c>PlanSheetNaming.Parse</c> on the DXF FILE NAME. They disagree: 60065-01's "HOTEL FOURTH FLOOR PLAN
    /// SHOWING FIFTH FLOOR FRAMING OVER" is blank in the ledger and reads L4 to the composer, and so are the
    /// other six that TheTitlesTheReaderDidNotUnderstandTests shows reading correctly. So "states no level" here
    /// means the REPORTED level is blank, and a sheet in that class may have been placed by a level the composer
    /// read perfectly well. Two readers of one title, one of them reported and the other obeyed.
    /// </summary>
    private static int Dropped(List<string> jobs, IReadOnlyList<CorpusAnalyzer.SheetRow> sheets)
    {
        var plans = sheets.Where(s => string.Equals(s.SheetType, "plan", StringComparison.OrdinalIgnoreCase))
            .Where(s => jobs.Count == 0 || jobs.Any(j => string.Equals(j, s.Job, StringComparison.OrdinalIgnoreCase))).ToList();
        static string Level(CorpusAnalyzer.SheetRow s) => (s.Level ?? string.Empty).Trim();
        static bool GaveAStorey(CorpusAnalyzer.SheetRow s) => !string.IsNullOrWhiteSpace(s.Storeys);
        var tookTheLevel = plans.Where(GaveAStorey).Select(s => (s.Job, Level: Level(s)))
            .Where(k => k.Level.Length > 0).ToHashSet();

        // What the COMPOSER's reader makes of the sheet's DXF name, for the sheets the page-title reader left blank.
        // It reads the built-in vocabulary, not the office's rows, so it is a floor under the disagreement, not a
        // measure of it: a row that widened the words could only make the number bigger.
        static string? ComposerLevel(CorpusAnalyzer.SheetRow s)
        {
            foreach (string f in CorpusAnalyzer.DxfFilesOf(s.DxfFiles))
            {
                var info = PlanSheetNaming.Parse(f);
                if (info.Levels.Count > 0) return "L" + string.Join("+", info.Levels);
                if (info.ParkadeLevels.Count > 0) return "P" + string.Join("+", info.ParkadeLevels);
            }
            return null;
        }

        var gaveNothing = plans.Where(s => !GaveAStorey(s) && s.Slabs > 0).ToList();
        var notPlaced = gaveNothing.Where(s => s.Placed != true).ToList();
        var placed = gaveNothing.Where(s => s.Placed == true).ToList();
        var noLevel = placed.Where(s => Level(s).Length == 0).ToList();
        var levelled = placed.Where(s => Level(s).Length > 0).ToList();
        var twins = levelled.Where(s => tookTheLevel.Contains((s.Job, Level(s)))).ToList();
        var orphans = levelled.Where(s => !tookTheLevel.Contains((s.Job, Level(s)))).ToList();

        Console.WriteLine($"  {plans.Count} plan sheets; {gaveNothing.Count} read a slab and gave the model no storey ({gaveNothing.Sum(s => s.Slabs)} slabs)");
        Console.WriteLine($"    stood on no grid                       {notPlaced.Count,5}  {notPlaced.Sum(s => s.Slabs),6} slabs");
        // THE OTHER READER, ON THE SAME SHEETS. "States no level" is the PAGE title reader's verdict; the composer
        // places by PlanSheetNaming.Parse on the DXF file name. Where that one DOES read a level, the sheet was
        // never levelless to the code that placed it, and the class above is mis-named for it.
        var composerReads = noLevel.Where(s => ComposerLevel(s) is not null).ToList();
        Console.WriteLine($"    placed, states no level                {noLevel.Count,5}  {noLevel.Sum(s => s.Slabs),6} slabs"
            + (composerReads.Count > 0 ? $"   ({composerReads.Count} of them, {composerReads.Sum(s => s.Slabs)} slabs, DO read a level to the composer's own reader)" : ""));
        Console.WriteLine($"    placed, states a level, nobody took it {orphans.Count,5}  {orphans.Sum(s => s.Slabs),6} slabs");
        Console.WriteLine($"    placed, states a level, A TWIN TOOK IT {twins.Count,5}  {twins.Sum(s => s.Slabs),6} slabs   <- one storey, two sheets, one kept");
        foreach (var (name, list) in new[] { ("A TWIN TOOK ITS LEVEL", twins), ("NOBODY TOOK ITS LEVEL", orphans) })
        {
            Console.WriteLine($"  {name}, by set:");
            foreach (var g in list.GroupBy(s => s.Job, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Sum(s => s.Slabs)).Take(20))
                Console.WriteLine($"    {g.Key,-14} {g.Count(),3} sheets {g.Sum(s => s.Slabs),5} slabs   {string.Join(", ", g.OrderByDescending(s => s.Slabs).Take(4).Select(s => $"{s.SheetNumber}[{Level(s)}] {s.Slabs}"))}");
        }
        return 0;
    }

    /// <summary>
    /// THE F11 BLAST RADIUS (2026-09-23). A sheet that stands on no grid is stacked by the frame its page is
    /// drawn in, so what that frame IS decides what stands under what - and step 64 moved it, when $INSBASE
    /// became (0, 0) so that a point in a view is a point on the page. The six-set gate cannot see that change:
    /// all six of its sets stand on grids. This counts who can.
    ///
    /// WHAT IT SAYS: how many written sheets stood on a grid and how many did not, set by set, with the storeys
    /// and plates those sets carry and whether the engineer's own model is there to judge them.
    ///
    /// WHAT IT DOES NOT SAY: it counts sheets that stand on NO GRID, not sheets whose frame DIFFERS from the
    /// set's placed sheets. A set whose unplaced pages share one media box with its placed ones stacks the same
    /// either way. So this is the UPPER BOUND of the radius, not the radius: the population that could have
    /// moved. It also cannot see a set that failed to build at all, which has no sheets to count.
    /// </summary>
    private static int Frames(IReadOnlyList<CorpusAnalyzer.SetRow> sets, IReadOnlyList<CorpusAnalyzer.SheetRow> sheets)
    {
        var bySet = sheets.Where(s => s.Placed is not null).GroupBy(s => s.Job, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (Placed: g.Count(s => s.Placed == true), Off: g.Count(s => s.Placed == false)), StringComparer.OrdinalIgnoreCase);
        int written = bySet.Sum(kv => kv.Value.Placed + kv.Value.Off), placed = bySet.Sum(kv => kv.Value.Placed), off = bySet.Sum(kv => kv.Value.Off);
        Console.WriteLine("  THE F11 BLAST RADIUS - what stacks by its page frame, not by a grid (upper bound)");
        Console.WriteLine($"  sheets with a verdict        {written,6}");
        Console.WriteLine($"    stood on a grid            {placed,6}  {Pct(placed, written)}");
        Console.WriteLine($"    stood on no grid           {off,6}  {Pct(off, written)}   <- stacked by the page frame");

        var withSheets = sets.Where(s => bySet.ContainsKey(s.Job)).ToList();
        var allOn = withSheets.Where(s => bySet[s.Job].Off == 0).ToList();
        var someOff = withSheets.Where(s => bySet[s.Job].Off > 0 && bySet[s.Job].Placed > 0).ToList();
        var noneOn = withSheets.Where(s => bySet[s.Job].Placed == 0).ToList();
        Console.WriteLine($"  sets with sheets judged      {withSheets.Count,6}  of {sets.Count} in the ledger");
        Console.WriteLine($"    every sheet on a grid      {allOn.Count,6}");
        Console.WriteLine($"    some sheet off the grid    {someOff.Count,6}   {someOff.Sum(s => s.StoreysBuilt ?? 0)} storeys, {someOff.Sum(s => s.Floors ?? 0)} plates, {someOff.Count(s => s.Yardstick is not null)} with her model");
        Console.WriteLine($"    no sheet on a grid         {noneOn.Count,6}   {noneOn.Sum(s => s.StoreysBuilt ?? 0)} storeys, {noneOn.Sum(s => s.Floors ?? 0)} plates, {noneOn.Count(s => s.Yardstick is not null)} with her model");

        var gateSets = new[] { "31130-01", "31138-01", "31065-01", "31202-01", "31168-01", "31170-01-arch" };
        Console.WriteLine("  the six-set gate's own sets:");
        foreach (string j in gateSets)
            Console.WriteLine($"    {j,-14} {(bySet.TryGetValue(j, out var g) ? $"{g.Placed} on the grid, {g.Off} off" : "not in this ledger")}");

        var radius = someOff.Concat(noneOn).OrderByDescending(s => bySet[s.Job].Off).ThenByDescending(s => s.Floors ?? 0).ToList();
        Console.WriteLine($"  worst first ({radius.Count} sets stack something by a page frame):");
        Console.WriteLine($"  {"job",-14} {"off",4} {"on",4} {"storeys",7} {"plates",6} {"her model",-9}  her sq ft");
        foreach (var s in radius.Take(40))
            Console.WriteLine($"  {s.Job,-14} {bySet[s.Job].Off,4} {bySet[s.Job].Placed,4} {s.StoreysBuilt,7} {s.Floors,6} {(s.Yardstick is not null ? "yes" : "-"),-9}  {(s.PlatesHersSqFt is { } h && !double.IsNaN(h) ? h.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) : "-")}");
        if (radius.Count > 40) Console.WriteLine($"  ... and {radius.Count - 40} more");
        Console.WriteLine("  THIS IS THE UPPER BOUND: a set whose unplaced pages share one media box with its placed ones stacks the same either way.");
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
        // HER SQUARE FEET, FIRST AND BY NAME (2026-09-22): the same judgement the gate makes, so a set that lost her
        // plate area cannot hide in a sixty-row table again - 31087 fell 89% -> 47% in run 43 and was read a day late.
        var gate = CorpusGate.Judge(before, after);
        if (gate.Judged.Count > 0) Console.Write(CorpusGate.Summary(gate));
        else Console.WriteLine("  (neither ledger carries her plate figures: one of them was banked before 2026-09-22)");
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
