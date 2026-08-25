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
        ["perimeter-column-layer-openings"] = "Unverified.",
        ["floor-from-perimeter-wall"] =
            "The fallback exists and runs where a sheet closes no slab at all, but no test asserts " +
            "it fires from HER rule -- \"we can even have just one thickness per floor, general " +
            "outline at first\" -- rather than from ours. It also changed twice on 24-25 August, " +
            "widened and narrowed again, with nothing red either time.",
        ["pier-label-every-wall"] =
            "dxf.assign-pier-labels is on and 130 labels are written, but nothing asserts EVERY " +
            "wall gets one, which is what she asked for.",
        ["parkade-datum-credible"] =
            "NormaliseBaseStorey exists because she said \"the lowest level, which is P3, seems way " +
            "too high\". No test asserts the datum stays credible on a new job.",
        ["layers-that-are-not-structure"] =
            "Layer patterns are settings and are portfolio-measured, but nothing proves a " +
            "non-structural layer is refused rather than merely absent from the pattern list.",
        ["section-properties-stay-engineer"] = "Scope. The tool writes sections it creates and reuses hers.",
        ["tool-engineer-scope-split"] = "Scope. A working agreement, not code.",
        ["revit-diff-is-the-prize"] = "Direction, not a rule this tool can obey today.",
        ["hatch-is-not-structure"] =
            "Migration 044. Whether the reader still honours it has never been asserted; a hatch " +
            "layer reaching the wall or column patterns would not fail anything today.",
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
