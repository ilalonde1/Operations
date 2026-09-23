// THE GATE BETWEEN A RULE AND ITS BANK (2026-09-22): the sets the engineer has a model of, built with the code in
// hand and judged against a banked ledger on HER figures - plate area, slab thickness, openings.
//
// The six-set gate says a rule changed nothing it should not have; it cannot say the rule is good, and six sets do
// not draw what 293 draw. Step 131 was banked on a green six-set gate and cost 31087 forty-one storeys (89% -> 47%
// of her plates), found a day later in a full run's diff. A full run is 3 h 17 m; these fifty-odd sets are ~35 min
// at 12 workers, which fits between a rule and its bank.
//
// Usage: takeoff corpus-gate [<projectsRoot>] [--baseline <ledger csv>] [--work <dir>] [--jobs a,b] [--parallel N]
//                            [--rules-db <conn>] [--yardsticks <dir>] [--out <csv>] [--no-build] [--bisect KNOB,KNOB]
//   --bisect     after a loss, re-read the LOST sets with each knob (KOR_STEP<n>_OFF) to name the rule that took them
//   --baseline   the banked ledger to judge against (default: the newest docs/etabs-handoff/corpus/ledger-sets-*.csv)
//   --jobs       judge these sets instead of every set of the baseline that has her model
//   --no-build   judge the ledger already in the work dir (a run that has just finished), building nothing
// Exit 1 if any set lost, so it can gate a commit; the table is written beside the work as gate-sets.csv.

