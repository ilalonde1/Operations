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

    /// <summary>
    /// The storeys this sheet's TITLE claims, before anything was placed or cut.
    ///
    /// Not <see cref="Stories"/>, which after the cut holds the storeys this sheet's surviving
    /// OBJECTS landed on. The two differ by the engineer's own rule -- a solid wall or column drawn
    /// on sheet N belongs to storey N+1 -- so a sheet titled LEVEL 28 reports its members on
    /// B-LEVEL 29 and is still a drawing of level 28.
    ///
    /// Both are wanted, by different questions. "Which drawings filled this model" is the object
    /// reading. "Which storey is this slab region on" is this one: she ruled on 24 Aug that the
    /// storey shift is for walls and columns only and that "the slab stays at level N". Asking the
    /// object reading put a slab question on B-LEVEL 29, one storey above the drawing it came from.
    /// </summary>
    public IReadOnlyList<string> NamedStories { get; init; } = Array.Empty<string>();
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
    /// <summary>The building this model was cut to, where it was cut to one.</summary>
    public string? BuildingCut { get; init; }

    /// <summary>The finished file's own contents, read back after every cut and cleanup pass.</summary>
    public E2kModelContents SavedModel { get; init; } = E2kModelContents.Empty;

    /// <summary>
    /// How many floor plates each storey of the finished file carries.
    ///
    /// So the tool can hold itself to a count the engineer has already given. She said the YMCA
    /// mezzanine has three slabs; a workbook that asks her again is a workbook that stops being
    /// read, and one that says "you told us three and this model has two" is the tool doing its
    /// job.
    /// </summary>
    public IReadOnlyDictionary<string, int> PlatesByStorey { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Storeys whose floor reaches well past the members standing on it — a shared slab under one
    /// building's structure. The engineer is asked whose it is; see ModelQuestionnaire S5.
    /// </summary>
    public IReadOnlyList<string> FloorsWiderThanTheirStructure { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Diameters of the circles this run read on a column layer and DECLINED, because they are
    /// drawn as polygons rather than with arcs and are a size the drawing set draws no round
    /// column at. Grid bubbles, on 31168.
    ///
    /// On the report because it is a decision, not a loss, and the difference matters to anything
    /// counting what was read against what was modelled. Re-reading one sheet cannot reach it —
    /// what a round column looks like is a fact about the whole set.
    /// </summary>
    public IReadOnlyList<double> DeclinedCircleDiameters { get; init; } = Array.Empty<double>();

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
        "dxf.non-structural-sheet-patterns",
        "dxf.level-words",
        "dxf.building-words",
        "dxf.parkade-words",
        "dxf.range-words",
        "dxf.roof-words",
        "dxf.mezzanine-words",
        "dxf.foundation-words",
        "dxf.elevator-roof-words",
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

        // These eight were read and not required, which is the one combination that looks safe and
        // is not. LoadRequired pulls the whole view, so a value present in KorStandards was always
        // applied -- but a value MISSING from it fell back to the number in this file, silently,
        // on a production run whose whole contract is that a missing rule stops it. They were also
        // invisible: the report and the engineer's "Rules in force" sheet are built from this list,
        // so eight of the rules her model was actually built on were named nowhere she could see.
        //
        // Found by comparing the keys the code reads against this list against the database: 43,
        // 35, 43. All eight were in KorStandards, so requiring them changes no value, only what
        // happens when one is absent and what she is told.
        "dxf.donor-plate-likeness-margin",
        "dxf.doubled-edge-coverage",
        "dxf.slab-callout-min-thickness",
        "dxf.slab-callout-max-thickness",
        "dxf.ring-on-plate-edge-fraction",
        "dxf.recovered-outline-tolerance",
        "dxf.storeys-at-one-level-gap",
        "dxf.same-ground-area-tolerance",
        "dxf.same-ground-centre-tolerance",
        "dxf.doubled-edge-parallel-ratio",
        "dxf.flood-fill-bridge",
        "dxf.joint-merge-tolerance",
        "dxf.min-floor-coverage",
        "dxf.outline-self-touch-tolerance",
        "dxf.self-touch-report-gap",
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
        "dxf.non-structural-sheet-patterns",
        "dxf.level-words",
        "dxf.building-words",
        "dxf.parkade-words",
        "dxf.range-words",
        "dxf.roof-words",
        "dxf.mezzanine-words",
        "dxf.foundation-words",
        "dxf.elevator-roof-words",
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
            ["dxf.donor-plate-likeness-margin"] = compose.DonorPlateLikenessMargin,
            ["dxf.doubled-edge-coverage"] = classification.DoubledEdgeCoverage,
            ["dxf.slab-callout-min-thickness"] = classification.SlabCalloutMinThickness,
            ["dxf.slab-callout-max-thickness"] = classification.SlabCalloutMaxThickness,
            ["dxf.ring-on-plate-edge-fraction"] = classification.RingOnPlateEdgeFraction,
            ["dxf.recovered-outline-tolerance"] = classification.RecoveredOutlineTolerance,
            ["dxf.storeys-at-one-level-gap"] = compose.StoreysAtOneLevelGap,
            ["dxf.same-ground-area-tolerance"] = compose.SameGroundAreaTolerance,
            ["dxf.same-ground-centre-tolerance"] = compose.SameGroundCentreTolerance,
            ["dxf.doubled-edge-parallel-ratio"] = classification.DoubledEdgeParallelRatio,
            ["dxf.flood-fill-bridge"] = classification.FloodFillBridge,
            ["dxf.joint-merge-tolerance"] = compose.JointMergeTolerance,
            ["dxf.min-floor-coverage"] = compose.MinFloorCoverage,
            ["dxf.outline-self-touch-tolerance"] = classification.OutlineSelfTouchTolerance,
            ["dxf.self-touch-report-gap"] = compose.SelfTouchReportGap,
        };

        var applied = values.Keys.Concat(TextRuleKeys);
        if (!RequiredRuleKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(applied.OrderBy(k => k, StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The DXF-to-ETABS required rule list and applied rule list differ.");

        return values;
    }

    internal static PlanClassificationOptions ApplyRules(
        PlanClassificationOptions options,
        IReadOnlyDictionary<string, RuleSetting> settings)
        => options with
        {
            WallLayerPatterns = settings.ListOr("dxf.wall-layer-patterns", options.WallLayerPatterns),
            ColumnLayerPatterns = settings.ListOr("dxf.column-layer-patterns", options.ColumnLayerPatterns),
            SlabLayerPatterns = settings.ListOr("dxf.slab-layer-patterns", options.SlabLayerPatterns),
            NonStructuralSheetPatterns = settings.ListOr("dxf.non-structural-sheet-patterns", options.NonStructuralSheetPatterns),
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
            DoubledEdgeParallelRatio = settings.ValueOr("dxf.doubled-edge-parallel-ratio", options.DoubledEdgeParallelRatio),
            DoubledEdgeCoverage = settings.ValueOr("dxf.doubled-edge-coverage", options.DoubledEdgeCoverage),
            SlabCalloutMinThickness = settings.ValueOr("dxf.slab-callout-min-thickness", options.SlabCalloutMinThickness),
            SlabCalloutMaxThickness = settings.ValueOr("dxf.slab-callout-max-thickness", options.SlabCalloutMaxThickness),
            RingOnPlateEdgeFraction = settings.ValueOr("dxf.ring-on-plate-edge-fraction", options.RingOnPlateEdgeFraction),
            RecoveredOutlineTolerance = settings.ValueOr("dxf.recovered-outline-tolerance", options.RecoveredOutlineTolerance),
        };

    /// <summary>
    /// The words this office uses, from KorStandards. Named ApplyRules like its two siblings and
    /// for the same reason: RequiredRuleCoverageTests hands each of them a dictionary that records
    /// every key looked up, and a rule read anywhere else is a rule that gate cannot see. It found
    /// these eight the moment they existed.
    /// </summary>
    internal static DrawingVocabulary ApplyRules(
        DrawingVocabulary vocabulary,
        IReadOnlyDictionary<string, RuleSetting> settings)
        => vocabulary with
        {
            LevelWords = settings.ListOr("dxf.level-words", vocabulary.LevelWords),
            BuildingWords = settings.ListOr("dxf.building-words", vocabulary.BuildingWords),
            ParkadeWords = settings.ListOr("dxf.parkade-words", vocabulary.ParkadeWords),
            RangeWords = settings.ListOr("dxf.range-words", vocabulary.RangeWords),
            RoofWords = settings.ListOr("dxf.roof-words", vocabulary.RoofWords),
            MezzanineWords = settings.ListOr("dxf.mezzanine-words", vocabulary.MezzanineWords),
            FoundationWords = settings.ListOr("dxf.foundation-words", vocabulary.FoundationWords),
            ElevatorRoofWords = settings.ListOr("dxf.elevator-roof-words", vocabulary.ElevatorRoofWords),
        };

    internal static ComposeOptions ApplyRules(
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
            StoreysAtOneLevelGap = settings.ValueOr("dxf.storeys-at-one-level-gap", options.StoreysAtOneLevelGap),
            SameGroundAreaTolerance = settings.ValueOr("dxf.same-ground-area-tolerance", options.SameGroundAreaTolerance),
            SameGroundCentreTolerance = settings.ValueOr("dxf.same-ground-centre-tolerance", options.SameGroundCentreTolerance),
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
        var referencePointNames = doc.PointNames();

        // Before anything else: the storey list is what ETABS builds from, and an export parks the
        // base a thousand feet under the building with the whole distance folded into the lowest
        // storey. Left alone, every member down there is extruded that far on import.
        bool baseNormalised = doc.NormaliseBaseStorey();

        // The storey list as the engineer's own model has it, kept because sheet matching needs it
        // whatever the cuts do to the model afterwards. See the note where matchNames is built.
        var storiesBeforeCuts = doc.ReadStories().Select(s => s.Name).ToList();

        // READ FROM THIS DISK. A share path is mirrored locally first and the mirror is verified
        // complete; a local path is used as it stands. See DrawingMirror -- this is the four
        // minutes a run that used to depend on somebody remembering to copy the sheets over.
        string dxfFolder = DrawingMirror.Folder(request.DxfFolder);
        string? stickFile = request.StickFilePdf is null ? null : DrawingMirror.SingleFile(request.StickFilePdf);
        string? annotatedFolder = request.AnnotatedDxfFolder is null
            ? null
            : DrawingMirror.Folder(request.AnnotatedDxfFolder);

        var files = Directory.EnumerateFiles(dxfFolder, "*.dxf", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var sheetInfoByFile = files.ToDictionary(f => f, PlanSheetNaming.Parse, StringComparer.OrdinalIgnoreCase);

        // THE CUTS HAPPEN AFTER COMPOSITION, NOT BEFORE IT. See CutToThisBuilding below.
        //
        // They used to run here, on the storey list, before a single member was placed — and that
        // is why two models of one building could not be made to agree. A member is placed by
        // rising to the storey above it, so placement walks the storey stack; cutting the stack
        // first means each deliverable walks a DIFFERENT ladder and reaches a different answer for
        // the same drawing.
        //
        // On 31168 that shipped six storeys of the YMCA with no vertical structure at all in the
        // site model — C-LEVEL 3 to C-LEVEL 8 as floor plates over nothing, while LEVEL 4 to
        // LEVEL 8 each carried the tower's columns and the YMCA's stacked together. The YMCA-only
        // model, walking a ladder with no tower storeys in it, had them right.
        //
        // Composing once and cutting afterwards makes the smaller model a SUBSET of the larger by
        // construction. It is not that they now agree; it is that they cannot disagree.
        string[] droppedStoreys = Array.Empty<string>();
        string[] droppedAbove = Array.Empty<string>();
        string[] droppedByName = Array.Empty<string>();

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

        // THE WORDS THIS OFFICE USES, BEFORE ANY SHEET NAME IS READ.
        //
        // Sheet naming is asked about from five places, so the vocabulary is set once here rather
        // than threaded through all of them. Everything else about reading a plan was already a
        // rule; what a drawing is CALLED was seven regexes compiled into the assembly, and it is
        // the one thing a practice reliably differs on.
        PlanSheetNaming.Vocabulary = ApplyRules(DrawingVocabulary.Default, banked);

        // The sheets were parsed with whatever vocabulary was in force when the folder was read,
        // which on the first run of a process is the default. Re-read them now that the office's
        // own words are known, or a firm's rule would take effect one run late.
        foreach (string file in files.ToList())
            sheetInfoByFile[file] = PlanSheetNaming.Parse(file);

        // AND THE SET SAYS WHAT ITS OWN SHORTHAND MEANS. See SheetSetGlossary.
        var glossary = SheetSetGlossary.Learn(files);
        foreach (string file in files.ToList())
        {
            var tags = glossary.TagsFor(sheetInfoByFile[file]);
            if (tags.Count > 0 && sheetInfoByFile[file].BuildingTags.Count == 0)
                sheetInfoByFile[file] = sheetInfoByFile[file] with
                {
                    BuildingTags = tags,
                    BuildingTag = tags[0],
                };
        }

        if (glossary.Meanings.Count > 0)
            warnings.Add(
                "This drawing set defines its own shorthand: " +
                string.Join("; ", glossary.Meanings.Select(x => $"{x.Key} = building {string.Join(" and ", x.Value)}")) +
                ". Sheets that use the short form are read as the long form says, which is how the " +
                "set reads to a person and the only way a sheet titled only \"WEST\" can be known " +
                "to be another building's.");

        var requested = ApplyRules(request.Classification, banked);

        // NOT EVERY PLAN IN A DRAWING SET IS A PLAN THIS BUILDS FROM.
        //
        // Until 2026-08-26 this read every .dxf in the folder and the filtering was done by hand,
        // outside the tool, in a script — which protected exactly one job and no other. 31168's
        // Revit export offers 139 plan views and 57 are reinforcing plans, core-wall key plans,
        // uncropped working views and a design load plan: drawings whose linework is a schematic
        // OF the building rather than the building. A load plan's zone boundary reached a model
        // and was cut out of the ground floor as a 10,245 sq ft opening.
        //
        // Applied here rather than at the enumeration because the rule comes from KorStandards
        // and is not known until the settings are read. Named, never silent: a sheet refused is a
        // floor that will not be in the model, and whoever looks for it is owed the reason.
        if (requested.NonStructuralSheetPatterns.Count > 0)
        {
            var refused = new List<string>();

            foreach (string file in files.ToList())
            {
                string name = Path.GetFileNameWithoutExtension(file);
                string? hit = requested.NonStructuralSheetPatterns.FirstOrDefault(
                    pattern => name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hit is null) continue;

                files.Remove(file);
                sheetInfoByFile.Remove(file);
                refused.Add($"{Path.GetFileName(file)} [{hit}]");
            }

            if (refused.Count > 0)
                warnings.Add(
                    $"{refused.Count} sheet(s) in the folder are not structural plans and were not read: " +
                    string.Join(", ", refused.Take(6)) +
                    (refused.Count > 6 ? $", and {refused.Count - 6} more" : "") +
                    ". Governed by dxf.non-structural-sheet-patterns.");
        }

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
        var readButNotPlaced = new List<(string Sheet, int Walls, int Columns, int Slabs)>();
        string? unplacedNote = null;
        var parsed = new List<(PlanSheetInfo Sheet, PlanGeometrySet Geometry, IReadOnlyList<string> Stories)>();
        var readSheets = new List<IReadOnlyList<DxfSegment>>();

        // What was read from the annotated export, and from which sheet. The engineer must be
        // able to see which drawing's words landed on which drawing's geometry.
        var annotationNotes = new List<string>();
        var slabThicknessBySheet = StickFileSlabThicknessReader.ReadBySheet(
            sheetInfoByFile.Values.ToList(),
            stickFile);

        // TWO HALVES OF ONE PLAN, JOINED BEFORE ANYTHING ELSE LOOKS AT THEM.
        //
        // A plan too wide for one sheet is cut on a MATCH LINE and drawn twice. Read apart, neither
        // half closes a slab edge — the edge runs off the page at the seam — so no floor is made and
        // the parkade comes out with no slab. Half a plan is not a plan, and no threshold fixes it.
        //
        // This has to happen BEFORE the building filter below. On a one-building run the other half
        // is somebody else's sheet and would be thrown away, and the parkade would stay broken in
        // exactly the model that needs it: "we need the full structure at the parkade".
        // ONLY WHERE THE ENGINEER ASKED. Joining every split plan in the set is a rebuild, not an
        // answer: Andrea signed off on this model except the parkade, and rejoining the ground floor
        // would change a storey she had already accepted. The storeys to join are a fact she gives —
        // "we need the full structure at the parkade" — and nothing is joined until she says so.
        // Banked as match-line-join.<job>.<storey>, the same shape as slab-count.<job>.<storey>:
        // not a rule about how drawings are read anywhere, just something she told us about this
        // building.
        string joinJob = Path.GetFileNameWithoutExtension(request.OutputE2k);

        // slab-count.<job>.<storey> — how many separate slabs she says a storey carries.
        var slabCounts = banked
            .Where(r => r.Key.StartsWith("slab-count.", StringComparison.OrdinalIgnoreCase))
            .Select(r => (Parts: r.Key.Split('.', 3), r.Value))
            .Where(x => x.Parts.Length == 3
                        && joinJob.Contains(x.Parts[1], StringComparison.OrdinalIgnoreCase)
                        && x.Value.IsNumeric)
            .ToDictionary(x => x.Parts[2], x => (int)x.Value.Value, StringComparer.OrdinalIgnoreCase);

        var joinStoreys = banked
            .Where(r => r.Key.StartsWith("match-line-join.", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Key.Split('.', 3))
            .Where(p => p.Length == 3 && joinJob.Contains(p[1], StringComparison.OrdinalIgnoreCase))
            .Select(p => p[2])
            .ToList();
        var matchLineLayers = banked.ListOr("dxf.match-line-layer-patterns", MatchLineSheetJoin.DefaultLayerPatterns);

        var storeysOfSheet = files.ToDictionary(
            f => f,
            // Renamed the same way the placement below renames them, or a fact banked against the
            // storey's real name — slab-count.31168.LEVEL 1 MEZZ — matches nothing.
            f => (IReadOnlyList<string>)PlanSheetNaming.MatchStories(sheetInfoByFile[f], matchNames)
                .Select(s => doc.StoreyRenames.TryGetValue(s, out string? now) ? now : s)
                .Where(s => !cutStoreys.Contains(s))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StringComparer.OrdinalIgnoreCase);

        var joinable = joinStoreys.Count == 0
            ? new List<string>()
            : files.Where(f => storeysOfSheet[f].Any(s =>
                  joinStoreys.Any(j => s.Contains(j, StringComparison.OrdinalIgnoreCase)))).ToList();

        // EVERY SHEET IS READ ONCE, HERE, BEFORE ANY OF IT IS CLASSIFIED.
        //
        // The drawings have to be put on the engineer's grid -- turned as well as moved -- and the
        // turn is worked out from the grid lines across the whole set. That has to be known before
        // the first sheet is classified, because a column's bearing and a slab's ring are computed
        // from the geometry and must come out already in the model's frame. Reading here rather
        // than in the loop below costs nothing: each file is parsed exactly once either way, and
        // they are on local disk by now.
        var segmentsOf = files.ToDictionary(f => f, DxfPlanReader.ReadSegments, StringComparer.OrdinalIgnoreCase);

        var (gridX, gridY) = ReadGridCoordinates(doc);
        var alignment = request.Offset is null
            ? GridAlignment.Solve(segmentsOf.Values.SelectMany(s => s), gridX, gridY)
            : null;

        if (alignment is not null && alignment.Frame.RotationDegrees == 0
            && Math.Abs(alignment.Frame.OffsetX) < 1.0 && Math.Abs(alignment.Frame.OffsetY) < 1.0)
        {
            // Already where it belongs. Saying so is worth a line; moving it is not.
            alignment = null;
        }

        if (alignment is not null)
        {
            warnings.Add(
                $"The drawings were turned {alignment.Frame.RotationDegrees:0}° and moved onto this model's " +
                $"grid, matched by the grid lines themselves: {alignment.Note} A Revit export in shared site " +
                "coordinates lands a long way from a model built at its own origin, and on this job a quarter " +
                "turn away from it as well — project north against plan north. The structure is unchanged; it " +
                "now lies on the grid it was drawn against, so the DXF can be laid straight over the model.");
        }

        var joined = MatchLineSheetJoin.Group(joinable.Select(f => (
            File: f,
            Seam: MatchLineSheetJoin.SeamOf(segmentsOf[f], matchLineLayers),
            Storeys: storeysOfSheet[f],
            Segments: segmentsOf[f])));

        // Every partner keyed to the sheet that leads its group, and every follower marked so the
        // loop reads it once, as part of the plan it belongs to, rather than again on its own.
        var partnersOf = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var joinedInto = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in joined)
        {
            partnersOf[group.Files[0]] = group.Files.Skip(1).ToList();
            foreach (string follower in group.Files.Skip(1)) joinedInto[follower] = group.Files[0];

            warnings.Add(
                $"{string.Join(" + ", group.Files.Select(Path.GetFileName))} carry the same match line and were "
                + "read as ONE plan. A drawing too wide for one sheet is split on a match line, and neither half "
                + "closes a slab edge on its own.");
        }

        foreach (string file in files)
        {
            var sheet = sheetInfoByFile[file];

            // Already read as part of the plan it is half of.
            if (joinedInto.ContainsKey(file)) continue;

            if (request.BuildingTag is not null &&
                sheet.BuildingTags.Count > 0 &&
                !sheet.BuildingTags.Contains(request.BuildingTag, StringComparer.OrdinalIgnoreCase) &&
                !partnersOf.ContainsKey(file))
            {
                continue;
            }

            var segments = segmentsOf[file];
            var tags = DxfPlanReader.ReadPositionedTags(file);

            // The other half of this plan, in the same coordinates. The two sheets are drawn from
            // one model onto one grid — their match lines land on each other — so joining them is a
            // union, not a transform. Do not move anything.
            if (partnersOf.TryGetValue(file, out var partners))
            {
                foreach (string other in partners)
                {
                    segments = segments.Concat(segmentsOf[other]).ToList();
                    tags = tags.Concat(DxfPlanReader.ReadPositionedTags(other)).ToList();
                }
            }


            // THE WORDS COME FROM THE OTHER EXPORT, IF THERE IS ONE.
            //
            // Matched by storey rather than by file name: the two exports name their sheets
            // differently -- "LEVEL 2 PLAN - CONCRETE OUTLINE" against plain "LEVEL 2" -- and
            // PlanSheetNaming already reads both into the levels they serve.
            if (!string.IsNullOrWhiteSpace(annotatedFolder) && tags.Count == 0)
            {
                var carried = AnnotationOverlay.TagsFor(
                    annotatedFolder!, sheet, segments, classification, out string? note);
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
            // Her count for this sheet's storey, so a sheet she says is short looks harder.
            int? expected = null;
            foreach (string storey in storeysOfSheet[file])
                if (slabCounts.TryGetValue(storey, out int n)) { expected = n; break; }

            var geometry = StructuralPlanClassifier.Classify(
                segments, classification with { ExpectedSlabCount = expected }, sheet, tags);

            // WITHDRAWN. Admitting the largest region her count was short by looked right and was
            // wrong twice over.
            //
            // It admitted at the SIZE gate, before the shape checks that follow it, so a 325 sq ft
            // region came through as a 37-point ring — a "thin or hooked shape", which is exactly
            // what the 55% box-fill test exists to reject. ETABS refused it outright:
            // "Area Object KF7 not correctly defined". It reached Andrea's screen.
            //
            // And it was the wrong region anyway. Her three mezzanine slabs are the open chains of
            // 2,593, 1,961 and 502 sq ft — all ABOVE the 400 sq ft minimum, as the comment at that
            // threshold already said. Whatever is losing her third slab, it is not the size gate,
            // and a count is not a licence to admit the largest thing that failed a different test.
            //
            // RefusedForSize is kept because the question it answers is still the right one; what
            // was missing is which check is actually dropping a 502 sq ft chain.

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
                NamedStories = matched,
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

                // A DRAWING FULL OF STRUCTURE THAT LANDS NOWHERE IS THE LOUDEST FAULT THERE IS.
                //
                // It was the quietest. Seven of 31168's parkade sheets — every per-building plan of
                // levels P1, P2 and P3, 26 walls and 66 columns on one of them — matched no storey
                // and said so on line 400 of a 72 KB report, one line each, in the same voice as a
                // key plan with nothing on it. The undivided site-wide parkade sheet WAS placed, so
                // every count came out plausible and the model of building C alone stood on the
                // whole site's parkade. Nobody looked, for four days.
                //
                // Counted separately from the empty ones and said once, at the top, with what was
                // on them. A sheet with no structure landing nowhere is housekeeping; a sheet with
                // structure landing nowhere is a piece of the building that is not in the model.
                if (geometry.Walls.Count + geometry.Columns.Count + geometry.Slabs.Count > 0)
                    readButNotPlaced.Add((sheet.FileName, geometry.Walls.Count, geometry.Columns.Count, geometry.Slabs.Count));

                continue;
            }

            if (geometry.Walls.Count == 0 && geometry.Columns.Count == 0 && geometry.Slabs.Count == 0)
            {
                warnings.Add($"{sheet.FileName}: no structural outlines found on the expected layers — not placed.");
                continue;
            }

            // The sheet is in the model, so what it could not read is now worth saying.
            if (unreadWarning is not null) warnings.Add(unreadWarning);

            // READ IN THE DRAWING'S FRAME, DELIVERED IN THE MODEL'S. See PlanGeometryTransform for
            // why this is the last thing done to a sheet rather than the first.
            parsed.Add((
                sheet,
                alignment is null ? geometry : PlanGeometryTransform.Apply(geometry, alignment.Frame),
                matched));
        }

        // THE DRAWING SET SAYS WHAT A ROUND COLUMN LOOKS LIKE.
        //
        // A circle drawn as a many-sided polygon cannot be told from a chamfered square by shape —
        // deciding it by shape once turned 160 chamfered columns into ten-inch cylinders — so the
        // tool reports the shape and models it square. That is right as far as it goes, and it is
        // not far enough: "LEVEL 33 PLAN - BLDG A" draws no columns at all on its column layer,
        // only 32 ten-inch circles in pairs around the perimeter band between two slab edges, and
        // all 32 became columns. Tower A's level 34 shipped with 49 where the tower has 24.
        //
        // The set answers it. Every round column anyone drew with a real ARC in these drawings
        // measures 16, 24 or 30 inches. A ten-inch circle matches none of them, and a drawing set
        // does not draw the same thing two ways and at a size it uses nowhere else.
        //
        // So a polygon that is the shape of a circle is modelled as a column only where the set
        // draws a real round column of about that size. Where the set draws no round columns with
        // arcs at all, nothing is known and nothing is dropped.
        var declinedCircleDiameters = new List<double>();
        var drawnWithArcs = parsed
            .SelectMany(p => p.Geometry.Columns)
            .Where(c => c.IsRound)
            .Select(c => c.Width)
            .ToList();

        if (drawnWithArcs.Count > 0)
        {
            var outOfFamily = new List<ColumnFootprint>();

            foreach (var (_, geometry, _) in parsed)
            {
                var strangers = geometry.Columns
                    .Where(c => c.DrawnAsAPolygonCircle)
                    .Where(c => !drawnWithArcs.Any(d => Math.Abs(d - c.Width) <= 2.0))
                    .ToList();

                foreach (var stranger in strangers) geometry.Columns.Remove(stranger);
                outOfFamily.AddRange(strangers);
            }

            declinedCircleDiameters = outOfFamily.Select(c => c.Width).Distinct().ToList();

            if (outOfFamily.Count > 0)
                warnings.Add(
                    $"{outOfFamily.Count} circle(s) on a column layer were not modelled, because they are " +
                    "drawn as polygons rather than with arcs AND are a size this drawing set draws no round " +
                    $"column at: {string.Join(", ", outOfFamily.Select(c => $"{c.Width:0}\"").Distinct().OrderBy(x => x))} " +
                    $"against {string.Join(", ", drawnWithArcs.Select(d => $"{d:0}\"").Distinct().OrderBy(x => x))} " +
                    "drawn with arcs. A circle drawn as a polygon at a size the set does use is still modelled, " +
                    "and so is every column drawn with an arc, whatever its size.");
        }

        if (readButNotPlaced.Count > 0)
            unplacedNote =
                $"{readButNotPlaced.Count} drawing(s) carry structure that is NOT IN THIS MODEL, because the " +
                "storeys they name do not exist in it: " +
                string.Join("; ", readButNotPlaced
                    .OrderByDescending(x => x.Walls + x.Columns)
                    .Select(x => $"{x.Sheet} ({x.Walls} wall(s), {x.Columns} column(s), {x.Slabs} plate(s))")
                    .Take(8)) +
                (readButNotPlaced.Count > 8 ? $", and {readButNotPlaced.Count - 8} more" : "") +
                ". Either the model needs those storeys, or the drawings name them differently from the " +
                "way the model does. This is not a warning about the drawings — it is a piece of the " +
                "building that was read, understood, and then left out.";

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

        // A MODEL HALF A MILE FROM ITS OWN GRIDS IS A MODEL NOBODY CAN SEE.
        //
        // The engineer's own 31168 sits at x -12..885, y 3,717..5,237 and her grids run -1,379 to
        // about 900. The Revit export is in SHARED coordinates -- the site survey system, which is
        // what makes every sheet stack -- and lands at x 38,815..41,683, y 27,057..31,107. Three
        // thousand feet east and two thousand north of her building.
        //
        // Nothing was wrong with the geometry. It opened, it was complete, it was internally
        // consistent, and it was off the screen: "the buildings don't show up". Centring existed
        // (AutoOffset) and was reachable only through a flag no publish sets.
        //
        // So it is not a flag any more. Where the drawing does not overlap the model's grids AT
        // ALL, it is moved onto them and the move is stated. Where it does overlap, nothing is
        // touched -- a drawing already in the model's coordinates must not be shifted, and that is
        // every job this tool has read until today.
        // A RECONSTRUCTED EDGE NAMES ITSELF, AT THE TOP.
        //
        // The per-sheet flag for this is written by the classifier and does not reach the report
        // for every sheet -- C-LEVEL 3's did not, and that storey's floor is the one the engineer
        // asked about. A plate whose outline was completed through linework this tool does not
        // model is the single thing on this run she most needs told, so it is said here, against
        // the model, where nothing can drop it.
        // Named by STOREY, not by sheet: it is the storey whose floor changed that she will open,
        // and PlanSheetInfo.FileName has had its level token stripped by the time it reaches here
        // ("S2.40.1_1_ PLAN"), which would put a half-name in front of an engineer.
        var completed = parsed
            .SelectMany(p => p.Geometry.Flags.Select(f => (
                Sheet: p.Stories.Count > 0 ? string.Join(" / ", p.Stories) : p.Sheet.Label,
                Flag: f)))
            .Where(x => x.Flag.Contains("was completed through", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (completed.Count > 0)
            warnings.Add(
                $"{completed.Count} slab outline(s) were completed through linework on layers this tool does " +
                "not model, because their ends meet that linework exactly — the edge is continuous on the " +
                "drawing even where it is not continuous on one layer. Nothing was bridged or invented, and " +
                "each is worth an eye: " +
                string.Join("; ", completed.Select(x => $"{x.Sheet} — {Sized(x.Flag)}")));

        // "…an outline of 22,663 sq ft was completed through 2 segment(s) on JBP_C_B_STRUCT…"
        static string Sized(string flag)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                flag, @"an outline of ([\d,]+ sq ft) was completed through (\d+ segment\(s\) on [^—]+)");
            return m.Success ? $"{m.Groups[1].Value} closed through {m.Groups[2].Value.Trim()}" : flag;
        }

        var offset = request.Offset ?? (0.0, 0.0);

        // The grid fit has already put the linework where it belongs, turn and all. Centring on
        // top of it would move a model that is already on its grid -- and centring is what this
        // fell back to when it could only translate.
        if (request.Offset is null && alignment is null)
        {
            var geometry = parsed.Select(p => p.Geometry).ToList();
            if (request.CentreOnGrid)
            {
                offset = AutoOffset(doc, geometry);
            }
            else if (DrawingMissesTheGrid(doc, geometry, out string howFar))
            {
                offset = AutoOffset(doc, geometry);
                if (offset != (0.0, 0.0))
                    warnings.Add(
                        $"The drawings do not overlap this model's grid lines at all — {howFar}. They have " +
                        $"been moved onto the grid by {offset.Item1:0} , {offset.Item2:0} in so the model " +
                        "opens where the grids are. Nothing about the structure changed; a drawing exported " +
                        "in site coordinates lands a long way from a model built at its own origin, and a " +
                        "model nobody can find is a model nobody can check.");
            }
        }

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
                        // ONE building, or none. A sheet titled "BLDG A&B" is drawn for both and
                        // is shared between them; taking its first tag made it building A's, and
                        // its members then rose to the next A-tagged storey and skipped the shared
                        // one between. LEVEL 26 lost every wall and column holding its floor up.
                        SheetBuildingTag = sheet.BuildingTags.Count == 1 ? sheet.BuildingTag : null,
                        SheetBuildingTags = sheet.BuildingTags,
                        IsIssuedSheet = sheet.IsIssuedSheet,
                        IsPerBuildingSheet = sheet.BuildingTags.Count > 0,
                        SlabThickness = slabThickness is null ? null : slabThickness.ThicknessInches / modelUnitInInches,
                        SlabThicknessInches = slabThickness?.ThicknessInches,
                        SlabThicknessPage = slabThickness?.PageNumber,
                    });
        }

        // A WHOLE-FLOOR SHEET AND THE PER-BUILDING SHEETS OF THE SAME FLOOR ARE ONE DRAWING.
        //
        // 31168 draws LEVEL 2 three times: once whole, on a sheet with no building in its title,
        // and once per building — "BLDG C" and "WEST (BLDG A & B)". Measured from this run's own
        // ledger, the whole sheet yields 38 walls and 60 columns and the two halves yield 16/36
        // and 22/24. 38 = 16 + 22 and 60 = 36 + 24: the same structure, drawn once entire and once
        // in parts.
        //
        // Only the parts know which building they belong to, and a member is placed by rising to
        // the storey above IT — so the whole sheet's members all rose to the shared storey and the
        // YMCA's C-LEVEL 3 came out with a floor plate and nothing holding it up. The parts put
        // them on C-LEVEL 3 and LEVEL 3 respectively, which is the building.
        //
        // So where a storey is drawn both ways, the parts win. Not a preference — the whole sheet
        // cannot answer the question placement asks.
        //
        // Deliberately narrow: it fires only where a storey has BOTH an untagged sheet and at
        // least one tagged one. A job whose sheets are all untagged, or all tagged, is untouched.
        // AND ONLY WHERE THE PARTS ACTUALLY COVER THE WHOLE.
        //
        // "The same storey is also drawn per building" is not enough on its own. 31168 issues a
        // RANGE sheet — "LEVEL 15 PLAN (L15-26) ... BLDG A&B" — which is tagged and lands on
        // twelve storeys, so every per-level sheet from LEVEL 15 to LEVEL 26 looked superseded.
        // LEVEL 26 lost the members holding its floor up and came back as a plate over nothing.
        //
        // The evidence that justified this rule was arithmetic: on LEVEL 2 the whole sheet gives
        // 38 walls and 60 columns and the parts give 16/36 and 22/24, so 38 = 16 + 22 exactly. So
        // that is the test. The parts must together carry at least what the whole carries; where
        // they do not, the whole sheet is drawing structure they leave out and it stays.
        // A SHEET IS A WHOLE WHENEVER OTHER SHEETS DRAW ITS BUILDINGS SEPARATELY.
        //
        // Untagged against tagged was only the common case of it. 31168's towers are also drawn
        // together — "LEVEL 15 PLAN (L15-26) - CONCRETE OUTLINE - BLDG A&B" — and again apart, as
        // BLDG A and BLDG B. That sheet names two buildings, so it is nobody's: its members cannot
        // prefer A's storeys or B's, and above LEVEL 26, where the stack splits into A-LEVEL 27 and
        // B-LEVEL 27 four inches apart, all of them rose to whichever came first. A-LEVEL 27 shipped
        // with 40 walls and 48 columns — both towers — and B-LEVEL 27 with a floor plate and nothing
        // under it. The same at 34, 35 and 36.
        //
        // Treating it as a part, because it named buildings at all, is what let it through. Against
        // BLDG A and BLDG B it is not a part; it is the whole they are the parts of. So the test is
        // containment, not taggedness: a sheet stands down where other sheets on the same storey
        // name a STRICT SUBSET of its buildings and together carry what it carries. An untagged
        // sheet names every building, which is why the old rule was a special case of this one.
        static bool DrawnBy(StoryPlacement p, StoryPlacement whole) =>
            p.SheetBuildingTags.Count > 0
            && (whole.SheetBuildingTags.Count == 0
                || (p.SheetBuildingTags.Count < whole.SheetBuildingTags.Count
                    && p.SheetBuildingTags.All(t => whole.SheetBuildingTags.Contains(t, StringComparer.OrdinalIgnoreCase))));

        // BY FLOOR, NOT BY STOREY NAME. A whole and its parts often land on different storeys of
        // the same floor: 31168's kept view "B-LEVEL 33" and the issued sheets "LEVEL 33 - BLDG A"
        // and "- BLDG B" are one floor under three names four inches apart, and a rule that
        // grouped on the name could not see them together.
        var floorOfStorey = doc.FloorOfStorey();
        string FloorNamed(string storey) =>
            floorOfStorey.TryGetValue(storey, out string? f) ? f : storey;

        var onFloor = placements
            .GroupBy(p => FloorNamed(p.Story.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // AND A KEPT VIEW STANDS DOWN TO AN ISSUED DRAWING OF THE SAME FLOOR.
        //
        // A Revit export offers every view in the model. The issued drawings are cropped to what
        // they are about; the working views the drafter kept are not, so "B-LEVEL 33" draws every
        // building standing at that elevation while carrying one tower's name. Its 73 columns went
        // up tower B and tower A's storeys came out as floor plates over nothing.
        //
        // Only where an issued sheet covers that floor. Levels 10 to 14 are drawn on kept views
        // alone, and they keep everything they have.
        static bool StandsDownTo(StoryPlacement part, StoryPlacement whole) =>
            DrawnBy(part, whole) || (part.IsIssuedSheet && !whole.IsIssuedSheet);

        var supersededByParts = placements
            .Where(whole => onFloor.TryGetValue(FloorNamed(whole.Story.Name), out var siblings)
                            && siblings.Where(p => StandsDownTo(p, whole)) is var parts
                            && parts.Any()
                            && parts.Sum(p => p.Geometry.Walls.Count) >= whole.Geometry.Walls.Count
                            && parts.Sum(p => p.Geometry.Columns.Count) >= whole.Geometry.Columns.Count)
            .ToList();

        // MEMBERS ONLY. The whole-floor sheet keeps its FLOOR.
        //
        // Dropping the whole sheet outright cost sixteen plates: 106 to 90. The parts draw the
        // structure the whole sheet draws, member for member, but they do NOT always draw the same
        // slab edge — a half-sheet is cropped to its building and the whole one is not. So the
        // whole sheet stays for its plates and openings, and stands down only for the walls and
        // columns, which are the things placement has to know a building for.
        //
        // Plates arriving twice is a case the composer already settles: one plate per place per
        // storey, and a floor read twice is not a floor with a hole in it.
        if (supersededByParts.Count > 0)
        {
            foreach (var whole in supersededByParts)
            {
                var floorsOnly = new PlanGeometrySet();

                // AND ITS FLOOR ONLY WHERE THE PARTS LEAVE GROUND UNCOVERED.
                //
                // Keeping every whole-sheet plate outright puts the same floor in the model twice.
                // 31168's LEVEL 2 came out with four plates and 63,114 sq ft on a storey whose
                // floor is about 40,000: the whole-site outline, and then building C's and the
                // west half's drawn again on top of it.
                //
                // Dropping them outright is worse — it cost sixteen plates the first time it was
                // tried, because a half-sheet IS cropped to its building and does not always draw
                // the whole slab edge. So neither: each whole-sheet plate is measured against the
                // ground the parts actually cover on that storey, and stands down only where they
                // have it covered.
                var partSlabs = placements
                    .Where(p => p.IsPerBuildingSheet
                                && p.Story.Name.Equals(whole.Story.Name, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(p => p.Geometry.Slabs)
                    .Select(s => s.Points)
                    .ToList();

                var kept = whole.Geometry.Slabs
                    .Where(s => CoveredFraction(s.Points, partSlabs) < 0.8)
                    .ToList();

                floorsOnly.Slabs.AddRange(kept);
                floorsOnly.Openings.AddRange(
                    whole.Geometry.Openings.Where(o => kept.Any(s => LoopGeometry.PointInPolygon(Centroid(o.Points), s.Points))));
                floorsOnly.Tags.AddRange(whole.Geometry.Tags);
                floorsOnly.Flags.AddRange(whole.Geometry.Flags);

                placements[placements.IndexOf(whole)] = whole with { Geometry = floorsOnly };
            }

            warnings.Add(
                $"{supersededByParts.Count} whole-floor sheet placement(s) gave up their walls and columns " +
                "because the same storey is also drawn per building, and only the per-building sheets say " +
                "which building a member belongs to: " +
                string.Join(", ", supersededByParts
                    .Select(w => $"{Path.GetFileName(w.SourceSheet)} on {w.Story.Name}")
                    .Distinct()
                    .Take(6)) +
                (supersededByParts.Count > 6 ? $", and {supersededByParts.Count - 6} more" : "") +
                ". Their floor plates are kept — a half-sheet is cropped to its building and does not " +
                "always draw the whole slab edge.");
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

                // Set AFTER InUnitOf, and never scaled by it: this is what a model unit MEASURES,
                // not a length measured in one. A thickness read off the drawing arrives as a
                // printed dimension in inches and is the only value here that needs it.
                ModelUnitInInches = modelUnitInInches,
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

        // Said where the workbook will find it. A note about a piece of the building that is not in
        // the model belongs in front of the engineer, not in the run log.
        if (unplacedNote is not null)
            summary = summary with { Flags = summary.Flags.Append(unplacedNote).ToList() };

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

        // The whole site is composed by now. What this model is NOT of comes off here, in the
        // order the three cuts have always run in: the building first, then the height, then the
        // names neither of those can see.
        droppedStoreys = request.TowerOnly is null
            ? Array.Empty<string>()
            : doc.KeepOnlyTower(request.TowerOnly).ToArray();

        droppedAbove = request.TopStorey is null
            ? Array.Empty<string>()
            : doc.KeepStoreysUpTo(request.TopStorey).ToArray();

        droppedByName = request.DropStoreys.Count == 0
            ? Array.Empty<string>()
            : doc.DropStoreys(request.DropStoreys).ToArray();

        // A merge is only a merge once the members follow it.
        int followedRenames = doc.RenameStoreysInAssigns();

        // ONE LABEL ALL THE WAY UP. After the cuts, so it merges exactly what she receives.
        //
        // ⚠ OFF, AND ONE LINE FROM ON. Turn this true to run it; everything else is finished.
        //
        // What is proven: placement is exact. 1,769 column objects become 268 and the multiset gate
        // below stays silent, confirmed independently by docs/etabs-handoff/members_by_storey.py --
        // all 29 storeys identical, walls, columns and plates. The "adds members, LEVEL 2 columns
        // 36 to 60" this was stashed for does not reproduce and was never the merge.
        //
        // What is NOT settled, and why this is still off: three coverage checks disagree with the
        // merged model on column SIZE and SHAPE -- 10 columns on 31168, 4 on 31138, every one of
        // them "drawn with arcs but built rectangular". They are not mixed-section members; after
        // the connectivity fix no object carries two sections. The lead is that a member now offers
        // many candidate storeys to EveryGeneratedMemberHasTheSizeItWasDrawnAt, and at
        // (2174,2630) a 42x42 column's 39in window reaches a KOR-D30 round column 23in away, so
        // whose arcs are whose is decided by Closer() against a centre list the merge has thinned.
        // That is a lead and not a measurement, and this stays off until it is one.
        //
        // The merge is a RENAMING, and the gate is its postcondition, not a report line. A member
        // appearing, vanishing or moving while 1,769 objects become 268 is a defect in this code,
        // and a model built by broken code must not reach an engineer. So it throws rather than
        // warning -- the same reason a missing rule stops a production run.
        const bool MergeStacksIntoOneLabel = false;

        var beforeStackMerge = MemberPlanStoreyMultisetPreserved.Capture(doc);
        int stacked = MergeStacksIntoOneLabel ? doc.MergeStackedMembers() : 0;
        MemberPlanStoreyMultisetPreserved.Assert(beforeStackMerge, doc);
        if (stacked > 0)
            warnings.Add(
                $"{stacked} column and wall object(s) were merged into the stack they belong to, so a member " +
                "running through several floors now carries ONE label its whole height with a separate " +
                "member between each pair of floors. Each storey is read from its own drawing, so each " +
                "used to arrive as a differently named object — a single column reading C360, C359, C363 up " +
                "the building. This is the convention your own 31138 model uses: one object, an assign per " +
                "storey, the section carried on the assign so a column may still change size as it rises.");

        // One floor holds one of each member — whether the cut merged two storeys, or the model
        // always had two names for the same floor.
        int mergedAway = doc.DropMembersDuplicatedOnOneFloor();
        if (mergedAway > 0)
            warnings.Add(
                $"{mergedAway} member(s) stood on one floor twice, and one of each was kept. The " +
                "shared ground floor is drafted once per building, and the engineer's model gives it " +
                "two storeys 1.67 in apart so each tower can rise through its own — so a whole-site " +
                "drawing, naming neither, is placed on both. Same joints, same section, an inch and " +
                "a half apart: right area, right position, and the floor in the model twice.");

        if (followedRenames > 0)
            warnings.Add(
                $"{followedRenames} member(s) followed a storey that was renamed by the cut — the shared " +
                "ground floor is drafted once per building and becomes one storey in a model of one " +
                "building. Without this they would name a storey the cut had just removed and be dropped, " +
                "which is a ground floor with nothing on it.");

        // AND THE OTHER BUILDINGS COME OFF THE SHARED STOREYS.
        //
        // The storey cut takes A-LEVEL 27 out of a building-C model because the NAME says whose it
        // is. It can do nothing about the towers' own structure standing on LEVEL 2, which is
        // shared and named for nobody — so the YMCA model came out carrying 63,114 sq ft of level
        // 2 when building C's share of it is 14,607, and every tower column with it.
        //
        // Which building a member belongs to is not in the file and cannot be recovered from it.
        // It is known once, where the member is made, from the sheet it was drawn on.
        if (request.TowerOnly is { } onlyBuilding)
        {
            // A member drawn on a sheet that names buildings, none of them this one, is not this
            // building's — wherever it stands. That covers "BLDG A & B" as squarely as "BLDG A",
            // and, through the set's own glossary, the bare "WEST" as well.
            //
            // A member drawn on a sheet that names nobody is kept. The parkade slab is drafted
            // once for the whole site and belongs to all three buildings; so does the core-wall
            // key plan that supplies this building's own ground-floor walls. An earlier attempt
            // dropped everything untagged on a storey this building drew for itself and took the
            // YMCA's ground floor with it: 66 walls to nil.
            var going = summary.BuildingOfObject
                .Where(x => x.Value.Count > 0
                            && !x.Value.Contains(onlyBuilding, StringComparer.OrdinalIgnoreCase))
                .Select(x => x.Key)
                .ToHashSet(StringComparer.Ordinal);

            var storeyOf = doc.StoreysByObject();

            // AND THE SITE-WIDE FLOOR OF A FLOOR THIS BUILDING DRAWS ITSELF.
            //
            // Plates only, and the distinction matters. A member that names nobody has to stay:
            // the core-wall key plan is drafted once for the site and it is where building C's own
            // ground-floor walls come from, so dropping untagged members cost the YMCA 66 walls and
            // 108 columns the first time this was tried.
            //
            // A plate is different, because a plate has an extent and the extent IS the answer.
            // Level 1 is drawn once for the whole site — 73,788 sq ft of podium over the parkade —
            // and once per building, building C's being 11,026. Both untagged and tagged plates
            // land on the same floor, so a model of building C came out with the whole podium under
            // a fifteen-thousand-foot building. Where this building draws its own floor, that
            // drawing is its floor.
            //
            // Where it does not, nothing is dropped: the parkade slab is drafted once for the site
            // and tagged for nobody, and it is as much building C's as anyone's.
            var plates = doc.PlateNames();
            var floorOfCutStorey = doc.FloorOfStorey();

            string FloorOf(string storey) =>
                floorOfCutStorey.TryGetValue(storey, out string? f) ? f : storey;

            var drawnByThisBuilding = storeyOf
                .Where(x => plates.Contains(x.Key)
                            && summary.BuildingOfObject.TryGetValue(x.Key, out var tags)
                            && tags.Contains(onlyBuilding, StringComparer.OrdinalIgnoreCase))
                .SelectMany(x => x.Value.Select(FloorOf))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int siteWide = 0;
            foreach (var (obj, storeys) in storeyOf)
            {
                if (going.Contains(obj) || !plates.Contains(obj)) continue;
                if (summary.BuildingOfObject.TryGetValue(obj, out var tags) && tags.Count > 0) continue;
                if (!storeys.Select(FloorOf).Any(drawnByThisBuilding.Contains)) continue;

                going.Add(obj);
                siteWide++;
            }

            if (siteWide > 0)
                warnings.Add(
                    $"{siteWide} site-wide floor plate(s) were removed from floors building {onlyBuilding} " +
                    "draws for itself. Level 1 is drawn once entire — the podium over the parkade, 73,788 " +
                    "sq ft — and once per building; keeping both puts the whole site's ground floor in a " +
                    "model of one building. Floors this building does not draw for itself, the parkade " +
                    "among them, keep their shared slab.");

            // AND WHAT STANDS OUTSIDE THE FLOOR THIS BUILDING DREW.
            //
            // Having taken the site-wide podium slab off level 1, the model still stood 108 columns
            // and 64 walls under an 11,026 sq ft plate: the whole site's ground-floor structure,
            // holding up floor that is no longer there. Plates and members have to answer to the
            // same rule or the model is incoherent whichever way it is cut.
            //
            // The rule is the one the plate already gave: on a floor this building draws for
            // itself, that drawing is the building. A column standing outside it is holding up
            // somebody else's floor. Three feet of margin, because a perimeter wall sits ON the
            // slab edge and half of it is outside by construction.
            //
            // Only on floors this building draws for itself — the parkade is drafted once for the
            // site, building C draws no parkade of its own, and all 108 of its columns stay.
            //
            // AND ONLY ON FLOORS THE BUILDINGS SHARE. A storey the engineer named C-LEVEL 3 is
            // building C's entire, and there is nothing on it to separate from anybody. Applied
            // there, this rule took nine of C's own columns off C-LEVEL 3 — real columns that rise
            // to that storey and stand beside its plate rather than on it, which is what a column
            // at a slab edge or under a setback does. The site model kept them; the two deliverables
            // disagreed about a storey that is one building's.
            var planOf = doc.PlanPointsOfObjects();

            var floorOutline = storeyOf
                .Where(x => plates.Contains(x.Key) && !going.Contains(x.Key) && planOf.ContainsKey(x.Key))
                .SelectMany(x => x.Value.Select(FloorOf).Distinct(StringComparer.OrdinalIgnoreCase)
                                        .Where(drawnByThisBuilding.Contains)
                                        .Select(f => (Floor: f, Outline: planOf[x.Key])))
                .GroupBy(x => x.Floor, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Outline).ToList(), StringComparer.OrdinalIgnoreCase);

            int elsewhere = 0;
            foreach (var (obj, storeys) in storeyOf)
            {
                if (going.Contains(obj) || plates.Contains(obj)) continue;

                // NEVER A MEMBER THE DRAWING SAYS IS THIS BUILDING'S. Position is the weakest
                // evidence there is and it only gets a say where the drawings are silent.
                //
                // This rule exists to take the site's ground-floor structure out of a one-building
                // model, back when the only sheet drawing it named no building. The parkade is
                // drawn per building too — BLDG C and WEST at every level — and once those sheets
                // were actually placed, building C's own parkade columns rise from P1 to LEVEL 1
                // and stand across the whole of C's parkade, which is far wider than the 11,026
                // sq ft of building sitting on it. Judged by position they are all outside, and
                // LEVEL 1 emptied for the second time this week: 0 walls, 0 columns.
                if (summary.BuildingOfObject.TryGetValue(obj, out var mine)
                    && mine.Contains(onlyBuilding, StringComparer.OrdinalIgnoreCase)) continue;

                if (!planOf.TryGetValue(obj, out var points)) continue;

                if (storeys.All(s => E2kDocument.BuildingTagOf(s)
                        .Equals(onlyBuilding, StringComparison.OrdinalIgnoreCase))) continue;

                var floors = storeys.Select(FloorOf).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (!floors.Any(floorOutline.ContainsKey)) continue;
                if (floors.Any(f => !floorOutline.ContainsKey(f))) continue;

                bool onSomeFloor = floors.Any(f => floorOutline[f]
                    .Any(outline => points.Any(p => WithinOrNear(p, outline, 36.0))));

                if (onSomeFloor) continue;
                going.Add(obj);
                elsewhere++;
            }

            if (elsewhere > 0)
                warnings.Add(
                    $"{elsewhere} member(s) stood outside the floor building {onlyBuilding} draws for " +
                    "itself and were removed. They are the rest of the site's ground-floor structure, " +
                    "drafted on the same site-wide sheets; left in, they hold up floor this model no " +
                    "longer has. Floors this building does not draw for itself keep everything.");

            int foreign = doc.DropObjects(going);

            if (foreign > 0)
                warnings.Add(
                    $"{foreign} member(s) drawn for another building were removed from the storeys this " +
                    $"model shares with it. A storey cut works on names, and a shared storey is named for " +
                    "nobody: without this, building " + onlyBuilding + "'s model carries the towers' own " +
                    "walls, columns and floor plates on every level they have in common.");
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

        // The bill for composing once and cutting afterwards: members that belong to a building
        // this model is not of are defined here and assigned to nothing. They come out.
        int orphanObjects = doc.DropObjectsWithNoAssign();
        int orphanPoints = doc.DropGeneratedOrphanPoints(referencePointNames);
        var saved = doc.ReadContents(summary.SourceSheetOfObject);

        // THE COUNTS HAVE TO BE THE COUNTS OF THE FILE, NOT OF THE COMPOSITION.
        //
        // Composing the whole site once and cutting afterwards left the summary describing what was
        // BUILT rather than what was KEPT. The YMCA report opened "63 storeys, 1,416 walls, 2,461
        // columns, 106 floors" beside a file holding 15, 294, 629 and 16 -- the first four numbers
        // an engineer reads, and every one of them a count of somebody else's building. The cuts
        // were disclosed further down; nobody reads further down when the top looks wrong.
        //
        // Recounted from the document itself, which is the only thing that can be right.
        summary = summary with
        {
            Walls = saved.Walls,
            Columns = saved.Columns,
            Floors = saved.Floors,
            Points = saved.Joints,
            Stories = saved.Storeys.Count,

            // AND THE DENOMINATOR INSIDE THE PROSE, not only the numbers at the top.
            //
            // "Slab thickness still ASSUMED: 5 of 90 floor plate(s)" is written during composition,
            // when there are 90; the building-C file that sentence is printed in holds 15 and the
            // site file holds 89. Both reports carried the identical "90", which is how you can
            // tell it describes neither of them. The count is corrected here, against the file, the
            // same as every other number above.
            Flags = summary.Flags
                .Select(f => Regex.Replace(
                    f,
                    @"(\bof\s+)[\d,]+(\s+floor plate\(s\))",
                    $"${{1}}{saved.Floors}${{2}}"))
                .ToList(),
        };
        if (orphanObjects > 0)
            summary = summary with
            {
                Flags = summary.Flags.Append(
                    $"{orphanObjects} generated object(s) belonged to storeys this model does not carry and " +
                    "were removed. The whole site is composed once and then cut to this building, so the " +
                    "members are made and then taken out rather than never made — which is what lets two " +
                    "models of one building agree about the storeys they share.").ToList(),
            };
        if (orphanPoints > 0)
            summary = summary with
            {
                Flags = summary.Flags.Append(
                    $"{orphanPoints} generated joint(s) were left behind by cut-away generated objects and " +
                    "were removed. Reference-model points are left alone; only KOR-generated KP joints are " +
                    "pruned by this cleanup.").ToList(),
            };

        // THE GAPS ARE COUNTED ON THE FILE SHE IS SENT, NOT ON THE COMPOSITION.
        //
        // The composer's own versions of these two lists are replaced here. They were written
        // before the cuts, so they described a model nobody receives, and they were written per
        // STOREY, so on a floor the engineer named twice they reported the plate on one name as
        // standing on air while its columns sat an inch and a half away on the other. Both flaws
        // reach the engineer's workbook as rows she cannot act on, and a workbook with rows like
        // that in it stops being read.
        // AND THE PINCHED PLATES ARE THE ONES STILL PINCHED IN THE FILE.
        //
        // A plate is measured for this while it is being composed, which is before spur removal
        // and the doubled-edge merge have finished with its outline. On 31168 that reported the
        // building-C ground floor as an outline closing through itself at (159, 406) ft; the plate
        // that shipped is a six-point wedge whose nearest two non-adjacent edges are 68 ft apart.
        // It was the only DEFECT row in her workbook, and it was not true of the file she had.
        //
        // So the measurement is repeated on what shipped, and an entry survives only if the finished
        // plate is still pinched within a foot of where it was. The sheet name comes from the
        // composition, which is the only place that knows it.
        if (summary.PinchedPlates.Count > 0)
        {
            var storeysOfPlate = doc.StoreysByObject();
            var platePoints = doc.PlanPointsOfObjects();
            var isPlate = doc.PlateNames();

            var stillPinched = new List<(string Storey, double X, double Y)>();
            foreach (string plate in isPlate)
            {
                if (!platePoints.TryGetValue(plate, out var pts) || pts.Count < 3) continue;
                if (!storeysOfPlate.TryGetValue(plate, out var storeys)) continue;

                var ring = pts.Select(p => new DxfPoint(p.X, p.Y)).ToList();
                if (E2kGeometryComposer.NarrowestSelfGap(ring) > composeOptions.SelfTouchReportGap) continue;

                var at = E2kGeometryComposer.SelfGapAt(ring);
                foreach (string storey in storeys)
                    stillPinched.Add((storey, at.X / 12.0, at.Y / 12.0));
            }

            var real = summary.PinchedPlates
                .Where(p => stillPinched.Any(s =>
                    s.Storey.Equals(p.Storey, StringComparison.OrdinalIgnoreCase)
                    && Math.Abs(s.X - p.AtXft) <= 1.0
                    && Math.Abs(s.Y - p.AtYft) <= 1.0))
                .ToList();

            if (real.Count > 0)
                summary = summary with
                {
                    Flags = summary.Flags.Append(
                        $"{real.Count} floor plate(s) have an outline that closes through itself: " +
                        string.Join(", ", real
                            .OrderBy(p => p.GapInches)
                            .Select(p => $"{p.Storey} — two edges {(p.GapInches < 0.5 ? "TOUCHING" : $"{p.GapInches / 12.0:0.0} ft apart")} " +
                                         $"at ({p.AtXft:0}, {p.AtYft:0}) ft, from {p.Sheet}")) +
                        ". A floor is a ring; where the ring meets itself the outline has closed through its " +
                        "own edge, and ETABS will mesh it badly or refuse it. Two wings joined at a point are " +
                        "usually two plates — which two is the drawing's answer, so this is reported and not " +
                        "repaired.").ToList(),
                };
        }

        // AND EVERY NOTE NAMES A STOREY THIS FILE HAS. See NotesAboutStoreysThisModelHas.
        summary = summary with
        {
            Flags = NotesAboutStoreysThisModelHas(
                summary.Flags, storiesBeforeCuts, doc.StoreyRenames,
                doc.ReadStories().Select(s => s.Name).ToList()),
        };

        warnings = NotesAboutStoreysThisModelHas(
            warnings, storiesBeforeCuts, doc.StoreyRenames,
            doc.ReadStories().Select(s => s.Name).ToList());

        var floorGaps = doc.FloorGapDetails();

        summary = summary with
        {
            Flags = summary.Flags
                .Where(f => !f.Contains("carry walls or columns and no floor plate", StringComparison.OrdinalIgnoreCase)
                            && !f.Contains("no wall or column beneath it", StringComparison.OrdinalIgnoreCase)
                            && !f.Contains("Floor does not reach the structure", StringComparison.OrdinalIgnoreCase))
                .Concat(floorGaps.FloorsWithNoPlate.Count == 0 ? Array.Empty<string>() : new[]
                {
                    $"{floorGaps.FloorsWithNoPlate.Count} storey(s) carry walls or columns and no floor plate at all, so they " +
                    $"have no diaphragm: {string.Join(", ", floorGaps.FloorsWithNoPlate)}. Nothing was borrowed or " +
                    "invented for them; add a plate if these storeys need one.",
                })
                .Concat(floorGaps.MostlyUncovered.Count == 0 ? Array.Empty<string>() : new[]
                {
                    $"{floorGaps.MostlyUncovered.Count} storey(s) have floor plate(s), but most of their " +
                    "walls and columns stand outside every plate on the floor: " +
                    $"{string.Join(", ", floorGaps.MostlyUncovered)}. This is measured from the finished " +
                    "file after cuts; it is a partial-coverage warning, not a no-diaphragm count.",
                })
                .Concat(floorGaps.PlatesWithNoSupport.Count == 0 ? Array.Empty<string>() : new[]
                {
                    $"{floorGaps.PlatesWithNoSupport.Count} storey(s) carry a floor plate with no wall or column beneath " +
                    $"it: {string.Join(", ", floorGaps.PlatesWithNoSupport)}. The plan placed there draws no vertical " +
                    "structure, so either the structure stops below that level or another sheet holds it.",
                })
                .ToList(),
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.OutputE2k))!);
        doc.Save(request.OutputE2k);

        // The sheet table is also a readback of what survived into the file, not what the
        // pre-cut composition placed.
        var sheetsAfterCut = SheetsAfterCut(outcomes, saved);

        return new DxfToEtabsReport(
            request.OutputE2k, files.Count, sheetsAfterCut.Count(s => s.Stories.Count > 0),
            saved.Storeys.Count,
            summary, offset, sheetsAfterCut, warnings, requested, composeFromReference)
        {
            RulesApplied = banked,
            FoundationStoreys = foundationStoreys,
            DeclinedCircleDiameters = declinedCircleDiameters,
            BuildingCut = request.TowerOnly,
            SavedModel = saved,
            PlatesByStorey = saved.PlatesByStorey,
            FloorsWiderThanTheirStructure = FloorsWiderThanTheirStructure(doc),
        };
    }

    private static IReadOnlyList<SheetOutcome> SheetsAfterCut(
        IReadOnlyList<SheetOutcome> before,
        E2kModelContents saved)
    {
        var bySheet = saved.Objects
            .Where(o => !string.IsNullOrWhiteSpace(o.SourceSheet))
            .GroupBy(o => o.SourceSheet!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    Storeys = g.SelectMany(o => o.Storeys)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    Walls = g.Count(o => o.Name.StartsWith("KW", StringComparison.Ordinal)
                                        && o.Kind.Equals("PANEL", StringComparison.OrdinalIgnoreCase)),
                    Columns = g.Count(o => o.Name.StartsWith("KC", StringComparison.Ordinal)
                                          && o.Kind.Equals("COLUMN", StringComparison.OrdinalIgnoreCase)),
                    Floors = g.Count(o => o.Name.StartsWith("KF", StringComparison.Ordinal)
                                        && o.Kind.Equals("FLOOR", StringComparison.OrdinalIgnoreCase)),
                },
                StringComparer.OrdinalIgnoreCase);

        return before
            .Select(s => bySheet.TryGetValue(s.File, out var kept)
                ? s with { Stories = kept.Storeys, Walls = kept.Walls, Columns = kept.Columns, Slabs = kept.Floors }
                : s with { Stories = Array.Empty<string>(), Walls = 0, Columns = 0, Slabs = 0 })
            .ToList();
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

    /// <summary>
    /// Whether the drawings and the model's grid lines share no ground at all.
    ///
    /// Deliberately "no overlap whatsoever" rather than "not centred". A drawing offset from its
    /// grids by a bay is a drawing with a bay's worth of drafting slop and must be left alone;
    /// one that does not touch them anywhere is in a different coordinate system.
    /// </summary>
    /// <summary>
    /// How much of a plate stands on ground some other plate already covers, by sampling.
    ///
    /// A grid of points inside the outline, counted against the others. Exact polygon union is a
    /// great deal more code for an answer this one is asked to give: is this the same floor drawn
    /// again, or a piece of floor nobody else drew.
    /// </summary>
    /// <summary>
    /// Whether a point is inside an outline, or near enough its edge to be part of it.
    ///
    /// The margin is what lets a perimeter wall count as on the floor it edges. Half of such a wall
    /// is outside the slab by construction, and a rule that asked for strict containment would take
    /// the outside face of every building it cut.
    /// </summary>
    private static bool WithinOrNear(
        (double X, double Y) point, IReadOnlyList<(double X, double Y)> outline, double margin)
    {
        var p = new DxfPoint(point.X, point.Y);
        var ring = outline.Select(q => new DxfPoint(q.X, q.Y)).ToList();

        if (ring.Count >= 3 && LoopGeometry.PointInPolygon(p, ring)) return true;

        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            if (LoopGeometry.PerpendicularDistance(p, a, b) <= margin) return true;
        }

        return false;
    }

    /// <summary>
    /// Rewrites the run's notes so that every storey they name is a storey the shipped file has.
    ///
    /// The notes are written while the whole site is being composed; the cuts come afterwards. So
    /// the building-C workbook told the engineer that B-LEVEL 28 and B-LEVEL 41 had been given a
    /// neighbour's floor plate and that B-LEVEL 1 had a slab edge touching itself — three storeys
    /// that are not in the file she was sent, one of them a tower two hundred feet away.
    ///
    /// A row she cannot act on is worse than no row: it teaches her that the rows are noise, and
    /// then the real ones are noise too.
    ///
    /// A cut RENAMES some storeys and REMOVES others, so both are applied here. A note left naming
    /// nothing that exists is dropped whole; one naming a mix keeps the part that survived.
    /// </summary>
    private static List<string> NotesAboutStoreysThisModelHas(
        IEnumerable<string> notes,
        IReadOnlyList<string> storeysBefore,
        IReadOnlyDictionary<string, string> renames,
        IReadOnlyCollection<string> storeysNow)
    {
        // BOUNDED AT BOTH ENDS, or a storey name is eaten out of the middle of another one.
        //
        // The guard was one-sided -- a lookahead only -- and ordering longest-first does not save
        // it, because the name being removed and the name being damaged are different storeys.
        // 31168 drops LEVEL 3 through LEVEL 10 and keeps C-LEVEL 3 through C-LEVEL 9, so removing
        // "LEVEL 4" from a note took the tail off "C-LEVEL 4" and left "C-". The engineer's own
        // slab-thickness table shipped on 28 August reading "C-: 8", C-: 7", C-: 9", C-ROOF: 9""
        // -- seven storeys she could not tell apart, in the model she was asked to check.
        const string NotPartOfALongerName = @"(?<![\w-])";

        var known = storeysBefore
            .Concat(renames.Values)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(s => s.Length)
            .ToList();

        var here = new HashSet<string>(storeysNow, StringComparer.OrdinalIgnoreCase);
        var kept = new List<string>();

        foreach (string note in notes)
        {
            string text = note;

            foreach (string name in known)
                if (renames.TryGetValue(name, out string? now))
                    text = Regex.Replace(text, NotPartOfALongerName + Regex.Escape(name) + @"(?![\w-])", now);

            var mentioned = known
                .Where(s => Regex.IsMatch(text, NotPartOfALongerName + Regex.Escape(s) + @"(?![\w-])"))
                .ToList();

            if (mentioned.Count == 0) { kept.Add(text); continue; }

            var gone = mentioned.Where(s => !here.Contains(s)).ToList();
            if (gone.Count == 0) { kept.Add(text); continue; }

            // Each dead storey takes with it the parenthetical that belongs to it — "B-LEVEL 28
            // (from B-LEVEL 27)" is one entry, not two — and the comma that separated it.
            foreach (string dead in gone)
                text = Regex.Replace(
                    text,
                    @",?\s*" + NotPartOfALongerName + Regex.Escape(dead) + @"(?![\w-])(\s*\([^)]*\))?",
                    string.Empty);

            bool anythingLeft = mentioned.Except(gone, StringComparer.OrdinalIgnoreCase)
                .Any(s => Regex.IsMatch(text, NotPartOfALongerName + Regex.Escape(s) + @"(?![\w-])"));

            if (anythingLeft) kept.Add(Regex.Replace(text, @":\s*,", ": ").Trim());
        }

        return kept;
    }

    /// <summary>
    /// Storeys whose floor plates reach well past the members standing on them.
    ///
    /// In a model cut to one building this is a shared floor: the parkade and the podium are
    /// drafted once for every building that stands on them, so a building's model gets the whole
    /// slab with its own columns under part of it. 31168's building C carries 76,967 sq ft of
    /// parkade and 66 columns spread over about half of it.
    ///
    /// Not a fault, and not repairable from the drawings — neither the BLDG C nor the WEST parkade
    /// sheet closes a slab edge, so there is no line to cut on. It is a question, and the ruling
    /// a-model-carries-only-its-own-elevations already records it as one: "shared structure
    /// genuinely under a building, such as the parkade below both towers, is a separate question
    /// this row does not settle."
    ///
    /// Measured by the box each holds, not by area, because a floor and the frame under it can be
    /// the same shape at very different sizes. Two and a half times is the threshold, set where the
    /// evidence is: 31168's building-C parkade holds a floor spanning 235 x 334 ft over columns
    /// spanning 100 x 292, which is 2.7. A floor half as wide again as its frame is a cantilever
    /// and says nothing; one that covers two and a half times its box is carrying somebody else.
    /// </summary>
    /// <summary>How many floor plates stand on each storey of the finished file.</summary>
    private static IReadOnlyDictionary<string, int> PlatesByStorey(E2kDocument doc)
    {
        var plates = doc.PlateNames();
        var count = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (obj, storeys) in doc.StoreysByObject())
        {
            if (!plates.Contains(obj)) continue;
            foreach (string storey in storeys)
                count[storey] = count.GetValueOrDefault(storey) + 1;
        }

        return count;
    }

    private static IReadOnlyList<string> FloorsWiderThanTheirStructure(E2kDocument doc)
    {
        var plates = doc.PlateNames();
        var planOf = doc.PlanPointsOfObjects();
        var storeysOf = doc.StoreysByObject();

        var floorBox = new Dictionary<string, (double MinX, double MaxX, double MinY, double MaxY)>(StringComparer.OrdinalIgnoreCase);
        var frameBox = new Dictionary<string, (double MinX, double MaxX, double MinY, double MaxY)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (obj, storeys) in storeysOf)
        {
            if (!planOf.TryGetValue(obj, out var pts) || pts.Count == 0) continue;

            // COLUMNS, not every member. A parkade's perimeter wall runs the full site whoever
            // owns the parkade, so a floor and a frame that both include it always match and the
            // question is never asked. A column stands where the frame actually is, and in the
            // e2k it is the object whose two joints share one plan position.
            bool isColumn = pts.Count == 2
                            && Math.Abs(pts[0].X - pts[1].X) < 0.01
                            && Math.Abs(pts[0].Y - pts[1].Y) < 0.01;

            if (!plates.Contains(obj) && !isColumn) continue;
            var into = plates.Contains(obj) ? floorBox : frameBox;

            foreach (string storey in storeys)
            {
                var box = into.TryGetValue(storey, out var had)
                    ? had
                    : (double.MaxValue, double.MinValue, double.MaxValue, double.MinValue);

                foreach (var p in pts)
                    box = (Math.Min(box.Item1, p.X), Math.Max(box.Item2, p.X),
                           Math.Min(box.Item3, p.Y), Math.Max(box.Item4, p.Y));

                into[storey] = box;
            }
        }

        static double AreaOf((double MinX, double MaxX, double MinY, double MaxY) b) =>
            Math.Max(0, b.MaxX - b.MinX) * Math.Max(0, b.MaxY - b.MinY);

        return doc.ReadStories()
            .Select(s => s.Name)
            .Where(s => floorBox.ContainsKey(s) && frameBox.ContainsKey(s))
            .Where(s => AreaOf(frameBox[s]) > 0 && AreaOf(floorBox[s]) >= AreaOf(frameBox[s]) * 2.5)
            .ToList();
    }

    private static double CoveredFraction(
        IReadOnlyList<DxfPoint> outline, IReadOnlyList<IReadOnlyList<DxfPoint>> others)
    {
        if (outline.Count < 3 || others.Count == 0) return 0;

        double minX = outline.Min(p => p.X), maxX = outline.Max(p => p.X);
        double minY = outline.Min(p => p.Y), maxY = outline.Max(p => p.Y);
        if (maxX - minX < 1e-6 || maxY - minY < 1e-6) return 0;

        const int steps = 24;
        int inside = 0, covered = 0;

        for (int i = 0; i < steps; i++)
        for (int j = 0; j < steps; j++)
        {
            var p = new DxfPoint(
                minX + (maxX - minX) * (i + 0.5) / steps,
                minY + (maxY - minY) * (j + 0.5) / steps);

            if (!LoopGeometry.PointInPolygon(p, outline)) continue;
            inside++;
            if (others.Any(o => LoopGeometry.PointInPolygon(p, o))) covered++;
        }

        return inside == 0 ? 0 : (double)covered / inside;
    }

    private static DxfPoint Centroid(IReadOnlyList<DxfPoint> points) =>
        new(points.Average(p => p.X), points.Average(p => p.Y));

    private static bool DrawingMissesTheGrid(
        E2kDocument doc, IReadOnlyList<PlanGeometrySet> sets, out string howFar)
    {
        howFar = string.Empty;

        var (gMinX, gMaxX, gMinY, gMaxY) = ReadGridExtents(doc);
        if (double.IsInfinity(gMinX) || double.IsInfinity(gMinY)) return false;

        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var set in sets)
        {
            foreach (var wall in set.Walls)
                foreach (var p in new[] { wall.Start, wall.End })
                {
                    minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                    minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                }
            foreach (var column in set.Columns)
            {
                minX = Math.Min(minX, column.Center.X); maxX = Math.Max(maxX, column.Center.X);
                minY = Math.Min(minY, column.Center.Y); maxY = Math.Max(maxY, column.Center.Y);
            }
            foreach (var slab in set.Slabs)
                foreach (var p in slab.Points)
                {
                    minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                    minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                }
        }

        if (minX > maxX) return false;

        // MEASURED AGAINST THE BUILDING'S OWN SIZE, not against zero.
        //
        // "Does not overlap the grids" is too eager. 31138's drawings sit 68 ft off its grid
        // extents on a building 170 ft across -- drafting slop and a grid that does not reach the
        // whole plan -- and shifting it broke a baseline that exists precisely to say this tool
        // does not move drawings. 31168's Revit export sits 3,200 ft from a building 340 ft
        // across. One is a drawing slightly off its grid; the other is a different coordinate
        // system, and the difference is a factor of ten.
        //
        // So: the centres must be further apart than the whole drawing is wide before anything is
        // moved. Below that, leave it alone -- being a little off the grid is what drawings are.
        double dxfCx = (minX + maxX) / 2.0, dxfCy = (minY + maxY) / 2.0;
        double gridCx = (gMinX + gMaxX) / 2.0, gridCy = (gMinY + gMaxY) / 2.0;

        double apart = Math.Sqrt((dxfCx - gridCx) * (dxfCx - gridCx) + (dxfCy - gridCy) * (dxfCy - gridCy));
        double drawingSize = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));

        if (drawingSize <= 0 || apart <= drawingSize) return false;

        howFar =
            $"their centres are {apart / 12:N0} ft apart and the whole drawing is only " +
            $"{drawingSize / 12:N0} ft across — the drawings span x {minX:0}..{maxX:0}, " +
            $"y {minY:0}..{maxY:0} in and the grids span x {gMinX:0}..{gMaxX:0}, y {gMinY:0}..{gMaxY:0}";
        return true;
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

    /// <summary>
    /// Every grid line the reference model carries, by direction. The extents are not enough to put
    /// a drawing on a grid: the SPACINGS are what identify it, because they are the building's and
    /// nothing else has them. See GridAlignment.
    /// </summary>
    private static (List<double> X, List<double> Y) ReadGridCoordinates(E2kDocument doc)
    {
        var x = new List<double>();
        var y = new List<double>();

        foreach (string raw in doc.LinesOf("GRIDS"))
        {
            string line = raw.Trim();
            if (!line.StartsWith("GRID ", StringComparison.OrdinalIgnoreCase)) continue;

            int dirAt = line.IndexOf("DIR \"", StringComparison.OrdinalIgnoreCase);
            int coordAt = line.IndexOf("COORD ", StringComparison.OrdinalIgnoreCase);
            if (dirAt < 0 || coordAt < 0) continue;

            string dir = line[(dirAt + 5)..];
            dir = dir[..Math.Max(dir.IndexOf('"'), 0)];

            string tail = line[(coordAt + 6)..].TrimStart();
            int end = tail.IndexOfAny(new[] { ' ', '\t' });
            if (end > 0) tail = tail[..end];
            if (!double.TryParse(tail, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double coord)) continue;

            (dir.Equals("X", StringComparison.OrdinalIgnoreCase) ? x : y).Add(coord);
        }

        x.Sort();
        y.Sort();
        return (x, y);
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
        bool hasSavedCounts = report.SavedModel.Storeys.Count > 0
            || report.SavedModel.Walls > 0
            || report.SavedModel.Columns > 0
            || report.SavedModel.Floors > 0
            || report.SavedModel.Joints > 0;

        var sb = new StringBuilder();
        sb.AppendLine($"Model written : {report.OutputPath}");
        sb.AppendLine($"Sheets read   : {report.SheetsRead}   placed: {report.SheetsPlaced}");
        sb.AppendLine($"Storeys built : {(hasSavedCounts ? report.SavedModel.Storeys.Count : report.StoriesPopulated)}");
        sb.AppendLine($"Walls         : {(hasSavedCounts ? report.SavedModel.Walls : report.Summary.Walls)}");
        sb.AppendLine($"Columns       : {(hasSavedCounts ? report.SavedModel.Columns : report.Summary.Columns)}");
        sb.AppendLine($"Floors        : {(hasSavedCounts ? report.SavedModel.Floors : report.Summary.Floors)}");
        sb.AppendLine($"Joints        : {(hasSavedCounts ? report.SavedModel.Joints : report.Summary.Points)}");
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
