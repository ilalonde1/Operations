// A BANKED LEDGER INTO THE DATABASE (2026-09-16). Run 22's analyzer wrote its CSVs and then lost the KorStandards
// connection on the final write ("a transport-level error"), so analysis.IntakeSet/IntakeSheet had no run-22 rows and
// corpus-query pages could not put run 22 beside run 21. The CSVs carry every row with its run id; this writes them.
// Usage: takeoff corpus-ledger-db <ledger-sets.csv> [<ledger-sheets.csv>]   (the sheets file defaults to the sets file's twin)
internal static class CorpusLedgerDbVerb
{
    public static bool Matches(string[] args) => args.Length >= 2 && args[0].Equals("corpus-ledger-db", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        string setsPath = args[1];
        string sheetsPath = args.Length >= 3 ? args[2]
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(setsPath)) ?? ".", Path.GetFileName(setsPath).Replace("ledger-sets", "ledger-sheets", StringComparison.OrdinalIgnoreCase));
        if (!File.Exists(setsPath)) { Console.Error.WriteLine($"no such file: {setsPath}"); return 1; }
        var sets = CorpusAnalyzer.ReadSets(setsPath);
        var sheets = File.Exists(sheetsPath) ? CorpusAnalyzer.ReadSheets(sheetsPath) : [];
        var runIds = sets.Select(s => s.RunId).Distinct().ToList();
        if (runIds.Count != 1 || runIds[0] == Guid.Empty) { Console.Error.WriteLine($"the sets file carries {runIds.Count} run id(s); one is needed."); return 1; }
        if (sheets.Any(s => s.RunId != runIds[0])) { Console.Error.WriteLine("the sheets file carries rows of another run."); return 1; }
        // never twice: a run's rows are written in one transaction, so a run is either there whole or not at all
        string? conn = Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(conn)) { Console.Error.WriteLine("no KorStandards connection (KOR_ENGINEERINGTOOLS_STANDARDSDB)."); return 2; }
        using (var c = new Microsoft.Data.SqlClient.SqlConnection(conn))
        {
            c.Open();
            using var count = new Microsoft.Data.SqlClient.SqlCommand("SELECT COUNT(*) FROM analysis.IntakeSet WHERE RunId = @id", c);
            count.Parameters.AddWithValue("@id", runIds[0]);
            int already = (int)count.ExecuteScalar()!;
            if (already > 0) { Console.Error.WriteLine($"run {runIds[0]} already has {already} set row(s) in analysis.IntakeSet; nothing written."); return 3; }
        }
        var result = new CorpusAnalyzer.RunResult(runIds[0], sets, sheets, 0, 0, TimeSpan.Zero);
        Console.WriteLine($"run {runIds[0]}: {sets.Count} set row(s), {sheets.Count} sheet row(s) from {Path.GetFileName(setsPath)}" + (sheets.Count == 0 ? " (no sheets file beside it)" : ""));
        Console.WriteLine("  " + CorpusAnalyzer.WriteLedger(result, null));
        return 0;
    }
}
