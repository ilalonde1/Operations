// The takeoff verb `single`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Single-issue absolute takeoff from one concrete schedule CSV (faithful to the app's
// GenerateTakeoff_Click: Import -> Compute -> BuildXlsx).
// Usage: takeoff single <schedule.csv> <out.xlsx> [wbs] [name] [issue] [imperial]
internal static class SingleVerb
{
    public static bool Matches(string[] args) => args.Length >= 3 && args[0].Equals("single", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        var sInputs = StructuralTakeoffCsvImporter.Import(File.ReadAllText(args[1]));
        bool imp = args.Length > 6 && args[6].Equals("imperial", StringComparison.OrdinalIgnoreCase);
        var table = imp ? StructuralDensityTable.KorImperialDefault : StructuralDensityTable.KorMetricDefault;
        var sResult = StructuralTakeoffService.Compute(sInputs, table);
        var sModel = new StructuralTakeoffReportModel(
            args.Length > 3 ? args[3] : "", args.Length > 4 ? args[4] : "",
            args.Length > 5 ? args[5] : "", DateTime.UtcNow, sResult);
        File.WriteAllBytes(args[2], StructuralTakeoffReportGenerator.BuildXlsx(sModel));
        string vU = imp ? "cu.yd" : "m3", wU = imp ? "lb" : "kg";
        Console.WriteLine($"Rows: {sInputs.Count}   Concrete: {sResult.TotalConcreteVolume:N0} {vU}   Reinforcing: {sResult.TotalRebarWeight:N0} {wU}");
        Console.WriteLine($"Wrote {args[2]}");
        return 0;
    }
}
