// The takeoff verb `pdf-inventory`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
internal static class PdfInventoryVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("pdf-inventory", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: takeoff pdf-inventory <pdf> [--pages A-B] [--scale N] [--rules-db <conn>] [--json out.json]"); return 1; }
        string ivPdf = args[1];
        if (!File.Exists(ivPdf)) { Console.Error.WriteLine($"PDF not found '{ivPdf}'."); return 2; }
        int ivFirst = 1, ivLast = int.MaxValue; int? ivScale = null; string? ivRules = null, ivJson = null;
        for (int i = 2; i < args.Length; i++)
        {
            string a = args[i];
            if (a.Equals("--scale", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[++i], out int sc)) ivScale = sc;
            else if (a.Equals("--rules-db", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) ivRules = args[++i];
            else if (a.Equals("--json", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) ivJson = args[++i];
            else if (a.Equals("--pages", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                string[] span = args[++i].Split('-', StringSplitOptions.RemoveEmptyEntries);
                if (span.Length >= 1) int.TryParse(span[0], out ivFirst);
                ivLast = span.Length >= 2 && int.TryParse(span[1], out int pl) ? pl : ivFirst;
            }
        }
        var (ivOptions, ivRulesSource) = PdfIntakeOptions.For(ivRules);
        using var ivDoc = UglyToad.PdfPig.PdfDocument.Open(ivPdf);
        var facts = DocumentFacts.From(ivDoc);
        ivLast = Math.Min(ivLast, facts.Pages);
        Console.WriteLine($"{Path.GetFileName(ivPdf)}  pages {ivFirst}-{ivLast} of {facts.Pages}  producer: {facts.Producer}  bookmarks: {facts.Bookmarks.Count}  " +
                          $"scale: {(ivScale is int s ? $"1:{s}" : "none (geometry not classified)")}  rules: {ivRulesSource}");
        Console.WriteLine();
        Console.WriteLine("page  type               sheet                                      words   paths |    read  discard  unread  ignore  unacct  title as read, level as read (sheet = the bookmark where there is one)");
        var ledgers = new List<SheetInventory.SheetLedger>();
        for (int p = ivFirst; p <= ivLast; p++)
        {
            SheetInventory.SheetLedger led;
            try { led = SheetInventory.Of(DrawingIntake.ReadSheet(ivDoc, p, new IntakeRequest(ivScale, ivOptions), facts)); }
            catch (Exception ex) { Console.WriteLine($"{p,4}  FAILED {ex.GetType().Name}: {ex.Message}"); continue; }
            ledgers.Add(led);
            var t = SheetInventory.Totals(led.Rows);
            string sheet = (led.BookmarkTitle ?? led.Title ?? "").Trim();
            if (sheet.Length > 42) sheet = sheet[..42];
            Console.WriteLine($"{p,4}  {led.SheetType,-18} {sheet,-42} {led.Words,6}  {led.Paths,6} | {t[Disposition.Read],7}  {t[Disposition.Discarded],7}  {t[Disposition.Unread],6}  {t[Disposition.Ignored],6}  {t[Disposition.Unaccounted],6}  title: {(led.TitleText ?? "-").Trim()}  level: {(led.Title ?? "-").Trim()}");
        }
        Console.WriteLine();
        var summary = SheetInventory.Summarise(ledgers);
        var totals = SheetInventory.Totals(summary);
        int grand = totals.Values.Sum();
        Console.WriteLine($"DOCUMENT ({grand:N0} words + paths + facts, each counted once)  read {totals[Disposition.Read]:N0}   discarded {totals[Disposition.Discarded]:N0}   " +
                          $"UNREAD {totals[Disposition.Unread]:N0}   ignored {totals[Disposition.Ignored]:N0}   UNACCOUNTED {totals[Disposition.Unaccounted]:N0}" +
                          (facts.OutlinesPresent && facts.Bookmarks.Count == 0 ? "   ⚠ the file has an outline tree PdfPig could not read" : ""));
        Console.WriteLine();
        foreach (var row in summary.Where(r => r.Primary && r.Count > 0))
            Console.WriteLine($"  {row.Disposition,-11} {row.Count,9:N0}  {row.Class}  — {row.By}");
        Console.WriteLine();
        Console.WriteLine("Context (what the readers produced; not summed above):");
        foreach (var row in summary.Where(r => !r.Primary && r.Count > 0))
            Console.WriteLine($"  {row.Disposition,-11} {row.Count,9:N0}  {row.Class}  — {row.By}");
        var notes = ledgers.SelectMany(l => l.Markup).ToList();
        if (notes.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Annotation text ({notes.Count}), retained by Core intake:");
            foreach (var n in notes.Take(25)) Console.WriteLine($"  p{n.PageNumber,-3} {n.Type,-9} {n.Author,-10} {n.Text}");
            if (notes.Count > 25) Console.WriteLine($"  … and {notes.Count - 25} more");
        }
        Console.WriteLine();
        Console.WriteLine("Sheet types: " + string.Join(", ", ledgers.GroupBy(l => l.SheetType).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")));
        if (ivJson is not null)
        {
            File.WriteAllText(ivJson, JsonSerializer.Serialize(new { File = Path.GetFileName(ivPdf), facts.Producer, facts.Pages, Scale = ivScale, Ledgers = ledgers, Summary = summary },
                new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"wrote {ivJson}");
        }
        return 0;
    }
}
