#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// THE TRIAGE OF THE READERS' COMPILED NUMBERS (completion plan WP5, 2026-09-11), as a gate rather
/// than a table in a document: every <c>const double|int</c> in the intake's readers is one of —
/// <b>Row</b>: read from a KorStandards row, whose key is named here and must exist in the code;
/// <b>Convention</b>: a drafting convention that belongs in a row and is still compiled — the debt,
/// counted; <b>Tolerance</b>: slack against drafting and floating-point precision, code by design;
/// <b>Rule</b>: a fact of geometry or of buildings, code by design; <b>Elsewhere</b>: another
/// product's setting that happens to share a file (the SAFE/WPF side); <b>Dead</b>: referenced by
/// nothing, and the test fails until it is deleted. A constant added without a line here fails the
/// test; a constant deleted without removing its line fails the test.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: the set of constants (the scan is the same one <c>tools/list_reader_constants.py</c>
/// makes), each named, each classed, a Row's key present in the code, a Dead one gone. WHAT IT DOES
/// NOT: whether a Row is READ where the constant was used (CompiledDefaultsAreTheBankedRowsTests
/// holds each row's value to its compiled default; the six-set gate holds the behaviour); whether
/// a Tolerance is the right size; a number written inline without <c>const</c>.
/// </remarks>
public sealed class EveryReaderConstantIsTriagedTests
{
    private enum Class { Row, Convention, Tolerance, Rule, Elsewhere, Dead }

    private sealed record Triage(Class Class, string Why, string? Key = null);

    private static Triage Row(string key, string why) => new(Class.Row, why, key);
    private static Triage Convention(string why) => new(Class.Convention, why);
    private static Triage Tolerance(string why) => new(Class.Tolerance, why);
    private static Triage Rule(string why) => new(Class.Rule, why);
    private static Triage Elsewhere(string why) => new(Class.Elsewhere, why);
    private static Triage Dead(string why) => new(Class.Dead, why);

    // THE TABLE. File:Name → what it is. A convention names the row it should become.
    /// <summary>The constants classed Rule that name a code or standard (NBC, BCBC, CSA): WP7's gate holds each to a knowledge.RuleClause row.</summary>
    internal static IReadOnlyList<(string Name, string Why)> RulesNamingACode()
        => Table.Where(kv => kv.Value.Class == Class.Rule && Regex.IsMatch(kv.Value.Why, @"\b(NBC|BCBC|CSA)\b")).Select(kv => (kv.Key, kv.Value.Why)).ToList();

