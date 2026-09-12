// The takeoff verb `ifc-takeoff`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// MODEL takeoff — read concrete quantities straight from a structural Revit/IFC export (the source that
// actually contains the whole building in 3D). No measurement, no scale, no AI: each element's NetVolume
// IS the quantity. Usage: takeoff ifc-takeoff <model.ifc> <out.xlsx>
internal static class IfcTakeoffVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("ifc-takeoff", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff ifc-takeoff <model.ifc> <out.xlsx>"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"IFC not found '{args[1]}'."); return 2; }

        var ifc = IfcQuantityTakeoff.Read(File.ReadAllText(args[1]));
        if (ifc.Inputs.Count == 0)
        {
            Console.Error.WriteLine($"No priceable concrete elements with a NetVolume in '{Path.GetFileName(args[1])}'.");
            Console.Error.WriteLine("Re-export from Revit with 'Export base quantities' ticked (IFC4 or IFC2x3 with the Revit IFC add-in).");
            return 3;
        }

        var ifcDensities = StructuralDensityTable.KorMetricDefault;   // model volumes are m³ → price in kg/m³
        var computed = StructuralTakeoffService.Compute(ifc.Inputs, ifcDensities);
        var ifcModel = new StructuralTakeoffReportModel(Path.GetFileNameWithoutExtension(args[1]), "IFC model takeoff", "", DateTime.UtcNow, computed);
        File.WriteAllBytes(args[2], StructuralTakeoffReportGenerator.BuildXlsx(ifcModel));

        Console.WriteLine($"\nModel takeoff — {ifc.ElementsRead} concrete elements read ({ifc.VolumeUnitNote}).");
        Console.WriteLine($"Concrete (exact, from model): {computed.TotalConcreteVolume:N1} m³   Reinforcing: {computed.TotalRebarWeight:N0} kg");
        Console.WriteLine(ifc.ModelledRebarBars > 0
            ? $"  Modelled rebar found: {ifc.ModelledRebarKg:N0} kg across {ifc.ModelledRebarBars} bars (exact steel — overrides the density estimate where present)."
            : "  Reinforcing is the calibrated density estimate (no 3D-modelled rebar in this IFC — bars detailed in 2D). Edit the orange densities to calibrate.");

        Console.WriteLine("\nPer-level / element — CONCRETE is exact (model NetVolume); reinforcing is the calibrated estimate:");
        foreach (var lvl in computed.Lines.GroupBy(l => l.Level).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {lvl.Key}");
            foreach (var ln in lvl.OrderByDescending(l => l.ConcreteVolume))
                Console.WriteLine($"     {ln.Element,-11} {ln.ConcreteVolume,9:N1} m³   {ln.RebarWeight,9:N0} kg");
        }

        if (ifc.Residual.Count > 0)
        {
            Console.WriteLine($"\nResidual: {ifc.Residual.Count} element(s) carried NO NetVolume — excluded, listed so nothing is silently dropped:");
            foreach (var rz in ifc.Residual.Take(40))
                Console.WriteLine($"   {rz.Type,-22} {rz.Level,-14} {rz.Tag,-18} {rz.Note}");
            if (ifc.Residual.Count > 40) Console.WriteLine($"   … and {ifc.Residual.Count - 40} more.");
        }
        Console.WriteLine($"\n  ->  {args[2]}");
        return 0;
    }
}
