using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Kor.Operations.EngineeringTools.RebarChange;

// Rebar change-detection mode: takeoff rebar <before.pdf> <after.pdf> <out.xlsx> [name] [beforeLabel] [afterLabel]
if (args.Length >= 4 && args[0].Equals("rebar", StringComparison.OrdinalIgnoreCase))
{
    var bPages = PdfPageTextReader.ReadPages(args[1]);
    var aPages = PdfPageTextReader.ReadPages(args[2]);
    string rname = args.Length > 4 ? args[4] : string.Empty;
    string rbl = args.Length > 5 ? args[5] : "Before";
    string ral = args.Length > 6 ? args[6] : "After";
    var rr = RebarChangeService.Compare(bPages, aPages, rbl, ral);

    if (args.Length >= 9) // ... beforeCsv afterCsv -> full takeoff + change
    {
        var dens = RebarDensityTable.Default;
        Dictionary<string, double> Vols(string csv)
        {
            var d = new Dictionary<string, double>();
            foreach (var l in TakeoffCsvImporter.Import(File.ReadAllText(csv), dens))
            {
                string k = l.ElementType switch
                {
                    TakeoffElementType.Wall => "Wall",
                    TakeoffElementType.Column => "Column",
                    TakeoffElementType.Foundation => "Foundation",
                    _ => "Slab"
                };
                d[k] = d.GetValueOrDefault(k) + l.ConcreteM3;
            }
            return d;
        }
        var corr = RebarWeightEstimator.Corroborate(aPages);
        var intB = RebarWeightEstimator.CalloutIntensity(bPages);
        var intA = RebarWeightEstimator.CalloutIntensity(aPages);
        var weight = RebarWeightEstimator.Estimate(Vols(args[7]), Vols(args[8]),
            RebarWeightEstimator.DefaultDensities, corr, rbl, ral, intB, intA);

        // Optional sheet-area CSV (Sheet,SlabAreaM2[,WallAreaM2]) prices the field-grid changes.
        Dictionary<string, double>? slabAreas = null, wallAreas = null;
        if (args.Length >= 10 && File.Exists(args[9]))
        {
            slabAreas = new(); wallAreas = new();
            foreach (var line in File.ReadAllLines(args[9]).Skip(1))
            {
                var p = line.Split(',');
                if (p.Length < 2 || string.IsNullOrWhiteSpace(p[0])) continue;
                if (double.TryParse(p[1], out var sa)) slabAreas[p[0].Trim()] = sa;
                if (p.Length >= 3 && double.TryParse(p[2], out var wa)) wallAreas[p[0].Trim()] = wa;
            }
        }
        var priced = RebarGridPricer.Compare(bPages, aPages, slabAreas, wallAreas, rbl, ral);
        File.WriteAllBytes(args[3], RebarChangeReportGenerator.BuildFull(rr, weight, rname, priced));
        Console.WriteLine($"Rebar weight: {weight.TotalBefore:N0} t -> {weight.TotalAfter:N0} t (delta {weight.TotalDelta:+0.0;-0.0;0})");
        Console.WriteLine($"Field-grid changes: {priced.Changes.Count} ({priced.PricedCount} priced, {priced.UnpricedCount} need area)");
        foreach (var c in priced.Changes)
            Console.WriteLine($"  {c.Sheet,-11} {c.Kind,-18} {(c.Before?.Display ?? "—"),-16} -> {(c.After?.Display ?? "—"),-16} ΔAs {c.DeltaAsKgPerM2,6:+0.00;-0.00} kg/m²" +
                (c.DeltaLb.HasValue ? $"  = {c.DeltaLb,8:+#,##0;-#,##0} lb on {c.AreaM2:#,##0} m²" : "  (area needed)"));
    }
    else
    {
        File.WriteAllBytes(args[3], RebarChangeReportGenerator.BuildXlsx(rr, rname));
    }

    Console.WriteLine($"Sheets compared {rr.SheetsCompared}, changed {rr.SheetsChanged} " +
                      $"(content {rr.ContentChanged}, new {rr.NewSheets}, removed {rr.RemovedSheets})");
    foreach (var s in rr.Sheets.Where(s => s.Status != RebarChangeStatus.Unchanged))
        Console.WriteLine($"  {s.Sheet,-11} {s.Status,-12} net {s.NetDelta,+3} : {string.Join(", ", s.Added.Concat(s.Removed))}");
    Console.WriteLine($"Wrote {args[3]}");
    return 0;
}

// Drives the REAL takeoff engine: two concrete-schedule CSVs -> issue delta -> xlsx + docx.
// Usage: takeoff <before.csv> <after.csv> <out-basepath> [wbs1] [name] [beforeLabel] [afterLabel]
if (args.Length < 3)
{
    Console.WriteLine("Usage: takeoff <before.csv> <after.csv> <out-basepath> [wbs1] [name] [beforeLabel] [afterLabel]");
    return 1;
}

string beforePath = args[0];
string afterPath  = args[1];
string outBase    = args[2];
string wbs1        = args.Length > 3 ? args[3] : string.Empty;
string projectName = args.Length > 4 ? args[4] : string.Empty;
string beforeLabel = args.Length > 5 ? args[5] : "Before";
string afterLabel  = args.Length > 6 ? args[6] : "After";

var densities = RebarDensityTable.Default;
var before = TakeoffCsvImporter.Import(File.ReadAllText(beforePath), densities);
var after  = TakeoffCsvImporter.Import(File.ReadAllText(afterPath), densities);

var diff = TakeoffDiffService.Compare(before, after);
var model = new TakeoffReportModel(wbs1, projectName, beforeLabel, afterLabel, DateTime.UtcNow, diff);

File.WriteAllBytes(outBase + ".xlsx", TakeoffReportGenerator.BuildXlsx(model));
File.WriteAllBytes(outBase + ".docx", TakeoffReportGenerator.BuildDocx(model));

Console.WriteLine($"Concrete delta: {diff.TotalConcreteDeltaM3:N1} m3   " +
                  $"Rebar delta: {diff.TotalRebarDeltaTonnes:N1} t   " +
                  $"Formwork delta: {diff.TotalFormworkDeltaM2:N1} m2");
if (diff.AddedLevels.Count > 0) Console.WriteLine("Added levels: " + string.Join(", ", diff.AddedLevels));
if (diff.RemovedLevels.Count > 0) Console.WriteLine("Removed levels: " + string.Join(", ", diff.RemovedLevels));
Console.WriteLine($"Wrote {outBase}.xlsx and {outBase}.docx");
return 0;
