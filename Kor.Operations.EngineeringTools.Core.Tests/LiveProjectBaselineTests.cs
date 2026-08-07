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
        Storeys: 60, Walls: 897, Columns: 2418, Floors: 128);

    private static readonly Baseline WestFirst = new(
        "31138 2170 W 1st",
        $@"{Residential}\31138-01 (2170 W 1st Ave Vancouver BC)\02 Engineering\02 Lateral Design\_DXF-plans-for-rebuild",
        $@"{Residential}\31138-01 (2170 W 1st Ave Vancouver BC)\02 Engineering\02 Lateral Design\01 ETABS Models\31138-reference-from-Andrea-gravity.e2k",
        Storeys: 19, Walls: 98, Columns: 118, Floors: 14);

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

            var stories = E2kDocument.Load(baseline.Reference).ReadStories();
            double top = stories.Max(s => s.Elevation);
            double bottom = stories.Min(s => s.ElevationBelow);

            // Every generated joint must lie between the base and the roof. Getting the storey
            // datum wrong put an entire model a thousand feet above the building, and counts
            // alone never noticed — nothing moved except the coordinates.
            var zs = File.ReadLines(output)
                .Select(l => System.Text.RegularExpressions.Regex.Match(l, @"^\s+POINT\s+""K\w+""\s+\S+\s+\S+\s+(\S+)"))
                .Where(m => m.Success)
                .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
                .ToList();

            if (zs.Count == 0) return;

            Assert.True(zs.Min() >= bottom - 1, $"{name}: lowest joint {zs.Min():0} is below the base {bottom:0}.");
            Assert.True(zs.Max() <= top + 1, $"{name}: highest joint {zs.Max():0} is above the roof {top:0}.");
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
