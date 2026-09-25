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
//     <job>.e2k         the ETABS model
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

        // The job number is what the banked job-scoped facts are keyed on - her slab counts, her
        // match-line joins. Taken from the file name when it is not given, which is where it is on
        // every set in the corpus: "31005-01 2025-06-13 1ST & BEECH Stickset CC#5.pdf".
        job ??= JobNumberIn(Path.GetFileName(pdf)) ?? Path.GetFileNameWithoutExtension(pdf);

        Directory.CreateDirectory(outDir);
        // The banked rules, or the compiled defaults where KorStandards is unreachable - and it says which.
        var (options, rulesSource) = PdfIntakeOptions.For(rulesDb);

        Console.WriteLine($"stickfile : {Path.GetFileName(pdf)}");
        Console.WriteLine($"job       : {job}");
        Console.WriteLine($"out       : {Path.GetFullPath(outDir)}");
        Console.WriteLine($"rules     : {rulesSource}");
        Console.WriteLine();

        // THE SCALE IS READ OFF THE SHEET unless given. A page with no scale is the one case the
        // reader cannot get past on its own - every path stays unclassified - so it is said plainly.
        int useScale = scale ?? 96;
        if (scale is null) Console.WriteLine("scale     : not given; 96 assumed (1/8\" = 1'-0\"). Pass --scale if the set draws at another.");

        var built = PdfOnlyBuild.Build(pdf, outDir, useScale, options, rulesDb,
            onSheet: null, stem: job, firstPage: first, lastPage: last);

        Console.WriteLine();
        Console.WriteLine($"pages read: {built.Pages}   plan sheets: {built.Sheets.Sheets.Count(s => s.IsPlan)}   views written: {built.Sheets.Written}");

        if (built.Model is null)
        {
            Console.WriteLine();
            Console.WriteLine("NO MODEL WAS BUILT.");
            Console.WriteLine($"  {built.ModelError}");
            Console.WriteLine();
            Console.WriteLine($"  What was read is still here: {Path.Combine(outDir, "sheets.csv")} and {Path.Combine(outDir, "dxf")}.");
            return 2;
        }

        var m = built.Model;
        Console.WriteLine($"storeys   : {m.SavedModel.Storeys.Count}   with a floor: {m.PlatesByStorey.Count(p => p.Value > 0)}");
        Console.WriteLine($"members   : {m.SavedModel.Walls} walls   {m.SavedModel.Columns} columns   {m.SavedModel.Floors} floors");
        Console.WriteLine();
        Console.WriteLine($"  model   : {built.OutputE2k}");
        Console.WriteLine($"  report  : {Path.Combine(outDir, "report.txt")}");
        Console.WriteLine($"  levels  : {Path.Combine(outDir, "levels.csv")}");

        if (questions)
        {
            string wb = Path.Combine(outDir, "questions.xlsx");
            try
            {
                ModelQuestionnaire.Write(wb, m, m.ClassificationUsed, m.ComposeUsed, job);
                int open = ModelQuestionnaire.StandingQuestions(m.ClassificationUsed, m.ComposeUsed, m).Count(q => !q.Decided);
                Console.WriteLine($"  questions: {wb}   ({open} waiting on the engineer)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  questions: not written - {ex.Message}");
            }
        }

        if (render)
        {
            string png = Path.Combine(outDir, "model.png");
            try
            {
                // EVERY STOREY ON ONE SHEET. The repo's oldest standing rule about this pipeline is
                // that the output gets LOOKED AT rather than counted, and a model nobody renders is a
                // model whose faults are found by the engineer opening the file.
                ModelRenderVerb.Run(["model-render", built.OutputE2k, png, $"{job} - every storey"]);
                Console.WriteLine($"  render  : {png}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  render  : not written - {ex.Message}");
            }
        }

        // WHAT THE ENGINEER SHOULD LOOK AT FIRST, taken from the run's own flags rather than a guess.
        var loud = m.Warnings.Concat(m.Summary.Flags)
            .Where(w => w.Contains("NOT PLACED", StringComparison.Ordinal)
                        || w.Contains("ASSUMED, NOT READ", StringComparison.Ordinal)
                        || w.Contains("RECEIVED A FLOOR", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (loud.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("READ THESE FIRST:");
            foreach (string w in loud.Take(6))
                Console.WriteLine("  - " + (w.Length > 300 ? w[..300] + " ..." : w));
            if (loud.Count > 6) Console.WriteLine($"  ... and {loud.Count - 6} more in report.txt");
        }

        return 0;
    }

    /// <summary>The five-digit job and its suffix out of a file name: "31005-01 2025-06-13 ….pdf".</summary>
    private static string? JobNumberIn(string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(name, @"\b(\d{5}-\d{2})\b");
        return m.Success ? m.Groups[1].Value : null;
    }
}
