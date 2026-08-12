using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Runs the generator over KOR's real drawing sets and holds its output to a recorded baseline.
///
/// The unit tests above prove individual rules on small shapes. These prove the whole pipeline
/// still reads two actual buildings the way it did when their models were checked against the
/// drawings, the grid and an engineer's own model — so a change that quietly loses walls, or
/// starts duplicating members, fails here rather than in ETABS.
///
/// They are skipped when the project share is unreachable, so they never fail off the network.
/// </summary>
public class LiveProjectBaselineTests
{
    private const string Residential = @"\\Kor-fs01\Projects\Projects\03 Residential";

    private sealed record Baseline(
        string Name, string DxfFolder, string Reference,
        int Storeys, int Walls, int Columns, int Floors);

    private static readonly Baseline Langara = new(
        "31168 YMCA Langara",
        $@"{Residential}\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models\_DXF-plans-for-rebuild",
        $@"{Residential}\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models\31168-reference.e2k",
        // Rebaselined 2026-08-09. Storeys 61->63 and walls 918->925 because a mezzanine was
        // taking the sheet for the floor below it, so both towers' level 1 stood empty; columns
        // 2425->2464 as the same fix landed level 1 and slender footprints became walls.
        Storeys: 63, Walls: 925, Columns: 2464, Floors: 83);

    private static readonly Baseline WestFirst = new(
        "31138 2170 W 1st",
        $@"{Residential}\31138-01 (2170 W 1st Ave Vancouver BC)\02 Engineering\02 Lateral Design\_DXF-plans-for-rebuild",
        $@"{Residential}\31138-01 (2170 W 1st Ave Vancouver BC)\02 Engineering\02 Lateral Design\01 ETABS Models\31138-reference-from-Andrea-gravity.e2k",
        // Rebaselined 2026-08-09. The big move is walls 89->136: the decomposer was refusing any
        // face shorter than 48" so corner limbs never formed, and her own gravity model carries
        // wall panels at 9, 12, 15, 23 and 27 inches. Columns 162->180 net of footprints more
        // slender than 3:1, which are walls in both engineers' models. Storeys 23->24: Mezz.
        // Rebaselined 2026-08-12 on the engineer's instruction "the model needs to go to P5".
        // Her model stopped at P3 while drafting issues LEVEL P4 and P5, so two whole parkade
        // floors were read and placed nowhere. Adding those storeys brings 60 walls, 43 columns
        // and 2 plates with them, and drops unaccounted drawn members from 29 to 24.
        Storeys: 26, Walls: 196, Columns: 223, Floors: 13);

    /// <summary>Counts may drift a little as rules improve; a real regression moves them further.</summary>
    private const double Tolerance = 0.10;

