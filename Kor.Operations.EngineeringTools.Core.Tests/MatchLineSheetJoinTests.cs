using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Two halves of one plan.
///
/// "This is a match line, it means that the structure is too big to show on one sheet. On this job
/// for the parkade, 2 sheets are needed. If you make the match lines correspond, you'll get the full
/// structure, and we need the full structure at the parkade." — Andrea Neuviale, 2026-08-28.
/// </summary>
public sealed class MatchLineSheetJoinTests
{
    private readonly ITestOutputHelper _out;
    public MatchLineSheetJoinTests(ITestOutputHelper output) => _out = output;

    private const double SeamX = 40320;

    private static readonly MatchLineSeam Seam =
        new(new DxfPoint(SeamX, 27019), new DxfPoint(SeamX, 31062));

    private static DxfSegment Seg(string layer, double x1, double y1, double x2, double y2) =>
        new(layer, new DxfPoint(x1, y1), new DxfPoint(x2, y2));

    /// <summary>A sheet's worth of linework lying <paramref name="side"/> of the seam: −1 west,
    /// +1 east, 0 straddling it evenly the way a whole plan does.</summary>
    private static IReadOnlyList<DxfSegment> Linework(int side, int count = 40)
    {
        var segs = new List<DxfSegment>();
        for (int i = 0; i < count; i++)
        {
            double x = side switch
            {
                < 0 => SeamX - 1000 - i,
                > 0 => SeamX + 1000 + i,
                _ => i % 2 == 0 ? SeamX - 1000 - i : SeamX + 1000 + i,
            };
            segs.Add(Seg("JBP_C_SLABEDG", x, 28000 + i, x + 5, 28010 + i));
        }
        return segs;
    }

    private static (string, MatchLineSeam?, IReadOnlyList<string>, IReadOnlyList<DxfSegment>) Sheet(
        string name, int side, params string[] storeys) =>
        (name, Seam, storeys, Linework(side));

    // ---------------------------------------------------------------------------------------
    // Finding the seam
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TheSeamIsTheLongestLineOnAMatchLineLayer()
    {
        var seam = MatchLineSheetJoin.SeamOf(new[]
        {
            Seg("JBP_C_SLABEDG", 0, 0, 5000, 0),
            Seg("JBP_G_MATCH_LINES", 40320, 27019, 40320, 31062),   // the seam
            Seg("JBP_G_MATCH_LINES", 40320, 31062, 40500, 31200),   // its leader
        });

        Assert.NotNull(seam);
        Assert.Equal(27019, seam!.Start.Y, 1);
        Assert.Equal(31062, seam.End.Y, 1);
    }

    [Fact]
    public void ASheetWithNoMatchLineHasNoSeam() =>
        Assert.Null(MatchLineSheetJoin.SeamOf(new[] { Seg("JBP_C_SLABEDG", 0, 0, 5000, 0) }));

    [Fact]
    public void TheSameSeamDrawnFromEitherEndIsStillTheSameSeam()
    {
        var reversed = new MatchLineSeam(Seam.End, Seam.Start);
        Assert.True(Seam.SameAs(reversed, MatchLineSheetJoin.DefaultTolerance));
    }

    // ---------------------------------------------------------------------------------------
    // Which side a sheet is on — the test that separates a real split from drawing furniture
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ASheetWholeOnOneSideOfTheSeamHasASideAndTheOtherSheetHasTheOther()
    {
        // Which sign means west is an artefact of the seam's direction and carries no meaning; what
        // has to hold is that the two halves come back OPPOSITE, because that is what joins them.
        int west = MatchLineSheetJoin.DominantSide(Linework(-1), Seam);
        int east = MatchLineSheetJoin.DominantSide(Linework(+1), Seam);

        Assert.NotEqual(0, west);
        Assert.Equal(-west, east);
    }

    [Fact]
    public void ASheetLyingAcrossTheSeamHasNoSideBecauseItIsAWholePlan()
    {
        // On 31138 every one of twenty-eight sheets carries the same line at the same place — it is
        // on the template. Those sheets sit across it at 1.13:1 and 1.41:1.
        Assert.Equal(0, MatchLineSheetJoin.DominantSide(Linework(0), Seam));
    }

    // ---------------------------------------------------------------------------------------
    // Grouping
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TwoSheetsSplitByTheSeamOnOneStoreyAreOnePlan()
    {
        var groups = MatchLineSheetJoin.Group(new[]
        {
            Sheet("BLDG C.dxf", +1, "LEVEL P3"),
            Sheet("BLDG A & B.dxf", -1, "LEVEL P3"),
        });

        Assert.Equal(2, Assert.Single(groups).Files.Count);
    }

    [Fact]
    public void SheetsThatMerelyCarryTheLineAreNotJoined()
    {
        // THE 31138 REGRESSION. Joining on the line alone fused two different elevations of level 1
        // into one plan and put members on a storey they were never drawn on.
        var groups = MatchLineSheetJoin.Group(new[]
        {
            Sheet("LEVEL 1 AT 55-0.dxf", 0, "LEVEL 1"),
            Sheet("LEVEL 1 AT 64-1.dxf", 0, "LEVEL 1"),
        });

        Assert.Empty(groups);
    }

