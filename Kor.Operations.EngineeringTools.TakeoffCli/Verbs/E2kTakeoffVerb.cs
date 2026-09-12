// The takeoff verb `e2k-takeoff`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// The takeoff that costs nothing to measure: the ETABS model we already built from the drawings
// states every slab outline, wall thickness and column section, on a storey, checked. This prices
// what is already there. Usage: takeoff e2k-takeoff <model.e2k> <out.xlsx> [--metric]
internal static class E2kTakeoffVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("e2k-takeoff", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff e2k-takeoff <model.e2k> <out.xlsx> [--metric]"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"Model not found '{args[1]}'."); return 2; }

        bool metric = args.Any(a => a.Equals("--metric", StringComparison.OrdinalIgnoreCase));
        var e2kUnit = metric ? UnitSystem.Metric : UnitSystem.Imperial;
        string vol = metric ? "m³" : "yd³", mass = metric ? "kg" : "lb", areaUnit = metric ? "m²" : "ft²";

        // The storey vocabulary is the firm's, not this tool's: the same rules the model was built with.
        IReadOnlyCollection<string>? roofWords = null, parkadeWords = null;
        string? e2kRulesDb = null;
        for (int i = 3; i < args.Length - 1; i++)
            if (args[i].Equals("--rules-db", StringComparison.OrdinalIgnoreCase)) e2kRulesDb = args[i + 1];
        try
        {
            var s = RuleSettings.Load(e2kRulesDb);
            roofWords = s.ListOr("dxf.roof-words", null);
            parkadeWords = s.ListOr("dxf.parkade-words", null);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  (rules DB unreachable — {ex.Message.Split('\n')[0]}; falling back to the built-in roof/parkade words.)");
        }

        var takeoff = E2kQuantityTakeoff.Read(E2kDocument.Load(args[1]), e2kUnit, roofWords, parkadeWords);
        if (takeoff.Inputs.Count == 0)
        {
            Console.Error.WriteLine($"No priceable concrete in '{Path.GetFileName(args[1])}'.");
            foreach (var rz in takeoff.Residual.Take(10)) Console.Error.WriteLine($"   {rz.Kind,-10} {rz.Storey,-16} {rz.Object,-8} {rz.Note}");
            return 3;
        }

        var e2kDensities = metric ? StructuralDensityTable.KorMetricDefault : StructuralDensityTable.KorImperialDefault;
        var e2kComputed = StructuralTakeoffService.Compute(takeoff.Inputs, e2kDensities);
        // Everything the console says about what was inferred and left out goes on the workbook too —
        // the estimator reads the xlsx, not this terminal.
        var e2kAssumptions = takeoff.Flags.Select(f => f.Note)
            .Concat(takeoff.Residual.Where(r => r.Kind != "foundation")
                .GroupBy(r => r.Kind)
                .Select(g => $"NOT PRICED — {g.Count()} {g.Key}(s): {g.First().Note} First: {g.First().Storey} {g.First().Object}."))
            .ToList();

        var e2kModel = new StructuralTakeoffReportModel(
            Path.GetFileNameWithoutExtension(args[1]), "ETABS model takeoff", "", DateTime.UtcNow, e2kComputed,
            ConcreteBasis: "Concrete volume is the generated ETABS model's own geometry — the slab outlines, wall thicknesses and column sections read from the drawings, placed on their storeys and checked against the shipped-model invariants. The takeoff and the engineer's model cannot disagree about the building.",
            FoundationNote: takeoff.Residual.FirstOrDefault(r => r.Kind == "foundation")?.Note,
            Assumptions: e2kAssumptions);
        File.WriteAllBytes(args[2], StructuralTakeoffReportGenerator.BuildXlsx(e2kModel));

        Console.WriteLine($"\nETABS model takeoff — {takeoff.MembersRead} members priced ({takeoff.UnitNote}).");
        Console.WriteLine($"Concrete: {e2kComputed.TotalConcreteVolume:N1} {vol}   Reinforcing: {e2kComputed.TotalRebarWeight:N0} {mass}   Formwork: {e2kComputed.TotalFormworkArea:N0} {areaUnit}");
        if (takeoff.OpeningAreaDeducted > 0)
            Console.WriteLine($"  {takeoff.OpeningAreaDeducted:N0} {areaUnit} of openings deducted from the slabs they are cut in.");

        Console.WriteLine("\nPer storey — concrete is the model's own geometry; reinforcing is the calibrated density estimate:");
        foreach (var lvl in e2kComputed.Lines.GroupBy(l => l.Level).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {lvl.Key}");
            foreach (var ln in lvl.OrderByDescending(l => l.ConcreteVolume))
                Console.WriteLine($"     {ln.Element,-8} {ln.Variant ?? "-",-9} {ln.Grade,-8} {ln.ConcreteVolume,9:N1} {vol}  {ln.RebarWeight,9:N0} {mass}  {ln.FormworkArea,9:N0} {areaUnit}");
        }

        if (takeoff.Flags.Count > 0)
        {
            Console.WriteLine("\nWHAT THESE NUMBERS REST ON — every inference, named:");
            foreach (var fl in takeoff.Flags) Console.WriteLine($"   [{fl.Code}] {fl.Note}");
        }

        if (takeoff.Residual.Count > 0)
        {
            var byKind = takeoff.Residual.GroupBy(r => r.Kind).OrderByDescending(g => g.Count());
            Console.WriteLine($"\nNOT PRICED — {takeoff.Residual.Count} item(s), listed so nothing is silently dropped:");
            foreach (var g in byKind)
            {
                Console.WriteLine($"   {g.Key} x{g.Count()}");
                foreach (var rz in g.Take(3)) Console.WriteLine($"      {rz.Storey,-16} {rz.Object,-8} {rz.Note}");
                if (g.Count() > 3) Console.WriteLine($"      … and {g.Count() - 3} more.");
            }
        }
        Console.WriteLine($"\n  ->  {args[2]}");
        return 0;
    }
}
