using System.Globalization;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// True where this row is a matter of record rather than something an engineer opening the
    /// job needs to read: a bug we fixed, or a default that is only interesting on a drawing set
    /// from another office.
    ///
    /// It stays in the workbook — on the reference sheet, with everything else. What it stops
    /// doing is competing for attention on the front page. Twenty-eight rows of which ten matter
    /// is a page that gets closed, and three of them told a KOR engineer what KOR's own layer
    /// convention is.
    /// </summary>
    public bool ForTheRecord { get; init; }

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
            .FirstOrDefault(f => f.Contains("carry walls or columns and no floor plate", StringComparison.OrdinalIgnoreCase)) is { } f2
            ? f2[(f2.IndexOf(':') + 1)..].Split('.')[0].Trim()
            : "the storeys named in the report";

        string perimeterFloors = report?.Summary.Flags
            .Any(f => f.Contains("perimeter wall", StringComparison.OrdinalIgnoreCase)) == true
            ? "Each level with no closed slab edge has been given one plate from the inside face of its perimeter wall."
            : "No level needed this on your project — every plate came from a drawn slab edge.";

        // M1's answer was fixed text saying "both towers" — true when she gave that rule. Then she
        // asked for the towers out, the run dropped eight storeys, and the workbook still told her
        // both towers were modelled, in her own quoted words, beside a file that does not contain
        // them. A row contradicting the model next to it makes an engineer doubt the whole package.
        string storeyFramework = report?.Summary.Flags
            .FirstOrDefault(f => f.Contains("belong to a building this model is not of", StringComparison.OrdinalIgnoreCase)) is { } f4
            ? "Applied, then NARROWED at your request — this model is not the whole site. " +
              f4[(f4.IndexOf("Removed:", StringComparison.Ordinal) is var i && i >= 0 ? i : 0)..].Split('.')[0].Trim() +
              ". The storey list is otherwise the site list, so results and comparisons still line up " +
              "with the model you had. Ask and the towers come back in one run."
            : "Applied. One model on the site storey list, both towers. The Tower-B-only variant that existed " +
              "before you said this has been withdrawn so there is no second file to choose between.";

        string leftAlone = report?.Summary.Flags
            .FirstOrDefault(f => f.Contains("were already modelled", StringComparison.OrdinalIgnoreCase)) is { } f3
            ? "On this job " + char.ToLowerInvariant(f3[0]) + f3[1..].TrimEnd() +
              " Turning this off would have added every one of them a second time."
            : "Nothing of yours was recognised at a generated location on this job, so nothing was skipped for " +
              "this reason — the count appears in the report when it is not zero.";

        string beamEvidence = report?.Summary.Flags
            .FirstOrDefault(f => f.Contains("names a member this tool does not model", StringComparison.OrdinalIgnoreCase)) is { } beam
            ? beam
            : "This job's structural layers did not contain a beam, joist, brace or truss layer with enough " +
              "linework to read as framing. Where a future drawing set does, the report names that layer and " +
              "says nothing from it is in the model.";

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
            { Decided = true, ForTheRecord = true },

        new ModelQuestion("C1", "Corners that come out as one thick element",
            "FIXED, and it was our bug rather than a judgement call. An L-shaped corner was coming out as one " +
            "thick element on a centreline that matched neither limb.",
            "An L now comes apart into its two limbs, each on its own centreline. The cause was order, not " +
            "geometry: a solid-footprint test ran BEFORE the decomposer and claimed anything filling most of its " +
            "bounding box, and an L fills 85% of its own. Handed the same outline directly, the decomposer had " +
            "always returned both limbs. So the decomposer is asked first, and the one-pier branch now keeps only " +
            "what genuinely does not come apart — a shape yielding fewer than two panels.",
            "It decides whether these carry shear as walls, and whether their centrelines match the walls beside them.",
            "Measured on the regression outline that exposed it: the corner is a 67x28 wall with a 36-thick " +
            "leg turned down beside it. It had been ONE " +
            "panel 67 long and 42 thick — thicker than anything drawn there, the leg gone and the doorway under it " +
            "gone with it. On the measured model it recovered 84 more wall panels, 156 more headers and four openings " +
            "including the 55\" gap on both corners. Held by a baseline test.")
            { RuleTopic = "corner-limbs-vs-stocky-pier", Decided = true, ForTheRecord = true },

        new ModelQuestion("F1", "Floors where no slab edge closes",
            "OUR DECISION — one cell here turns it off. Where a storey's slab edges will not close, its floor is " +
            "taken from the inside face of the perimeter wall: one outline, one thickness, flagged in the report " +
            "as an approximation.",
            perimeterFloors + " A real slab-edge outline always wins where one exists, so this governs only the " +
            "storeys that have none. Answer 0 to leave those storeys without a plate instead.",
            "A storey with no plate has no diaphragm at all, and its walls and columns read as unsupported.",
            "The outline is the drawn inside face of the wall, so it is measured rather than invented. It is left " +
            "on because the alternative is not a smaller approximation, it is a storey that looks structurally " +
            "absurd in a way that hides the real point — that nobody drew the slab.")
            {
                RuleTopic = "floor-from-perimeter-wall",
                SettingKey = "dxf.floor-from-perimeter-wall",
                SettingUnits = "bool",
                Decided = true
            },

        new ModelQuestion("F2", "Storeys still with no floor",
            $"OUR DECISION — these are left without a plate rather than given an invented one: {plateless}. They " +
            $"need a slab edge drawn. Tell us here if something else on those drawings should be read as the floor.",
            "Nothing is invented from where the columns happen to sit. There is no closed outline on any slab " +
            "layer of these storeys and no perimeter wall to fall back on either, so F1's fallback has nothing " +
            "to work from.",
            "Those storeys have no diaphragm, and the report says so rather than hiding it behind a guessed plate.",
            "Small closed rings do exist on some of them, but every one is shaft-sized — far below a floor. A " +
            "convex hull of the members standing there would close the gap and would be a fabrication: it would " +
            "put slab where the drawings show none, and it would be indistinguishable in the model from a plate " +
            "somebody drew.")
            { RuleTopic = "storeys-with-no-drawn-floor", Decided = true },

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
            $"A gap in a wall run between {options.MinOpeningSpan:0}\" and {options.MaxOpeningSpan:0}\" is read as a " +
            "doorway. Narrower is taken for a drafting break; wider is taken for two different walls. " +
            "Answer with two spans in inches to change it.")
            {
                RuleTopic = "wall-run-gaps-are-doorways",
                SettingKey = "dxf.min-opening-span;dxf.max-opening-span",
                SettingUnits = "in;in",
                Decided = true
            },

        new ModelQuestion("W5", "How thin is a wall, and how thick is worth a second look",
            $"Two faces closer together than {options.MinWallThickness:0}\" are not read as a wall at all. " +
            $"Above {options.UnusualWallThickness:0}\" a wall is modelled and noted rather than questioned. " +
            "Both are ours rather than yours — change either if they are wrong for your work.",
            $"Applied: {options.MinWallThickness:0}\" floor, {options.UnusualWallThickness:0}\" the point past which " +
            "a thickness is worth an eye.",
            "The floor decides what is linework and what is concrete. Too high and thin walls vanish; too low and " +
            "drafting noise becomes structure.",
            "Measured across the portfolio: only 58 wall sections in 1,126 engineer models fall below 4\", and all " +
            "are placeholders — 0.048\", 1\", 2\", 3\". Answer with two thicknesses in inches to change it.")
            {
                RuleTopic = "wall-thickness-bounds",
                SettingKey = "dxf.min-wall-thickness;dxf.unusual-wall-thickness",
                SettingUnits = "in;in",
                Decided = true
            },

        new ModelQuestion("C3", "How large may a column be",
            $"A footprint on a column layer is modelled as a column when its short face is at least " +
            $"{options.MinColumnSize:0}\" and its long face no more than {options.MaxColumnSize:0}\". Outside that it " +
            "is reported rather than modelled. Are those the right bounds?",
            $"Applied. Anything outside {options.MinColumnSize:0}\"–{options.MaxColumnSize:0}\" is named in the report " +
            "with its location, rather than being dropped.",
            "The upper bound is the one that bites. A blade column past it is not modelled at all, and until it was " +
            "reported that happened without a word.",
            "Measured across 1,126 engineer models: 7,538 concrete column sections, short faces 6\"–54\" with not one " +
            "below 6\", long faces to 165\". 96\" admitted 97.3% and was raised to 132\", which admits 99.2\". " +
            "Answer with two dimensions in inches to change it.")
            {
                RuleTopic = "column-size-bounds",
                SettingKey = "dxf.min-column-size;dxf.max-column-size",
                SettingUnits = "in;in",
                Decided = true
            },

        new ModelQuestion("C4", "How stocky may a pier be",
            $"A solid footprint on a wall layer stays one wall panel on its long axis while it is no thicker than " +
            $"{options.MaxPierThickness:0}\". Past that it is treated as something else. A boundary element at the " +
            "end of a core wall is routinely 40\" or more — is this the right limit for your work?",
            $"Applied: {options.MaxPierThickness:0}\".",
            "Set too low, a real pier is broken up or turned into a frame element and loses the in-plane shear it " +
            "was drawn to carry.",
            "Answer with one thickness in inches to change it.")
            {
                RuleTopic = "pier-stockier-than-a-wall",
                SettingKey = "dxf.max-pier-thickness",
                SettingUnits = "in",
                Decided = true
            },

        new ModelQuestion("O1", "Openings marked between perimeter elements",
            "OUR DECISION. Where apparent openings sit between perimeter " +
            "elements drawn on a COLUMN layer, those elements are modelled as columns and no opening is " +
            "generated between them.",
            "The drawing layer decides what a thing is, which is the rule this tool applies everywhere else — a " +
            "footprint on a column layer is a column. An opening is generated only where two in-line WALL ends " +
            "face each other, so a gap between two columns produces none, and no header spans it.",
            "A perimeter of columns carries no in-plane shear where a perimeter of pierced wall does, and the " +
            "difference runs the height of the tower.",
            "Measured on a typical tower floor: 24 perimeter footprints at 16x40, 18x45, 30x30 and 24x28, none " +
            "more slender than 2.5:1, all on a column layer, against 36 wall-layer segments for the core. " +
            "If they should be pierced wall instead, that is a modelling rule still missing from this sheet.")
            { RuleTopic = "perimeter-column-layer-openings", Decided = true },

        new ModelQuestion("M2", "Beams",
            "OUT OF SCOPE. Beams are not modelled at all.",
            "No beam is generated. Any beam in the delivered model is one you drew.",
            "A beam carries load the geometry otherwise hands to the walls and columns around it. If your framing " +
            "matters to the analysis, it has to come from you.",
            beamEvidence)
            { RuleTopic = "beams-are-not-modelled", Decided = true },

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

        new ModelQuestion("S3", "Slab thickness where the drawing is silent, and the smallest ring worth reading",
            $"Where a slab outline states no thickness, {compose.DefaultSlabThickness:0}\" is used. A closed ring on a " +
            $"slab layer smaller than {options.MinSlabArea / 144:0} sq ft is treated as drafting detail rather than any " +
            "part of a floor. Both are ours.",
            $"Applied: {compose.DefaultSlabThickness:0}\" default thickness, {options.MinSlabArea / 144:0} sq ft floor. " +
            "A drawn thickness always wins where the drawing gives one.",
            "The thickness drives plate mass and stiffness on every storey that does not state one. The area floor " +
            "decides whether a small ring is a shaft, a detail, or nothing at all.",
            "Answer with a thickness in inches and an area, for example: 10 in, 60 sq ft.")
            {
                RuleTopic = "slab-thickness-and-ring-floor",
                SettingKey = "dxf.default-slab-thickness;dxf.min-slab-area",
                SettingUnits = "in;sqin",
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

        new ModelQuestion("C5", "When a footprint is one pier rather than two walls",
            "OUR DECISION — two numbers, both overridable here. A closed outline is handed to the decomposer " +
            "first; it stays a single pier only where that yields fewer than two panels. Two ratios decide what " +
            "the decomposer will accept: how much of its bounding box a shape must fill to read as solid at " +
            $"all ({options.PierFillRatio:0.##}), and how slender a piece must be to be kept as a panel of its " +
            $"own ({options.MinPanelAspect:0.##}).",
            "Raising the fill ratio sends more shapes to the decomposer and produces more, thinner members. " +
            "Raising the panel aspect throws away more near-square limbs. Answer with two numbers to change " +
            "them, for example: fill 0.6, aspect 1.2.",
            "This is the boundary between a core modelled as its actual limbs and a core modelled as one block " +
            "on a centreline matching none of them.",
            "Measured on the same north-west corner regression outline: the leg is 42x36, an aspect of 1.17 — under the " +
            "1.2 floor by three " +
            "hundredths, and it survives only because a near-square piece is now held over and kept when it " +
            "runs into an accepted panel. A genuine leftover sliver touches nothing, because the faces that " +
            "would have joined it were consumed with their wall.")
            {
                RuleTopic = "solid-enough-to-be-one-pier",
                SettingKey = "dxf.pier-fill-ratio;dxf.min-panel-aspect",
                SettingUnits = "ratio;ratio",
                Decided = true
            },

        new ModelQuestion("W6", "When a rectangle is a footprint rather than a wall run",
            "OUR DECISION — override with one ratio. A plain four-point rectangle squarer than " +
            $"{options.MinWallAspect:0.##}:1 is treated as a footprint and sent to the column and pier branches " +
            "instead of being read as a length of wall.",
            "It is a separate rule from the slenderness limit in A1, which decides what a SHORT element is. " +
            "This one decides whether a rectangle is read as a run at all, before any length is measured.",
            "A footprint read as a wall run gets a centreline through its long axis and a length it does not " +
            "have; a wall run read as a footprint loses its in-plane stiffness.",
            "Answer with one ratio if changing it, for example: 2.0.")
            {
                RuleTopic = "rectangle-is-a-run-not-a-footprint",
                SettingKey = "dxf.min-wall-aspect",
                SettingUnits = "ratio",
                Decided = true
            },

        new ModelQuestion("R1", "What it leaves alone in your model",
            "YOUR RULE, and it stays on unless you say otherwise: a member you have already modelled is " +
            "recognised at that location and not added again.",
            "The output is your model with geometry added, not a replacement for it. Answer no only if you want " +
            "everything the drawings show regardless of what you have built.",
            "Doubling a member you drew is worse than omitting one: it is invisible in a count and wrong in " +
            "every analysis afterwards.",
            leftAlone)
            {
                RuleTopic = "never-duplicate-the-engineers-work",
                SettingKey = "dxf.skip-members-already-modelled",
                SettingUnits = "bool",
                Decided = true
            },

        new ModelQuestion("F3", "Whether it builds floors at all",
            "OUR DECISION — one cell turns floors off entirely. Floor plates are generated wherever a slab edge " +
            "closes, and F1 covers the storeys where none does.",
            "Off is for a caller who wants the vertical structure alone. It is not the same as F1: turning F1 " +
            "off drops only the storeys with no drawn slab edge, turning this off drops every plate.",
            "A model without diaphragms behaves differently under lateral load in a way no count will show you.",
            "Answer yes/no if changing this.")
            {
                RuleTopic = "floors-are-generated",
                SettingKey = "dxf.include-floors",
                SettingUnits = "bool",
                Decided = true
            },

        new ModelQuestion("L1", "What your wall layers are called",
            $"OUR DEFAULT, and the one most likely to be wrong on a job we have not seen. Linework counts as a " +
            $"wall where its layer name contains any of: {string.Join(", ", options.WallLayerPatterns)}. Matched " +
            "anywhere in the name and case-insensitively, so one short pattern covers a whole office convention.",
            "Answer with the pattern or patterns your drawings use, separated by semicolons. This is a rule like " +
            "any other: answer it once and every job afterwards is read your way.",
            "This decides what the tool considers a wall AT ALL. Get it wrong and there is no partial result — " +
            "the walls are not misplaced, they are absent, and every count agrees with itself because nothing " +
            "was ever read.",
            "Nothing in the drawings can settle this; it is a naming convention, not a measurement. The report " +
            "lists every layer it did not claim, with its segment count, which is where to look if the model " +
            "comes back emptier than the drawings.")
            {
                RuleTopic = "wall-layer-names",
                SettingKey = "dxf.wall-layer-patterns",
                SettingUnits = "layers",
                Confidence = "engineer-confirmed",
                Decided = true, ForTheRecord = true
            },

        new ModelQuestion("L2", "What your column layers are called",
            $"OUR DEFAULT. Linework counts as a column where its layer name contains any of: " +
            $"{string.Join(", ", options.ColumnLayerPatterns)}.",
            "Answer with your own patterns, semicolon separated. Columns are tested BEFORE walls, because a " +
            "layer can satisfy both patterns and the column name is usually the more specific — a layer called " +
            "V_COL-WALL is a column layer, and testing walls first would take it for a wall.",
            "A column layer read as a wall layer produces walls where the building has columns, and the two " +
            "carry load in completely different ways.",
            "Same as L1: a convention, not a measurement. The unclaimed-layer list in the report is the evidence.")
            {
                RuleTopic = "column-layer-names",
                SettingKey = "dxf.column-layer-patterns",
                SettingUnits = "layers",
                Confidence = "engineer-confirmed",
                Decided = true, ForTheRecord = true
            },

        new ModelQuestion("L3", "What your slab-edge layers are called",
            $"OUR DEFAULT, and the most KOR-specific of the three. A closed outline counts as a floor where its " +
            $"layer name contains any of: {string.Join(", ", options.SlabLayerPatterns)}.",
            "Answer with your own patterns, semicolon separated.",
            "SLABEDG is a Revit export convention rather than anything standard. A drawing set that names slab " +
            "edges differently comes back with no floor plates at all, which means no diaphragms, which means " +
            "every wall and column in the model reads as unsupported.",
            "Same as L1: a convention, not a measurement.")
            {
                RuleTopic = "slab-layer-names",
                SettingKey = "dxf.slab-layer-patterns",
                SettingUnits = "layers",
                Confidence = "engineer-confirmed",
                Decided = true, ForTheRecord = true
            },

        new ModelQuestion("M1", "Storey framework",
            "YOUR RULE: \"let's have a full model with both towers modelled, I will separate the towers later\".",
            storeyFramework,
            "It decides how results are reported and how the model compares with the old one.",
            "The tool can still split a tower out on request; nothing about that is lost.")
            { Decided = true },
        }
        .Concat(ThisJobsQuestions(report))
        .ToList();
    }

    /// <summary>
    /// What THIS run could not settle, as questions rather than as lines in a report.
    ///
    /// Everything above is a standing rule -- true of every job, answered once. None of it is a
    /// question about the building in front of her. So the workbook could open saying "nothing
    /// here is waiting on you" while the report beside it listed four storeys with no diaphragm
    /// that only an engineer can resolve. The questions this job raised were written down in a
    /// place nobody is asked to answer.
    ///
    /// These come from the run's own flags, so they cannot claim a problem the model does not
    /// have, and they disappear from the workbook when the job stops having it.
    /// </summary>
    /// <summary>
    /// Thinnest run of material this will call concrete and put in front of an engineer.
    ///
    /// Not a modelling threshold -- the classifier has its own, and a thin outline is already not
    /// modelled. This decides only whether the tool ASKS about it. Six inches is below anything
    /// either reference model builds in -- 31168's thinnest wall is 10 -- and well above the 2 to
    /// 4 inches that drafting scratch measures.
    /// </summary>
    private const double MinConcreteThickness = 6.0;

    private static IEnumerable<ModelQuestion> ThisJobsQuestions(DxfToEtabsReport? report)
    {
        if (report is null) yield break;

        string? Flag(string contains) => report.Summary.Flags
            .FirstOrDefault(f => f.Contains(contains, StringComparison.OrdinalIgnoreCase));

        if (Flag("carry walls or columns and no floor plate") is { } plateless)
        {
            string storeys = plateless[(plateless.IndexOf(':') + 1)..].Split('.')[0].Trim();
            yield return new ModelQuestion("J1", "Storeys with no floor plate",
                $"These storeys carry walls and columns but no slab, so they have no diaphragm: {storeys}. " +
                "Their slab edges would not close. Is the slab edge drawn closed on those sheets, or is " +
                "the floor shown some other way we should be reading?",
                "Nothing was invented in their place. The perimeter-wall fallback needs an enclosing wall " +
                "ring and these storeys have none, so they were left without a plate and named here.",
                "A storey with no diaphragm behaves differently under lateral load, and every wall and " +
                "column on it reads as unsupported.",
                "Closure tolerance was tested at 6, 12 and 18 inches on this job and the result did not " +
                "change, so this is not a tolerance that can be widened into a floor.")
                { RuleTopic = "storeys-with-no-drawn-floor" };
        }

        if (Flag("no wall or column beneath") is { } floating)
        {
            string storeys = floating[(floating.IndexOf(':') + 1)..].Split('.')[0].Trim();
            yield return new ModelQuestion("J2", "A floor with nothing under it",
                $"{storeys} carries a floor plate with no wall or column on its own storey. Does the " +
                "structure stop below that level, or is it drawn on a sheet we did not place there?",
                "The plate was kept rather than dropped, because a roof over structure that stops below " +
                "is a real building, and guessing otherwise would remove a floor you drew.",
                "A plate with nothing beneath it is either correct or a sheet that landed on the wrong storey.",
                "Taken from this run: the plan placed there draws no vertical structure at all.")
                { RuleTopic = "plate-with-nothing-beneath" };
        }

        // Plates a storey was given because its own drawing has none. She is not being asked to
        // solve anything -- the model has a floor there -- but a plate she cannot tell from a
        // measured one is worse than the hole it filled, so it is put in front of her, once.
        if (Flag("a floor plate from a neighbour") is { } inferred)
        {
            string storeys = inferred[(inferred.IndexOf(':') + 1)..].Split('.')[0].Trim();
            yield return new ModelQuestion("J4", "Floors copied from another storey",
                $"These storeys were given another storey's floor plate: {storeys}. " +
                "Their own drawings carry no closed slab edge to read one from. Are these plates the " +
                "right shape, or should their edges be somewhere else?",
                "Nothing was invented: each plate is another storey's own, chosen as the one closest in " +
                "SHAPE to what stands on this storey — not merely the nearest below it, which handed the " +
                "mid-rise the ground floor's site-wide slab. They are marked INFERRED in the report.",
                "Without them those storeys have no diaphragm at all and every member on them reads as " +
                "unsupported. With them, the edges are another storey's, not yours.",
                "Measured on this job: the slab edge on those sheets arrives as sixty-odd open chains, " +
                "and at every tolerance from 0.05 to 72 inches the largest region it encloses is 119 sq " +
                "ft. There is nothing there to close, so no tolerance produces that floor.")
                { RuleTopic = "floors-taken-from-below" };
        }

        // A floor that stops short of the structure standing on it. She has to answer this one --
        // a podium ending where a tower begins and a slab edge that failed to close produce the
        // same model, and only the person who knows the building can say which.
        // The outline that closes through itself. Its own question, and above J5, because it is a
        // defect rather than something only she can settle: whatever the podium's real shape,
        // ETABS will not mesh a self-touching area properly. It goes to her because splitting one
        // ring into two plates needs the drawing, not because there is any doubt it is wrong.
        if (Flag("closes through itself") is { } pinched)
        {
            string where = pinched[(pinched.IndexOf(':') + 1)..].Split(". A floor is a ring")[0].Trim();
            yield return new ModelQuestion("J6", "A floor outline that closes through itself",
                $"{where}. Is that floor two separate slabs that our reader joined into one ring, or " +
                "does the slab genuinely narrow to nothing there?",
                "Reported, not repaired. The outline is the one the drawing's slab-edge linework closed " +
                "into; splitting it into two plates needs to know which two, and that is on the drawing.",
                "A floor is a ring, and where the ring meets its own edge ETABS meshes it badly or refuses " +
                "it. Everything standing on that storey then has a diaphragm that may not behave like one.",
                "Say whether it is one slab or two. If two, the point they should separate at is enough.")
                { RuleTopic = "plate-outline-closes-through-itself" };
        }

        // SHE HAS ALREADY ANSWERED THIS ONE, FOUR TIMES IN ONE CALL.
        //
        // 25 August, at 5:50, 6:56, 7:23 and 7:38, and in writing before that: "why is level 2
        // empty ... it's actually missing, so there's a problem at level 2 ... it's missing the
        // slab". Then she diagnosed it herself: "it drew the inside slab, because we have
        // different thicknesses ... she's having a hard time with overlapping slabs. Because
        // there's a base slab that's 14 inch and inside we have a thicker one."
        //
        // So for a storey whose slab edge is KNOWN not to have closed, the two explanations this
        // question offers are not open: we know which it is, and putting it to her again is how a
        // workbook stops being read. It is stated as a defect instead, and only storeys with no
        // such flag are still a question.
        if (Flag("Floor does not reach the structure") is { } shortFloor)
        {
            string storeys = shortFloor[(shortFloor.IndexOf(':') + 1)..].Split(". Those")[0].Trim();

            bool edgeKnownOpen = report.Summary.Flags.Any(f =>
                f.Contains("would not close", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("did not close as vectors", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("crossed itself", StringComparison.OrdinalIgnoreCase));

            yield return new ModelQuestion("J5", "Floors that stop short of their structure",
                edgeKnownOpen
                    ? $"On these storeys the floor spans much less ground than the walls and columns " +
                      $"standing on it: {storeys}. THIS IS A KNOWN DEFECT, NOT A QUESTION — this " +
                      "model also reports a slab edge on these sheets that would not close, which is " +
                      "the cause you identified: a base slab with a thicker one inside it, where only " +
                      "the inner outline closes. Nothing is needed from you unless a storey listed " +
                      "here is genuinely a small plate over open space."
                    : $"On these storeys the floor spans much less ground than the walls and columns " +
                      $"standing on it: {storeys}. Is that the building — a mezzanine over part of a " +
                      "room, a podium ending where the tower starts — or did a slab edge fail to close?",
                "Nothing was added or removed. Each of these storeys has the plate its own drawing gave, " +
                "and the number is how much of the ground its own members cover that the plate reaches.",
                "If it is the building, nothing needs doing and the model is right. If a slab edge failed " +
                "to close, every member out beyond the plate is standing with no diaphragm, and the " +
                "analysis will distribute lateral load as though that part of the floor were not there.",
                "Say which storeys are correct as drawn. For any that are not, the slab layer and the " +
                "sheet they should have come from is enough to go back and read them again.")
                { RuleTopic = "floor-stops-short-of-members" };
        }

        // Only the ones that could be concrete.
        //
        // Every outline that will not resolve was asked about, and on 31168 that put eighteen rows
        // in front of an engineer of which every single one was linework: 2 to 4 inches of implied
        // material where her thinnest real wall is 10. Asking her to identify drafting scratch is
        // how a workbook stops being read. The flag now carries the implied thickness, so the ones
        // too thin to be concrete are answered here rather than by her.
        var unresolved = report.Summary.Flags
            .Where(f => f.Contains("could not be resolved into wall panels", StringComparison.OrdinalIgnoreCase))
            .Where(f =>
            {
                var t = Regex.Match(f, @"implied thickness\s+([\d.]+)\s*in", RegexOptions.IgnoreCase);
                if (!t.Success) return true;   // an older flag with no measurement still gets asked
                return !double.TryParse(t.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                           out double thickness)
                    || thickness >= MinConcreteThickness;
            })
            .ToList();

        if (unresolved.Count > 0)
        {
            // Sheet, layer and size for each, so they can be found. The first version of this row
            // repeated "could not be resolved into wall panels" once per outline, named no sheet
            // at all, and showed four of eighteen without saying the other fourteen existed.
            var located = unresolved
                .Select(f =>
                {
                    var m = Regex.Match(f,
                        @"^(?<sheet>.+?\.dxf):\s*(?<layer>[^:]+):\s*outline\s+(?<size>[\dx.]+)\s+with\s+(?<v>\d+)\s+vert",
                        RegexOptions.IgnoreCase);
                    return m.Success
                        ? $"{m.Groups["size"].Value}\" ({m.Groups["v"].Value} corners) on {m.Groups["layer"].Value}, " +
                          $"sheet {Regex.Replace(m.Groups["sheet"].Value, @"^-+Structural Plan - ", "")}"
                        : f.Trim();
                })
                .ToList();

            yield return new ModelQuestion("J3", "Wall outlines that would not resolve",
                $"{located.Count} outline(s) on the wall layers were read but could not be turned into wall " +
                "panels, so those walls are NOT in the model. Each one, with the size measured off your " +
                $"drawing and the sheet it is on:{Environment.NewLine}" +
                string.Join(Environment.NewLine, located.Select(x => "  • " + x)) + Environment.NewLine +
                "Are these walls? If so, is there anything unusual about how they are drawn?",
                "The outlines were read and measured; what failed was turning them into panels on their " +
                "centrelines. Nothing was guessed in their place, and nothing was moved.",
                "These are missing walls rather than misplaced ones, and no count in the model will show " +
                "them to you — the totals look healthy without them.",
                "Sizes are width by depth in inches, measured off the linework on the sheet named.")
                { RuleTopic = "outlines-that-would-not-resolve" };
        }
    }

    public static void Write(string path, DxfToEtabsReport report, PlanClassificationOptions options,
        ComposeOptions compose, string projectName)
    {
        // Two sheets. The questions, and the lookup for when something in the model looks wrong.
        // The per-drawing ledger lives in the report; it is 60 rows nobody reads in a spreadsheet.
        using var workbook = new XLWorkbook();
        WriteQuestions(workbook, report, options, compose, projectName);
        WriteRulesInForce(workbook, report, options, compose);
        WriteFlags(workbook, report);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        workbook.SaveAs(path);
    }

    /// <summary>
    /// Every rule the run applied, whatever asked for it.
    ///
    /// The questions sheet covers the judgement calls, and it should: a structural engineer has no
    /// use for being asked what join tolerance to weld dashed linework at. But "not worth asking
    /// about" is not the same as "not worth seeing". Seven of the rules this tool runs on are
    /// geometry-cleanup tolerances that no question touches, and with no page listing them an
    /// engineer wondering why an outline did not close has nothing to look at and no number to
    /// name. So the whole set is written out with its value, where it came from and why it holds.
    /// </summary>
    private static void WriteRulesInForce(
        XLWorkbook workbook, DxfToEtabsReport report, PlanClassificationOptions options, ComposeOptions compose)
    {
        if (report.RulesApplied.Count == 0) return;

        var asked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var q in StandingQuestions(options, compose, report))
            foreach (var key in (q.SettingKey ?? string.Empty)
                     .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                asked[key] = q.Code;

        var sheet = workbook.Worksheets.Add("Rules in force");
        sheet.Cell(1, 1).Value = "Every rule this model was built on";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 13;
        sheet.Cell(2, 1).Value =
            "Read-only. These are the values the run actually used, loaded from KorStandards rather than built " +
            "into the tool. Where a rule has a row on the Questions sheet, that row's reference is given — " +
            "answering there changes this. The rest are geometry-cleanup tolerances: nothing to decide, but " +
            "if something in the model looks wrong they are what to name.";
        sheet.Cell(2, 1).Style.Font.Italic = true;

        string[] headers = { "Rule", "Value", "Units", "Change it at", "Confidence", "Set by", "Why it holds" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = sheet.Cell(4, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(122, 34, 48);
            cell.Style.Font.FontColor = XLColor.White;
        }

        int row = 5;
        foreach (var rule in report.RulesApplied.Values.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase))
        {
            sheet.Cell(row, 1).Value = rule.Key;

            // A list rule has no number, and a spreadsheet cell will not take NaN. Its value is the
            // text itself, which is the thing an engineer needs to read anyway.
            if (rule.IsNumeric) sheet.Cell(row, 2).Value = rule.Value;
            else sheet.Cell(row, 2).Value = rule.Text;
            sheet.Cell(row, 3).Value = rule.Units;
            sheet.Cell(row, 4).Value = asked.TryGetValue(rule.Key, out string? code)
                ? $"question {code}"
                : "ask, and it becomes a question";
            sheet.Cell(row, 5).Value = rule.Confidence;
            sheet.Cell(row, 6).Value = rule.Authority;
            sheet.Cell(row, 7).Value = rule.Because;
            row++;
        }

        sheet.Column(1).Width = 34;
        sheet.Column(2).Width = 10;
        sheet.Column(3).Width = 8;
        sheet.Column(4).Width = 26;
        sheet.Column(5).Width = 20;
        sheet.Column(6).Width = 22;
        sheet.Column(7).Width = 84;
        sheet.Range(5, 4, row - 1, 7).Style.Alignment.WrapText = true;
        sheet.Range(5, 1, row - 1, 7).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        sheet.SheetView.FreezeRows(4);
    }

    /// <summary>
    /// Whether writing in this row's answer column changes anything. A row with no setting key
    /// records a decision about what the tool does at all; an answer to it is banked as a ruling
    /// but moves no geometry, so the sheet must not offer it as a dial.
    /// </summary>
    public static bool Changeable(ModelQuestion q) => !string.IsNullOrWhiteSpace(q.SettingKey);

    /// <summary>
    /// The paragraph under the title, built from what the sheet actually contains.
    ///
    /// Separated out and tested directly because the branch that does not render is the one that
    /// goes wrong: this text promised that any nonblank answer becomes a rule, which was false for
    /// seven rows, and the promise survived a correction because it sat in the branch no current
    /// job triggers.
    /// </summary>
    public static string Introduction(int open, int changeable)
    {
        string dials =
            $"{changeable} row(s) are tied to a rule and change the model when you write in YOUR ANSWER. " +
            "The rest record what the tool does or does not attempt at all — they are marked SCOPE, and an " +
            "answer to one is noted but changes nothing, so say it to us instead.";

        return open == 0
            ? "Every judgement this tool had to make is listed, and every one has been taken — nothing here " +
              "is waiting on you. Each row carries what was decided and the measurement behind it, so you " +
              "can disagree with a specific number rather than with the whole tool. " + dials
            : $"Every judgement this tool had to make is listed. DECIDED and SCOPE rows are ours, taken on " +
              $"the evidence beside them. NEEDS YOU marks the {open} nothing in the drawings could settle. " +
              dials;
    }

    private static void WriteQuestions(XLWorkbook workbook, DxfToEtabsReport report, PlanClassificationOptions options, ComposeOptions compose, string projectName)
    {
        var sheet = workbook.Worksheets.Add("Questions");
        // Ordered by what it asks of her, not alphabetically.
        //
        // Sorting by reference put A1 first because it starts with an A, and buried the rows she
        // could actually act on among rows there is nothing to do about. An engineer opening this
        // wants the same thing every time: what needs me, then what can I change, then what am I
        // merely being told.
        var all = StandingQuestions(options, compose, report)
            .OrderBy(q => q.Decided ? (Changeable(q) ? 1 : 2) : 0)
            .ThenBy(q => q.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // The front page is what an engineer has to read. Everything else is on "Rules in force",
        // which is the whole set, read-only, and always has been. Handing her all of it twice put
        // fixed bugs and this office's own layer names in the same list as the decisions that
        // actually shape her model.
        var questions = all.Where(q => !q.ForTheRecord).ToList();
        int open = questions.Count(q => !q.Decided);

        sheet.Cell(1, 1).Value = open == 0
            ? $"{projectName} — decisions"
            : $"{projectName} — decisions, and {open} thing(s) only you can settle";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 13;

        sheet.Cell(2, 1).Value = Introduction(open, questions.Count(Changeable));
        sheet.Cell(2, 1).Style.Font.Italic = true;

        string[] headers =
        {
            "Ref", "Status", "Question", "What the tool did", "YOUR ANSWER", "Evidence",
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
        foreach (var q in questions)
        {
            sheet.Cell(row, 1).Value = q.Code;

            // The status is the point of the page, and it has to answer the only question an
            // engineer asks of a row: can I change this here? Marking all three kinds DECIDED in
            // one colour said yes to seven rows where writing in the answer column does nothing —
            // the sheet invited an answer it could not act on, and only a sentence at the top,
            // which nobody reads twice, said otherwise.
            var status = sheet.Cell(row, 2);
            status.Value = !q.Decided ? "NEEDS YOU" : Changeable(q) ? "DECIDED" : "SCOPE";
            status.Style.Font.Bold = true;
            status.Style.Font.FontColor = !q.Decided
                ? XLColor.FromArgb(169, 58, 51)
                : Changeable(q) ? XLColor.FromArgb(44, 115, 85) : XLColor.FromArgb(110, 110, 110);

            sheet.Cell(row, 3).Value = q.Question;
            sheet.Cell(row, 4).Value = q.WhatWeDid;

            // Only a row an answer can act on gets the cream box. A SCOPE row gets a struck-through
            // grey cell, so it never reads as an empty field waiting to be filled in.
            var answer = sheet.Cell(row, 5);
            if (Changeable(q))
            {
                answer.Style.Fill.BackgroundColor = XLColor.FromArgb(253, 246, 231);
            }
            else
            {
                // Grey, and EMPTY. Writing a dash in here to mean "nothing to fill in" put a
                // nonblank string in the answer column, and the importer read it as the engineer
                // speaking: seven ruling rows banked per import that nobody had typed. A cell that
                // means "no answer" has to BE no answer.
                answer.Style.Fill.BackgroundColor = XLColor.FromArgb(238, 238, 238);
            }
            sheet.Cell(row, 6).Value = q.Evidence;
            sheet.Cell(row, 7).Value = q.RuleScope;
            sheet.Cell(row, 8).Value = string.IsNullOrWhiteSpace(q.RuleTopic) ? q.Topic : q.RuleTopic;
            sheet.Cell(row, 9).Value = q.SettingKey ?? string.Empty;
            sheet.Cell(row, 10).Value = q.SettingUnits ?? string.Empty;
            sheet.Cell(row, 11).Value = q.Confidence;
            row++;
        }

        // The rows kept off the front page are named, not hidden. An engineer who wants the fixed
        // bugs and the standing defaults should be able to find them without being told they exist.
        int kept = all.Count - questions.Count;
        if (kept > 0)
        {
            var note = sheet.Cell(row + 1, 1);
            note.Value = $"{kept} further row(s) — bugs already fixed, and the layer-name defaults — are on " +
                         "the ‘Rules in force’ sheet with the rest of the rule set: " +
                         string.Join(", ", all.Where(q => q.ForTheRecord).Select(q => q.Code));
            note.Style.Font.Italic = true;
            note.Style.Font.FontColor = XLColor.FromArgb(130, 130, 130);
        }

        sheet.Column(1).Width = 7;
        sheet.Column(2).Width = 11;
        sheet.Column(3).Width = 60;
        sheet.Column(4).Width = 52;
        sheet.Column(5).Width = 34;
        sheet.Column(6).Width = 58;
        sheet.Columns(7, 11).Hide();
        sheet.Range(5, 3, row - 1, 6).Style.Alignment.WrapText = true;
        sheet.Range(5, 1, row - 1, 11).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
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

