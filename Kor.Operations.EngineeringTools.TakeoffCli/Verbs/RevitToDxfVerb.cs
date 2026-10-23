// REVIT'S WALLS AND COLUMNS AS THE DXF THE STICK-SET PIPELINE READS (2026-10-23). The KOR Drafter Bridge answers a
// query with every wall's location line and type and every column's point and type, in model millimetres. This verb
// writes them, storey by storey, as the same KOR-layer DXF the PDF reader writes for a plan sheet, so dxf-to-etabs
// builds the model exactly as it does from drawings and model-diff / model-yardstick judge it against the engineer's
// e2k: the drafted Revit model against the analysis model, in both directions, with no PDF and no reader in between.
//
// What it does NOT do: decide which level an element belongs to. The L1 plan view shows every P1 wall as well as its
// own, so the manifest names the elements of each storey (and may exclude ids); the caller settles membership from
// the bridge's Base Constraint or from view membership, and says which in the manifest's "note".
//
// Usage: takeoff revit-to-dxf <manifest.json> <outDir>
//   manifest: { "levels": [ { "storey": "L-1", "walls": "walls.json", "columns": "columns.json", "exclude": [ids] } ],
//               "note": "how membership was decided" }
//   walls.json / columns.json are bridge replies ({"result":{"elements":[...]}}) or bare element arrays.
//
// THE RECIPE THAT WORKED (31170-01, 2026-10-23, Revit-before against the engineer's yardstick):
//   takeoff dxf-to-etabs <outDir> - <revit.e2k> --levels <her storeys as level,elevation> --levels-unit ft
//                        --offset <dx>,<dy> --no-storey-rise
//   - reference "-": with HER model as the reference the composer merges our members into her coincident stacks
//     and the output loses most of ours (40 walls became 12). A levels file carries her storey names instead.
//   - --no-storey-rise: a plan's members otherwise rise to the storey above; the bridge already says which storey.
//   - --offset is applied in the MODEL's units (feet when the levels are in feet), whatever the help text says;
//     find the frame from the columns (35 of 36 coincided at +231 ft on 31170-01).
//   Then takeoff model-diff <her.e2k> <revit.e2k> for columns, and wall COVERAGE (a panel of hers inside one of
//   ours and the reverse) for walls - her model splits walls at columns where Revit has one wall, so midpoints do not
//   pair but coverage does (46 of 46 of her L-1 walls, 23 of 23 on L-2; 12 + 5 of Revit's she does not carry).
//   The coverage script is scratchpad\31170-markup\etabs-diff\e2k_cover.py until it is a verb (model-cover, owed).
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.PdfToSafe;

