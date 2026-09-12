// The takeoff verb `rebar`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Rebar change-detection mode: takeoff rebar <before.pdf> <after.pdf> <out.xlsx> [name] [beforeLabel] [afterLabel]
internal static class RebarVerb
{
    public static bool Matches(string[] args) => args.Length >= 4 && args[0].Equals("rebar", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        var bPages = PdfPageTextReader.ReadPages(args[1]);
        var aPages = PdfPageTextReader.ReadPages(args[2]);

        // PRE-CHECK — refuse up front if EITHER set is scanned/flattened (no vector text layer). You cannot
        // detect a callout change against a side the tool is blind to, so catch it here with a clear reason
        // rather than only at the later 0-callouts guard. Exit 3, no report written.
        foreach (var (label, path, pages) in new[] { ("BEFORE", args[1], bPages), ("AFTER", args[2], aPages) })
        {
            var rv = PdfReadabilityAssessor.AssessPageTexts(pages);
            if (!rv.Readable)
            {
                Console.Error.WriteLine($"CANNOT READ THE {label} SET ({Path.GetFileName(path)}) — {rv.Reason}");
                return 3;
            }
        }

        string rname = args.Length > 4 ? args[4] : string.Empty;
        string rbl = args.Length > 5 ? args[5] : "Before";
        string ral = args.Length > 6 ? args[6] : "After";
        // Optional extents sidecar (from vector-takeoff: <out>.xlsx.extents.json) — measured per-level slab
        // areas that price ΔAs grid changes. Detected by .json so it can never collide with the CSV path.
        string? extentsPath = args.Length == 8 && args[7].EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? args[7] : null;
        // Positioned-word pipeline (same as the overlay markup) so the ledger and the PDF tell one story.
        var rr = RebarChangeService.ComparePdfs(args[1], args[2], rbl, ral);

        // "Can't-read" guard: matched real sheets but read ZERO reinforcing call-outs ⇒ the set's annotation
        // grammar wasn't recognised. That is NOT "no change" — refuse to emit a falsely-reassuring report.
        if (rr.SheetsCompared >= 3 && rr.TotalCalloutsRead == 0)
        {
            Console.Error.WriteLine(
                $"ABORT: compared {rr.SheetsCompared} sheets but read 0 reinforcing call-outs — this set's " +
                "call-out style was not recognised, so a change cannot be detected. This is NOT a 'no change' " +
                "result. No report written.");
            return 3;
        }

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
        else if (extentsPath is not null && File.Exists(extentsPath))
        {
            // FUSION: price ΔAs grid changes with the takeoff's own MEASURED plate areas (slab grids
            // only — direct measurements). Estimate stays on its own sheet, areas stay orange-editable;
            // the exact call-out delta below is untouched.
            var extents = RebarExtents.FromJson(File.ReadAllText(extentsPath));
            var sheetTitles = RebarCalloutExtractor.GroupTextBySheet(bPages)
                .Select(x => (x.Sheet, x.Title))
                .Union(RebarCalloutExtractor.GroupTextBySheet(aPages).Select(x => (x.Sheet, x.Title)))
                .Distinct().ToList();
            var slabAreas = RebarExtents.SlabAreasM2BySheet(sheetTitles, extents);
            var priced = RebarGridPricer.Compare(bPages, aPages, slabAreas, null, rbl, ral);
            File.WriteAllBytes(args[3], RebarChangeReportGenerator.BuildWithPricedGrids(rr, priced, rname));
            Console.WriteLine($"Extent-based ΔAs ESTIMATE (slab grids × measured plate areas — separate from the exact call-out figure below):");
            Console.WriteLine($"  {priced.PricedCount} grid change(s) priced -> {priced.TotalKnownDeltaKg * 2.20462:+#,##0;-#,##0;0} lb; {priced.UnpricedCount} still need an area (orange cells in the workbook).");
            foreach (var c in priced.Changes.Where(c => c.DeltaLb.HasValue))
                Console.WriteLine($"  {c.Sheet,-11} {c.Kind,-18} {(c.Before?.Display ?? "—"),-16} -> {(c.After?.Display ?? "—"),-16} ΔAs {c.DeltaAsKgPerM2,6:+0.00;-0.00} kg/m²  = {c.DeltaLb,8:+#,##0;-#,##0} lb on {c.AreaM2:#,##0} m² (measured)");
        }
        else
        {
            File.WriteAllBytes(args[3], RebarChangeReportGenerator.BuildXlsx(rr, rname));
        }

        Console.WriteLine($"Sheets compared {rr.SheetsCompared}, changed {rr.SheetsChanged} " +
                          $"(content {rr.ContentChanged}, new {rr.NewSheets}, removed {rr.RemovedSheets})");
        Console.WriteLine($"Net weighable rebar change: {rr.NetWeightLb:+#,##0;-#,##0;0} lb "
            + $"(+{rr.AddedWeightLb:N0} / -{rr.RemovedWeightLb:N0}; {rr.UnweighedChanges} changed call-out(s) carry no count/length)");
        foreach (var s in rr.Sheets.Where(s => s.Status != RebarChangeStatus.Unchanged))
            Console.WriteLine($"  {s.Sheet,-11} {s.Status,-12} net {s.NetDelta,+3} : {string.Join(", ", s.Added.Concat(s.Removed))}");

        // Per-issue WEIGHT (qty × length × CSA mass) — the lb number a manual rebar comparison produces.
        // Read through the SAME positioned pipeline as the change result above, so the absolute sums and
        // the change deltas can never disagree about what was read.
        {
            Dictionary<string, Dictionary<string, int>> LoadCounts(string path)
            {
                using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
                return RebarPdfReader.SheetCounts(RebarPdfReader.OwnSheet(RebarPdfReader.Read(doc, UnitSystem.Metric)));
            }
            var bSheets = LoadCounts(args[1]);
            var aSheets = LoadCounts(args[2]);
            double tb = 0, ta = 0; int unweigh = 0;
            var rows = new List<(string Sheet, double B, double A)>();
            foreach (var sh in bSheets.Keys.Union(aSheets.Keys).OrderBy(x => x))
            {
                var wb = bSheets.TryGetValue(sh, out var sbc) ? RebarBarListWeigher.Weigh(sbc) : default;
                var wa = aSheets.TryGetValue(sh, out var sac) ? RebarBarListWeigher.Weigh(sac) : default;
                if (wb.WeightLb <= 0 && wa.WeightLb <= 0 && wb.UnweighableCallouts == 0 && wa.UnweighableCallouts == 0) continue;
                rows.Add((sh, wb.WeightLb, wa.WeightLb));
                tb += wb.WeightLb; ta += wa.WeightLb; unweigh += wb.UnweighableCallouts + wa.UnweighableCallouts;
            }
            if (tb > 0 || ta > 0)
            {
                Console.WriteLine($"\nBar-list rebar weight (qty×length×CSA mass; readable quantity-bearing call-outs only):");
                Console.WriteLine($"  {"Sheet",-11} {rbl,13} {ral,13} {"Δ lb",13}");
                foreach (var r in rows.OrderByDescending(r => System.Math.Abs(r.A - r.B)))
                    Console.WriteLine($"  {r.Sheet,-11} {r.B,13:N0} {r.A,13:N0} {r.A - r.B,13:+#,##0;-#,##0;0}");
                Console.WriteLine($"  {"TOTAL",-11} {tb,13:N0} {ta,13:N0} {ta - tb,13:+#,##0;-#,##0;0}");
                Console.WriteLine($"  NOTE: a call-out-SUM estimate, not a full per-element model (no mat-by-area / hooks / studrails / stirrups). Unweighable continuous call-outs skipped: {unweigh}.");
            }
        }
        Console.WriteLine($"Wrote {args[3]}");
        return 0;
    }
}
