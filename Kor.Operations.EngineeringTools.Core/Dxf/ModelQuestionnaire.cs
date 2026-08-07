using ClosedXML.Excel;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>One thing the tool had to decide without being told, and the answer that would settle it.</summary>
public sealed record ModelQuestion(
    string Code,
    string Topic,
    string Question,
    string WhatWeDid,
    string WhyItMatters,
    string Evidence)
{
    /// <summary>
    /// True where the answer follows from engineering rather than preference, so it has been
    /// taken rather than asked. Still listed — an engineer disagreeing with one of these is
    /// worth more than one answering eight open questions.
    /// </summary>
    public bool Decided { get; init; }
}

/// <summary>
/// Writes the open questions from a run to a spreadsheet an engineer can answer in a column.
///
/// Every rule in this tool started as a judgement someone makes without thinking — is that a wall
/// or a pier, is that gap a doorway or the end of the concrete. Where the tool had to guess, the
/// guess is stated here with what it did and why it matters, so an answer turns it into a rule
/// instead of staying folded into the code.
/// </summary>
public static class ModelQuestionnaire
{
    /// <summary>Questions that apply to any drawing set, with what the tool currently assumes.</summary>
    public static IReadOnlyList<ModelQuestion> StandingQuestions(PlanClassificationOptions options) => new[]
    {
        new ModelQuestion("W1", "Wall vs pier",
            "A stubby element on the wall layer — pier or column?",
            $"Modelled as a wall panel on its long axis, up to {options.MaxPierThickness:0}\" thick. Only stockier outlines fall through to columns.",
            "A pier modelled as a column carries no in-plane shear and the core comes out softer than it is.",
            "70 elements on 31168 read this way; they are drawn on the wall layer, so they are lateral.")
            { Decided = true },

        new ModelQuestion("W2", "Thick walls",
            $"Walls thicker than {options.UnusualWallThickness:0}\" are flagged. Are the thick ones real, or should a maximum be set?",
            $"Anything between {options.MinWallThickness:0}\" and {options.MaxWallThickness:0}\" is modelled; thicker outlines are reported and skipped.",
            "A face paired across a junction reads thicker than the wall is, and overstates stiffness.",
            "31168's Revit sections include real 36\" walls, so thick is not automatically wrong."),

        new ModelQuestion("W3", "Doorways",
            "A wall enclosure is drawn with a gap where its door is. Should the wall be modelled through the opening, or stop either side of it?",
            "Modelled straight through: the enclosure is read as continuous wall.",
            "A door in a shear wall is a real reduction in stiffness and is usually modelled as an opening.",
            "31138 L10: a 46.6\" break in the stair enclosure outline."),

        new ModelQuestion("S1", "Slab plates",
            "Slab edges break where other linework crosses. Which storeys most need floor plates, and is a partial plate worse than none?",
            $"Rings smaller than {options.MinSlabArea / 144:0} sq ft are discarded; outlines that will not close are dropped and reported.",
            "Plates carry diaphragm action and mass; a wrong outline is worse than a missing one.",
            "31168: 128 plates over 60 storeys. 31138: 14 over 19."),

        new ModelQuestion("S2", "Openings",
            "Shafts and stair openings are found but not cut out of the plates. Should they be cut?",
            "Detected and reported; the plate is left whole.",
            "An uncut plate overstates floor area, mass and diaphragm stiffness at the core.",
            "Inner rings inside a slab outline are identified on every storey."),

        new ModelQuestion("L1", "Superimposed loads",
            "Superimposed dead and live loads are not applied to generated plates. What values belong where?",
            "None applied. Load patterns, load cases, mass source and true self-weight are set up and ready.",
            "SDL and live load drive gravity design and seismic mass; they cannot be read from a structural outline.",
            "31138 carries five different SDL values on L01 alone (5 to 145 psf), so no single value fits a storey."),

        new ModelQuestion("D1", "Diaphragms",
            "Rigid, semi-rigid, or none?",
            "A rigid diaphragm per storey, assigned to every generated plate.",
            "Modal results are not meaningful without one, and a concrete plate behaves as a rigid diaphragm.",
            "One per storey rather than one shared: a single diaphragm across elevations makes ETABS warn.")
            { Decided = true },

        new ModelQuestion("W4", "Thick wall ceiling",
            "Walls are modelled up to 36\" and piers to 48\". Are those the right ceilings for this building?",
            "36\" for a wall read from paired faces, 48\" for a pier drawn whole.",
            "Too low and real walls are dropped; too high and a face paired across a junction becomes a wall.",
            "31168's own Revit sections include 36\" walls, so thick is not automatically wrong.")
            { Decided = true },

        new ModelQuestion("M1", "Storey framework",
            "31168 is built on the site model's storeys (B-LEVEL 27 up, shared storeys below). Your lost model was Tower B alone, L01-L40. Which do you want?",
            "Site model storeys, because that is the reference ETABS exported.",
            "The frame decides how results are reported and how the model is compared with the old one.",
            "Recovered from the pier-force export of the lost model."),
    };

    public static void Write(string path, DxfToEtabsReport report, PlanClassificationOptions options, string projectName)
    {
        using var workbook = new XLWorkbook();
        WriteQuestions(workbook, report, options, projectName);
        WriteFlags(workbook, report);
        WriteSheetLedger(workbook, report);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        workbook.SaveAs(path);
    }

