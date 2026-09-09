#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A set's storeys are the union of its elevation sheets' ladders, reconciled by the pair of level
/// names; the model's height for the same pair is the difference of the two named elevations
/// (brief 26). A check, not a replacement.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: the median and spread across sheets, first-seen order, the pair-wise
/// comparison against a model whose storey list interleaves another building's levels, the
/// tolerance, the unmatched pair, and the summary line. WHAT IT DOES NOT: a real set against a real
/// model — `takeoff storeys-check` does that by hand, and the publish reports it.
/// </remarks>
public sealed class TheDrawingsStoreysAgainstTheModelsTests
{
    private static StoreyLadder.Storey S(string level, string below, double mm) => new(level, below, mm, 0);

    [Fact]
    public void SheetsStatingOneStoreyAreReconciledToTheMedianWithTheirSpread()
    {
        var table = SetStoreys.Reconcile(new List<(int, IReadOnlyList<StoreyLadder.Storey>)>
        {
            (37, new[] { S("L3", "L2", 5336), S("L2", "L1", 2808) }),
            (38, new[] { S("L3", "L2", 5340), S("L2", "L1", 2900), S("L1", "P1", 8652) }),
            (39, new[] { S("L3", "L2", 5330) }),
        }, elevationSheets: 5);

        Assert.Equal(5, table.ElevationSheets);
        Assert.Equal(3, table.SheetsWithStoreys);
        Assert.Equal(new[] { "L3", "L2", "L1" }, table.Storeys.Select(s => s.Level));
        var l3 = table.Storeys[0];
        Assert.Equal(5336, l3.HeightMm); Assert.Equal(3, l3.Sheets); Assert.Equal(10, l3.SpreadMm);
        var l2 = table.Storeys[1];
        Assert.Equal(2854, l2.HeightMm); Assert.Equal(92, l2.SpreadMm);   // two sheets disagree by 92 mm: the median is their mean
        Assert.Equal(1, table.Storeys[2].Sheets);
    }

    [Fact]
    public void TheModelsHeightForAPairIsTheDifferenceOfItsTwoNamedElevationsNotItsStoreyBelow()
    {
        // an interleaved site list, as 31168's reference has it: another building's roof sits between LEVEL 10 and LEVEL 9
        var stories = new List<StoryLevel>
        {
            new("LEVEL 11", 3183.23, 3105.73), new("C-ROOF", 3105.73, 3067.23), new("LEVEL 10", 3067.23, 2978.60),
            new("C-LEVEL 9", 2978.60, 2951.23), new("LEVEL 9", 2951.23, 2840.73), new("LEVEL 3", 2255.23, 2045.23),
            new("LEVEL 2", 2045.23, 1934.73), new("LEVEL 1 MEZZ", 1934.73, 1790.98),
        };
        var table = SetStoreys.Reconcile(new List<(int, IReadOnlyList<StoreyLadder.Storey>)>
        {
            (37, new[] { S("L11", "L10", 2946), S("L10", "L9", 2946), S("L3", "L2", 5336), S("L2", "L1", 2808) }),
        }, elevationSheets: 1);

        var result = StoreyAgreement.Compare(table, stories, unitInInches: 1.0);

        Assert.Equal(3, result.Matched);            // L2 -> L1: the model names no "L1"
        Assert.Equal(3, result.WithinTolerance);    // 116 in = 2,946.4 and 210 in = 5,334 against 2,946 and 5,336
        Assert.Empty(result.Off);
        var l11 = result.Rows.Single(r => r.Level == "L11");
        Assert.Equal(2946.4, l11.ModelMm!.Value, 1);   // 3183.23 - 3067.23 = 116 in, not the 77.5 in to C-ROOF
        Assert.Single(result.DrawingOnly);
        Assert.Contains("4 storeys", result.Summary());
        Assert.Contains("3 match the model by both level names, 3 of those within 25 mm", result.Summary());
        Assert.Contains("L2->L1", result.Summary());
    }

    [Fact]
    public void AStoreyOffByMoreThanTheToleranceIsNamedWithBothNumbers()
    {
        var stories = new List<StoryLevel> { new("LEVEL 3", 2255.23, 2045.23), new("LEVEL 2", 2045.23, 1934.73) };
        var table = SetStoreys.Reconcile(new List<(int, IReadOnlyList<StoreyLadder.Storey>)> { (1, new[] { S("L3", "L2", 5000) }) }, 1);
        var result = StoreyAgreement.Compare(table, stories, 1.0);
        var off = Assert.Single(result.Off);
        Assert.Equal(-334, off.DeltaMm!.Value, 0);
        Assert.Contains("off: L3->L2 drawing 5000 vs model 5334 mm", result.Summary());
    }

    [Fact]
    public void NoStoreysSaysWhyInOneLine()
    {
        var result = StoreyAgreement.Compare(SetStoreys.Reconcile(new List<(int, IReadOnlyList<StoreyLadder.Storey>)>(), 4), new List<StoryLevel>(), 1.0);
        Assert.StartsWith("Storeys: the drawings' 4 section/elevation sheet(s) yielded no storey heights", result.Summary());
    }
}
