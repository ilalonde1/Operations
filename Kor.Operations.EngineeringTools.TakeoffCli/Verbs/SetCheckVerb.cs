// The takeoff verb `set-check`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// THE SET CHECKS ITSELF (foundation §3.3): the gatekeeper's page before an issue goes out.
// Usage: takeoff set-check <pdf> --scale N [--reference model.e2k] [--rules-db <conn>]
internal static class SetCheckVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("set-check", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: takeoff set-check <pdf> --scale N [--reference model.e2k] [--rules-db <conn>]"); return 1; }
        string scPdf = args[1];
        int scScale = 0; string? scRef = null, scRules = null;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i].Equals("--scale", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) int.TryParse(args[++i], out scScale);
            else if (args[i].Equals("--reference", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) scRef = args[++i];
            else if (args[i].Equals("--rules-db", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) scRules = args[++i];
        }
        if (!File.Exists(scPdf)) { Console.Error.WriteLine($"PDF not found '{scPdf}'."); return 2; }
        if (scScale <= 0) { Console.Error.WriteLine("--scale <denominator> is required (1/8\" = 1'-0\" is 96)."); return 2; }
        var (scOptions, scRulesSource) = PdfIntakeOptions.For(scRules);
        var scRecords = new List<SheetRecord>();
        using (var scDoc = UglyToad.PdfPig.PdfDocument.Open(scPdf))
        {
            var scFacts = DocumentFacts.From(scDoc);
            var scRequest = new IntakeRequest(scScale, scOptions);
            for (int p = 1; p <= scDoc.NumberOfPages; p++)
            {
                try { scRecords.Add(DrawingIntake.ReadSheet(scDoc, p, scRequest, scFacts)); }
                catch (Exception ex) { Console.Error.WriteLine($"p{p}: {ex.GetType().Name}: {ex.Message}"); }
            }
        }
        StoreyAgreement.Result? scStoreys = null;
        if (scRef is not null && File.Exists(scRef))
        {
            var e2k = E2kDocument.Load(scRef);
            scStoreys = StoreyAgreement.Compare(SetStoreys.Read(scPdf), e2k.ReadStories(), e2k.LengthUnitInInches() ?? 1.0);
        }
        var report = SetCheck.Set(scRecords, scStoreys);
        Console.WriteLine($"{Path.GetFileName(scPdf)}  {report.Pages} pages, {report.Plans} plans  1:{scScale}  rules: {scRulesSource}{(scRef is null ? "" : "  model: " + Path.GetFileName(scRef))}");
        Console.WriteLine();
        foreach (string line in report.Lines()) Console.WriteLine(line);
        Console.WriteLine();
        Console.WriteLine($"{report.Findings.Count} finding(s): " + string.Join("; ", report.ByKind().Select(k => $"{k.Kind} {k.Count}")));
        return 0;
    }
}
