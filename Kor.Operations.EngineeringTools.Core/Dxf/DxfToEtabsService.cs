using System.Globalization;
using System.Text;

namespace Kor.Operations.EngineeringTools.Dxf;

public sealed record DxfToEtabsRequest
{
    public required string DxfFolder { get; init; }

    /// <summary>An .e2k that ETABS itself exported from the target model — the source of storeys, grids and materials.</summary>
    public required string ReferenceE2k { get; init; }

    public required string OutputE2k { get; init; }

    /// <summary>Restrict to one building's sheets, e.g. "B" for a "BLDG B" tower.</summary>
    public string? BuildingTag { get; init; }

    /// <summary>
    /// Cut the model down to one tower: its storeys and the shared podium ones, with the other
    /// towers' storeys removed so none of them stands empty.
    /// </summary>
    public string? TowerOnly { get; init; }

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
    IReadOnlyList<string> Stories, int Walls, int Columns, int Slabs, IReadOnlyList<string> Flags);

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
        var doc = E2kDocument.Load(request.ReferenceE2k);

        // Before anything else: the storey list is what ETABS builds from, and an export parks the
        // base a thousand feet under the building with the whole distance folded into the lowest
        // storey. Left alone, every member down there is extruded that far on import.
        bool baseNormalised = doc.NormaliseBaseStorey();

        // A model of one tower carries only that tower's storeys. On a site model the others stand
        // empty, which is what the engineer saw: "some levels don't exist, they're blank."
        var droppedStoreys = request.TowerOnly is null
            ? Array.Empty<string>()
            : doc.KeepOnlyTower(request.TowerOnly).ToArray();

        // Drafting can issue parkade levels the model has never had. On 31138 the drawings go to
        // LEVEL P5 and the model stopped at P3, so two whole floors were read and placed nowhere —
        // "the model needs to go to P5". The storeys are added below the lowest parkade level at the
        // height that parkade already uses, and the base drops by the same amount.
        var addedStoreys = Array.Empty<string>();
        if (request.AddMissingParkadeStoreys)
        {
            var wanted = Directory.EnumerateFiles(request.DxfFolder, "*.dxf", SearchOption.TopDirectoryOnly)
                .SelectMany(f => PlanSheetNaming.Parse(f).ParkadeLevels)
                .Distinct()
                .ToList();
            addedStoreys = doc.AddParkadeStoreysBelow(wanted).ToArray();
        }

        var stories = doc.ReadStories();
        if (stories.Count == 0)
            throw new InvalidOperationException("The reference model lists no storeys.");

        var storyNames = stories.Select(s => s.Name).ToList();
        var byName = stories.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

        var files = Directory.EnumerateFiles(request.DxfFolder, "*.dxf", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

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
            ? RuleSettings.LoadRequired(request.RuleSettingsConnection, builtIn.Keys)
            : new Dictionary<string, RuleSetting>(StringComparer.OrdinalIgnoreCase);
        warnings.AddRange(RuleSettings.Describe(banked, builtIn));

        var requested = ApplyRules(request.Classification, banked);

        double scale = drawingUnit.Value / modelUnitInInches;
        var composeFromReference = ApplyRules(request.Compose, banked);
        var classification = Math.Abs(modelUnitInInches - 1.0) < 1e-9
            ? requested
            : requested.InUnitOf(modelUnitInInches);

        if (Math.Abs(scale - 1.0) > 1e-9)
            warnings.Add($"The drawings are {Describe(drawingUnit.Value)} and the model is " +
                         $"{Describe(modelUnitInInches)}, so every coordinate was scaled by {scale:0.######}.");
        var outcomes = new List<SheetOutcome>();
        var parsed = new List<(PlanSheetInfo Sheet, PlanGeometrySet Geometry, IReadOnlyList<string> Stories)>();
        var readSheets = new List<IReadOnlyList<DxfSegment>>();

        foreach (string file in files)
        {
            var sheet = PlanSheetNaming.Parse(file);

            if (request.BuildingTag is not null &&
                sheet.BuildingTags.Count > 0 &&
                !sheet.BuildingTags.Contains(request.BuildingTag, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var segments = DxfPlanReader.ReadSegments(file);
            var unsupported = DxfPlanReader.UnsupportedStructuralEntities(file, classification);
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
                warnings.Add($"{sheet.FileName}: {total:N0} unreadable DXF entit{(total == 1 ? "y" : "ies")} " +
                             $"carrying shape, not read: {examples}. " +
                             (anyClaimed
                                ? "Some sit on layers this tool reads, so that geometry is missing from the model."
                                : "None sits on a layer this tool reads, so if any of it is structure, the model does not have it."));
            }

            if (Math.Abs(scale - 1.0) > 1e-9)
                segments = segments.Select(g => new DxfSegment(g.Layer,
                    new DxfPoint(g.Start.X * scale, g.Start.Y * scale),
                    new DxfPoint(g.End.X * scale, g.End.Y * scale)) { FromCurve = g.FromCurve }).ToList();

            readSheets.Add(segments);
            var geometry = StructuralPlanClassifier.Classify(segments, classification);
            var matched = PlanSheetNaming.MatchStories(sheet, storyNames);

            outcomes.Add(new SheetOutcome(
                sheet.FileName, sheet.Label, sheet.BuildingTag, sheet.Levels, matched,
                geometry.Walls.Count, geometry.Columns.Count, geometry.Slabs.Count, geometry.Flags));

            if (matched.Count == 0)
            {
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

        warnings.AddRange(FarFromOriginWarnings(parsed.Select(p => p.Geometry), offset));

        var placements = new List<StoryPlacement>();
        foreach (var (sheet, geometry, matched) in parsed)
            foreach (string storyName in matched)
                if (byName.TryGetValue(storyName, out var story))
                    placements.Add(new StoryPlacement(story, geometry, sheet.FileName));

        var composeOptions = (Math.Abs(modelUnitInInches - 1.0) < 1e-9 ? composeFromReference : composeFromReference.InUnitOf(modelUnitInInches))
            with { OffsetX = offset.X, OffsetY = offset.Y };
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
        sb.AppendLine($"Sections made : {string.Join(", ", report.Summary.Sections)}");
        sb.AppendLine($"Offset applied: {report.AppliedOffset.X:0.#}, {report.AppliedOffset.Y:0.#} in");
        sb.AppendLine();

        sb.AppendLine("Sheet                                                  Lvls  Storeys  Walls  Cols  Slabs");
        foreach (var s in report.Sheets.OrderBy(s => s.File, StringComparer.OrdinalIgnoreCase))
        {
            string name = s.File.Length <= 52 ? s.File.PadRight(52) : s.File[..49] + "...";
            sb.AppendLine($"{name}  {s.Levels.Count,4}  {s.Stories.Count,7}  {s.Walls,5}  {s.Columns,4}  {s.Slabs,5}");
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
            var perSheet = report.Summary.Flags.Where(f => f.Contains(".dxf:", StringComparison.OrdinalIgnoreCase)).ToList();

            if (wholeModel.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("About the model as a whole:");
                foreach (string f in wholeModel) sb.AppendLine("  - " + f);
            }

            sb.AppendLine();
            sb.AppendLine($"Flags ({perSheet.Count}) — outlines that needed judgement:");
            foreach (string f in perSheet.Take(40)) sb.AppendLine("  - " + f);
            if (perSheet.Count > 40) sb.AppendLine($"  ... and {perSheet.Count - 40} more");
        }

        return sb.ToString();
    }
}