    [Fact]
    public void TwoSheetsOnTheSameSideAreNotTwoHalves()
    {
        var groups = MatchLineSheetJoin.Group(new[]
        {
            Sheet("a.dxf", +1, "LEVEL P3"),
            Sheet("b.dxf", +1, "LEVEL P3"),
        });

        Assert.Empty(groups);
    }

    [Fact]
    public void TheSameSeamOnADifferentStoreyIsADifferentPlan()
    {
        // Every level of a parkade is split on the same line. They are still different floors.
        var groups = MatchLineSheetJoin.Group(new[]
        {
            Sheet("P3 west.dxf", -1, "LEVEL P3"),
            Sheet("P2 east.dxf", +1, "LEVEL P2"),
        });

        Assert.Empty(groups);
    }

    [Fact]
    public void ASheetWhoseSeamNobodySharesIsLeftAlone()
    {
        // A match line pointing at a drawing that is not in this set. Half a plan is not a plan, but
        // inventing a partner for it would be worse.
        var far = new MatchLineSeam(new DxfPoint(900, 0), new DxfPoint(900, 100));

        var groups = MatchLineSheetJoin.Group(new (string, MatchLineSeam?, IReadOnlyList<string>, IReadOnlyList<DxfSegment>)[]
        {
            Sheet("only.dxf", -1, "LEVEL P3"),
            ("elsewhere.dxf", far, new[] { "LEVEL P3" }, Linework(+1)),
        });

        Assert.Empty(groups);
    }

    /// <summary>
    /// THE REAL SHEETS. 31168's P3 foundation is drawn twice — BLDG C and BLDG A &amp; B — both carry
    /// the same match line at x 40320.1, and the seam genuinely divides them: 2.8:1 one way, 8.2:1
    /// the other. Read apart, neither closes a slab edge.
    /// </summary>
    [Fact]
    public void The31168ParkadeSheetsAreTwoHalvesOfOnePlan()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "claude", "C--VIsual-Studio-Projects-Operations");

        // One copy of each named sheet — scratchpads hold duplicates from old sessions, and a test
        // that counts whatever it finds is measuring the scratchpad, not the drawings.
        static string? First(string root, string contains) =>
            Directory.Exists(root)
                ? Directory.EnumerateFiles(root, "*LEVEL P3 PLAN - FOUNDATION PLAN*.dxf", SearchOption.AllDirectories)
                    .FirstOrDefault(f => Path.GetFileName(f).Contains(contains, StringComparison.OrdinalIgnoreCase))
                : null;

        var files = new[] { First(root, "BLDG C"), First(root, "BLDG A & B") }
            .Where(f => f is not null).Select(f => f!).ToList();

        if (files.Count < 2) { _out.WriteLine("SKIPPED: the local 31168 DXFs are not on this machine."); return; }

        var sheets = files.Select(f =>
        {
            var segs = DxfPlanReader.ReadSegments(f);
            return (File: f,
                    Seam: MatchLineSheetJoin.SeamOf(segs),
                    Storeys: (IReadOnlyList<string>)new[] { "LEVEL P3" },
                    Segments: segs);
        }).ToList();

        foreach (var s in sheets)
            _out.WriteLine($"{Path.GetFileName(s.File)} — seam at x {s.Seam?.Start.X:N1}, "
                + $"side {MatchLineSheetJoin.DominantSide(s.Segments, s.Seam!)}");

        Assert.All(sheets, s => Assert.NotNull(s.Seam));

        // The two must fall on OPPOSITE sides — that is what makes them halves.
        var sides = sheets.Select(s => MatchLineSheetJoin.DominantSide(s.Segments, s.Seam!)).ToList();
        Assert.Equal(new[] { -1, 1 }, sides.OrderBy(x => x).ToArray());

        var group = Assert.Single(MatchLineSheetJoin.Group(sheets));
        Assert.Equal(2, group.Files.Count);
        Assert.Equal(40320.1, group.Seam.Start.X, 1);
    }

    /// <summary>
    /// AND THE SHEETS THAT MUST NOT BE JOINED. All twenty-eight of 31138's carry the identical line
    /// at the identical place, because it is on the drawing template. Joining on it broke two
    /// coverage ratchets.
    /// </summary>
    [Fact]
    public void The31138SheetsShareALineButAreNotSplitByIt()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "claude", "C--VIsual-Studio-Projects-Operations");

        var files = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*DXF-S2-07_*LEVEL*.dxf", SearchOption.AllDirectories)
                .GroupBy(Path.GetFileName).Select(g => g.First()).ToList()
            : new List<string>();

        if (files.Count < 2) { _out.WriteLine("SKIPPED: the local 31138 DXFs are not on this machine."); return; }

        var sheets = files.Select(f =>
        {
            var segs = DxfPlanReader.ReadSegments(f);
            return (File: f,
                    Seam: MatchLineSheetJoin.SeamOf(segs),
                    Storeys: (IReadOnlyList<string>)new[] { "LEVEL 1" },
                    Segments: segs);
        }).ToList();

        foreach (var s in sheets)
            _out.WriteLine($"{Path.GetFileName(s.File)} — side {MatchLineSheetJoin.DominantSide(s.Segments, s.Seam!)}");

        Assert.All(sheets, s => Assert.Equal(0, MatchLineSheetJoin.DominantSide(s.Segments, s.Seam!)));
        Assert.Empty(MatchLineSheetJoin.Group(sheets));
    }
}
