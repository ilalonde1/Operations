using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

// The title reader's trace (intake step 109, 2026-09-16): why is this page one view? Every line that names a plan and
// every underlined line, whether it sits in the title block, and what SheetViews.Titles made of it - then the views it
// returns. 30838's S2.26 answered: the concrete-outline plan's title runs two lines and only the second is underlined.
// Usage: takeoff sheet-views <pdf> <page>
internal static class SheetViewsVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("sheet-views", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff sheet-views <pdf> <page>"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
        if (!int.TryParse(args[2], out int page) || page < 1) { Console.Error.WriteLine("Page must be a positive integer."); return 2; }

        var content = VectorPageReader.ReadPage(args[1], page);
        Console.WriteLine($"Page {page}: {content.WidthPts:F0}x{content.HeightPts:F0} pts, {content.Words.Count} words, {content.Paths.Count} paths");
        var lines = SheetViews.Explain(content);
        Console.WriteLine($"  {lines.Count} line(s) name a plan or are underlined (y up the page):");
        foreach (var l in lines)
        {
            string what = l.InTitleBlock ? "title block" : !l.Underlined ? "no underline" : l.NamesPlan ? "PLAN VIEW"
                : SheetViews.BeginsWithAConjunction(l.Text) ? "underlined, a title's tail?" : "underlined, names no plan";
            Console.WriteLine($"    y {l.YPts,7:0}  x {l.XPts,7:0}  {what,-28} \"{l.Text}\"");
        }
        var views = SheetViews.Titles(content);
        Console.WriteLine($"  {views.Count} view(s) read, left to right:");
        foreach (var v in views) Console.WriteLine($"    y {v.YPts,7:0}  x {v.MinXPts,6:0}..{v.MaxXPts,6:0}  \"{v.Title}\"");
        return 0;
    }
}