    private static readonly IReadOnlyDictionary<string, Triage> Table = new Dictionary<string, Triage>(StringComparer.Ordinal)
    {
        // ---- rows already (the value in code is the compiled default of a banked or declared row) ----
        ["PdfIntakeOptions.cs:DefaultMinWallThicknessMm"] = Row("dxf.min-wall-thickness", "shared with the DXF side"),
        ["PdfIntakeOptions.cs:DefaultMaxWallThicknessMm"] = Row("dxf.max-wall-thickness", "shared with the DXF side"),
        ["PdfIntakeOptions.cs:DefaultMinWallLengthMm"] = Row("dxf.min-wall-length", "shared with the DXF side"),
        ["PdfIntakeOptions.cs:DefaultMinWallAspect"] = Row("dxf.min-wall-aspect", "shared with the DXF side"),
        ["GeometryFilterService.cs:DefaultMaxColumnAspect"] = Row("dxf.max-column-aspect", "shared with the DXF side"),
        ["PdfToSafeConstants.cs:DefaultSlabMinDiagonalMm"] = Row("dxf.pdf.slab-min-diagonal-mm", "PdfIntakeOptions"),
        ["PdfToSafeConstants.cs:DefaultLineMinLengthMm"] = Row("dxf.pdf.line-min-length-mm", "PdfIntakeOptions"),
        ["StoreysFromPlans.cs:DefaultAssumedStoreyHeightMm"] = Row("dxf.pdf.assumed-storey-height-mm", "migration 084"),
        ["GeometryFilterService.cs:DefaultSlabEdgeBridgeMm"] = Row("dxf.bridge-tolerance", "the DXF side's own bridge, 6 in on both sides; PdfIntakeOptions.SlabEdgeBridgeMm (WP5)"),
        ["GeometryFilterService.cs:DefaultMinSlabAreaMm2"] = Row("dxf.min-plate-area", "the DXF side's MinPlateArea, 400 sq ft on both sides; PdfIntakeOptions.MinSlabAreaMm2 (WP5)"),
        ["FootingOutlines.cs:DefaultDashGapMm"] = Row("dxf.dash-join-gap", "the DXF side's dash-join gap, 14 in on both sides; PdfIntakeOptions.DashGapMm (WP5)"),
        ["PdfIntakeOptions.cs:DefaultFallbackScale"] = Row("dxf.pdf.fallback-scale", "the office's plans are 1/8\" = 1'-0\" when a sheet states nothing; migration 085"),
        ["StoreyLadder.cs:DefaultMinRows"] = Row("dxf.pdf.ladder-min-rows", "fewer rows is a caption; migration 085"),

        // ---- conventions still compiled: each names the row it becomes (the debt WP5 counts) ----
        ["GeometryFilterService.cs:SlabEdgeExtendMm"] = Convention("dxf.pdf.slab-edge-extend-mm — 48 in here; NOT the DXF side's dxf.extend-limit, which measured extending as harmful and banks 0"),
        ["SheetViews.cs:TitleRegionMinFx"] = Convention("dxf.pdf.title-block-min-fx — the title block is the right 20% of the sheet; ONE definition for five readers is owed first, then the row"),
        ["TitleBlockFields.cs:RegionMinFx"] = Convention("dxf.pdf.title-block-min-fx — a copy"),
        ["ViewCaptions.cs:TitleRegionMinFx"] = Convention("dxf.pdf.title-block-min-fx — a copy"),
        ["SheetScaleReader.cs:TitleRegionMinFx"] = Convention("dxf.pdf.title-block-min-fx — a copy"),
        ["SheetTitleReader.cs:TitleRegionMinFx"] = Convention("dxf.pdf.title-block-min-fx — a copy"),
        ["GeometryFilterService.cs:DoorwayMinLengthMm"] = Convention("dxf.pdf.doorway-min-length-mm — 18 in: narrower is a slot or a text mask"),
        ["GeometryFilterService.cs:PierMinLengthMm"] = Convention("dxf.pdf.pier-min-length-mm — 12 in; the DXF side's panel floor"),
        ["GeometryFilterService.cs:SheetFrameMinShare"] = Convention("dxf.pdf.sheet-frame-min-share — a closed shape over 60% of the page is the frame"),
        ["GeometryFilterService.cs:SlabEdgeChainMinMm"] = Convention("dxf.pdf.slab-edge-chain-min-mm — shorter is a tick or a letter"),
        ["GeometryFilterService.cs:FaceTraceMinOverlapMm"] = Convention("dxf.pdf.face-trace-min-overlap-mm"),
        ["GeometryFilterService.cs:FaceLeftoverMinMm"] = Tolerance("a wall face's piece beyond its panel shorter than this stays the join tolerance's business, not the arrangement's (step 124)"),
        ["GeometryFilterService.cs:BarMarkOnLineHeights"] = Tolerance("a bar's label sits on its bar: the label's centre within this many text heights of the line; a leader's or a dimension's text stands a height or more off (step 126)"),
        ["GeometryFilterService.cs:BarRunMinMm"] = Rule("a bar run is 600 mm or longer; a mark beside a shorter stroke labels a tick or a leader (step 126)"),
        ["GeometryFilterService.cs:HookTickMaxMm"] = Tolerance("a bar's hook is a stroke of 400 mm or less: 31017's and 30838's hooks are 150-300 mm at 1:96 (step 127)"),
        ["GeometryFilterService.cs:HookedRunMinMm"] = Rule("a hooked run is 1,500 mm or longer; a slab edge's notch is short-long-short too and its run is under this (step 127)"),
        ["GeometryFilterService.cs:HookTouchMm"] = Tolerance("a hook's end and the run's end are one point within 10 mm (step 127)"),
        ["GeometryFilterService.cs:HookCosine"] = Tolerance("a hook turns within 15 degrees of a right angle - the cosine (step 127)"),
        ["GeometryFilterService.cs:DimensionOnLineHeights"] = Tolerance("a dimension's number sits on its line: centre within 1.5 text heights; that line is a dimension, not a bar (step 127)"),
        ["GeometryFilterService.cs:LeakRasterMm"] = Tolerance("the leak finder's raster (an instrument behind FaceTrace): where the outside gets into a ring, to two cells"),
        ["GeometryFilterService.cs:XMarkMinArmMm"] = Rule("an X that marks an opening has arms of 2 m or more: a shaft's are 11-12 ft on 31130, 31138 and 31202; a symbol's are under a metre (step 104)"),
        ["GeometryFilterService.cs:XMarkBoxedMinArmMm"] = Rule("a boxed X (a rectangle with its diagonals) marks a sleeve down to 600 mm arms: her sleeves' arms are 0.7-1.2 m, a symbol's under that (step 111)"),
        ["GeometryFilterService.cs:XMarkMaxArmMm"] = Rule("an X that marks an opening has arms of 30 ft or less: 31202's 109 ft X spans a region labelled 9 in SLAB, a region mark (step 104)"),
        ["GeometryFilterService.cs:TreadMinWidthMm"] = Rule("a stair flight is 900 mm and more wide - the building code's stair (NBC 9.8.2: 860 mm in a dwelling, 1,100 mm elsewhere), not a drafting choice; 31202 draws its treads 1,166-1,187 mm (step 105)"),
        ["GeometryFilterService.cs:TreadMaxWidthMm"] = Rule("and 1,700 mm and less: wider is a corridor or a ramp, not a flight (step 105)"),
        ["GeometryFilterService.cs:TreadMinDepthMm"] = Rule("a tread's going is 220 mm and more - the code's run (NBC 9.8.4: 255 mm minimum), drawn to it (31202: 276-280 mm) (step 105)"),
        ["GeometryFilterService.cs:TreadMaxDepthMm"] = Rule("and 340 mm and less: deeper is a landing or a step, not a tread (step 105)"),
        ["GeometryFilterService.cs:FlightMinTreads"] = Rule("a flight is five treads and more; fewer is a step or a symbol (step 105)"),
        ["GridBubbles.cs:MinRadiusPts"] = Convention("dxf.pdf.bubble-min-radius-pt — how small a grid bubble is drawn"),
        ["GridBubbles.cs:MaxRadiusPts"] = Convention("dxf.pdf.bubble-max-radius-pt"),
        ["GridBubbles.cs:AxisMinShare"] = Convention("dxf.pdf.axis-min-share — a grid axis crosses at least a quarter of the page"),
        ["SheetViews.cs:AxisReachMm"] = Convention("dxf.pdf.axis-reach-mm — the bubble stands off the plan by up to 3 m"),
        ["TextBaselines.cs:SameLineFraction"] = Tolerance("two words are on one baseline within a quarter of their height (step 81): a typesetting fact, not a drafting convention - a dash sits a quarter-height up and stays its own line"),
        ["SheetFurniture.cs:MatchLineMinSpanShare"] = Convention("dxf.pdf.match-line-min-span-share"),
        ["SheetFurniture.cs:MatchLineLabelReachHeights"] = Convention("dxf.pdf.match-line-label-reach-heights"),
        ["SheetFurniture.cs:TitleBlockRuleMinShare"] = Convention("dxf.pdf.title-block-rule-min-share"),
        ["SheetFurniture.cs:TitleBlockMaxShare"] = Convention("dxf.pdf.title-block-max-share — a title block is a strip"),
        ["SheetFurniture.cs:FurnitureMaxShare"] = Convention("dxf.pdf.furniture-max-share"),
        ["SheetFurniture.cs:TitleMaxWords"] = Convention("dxf.pdf.title-max-words — a title is a short line"),
        ["SheetFurniture.cs:NorthArrowMinPts"] = Convention("dxf.pdf.north-arrow-min-pt"),
        ["SheetFurniture.cs:NorthArrowMaxPts"] = Convention("dxf.pdf.north-arrow-max-pt"),
        ["SheetTitleReader.cs:SheetNumMinFx"] = Convention("dxf.pdf.sheet-number-min-fx — the number sits in the bottom-right corner"),
        ["SheetTitleReader.cs:SheetNumMaxFy"] = Convention("dxf.pdf.sheet-number-max-fy"),
        ["SheetTitleReader.cs:SheetNumMinH"] = Convention("dxf.pdf.sheet-number-min-height-pt"),
        ["SheetTitleReader.cs:TitleMinH"] = Convention("dxf.pdf.title-min-height-pt"),
        ["SheetScaleReader.cs:ScaleFieldMaxFy"] = Convention("dxf.pdf.scale-field-max-fy"),
        ["WallTypeTagging.cs:ReachMm"] = Convention("dxf.pdf.wall-tag-reach-mm — how far from a wall's axis its tag stands"),
        ["WallTypeTagging.cs:TaggingSheetMinTags"] = Convention("dxf.pdf.tagging-sheet-min-tags"),
        ["WallTypeTagging.cs:WoodPlanMinUnfilledPairs"] = Tolerance("how many unfilled pairs make a sheet a wood plan (20): a count floor on the measured share, as TaggingSheetMinTags is"),
        ["WallPiers.cs:DefaultPierMinLongSideMm"] = Row("dxf.pdf.pier-min-long-side-mm", "24 in: her ruling W1, walls 24 in and over are walls - a filled rectangle past it that no schedule declares is a pier (migration 094)"),
        ["WallPiers.cs:DefaultPierMinAspect"] = Row("dxf.pdf.pier-min-aspect", "twice as long as thick: a 12 x 24 is a column she schedules, a 14 x 40 is a pier (migration 094)"),
        ["WallTypeTagging.cs:DefaultUnfilledWallMinThicknessMm"] = Row("dxf.pdf.unfilled-wall-min-thickness-mm", "8 in: on a wood plan an unfilled line pair is a concrete wall only at a retaining wall's thickness (step 67; the row since migration 091, read since step 72)"),
        ["MarkupList.cs:MeasurementReachMm"] = Convention("dxf.pdf.markup-measurement-reach-mm"),
        ["MarkupList.cs:MemberReachMm"] = Convention("dxf.pdf.markup-member-reach-mm"),
        ["MarkupList.cs:TickMaxPts"] = Convention("dxf.pdf.markup-tick-max-pt"),
        ["MarkupList.cs:RingMinPts"] = Convention("dxf.pdf.markup-ring-min-pt"),
        ["MarkupList.cs:RingMaxPts"] = Convention("dxf.pdf.markup-ring-max-pt"),
        ["MarkupList.cs:RingMaxAspect"] = Convention("dxf.pdf.markup-ring-max-aspect"),
        ["MarkupReconcile.cs:ReplyReachPts"] = Convention("dxf.pdf.markup-reply-reach-pt — a reply sits within an inch of what it answers"),
        ["TendonAnchors.cs:LabelReachHeights"] = Convention("dxf.pdf.tendon-label-reach-heights — a force label sits within four of its heights of its tendon"),
        ["TendonAnchors.cs:MinTendonLengthMm"] = Convention("dxf.pdf.tendon-min-length-mm — a tendon spans a bay at least"),
        ["ScheduleGridReader.cs:ColDimMinMm"] = Convention("dxf.pdf.schedule-column-dim-min-mm — a scheduled column dimension is between these"),
        ["ScheduleGridReader.cs:ColDimMaxMm"] = Convention("dxf.pdf.schedule-column-dim-max-mm"),
        ["DxfExporter.cs:nameHeightMm"] = Convention("dxf.pdf.dxf-name-height-mm — the text height the DXF outlet writes grid names at"),
        ["GridAlignment.cs:NameReachHeights"] = Convention("dxf.grid-name-reach-heights — a grid name sits within this many heights of the line's end (DXF side)"),
        ["GridAlignment.cs:NameReachFloor"] = Convention("dxf.grid-name-reach-floor"),
        ["ModelQuestionnaire.cs:MinConcreteThickness"] = Convention("dxf.min-concrete-thickness — 6 in (DXF side)"),
        ["E2kDocument.cs:maxPlausibleStoreyHeight"] = Convention("dxf.max-plausible-storey-height — 480 in (DXF side)"),

        // ---- tolerances: slack against drafting and precision, code by design ----
        ["DimensionStrings.cs:AgreeMm"] = Tolerance("an inch of drafting"),
        ["FootingOutlines.cs:SizeToleranceMm"] = Tolerance("a drawn side against the scheduled size"),
        ["FootingOutlines.cs:CollinearMm"] = Tolerance("pieces of one side"),
        ["FootingOutlines.cs:MinPieces"] = Rule("a dashed side has at least three pieces"),
        ["SetCheck.cs:GridDisagreeMm"] = Tolerance("a grid axis against the reference sheet"),
        ["SetStoreys.cs:AgreeMm"] = Tolerance("two sheets stating one storey"),
        ["StoreyAgreement.cs:WithinMm"] = Tolerance("two sources stating one storey"),
        ["SheetDiff.cs:ColumnMatchMm"] = Tolerance("the differential: what counts as the same column"),
        ["SheetDiff.cs:ColumnMovedMm"] = Tolerance("the differential"),
        ["SheetDiff.cs:SizeChangedMm"] = Tolerance("the differential"),
        ["SheetDiff.cs:WallAxisMm"] = Tolerance("the differential"),
        ["SheetDiff.cs:WallOverlapShare"] = Tolerance("the differential"),
        ["SheetDiff.cs:WallLengthChangedMm"] = Tolerance("the differential"),
        ["SheetDiff.cs:WallThicknessMm"] = Tolerance("the differential"),
        ["SheetDiff.cs:WallAngleDegrees"] = Tolerance("the differential"),
        ["SheetDiff.cs:FootingMatchMm"] = Tolerance("the differential"),
        ["SheetDiff.cs:FootingSizeChangedMm"] = Tolerance("the differential"),
        ["SheetDiff.cs:GridMovedMm"] = Tolerance("the differential"),
        ["SheetDiff.cs:StoreyHeightChangedMm"] = Tolerance("the differential"),
        ["WallTypeTagging.cs:hand"] = Tolerance("a hand's width"),
        ["TendonAnchors.cs:EndSlackMm"] = Tolerance("a tendon's end inside a column's footprint, with slack"),
        ["TendonAnchors.cs:PieceLateralMm"] = Tolerance("pieces of one tendon lie on one line within this"),
        ["TendonAnchors.cs:PieceGapMm"] = Convention("dxf.pdf.tendon-piece-gap-mm — a tendon's pieces are broken for labels and chair marks up to this gap"),
        ["TendonAnchors.cs:PostTensionedSheetMinLabels"] = Convention("dxf.pdf.tendon-sheet-min-labels — three force labels make a P/T plan"),
        ["TendonAnchors.cs:EndOvershootMm"] = Tolerance("a tendon's run may stop this far past its anchor - the leader stub on its own line"),
        ["SheetFurniture.cs:AxisPenHeavierBy"] = Tolerance("a stroke on a grid axis heavier than this many times the grid's pen at its bubble is not the grid"),
        ["DxfExporter.cs:minSeg"] = Tolerance("an alias of MinVertexDistanceMm"),
        ["GeometryFilterService.cs:WallLimitSlackMm"] = Tolerance("half an inch on the wall limits: a wall drawn at exactly the limit is a wall"),
        ["GeometryFilterService.cs:WallFloorSlackMm"] = Tolerance("half an inch under the wall floor: a six-inch wall is drawn at 142-150 mm on 31065; an eighth refused 60 of its walls (step 63)"),
        ["GeometryFilterService.cs:RectangleCornerCos"] = Tolerance("a corner is square within 3 degrees"),
        ["GeometryFilterService.cs:RectangleFillShare"] = Tolerance("a rectangle fills its own box"),
        ["GeometryFilterService.cs:CellAbutMm"] = Tolerance("an inch of drafting"),
        ["GeometryFilterService.cs:PieceLateralMm"] = Tolerance("a pen's width"),
        ["GeometryFilterService.cs:FaceParallelCos"] = Tolerance("parallel within a degree"),
        ["GeometryFilterService.cs:FaceTaperMm"] = Tolerance("faces converging"),
        ["GeometryFilterService.cs:FaceTaperShare"] = Tolerance("faces converging"),
        ["GeometryFilterService.cs:PatternSpacingShare"] = Tolerance("a pattern's third line at the pair's spacing"),
        ["GeometryFilterService.cs:PenMatchShare"] = Tolerance("the cut pen's width"),
        ["GeometryFilterService.cs:AcrossCos"] = Tolerance("perpendicular within 10 degrees"),
        ["GeometryFilterService.cs:RiserGapShare"] = Tolerance("a riser spans the gap"),
        ["GeometryFilterService.cs:LighterBetweenMax"] = Tolerance("lighter lines between a pair's faces"),
        ["GeometryFilterService.cs:CoverSamples"] = Tolerance("points sampled along an axis"),
        ["GeometryFilterService.cs:SlabEdgeJoinMm"] = Tolerance("a PDF's own coordinates meet exactly"),
        ["GeometryFilterService.cs:SlabEdgeNeighbourhoodShare"] = Tolerance("a ring judged against its neighbourhood"),
        ["GridBubbles.cs:RoundnessTolerance"] = Tolerance("a point on the circle"),
        ["GridBubbles.cs:AxisTolerancePts"] = Tolerance("a rule through a bubble's centre"),
        ["GridBubbles.cs:SameRulePts"] = Tolerance("a drafter's jitter"),
        ["PdfToSafeConstants.cs:MinVertexDistanceMm"] = Tolerance("vertex thinning"),
        ["PdfToSafeConstants.cs:DouglasPeuckerEpsilonMm"] = Tolerance("path simplification"),
        ["PdfToSafeConstants.cs:BezierSegments"] = Tolerance("curve tessellation"),
        ["SheetFurniture.cs:MatchLinePieceGapPts"] = Tolerance("a dash of a dashed line"),
        ["SheetScaleReader.cs:ValueReachPt"] = Tolerance("the value just right of its label"),
        ["SheetScaleReader.cs:BaselineTolPt"] = Tolerance("one baseline"),
        ["ScheduleGridReader.cs:PhraseGapPts"] = Tolerance("a space, not a column gap"),
        ["ScheduleGridReader.cs:LadderColumnPts"] = Tolerance("one column of a ladder"),
        ["ScheduleTableBorder.cs:LineTolerancePts"] = Tolerance("tokens on one line"),
        ["ScheduleTableBorder.cs:StraightPts"] = Tolerance("a rule deviates less than this"),
        ["ScheduleTableBorder.cs:MinPiecePts"] = Tolerance("glyph strokes are not rules"),
        ["ScheduleTableBorder.cs:AbutPts"] = Tolerance("collinear pieces are one rule"),
        ["ScheduleTableBorder.cs:SideSlackPts"] = Tolerance("a side vertical outside the top rule's span"),
        ["ScheduleTableBorder.cs:TouchPts"] = Tolerance("a vertical touching the box"),
        ["ScheduleTableBorder.cs:MinHeightPts"] = Tolerance("a box shorter than this is an underline"),
        ["ScheduleTableBorder.cs:MinRowHeightPts"] = Tolerance("a band holding no text"),
        ["ScheduleTableBorder.cs:TopRuleMinReachPts"] = Tolerance("the top rule under the title"),
        ["ScheduleTableBorder.cs:TopRuleMinLengthPts"] = Tolerance("a table, not a leader"),
        ["ScheduleTableBorder.cs:ColumnRuleMinShare"] = Tolerance("a vertical spanning the table"),
        ["ScheduleTableBorder.cs:TitleLinesReach"] = Tolerance("a table's top rule under its title"),
        ["StickFileSlabThicknessReader.cs:TitleWrapDropPt"] = Tolerance("a title's continuation"),
        ["StickFileSlabThicknessReader.cs:TitleWrapColumnPt"] = Tolerance("a title's continuation"),
        ["StructuralPlanClassifier.cs:Near"] = Tolerance("a hundredth of a unit: a loop vertex on a curve end (audit F12, by distance)"),
        ["TriangleTwins.cs:SameVertexMm"] = Tolerance("a hundredth of a millimetre: two triangles share a vertex a driver wrote twice (step 61)"),
        ["StructuralPlanClassifier.cs:SizeSlack"] = Tolerance("half an inch on the size limits (DXF side)"),
        ["StructuralPlanClassifier.cs:LengthSlack"] = Tolerance("half an inch on the length (DXF side)"),
        ["WallNetwork.cs:ParallelDegrees"] = Tolerance("centrelines parallel"),
        ["WallNetwork.cs:ReachFactor"] = Tolerance("a centreline carried past its end"),
        ["WallNetwork.cs:SnapTolerance"] = Tolerance("ends this close are one joint"),
        ["WallOutlineDecomposer.cs:ParallelDot"] = Tolerance("opposite faces of one wall"),
        ["WallOutlineDecomposer.cs:ThicknessSlack"] = Tolerance("CAD export drift"),
        ["WallOutlineDecomposer.cs:slack"] = Tolerance("CAD export drift"),
        ["WallOutlineDecomposer.cs:step"] = Tolerance("a sampling step"),
        ["GridAlignment.cs:SamePosition"] = Tolerance("the same line drawn twice"),
        ["GridAlignment.cs:Tolerance"] = Tolerance("grid coordinates are drafted"),
        ["GridAlignment.cs:NameTolerance"] = Tolerance("a plan read at 1:96 resolves to an inch"),
        ["AnnotationOverlay.cs:reach"] = Tolerance("the rigid transform between two exports"),
        ["AnnotationOverlay.cs:step"] = Tolerance("the rigid transform between two exports"),
        ["AnnotationOverlay.cs:agree"] = Tolerance("the rigid transform between two exports"),
        ["DxfPlanReader.cs:ArcChordTolerance"] = Tolerance("an arc as segments"),
        ["DxfToEtabsService.cs:StandDownReachInches"] = Tolerance("a hand's width from a partition's footprint"),
        ["DxfToEtabsService.cs:FarFromOrigin"] = Tolerance("a coordinate that is a survey coordinate"),
        ["E2kDocument.cs:duplicateFloorTolerance"] = Tolerance("two floors at one elevation"),
        ["E2kGeometryComposer.cs:Thin"] = Tolerance("floating point"),
        ["LoopGeometry.cs:Slack"] = Tolerance("floating point: the T-touch"),
        ["MatchLineSheetJoin.cs:DefaultTolerance"] = Tolerance("two match lines that are one seam"),
        ["ModelYardstick.cs:bin"] = Tolerance("the residual histogram's bin"),
        ["CorpusAnalyzer.cs:CurrentYardstickDays"] = Tolerance("the yardstick report's AGE bin (180 days), as 100 mm is its distance bin: it sorts the verdicts into two populations and decides nothing about a drawing"),
        ["GridAlignment.cs:LeastConvincingByColumns"] = Tolerance("fewer of a sheet's members than this, or than half of them, standing over placed members is a coincidence, not a frame"),
        ["GridAlignment.cs:ColumnRegistrationMm"] = Tolerance("a column stands over a placed one within this"),
        ["ModelYardstick.cs:FootprintMarginMm"] = Tolerance("how far past her outermost column a column of ours is still judged against her model - registration slop and a slab-edge column, not a bay"),
        ["ModelYardstick.cs:OpeningMatchMm"] = Tolerance("how far apart the centres of our opening and hers may be and still be one opening: a shaft is 2-3 m across and the frame carries registration slop (step 104)"),
        ["ModelYardstick.cs:SliverMm"] = Tolerance("an opening of hers narrower than this across is a modelling release along a wall (31065: 0.0-0.1 x 4.5 m, 88 of her 209), not a hole a drafter draws; counted, judged nowhere (2026-09-18)"),
        ["ModelYardstick.cs:VoidNeedsOurPlateFraction"] = Tolerance("her opening off every plate of ours is a void we left out of the plate only when we carry the floor - our plate area on the storey at least this much of hers (31017's missing podium plates are not voids we left out) (2026-09-18)"),

        // ---- rules: facts of geometry or of buildings, code by design ----
        ["GeometryFilterService.cs:WallShapeTaperShare"] = Rule("a retaining wall's faces converge by up to a quarter"),
        ["GeometryFilterService.cs:BandMinAspect"] = Rule("a band is ten times longer than it is thick"),
        ["GeometryFilterService.cs:BandMaxThicknessShare"] = Rule("wider than twice the thickest wall is a floor"),
        ["GeometryFilterService.cs:TreadMaxMm"] = Rule("a tread is at most 14 in"),
        ["GeometryFilterService.cs:RiserRunMin"] = Rule("three risers make a stair"),
        ["GridAlignment.cs:LeastConvincing"] = Rule("fewer matched lines is a coincidence"),
        ["GridAlignment.cs:LeastConvincingByName"] = Rule("fewer named lines is a coincidence"),
        ["JobCalibration.cs:MinimumSample"] = Rule("fewer members and a percentile is noise"),
        ["MatchLineSheetJoin.cs:MinimumSideRatio"] = Rule("how lopsided a split sheet's linework is"),
        ["ReferenceRules.cs:enoughSamples"] = Rule("deriving a rule from a reference model"),
        ["ReferenceRules.cs:enoughSections"] = Rule("deriving a rule from a reference model"),
        ["ReferenceRules.cs:lowestCredible"] = Rule("deriving a rule from a reference model"),
        ["ReferenceRules.cs:highestCredible"] = Rule("deriving a rule from a reference model"),
        ["E2kDocument.cs:OneFloor"] = Rule("the row below a storey is the floor below it"),
        ["DxfToEtabsService.cs:steps"] = Rule("samples along a member"),
        ["DxfFloodFillPlateDetector.cs:maxEdgePixels"] = Rule("the raster's resolution"),
        ["DxfFloodFillPlateDetector.cs:margin"] = Rule("the raster's margin"),
        ["ModelRender.cs:pad"] = Rule("the render's padding"),
        ["ModelRender.cs:head"] = Rule("the render's header height"),
        ["PdfToSafeConstants.cs:PointsToMm"] = Rule("a unit: 72 points to the inch"),
        ["PublishDiscovery.cs:SearchDepth"] = Rule("how deep under the model folder a set may sit"),

        // ---- another product's settings sharing PdfToSafeConstants.cs (the SAFE/WPF side; plan §7) ----
        ["PdfToSafeConstants.cs:DefaultThicknessMm"] = Elsewhere("the WPF app's default slab thickness"),
        ["PdfToSafeConstants.cs:PaperTextHeightMm"] = Elsewhere("the WPF parser's markup label height; nothing in Core reads it"),
        ["PdfToSafeConstants.cs:PreviewBitmapWidth"] = Elsewhere("the WPF app's preview"),
        ["PdfToSafeConstants.cs:DefaultMeshSizeMm"] = Elsewhere("the SAFE export's mesh"),
        ["PdfToSafeConstants.cs:DefaultStripSpacingMm"] = Elsewhere("the SAFE export's strips"),

        // ---- dead: referenced by nothing; the test fails until each is gone ----
        ["PdfToSafeConstants.cs:AiHttpTimeoutSeconds"] = Dead("no reference outside its own file"),
        ["PdfToSafeConstants.cs:AiMaxTokens"] = Dead("no reference outside its own file"),
        ["PdfToSafeConstants.cs:DefaultColumnMinDimensionMm"] = Dead("no reference outside its own file; the row is column-min-dim-mm"),
    };

