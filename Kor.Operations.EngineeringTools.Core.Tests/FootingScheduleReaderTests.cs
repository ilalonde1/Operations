#nullable enable

using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using TT = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.TextToken;
using PC = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.PageContent;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The deterministic footing takeoff: FOUNDATION SCHEDULE rows (mark → L×W×D DEEP) × plan mark
/// placements counted outside the table. Synthetic pages, written from the drawing convention.
/// </summary>
public sealed class FootingScheduleReaderTests
{
    private static TT W(string text, double x, double y) => new(text, x, y, x - 12, y - 3, x + 12, y + 3);
    private static PC Page(params TT[] words) => new(1, 2592, 1728, words, new System.Collections.Generic.List<VectorPageReader.GeomPath>());

    private static TT[] ScheduleRow(string mark, string dims, double y)
    {
        var toks = new System.Collections.Generic.List<TT> { W(mark, 400, y) };
        double x = 440;
        foreach (var t in dims.Split(' ')) { toks.Add(W(t, x, y)); x += 34; }
        return toks.ToArray();
    }

    [Fact]
    public void Spread_rows_parse_and_strip_rows_are_flagged_unpriceable()
    {
        var page = Page(ScheduleRow("G1", "2000 x 2000 x 800 DEEP", 500)
            .Concat(ScheduleRow("SG1", "600 x 350 DEEP", 480)).ToArray());
        var (types, _) = FootingScheduleReader.ReadSchedule(page);

        var g1 = Assert.Single(types, t => t.Mark == "G1");
        Assert.True(g1.IsSpread);
        // 2.0 × 2.0 × 0.8 m = 3.2 m³ = 4.19 cy per placement.
        Assert.Equal(4.19, g1.VolumeCuYdEach, 2);

        var sg1 = Assert.Single(types, t => t.Mark == "SG1");
        Assert.False(sg1.IsSpread);
        Assert.Equal(0, sg1.VolumeCuYdEach);
    }

    [Fact]
    public void Thousands_comma_in_a_dimension_parses()
    {
        // CAD tables print 4-digit mm with a comma: "4000 x 4000 x 1,300 DEEP".
        var page = Page(ScheduleRow("G3", "4000 x 4000 x 1,300 DEEP", 500));
        var (types, _) = FootingScheduleReader.ReadSchedule(page);
        var g3 = Assert.Single(types);
        Assert.Equal(1300, g3.DepthMm);
    }

    [Fact]
    public void Rows_without_DEEP_are_not_footings()
    {
        // A column-schedule size cell ("500 x 900") shares the mark-then-dims shape but has no DEEP.
        var page = Page(ScheduleRow("PC1", "500 x 900", 500));
        var (types, _) = FootingScheduleReader.ReadSchedule(page);
        Assert.Empty(types);
    }

    [Fact]
    public void Placements_count_marks_outside_the_table_only()
    {
        var schedule = ScheduleRow("G1", "2000 x 2000 x 800 DEEP", 500);
        var plan = new[] { W("G1", 1200, 900), W("G1", 1500, 1100), W("G1", 1900, 700), W("G2", 1300, 950) };
        var page = Page(schedule.Concat(plan).ToArray());

        var (types, box) = FootingScheduleReader.ReadSchedule(page);
        var counts = FootingScheduleReader.CountPlacements(page, types, box);

        Assert.Equal(3, counts["G1"]);          // three plan placements; the schedule row itself excluded
        Assert.False(counts.ContainsKey("G2")); // not a declared mark — never counted
    }
}