internal static class CorpusGateVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("corpus-gate", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        string root = PublishDiscovery.ProjectsRoot;
        string work = Path.Combine(DrawingMirror.Root, "corpus");
        string? baseline = null, jobs = null, rulesDb = null, yardsticks = null, outCsv = null, bisect = null;
        int parallel = 12;
        bool build = true;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].Equals("--baseline", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) baseline = args[++i];
            else if (args[i].Equals("--work", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) work = args[++i];
            else if (args[i].Equals("--jobs", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) jobs = args[++i];
            else if (args[i].Equals("--parallel", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) parallel = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i].Equals("--rules-db", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) rulesDb = args[++i];
            else if (args[i].Equals("--yardsticks", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) yardsticks = args[++i];
            else if (args[i].Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) outCsv = args[++i];
            else if (args[i].Equals("--bisect", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) bisect = args[++i];
            else if (args[i].Equals("--no-build", StringComparison.OrdinalIgnoreCase)) build = false;
            else root = args[i];
        }

        baseline ??= NewestBankedLedger();
        if (baseline is null || !File.Exists(baseline))
        {
            Console.Error.WriteLine("no baseline ledger: pass --baseline <ledger-sets-*.csv>, or bank one first (docs/etabs-handoff/corpus/).");
            return 2;
        }
        var before = CorpusAnalyzer.ReadSets(baseline);
        var wanted = jobs is null
            ? CorpusGate.JobsWithAYardstick(before)
            : jobs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (wanted.Count == 0)
        {
            Console.Error.WriteLine($"the baseline names no set with the engineer's model: {baseline}");
            return 2;
        }
        Console.WriteLine($"baseline: {Path.GetFileName(baseline)} ({before.Count} rows); judging {wanted.Count} set(s) she has modelled");

        string ledger = Path.Combine(work, "ledger-sets.csv");
        if (build)
        {
            // the same walk the corpus run uses, over these jobs only (the read cache in the work dir stands)
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                Console.WriteLine($"  Ctrl-C at {DateTime.Now:HH:mm:ss} ignored: a gate finishes or is killed, it is not interrupted");
            };
            var problems = new List<string>();
            Console.WriteLine("census...");
            var census = StickFileCorpus.CensusCached(root, problems, TimeSpan.FromHours(12), false, line => Console.WriteLine("  " + line), parallel: 12);
            var (options, rulesSource) = PdfIntakeOptions.For(rulesDb ?? Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable));
            Console.WriteLine($"rules: {rulesSource}; work: {work}");
            var run = CorpusAnalyzer.Run(census, work, options, rulesDb, parallel, force: true, line => Console.WriteLine(line),
                string.Join(",", wanted), yardsticks, reuseBuilds: false, recompose: false);
            Console.WriteLine();
            Console.Write(CorpusAnalyzer.Summary(run));
            // the gate's own ledger, so a run's ledger-sets.csv is never half-written by it
            ledger = Path.Combine(work, "gate-sets.csv");
            CorpusAnalyzer.WriteSetCsv(ledger, run.Sets);
        }
        else if (!File.Exists(ledger))
        {
            Console.Error.WriteLine($"--no-build, and no ledger to judge at {ledger}");
            return 2;
        }

        var after = CorpusAnalyzer.ReadSets(ledger);
        var report = CorpusGate.Judge(before, after);
        Console.WriteLine();
        Console.Write(CorpusGate.Summary(report));

        // AND THE BISECT IS OF THE LOSERS ONLY (2026-09-22). On 2026-09-19 four rules landed together and one of them
        // cost 31202 its LEVEL 13 outline; finding which took six serial six-set gates, seventy minutes, by hand. The
        // rules carry their own knobs (KOR_STEP<n>_OFF); a set that LOST is re-read with each knob in turn - two sets
        // and four knobs is eight reads, not a corpus - and the knob that gives the set its plate area back names the
        // rule. Nothing is bisected when nothing lost.
        if (bisect is not null && report.Losses.Count > 0 && build)
        {
            var knobs = bisect.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            Console.WriteLine();
            Console.WriteLine($"  bisecting {report.Losses.Count} lost set(s) over {knobs.Count} knob(s): {string.Join(", ", knobs)}");
            var problems = new List<string>();
            var census = StickFileCorpus.CensusCached(root, problems, TimeSpan.FromHours(12), false, _ => { }, parallel: 12);
            var (options, _) = PdfIntakeOptions.For(rulesDb ?? Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable));
            foreach (var lost in report.Losses)
            {
                Console.WriteLine($"  {lost.Job}: {lost.BeforeSqFt:N0} -> {lost.AfterSqFt:N0} sq ft of her {lost.HersSqFt:N0}");
                foreach (string knob in knobs)
                {
                    Environment.SetEnvironmentVariable(knob, "1");
                    try
                    {
                        var one = CorpusAnalyzer.Run(census, work, options, rulesDb, 1, force: true, _ => { }, lost.Job, yardsticks, reuseBuilds: false, recompose: false);
                        double? got = one.Sets.FirstOrDefault(s => s.Job.Equals(lost.Job, StringComparison.OrdinalIgnoreCase))?.PlatesOursSqFt;
                        bool restored = got is { } g && lost.BeforeSqFt is { } b && g >= b - Math.Max(CorpusGate.PlateLossFloorSqFt, CorpusGate.PlateLossShare * (lost.HersSqFt ?? 0));
                        Console.WriteLine($"      {knob,-22} -> {(got is { } v ? v.ToString("N0", CultureInfo.InvariantCulture) : "-"),12} sq ft {(restored ? "  <== this rule took it" : "")}");
                    }
                    finally { Environment.SetEnvironmentVariable(knob, null); }
                }
            }
            // the gate's own ledger was overwritten by the bisect's single-set runs; say so rather than leave a half table
            Console.WriteLine($"  (the bisect re-read those sets: {Path.GetFileName(ledger)} now holds their last knob's build, not the gate's)");
        }
        else if (report.Losses.Count > 0 && bisect is null)
        {
            Console.WriteLine("  to find which rule took them: --bisect KOR_STEP130_OFF,KOR_STEP131_OFF,KOR_STEP133_OFF,KOR_STEP134_OFF,KOR_STEP135_OFF");
        }
        if (outCsv is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outCsv))!);
            File.WriteAllLines(outCsv, new[] { "job,before_sqft,after_sqft,hers_sqft,moved_sqft,share_now,thickness_agree_before,thickness_agree_after,thickness_storeys,openings_we_have_before,openings_we_have_after,openings_hers,lost" }
                .Concat(report.Verdicts.Select(v => string.Join(",", v.Job, F(v.BeforeSqFt), F(v.AfterSqFt), F(v.HersSqFt), F(v.MovedSqFt), F(v.ShareNow),
                    v.ThicknessAgreeBefore, v.ThicknessAgreeAfter, v.ThicknessStoreys, v.OpeningsWeHaveBefore, v.OpeningsWeHaveAfter, v.OpeningsHers, v.Lost ? "LOST" : ""))));
            Console.WriteLine($"  csv: {outCsv}");
        }
        return report.Losses.Count == 0 ? 0 : 1;

        static string F(double? v) => v is null ? "" : v.Value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>The newest banked set ledger under docs/etabs-handoff/corpus/, by name (they carry their run's date).</summary>
    private static string? NewestBankedLedger()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string corpus = Path.Combine(dir.FullName, "docs", "etabs-handoff", "corpus");
            if (!Directory.Exists(corpus)) continue;
            return Directory.EnumerateFiles(corpus, "ledger-sets-*.csv").OrderBy(p => p, StringComparer.Ordinal).LastOrDefault();
        }
        return null;
    }
}