    private static DxfToEtabsReport? RunOrSkip(Baseline baseline)
    {
        if (!Directory.Exists(baseline.DxfFolder) || !File.Exists(baseline.Reference)) return null;

        string output = Path.Combine(Path.GetTempPath(), $"kor-baseline-{Guid.NewGuid():N}.e2k");
        try
        {
            return DxfToEtabsService.Run(new DxfToEtabsRequest
            {
                DxfFolder = baseline.DxfFolder,
                ReferenceE2k = baseline.Reference,
                OutputE2k = output,
            });
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    private static void AssertWithin(int expected, int actual, string what)
    {
        int allowed = Math.Max(2, (int)Math.Ceiling(expected * Tolerance));
        Assert.True(Math.Abs(actual - expected) <= allowed,
            $"{what}: expected about {expected} (±{allowed}), got {actual}.");
    }

    public static TheoryData<string> Projects => new() { Langara.Name, WestFirst.Name };

    private static Baseline For(string name) => name == Langara.Name ? Langara : WestFirst;

    [Theory]
    [MemberData(nameof(Projects))]
    public void TheModelStillCarriesTheSameStructure(string name)
    {
        var baseline = For(name);
        var report = RunOrSkip(baseline);
        if (report is null) return;   // share unreachable

        AssertWithin(baseline.Storeys, report.StoriesPopulated, $"{name} storeys");
        AssertWithin(baseline.Walls, report.Summary.Walls, $"{name} walls");
        AssertWithin(baseline.Columns, report.Summary.Columns, $"{name} columns");
        AssertWithin(baseline.Floors, report.Summary.Floors, $"{name} floors");
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void NothingIsPlacedByShiftingTheDrawings(string name)
    {
        var report = RunOrSkip(For(name));
        if (report is null) return;

        // The drawings share the model's coordinate system; any offset means that stopped being true.
        Assert.Equal(0, report.AppliedOffset.X, 3);
        Assert.Equal(0, report.AppliedOffset.Y, 3);
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void EverySheetWithGeometryIsPlacedOnAStorey(string name)
    {
        var report = RunOrSkip(For(name));
        if (report is null) return;

        var unplaced = report.Sheets
            .Where(s => s.Stories.Count == 0 && (s.Walls > 0 || s.Columns > 0))
            .Where(s => s.Levels.Count > 0)          // a sheet naming no level is a separate problem
            .Select(s => s.File)
            .ToList();

        Assert.True(unplaced.Count == 0,
            $"{name}: sheets carrying geometry but landing nowhere: {string.Join(", ", unplaced)}");
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void GeneratedGeometryStandsInsideTheBuilding(string name)
    {
        var baseline = For(name);
        if (!Directory.Exists(baseline.DxfFolder) || !File.Exists(baseline.Reference)) return;

        string output = Path.Combine(Path.GetTempPath(), $"kor-height-{Guid.NewGuid():N}.e2k");
        try
        {
            DxfToEtabsService.Run(new DxfToEtabsRequest
            {
                DxfFolder = baseline.DxfFolder,
                ReferenceE2k = baseline.Reference,
                OutputE2k = output,
            });

            var doc = E2kDocument.Load(output);
            var stories = doc.ReadStories();
            var known = new HashSet<string>(stories.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);

            // Every generated member must land on a storey the model actually has. Elevation comes
            // from that storey, so an unknown one is a member ETABS places nowhere — the failure
            // that put a whole model a thousand feet above the building, which counts never noticed.
            var geometry = E2kGeometryReader.Read(doc);
            var strays = geometry.Walls.Select(w => (w.Name, w.Story))
                .Concat(geometry.Columns.Select(c => (c.Name, c.Story)))
                .Where(m => m.Name.StartsWith("K", StringComparison.OrdinalIgnoreCase))
                .Where(m => !known.Contains(m.Story))
                .Select(m => $"{m.Name}->{m.Story}")
                .Distinct()
                .Take(5)
                .ToList();

            Assert.True(strays.Count == 0,
                $"{name}: generated members assigned to storeys the model has no record of: {string.Join(", ", strays)}");

            // A joint's third number is an offset from its storey, not an elevation. A header needs
            // a small one — it stands only over its opening — but anything approaching a building
            // height means an elevation has been written there, which is what once threw the whole
            // model a thousand feet off its storeys.
            var offsets = File.ReadLines(output)
                .Select(l => System.Text.RegularExpressions.Regex.Match(l, @"^\s+POINT\s+""K\w+""\s+\S+\s+\S+\s+(\S+)"))
                .Where(m => m.Success)
                .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
                .ToList();

            var elevations = offsets.Where(z => Math.Abs(z) > 120).Take(3).ToList();
            Assert.True(elevations.Count == 0,
                $"{name}: {elevations.Count} generated joint(s) carry a third value too large to be a " +
                $"storey offset ({string.Join(", ", elevations.Select(z => $"{z:0}"))}) — that slot is an " +
                "offset, and an elevation there places the member nowhere near its storey.");
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public void MembersAreNotModelledOnTopOfOnesTheEngineerAlreadyHas()
    {
        var baseline = WestFirst;   // the reference here is an engineer's own working model
        if (!File.Exists(baseline.Reference)) return;

        string output = Path.Combine(Path.GetTempPath(), $"kor-dupe-{Guid.NewGuid():N}.e2k");
        try
        {
            var report = DxfToEtabsService.Run(new DxfToEtabsRequest
            {
                DxfFolder = baseline.DxfFolder,
                ReferenceE2k = baseline.Reference,
                OutputE2k = output,
            });
            if (report.Summary.Columns == 0) return;

            var geometry = E2kGeometryReader.Read(E2kDocument.Load(output));
            var byStory = geometry.Columns.GroupBy(c => c.Story, StringComparer.OrdinalIgnoreCase);

            foreach (var story in byStory)
            {
                var generated = story.Where(c => c.Name.StartsWith("K", StringComparison.OrdinalIgnoreCase)).ToList();
                var theirs = story.Where(c => !c.Name.StartsWith("K", StringComparison.OrdinalIgnoreCase)).ToList();

                foreach (var mine in generated)
                    Assert.DoesNotContain(theirs, t => t.At.DistanceTo(mine.At) <= 6.0);
            }
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }
}
