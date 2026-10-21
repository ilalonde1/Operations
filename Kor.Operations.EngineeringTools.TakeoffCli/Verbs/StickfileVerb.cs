using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;

// The takeoff verb `stickfile`: ONE stick file in, every output out (2026-09-24).
// Usage: takeoff stickfile <stickfile.pdf> <outDir> [--scale N] [--job 31005-01] [--pages A-B]
//                          [--rules-db <conn>] [--no-render] [--no-questions]
//
// WHY THIS EXISTS. Ian, 2026-09-24: "get this working so I can give you a stickfile and get a
// choice of outputs." Until now there was no such command. The route existed - PdfOnlyBuild.Build
// does the whole thing and corpus-analyze calls it - but the only ways in were `pdf-takeoff` then
// `pdf-levels` then `dxf-to-etabs` chained by hand, or `corpus-analyze`, which wants a work
// directory, a job number and the projects share. Neither is "here is a drawing set, build me a
// model", and that is the product.
//
// WHAT IT WRITES, all into <outDir>:
//
//     out.e2k           the ETABS model
//     report.txt        the WHY for every count - what was read, assumed, refused, and how to fix it
//     questions.xlsx    the engineer's workbook: what the tool could not settle, one row per question
//     model.png         every storey drawn on one sheet, so the model is LOOKED at, not counted
//     levels.csv        the storey ladder, assumptions marked
//     sheets.csv        what each drawing gave: its level, its walls, columns and plates
//     dxf/              the views the reader wrote, for dxf-render and dxf-inspect
//
// A set the tool cannot build is an ordinary outcome, not a crash: the model error says which of the
// three causes it was and what would change it (PdfOnlyBuild.NoPlanSheetReason), and the sheet table
// is still written so the failure can be read rather than guessed at.
internal static class StickfileVerb
{
    public static bool Matches(string[] args) =>
        args.Length >= 1 && args[0].Equals("stickfile", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine(
                "Usage: takeoff stickfile <stickfile.pdf> <outDir> [--scale N] [--job 31005-01] " +
                "[--pages A-B] [--rules-db <conn>] [--no-render] [--no-questions]");
            return 1;
        }

        string pdf = args[1], outDir = args[2];
        if (!File.Exists(pdf)) { Console.Error.WriteLine($"Not found: {pdf}"); return 1; }

        int? scale = null, first = null, last = null;
        string? job = null, rulesDb = null;
        bool render = true, questions = true;

        for (int i = 3; i < args.Length; i++)
        {
            string a = args[i];
            if (a.Equals("--scale", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) scale = int.Parse(args[++i]);
            else if (a.Equals("--job", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) job = args[++i];
            else if (a.Equals("--rules-db", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) rulesDb = args[++i];
            else if (a.Equals("--no-render", StringComparison.OrdinalIgnoreCase)) render = false;
            else if (a.Equals("--no-questions", StringComparison.OrdinalIgnoreCase)) questions = false;
            else if (a.Equals("--pages", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var parts = args[++i].Split('-', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1) first = int.Parse(parts[0]);
                if (parts.Length >= 2) last = int.Parse(parts[1]);
            }
        }

        // THE WHOLE RUN IS StickfileRun (Core, 2026-10-20), shared with the app's "Build whole building" button so
        // the two cannot drift. This verb parses its arguments and prints.
        var r = StickfileRun.Run(pdf, outDir, scale, job, rulesDb, first, last, questions, render);
        var built = r.Built;

        Console.WriteLine($"stickfile : {Path.GetFileName(pdf)}");
        Console.WriteLine($"job       : {r.Job}");
        Console.WriteLine($"out       : {Path.GetFullPath(outDir)}");
        Console.WriteLine($"rules     : {r.RulesSource}");
        Console.WriteLine();
        if (r.ScaleAssumed) Console.WriteLine("scale     : not given; 96 assumed (1/8\" = 1'-0\"). Pass --scale if the set draws at another.");

        Console.WriteLine();
        Console.WriteLine($"pages read: {built.Pages}   plan sheets: {built.Sheets.Sheets.Count(s => s.IsPlan)}   views written: {built.Sheets.Written}");

        if (built.Model is null)
        {
            Console.WriteLine();
            Console.WriteLine("NO MODEL WAS BUILT.");
            Console.WriteLine($"  {built.ModelError}");
            Console.WriteLine();
            Console.WriteLine($"  What was read is still here: {r.SheetsCsv} and {Path.Combine(outDir, "dxf")}.");
            return 2;
        }

        var m = built.Model;
        Console.WriteLine($"storeys   : {m.SavedModel.Storeys.Count}   with a floor: {m.PlatesByStorey.Count(p => p.Value > 0)}");
        Console.WriteLine($"members   : {m.SavedModel.Walls} walls   {m.SavedModel.Columns} columns   {m.SavedModel.Floors} floors");
        Console.WriteLine();
        Console.WriteLine($"  model   : {built.OutputE2k}");
        Console.WriteLine($"  report  : {Path.Combine(outDir, "report.txt")}");
        Console.WriteLine($"  levels  : {Path.Combine(outDir, "levels.csv")}");
        Console.WriteLine($"  sheets  : {r.SheetsCsv}");
        if (questions)
            Console.WriteLine(r.Questions is not null
                ? $"  questions: {r.Questions}   ({r.OpenQuestions} waiting on the engineer)"
                : $"  questions: not written - {r.QuestionsError}");
        if (render)
            Console.WriteLine(r.Png is not null ? $"  render  : {r.Png}" : $"  render  : not written - {r.RenderError}");

        if (r.ReadFirst.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("READ THESE FIRST:");
            foreach (string w in r.ReadFirst.Take(6))
                Console.WriteLine("  - " + (w.Length > 300 ? w[..300] + " ..." : w));
            if (r.ReadFirst.Count > 6) Console.WriteLine($"  ... and {r.ReadFirst.Count - 6} more in report.txt");
        }

        return 0;
    }
}
