// The takeoff verb `storeys-check`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// THE DRAWINGS' STOREYS AGAINST THE MODEL'S. Usage: takeoff storeys-check <stickfile.pdf> <model.e2k>
internal static class StoreysCheckVerb
{
    public static bool Matches(string[] args) => args.Length >= 3 && args[0].Equals("storeys-check", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (!File.Exists(args[1]) || !File.Exists(args[2])) { Console.Error.WriteLine("Both files must exist."); return 1; }
        var scTable = SetStoreys.Read(args[1]);
        var scDoc = E2kDocument.Load(args[2]);
        var scResult = StoreyAgreement.Compare(scTable, scDoc.ReadStories(), scDoc.LengthUnitInInches() ?? 1.0);
        Console.WriteLine($"{Path.GetFileName(args[1])} against {Path.GetFileName(args[2])}");
        Console.WriteLine("level      below      sheets   drawing mm   model mm   delta");
        foreach (var r in scResult.Rows)
            Console.WriteLine($"{r.Level,-10} {r.LevelBelow,-10} {r.Sheets,6}   {r.DrawingMm,10:0}   {(r.ModelMm is double m ? m.ToString("0") : "-"),8}   {(r.DeltaMm is double d ? d.ToString("+0;-0;0") : "-"),5}{(r.ModelMm is not null && !r.Within ? "  OFF" : "")}");
        Console.WriteLine();
        Console.WriteLine(scResult.Summary());
        return 0;
    }
}
