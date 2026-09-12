// The takeoff verb `markup-list`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// THE LEDGER. Everything a page carries, by kind, and what the intake did with each kind: read,
// discarded by a named rule, unread, ignored by design, or unaccounted. The unaccounted and unread
// totals are the intake's backlog as a number. See SheetInventory for what it covers and does not.
// Usage: takeoff pdf-inventory <pdf> [--pages A-B] [--scale N] [--rules-db <conn>] [--json out.json]
// A MARK-UP IS A LIST OF INSTRUCTIONS (foundation §3.1, the engineer-to-drafter loop): every
// annotation with words, what it asks, where on the grid, beside which member.
// Usage: takeoff markup-list <pdf> --scale N [--pages A-B]
internal static class MarkupListVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("markup-list", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: takeoff markup-list <pdf> --scale N [--pages A-B] [--rules-db <conn>]"); return 1; }
        string mlPdf = args[1];
        int mlScale = 0, mlFirst = 1, mlLast = int.MaxValue; string? mlRules = null;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i].Equals("--scale", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) int.TryParse(args[++i], out mlScale);
            else if (args[i].Equals("--pages", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var parts = args[++i].Split('-');
                int.TryParse(parts[0], out mlFirst);
                if (parts.Length < 2 || !int.TryParse(parts[1], out mlLast)) mlLast = mlFirst;
            }
            else if (args[i].Equals("--rules-db", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) mlRules = args[++i];
        }
        if (!File.Exists(mlPdf)) { Console.Error.WriteLine($"PDF not found '{mlPdf}'."); return 2; }
        if (mlScale <= 0) { Console.Error.WriteLine("--scale <denominator> is required (1/8\" = 1'-0\" is 96)."); return 2; }
        var (mlOptions, mlRulesSource) = PdfIntakeOptions.For(mlRules);
        using var mlDoc = UglyToad.PdfPig.PdfDocument.Open(mlPdf);
        var mlFacts = DocumentFacts.From(mlDoc);
        var mlRequest = new IntakeRequest(mlScale, mlOptions);
        Console.WriteLine($"{Path.GetFileName(mlPdf)}  1:{mlScale}  rules: {mlRulesSource}");
        var mlCounts = new Dictionary<MarkupList.Kind, int>();
        int mlWithDistance = 0, mlWithMember = 0;
        for (int p = Math.Max(1, mlFirst); p <= Math.Min(mlDoc.NumberOfPages, mlLast); p++)
        {
            SheetRecord record;
            try { record = DrawingIntake.ReadSheet(mlDoc, p, mlRequest, mlFacts); }
            catch (Exception ex) { Console.WriteLine($"p{p}: {ex.GetType().Name}: {ex.Message}"); continue; }
            var items = MarkupList.Build(record);
            if (items.Count == 0) continue;
            Console.WriteLine();
            Console.WriteLine($"p{p} {record.SheetNumber ?? "(no sheet number)"} {record.SheetType}: {items.Count} annotation(s) with words, " +
                              $"{items.Count(i => i.Kind == MarkupList.Kind.Instruction)} instruction(s), {items.Count(i => i.Kind == MarkupList.Kind.Measurement)} measurement(s), " +
                              $"{items.Count(i => i.Kind == MarkupList.Kind.Approval)} tick(s), {items.Count(i => i.Kind == MarkupList.Kind.Note)} note(s); grid axes {record.Geometry.GridAxes.Count}");
            foreach (var item in items.Where(i => i.Kind is not (MarkupList.Kind.Approval or MarkupList.Kind.Shape)))
            {
                Console.WriteLine("  " + item.Line());
                mlCounts[item.Kind] = mlCounts.GetValueOrDefault(item.Kind) + 1;
                if (item.Kind == MarkupList.Kind.Instruction && item.DistanceMm is not null) mlWithDistance++;
                if (item.Kind == MarkupList.Kind.Instruction && item.Nearest is not null) mlWithMember++;
            }
            mlCounts[MarkupList.Kind.Approval] = mlCounts.GetValueOrDefault(MarkupList.Kind.Approval) + items.Count(i => i.Kind == MarkupList.Kind.Approval);
        }
        Console.WriteLine();
        int mlInstr = mlCounts.GetValueOrDefault(MarkupList.Kind.Instruction);
        Console.WriteLine($"{mlInstr} instruction(s) ({mlWithDistance} with a distance, {mlWithMember} beside a member), {mlCounts.GetValueOrDefault(MarkupList.Kind.Measurement)} measurement(s), " +
                          $"{mlCounts.GetValueOrDefault(MarkupList.Kind.Approval)} tick(s), {mlCounts.GetValueOrDefault(MarkupList.Kind.Note)} note(s).");
        return 0;
    }
}
