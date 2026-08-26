using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Dxf;

public sealed record DxfToEtabsRequest
{
    public required string DxfFolder { get; init; }

    /// <summary>
    /// An .e2k that ETABS itself exported from the target model — storeys, grids and materials.
    ///
    /// No longer required. Give <see cref="LevelsFile"/> instead and the model is built from a
    /// level list with no ETABS file on the input side at all; see <see cref="E2kShellBuilder"/>.
    /// Supply one or the other.
    /// </summary>
    public string ReferenceE2k { get; init; } = string.Empty;

    /// <summary>
    /// A level list — "name, elevation" a line — used in place of a reference model.
    ///
    /// This is what Revit knows and what the drawings cannot say, being flat. KOR.Drafter.Bridge
    /// already reads it: every plan view it exports carries the level it was cut at.
    /// </summary>
    public string? LevelsFile { get; init; }

    /// <summary>
    /// Optional structural stick-file PDF. When supplied, floor plate thickness is read per matched
    /// sheet title; when absent, generated plates keep <c>dxf.default-slab-thickness</c>.
    /// </summary>
    public string? StickFilePdf { get; init; }

    /// <summary>
    /// A second export of the same drawings that CARRIES ITS TEXT, from our own Revit bridge.
    ///
    /// The DXFs this tool is usually given contain no text at all, so the thickness tag printed
    /// inside a slab -- the thing that says a region is slab and not a hole -- never reaches it.
    /// Supply this and the tags are lifted onto the geometry by AnnotationOverlay, which solves
    /// the rigid transform between the two exports from their column clouds.
    ///
    /// Absent, everything behaves exactly as before.
    /// </summary>
    public string? AnnotatedDxfFolder { get; init; }

    /// <summary>The unit the level elevations are given in, as ETABS names it: "in", "ft", "mm", "m".</summary>
    public string LevelsUnit { get; init; } = "in";

    public required string OutputE2k { get; init; }

    /// <summary>Restrict to one building's sheets, e.g. "B" for a "BLDG B" tower.</summary>
    public string? BuildingTag { get; init; }

    /// <summary>
    /// Layer-name patterns for THIS job, overriding the rule in KorStandards for this run only.
    ///
    /// A threshold is the same on every job — a 48" wall is 48" everywhere — so a global rule is
    /// the right shape for one. A layer name is not: what drafting calls a column is a fact about
    /// one office and often one project inside it. 500 Foster draws columns on V-COL and KOR draws
    /// them on JBP_V_COL, so setting the global rule for either breaks the other.
    ///
    /// Moving these patterns into the database made them visible and overridable-once. This makes
    /// an unfamiliar job runnable TODAY, without a migration and without disturbing anyone else's
    /// jobs. Answering the workbook question is still how a convention becomes permanent.
    /// </summary>
    public IReadOnlyList<string>? WallLayerPatterns { get; init; }

    public IReadOnlyList<string>? ColumnLayerPatterns { get; init; }

    public IReadOnlyList<string>? SlabLayerPatterns { get; init; }

    /// <summary>
    /// CAD tolerances given for THIS run, overriding the standing rule — the geometry-cleanup
    /// numbers an engineer never sets but somebody investigating a drawing needs to move.
    ///
    /// They existed as CLI flags and did nothing: ApplyRules takes the database value over the
    /// caller's, so --bridge 14 parsed, was accepted, and ran at 6 with nothing said. A conclusion
    /// recorded against that flag — "widening the closure tolerance was tried, every plate it
    /// added was a fragment" — was reached without the tolerance ever changing.
    /// </summary>
    public double? BridgeTolerance { get; init; }

    public double? JoinTolerance { get; init; }

    public double? ExtendLimit { get; init; }

    public double? DashJoinGap { get; init; }

    /// <summary>
    /// Cut the model down to one tower: its storeys and the shared podium ones, with the other
    /// towers' storeys removed so none of them stands empty.
    /// </summary>
    public string? TowerOnly { get; init; }

    /// <summary>
    /// Keep this storey and everything below it; drop everything above.
    ///
    /// The other way to say "the podium and the mid-rise, not the towers". <see cref="TowerOnly"/>
    /// cuts by name and cannot express it: on a site model the tower floors below the split carry
    /// no tower prefix, and the towers' ground floors carry one while sitting at grade inside the
    /// podium that is wanted.
    /// </summary>
    public string? TopStorey { get; init; }

