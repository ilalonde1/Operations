// The takeoff verb `corpus-analyze`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// THE ANALYZER: the whole corpus through the one ingestion point, into the ledger (completion plan
// WP1, 2026-09-11). Every job's current stick file, mirrored once, built exactly as the verbs build
// one (PdfOnlyBuild), one row per set and per sheet into analysis.IntakeSet / IntakeSheet (migration
// 083) and CSV beside the work; then the summary: X of Y build, and why the rest do not.
// Usage: takeoff corpus-analyze [<projectsRoot>] [--work <dir>] [--jobs 31168-01,31170-01] [--parallel N] [--force] [--reuse] [--rules-db <conn>] [--yardsticks <dir of <job>.e2k>]
// --reuse keeps every built model whatever the tool's build stamp (for a change proven outside the build path by the six-set bank) and re-measures the yardsticks.
// --recompose keeps the views a previous build wrote and runs the ladder and the composer again (a change to how storeys are found, or to the composer).
internal static class CorpusAnalyzeVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("corpus-analyze", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        string caRoot = PublishDiscovery.ProjectsRoot;
        string caWork = Path.Combine(DrawingMirror.Root, "corpus");
        string? caJobs = null, caRulesDb = null, caYardsticks = null;
        int caParallel = 4;
        bool caForce = false, caReuse = false, caRecompose = false;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].Equals("--work", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) caWork = args[++i];
            else if (args[i].Equals("--jobs", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) caJobs = args[++i];
            else if (args[i].Equals("--parallel", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) caParallel = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i].Equals("--rules-db", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) caRulesDb = args[++i];
            else if (args[i].Equals("--yardsticks", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) caYardsticks = args[++i];
            else if (args[i].Equals("--force", StringComparison.OrdinalIgnoreCase)) caForce = true;
            else if (args[i].Equals("--reuse", StringComparison.OrdinalIgnoreCase)) caReuse = true;
            else if (args[i].Equals("--recompose", StringComparison.OrdinalIgnoreCase)) caRecompose = true;
            else caRoot = args[i];
        }
        var caProblems = new List<string>();
        Console.WriteLine("census...");
        var caCensus = StickFileCorpus.Census(caRoot, caProblems, parallel: 12);
        Console.Write(StickFileCorpus.Summary(caCensus));
        var (caOptions, caRulesSource) = PdfIntakeOptions.For(caRulesDb ?? Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable));
        Console.WriteLine($"rules: {caRulesSource}; work: {caWork}");
        var caRun = CorpusAnalyzer.Run(caCensus, caWork, caOptions, caRulesDb, caParallel, caForce, line => Console.WriteLine(line), caJobs, caYardsticks, caReuse, caRecompose);
        Console.WriteLine();
        Console.Write(CorpusAnalyzer.Summary(caRun));
        Console.WriteLine("  " + CorpusAnalyzer.WriteLedger(caRun, caRulesDb));
        Console.WriteLine($"  csv: {Path.Combine(caWork, "ledger-sets.csv")}, {Path.Combine(caWork, "ledger-sheets.csv")}");
        return 0;
    }
}
