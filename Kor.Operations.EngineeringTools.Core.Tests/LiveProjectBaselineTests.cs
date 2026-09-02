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
/// <remarks>
/// SLOW: reads the live project models off the share.
///
/// Tagged so an ordinary edit can run everything else in seconds. These five classes are the
/// reason the suite takes ten minutes, and a suite that takes ten minutes gets run for changes
/// it cannot possibly affect -- a PowerShell edit, a document -- while holding the build lock
/// against the next change.
///
///   dotnet test --filter "Speed!=Slow"    every edit, seconds
///   dotnet test                           geometry changes and before any publish
///
/// Geometry work always runs the full suite: the coverage ratchets here are the only thing
/// that catches a member read on one build and lost on the next, and they may only come down.
/// </remarks>
[Trait("Speed", "Slow")]
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
        // Rebaselined 2026-08-13: outlines are grouped by layer family, so JBP_V-WALL is no longer
        // welded to JBP_B_WALL. Walls 925->948 and headers 139->148 as cores that resolved to
        // nothing come back — LEVEL 27 tower B goes from 1 wall to 18, matching its neighbours.
        // Plates 83->82: the lost one existed only because unrelated slab layers were stitched.
        // Rebaselined 2026-08-13, three of the engineer's own items in one pass. Walls 947->1097
        // and columns 2464->2438: a panel's length is now how far its concrete runs rather than
        // how much of its two faces overlap, so the returns turned up at the end of a wall survive
        // instead of being dropped as slivers or made into columns; and the pier branch no longer
        // takes an L-shaped corner before the decomposer sees it, so tower B's north corners come
        // apart into a 67x28 wall and its 36-thick leg instead of one 42-thick panel across the
        // top. Both were on her list — "Tower A core missing return walls (red) and headers
        // (blue)" and "still have that problem with the north corner walls for tower B" — and
        // together they take headers 144->262, because a wall that reaches its true end leaves a
        // doorway beside it where before there was a void nothing bounded.
        // Rebaselined 2026-08-14 after every dimension rule was measured against the 1,126
        // engineer-authored models on the projects volume rather than against these two jobs.
        // The wall ceiling moved 36->60 because 36 was rejecting 4,681 of 36,761 real wall
        // sections, and the column long-side cap 96->132 because 207 real column sections run 98"-165".
        // Walls 1,097->1,119 and headers 365->375 follow from the wall ceiling; columns hold.
        //
        // Rebaselined 2026-08-24, floors 82 -> 94, from the same change: slab outlines now close by
        // joining their own loose ends, and at the interruption width where the ordinary bridge is
        // too tight. On this job it is what gives LEVEL 1 the first floor plate it has ever had --
        // 78,859 sq ft on a storey whose slab edge has never closed as vectors -- and the YMCA
        // mezzanine the second of the three slabs the engineer says are there.
        //
        // Rebaselined 2026-08-25. Walls 1119 -> 1388 because two walls that cross are now both cut
        // at the crossing so they share a joint -- the engineer's rule, and the panels either side
        // of a cut are two panels. Floors 94 -> 87 because the three plate-recovery changes made on
        // 24 August were withdrawn the next morning, after she opened the model and found floors
        // with their own slabs cut out of them as openings.
        Storeys: 63, Walls: 1388, Columns: 2462, Floors: 87);

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
        // Rebaselined again the same day: three sheets are titled "LEVEL 8, 9", "LEVEL 11, 12" and
        // "LEVEL 17, 18", and only the first number was read, so L09, L12 and L18 were built empty.
        // Reading listed titles whole takes 26 storeys to 29 and columns 223 to 248; walls hold at
        // 196 because her model already carries walls on those floors.
        // Rebaselined 2026-08-13, columns 248->435 and walls 202->235, from an independent audit:
        // the reader knew LINE, ARC, LWPOLYLINE and POLYLINE and had no INSERT case, so anything
        // drafting placed as a block was never read. 31138 puts 100 columns down that way — HSS
        // 6x6, HSS 8x8 and round concrete — and 75 of them were absent from the model. Reading
        // the BLOCKS section and placing each insert recovered 43; the remaining 32 were the HSS
        // 6x6 columns, whose loops close at exactly 6.000000 x 6.000000 and still fell a hair
        // under a bare 6.0 minimum, so the size limits now carry the same half-inch of slack the
        // wall rules do. Whole floors were affected: levels 5 and 6 had all 22 missing, the mech
        // level and the roof all 15. Plates 13->14 and headers 20->22 come with the geometry.
        // Rebaselined 2026-08-14, and the columns move DOWN, which is the point of it.
        // 435 included duplicates: 31138 draws the same edges on JBP_V_COL and JBP_V_COL-1 with
        // identical coordinates, one layer family, so 45 members were built twice and every count
        // agreed with itself. Reading an edge once per family gives 390.
        // Walls 235->242: a 36x104 footprint on JBP_B_WALL had been becoming a frame column,
        // stopped only by the old 96" size cap and stopped silently. Wall-layer concrete 104"
        // long is a wall by the engineer's own rule -- "less than 48 in length should be a
        // column" -- so it is one now, keeping the in-plane shear it was drawn to carry.
        // Plates 14->13: the orphan-plate rule dropped a legend box that had been a floor.
        //
        // Rebaselined 2026-08-24. Walls 242->205 and columns 390->304 because members now rise to
        // the storey they run to, and on THIS job that means fewer of them are built, not more:
        // 31138 is a gap-fill against a model the engineer had already built by hand, so a member
        // landing on its true storey lands where hers already is and is correctly skipped as a
        // duplicate. Measured both ways on the same build: walls already modelled 312->348,
        // columns 316->391. Exactly the members that stopped being generated.
        //
        // That is the second independent building to confirm the rule -- 31065 scores it against
        // an engineer's own model in EngineerModelBenchmarkTests, and this one shows it from the
        // other side, by agreeing with a model the tool was not built from.
        //
        // Rebaselined 2026-08-24, floors 13 -> 48. Slab outlines now close two ways the drawing
        // does not: by joining a chain's own two loose ends, and at the interruption width where
        // the ordinary bridge is too tight. More plates on this job is the RIGHT direction, and it
        // is measured rather than assumed -- built from a bare storey list and scored against the
        // engineer's own model, 31138 carries 170,274 sq ft of floor against her 327,220, and only
        // 11 of its 27 storeys are within a fifth of hers. It is finding half her floors, not
        // inventing extra ones.
        //
        // Floors 48 -> 15 with the withdrawal of chain-closing. That job is back to reading only
        // the outlines its drawings actually close; its part-plan storeys are unfloored again, and
        // that is a visible gap rather than an invented slab.
        //
        // Rebaselined 2026-09-01, walls 205 -> 228 and columns 304 -> 307. Her rule of 1 Sep: "some
        // columns are double height. In that case they should be modelled on both floors ...
        // otherwise they're just hanging from L2", and "the same for walls too". A member standing
        // on two storeys with one empty storey between now carries an assign on that storey, and a
        // member whose base landed on a storey with no slab under it is carried down one floor to
        // one that has. On 31138 that is 23 walls and 3 columns.
        //
        // The count RISING on this job is the direction to watch: 31138 is a gap-fill against a
        // model she had already built, so most of what we would add is correctly skipped as hers.
        // These are the members that were hanging, and the same change adds 17 columns on 31168
        // where the drawings carry the whole building. Measured on both before it shipped -- an
        // uncapped version of the same rule took this job to 455 walls and 587 columns, and that is
        // what the second building is for.
        Storeys: 29, Walls: 228, Columns: 307, Floors: 15);

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
                RequireRuleSettings = true,
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
                RequireRuleSettings = true,
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
                RequireRuleSettings = true,
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