    /// <summary>
    /// Storeys to leave out by name, whatever their elevation.
    ///
    /// <see cref="TopStorey"/> was meant to be the answer to "not the towers" and is not: 31168's
    /// tower levels 3 to 10 carry no prefix and stand below the mid-rise's own roof, so cutting at
    /// C-ROOF kept all eight of them and an engineer opened a model of a building she had said was
    /// out of scope. Nothing about their names or their heights marks them; only their position on
    /// plan does, and until that is the rule they are named here.
    /// </summary>
    public IReadOnlyList<string> DropStoreys { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Translation applied to drawing coordinates. Defaults to none: the CAD export and the
    /// model both come out of the same Revit project, so the drawings already sit in the
    /// model's coordinate system — on 31168 the core walls land on grid lines 15 and 16 to
    /// within 0.05". Set <see cref="CentreOnGrid"/> only for a drawing that does not share it.
    /// </summary>
    public (double X, double Y)? Offset { get; init; }

    /// <summary>Fall back to centring the drawings on the model's grid extents.</summary>
    public bool CentreOnGrid { get; init; }

    /// <summary>
    /// Add parkade storeys the drawings have and the model does not. On by the engineer's
    /// instruction — "the model needs to go to P5" — where her model stopped at P3.
    /// </summary>
    public bool AddMissingParkadeStoreys { get; init; } = true;

    /// <summary>
    /// Read what can be read off the reference model — the opening a header spans, how slender a
    /// column may be — rather than using a number measured once on another job. A derived value is
    /// taken only where the model supports it and the answer is physically credible.
    /// </summary>
    public bool DeriveRulesFromReference { get; init; } = true;

    public PlanClassificationOptions Classification { get; init; } = new();
    public ComposeOptions Compose { get; init; } = new();

    /// <summary>
    /// Where to read the banked rules from. Null takes it from the environment.
    /// </summary>
    public string? RuleSettingsConnection { get; init; }

    /// <summary>
    /// Production generation must be driven by KorStandards. Tests and local probes can leave this
    /// false to use the built-in defaults and have that fact stated in the report.
    /// </summary>
    public bool RequireRuleSettings { get; init; }
}

public sealed record SheetOutcome(
    string File, string Label, string? Building, IReadOnlyList<int> Levels,
    IReadOnlyList<string> Stories, int Walls, int Columns, int Slabs, IReadOnlyList<string> Flags)
{
    public int? DrawingSlabThicknessInches { get; init; }
    public int? DrawingSlabThicknessPage { get; init; }
    public string? DrawingSlabThicknessTitle { get; init; }
}

public sealed record DxfToEtabsReport(
    string OutputPath,
    int SheetsRead,
    int SheetsPlaced,
    int StoriesPopulated,
    ComposeSummary Summary,
    (double X, double Y) AppliedOffset,
    IReadOnlyList<SheetOutcome> Sheets,
    IReadOnlyList<string> Warnings,
    PlanClassificationOptions ClassificationUsed,
    ComposeOptions ComposeUsed)
{
    /// <summary>
    /// Every rule this run read from KorStandards, with its value, its authority and the reason it
    /// holds. Carried on the report so the deliverable can show an engineer the whole rule set
    /// rather than only the rules a question happens to ask about — a rule she cannot see is one
    /// she cannot disagree with. Empty on a run that was not given the rules database.
    /// </summary>
    public IReadOnlyDictionary<string, RuleSetting> RulesApplied { get; init; }
        = new Dictionary<string, RuleSetting>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Storeys placed from FOUNDATION sheets, where a slab-on-grade is not a diaphragm.</summary>
    public IReadOnlyList<string> FoundationStoreys { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Turns a folder of structural plan DXFs into an ETABS model: read, classify,
/// map each sheet onto the storeys it serves, and merge the result into a model
/// ETABS exported. One import in ETABS then carries the whole building.
/// </summary>
public static class DxfToEtabsService
{
    public static IReadOnlyList<string> RequiredRuleKeys { get; } =
    [
        "dxf.wall-layer-patterns",
        "dxf.column-layer-patterns",
        "dxf.slab-layer-patterns",
        "dxf.min-wall-thickness",
        "dxf.max-wall-thickness",
        "dxf.min-wall-length",
        "dxf.min-panel-overlap",
        "dxf.connect-walls",
        "dxf.floor-from-perimeter-wall",
        "dxf.min-opening-span",
        "dxf.max-opening-span",
        "dxf.min-wall-aspect",
        "dxf.min-panel-aspect",
        "dxf.max-column-aspect",
        "dxf.min-column-size",
        "dxf.max-column-size",
        "dxf.pier-fill-ratio",
        "dxf.unusual-wall-thickness",
        "dxf.max-pier-thickness",
        "dxf.min-slab-area",
        "dxf.min-plate-area",
        "dxf.dash-join-gap",
        "dxf.extend-limit",
        "dxf.join-tolerance",
        "dxf.bridge-tolerance",
        "dxf.wall-bridge-tolerance",
        "dxf.default-slab-thickness",
        "dxf.opening-height",
        "dxf.already-modelled-tolerance",
        "dxf.assign-pier-labels",
        "dxf.assign-diaphragms",
        "dxf.include-floors",
        "dxf.skip-members-already-modelled",
        "dxf.spandrel-depth-floor",
        "dxf.spandrel-depth-ceiling",
    ];

    /// <summary>
    /// The rules whose value is a list of names rather than a number.
    ///
    /// These are the ones that decide what this tool considers structure at all, and until they
    /// moved here they were three string constants in C#. A firm that names its slab edges
    /// anything else got a model with no floor plates and no way to correct it without a code
    /// change, which is the opposite of what "agnostic" is supposed to mean.
    /// </summary>
    public static IReadOnlyList<string> TextRuleKeys { get; } =
    [
        "dxf.wall-layer-patterns",
        "dxf.column-layer-patterns",
        "dxf.slab-layer-patterns",
    ];

    private static IReadOnlyDictionary<string, double> BuiltInRuleValues(
        PlanClassificationOptions classification,
        ComposeOptions compose)
    {
        var values = new Dictionary<string, double>
        {
            ["dxf.min-wall-thickness"] = classification.MinWallThickness,
            ["dxf.max-wall-thickness"] = classification.MaxWallThickness,
            ["dxf.min-wall-length"] = classification.MinWallLength,
            ["dxf.min-panel-overlap"] = classification.MinPanelOverlap,
            ["dxf.connect-walls"] = classification.ConnectWalls ? 1 : 0,
            ["dxf.floor-from-perimeter-wall"] = classification.FloorFromPerimeterWall ? 1 : 0,
            ["dxf.min-opening-span"] = classification.MinOpeningSpan,
            ["dxf.max-opening-span"] = classification.MaxOpeningSpan,
            ["dxf.min-wall-aspect"] = classification.MinWallAspect,
            ["dxf.min-panel-aspect"] = classification.MinPanelAspect,
            ["dxf.max-column-aspect"] = classification.MaxColumnAspect,
            ["dxf.min-column-size"] = classification.MinColumnSize,
            ["dxf.max-column-size"] = classification.MaxColumnSize,
            ["dxf.pier-fill-ratio"] = classification.PierFillRatio,
            ["dxf.unusual-wall-thickness"] = classification.UnusualWallThickness,
            ["dxf.max-pier-thickness"] = classification.MaxPierThickness,
            ["dxf.min-slab-area"] = classification.MinSlabArea,
            ["dxf.min-plate-area"] = classification.MinPlateArea,
            ["dxf.dash-join-gap"] = classification.DashJoinGap,
            ["dxf.extend-limit"] = classification.ExtendLimit,
            ["dxf.join-tolerance"] = classification.JoinTolerance,
            ["dxf.bridge-tolerance"] = classification.BridgeTolerance,
            ["dxf.wall-bridge-tolerance"] = classification.WallBridgeTolerance,
            ["dxf.default-slab-thickness"] = compose.DefaultSlabThickness,
            ["dxf.opening-height"] = compose.OpeningHeight,
            ["dxf.already-modelled-tolerance"] = compose.AlreadyModelledTolerance,
            ["dxf.assign-pier-labels"] = compose.AssignPierLabels ? 1 : 0,
            ["dxf.assign-diaphragms"] = compose.AssignDiaphragms ? 1 : 0,
            ["dxf.include-floors"] = compose.IncludeFloors ? 1 : 0,
            ["dxf.skip-members-already-modelled"] = compose.SkipMembersAlreadyModelled ? 1 : 0,
            ["dxf.spandrel-depth-floor"] = compose.SpandrelDepthFloor,
            ["dxf.spandrel-depth-ceiling"] = compose.SpandrelDepthCeiling,
        };

        var applied = values.Keys.Concat(TextRuleKeys);
        if (!RequiredRuleKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(applied.OrderBy(k => k, StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The DXF-to-ETABS required rule list and applied rule list differ.");

        return values;
    }

    private static PlanClassificationOptions ApplyRules(
        PlanClassificationOptions options,
        IReadOnlyDictionary<string, RuleSetting> settings)
        => options with
        {
            WallLayerPatterns = settings.ListOr("dxf.wall-layer-patterns", options.WallLayerPatterns),
            ColumnLayerPatterns = settings.ListOr("dxf.column-layer-patterns", options.ColumnLayerPatterns),
            SlabLayerPatterns = settings.ListOr("dxf.slab-layer-patterns", options.SlabLayerPatterns),
            MinWallThickness = settings.ValueOr("dxf.min-wall-thickness", options.MinWallThickness),
            MaxWallThickness = settings.ValueOr("dxf.max-wall-thickness", options.MaxWallThickness),
            MinWallLength = settings.ValueOr("dxf.min-wall-length", options.MinWallLength),
            MinPanelOverlap = settings.ValueOr("dxf.min-panel-overlap", options.MinPanelOverlap),
            ConnectWalls = settings.FlagOr("dxf.connect-walls", options.ConnectWalls),
            FloorFromPerimeterWall = settings.FlagOr("dxf.floor-from-perimeter-wall", options.FloorFromPerimeterWall),
            MinOpeningSpan = settings.ValueOr("dxf.min-opening-span", options.MinOpeningSpan),
            MaxOpeningSpan = settings.ValueOr("dxf.max-opening-span", options.MaxOpeningSpan),
            MinWallAspect = settings.ValueOr("dxf.min-wall-aspect", options.MinWallAspect),
            MinPanelAspect = settings.ValueOr("dxf.min-panel-aspect", options.MinPanelAspect),
            MaxColumnAspect = settings.ValueOr("dxf.max-column-aspect", options.MaxColumnAspect),
            MinColumnSize = settings.ValueOr("dxf.min-column-size", options.MinColumnSize),
            MaxColumnSize = settings.ValueOr("dxf.max-column-size", options.MaxColumnSize),
            PierFillRatio = settings.ValueOr("dxf.pier-fill-ratio", options.PierFillRatio),
            UnusualWallThickness = settings.ValueOr("dxf.unusual-wall-thickness", options.UnusualWallThickness),
            MaxPierThickness = settings.ValueOr("dxf.max-pier-thickness", options.MaxPierThickness),
            MinSlabArea = settings.ValueOr("dxf.min-slab-area", options.MinSlabArea),
            MinPlateArea = settings.ValueOr("dxf.min-plate-area", options.MinPlateArea),
            DashJoinGap = settings.ValueOr("dxf.dash-join-gap", options.DashJoinGap),
            ExtendLimit = settings.ValueOr("dxf.extend-limit", options.ExtendLimit),
            JoinTolerance = settings.ValueOr("dxf.join-tolerance", options.JoinTolerance),
            BridgeTolerance = settings.ValueOr("dxf.bridge-tolerance", options.BridgeTolerance),
            WallBridgeTolerance = settings.ValueOr("dxf.wall-bridge-tolerance", options.WallBridgeTolerance),
            OutlineSelfTouchTolerance = settings.ValueOr("dxf.outline-self-touch-tolerance", options.OutlineSelfTouchTolerance),
            FloodFillBridge = settings.ValueOr("dxf.flood-fill-bridge", options.FloodFillBridge),
        };

    private static ComposeOptions ApplyRules(
        ComposeOptions options,
        IReadOnlyDictionary<string, RuleSetting> settings)
        => options with
        {
            DefaultSlabThickness = settings.ValueOr("dxf.default-slab-thickness", options.DefaultSlabThickness),
            OpeningHeight = settings.ValueOr("dxf.opening-height", options.OpeningHeight),
            AlreadyModelledTolerance = settings.ValueOr("dxf.already-modelled-tolerance", options.AlreadyModelledTolerance),
            AssignPierLabels = settings.FlagOr("dxf.assign-pier-labels", options.AssignPierLabels),
            AssignDiaphragms = settings.FlagOr("dxf.assign-diaphragms", options.AssignDiaphragms),
            IncludeFloors = settings.FlagOr("dxf.include-floors", options.IncludeFloors),
            SkipMembersAlreadyModelled = settings.FlagOr("dxf.skip-members-already-modelled", options.SkipMembersAlreadyModelled),
            SpandrelDepthFloor = settings.ValueOr("dxf.spandrel-depth-floor", options.SpandrelDepthFloor),
            SpandrelDepthCeiling = settings.ValueOr("dxf.spandrel-depth-ceiling", options.SpandrelDepthCeiling),
            JointMergeTolerance = settings.ValueOr("dxf.joint-merge-tolerance", options.JointMergeTolerance),
            DonorPlateLikenessMargin = settings.ValueOr("dxf.donor-plate-likeness-margin", options.DonorPlateLikenessMargin),
            MinFloorCoverage = settings.ValueOr("dxf.min-floor-coverage", options.MinFloorCoverage),
            SelfTouchReportGap = settings.ValueOr("dxf.self-touch-report-gap", options.SelfTouchReportGap),
        };

    private static string Describe(double unitInInches) => unitInInches switch
    {
        1.0 => "inches",
        12.0 => "feet",
        _ when Math.Abs(unitInInches - 1.0 / 25.4) < 1e-9 => "millimetres",
        _ when Math.Abs(unitInInches - 1.0 / 2.54) < 1e-9 => "centimetres",
        _ when Math.Abs(unitInInches - 1000.0 / 25.4) < 1e-9 => "metres",
        _ => $"{unitInInches:0.####} inches per unit",
    };

    public static DxfToEtabsReport Run(DxfToEtabsRequest request)
    {
        // Either a model ETABS exported, or a list of levels. The second is the ordinary case now:
        // a job that has never been modelled has no .e2k to give, which is every job but two.
        if (string.IsNullOrWhiteSpace(request.ReferenceE2k) && string.IsNullOrWhiteSpace(request.LevelsFile))
            throw new InvalidOperationException(
                "Give either a reference .e2k or a level list. Without one there is no way to know " +
                "what the storeys are called or how high they are, and a plan drawing cannot say — " +
                "it is flat.");

        var doc = string.IsNullOrWhiteSpace(request.LevelsFile)
            ? E2kDocument.Load(request.ReferenceE2k)
            : E2kShellBuilder.FromLevels(
                E2kShellBuilder.ParseLevels(File.ReadAllLines(request.LevelsFile)), request.LevelsUnit);

        // Before anything else: the storey list is what ETABS builds from, and an export parks the
        // base a thousand feet under the building with the whole distance folded into the lowest
        // storey. Left alone, every member down there is extruded that far on import.
        bool baseNormalised = doc.NormaliseBaseStorey();

        // The storey list as the engineer's own model has it, kept because sheet matching needs it
        // whatever the cuts do to the model afterwards. See the note where matchNames is built.
        var storiesBeforeCuts = doc.ReadStories().Select(s => s.Name).ToList();

        var files = Directory.EnumerateFiles(request.DxfFolder, "*.dxf", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var sheetInfoByFile = files.ToDictionary(f => f, PlanSheetNaming.Parse, StringComparer.OrdinalIgnoreCase);

        // A model of one tower carries only that tower's storeys. On a site model the others stand
        // empty, which is what the engineer saw: "some levels don't exist, they're blank."
        var droppedStoreys = request.TowerOnly is null
            ? Array.Empty<string>()
            : doc.KeepOnlyTower(request.TowerOnly).ToArray();

        // After the tower cut, not instead of it: the two answer different questions and an
        // engineer may want both ("building C, and nothing above its roof").
        var droppedAbove = request.TopStorey is null
            ? Array.Empty<string>()
            : doc.KeepStoreysUpTo(request.TopStorey).ToArray();

        // Last, and after both, because it exists to catch what neither of them can see.
        var droppedByName = request.DropStoreys.Count == 0
            ? Array.Empty<string>()
            : doc.DropStoreys(request.DropStoreys).ToArray();

        // Drafting can issue parkade levels the model has never had. On 31138 the drawings go to
        // LEVEL P5 and the model stopped at P3, so two whole floors were read and placed nowhere —
        // "the model needs to go to P5". The storeys are added below the lowest parkade level at the
        // height that parkade already uses, and the base drops by the same amount.
        var addedStoreys = Array.Empty<string>();
        if (request.AddMissingParkadeStoreys)
        {
            var wanted = sheetInfoByFile.Values
                .SelectMany(s => s.ParkadeLevels)
                .Distinct()
                .ToList();
            addedStoreys = doc.AddParkadeStoreysBelow(wanted).ToArray();
        }

        var stories = doc.ReadStories();
        if (stories.Count == 0)
            throw new InvalidOperationException("The reference model lists no storeys.");

        var storyNames = stories.Select(s => s.Name).ToList();
        var byName = stories.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);


        var warnings = new List<string>();

        // UNITS. Every rule here is a real length — a 48" wall, a 12" face, a 400 sq ft plate — and
        // every coordinate written has to be in the model's own unit. Both are inches on the two
        // jobs built so far, which is precisely why neither was ever read. A drawing in millimetres
        // would not fail; it would produce a building of the wrong size and say nothing.
        double modelUnitInInches = doc.LengthUnitInInches()
            ?? throw new InvalidOperationException(
                "The reference model does not state a length unit this tool understands, so geometry " +
                "cannot be written in its units. Expected CONTROLS UNITS with IN, FT, MM, CM or M.");

        double? drawingUnit = files.Count > 0 ? DxfPlanReader.UnitInInches(files[0]) : modelUnitInInches;
        if (drawingUnit is null)
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(files[0])} does not declare $INSUNITS, so there is no way to know " +
                "whether it is drawn in inches, feet or millimetres. Every size rule and every " +
                "coordinate depends on that. Set the units in the export, or pass them explicitly.");
        }

        // Mixed units across one set means the sheets disagree about the size of the building.
        foreach (string file in files)
        {
            double? each = DxfPlanReader.UnitInInches(file);
            if (each is null || Math.Abs(each.Value - drawingUnit.Value) > 1e-9)
                throw new InvalidOperationException(
                    $"{Path.GetFileName(file)} is drawn in different units from the rest of the set. " +
                    "One drawing set has to share one unit.");
        }

        // The rules, from KorStandards where the run is production. An answer from the engineer is
        // banked there with its confidence, and this is where it becomes behaviour.
        var builtIn = BuiltInRuleValues(request.Classification, request.Compose);
        var banked = request.RequireRuleSettings
            ? RuleSettings.LoadRequired(request.RuleSettingsConnection, RequiredRuleKeys)
            : new Dictionary<string, RuleSetting>(StringComparer.OrdinalIgnoreCase);
        warnings.AddRange(RuleSettings.Describe(banked, builtIn));

        var requested = ApplyRules(request.Classification, banked);

        // After the rules, never before: ApplyRules takes the database value over whatever the
        // caller set, which is right for a threshold and wrong for a name given for this job.
        if (request.WallLayerPatterns is { Count: > 0 })
            requested = requested with { WallLayerPatterns = request.WallLayerPatterns };
        if (request.ColumnLayerPatterns is { Count: > 0 })
            requested = requested with { ColumnLayerPatterns = request.ColumnLayerPatterns };
        if (request.SlabLayerPatterns is { Count: > 0 })
            requested = requested with { SlabLayerPatterns = request.SlabLayerPatterns };

        // Same reason, same place: after the rules, or the database silently wins.
        if (request.BridgeTolerance is { } bridge) requested = requested with { BridgeTolerance = bridge };
        if (request.JoinTolerance is { } join) requested = requested with { JoinTolerance = join };
        if (request.ExtendLimit is { } extend) requested = requested with { ExtendLimit = extend };
        if (request.DashJoinGap is { } dash) requested = requested with { DashJoinGap = dash };

        foreach (var (what, given, standing) in new (string, double?, double)[]
                 {
                     ("bridge tolerance", request.BridgeTolerance, banked.ValueOr("dxf.bridge-tolerance", 0)),
                     ("join tolerance", request.JoinTolerance, banked.ValueOr("dxf.join-tolerance", 0)),
                     ("extend limit", request.ExtendLimit, banked.ValueOr("dxf.extend-limit", 0)),
                     ("dash-join gap", request.DashJoinGap, banked.ValueOr("dxf.dash-join-gap", 0)),
                 })
        {
            if (given is { } v)
                warnings.Add($"{what} for this run was given as {v:0.###}, overriding the standing rule " +
                             $"of {standing:0.###}. This model was not built to the banked tolerances.");
        }

        foreach (var (role, given) in new[]
                 {
                     ("wall", request.WallLayerPatterns),
                     ("column", request.ColumnLayerPatterns),
                     ("slab-edge", request.SlabLayerPatterns),
                 })
        {
            if (given is { Count: > 0 })
                warnings.Add($"{role} layers for this run were given as {string.Join(", ", given)}, " +
                             "overriding the standing rule. Answer the layer question in the workbook to " +
                             "make it the rule instead of a flag.");
        }

        double scale = drawingUnit.Value / modelUnitInInches;
        var composeFromReference = ApplyRules(request.Compose, banked);
        var classification = Math.Abs(modelUnitInInches - 1.0) < 1e-9
            ? requested
            : requested.InUnitOf(modelUnitInInches);

        if (Math.Abs(scale - 1.0) > 1e-9)
            warnings.Add($"The drawings are {Describe(drawingUnit.Value)} and the model is " +
                         $"{Describe(modelUnitInInches)}, so every coordinate was scaled by {scale:0.######}.");
        // Everything a deliberate cut took out, by name and by the level number in the name, so a
        // sheet for a removed storey can be recognised and not reported as a failure.
        var cutStoreys = new HashSet<string>(
            droppedStoreys.Concat(droppedAbove).Concat(droppedByName), StringComparer.OrdinalIgnoreCase);
        var cutLevelNumbers = new HashSet<int>(cutStoreys
            .Select(n => Regex.Match(n, @"(\d+)"))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)));
        int sheetsCutAway = 0;

        // Sheets are matched against the storey list the model had BEFORE any cut, and the cut
        // storeys are struck from the answer afterwards. Matching against what is left instead
        // lets a drawing migrate to another building: an untagged "LEVEL 8" sheet prefers the
        // unprefixed "LEVEL 8" storey and falls through to "C-LEVEL 8" once that is the only
        // level 8 still standing. Removing the towers' eight storeys therefore moved the towers'
        // drawings INTO the mid-rise — C-LEVEL 8 went from 10 wall panels to 34 — which is a worse
        // fault than the one being fixed, and it looks like a fuller model rather than a wrong one.
        // A sheet whose own storey has been cut belongs nowhere, and says so.
        var matchNames = cutStoreys.Count == 0 ? storyNames : storiesBeforeCuts;

        var outcomes = new List<SheetOutcome>();
        var parsed = new List<(PlanSheetInfo Sheet, PlanGeometrySet Geometry, IReadOnlyList<string> Stories)>();
        var readSheets = new List<IReadOnlyList<DxfSegment>>();

        // What was read from the annotated export, and from which sheet. The engineer must be
        // able to see which drawing's words landed on which drawing's geometry.
        var annotationNotes = new List<string>();
        var slabThicknessBySheet = StickFileSlabThicknessReader.ReadBySheet(
            sheetInfoByFile.Values.ToList(),
            request.StickFilePdf);

        foreach (string file in files)
        {
            var sheet = sheetInfoByFile[file];

            if (request.BuildingTag is not null &&
                sheet.BuildingTags.Count > 0 &&
                !sheet.BuildingTags.Contains(request.BuildingTag, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var segments = DxfPlanReader.ReadSegments(file);
            var tags = DxfPlanReader.ReadPositionedTags(file);

            // THE WORDS COME FROM THE OTHER EXPORT, IF THERE IS ONE.
            //
            // Matched by storey rather than by file name: the two exports name their sheets
            // differently -- "LEVEL 2 PLAN - CONCRETE OUTLINE" against plain "LEVEL 2" -- and
            // PlanSheetNaming already reads both into the levels they serve.
            if (!string.IsNullOrWhiteSpace(request.AnnotatedDxfFolder) && tags.Count == 0)
            {
                var carried = AnnotationOverlay.TagsFor(
                    request.AnnotatedDxfFolder!, sheet, segments, classification, out string? note);
                if (carried.Count > 0) tags = carried;
                if (note is not null) annotationNotes.Add(note);
            }
            // Held, not raised yet. This warning is only true of a sheet that is IN the model: a
            // sheet whose storeys were cut away, or that placed nowhere, already has its own line
            // saying so, and telling an engineer about 96 unread ellipses on a tower she asked us
            // to leave out reads as though nobody understood the request.
            var unsupported = DxfPlanReader.UnsupportedStructuralEntities(file, classification);
            string? unreadWarning = null;
            // HATCH is fill, and fill is not structure.
            //
            // It was reported as shape the model might be missing, which put a line on nearly every
            // sheet of every job: 88 on one of 31168's Level 2 sheets, 33 on Autodesk's sample, 13
            // of those on a layer this tool reads. Two conventions, the same noise. Ruled out
            // 2026-08-21 -- "Almost positive hatching is always fill" -- so it stops being counted
            // as geometry gone missing. It is still READ as nothing, which was always true; what
            // changes is that the report no longer suggests a hole where there is none.
            //
            // Everything else that carries shape and cannot be read -- ELLIPSE, SPLINE, a solid --
            // still counts, because none of those has been ruled on.
            unsupported = unsupported
                .Where(e => !e.EntityType.Equals("HATCH", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (unsupported.Count > 0)
            {
                int total = unsupported.Sum(e => e.Count);
                string examples = string.Join(", ", unsupported.Take(4)
                    .Select(e => $"{e.Count:N0} {e.EntityType} on {e.Layer}"));
                // "on structural layers" was the old wording and it was the old gate. It is not
                // true any more and it was the misleading half: the sheets worth warning about are
                // the ones whose layers this tool does not recognise, and those have no structural
                // layer for the sentence to refer to.
                bool anyClaimed = unsupported.Any(e => classification.RoleOf(e.Layer) is not null);
                unreadWarning = $"{sheet.FileName}: {total:N0} unreadable DXF entit{(total == 1 ? "y" : "ies")} " +
                                $"carrying shape, not read: {examples}. " +
                                (anyClaimed
                                    ? "Some sit on layers this tool reads, so that geometry is missing from the model."
                                    : "None sits on a layer this tool reads, so if any of it is structure, the model does not have it.");
            }

            if (Math.Abs(scale - 1.0) > 1e-9)
            {
                segments = segments.Select(g => new DxfSegment(g.Layer,
                    new DxfPoint(g.Start.X * scale, g.Start.Y * scale),
                    new DxfPoint(g.End.X * scale, g.End.Y * scale)) { FromCurve = g.FromCurve }).ToList();
                tags = tags.Select(t => t with
                {
                    Point = new DxfPoint(t.Point.X * scale, t.Point.Y * scale)
                }).ToList();
            }

            readSheets.Add(segments);
            var geometry = StructuralPlanClassifier.Classify(segments, classification, sheet, tags);
            var matched = PlanSheetNaming.MatchStories(sheet, matchNames)
                .Select(s => doc.StoreyRenames.TryGetValue(s, out string? now) ? now : s)
                .Where(s => !cutStoreys.Contains(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            slabThicknessBySheet.TryGetValue(sheet.FileName, out var slabThickness);

            outcomes.Add(new SheetOutcome(
                sheet.FileName, sheet.Label, sheet.BuildingTag, sheet.Levels, matched,
                geometry.Walls.Count, geometry.Columns.Count, geometry.Slabs.Count, geometry.Flags)
            {
                DrawingSlabThicknessInches = slabThickness?.ThicknessInches,
                DrawingSlabThicknessPage = slabThickness?.PageNumber,
                DrawingSlabThicknessTitle = slabThickness?.MatchedTitle,
            });

            if (matched.Count == 0)
            {
                // A sheet that matches nothing because WE removed its storeys is not a fault, and
                // listing forty of them individually reads as forty faults. They are counted and
                // reported once, after the loop.
                if (cutStoreys.Count > 0 && sheet.Levels.Concat(sheet.ParkadeLevels.Select(p => -p))
                        .Any(l => cutLevelNumbers.Contains(l)))
                {
                    sheetsCutAway++;
                    continue;
                }

                warnings.Add(!sheet.HasPlacement
                    ? $"{sheet.FileName}: no level number in the sheet name — not placed."
                    : $"{sheet.FileName}: levels {string.Join(",", sheet.Levels.Select(l => l.ToString()).Concat(sheet.ParkadeLevels.Select(p => "P" + p)))} match no storey in the model — not placed.");
                continue;
            }

            if (geometry.Walls.Count == 0 && geometry.Columns.Count == 0 && geometry.Slabs.Count == 0)
            {
                warnings.Add($"{sheet.FileName}: no structural outlines found on the expected layers — not placed.");
                continue;
            }

            // The sheet is in the model, so what it could not read is now worth saying.
            if (unreadWarning is not null) warnings.Add(unreadWarning);

            parsed.Add((sheet, geometry, matched));
        }

        // LAYERS. What a piece of linework IS comes from the layer it sits on, and the patterns are
        // KOR's own drafting convention. A drafter who names columns anything else gets no error —
        // the columns are simply never seen, and every count agrees with itself because nothing was
        // read. So the ledger goes in the report, and a role that ends up with nothing while
        // unclaimed layers carry real geometry stops the run.
        var ledger = LayerLedger.Build(readSheets, classification);
        var missingRoles = LayerLedger.RolesMissingWithGeometryUnclaimed(ledger);
        if (missingRoles.Count > 0)
        {
            var candidates = ledger.Where(e => !e.Claimed).Take(10)
                .Select(e => $"{e.Layer} ({e.Segments:N0} segments)");
            throw new InvalidOperationException(
                $"No layer in this drawing set matched {string.Join(" or ", missingRoles)}, yet " +
                $"{ledger.Where(e => !e.Claimed).Sum(e => e.Segments):N0} segments sit on layers the tool " +
                "does not recognise. That is a layer-naming mismatch, not a building without them. " +
                $"Candidates: {string.Join(", ", candidates)}. Set the layer patterns for this job.");
        }

        // RULES FROM THE MODEL IN FRONT OF US, where it can support them. Several numbers here were
        // measured once from one engineer's one model and became constants; each is now read off the
        // reference where that is credible, and the report says which source won.
        var derived = new List<DerivedRule>();
        if (request.DeriveRulesFromReference)
        {
            var opening = ReferenceRules.OpeningHeight(doc, composeFromReference.OpeningHeight);
            var slender = ReferenceRules.MaxColumnAspect(doc, classification.MaxColumnAspect);
            derived.Add(opening);
            derived.Add(slender);

            if (opening.FromReference) composeFromReference = composeFromReference with { OpeningHeight = opening.Value };
            if (slender.FromReference) classification = classification with { MaxColumnAspect = slender.Value };
        }

        var offset = request.Offset
            ?? (request.CentreOnGrid ? AutoOffset(doc, parsed.Select(p => p.Geometry)) : (0.0, 0.0));

        if (sheetsCutAway > 0)
            warnings.Add($"{sheetsCutAway} sheet(s) draw storeys this run removed and were not placed. " +
                         "That is the cut doing its job, not a drawing that failed to read.");

        // A rigid diaphragm spread across storeys is what ETABS warns about the moment the model
        // opens: "Horizontal rigid diaphragm connection found between joints at different
        // elevations." It comes in with the REFERENCE -- this tool assigns no diaphragms at all,
        // by the engineer's own ruling -- and an engineer seeing that dialog beside a generated
        // model will reasonably assume the generator did it. Say whose it is.
        // Which drawing's words landed on which drawing's geometry, said plainly.
        warnings.AddRange(annotationNotes);
        warnings.AddRange(ReferenceDiaphragmWarnings(doc));

        warnings.AddRange(FarFromOriginWarnings(parsed.Select(p => p.Geometry), offset));

        // What this job's own drawings say a wall is, before anything is written. A portfolio rule
        // cannot tell a 42" tower core from two faces paired across a parkade corridor; the job's
        // own distribution can, and it costs nothing to measure. See JobCalibration.
        var calibration = JobCalibration.From(
            parsed.SelectMany(p => p.Geometry.Walls).ToList(),
            parsed.SelectMany(p => p.Geometry.Columns).ToList());

        warnings.AddRange(calibration.Notes(parsed.SelectMany(p => p.Geometry.Walls).ToList()));

        var placements = new List<StoryPlacement>();
        foreach (var (sheet, geometry, matched) in parsed)
        {
            slabThicknessBySheet.TryGetValue(sheet.FileName, out var slabThickness);
            foreach (string storyName in matched)
                if (byName.TryGetValue(storyName, out var story))
                    placements.Add(new StoryPlacement(story, geometry, sheet.FileName, sheet.IsFoundation)
                    {
                        SlabThickness = slabThickness is null ? null : slabThickness.ThicknessInches / modelUnitInInches,
                        SlabThicknessInches = slabThickness?.ThicknessInches,
                        SlabThicknessPage = slabThickness?.PageNumber,
                    });
        }

        var foundationStoreys = placements
            .Where(p => p.IsFoundationSheet)
            .Select(p => p.Story.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Carried straight from the request, never from the rules: a plate that was not drawn is a
        // judgement about one job's drawings, not a standard the office holds.
        var composeOptions = (Math.Abs(modelUnitInInches - 1.0) < 1e-9 ? composeFromReference : composeFromReference.InUnitOf(modelUnitInInches))
            with
            {
                OffsetX = offset.X,
                OffsetY = offset.Y,
                InferMissingFloors = request.Compose.InferMissingFloors,
                StickFileSlabThicknessAttempted = !string.IsNullOrWhiteSpace(request.StickFilePdf),
                DefaultSlabThicknessInches = composeFromReference.DefaultSlabThickness,
            };
        var summary = E2kGeometryComposer.Compose(doc, placements, composeOptions);

        if (baseNormalised)
        {
            var lowest = doc.ReadStories().OrderBy(s => s.Elevation).First();
            summary = summary with
            {
                Flags = summary.Flags.Append(
                    $"The lowest storey ({lowest.Name}) was exported {480 / 12:0}ft-plus tall, because ETABS " +
                    "parks the base far below the building and folds the distance into it. Its height has been " +
                    $"set to a typical storey and the base raised to match, so the storey now spans " +
                    $"{(lowest.Elevation - lowest.ElevationBelow) / 12:0.0}ft. No other storey moved.").ToList(),
            };
        }

        summary = summary with { Flags = summary.Flags.Concat(LayerLedger.Describe(ledger)).ToList() };

        foreach (var rule in derived)
            summary = summary with
            {
                Flags = summary.Flags.Append(rule.FromReference
                    ? $"{rule.Name}: {rule.Value:0.##} — {rule.Because}."
                    : $"{rule.Name}: {rule.Value:0.##}, the standing value — {rule.Because}.").ToList(),
            };

        if (addedStoreys.Length > 0)
        {
            summary = summary with
            {
                Flags = summary.Flags.Append(
                    $"ADDED TO YOUR STOREY LIST: {string.Join(", ", addedStoreys)}. The drawings carry these " +
                    "parkade levels and the model did not, so their geometry had nowhere to go. Each was given " +
                    "the height your parkade already uses and the base dropped by the same total, so every " +
                    "storey above them is exactly where it was.").ToList(),
            };
        }

        if (droppedAbove.Length > 0)
        {
            // Same reason as the tower cut below: the reference's own members on the storeys we
            // removed would otherwise point at storeys the model no longer has. And a cut this
            // large has to be stated -- 26 storeys leaving a model quietly is the fault class this
            // report exists for, whether or not somebody asked for it.
            int orphanedAbove = doc.DropAssignsForMissingStoreys();
            summary = summary with
            {
                Flags = summary.Flags.Append(
                    $"Nothing above {request.TopStorey}: {droppedAbove.Length} storey(s) standing higher were " +
                    $"removed from the storey list, along with {orphanedAbove} assign(s) that stood on them. " +
                    $"Removed: {string.Join(", ", droppedAbove.Take(8))}{(droppedAbove.Length > 8 ? ", …" : "")}. " +
                    "Anything the drawings show above that height is not in this model.").ToList(),
            };
        }

        if (droppedByName.Length > 0)
        {
            int orphanedByName = doc.DropAssignsForMissingStoreys();
            summary = summary with
            {
                Flags = summary.Flags.Append(
                    $"Left out by name: {droppedByName.Length} storey(s) were removed because they belong to a " +
                    $"building this model is not of, along with {orphanedByName} assign(s) that stood on them. " +
                    $"Removed: {string.Join(", ", droppedByName)}. " +
                    "Neither the height cut nor the tower cut can see these — they are named, and that is " +
                    "why the footprint of every storey kept is listed above.").ToList(),
            };
        }

        if (droppedStoreys.Length > 0)
        {
            // The reference's own members on the towers we removed would otherwise point at
            // storeys that no longer exist.
            int orphaned = doc.DropAssignsForMissingStoreys();
            summary = summary with
            {
                Flags = summary.Flags.Append(
                    $"Tower {request.TowerOnly} only: {droppedStoreys.Length} storey(s) belonging to other towers " +
                    $"were removed from the storey list, along with {orphaned} assign(s) that stood on them. " +
                    $"Removed: {string.Join(", ", droppedStoreys.Take(8))}{(droppedStoreys.Length > 8 ? ", …" : "")}.").ToList(),
            };
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.OutputE2k))!);
        doc.Save(request.OutputE2k);

        return new DxfToEtabsReport(
            request.OutputE2k, files.Count, parsed.Count,
            placements.Select(p => p.Story.Name).Distinct().Count(),
            summary, offset, outcomes, warnings, requested, composeFromReference)
        {
            RulesApplied = banked,
            FoundationStoreys = foundationStoreys,
        };
    }

    /// <summary>
    /// Centres the drawings on the model's grid. Drafting exports carry Revit's
    /// project coordinates while ETABS models sit near their own origin, so without
    /// this the geometry lands thousands of inches away from the grid.
    /// </summary>
    /// <summary>
    /// Whether the geometry lands somewhere a building could be, once the offset is applied.
    ///
    /// Everything upstream of this is relative. A Revit export carries project coordinates
    /// thousands of inches from the origin and that is ordinary, which is exactly what the offset
    /// exists to absorb — so no part of the pipeline had a reason to care about absolute
    /// magnitude, and none did. A single wall drawn at 1,000,000,000,000 inches therefore
    /// generated cleanly and exited zero: one wall, the right count, fifteen million miles from
    /// the building.
    ///
    /// A quarter of a million inches is about four miles. No building is four miles across and no
    /// offset this tool computes should leave one out there, so past that something upstream is
    /// wrong — the wrong units, a corrupt coordinate, or sheets from two different sites.
    ///
    /// It warns and writes anyway. Moving the members would invent a position nobody drew, and
    /// dropping them would lose the evidence of whatever went wrong.
    /// </summary>
    public static IReadOnlyList<string> FarFromOriginWarnings(
        IEnumerable<PlanGeometrySet> sets, (double X, double Y) offset)
    {
        const double FarFromOrigin = 250_000.0;

        double farthest = 0;
        foreach (var geometry in sets)
            foreach (var wall in geometry.Walls)
                farthest = Math.Max(farthest, Math.Max(
                    Math.Max(Math.Abs(wall.Start.X + offset.X), Math.Abs(wall.Start.Y + offset.Y)),
                    Math.Max(Math.Abs(wall.End.X + offset.X), Math.Abs(wall.End.Y + offset.Y))));

        if (farthest <= FarFromOrigin) return Array.Empty<string>();

        return new[]
        {
            $"Geometry lands {farthest / 12.0:N0} ft from the model origin, which is further than any " +
            "building is wide. The drawings are probably in different units from the model, carry a " +
            "corrupt coordinate, or come from more than one site. The members were written where they " +
            "were drawn; nothing was moved to hide it.",
        };
    }

    public static IReadOnlyList<string> ReferenceDiaphragmWarnings(E2kDocument doc)
    {
        var warnings = new List<string>();
        var diaphragmStoreys = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in doc.LinesOf("AREA ASSIGNS"))
        {
            var m = Regex.Match(raw.Trim(),
                @"^AREAASSIGN\s+""(?<obj>[^""]+)""\s+""(?<storey>[^""]+)"".*?\bDIAPH\s+""(?<d>[^""]+)""",
                RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            if (m.Groups["obj"].Value.StartsWith("K", StringComparison.OrdinalIgnoreCase)) continue;

            string d = m.Groups["d"].Value;
            if (!diaphragmStoreys.TryGetValue(d, out var set))
                diaphragmStoreys[d] = set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(m.Groups["storey"].Value);
        }

        foreach (var (name, storeys) in diaphragmStoreys.Where(kv => kv.Value.Count > 1))
            warnings.Add($"Your reference model puts diaphragm \"{name}\" on more than one storey " +
                         $"({string.Join(", ", storeys)}). ETABS warns about that on import — " +
                         "\"rigid diaphragm connection between joints at different elevations\". " +
                         "It came from the reference, not from anything generated here, and nothing " +
                         "was changed about it.");

        return warnings;
    }

    private static (double X, double Y) AutoOffset(E2kDocument doc, IEnumerable<PlanGeometrySet> sets)
    {
        var (gMinX, gMaxX, gMinY, gMaxY) = ReadGridExtents(doc);

        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        void Take(DxfPoint p)
        {
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }

        foreach (var set in sets)
        {
            foreach (var wall in set.Walls) { Take(wall.Start); Take(wall.End); }
            foreach (var column in set.Columns) Take(column.Center);
            foreach (var slab in set.Slabs) foreach (var p in slab.Points) Take(p);
        }

        if (minX > maxX || double.IsInfinity(gMinX)) return (0, 0);

        double dxfCx = (minX + maxX) / 2.0, dxfCy = (minY + maxY) / 2.0;
        double modelCx = (gMinX + gMaxX) / 2.0, modelCy = (gMinY + gMaxY) / 2.0;
        return (modelCx - dxfCx, modelCy - dxfCy);
    }

    private static (double MinX, double MaxX, double MinY, double MaxY) ReadGridExtents(E2kDocument doc)
    {
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;

        foreach (string raw in doc.LinesOf("GRIDS"))
        {
            string line = raw.Trim();
            if (!line.StartsWith("GRID ", StringComparison.OrdinalIgnoreCase)) continue;

            int dirAt = line.IndexOf("DIR \"", StringComparison.OrdinalIgnoreCase);
            int coordAt = line.IndexOf("COORD ", StringComparison.OrdinalIgnoreCase);
            if (dirAt < 0 || coordAt < 0) continue;

            char dir = char.ToUpperInvariant(line[dirAt + 5]);
            string tail = line[(coordAt + "COORD ".Length)..].Trim();
            string token = new(tail.TakeWhile(c => !char.IsWhiteSpace(c)).ToArray());
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) continue;

            if (dir == 'X') { minX = Math.Min(minX, value); maxX = Math.Max(maxX, value); }
            else if (dir == 'Y') { minY = Math.Min(minY, value); maxY = Math.Max(maxY, value); }
        }

        if (double.IsInfinity(minY)) { minY = 0; maxY = 0; }
        return (minX, maxX, minY, maxY);
    }

    /// <summary>A report an engineer can read in under a minute before opening the model.</summary>
    public static string FormatReport(DxfToEtabsReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Model written : {report.OutputPath}");
        sb.AppendLine($"Sheets read   : {report.SheetsRead}   placed: {report.SheetsPlaced}");
        sb.AppendLine($"Storeys built : {report.StoriesPopulated}");
        sb.AppendLine($"Walls         : {report.Summary.Walls}");
        sb.AppendLine($"Columns       : {report.Summary.Columns}");
        sb.AppendLine($"Floors        : {report.Summary.Floors}");
        sb.AppendLine($"Joints        : {report.Summary.Points}");
        // Made and reused, kept apart.
        //
        // One list headed "Sections made" that included Rvt-Wall2, Rvt-Wall8 and Rvt-Floor0 read
        // like the Revit export's content had come through with the geometry -- on a package whose
        // whole claim is that every member came from a drawing. It had not: those are property
        // definitions that already existed in the model this was built on, and using one rather
        // than inventing a duplicate is the right thing to do. It just has to say so.
        var made = report.Summary.Sections.Where(s => !report.Summary.Reused.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
        sb.AppendLine($"Sections made : {string.Join(", ", made)}");
        if (report.Summary.Reused.Count > 0)
            sb.AppendLine($"Sections reused: {string.Join(", ", report.Summary.Reused)}   " +
                          "(already in your model — pointed at, not redefined; no member came from it)");

        // The count the workbook opens with, on the report, so no third document has to guess it.
        // The one-page summary said "nothing there is waiting on you" beside a workbook with three
        // NEEDS YOU rows, because the sentence was written once and the count lived somewhere else.
        int open = ModelQuestionnaire
            .StandingQuestions(report.ClassificationUsed, report.ComposeUsed, report)
            .Where(q => !q.ForTheRecord)
            .Count(q => !q.Decided);
        sb.AppendLine($"Questions for you: {open}");

        sb.AppendLine($"Offset applied: {report.AppliedOffset.X:0.#}, {report.AppliedOffset.Y:0.#} in");
        sb.AppendLine();

        // Sheets that are IN the model first. Sorting by filename alone opened this table with
        // eleven tower sheets showing 0 storeys, because A-LEVEL sorts before LEVEL -- eleven rows
        // that read as eleven failures, on a job whose engineer had asked for no towers. They were
        // removed on purpose; they belong under a heading that says so.
        var placedSheets = report.Sheets.Where(x => x.Stories.Count > 0)
            .OrderBy(x => x.File, StringComparer.OrdinalIgnoreCase).ToList();
        var unplacedSheets = report.Sheets.Where(x => x.Stories.Count == 0)
            .OrderBy(x => x.File, StringComparer.OrdinalIgnoreCase).ToList();

        sb.AppendLine("Sheet                                                  Lvls  Storeys  Walls  Cols  Slabs");
        foreach (var s in placedSheets)
        {
            string name = s.File.Length <= 52 ? s.File.PadRight(52) : s.File[..49] + "...";
            sb.AppendLine($"{name}  {s.Levels.Count,4}  {s.Stories.Count,7}  {s.Walls,5}  {s.Columns,4}  {s.Slabs,5}");
        }

        if (unplacedSheets.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Read but not placed on any storey in this model ({unplacedSheets.Count}):");
            foreach (var s in unplacedSheets)
            {
                string name = s.File.Length <= 52 ? s.File.PadRight(52) : s.File[..49] + "...";
                sb.AppendLine($"{name}  {s.Levels.Count,4}  {s.Stories.Count,7}  {s.Walls,5}  {s.Columns,4}  {s.Slabs,5}");
            }
        }

        if (report.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Not placed:");
            foreach (string w in report.Warnings) sb.AppendLine("  - " + w);
        }

        if (report.Summary.Flags.Count > 0)
        {
            // Flags about the model as a whole — storeys left without a plate, members already in
            // the engineer's model — are the ones worth acting on, and there are only a handful.
            // Listed with the per-sheet flags they fell off the end of a 766-line truncation.
            var wholeModel = report.Summary.Flags.Where(f => !f.Contains(".dxf:", StringComparison.OrdinalIgnoreCase)).ToList();
            var perSheet = report.Summary.Flags
                .Where(f => f.Contains(".dxf:", StringComparison.OrdinalIgnoreCase))
                .OrderBy(PerSheetFlagPriority)
                .ToList();

            if (wholeModel.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("About the model as a whole:");
                foreach (string f in wholeModel) sb.AppendLine("  - " + f);
            }

            sb.AppendLine();
            // ALL of them. This document is the location-by-location account, and it is the only
            // place some findings appear at all -- the one-page summary abbreviates on purpose.
            //
            // It used to stop at forty. That hid the line saying a floor had been split into two
            // plates, and it hid every slab-closure flag for C-LEVEL 3 on 31168 -- the storey the
            // engineer came back on with "level 3 has its own slab edge, it's on the drawings".
            // The evidence needed to answer her was in the 31 flags the report declined to print.
            sb.AppendLine($"Flags ({perSheet.Count}) — outlines that needed judgement:");
            foreach (string f in perSheet) sb.AppendLine("  - " + f);
        }

        return sb.ToString();
    }

    private static int PerSheetFlagPriority(string flag)
    {
        // A flag that says the model CHANGED SHAPE comes before any flag about something the tool
        // merely noticed. Splitting one floor into two is the largest single thing this list can
        // report, and it landed in the tail of a Take(40) with no priority of its own -- so the
        // one flag written specifically to stop a silent change was itself silent, on the run that
        // turned 31168's LEVEL 2 from one plate into two.
        if (flag.Contains("crossed itself", StringComparison.OrdinalIgnoreCase)) return -1;

        if (flag.Contains("recovered by flood", StringComparison.OrdinalIgnoreCase)) return 0;
        if (flag.Contains("taken from the inside face", StringComparison.OrdinalIgnoreCase)) return 1;
        if (flag.Contains("could not be resolved", StringComparison.OrdinalIgnoreCase)) return 2;
        if (flag.Contains("Modelled square", StringComparison.OrdinalIgnoreCase)) return 3;
        return 10;
    }
}
