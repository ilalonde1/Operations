// The takeoff verb `markup-reconcile`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// THE DRAFTER'S REPLY IS BESIDE THE THING IT ANSWERS: a round of the engineer's mark-ups against
// the back-checked copy the drafter saved over it; each item done, replied or open, page by page.
// Usage: takeoff markup-reconcile <round.pdf> <backchecked.pdf> --scale N [--engineer <name>]
internal static class MarkupReconcileVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("markup-reconcile", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff markup-reconcile <round.pdf> <backchecked.pdf> --scale N [--engineer <name>] [--rules-db <conn>]"); return 1; }
        string mrRound = args[1], mrChecked = args[2];
        int mrScale = 0; string? mrEngineer = null, mrRules = null;
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i].Equals("--scale", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) int.TryParse(args[++i], out mrScale);
            else if (args[i].Equals("--engineer", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) mrEngineer = args[++i];
            else if (args[i].Equals("--rules-db", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) mrRules = args[++i];
        }
        if (!File.Exists(mrRound) || !File.Exists(mrChecked)) { Console.Error.WriteLine("Both PDFs must exist."); return 2; }
        if (mrScale <= 0) { Console.Error.WriteLine("--scale <denominator> is required (1/8\" = 1'-0\" is 96)."); return 2; }
        var (mrOptions, mrRulesSource) = PdfIntakeOptions.For(mrRules);
        using var mrRoundDoc = UglyToad.PdfPig.PdfDocument.Open(mrRound);
        using var mrCheckedDoc = UglyToad.PdfPig.PdfDocument.Open(mrChecked);
        var mrRequest = new IntakeRequest(mrScale, mrOptions);
        var mrRoundFacts = DocumentFacts.From(mrRoundDoc);
        var mrCheckedFacts = DocumentFacts.From(mrCheckedDoc);
        Console.WriteLine($"{Path.GetFileName(mrRound)} ({mrRoundDoc.NumberOfPages} pages) against {Path.GetFileName(mrChecked)} ({mrCheckedDoc.NumberOfPages} pages)   1:{mrScale}   rules: {mrRulesSource}");
        int done = 0, replied = 0, open = 0, unprompted = 0;
        var openLines = new List<string>();
        for (int p = 1; p <= Math.Min(mrRoundDoc.NumberOfPages, mrCheckedDoc.NumberOfPages); p++)
        {
            SheetRecord round, checkedRecord;
            try
            {
                round = DrawingIntake.ReadSheet(mrRoundDoc, p, mrRequest, mrRoundFacts);
                checkedRecord = DrawingIntake.ReadSheet(mrCheckedDoc, p, mrRequest, mrCheckedFacts);
            }
            catch (Exception ex) { Console.WriteLine($"p{p}: {ex.GetType().Name}: {ex.Message}"); continue; }
            var page = MarkupReconcile.Reconcile(round, checkedRecord, mrEngineer);
            if (page.Results.Count == 0 && page.Unprompted.Count == 0) continue;
            Console.WriteLine();
            Console.WriteLine($"p{p} {round.SheetNumber ?? "(no sheet number)"} {round.SheetType}: {page.Results.Count} item(s) by {page.Engineer} — {page.Done} done, {page.Replied} replied, {page.Open} open; {page.Unprompted.Count} unprompted");
            foreach (var r in page.Results)
            {
                string mark = r.Outcome switch { MarkupReconcile.Outcome.Done => "done   ", MarkupReconcile.Outcome.Replied => "replied", _ => "OPEN   " };
                string reply = r.Outcome == MarkupReconcile.Outcome.Replied ? $"  <- [{r.ReplyAuthor}] \"{r.Reply}\"" : "";
                Console.WriteLine($"  {mark}  {r.Item.Line()}{reply}");
                if (r.Outcome == MarkupReconcile.Outcome.Open) openLines.Add($"p{p} {r.Item.Line()}");
            }
            foreach (var u in page.Unprompted) Console.WriteLine($"  unprompted  {u.Line()}");
            done += page.Done; replied += page.Replied; open += page.Open; unprompted += page.Unprompted.Count;
        }
        Console.WriteLine();
        Console.WriteLine($"{done + replied + open} item(s): {done} done, {replied} replied, {open} open; {unprompted} unprompted note(s) by the drafter.");
        return 0;
    }
}
