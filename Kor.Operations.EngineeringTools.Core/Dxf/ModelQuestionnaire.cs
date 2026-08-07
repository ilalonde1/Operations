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
        new ModelQuestion("H1", "Header depth",
            $"Headers are now generated over openings. They are {options.SpandrelDepth:0}\" deep — what depth do you want?",
            $"A spandrel beam spanning each opening, {options.SpandrelDepth:0}\" deep and the wall's thickness wide, " +
            "labelled so the same opening is one spandrel up the building.",
            "The header couples the piers either side of an opening; its depth drives how much.",
            "31168: openings measured as one cluster of gaps between 36\" and 48\" in a wall run."),

        new ModelQuestion("A1", "Short faces in a core",
            "An element under 48\" long is now a column, as you asked. Inside a core, a short wall face " +
            "between two returns also falls under that — do you want those kept as walls?",
            "Applied literally: anything under 48\" long becomes a column. The count is in the report.",
            "A short core face carries in-plane shear as a wall; as a column it carries none.",
            "31138: 45 panels moved from wall to column under this rule."),

        new ModelQuestion("P1", "Perimeter basement wall",
            "The below-grade perimeter wall is now read on three sides. The angled west wall is still " +
            "missed — is that wall drawn differently, or is a slanted face just harder?",
            "Walls drawn as two concentric rings are paired and read as one wall.",
            "It was the whole below-grade lateral system: \"the basement walls are missing\".",
            "31168 P1/P2/P3: parkade walls went from 36-45 per level to 41-48."),

        new ModelQuestion("W1", "Wall vs column",
            "YOUR RULE: \"less than 48 in length should be a column\".",
            $"Applied. Anything under {options.MinWallLength:0}\" long on plan is now a column, whatever layer it is drawn on.",
            "A pier modelled as a column carries no in-plane shear; a column modelled as a wall is too stiff.",
            "31138: 45 panels moved from wall to column. See A1 — this also catches short faces inside a core.")
            { Decided = true },

        new ModelQuestion("W2", "Thick walls",
            "YOUR RULE: \"some walls are thicker than 24\"\".",
            "Applied. The \"unusually thick, confirm\" flag is gone — it produced 615 notes on 31168 asking you " +
            $"to confirm something that is simply true. Walls up to {options.MaxWallThickness:0}\" are modelled without comment.",
            "A flag that is always wrong trains the reader to skip the whole list.",
            "31168's Revit sections carry real 36\" walls.")
            { Decided = true },

        new ModelQuestion("W3", "Doorways and headers",
            "YOUR RULE: \"the wall should stop at the opening. A header (spandrel) may be over the opening\".",
            "Applied, both halves. Openings are found between in-line wall ends and left open; a spandrel beam " +
            "is generated across each one and labelled.",
            "Without a header the piers either side of an opening are tied together by nothing.",
            "31168: gaps in a wall run measure as one cluster between 36\" and 48\", nothing below 18\" — those are doors.")
            { Decided = true },

        new ModelQuestion("C1", "Wall connectivity",
            "YOUR POINT: \"we can't have a wall go from here to here and then another one from here to here. " +
            "We need a connection\" — and \"this line is not aligned with this one\".",
            "Walls now form a network: centrelines meeting at a corner are carried out to where they cross and " +
            "share a joint, and a wall running into another splits it so the T has a joint on both members.",
            "A wall in ETABS is a shell; two walls only carry force between them where they share a joint.",
            "31168: half of all wall ends now share a joint with another member. Before, none did.")
            { Decided = true },

        new ModelQuestion("S1", "Storeys with no plate",
            "The parkade levels have walls and columns but no floor plate, because their slab edges will not close. " +
            "Do you want plates approximated there, or will you draw them?",
            $"Left out. A ring must reach {options.MinPlateArea / 144:0} sq ft to be modelled as a plate; " +
            "smaller standalone rings are slab-edge linework and were drawing as scraps of floor hanging in space.",
            "A storey without a plate has no diaphragm, and in a 3D view its members look unsupported.",
            "31168: 124 plates over 60 storeys, but LEVEL P1/P2/P3 and LEVEL 1 MEZZ carry members and no plate. " +
            "31138: standalone rings measured 52-68 sq ft against a real tower floor of 9,666."),

        new ModelQuestion("S2", "Slab openings",
            "Shafts and stair openings are found but not yet cut out of the plates. You said you can cut them — " +
            "worth the tool doing it, or leave it?",
            "Detected and reported; the plate is left whole.",
            "An uncut plate overstates floor area, mass and diaphragm stiffness at the core.",
            "Inner rings inside a slab outline are identified on every storey."),

        new ModelQuestion("Y1", "Yours, not the tool's",
            "YOUR SCOPE: loads and load assignment, diaphragms, stiffness modifiers, section properties.",
            "Removed from the tool. It no longer assigns diaphragms — the geometry arrives without them, so " +
            "there is nothing to undo. No loads are applied. Sections carry nominal thickness only.",
            "These are the judgement half of the work; you said you want to keep them, and they are quick.",
            "Green list: columns, walls, slabs, openings, grid lines, storey elevations. Yellow list: these.")
            { Decided = true },

        new ModelQuestion("W4", "Pier labels",
            "YOUR RULE: \"all walls should be assigned a pier label\".",
            "Applied. Every generated wall carries one, and walls at the same plan position on different storeys " +
            "share a label, so a pier is one element up the building.",
            "Without a shared label the forces come out panel by panel and are no use to design from.",
            "31168: 113 pier labels across 910 wall panels — that ratio is the walls stacking, which is right.")
            { Decided = true },

        new ModelQuestion("M1", "Storey framework",
            "YOUR ANSWER: \"Tower B model should only include tower B storeys\". NOT DONE YET — the model is " +
            "still built on the site storey list, which is why levels look blank. Do you want Tower B split out " +
            "as its own file, or the site model with the empty storeys removed?",
            "Still the site model's storeys. This is the largest thing outstanding from your feedback.",
            "It is why levels appear empty, and it decides how results are reported.",
            "31168's reference holds 60 storeys across towers A, B and C plus the shared podium."),
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
        if (flag.Contains("no floor plate", StringComparison.OrdinalIgnoreCase)) return "Storey has members but no plate — no diaphragm there";
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