    private static readonly Regex Declaration = new(
        @"^\s*(?:public|internal|private)?\s*const\s+(?:double|int|float)\s+(?<decl>[^;]+);", RegexOptions.Compiled);

    /// <summary>Every File:Name the readers declare — the same scan as tools/list_reader_constants.py, every declarator counted.</summary>
    public static SortedSet<string> Scan(string coreRoot)
    {
        var files = new List<string>();
        foreach (string folder in new[] { "Intake", "PdfToSafe", "Dxf" })
            files.AddRange(Directory.EnumerateFiles(Path.Combine(coreRoot, folder), "*.cs"));
        foreach (string extra in new[] { "ScheduleGridReader.cs", "SheetScaleReader.cs", "SheetTitleReader.cs", "ScheduleTableBorder.cs" })
            files.Add(Path.Combine(coreRoot, extra));
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            foreach (string line in File.ReadLines(file))
            {
                var m = Declaration.Match(line);
                if (!m.Success) continue;
                // "MinRadiusPts = 4.0, MaxRadiusPts = 40.0" declares two
                foreach (string part in m.Groups["decl"].Value.Split(','))
                {
                    string name = part.Split('=')[0].Trim();
                    if (name.Length > 0) keys.Add($"{Path.GetFileName(file)}:{name}");
                }
            }
        }
        return keys;
    }

    private static string CoreRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.EngineeringTools.Core"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("repo root"), "Kor.Operations.EngineeringTools.Core");
    }

    [Fact]
    public void EveryConstantIsTriagedAndEveryTriagedConstantExists()
    {
        var scanned = Scan(CoreRoot());
        var untriaged = scanned.Where(k => !Table.ContainsKey(k)).ToList();
        var gone = Table.Keys.Where(k => !scanned.Contains(k) && Table[k].Class != Class.Dead).ToList();
        var deadStillHere = Table.Where(kv => kv.Value.Class == Class.Dead && scanned.Contains(kv.Key)).Select(kv => kv.Key).ToList();
        Assert.True(untriaged.Count == 0, "constants with no line in the triage: " + string.Join(", ", untriaged));
        Assert.True(gone.Count == 0, "triaged constants no longer in the code (remove their lines): " + string.Join(", ", gone));
        Assert.True(deadStillHere.Count == 0, "dead constants still in the code (delete them): " + string.Join(", ", deadStillHere));
    }

    [Fact]
    public void EveryRowNamesAKeyTheCodeReads()
    {
        string source = string.Join("\n", Directory.EnumerateFiles(CoreRoot(), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        var missing = Table.Where(kv => kv.Value.Class == Class.Row && !source.Contains($"\"{kv.Value.Key}\"", StringComparison.Ordinal) && !source.Contains($".{kv.Value.Key!["dxf.pdf.".Length..]}\"", StringComparison.Ordinal))
            .Select(kv => $"{kv.Key} → {kv.Value.Key}").ToList();
        Assert.True(missing.Count == 0, "rows whose key nothing in Core reads: " + string.Join(", ", missing));
        // and every dxf.pdf.* row is one of the PDF side's declared settings
        var undeclared = Table.Values.Where(t => t.Class == Class.Row && t.Key!.StartsWith("dxf.pdf.", StringComparison.Ordinal))
            .Select(t => t.Key!).Distinct().Where(k => !PdfIntakeOptions.SettingKeys.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
        Assert.True(undeclared.Count == 0, "dxf.pdf.* rows not in PdfIntakeOptions.SettingKeys: " + string.Join(", ", undeclared));
    }

    [Fact]
    public void TheDebtIsCounted()
    {
        var scanned = Scan(CoreRoot());
        var byClass = Table.Where(kv => scanned.Contains(kv.Key)).GroupBy(kv => kv.Value.Class).ToDictionary(g => g.Key, g => g.Count());
        string summary = string.Join(", ", Enum.GetValues<Class>().Select(c => $"{c} {byClass.GetValueOrDefault(c)}"));
        // the number the plan carries (§3 of the completion plan): conventions still compiled
        Assert.True(byClass.GetValueOrDefault(Class.Convention) <= 48, $"more conventions compiled than the plan states: {summary}");   // 48 since step 72: the wood rule's 8 in is a row (migration 091)
    }
}
