using Kor.Operations.EngineeringTools.Dxf;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A ruling an engineer has given is an obligation, not a note.
///
/// KorStandards holds two different things and only one of them was binding. SETTINGS -- numbers
/// like dxf.flood-fill-bridge -- are read at runtime, and a missing one stops the run. RULINGS --
/// her judgements, "dashed is below", "a big cross means no slab", "solid linework belongs to the
/// storey above" -- are prose, read by nothing. Somebody implements them or does not, and the
/// build says the same either way.
///
/// That gap is not theoretical. solid-linework-belongs-to-the-storey-above sat banked and
/// unimplemented while every model this tool produced put every wall and column a storey too low,
/// and the test suite was green throughout. The engineer found it, weeks later, by reading a model.
///
/// This closes it. Every ruling is claimed here by the test that proves the code obeys it, or
/// declared unimplemented with a reason. A ruling that is neither turns the build red the moment
/// somebody banks it, which is the point of banking it.
///
/// Skipped when KorStandards is unreachable, like every other test that needs it.
/// </summary>
public class RulingCoverageTests
{
    private readonly ITestOutputHelper _out;

    public RulingCoverageTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Ruling topic -> the test that proves this tool obeys it. The value is documentation for a
    /// human; the KEY is what the build checks.
    /// </summary>
    private static readonly Dictionary<string, string> Proven = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a-tag-inside-a-region-means-slab"] = "TagGatedSlabRecoveryTests.AnOpenOutlineWithNoCallOutInsideItRecoversNothing",
        ["a-plate-recovered-twice-is-not-a-hole"] = "PlateReadTwiceTests.AFloorFoundTwiceIsOnePlateAndNoOpening",
        ["a-question-she-has-answered-is-not-asked-again"] = "RulingCoverageTests.NoQuestionIsAskedOnATopicSheHasAlreadyRuledOn",
        ["compose-the-site-once-then-cut"] = "ShippedModelsAgreeWithEachOtherTests.TheTwoPublished31168ModelsAgreeOnEveryStoreyTheyShare",
        ["a-whole-floor-sheet-and-its-parts-are-one-drawing"] = "ShippedModelsAgreeWithEachOtherTests.TheTwoPublished31168ModelsAgreeOnEveryStoreyTheyShare (C-LEVEL 3 carries its own members)",
        ["not-every-plan-is-a-structural-plan"] = "NonStructuralSheetsAreRefusedTests.OnlyThePlansThatDrawTheStructureAreRead",
        ["pier-label-every-wall"] = "EngineerRulingsStillHoldTests.EveryGeneratedWallCarriesAPierLabel",
        ["hatch-is-not-structure"] = "EngineerRulingsStillHoldTests.HatchOnAStructuralLayerContributesNoGeometry",
        ["layers-that-are-not-structure"] = "EngineerRulingsStillHoldTests.GeometryOnAnUnrecognisedLayerIsNotModelled",
        ["perimeter-column-layer-openings"] = "EngineerRulingsStillHoldTests.TheGapBetweenTwoPerimeterColumnsIsNotCutAsAnOpening",
        ["solid-linework-belongs-to-the-storey-above"] = "DxfToEtabsTests.AMemberRisesToTheStoreyAboveTheSheetItWasDrawnOn",
        ["dashed-columns-support-the-slab"] = "DxfToEtabsTests.AMemberRisesToTheStoreyAboveTheSheetItWasDrawnOn (the dashed half)",
        ["level-one-is-one-storey"] = "StoreyCutTests.TheGroundFloorDraftedTwiceBecomesOneStoreyOfTheBuildingAboveIt",
        ["a-model-carries-only-its-own-elevations"] = "StoreyCutTests.UnprefixedTowerFloorsAreNotTheMidRises",
        ["one-model-per-building"] = "PublishPlanTests.EachBuildingGetsAModelAndTheSharedBaseIsInAllOfThem",
        ["level-three-has-its-own-slab-edge"] = "ModelCoverageTests.APlateTheEngineerHasAcceptedKeepsItsArea",
        ["default-slab-thickness-twelve-inches"] = "DxfToEtabsTests.EveryPlateReportsThatItsThicknessWasAssumed",
        ["a-storey-may-have-two-separate-slabs"] = "DxfToEtabsTests.AnOutlineThatCrossesItselfComesBackAsTwoRingsNotOneHourglass",
        ["plate-outline-closes-through-itself"] = "DxfToEtabsTests.AnOutlineThatCrossesItselfComesBackAsTwoRingsNotOneHourglass",
        ["plate-with-nothing-beneath"] = "DxfToEtabsTests.ASlabOutlineWithNothingUnderItAnywhereIsNotAFloor",
        ["floor-stops-short-of-members"] = "DxfToEtabsTests.AFloorThatStopsShortOfItsOwnStructureIsReportedEvenThoughTheStoreyHasOne",
        ["floors-taken-from-below"] = "DxfToEtabsTests.AStoreyWithNoDrawnFloorTakesOneFromBelowButNeverFromAnotherBuilding",
        ["storeys-with-no-drawn-floor"] = "DxfToEtabsTests.EveryStoreyCarryingMembersAlsoCarriesAFloorToSpanBetweenThem",
        ["never-duplicate-the-engineers-work"] = "ModelCoverageTests.EveryDrawnMemberIsModelledOrAlreadyThere",
        ["column-size-bounds"] = "ModelCoverageTests.EveryGeneratedMemberHasTheSizeItWasDrawnAt",
        ["wall-thickness-bounds"] = "ModelPlausibilityTests.NoWallIsAWafer",
        ["header-depth-from-opening-height"] = "ModelPlausibilityTests.EveryHeaderIsHeaderSized",
        ["corner-limbs-vs-stocky-pier"] = "DxfToEtabsTests.AWallsReturnSurvivesEvenWhereItsFaceIsMostlyBuried",
        ["rectangle-is-a-run-not-a-footprint"] = "PlanGeometryTests (wall outline decomposition)",
        ["solid-enough-to-be-one-pier"] = "PlanGeometryTests (wall outline decomposition)",
        ["walls-joined-on-the-drawing-share-one-joint"] = "WallNetworkTests.TwoWallsThatCrossAreBothCutAtTheCrossing",
        ["basement-two-concentric-rings"] = "DxfToEtabsTests.AWallDrawnAsTwoConcentricRingsIsOneWallNotTwoEnormousOnes",
        ["round-columns-are-round"] = "DxfToEtabsTests.OnlyAColumnTheDrawingDrewWithACurveIsRound",
        ["thick-walls-are-real"] = "WallThicknessBoundsStillAdmitWhatEngineersDraw",
        ["wall-vs-column-length"] = "ColumnSizeBoundsStillAdmitWhatEngineersDraw",
        ["headers-are-shells"] = "DxfToEtabsTests.AHeaderGetsOneStoreyEvenWhereAWallWouldGetTwo",
        ["opening-gets-header"] = "DxfToEtabsTests.ADoorwayIsFoundAsAnOpeningRatherThanClosedUp",
        ["wall-connectivity-required"] = "ConnectivityFlagsMatchTheFormsAnEngineersModelUses",
        ["tower-storey-scope"] = "StoreyCutTests.NamingTheTowerStoreysRemovesWhatNeitherFilterCanSee",
        ["sheet-naming-conventions"] = "ParkadeSheetsMatchParkadeStoreysOnly",
    };

    /// <summary>
    /// Rulings the code does NOT obey, each with the reason. Being on this list is a debt that is
    /// visible, which is the whole difference between this and silence.
    /// </summary>
    private static readonly Dictionary<string, string> NotYetObeyed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mezzanine-has-three-slabs"] =
            "Two of three found, and the reading that found the second also produced floors with " +
            "holes cut in them that the engineer rejected on 25 Aug. Withdrawn; see " +
            "reference_etabs_slab_reading_rules.",
        ["two-kinds-of-dashed-line"] =
            "Not a dash-pitch test. The reader is protected from a sparse building outline only " +
            "incidentally, by taking closed loops within the column size bounds.",
        ["dashed-rule-applies-to-walls-too"] =
            "Dashed structure is read only where a sheet draws a slab and no structure at all, " +
            "which is what a roof plan looks like. Applying it more widely doubles members on a " +
            "transfer level.",
        ["basement-walls-support-the-l1-slab"] =
            "Unverified against what the reader produces on LEVEL 1.",
        ["a-big-cross-means-no-slab"] =
            "Built conservatively: a cross cuts only inside an existing plate. Whether it fires " +
            "where the engineer expects has never been measured.",
        ["each-level-appears-once"] =
            "The reader dedups, and this ruling confirms the intent rather than changing it. No " +
            "test asserts the dedup follows from HER rule rather than from ours.",
        ["ask-the-engineer-about-conventions-not-geometry"] = "A working rule for people, not code.",
        ["beams-are-not-modelled"] = "Scope. Nothing to obey.",
        ["diaphragms-are-the-engineers"] = "Scope. dxf.assign-diaphragms is 0 by her instruction.",
        ["roof-carries-columns-not-walls"] =
            "Confirmation of dashed-columns-support-the-slab on one sheet, not a separate rule.",
        ["slab-thickness-and-ring-floor"] = "Superseded by default-slab-thickness-twelve-inches.",
        ["outlines-that-would-not-resolve"] = "Reporting behaviour; the report names them.",
        ["floor-from-perimeter-wall"] =
            "The fallback exists and runs where a sheet closes no slab at all, but no test asserts " +
            "it fires from HER rule -- \"we can even have just one thickness per floor, general " +
            "outline at first\" -- rather than from ours. It also changed twice on 24-25 August, " +
            "widened and narrowed again, with nothing red either time.",
        ["parkade-datum-credible"] =
            "NormaliseBaseStorey exists because she said \"the lowest level, which is P3, seems way " +
            "too high\". No test asserts the datum stays credible on a new job.",
        ["section-properties-stay-engineer"] = "Scope. The tool writes sections it creates and reuses hers.",
        ["tool-engineer-scope-split"] = "Scope. A working agreement, not code.",
        ["revit-diff-is-the-prize"] = "Direction, not a rule this tool can obey today.",
    };

    [Fact]
    public void EveryRulingIsEitherObeyedOrDeclaredUnobeyed()
    {
        string? connection = Environment.GetEnvironmentVariable("KOR_ENGINEERINGTOOLS_STANDARDSDB");
        if (string.IsNullOrWhiteSpace(connection))
        {
            _out.WriteLine("SKIPPED: KOR_ENGINEERINGTOOLS_STANDARDSDB is not set.");
            return;
        }

        List<string> banked;
        try
        {
            banked = ReadTopics(connection);
        }
        catch (Exception ex)
        {
            _out.WriteLine($"SKIPPED: KorStandards unreachable — {ex.Message}");
            return;
        }

        if (banked.Count == 0) { _out.WriteLine("SKIPPED: no rulings returned."); return; }

        var unaccounted = banked
            .Where(t => !Proven.ContainsKey(t) && !NotYetObeyed.ContainsKey(t))
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _out.WriteLine($"{banked.Count} rulings banked: {Proven.Count} proven, " +
                       $"{NotYetObeyed.Count} declared unobeyed, {unaccounted.Count} unaccounted.");

        Assert.True(unaccounted.Count == 0,
            "These rulings are banked in KorStandards and this build says nothing about whether the " +
            "code obeys them:\n  " + string.Join("\n  ", unaccounted) +
            "\n\nAn engineer gave each of these as an instruction. Add the test that proves the tool " +
            "follows it, or add it to NotYetObeyed with the reason it does not — but do not leave a " +
            "ruling recorded and unanswered. solid-linework-belongs-to-the-storey-above sat exactly " +
            "there while every model went out with every wall a storey too low.");
    }

    /// <summary>
    /// Rows that keep asking even though their topic is banked, each with the sentence in the
    /// ruling that authorises the question.
    ///
    /// A ruling settles what the TOOL does. Occasionally it settles that and then says, in terms,
    /// that a particular case still goes to the engineer -- and the difference between that and an
    /// oversight is whether somebody wrote down which. This is where they write it down.
    /// </summary>
    private static readonly Dictionary<string, string> AsksDespiteARuling = new(StringComparer.OrdinalIgnoreCase)
    {
        ["J3"] =
            "outlines-that-would-not-resolve settles the reporting and then draws the line itself: " +
            "\"one thinner than concrete is ANSWERED RATHER THAN ASKED\". The ones thick enough to be " +
            "concrete are the remainder the ruling deliberately leaves with her, and on 31168 the " +
            "filter removes every row — all eighteen were 2 to 4 inches of drafting scratch.",
        ["J4"] =
            "floors-taken-from-below settles WHICH plate a storey with no slab edge of its own is " +
            "given, and requires it to be marked INFERRED. It does not settle whether the shape is " +
            "right, and asking is what produced level-three-has-its-own-slab-edge: she answered " +
            "\"NO, level 3 has its own slab edge, it's on the drawings\" and the reader was fixed.",
    };

    /// <summary>
    /// A question she has already answered is not put to her again.
    ///
    /// The other test in this file asks whether the CODE obeys a ruling. This asks whether the
    /// WORKBOOK does, and they fail differently. Every J-row below was marked NEEDS YOU on a
    /// model sent to her while the answer sat banked in this database in her own words --
    /// diaphragms-are-the-engineers for the storeys with no plate, dashed-columns-support-the-slab
    /// for the plates with nothing beneath them, a-storey-may-have-two-separate-slabs for LEVEL 2.
    /// She said so on the call: "it's obsessed with diaphragm", and "I just told her to ignore
    /// those".
    ///
    /// A workbook that asks an engineer something she has answered twice is a workbook she stops
    /// opening, and every genuine question on it goes with her. So: a row whose RuleTopic is a
    /// banked ruling may be listed, may carry any wording, and may not be marked NEEDS YOU.
    /// </summary>
    [Fact]
    public void NoQuestionIsAskedOnATopicSheHasAlreadyRuledOn()
    {
        string? connection = Environment.GetEnvironmentVariable("KOR_ENGINEERINGTOOLS_STANDARDSDB");
        if (string.IsNullOrWhiteSpace(connection))
        {
            _out.WriteLine("SKIPPED: KOR_ENGINEERINGTOOLS_STANDARDSDB is not set.");
            return;
        }

        List<string> banked;
        try { banked = ReadTopics(connection); }
        catch (Exception ex) { _out.WriteLine($"SKIPPED: KorStandards unreachable — {ex.Message}"); return; }
        if (banked.Count == 0) { _out.WriteLine("SKIPPED: no rulings returned."); return; }

        var ruled = new HashSet<string>(banked, StringComparer.OrdinalIgnoreCase);

        // Every this-job question fires off a report flag, so a report with none of them asks
        // none of them. This one carries all of them at once — a building no drawing set would
        // produce, which is the point: it makes every row that CAN appear appear.
        var everyFault = new DxfToEtabsReport(
            "questions-audit.e2k", 1, 1, 1,
            new ComposeSummary(1, 1, 1, 4, 1, Array.Empty<string>(), new[]
            {
                "2 storey(s) carry walls or columns and no floor plate, so they have no diaphragm: X, Y.",
                "8 storey(s) carry a floor plate with no wall or column beneath it: X, Y.",
                "3 storey(s) were given a floor plate from a neighbour: X, Y.",
                "1 floor plate(s) have an outline that closes through itself: X — two edges TOUCHING " +
                "at (1, 2) ft, from a.dxf. A floor is a ring",
                "Floor does not reach the structure on 2 storey(s): X (24% of the ground its own walls " +
                "and columns cover). Those storeys have a plate.",
                "a.dxf: slab edges: 4 outline(s) would not close (10 units of edge ignored).",
                "a.dxf: JBP_V-WALL: outline 256x70 with 6 vertices could not be resolved into wall " +
                "panels — check this location. [implied thickness 14.0 in]",
            }),
            (0, 0),
            Array.Empty<SheetOutcome>(),
            Array.Empty<string>(),
            new PlanClassificationOptions(),
            new ComposeOptions { SpandrelDepthFloor = 18, SpandrelDepthCeiling = 60 });

        var asked = ModelQuestionnaire
            .StandingQuestions(everyFault.ClassificationUsed, everyFault.ComposeUsed, everyFault)
            .Where(q => !q.Decided)
            .Where(q => !string.IsNullOrWhiteSpace(q.RuleTopic) && ruled.Contains(q.RuleTopic))
            .Where(q => !AsksDespiteARuling.ContainsKey(q.Code))
            .Select(q => $"{q.Code} ({q.RuleTopic})")
            .ToList();

        _out.WriteLine($"{banked.Count} rulings banked; {asked.Count} question(s) still ask about one.");

        Assert.True(asked.Count == 0,
            "These rows are marked NEEDS YOU on a topic the engineer has already ruled on:\n  " +
            string.Join("\n  ", asked) +
            "\n\nRead the ruling in analysis.Ruling and either state its answer in the row and mark it " +
            "Decided, or — if the ruling itself says this case still goes to her — add the row to " +
            "AsksDespiteARuling with the sentence that says so. Asking her twice spends the only " +
            "attention the workbook gets.");
    }

    /// <summary>
    /// The topics an engineer has ruled on. SCOPE and REFER are included: a scope decision the code
    /// quietly ignores is the same failure as an APPLY it ignores.
    /// </summary>
    private static List<string> ReadTopics(string connectionString)
    {
        var topics = new List<string>();

        using var connection = new SqlConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Topic FROM analysis.Ruling WHERE RetiredAtUtc IS NULL AND Scope = 'etabs-modelling'";
        command.CommandTimeout = 30;

        using var reader = command.ExecuteReader();
        while (reader.Read()) topics.Add(reader.GetString(0));

        return topics.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
