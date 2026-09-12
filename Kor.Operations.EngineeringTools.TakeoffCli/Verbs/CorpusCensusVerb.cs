// The takeoff verb `corpus-census`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// THE CENSUS OF THE DRAWING CORPUS: every job on the projects share, what it holds that the intake
// can learn from - dated structural stick files, architects' sets, the engineer's ETABS models -
// so rules are chosen by how many sets share a failure, not one drawing at a time (2026-09-11).
// Usage: takeoff corpus-census [<projectsRoot>] [--out census.csv] [--parallel N]
// Read-only; one bounded listing per folder, never a recursive walk (CLAUDE.md rule 4).
internal static class CorpusCensusVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("corpus-census", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        string ccRoot = PublishDiscovery.ProjectsRoot;
        string? ccOut = null;
        int ccParallel = 8;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) ccOut = args[++i];
            else if (args[i].Equals("--parallel", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) ccParallel = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else ccRoot = args[i];
        }
        var ccProblems = new List<string>();
        var ccWatch = System.Diagnostics.Stopwatch.StartNew();
        var ccCensus = StickFileCorpus.Census(ccRoot, ccProblems, progress: line => Console.Error.WriteLine("  " + line), parallel: ccParallel);
        Console.Write(StickFileCorpus.Summary(ccCensus));
        Console.WriteLine($"  {ccProblems.Count} folder(s) could not be listed; {ccWatch.Elapsed.TotalSeconds:F0} s over {ccRoot}");
        foreach (var p in ccProblems.Take(20)) Console.WriteLine("    ! " + p);
        if (ccOut is not null)
        {
            StickFileCorpus.WriteCsv(ccCensus, ccOut);
            Console.WriteLine($"  one row per job: {ccOut}");
        }
        return 0;
    }
}
