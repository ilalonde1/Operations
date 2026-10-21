#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.PdfToSafe;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// ONE STICK FILE IN, EVERY OUTPUT OUT, for every caller (2026-10-20). This was the body of the CLI's `stickfile` verb;
/// it moved here so the app's "Build whole building" button and the command run the SAME code and cannot drift - the
/// burgundy markup did exactly that between the app and the CLI and was red for a week before anyone noticed.
///
/// Into <c>outDir</c>: out.e2k, report.txt, levels.csv (PdfOnlyBuild), sheets.csv, questions.xlsx, model.svg/png, dxf/.
/// ⛔ PdfOnlyBuild.Build DELETES <c>outDir</c> first. A caller that must never overwrite (the app) passes a fresh folder.
/// </summary>
public static class StickfileRun
{
    /// <summary>What a run wrote and what it could not, for the caller to show. Paths are null where nothing was written.</summary>
    public sealed record Result(
        string Job, string RulesSource, int Scale, bool ScaleAssumed, PdfOnlyBuild.BuildOutcome Built, string SheetsCsv,
        string? Questions, int OpenQuestions, string? QuestionsError,
        string? Png, string? RenderError,
        IReadOnlyList<string> ReadFirst);

    /// <param name="scale">The drawing scale denominator; null = 96 (1/8" = 1'-0"), and <see cref="Result.ScaleAssumed"/> says so.</param>
    /// <param name="job">The job number the banked job-scoped facts are keyed on; null = taken from the file name.</param>
    /// <param name="rulesConnection">KorStandards; null = the compiled defaults, and <see cref="Result.RulesSource"/> says which.</param>
    public static Result Run(string pdf, string outDir, int? scale = null, string? job = null, string? rulesConnection = null,
        int? firstPage = null, int? lastPage = null, bool questions = true, bool render = true)
    {
        // The job number is what the banked job-scoped facts are keyed on - her slab counts, her
        // match-line joins. Taken from the file name when it is not given, which is where it is on
        // every set in the corpus: "31005-01 2025-06-13 1ST & BEECH Stickset CC#5.pdf".
        job ??= JobNumberIn(Path.GetFileName(pdf)) ?? Path.GetFileNameWithoutExtension(pdf);

        Directory.CreateDirectory(outDir);
        // The banked rules, or the compiled defaults where KorStandards is unreachable - and it says which.
        var (options, rulesSource) = PdfIntakeOptions.For(rulesConnection);

        // THE SCALE IS READ OFF THE SHEET unless given. A page with no scale is the one case the
        // reader cannot get past on its own - every path stays unclassified - so it is said plainly.
        int useScale = scale ?? 96;

        var built = PdfOnlyBuild.Build(pdf, outDir, useScale, options, rulesConnection,
            onSheet: null, stem: job, firstPage: firstPage, lastPage: lastPage);

        // WHAT EVERY PAGE GAVE, one row each. PdfOnlyBuild does not write this - the corpus analyzer
        // builds its own ledger and the verbs never did - so a single set had no way to see its own
        // sheet table except by reading 30 KB of report. It is the first thing anyone asks for when a
        // storey is empty: which drawing was supposed to draw it, and what did that drawing give?
        string sheetsCsv = Path.Combine(outDir, "sheets.csv");
        using (var w = new StreamWriter(sheetsCsv))
        {
            w.WriteLine("page,sheet_number,sheet_type,title,level,scale_note,scale_denominator,slabs,columns,walls,footings,lines,dxf_files,failure");
            foreach (var s in built.Sheets.Sheets.OrderBy(s => s.Page))
                w.WriteLine(string.Join(",", new[]
                {
                    s.Page.ToString(), Csv(s.SheetNumber), Csv(s.SheetType), Csv(s.Title), Csv(s.Level),
                    Csv(s.ScaleNote), s.ScaleDenominator?.ToString() ?? "",
                    s.Slabs.ToString(), s.Columns.ToString(), s.Walls.ToString(), s.Footings.ToString(),
                    s.Lines.ToString(), Csv(string.Join(" | ", s.DxfFiles)), Csv(s.Failure),
                }));
        }

        var m = built.Model;
        if (m is null)
            return new Result(job, rulesSource, useScale, scale is null, built, sheetsCsv, null, 0, null, null, null, []);

        string? wb = null, wbError = null; int open = 0;
        if (questions)
        {
            try
            {
                wb = Path.Combine(outDir, "questions.xlsx");
                ModelQuestionnaire.Write(wb, m, m.ClassificationUsed, m.ComposeUsed, job);
                open = ModelQuestionnaire.StandingQuestions(m.ClassificationUsed, m.ComposeUsed, m).Count(q => !q.Decided);
            }
            catch (Exception ex) { wb = null; wbError = ex.Message; }
        }

        string? png = null, renderError = null;
        if (render)
        {
            // EVERY STOREY ON ONE SHEET. The repo's oldest standing rule about this pipeline is
            // that the output gets LOOKED AT rather than counted, and a model nobody renders is a
            // model whose faults are found by the engineer opening the file.
            try
            {
                var (_, written, _) = ModelRender.Write(built.OutputE2k, Path.Combine(outDir, "model.png"), $"{job} - every storey");
                png = written;
                if (png is null) renderError = "Edge wrote no PNG (is Edge installed?); model.svg stands";
            }
            catch (Exception ex) { renderError = ex.Message; }
        }

        // WHAT THE ENGINEER SHOULD LOOK AT FIRST, taken from the run's own flags rather than a guess.
        var loud = m.Warnings.Concat(m.Summary.Flags)
            .Where(w => w.Contains("NOT PLACED", StringComparison.Ordinal)
                        || w.Contains("ASSUMED, NOT READ", StringComparison.Ordinal)
                        || w.Contains("RECEIVED A FLOOR", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new Result(job, rulesSource, useScale, scale is null, built, sheetsCsv, wb, open, wbError, png, renderError, loud);
    }

    /// <summary>One CSV field, quoted where it has to be: a sheet title carries commas and quotes.</summary>
    private static string Csv(string? v) =>
        string.IsNullOrEmpty(v) ? "" :
        v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r')
            ? "\"" + v.Replace("\"", "\"\"") + "\""
            : v;

    /// <summary>The five-digit job and its suffix out of a file name: "31005-01 2025-06-13 ….pdf".</summary>
    public static string? JobNumberIn(string name)
    {
        var m = Regex.Match(name, @"\b(\d{5}-\d{2})\b");
        return m.Success ? m.Groups[1].Value : null;
    }
}