    private static void WriteQuestions(XLWorkbook workbook, DxfToEtabsReport report, PlanClassificationOptions options, string projectName)
    {
        var sheet = workbook.Worksheets.Add("Questions");

        sheet.Cell(1, 1).Value = $"{projectName} — questions for the engineer";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 13;
        sheet.Cell(2, 1).Value =
            "Rows marked DECIDED follow from engineering and have been applied — say so only if you disagree. " +
            "Rows marked OPEN need you. Either way, an answer becomes a rule the tool applies from then on.";
        sheet.Cell(2, 1).Style.Font.Italic = true;

        string[] headers = { "Ref", "Status", "Topic", "Question", "What the tool did", "Why it matters", "YOUR ANSWER", "Evidence" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = sheet.Cell(4, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(122, 34, 48);
            cell.Style.Font.FontColor = XLColor.White;
        }

        int row = 5;
        foreach (var q in StandingQuestions(options).OrderBy(q => q.Decided ? 1 : 0).ThenBy(q => q.Code))
        {
            sheet.Cell(row, 1).Value = q.Code;
            sheet.Cell(row, 2).Value = q.Decided ? "DECIDED" : "OPEN";
            sheet.Cell(row, 2).Style.Font.Bold = true;
            sheet.Cell(row, 2).Style.Font.FontColor = q.Decided ? XLColor.FromArgb(60, 110, 60) : XLColor.FromArgb(150, 90, 0);
            sheet.Cell(row, 3).Value = q.Topic;
            sheet.Cell(row, 4).Value = q.Question;
            sheet.Cell(row, 5).Value = q.WhatWeDid;
            sheet.Cell(row, 6).Value = q.WhyItMatters;
            sheet.Cell(row, 7).Style.Fill.BackgroundColor = XLColor.FromArgb(253, 246, 231);
            sheet.Cell(row, 8).Value = q.Evidence;
            row++;
        }

        sheet.Column(1).Width = 7;
        sheet.Column(2).Width = 11;
        sheet.Column(3).Width = 18;
        sheet.Column(4).Width = 46;
        sheet.Column(5).Width = 46;
        sheet.Column(6).Width = 44;
        sheet.Column(7).Width = 34;
        sheet.Column(8).Width = 40;
        sheet.Range(5, 4, row - 1, 8).Style.Alignment.WrapText = true;
        sheet.Range(5, 1, row - 1, 8).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        sheet.SheetView.FreezeRows(4);
    }

    /// <summary>
    /// Flags are per-location and run into the hundreds; nobody reads that. They are grouped into
    /// the handful of kinds they actually represent, with a count and one example each, so the
    /// sheet is a summary to react to rather than a list to work through.
    /// </summary>
    private static void WriteFlags(XLWorkbook workbook, DxfToEtabsReport report)
    {
        var sheet = workbook.Worksheets.Add("What needed judgement");
        sheet.Cell(1, 1).Value = "Grouped by kind. The count says how widespread it is; the example says where to look.";
        sheet.Cell(1, 1).Style.Font.Italic = true;

        string[] headers = { "Kind", "How often", "An example", "YOUR ANSWER" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = sheet.Cell(3, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(122, 34, 48);
            cell.Style.Font.FontColor = XLColor.White;
        }

        var groups = report.Summary.Flags
            .GroupBy(KindOf)
            .OrderByDescending(g => g.Count())
            .ToList();

        int row = 4;
        foreach (var group in groups)
        {
            sheet.Cell(row, 1).Value = group.Key;
            sheet.Cell(row, 2).Value = group.Count();
            sheet.Cell(row, 3).Value = group.First();
            sheet.Cell(row, 4).Style.Fill.BackgroundColor = XLColor.FromArgb(253, 246, 231);
            row++;
        }

        sheet.Column(1).Width = 44;
        sheet.Column(2).Width = 11;
        sheet.Column(3).Width = 88;
        sheet.Column(4).Width = 34;
        sheet.Range(4, 1, Math.Max(4, row - 1), 3).Style.Alignment.WrapText = true;
        sheet.Range(4, 1, Math.Max(4, row - 1), 4).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        sheet.SheetView.FreezeRows(3);
    }

    private static string KindOf(string flag)
    {
        if (flag.Contains("would not close", StringComparison.OrdinalIgnoreCase))
            return flag.Contains("slab", StringComparison.OrdinalIgnoreCase)
                ? "Slab outline broken — plate not made"
                : "Wall outline broken — panels read anyway";
        if (flag.Contains("unusually thick", StringComparison.OrdinalIgnoreCase)) return "Wall thicker than 24\" — confirm";
        if (flag.Contains("could not be resolved", StringComparison.OrdinalIgnoreCase)) return "Outline not readable as walls";
        if (flag.Contains("already modelled", StringComparison.OrdinalIgnoreCase)) return "Member you already have — not duplicated";
        if (flag.Contains("collapsed", StringComparison.OrdinalIgnoreCase)) return "Slab outline collapsed — skipped";
        return "Other";
    }

    private static void WriteSheetLedger(XLWorkbook workbook, DxfToEtabsReport report)
    {
        var sheet = workbook.Worksheets.Add("Sheets read");

        string[] headers = { "Drawing", "Levels", "Storeys it filled", "Walls", "Columns", "Slabs" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
        }

        int row = 2;
        foreach (var s in report.Sheets.OrderBy(s => s.File, StringComparer.OrdinalIgnoreCase))
        {
            sheet.Cell(row, 1).Value = s.File;
            sheet.Cell(row, 2).Value = string.Join(", ", s.Levels);
            sheet.Cell(row, 3).Value = string.Join(", ", s.Stories);
            sheet.Cell(row, 4).Value = s.Walls;
            sheet.Cell(row, 5).Value = s.Columns;
            sheet.Cell(row, 6).Value = s.Slabs;
            if (s.Stories.Count == 0) sheet.Row(row).Style.Fill.BackgroundColor = XLColor.FromArgb(255, 235, 235);
            row++;
        }

        sheet.Column(1).Width = 62;
        sheet.Columns(2, 3).Width = 26;
        sheet.SheetView.FreezeRows(1);
    }
}
