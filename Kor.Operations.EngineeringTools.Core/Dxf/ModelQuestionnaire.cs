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

        double minDepth = 24, maxDepth = 60;

        return new[]
        {
        new ModelQuestion("H1", "Header depth",
            $"Header depth follows your rule — storey height less an assumed {compose.OpeningHeight:0}\" opening — " +
            $"and is then held between {minDepth:0}\" and {maxDepth:0}\". So depth varies by storey rather than being " +
            "one number. Is the assumed opening height right, and are those bounds sensible?",
            $"Depth = storey height − {compose.OpeningHeight:0}\", clamped to {minDepth:0}–{maxDepth:0}\". The clamp " +
            "exists because a double-height storey otherwise produced a 396\" header, which is a wall.",
            "The header couples the piers either side of an opening; its depth drives how much.",
            $"Your 31138 model's own beams run 24\" to 36\" deep; raised-joint offsets across KOR's models run " +
            "1.7\" to 122\", so the bounds are policy rather than format."),

        new ModelQuestion("P1", "Perimeter basement wall",
            "FIXED. The below-grade perimeter wall is now read on all four sides, including the angled west one.",
            "Walls drawn as two concentric rings are paired and read as the one wall they are. The west wall was " +
            "dropped because the two rings were joined without a proper bridge, so its midpoint probed as void.",
            "It was the whole below-grade lateral system: \"the basement walls are missing\".",
            "31168 P2: the west wall now reads 2,806\" long at 88.1 degrees, alongside the north, south and east.")
            { Decided = true },

        new ModelQuestion("C1", "Corners that come out as one thick element",
            "You said \"this wall and this wall should be aligned — it's doing just one big wall that's not " +
            "aligned with this one\". A stepped block — one limb thinner than the other — is modelled as a " +
            "single pier on the box's long axis, so its centreline sits between the two limbs and matches " +
            "neither. Is that one pier, or a wall with a thickening? If the latter, what separates them?",
            "Left as one pier. Splitting it into limbs was tried and is worse: they come out stubby enough " +
            "that the aspect rule discards them and they fall through to columns, losing their in-plane shear.",
            "It decides whether these carry shear as walls, and whether their centrelines match the walls beside them.",
            "Measured on 31168: a 67\" x 42\" block, 28\" top strip and 42\" leg, modelled centred at y=3271 " +
            "while the core wall it continues runs at y=3278."),

        new ModelQuestion("F1", "Floors where no slab edge closes",
            "Where no slab edge closes, the floor has been taken from the inside face of the perimeter wall — " +
            "one outline, one thickness. Keep that, or will you draw them?",
            perimeterFloors + " A real slab-edge outline always wins where one exists; this is only the fallback.",
            "A storey with no plate has no diaphragm at all, and its walls and columns read as unsupported.",
            "The outline is the drawn inside face of the wall, so it is measured rather than invented."),

        new ModelQuestion("F2", "Storeys still with no floor",
            $"These have no closed outline anywhere on any slab layer, and no perimeter wall to fall back on " +
            $"either: {plateless}. They need a plate drawn — anything we should read instead?",
            "Left without a plate rather than invented from where the columns happen to sit.",
            "Those storeys have no diaphragm.",
            "Small closed rings do exist on some of them, but all are shaft-sized, far below a floor."),

        new ModelQuestion("A1", "Short faces in a core",
            "An element under 48\" long is now a column, as you asked. Inside a core, a short wall face " +
            "between two returns also falls under that — do you want those kept as walls?",
            "Applied literally: anything under 48\" long becomes a column. The count is in the report.",
            "A short core face carries in-plane shear as a wall; as a column it carries none.",
            "31138: 45 panels moved from wall to column under this rule."),


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

        new ModelQuestion("S1", "Scraps of floor",
            "Small closed rings on the slab-edge layers are linework, not floors.",
            $"A standalone ring must reach {options.MinPlateArea / 144:0} sq ft to be modelled as a plate.",
            "Modelled, each drew as a chip of concrete hanging in space.",
            "Measured across both projects the two populations do not overlap: standalone rings come out at " +
            "52-115 sq ft, real plates at 915 and up. 31138's tower floor is 9,666.")
            { Decided = true },

        new ModelQuestion("S2", "Slab openings",
            "DONE — you said you could cut these yourself, but they are on your green list, so the tool now does it.",
            "Shafts and stair openings are cut out of the plates as areas carrying no section, which is how your " +
            "own 31138 model does it. 46 cut on 31168.",
            "An uncut plate overstates floor area, mass and diaphragm stiffness at the core.",
            "Your 31138 model carries 42 openings drawn by hand, each an AREA with SECTION \"None\".")
            { Decided = true },

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
        sheet.Cell(2, 1).Value = "An answer becomes a rule the tool applies from then on.";
        sheet.Cell(2, 1).Style.Font.Italic = true;

        string[] headers = { "Ref", "Question", "What the tool did", "YOUR ANSWER" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = sheet.Cell(4, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(122, 34, 48);
            cell.Style.Font.FontColor = XLColor.White;
        }

        // Only what is still open. Listing what she has already ruled on is asking her to read her
        // own answers back.
        int row = 5;
        foreach (var q in StandingQuestions(options, compose, report).Where(q => !q.Decided).OrderBy(q => q.Code))
        {
            sheet.Cell(row, 1).Value = q.Code;
            sheet.Cell(row, 2).Value = q.Question;
            sheet.Cell(row, 3).Value = q.WhatWeDid;
            sheet.Cell(row, 4).Style.Fill.BackgroundColor = XLColor.FromArgb(253, 246, 231);
            row++;
        }

        sheet.Column(1).Width = 7;
        sheet.Column(2).Width = 66;
        sheet.Column(3).Width = 56;
        sheet.Column(4).Width = 40;
        sheet.Range(5, 2, row - 1, 4).Style.Alignment.WrapText = true;
        sheet.Range(5, 1, row - 1, 4).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
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

