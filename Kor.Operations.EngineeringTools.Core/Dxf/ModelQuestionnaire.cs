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

    public string RuleScope { get; init; } = "etabs-modelling";
    public string RuleTopic { get; init; } = string.Empty;
    public string? SettingKey { get; init; }
    public string? SettingUnits { get; init; }
    public string Confidence { get; init; } = "engineer-confirmed";
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
    /// <summary>
    /// The questions, carrying only facts true of the project being written.
    ///
    /// <paramref name="report"/> is what keeps them honest. Written as fixed prose they carried one
    /// project's numbers into both workbooks, so 31138's asked the engineer to rule on 31168's
    /// storeys and quoted 31168's plate areas. A question about the wrong building is worse than no
    /// question: it says nobody read what was sent.
    /// </summary>
    public static IReadOnlyList<ModelQuestion> StandingQuestions(
        PlanClassificationOptions options, ComposeOptions compose, DxfToEtabsReport? report = null)
    {
        // Whatever this project's own run found, rather than a number typed in once.
        string plateless = report?.Summary.Flags
            .FirstOrDefault(f => f.Contains("no floor plate", StringComparison.OrdinalIgnoreCase)) is { } f2
            ? f2[(f2.IndexOf(':') + 1)..].Split('.')[0].Trim()
            : "the storeys named in the report";

        string perimeterFloors = report?.Summary.Flags
            .Any(f => f.Contains("perimeter wall", StringComparison.OrdinalIgnoreCase)) == true
            ? "Each level with no closed slab edge has been given one plate from the inside face of its perimeter wall."
            : "No level needed this on your project — every plate came from a drawn slab edge.";

        double minDepth = compose.SpandrelDepthFloor, maxDepth = compose.SpandrelDepthCeiling;

        return new[]
        {
        new ModelQuestion("H1", "Header depth",
            "Current rule. Change these numbers only if this job needs a different opening height or header-depth clamp.",
            $"Depth = storey height − {compose.OpeningHeight:0}\", clamped to {minDepth:0}–{maxDepth:0}\". " +
            "The backing evidence lives in KorStandards; a nonblank answer here supersedes the rule for future jobs.",
            "The header couples the piers either side of an opening; its depth drives how much.",
            "Answer with three numbers if changing it, for example: opening 90, clamp 18-60.")
            {
                RuleTopic = "header-depth-from-opening-height",
                SettingKey = "dxf.opening-height;dxf.spandrel-depth-floor;dxf.spandrel-depth-ceiling",
                SettingUnits = "in;in;in",
                Decided = true
            },

        new ModelQuestion("P1", "Perimeter basement wall",
            "FIXED. The below-grade perimeter wall is now read on all four sides, including the angled west one.",
            "Walls drawn as two concentric rings are paired and read as the one wall they are. The west wall was " +
            "dropped because the two rings were joined without a proper bridge, so its midpoint probed as void.",
            "It was the whole below-grade lateral system: \"the basement walls are missing\".",
            "If this job has a perimeter wall drawn only as hatch or non-linework, it should appear in the report as unread.")
            { Decided = true },

        new ModelQuestion("C1", "Corners that come out as one thick element",
            "You said \"this wall and this wall should be aligned — it's doing just one big wall that's not " +
            "aligned with this one\". A stepped block — one limb thinner than the other — is still modelled as a " +
            "single pier on the box's long axis, so its centreline sits between the two limbs and matches " +
            "neither. Should these be limbs on their own centrelines sharing one pier label, or one stocky pier?",
            "Still one pier. Not for want of trying: the decomposer's face floor was lowered to 12\" and its " +
            "panel aspect relaxed, and these blocks still do not come apart, so something earlier is refusing " +
            "them. Rather than guess a third time it is measured next. The limbs are no longer at risk either " +
            "way — anything longer than three times its width now stays a wall whatever it touches.",
            "It decides whether these carry shear as walls, and whether their centrelines match the walls beside them.",
            "Answering this records the judgement; it will not change geometry until a decomposition rule is added.")
            { RuleTopic = "corner-limbs-vs-stocky-pier" },

        new ModelQuestion("F1", "Floors where no slab edge closes",
            "Where no slab edge closes, the floor has been taken from the inside face of the perimeter wall — " +
            "one outline, one thickness. Keep that, or will you draw them?",
            perimeterFloors + " A real slab-edge outline always wins where one exists; this is only the fallback.",
            "A storey with no plate has no diaphragm at all, and its walls and columns read as unsupported.",
            "The outline is the drawn inside face of the wall, so it is measured rather than invented.")
            { RuleTopic = "floor-from-perimeter-wall" },

        new ModelQuestion("F2", "Storeys still with no floor",
            $"These have no closed outline anywhere on any slab layer, and no perimeter wall to fall back on " +
            $"either: {plateless}. They need a plate drawn — anything we should read instead?",
            "Left without a plate rather than invented from where the columns happen to sit.",
            "Those storeys have no diaphragm.",
            "Small closed rings do exist on some of them, but all are shaft-sized, far below a floor.")
            { RuleTopic = "storeys-with-no-drawn-floor" },

        new ModelQuestion("A1", "Short faces in a core",
            "SETTLED, from your own model — nothing to answer.",
            "Two things decide it now, and neither is length alone. A short face joined to other walls is part of " +
            "a core and stays a wall. And anything longer than three times its width stays a wall whatever it " +
            "touches. Change the slenderness limit here if this job treats a different footprint aspect as a column.",
            "A short core face carries in-plane shear as a wall; as a column it carries none.",
            "Answer with one ratio if changing it, for example: 2.5.")
            {
                RuleTopic = "column-slenderness-limit",
                SettingKey = "dxf.max-column-aspect",
                SettingUnits = "ratio",
                Decided = true
            },


        new ModelQuestion("W1", "Wall vs column",
            "YOUR RULE: \"less than 48 in length should be a column\".",
            $"Applied. Anything under {options.MinWallLength:0}\" long on plan is now a column, whatever layer it is drawn on.",
            "A pier modelled as a column carries no in-plane shear; a column modelled as a wall is too stiff.",
            "Answer with one length in inches if changing it.")
            {
                RuleTopic = "wall-vs-column-length",
                SettingKey = "dxf.min-wall-length",
                SettingUnits = "in",
                Decided = true
            },

        new ModelQuestion("W2", "Thick walls",
            "YOUR RULE: \"some walls are thicker than 24\"\".",
            $"Applied. Walls up to {options.MaxWallThickness:0}\" are modelled without comment.",
            "A flag that is always wrong trains the reader to skip the whole list.",
            "Answer with one thickness in inches if changing it.")
            {
                RuleTopic = "thick-walls-are-real",
                SettingKey = "dxf.max-wall-thickness",
                SettingUnits = "in",
                Decided = true
            },

        new ModelQuestion("W3", "Doorways and headers",
            "YOUR RULE: \"the wall should stop at the opening. A header (spandrel) may be over the opening\".",
            "Applied, both halves. Openings are found between in-line wall ends and left open; a spandrel beam " +
            "is generated across each one and labelled.",
            "Without a header the piers either side of an opening are tied together by nothing.",
            "Opening span limits are read from KorStandards.")
            { Decided = true },

        new ModelQuestion("O1", "The openings you marked on the tower floors",
            "Some apparent openings may sit between perimeter elements drawn on a column layer rather than a wall layer. " +
            "Are they wall panels with openings between them, or columns?",
            "Left as columns when the drawing layer says column. An opening is generated where two in-line wall ends face " +
            "each other, so a gap between two columns produces none.",
            "A perimeter of columns carries no in-plane shear where a perimeter of pierced wall does, and the " +
            "difference runs the height of the tower. It also decides whether those gaps want headers.",
            "Answering this records the judgement; it will not change geometry until a perimeter wall/column rule is added.")
            { RuleTopic = "perimeter-column-layer-openings" },

        new ModelQuestion("C2", "Wall connectivity",
            "YOUR POINT: \"we can't have a wall go from here to here and then another one from here to here. " +
            "We need a connection\" — and \"this line is not aligned with this one\".",
            "Walls now form a network: centrelines meeting at a corner are carried out to where they cross and " +
            "share a joint, and a wall running into another splits it so the T has a joint on both members.",
            "A wall in ETABS is a shell; two walls only carry force between them where they share a joint.",
            "Answer yes/no if changing this.")
            {
                RuleTopic = "wall-connectivity-required",
                SettingKey = "dxf.connect-walls",
                SettingUnits = "bool",
                Decided = true
            },

        new ModelQuestion("S1", "Scraps of floor",
            "Small closed rings on the slab-edge layers are linework, not floors.",
            $"A standalone ring must reach {options.MinPlateArea / 144:0} sq ft to be modelled as a plate.",
            "Modelled, each drew as a chip of concrete hanging in space.",
            "Answer with an area, for example: 450 sq ft.")
            {
                RuleTopic = "standalone-ring-plate-threshold",
                SettingKey = "dxf.min-plate-area",
                SettingUnits = "sqin",
                Decided = true
            },

        new ModelQuestion("S2", "Slab openings",
            "DONE — you said you could cut these yourself, but they are on your green list, so the tool now does it.",
            "Shafts and stair openings are cut out of the plates as areas carrying no section, which is how your " +
            "ETABS imports floor openings.",
            "An uncut plate overstates floor area, mass and diaphragm stiffness at the core.",
            "If this job uses a different opening convention, answer this row.")
            { Decided = true },

        new ModelQuestion("Y1", "Yours, not the tool's",
            "YOUR SCOPE: loads and load assignment, diaphragms, stiffness modifiers, section properties.",
            "Removed from the tool. It no longer assigns diaphragms — the geometry arrives without them, so " +
            "there is nothing to undo. No loads are applied. Sections carry nominal thickness only.",
            "These are the judgement half of the work; you said you want to keep them, and they are quick.",
            "Green list: columns, walls, slabs, openings, grid lines, storey elevations. Yellow list: these.")
            {
                RuleTopic = "diaphragms-are-the-engineers",
                SettingKey = "dxf.assign-diaphragms",
                SettingUnits = "bool",
                Decided = true
            },

        new ModelQuestion("W4", "Pier labels",
            "YOUR RULE: \"all walls should be assigned a pier label\".",
            "Applied. Every generated wall carries one, and walls at the same plan position on different storeys " +
            "share a label, so a pier is one element up the building.",
            "Without a shared label the forces come out panel by panel and are no use to design from.",
            "Answer yes/no if changing this.")
            {
                RuleTopic = "pier-label-every-wall",
                SettingKey = "dxf.assign-pier-labels",
                SettingUnits = "bool",
                Decided = true
            },

        new ModelQuestion("M1", "Storey framework",
            "YOUR RULE: \"let's have a full model with both towers modelled, I will separate the towers later\".",
            "Applied. One model on the site storey list, both towers. The Tower-B-only variant that existed " +
            "before you said this has been withdrawn so there is no second file to choose between.",
            "It decides how results are reported and how the model compares with the old one.",
            "The tool can still split a tower out on request; nothing about that is lost.")
            { Decided = true },
        };
    }

    public static void Write(string path, DxfToEtabsReport report, PlanClassificationOptions options,
        ComposeOptions compose, string projectName)
    {
        // Two sheets. The questions, and the lookup for when something in the model looks wrong.
        // The per-drawing ledger lives in the report; it is 60 rows nobody reads in a spreadsheet.
        using var workbook = new XLWorkbook();
        WriteQuestions(workbook, report, options, compose, projectName);
        WriteFlags(workbook, report);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        workbook.SaveAs(path);
    }

    private static void WriteQuestions(XLWorkbook workbook, DxfToEtabsReport report, PlanClassificationOptions options, ComposeOptions compose, string projectName)
    {
        var sheet = workbook.Worksheets.Add("Questions");

        sheet.Cell(1, 1).Value = $"{projectName} — questions";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 13;
        sheet.Cell(2, 1).Value = "Answer only rows you want to change or settle. A nonblank answer becomes a rule the tool applies from then on.";
        sheet.Cell(2, 1).Style.Font.Italic = true;

        string[] headers =
        {
            "Ref", "Question", "What the tool did", "YOUR ANSWER",
            "Rule scope", "Rule topic", "Setting key", "Setting units", "Confidence"
        };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = sheet.Cell(4, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(122, 34, 48);
            cell.Style.Font.FontColor = XLColor.White;
        }

        int row = 5;
        foreach (var q in StandingQuestions(options, compose, report).OrderBy(q => q.Code))
        {
            sheet.Cell(row, 1).Value = q.Code;
            sheet.Cell(row, 2).Value = q.Question;
            sheet.Cell(row, 3).Value = q.WhatWeDid;
            sheet.Cell(row, 4).Style.Fill.BackgroundColor = XLColor.FromArgb(253, 246, 231);
            sheet.Cell(row, 5).Value = q.RuleScope;
            sheet.Cell(row, 6).Value = string.IsNullOrWhiteSpace(q.RuleTopic) ? q.Topic : q.RuleTopic;
            sheet.Cell(row, 7).Value = q.SettingKey ?? string.Empty;
            sheet.Cell(row, 8).Value = q.SettingUnits ?? string.Empty;
            sheet.Cell(row, 9).Value = q.Confidence;
            row++;
        }

        sheet.Column(1).Width = 7;
        sheet.Column(2).Width = 66;
        sheet.Column(3).Width = 56;
        sheet.Column(4).Width = 40;
        sheet.Columns(5, 9).Hide();
        sheet.Range(5, 2, row - 1, 4).Style.Alignment.WrapText = true;
        sheet.Range(5, 1, row - 1, 9).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        sheet.SheetView.FreezeRows(4);
    }

    /// <summary>
    /// Everything the tool decided that an engineer might decide differently, in one list.
    ///
    /// Per-location flags run into the hundreds and nobody reads that, so they are grouped into the
    /// handful of kinds they represent. Each row says what was done, how widespread it is, where to
    /// look, and whether it is an approximation offered or a limit of the drawing — the difference
    /// being whether overriding it is a choice or a necessity.
    /// </summary>
    private static void WriteFlags(XLWorkbook workbook, DxfToEtabsReport report)
    {
        var sheet = workbook.Worksheets.Add("If something looks wrong");
        sheet.Cell(1, 1).Value =
            "No answer needed. What the tool assumed, and where the drawings ran out. Counts are locations, not decisions.";
        sheet.Cell(1, 1).Style.Font.Italic = true;

        string[] headers = { "What", "Locations", "An example" };
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
            row++;
        }

        sheet.Column(1).Width = 62;
        sheet.Column(2).Width = 11;
        sheet.Column(3).Width = 88;
        sheet.Range(4, 1, Math.Max(4, row - 1), 3).Style.Alignment.WrapText = true;
        sheet.Range(4, 1, Math.Max(4, row - 1), 3).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        sheet.SheetView.FreezeRows(3);
    }

    private static string KindOf(string flag)
    {
        if (flag.Contains("perimeter wall", StringComparison.OrdinalIgnoreCase))
            return "APPROXIMATION · floor taken from the inside of the perimeter wall — replace with your outline where it matters";
        if (flag.Contains("no floor plate", StringComparison.OrdinalIgnoreCase))
            return "DRAWING LIMIT · storey has members but no plate, so no diaphragm — nothing in the drawing closes to make one";
        if (flag.Contains("could not be resolved", StringComparison.OrdinalIgnoreCase))
            return "DRAWING LIMIT · outline read but modelled as nothing — check this location";
        if (flag.Contains("modelled as columns", StringComparison.OrdinalIgnoreCase))
            return "APPROXIMATION · short element joined to nothing, modelled as a column per your 48\" rule";
        if (flag.Contains("would not close", StringComparison.OrdinalIgnoreCase))
            return flag.Contains("slab", StringComparison.OrdinalIgnoreCase)
                ? "DRAWING LIMIT · slab outline broken, so no plate from it"
                : "APPROXIMATION · wall outline broken, panels read from it anyway";
        if (flag.Contains("too small for a floor plate", StringComparison.OrdinalIgnoreCase))
            return "APPROXIMATION · small closed ring treated as linework, not a floor";
        if (flag.Contains("already modelled", StringComparison.OrdinalIgnoreCase))
            return "NO ACTION · member you already have, not duplicated";
        if (flag.Contains("storey(s) belonging to other towers", StringComparison.OrdinalIgnoreCase))
            return "NO ACTION · other towers' storeys removed from this model";
        if (flag.Contains("lowest storey", StringComparison.OrdinalIgnoreCase))
            return "APPROXIMATION · lowest storey given a typical height, because the export folded the base into it";
        if (flag.Contains("collapsed", StringComparison.OrdinalIgnoreCase))
            return "DRAWING LIMIT · slab outline collapsed, skipped";
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

