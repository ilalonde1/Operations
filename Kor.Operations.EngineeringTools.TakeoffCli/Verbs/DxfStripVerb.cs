using Kor.Operations.EngineeringTools.Dxf;

// THE STRIP-A-LAYER DIFFERENTIAL, first half (DxfLayerStrip, 2026-09-15): copy a set's DXF folder with one
// layer's entities moved to X-STRIPPED, then `dxf-to-etabs` both folders and `model-diff` the models.
// Usage: takeoff dxf-strip <dxfFolder> <outFolder> --layers SLABEDG[,BEAM] [--sheets "S2.02*.dxf"]
internal static class DxfStripVerb
{
    public static bool Matches(string[] args) => args.Length >= 3 && args[0].Equals("dxf-strip", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (!Directory.Exists(args[1])) { Console.Error.WriteLine($"no such folder: {args[1]}"); return 1; }
        var layers = new List<string>(); string glob = "*.dxf";
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i].Equals("--layers", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) layers.AddRange(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            else if (args[i].Equals("--sheets", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) glob = args[++i];
        }
        if (layers.Count == 0) { Console.Error.WriteLine("Usage: takeoff dxf-strip <dxfFolder> <outFolder> --layers SLABEDG[,BEAM] [--sheets \"S2.02*.dxf\"]"); return 1; }
        var rows = DxfLayerStrip.Strip(args[1], args[2], layers, glob);
        foreach (var (file, stripped) in rows.Where(r => r.Stripped > 0))
            Console.WriteLine($"  {file}: {stripped} entities moved to {DxfLayerStrip.StrippedLayer}");
        Console.WriteLine($"{rows.Count} DXF(s) copied to {args[2]}, {rows.Count(r => r.Stripped > 0)} with entities stripped ({rows.Sum(r => r.Stripped)} in all). Now: dxf-to-etabs on both folders, then model-diff.");
        return 0;
    }
}
