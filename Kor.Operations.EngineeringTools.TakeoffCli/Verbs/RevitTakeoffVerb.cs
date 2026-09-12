// The takeoff verb `revit-takeoff`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// The takeoff straight off the Revit schedules, as Revit exports them — no hand-assembled sheet in
// between. Usage: takeoff revit-takeoff <folder-or-csv...> <out.xlsx>
internal static class RevitTakeoffVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("revit-takeoff", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff revit-takeoff <folder|schedule.csv> [more.csv ...] <out.xlsx>"); return 1; }

        string revitOut = args[^1];
        var revitFiles = new List<string>();
        foreach (string a in args[1..^1])
        {
            if (Directory.Exists(a)) revitFiles.AddRange(Directory.EnumerateFiles(a, "*.csv", SearchOption.TopDirectoryOnly));
            else if (File.Exists(a)) revitFiles.Add(a);
            else { Console.Error.WriteLine($"Not found '{a}'."); return 2; }
        }
        if (revitFiles.Count == 0) { Console.Error.WriteLine("No .csv schedules found."); return 2; }

        var revit = RevitScheduleImporter.Import(
            revitFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                      .Select(f => (Path.GetFileName(f), File.ReadAllText(f))));

        if (revit.Inputs.Count == 0)
        {
            Console.Error.WriteLine($"Nothing priceable in {revitFiles.Count} file(s).");
            foreach (var rz in revit.Residual.Take(10)) Console.Error.WriteLine($"   {rz.Source,-34} {rz.Note}");
            return 3;
        }

        bool revitMetric = revit.Unit == UnitSystem.Metric;
        string rVol = revitMetric ? "m³" : "yd³", rMass = revitMetric ? "kg" : "lb";
        var revitComputed = StructuralTakeoffService.Compute(
            revit.Inputs, revitMetric ? StructuralDensityTable.KorMetricDefault : StructuralDensityTable.KorImperialDefault);

        var revitModel = new StructuralTakeoffReportModel(
            Path.GetFileNameWithoutExtension(revitOut), "Revit schedule takeoff", "", DateTime.UtcNow, revitComputed,
            ConcreteBasis: "Concrete volume is Revit's own, read straight from the exported schedules — modelled solid geometry, so every thickening, drop and transfer the model carries is already in it.",
            Assumptions: revit.Notes
                .Concat(revit.Residual.GroupBy(r => r.Source)
                    .Select(g => $"NOT PRICED — {g.Count()} row(s) from {g.Key}: {g.First().Note}"))
                .ToList());
        File.WriteAllBytes(revitOut, StructuralTakeoffReportGenerator.BuildXlsx(revitModel));

        Console.WriteLine($"\nRevit schedule takeoff — {revit.RowsRead} row(s) from {revitFiles.Count} export(s).");
        Console.WriteLine($"Concrete: {revitComputed.TotalConcreteVolume:N1} {rVol}   Reinforcing: {revitComputed.TotalRebarWeight:N0} {rMass}");

        Console.WriteLine("\nPer level:");
        foreach (var lvl in revitComputed.Lines.GroupBy(l => l.Level).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {lvl.Key}");
            foreach (var ln in lvl.OrderByDescending(l => l.ConcreteVolume))
                Console.WriteLine($"     {ln.Element,-11} {ln.Grade,-16} {ln.ConcreteVolume,9:N1} {rVol}  {ln.RebarWeight,10:N0} {rMass}");
        }

        Console.WriteLine("\nWHAT THESE NUMBERS REST ON:");
        foreach (string n in revit.Notes) Console.WriteLine($"   {n}");

        if (revit.Residual.Count > 0)
        {
            Console.WriteLine($"\nNOT PRICED — {revit.Residual.Count} row(s):");
            foreach (var g in revit.Residual.GroupBy(r => r.Source))
            {
                Console.WriteLine($"   {g.Key} x{g.Count()}");
                foreach (var rz in g.Take(2)) Console.WriteLine($"      \"{(rz.Row.Length > 60 ? rz.Row[..60] + "…" : rz.Row)}\" — {rz.Note}");
            }
        }
        Console.WriteLine($"\n  ->  {revitOut}");
        return 0;
    }
}
