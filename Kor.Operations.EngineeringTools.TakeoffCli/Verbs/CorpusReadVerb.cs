// The takeoff verb `corpus-read`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// DXF -> ETABS — build a model from drafting's concrete-outline plan exports. Walls, columns and slabs are
// read off the structural layers, each sheet is placed on every storey its title covers, and the result is
// merged into an .e2k ETABS itself exported (so storeys, grids and materials stay ETABS's own).
// CORPUS READER CHECK — run this tool's reader over every model KOR engineers have built.
//
// The rules have been measured against the portfolio. The reader never has: it has been exercised
// against two reference models from one office. Since an unfamiliar job's FIRST act is to hand
// this tool a reference model unlike anything it has read, that is the wrong two files to be
// confident about. Read-only; writes nothing but its own report.
//
// Usage: takeoff corpus-read <projectsRoot> [out.txt] [--limit N]
internal static class CorpusReadVerb
{
    public static bool Matches(string[] args) => args.Length >= 2 && args[0].Equals("corpus-read", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        string root = args[1];
        if (!Directory.Exists(root)) { Console.Error.WriteLine($"Not a folder: '{root}'."); return 2; }

        int limit = 0;
        for (int i = 2; i < args.Length - 1; i++)
            if (args[i].Equals("--limit", StringComparison.OrdinalIgnoreCase))
                int.TryParse(args[i + 1], out limit);

        string? outPath = args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal) ? args[2] : null;

        var results = new List<CorpusReadResult>();
        var started = DateTime.UtcNow;
        foreach (string corpusModel in CorpusReaderCheck.Models(root, limit))
        {
            results.Add(CorpusReaderCheck.Check(corpusModel));
            if (results.Count % 50 == 0)
                Console.Error.WriteLine($"  {results.Count:N0} read ({(DateTime.UtcNow - started).TotalSeconds:0}s)");
        }

        string summary = CorpusReaderCheck.Summarise(results);
        Console.WriteLine(summary);

        if (outPath is not null)
        {
            var lines = new List<string> { summary, string.Empty, "outcome	storeys	walls	columns	detail	path" };
            lines.AddRange(results
                .OrderBy(r => r.Outcome, StringComparer.Ordinal)
                .Select(r => $"{r.Outcome}	{r.Storeys}	{r.Walls}	{r.Columns}	{r.Detail}	{r.Path}"));
            File.WriteAllLines(outPath, lines);
            Console.WriteLine($"per-model detail: {outPath}");
        }

        return 0;
    }
}
