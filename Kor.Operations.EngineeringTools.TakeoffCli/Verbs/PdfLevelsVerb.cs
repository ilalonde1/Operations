// The takeoff verb `pdf-levels`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// THE DRAWINGS' STOREYS AS A LEVELS FILE, for a job nobody has modelled: what dxf-to-etabs takes
// in place of a reference .e2k. Usage: takeoff pdf-levels <stickfile.pdf> [levels.csv] [--plans <dxfDir>]
// The set's storeys come off its wall elevations (SetStoreys: a storey height is the distance
// between two level lines at the sheet's scale), chained from the lowest level at 0 upward, in mm.
internal static class PdfLevelsVerb
{
    public static bool Matches(string[] args) => args.Length >= 2 && args[0].Equals("pdf-levels", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"Not found: {args[1]}"); return 1; }
        var (plOptions, _) = PdfIntakeOptions.For(args.SkipWhile(a => !a.Equals("--rules-db", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault());
        // --plans <dxfDir>: the storeys the written plan views are NAMED for join the ladder, heights assumed where
        // the elevations state none (StoreysFromPlans, step 45); without it the ladder is the elevations' alone
        string? plPlans = args.SkipWhile(a => !a.Equals("--plans", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var plPlanNames = plPlans is not null && Directory.Exists(plPlans) ? Directory.EnumerateFiles(plPlans, "*.dxf").Select(Path.GetFileName).Select(n => n!).ToList() : null;
        string plOut = args.Length >= 3 && !args[2].StartsWith("--", StringComparison.Ordinal) ? args[2] : Path.Combine(Path.GetTempPath(), "levels.csv");
        // chained in Core (SetStoreys.Levels, intake step 25): first stated wins a name, a storey that
        // skips names is a break filled at the typical storey, a storey across two buildings is nobody's
        var plLadder = PdfOnlyBuild.WriteLevels(args[1], plOut, plOptions, out var plTable, out var plChain, plPlanNames);
        if (args.Length >= 3 && !args[2].StartsWith("--", StringComparison.Ordinal)) Console.WriteLine($"{plLadder.Storeys.Count} levels → {args[2]}");
        else foreach (var l in File.ReadAllLines(plOut)) Console.WriteLine(l);
        Console.WriteLine(StoreysFromPlans.Summary(plLadder));
        Console.WriteLine($"{plTable.SheetsWithStoreys} of {plTable.ElevationSheets} elevation sheets stated storeys; {plTable.Storeys.Count} storeys, {plChain.Bases.Count} base(s): {string.Join(", ", plChain.Bases)}"
                          + (plChain.TypicalMm is double plTyp ? $"; typical storey {plTyp:0} mm" : "")
                          + (plChain.Unchained.Count > 0 ? $"; not chained to a base: {string.Join(", ", plChain.Unchained)}" : ""));
        if (plChain.NamedTwice.Count > 0) Console.WriteLine($"named twice, first stated kept: {string.Join("; ", plChain.NamedTwice)}");
        foreach (var b in plChain.Breaks) Console.WriteLine($"break: {b}");
        foreach (var l in plChain.Levels.Where(l => l.From.Contains("not drawn", StringComparison.Ordinal))) Console.WriteLine($"  {l.Name} at {l.ElevationMm:0}: {l.From}");
        return 0;
    }
}