internal static class RevitToDxfVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("revit-to-dxf", StringComparison.OrdinalIgnoreCase);

    private static readonly Regex Inches = new(@"(\d+(?:\.\d+)?)\s*""", RegexOptions.Compiled);
    private static readonly Regex Size = new(@"(\d+(?:\.\d+)?)\s*""\s*[xX]\s*(\d+(?:\.\d+)?)\s*""", RegexOptions.Compiled);
    private static readonly (byte, byte, byte) Concrete = (0xD7, 0xD7, 0xD7);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff revit-to-dxf <manifest.json> <outDir>"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"Manifest not found '{args[1]}'."); return 2; }
        string outDir = args[2];
        Directory.CreateDirectory(outDir);
        using var manifest = JsonDocument.Parse(File.ReadAllText(args[1]));
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(args[1])) ?? ".";
        if (manifest.RootElement.TryGetProperty("note", out var note)) Console.WriteLine($"membership : {note.GetString()}");

        int storeys = 0;
        foreach (var level in manifest.RootElement.GetProperty("levels").EnumerateArray())
        {
            string storey = level.GetProperty("storey").GetString() ?? throw new InvalidOperationException("a level needs a storey name");
            var exclude = new HashSet<long>();
            if (level.TryGetProperty("exclude", out var ex))
                foreach (var e in ex.EnumerateArray()) exclude.Add(e.GetInt64());

            var g = new ExtractedGeometry { ScaleDenominator = 96 };
            int walls = 0, columns = 0, skipped = 0, unsized = 0;
            if (level.TryGetProperty("walls", out var wallsFile))
                foreach (var w in Elements(Path.Combine(baseDir, wallsFile.GetString()!)))
                {
                    if (exclude.Contains(Id(w))) { skipped++; continue; }
                    if (!w.TryGetProperty("x1_mm", out _)) { skipped++; continue; }   // a curved or unplaced wall has no line
                    double x1 = w.GetProperty("x1_mm").GetDouble(), y1 = w.GetProperty("y1_mm").GetDouble();
                    double x2 = w.GetProperty("x2_mm").GetDouble(), y2 = w.GetProperty("y2_mm").GetDouble();
                    double thick = w.TryGetProperty("width_mm", out var wm) && wm.ValueKind == JsonValueKind.Number ? wm.GetDouble()
                        : InchesInName(w.GetProperty("type").GetString()) * 25.4;
                    if (thick <= 0) { unsized++; continue; }
                    double len = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
                    if (len < 1) { skipped++; continue; }
                    double nx = -(y2 - y1) / len * thick / 2, ny = (x2 - x1) / len * thick / 2;
                    var outline = new List<(double X, double Y)> { (x1 + nx, y1 + ny), (x2 + nx, y2 + ny), (x2 - nx, y2 - ny), (x1 - nx, y1 - ny) };
                    g.Walls.Add(new WallPanel(outline, (x1, y1), (x2, y2), thick));
                    g.WallColors.Add(Concrete);
                    g.WallIsAnnotation.Add(false);
                    walls++;
                }
            if (level.TryGetProperty("columns", out var colsFile))
                foreach (var c in Elements(Path.Combine(baseDir, colsFile.GetString()!)))
                {
                    if (exclude.Contains(Id(c))) { skipped++; continue; }
                    if (!c.TryGetProperty("x_mm", out var cx) || cx.ValueKind != JsonValueKind.Number) { skipped++; continue; }
                    string type = c.GetProperty("type").GetString() ?? "";
                    // The bridge also sends the column's family name; a round family ("Round", "Circ") is a round column,
                    // which the geometry set cannot carry until intake step 173 (ColumnIsRound) lands - re-add it then.
                    var m = Size.Match(type);
                    (double W, double D) size = m.Success
                        ? (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 25.4, double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 25.4)
                        : (0, 0);
                    if (size.W <= 0) { unsized++; size = (300, 300); }
                    g.Columns.Add((cx.GetDouble(), c.GetProperty("y_mm").GetDouble()));
                    g.ColumnColors.Add(Concrete);
                    g.ColumnIsAnnotation.Add(false);
                    g.ColumnSizes.Add(size);
                    columns++;
                }
            string path = Path.Combine(outDir, storey + ".dxf");
            DxfExporter.Export(g, path, korLayers: true);
            storeys++;
            Console.WriteLine($"{storey,-8}: {walls} walls, {columns} columns -> {path}" + (skipped > 0 ? $"  ({skipped} skipped by exclusion or without a line/point)" : "") + (unsized > 0 ? $"  ({unsized} without a size in their type name)" : ""));
        }
        Console.WriteLine($"{storeys} storey DXF(s) written. Next: takeoff dxf-to-etabs \"{outDir}\" <engineer.e2k> <revit.e2k>, then model-diff / model-yardstick.");
        return storeys > 0 ? 0 : 2;
    }

    private static IEnumerable<JsonElement> Elements(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"bridge reply not found: {path}");
        var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array) return root.EnumerateArray().ToList();
        if (root.TryGetProperty("result", out var r) && r.TryGetProperty("elements", out var els)) return els.EnumerateArray().ToList();
        if (root.TryGetProperty("elements", out var e2)) return e2.EnumerateArray().ToList();
        throw new InvalidOperationException($"{path}: neither a bridge reply with result.elements nor an element array.");
    }

    private static long Id(JsonElement e) => e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt64() : -1;

    /// <summary>The thickness a KOR wall type carries in its name: Concrete - Core Shaft - 12" (45 MPa) -> 12.</summary>
    private static double InchesInName(string? type)
    {
        var m = Inches.Match(type ?? "");
        return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }
}
