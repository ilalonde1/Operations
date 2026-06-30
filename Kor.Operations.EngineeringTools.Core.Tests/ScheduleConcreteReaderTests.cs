#nullable enable

using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class ScheduleConcreteReaderTests
{
    [Fact]
    public void Orders_levels_top_to_bottom_with_parkade_below_grade()
    {
        var ladder = ScheduleConcreteReader.OrderLevels(new[] { "LEVEL 1", "LEVEL 19", "P2", "LEVEL 2", "P1" });
        Assert.Equal(new[] { "LEVEL 19", "LEVEL 2", "LEVEL 1", "P1", "P2" }, ladder);
    }

    private static readonly string[] Ladder5 = { "LEVEL 5", "LEVEL 4", "LEVEL 3", "LEVEL 2", "LEVEL 1" };

    [Fact]
    public void Fills_a_column_size_down_to_the_next_stated_level()
    {
        // C1 stated at LEVEL 5 (450x450) and LEVEL 2 (600x600). Fill-down the full ladder: 5,4,3 = 450; 2,1 = 600.
        const string json = @"{""entries"":[
            {""mark"":""C1"",""levelTop"":""LEVEL 5"",""levelBottom"":""LEVEL 5"",""widthIn"":17.72,""depthIn"":17.72},
            {""mark"":""C1"",""levelTop"":""LEVEL 2"",""levelBottom"":""LEVEL 1"",""widthIn"":23.62,""depthIn"":23.62}]}";
        var bands = ScheduleConcreteReader.ColumnBands(json, Ladder5);

        Assert.Equal(5, bands.Count);                                          // 5,4,3,2,1
        Assert.Equal(3, bands.Count(b => b.WidthIn > 17 && b.WidthIn < 18));   // 5,4,3 carried 450
        Assert.Equal(2, bands.Count(b => b.WidthIn > 23 && b.WidthIn < 24));   // 2,1 = 600
    }

    [Fact]
    public void Does_not_emit_levels_above_the_first_stated_one()
    {
        // A column that only starts at LEVEL 3 must not be priced on LEVEL 5/4.
        const string json = @"{""entries"":[
            {""mark"":""C3"",""levelTop"":""LEVEL 3"",""levelBottom"":""LEVEL 1"",""widthIn"":20,""depthIn"":20}]}";
        var c3 = ScheduleConcreteReader.ColumnBands(json, Ladder5).Where(b => b.Mark == "C3").ToList();
        Assert.Equal(3, c3.Count);                                  // 3,2,1 only
        Assert.All(c3, b => Assert.DoesNotMatch(@"LEVEL [45]", b.LevelTop));
    }

    [Fact]
    public void Prices_through_ComputeColumn_to_a_positive_total()
    {
        const string json = @"{""entries"":[
            {""mark"":""C1"",""levelTop"":""LEVEL 3"",""levelBottom"":""LEVEL 1"",""widthIn"":24,""depthIn"":24}]}";
        var ladder = new[] { "LEVEL 3", "LEVEL 2", "LEVEL 1" };
        var bands = ScheduleConcreteReader.ColumnBands(json, ladder);
        var storeys = ladder.Select(_ => 126.0).ToList();           // 10.5 ft

        var r = ScheduleTakeoff.ComputeColumn(ladder, storeys, bands);
        // 24"×24" = 4 sq.ft × 10.5 ft × 3 levels = 126 cu.ft = 4.67 cu.yd.
        Assert.Equal(4.67, r.TotalCuYd, 2);
    }

    [Fact]
    public void Wall_thickness_in_mm_is_normalized_to_inches()
    {
        // W16 reads "300" (mm) and Z11 reads "12" (inches) — both must end up as inches, ~11.8 and 12.
        const string json = @"{""entries"":[
            {""mark"":""W16"",""levelTop"":""LEVEL 19"",""levelBottom"":""LEVEL 1"",""thicknessIn"":300},
            {""mark"":""Z11"",""levelTop"":""LEVEL 9"",""levelBottom"":""LEVEL 1"",""thicknessIn"":12}]}";
        var bands = ScheduleConcreteReader.WallBands(json);
        Assert.Equal(11.81, bands.Single(b => b.Mark == "W16").ThicknessIn, 2);   // 300 mm → 11.81"
        Assert.Equal(12.0, bands.Single(b => b.Mark == "Z11").ThicknessIn, 2);    // already inches
    }

    [Fact]
    public void Wall_lengths_sum_per_repeated_mark()
    {
        const string json = @"{""marks"":[{""mark"":""W14"",""lengthFt"":22},{""mark"":""W14"",""lengthFt"":22},{""mark"":""W16"",""lengthFt"":10}]}";
        var len = ScheduleConcreteReader.WallLengthsByMark(json);
        Assert.Equal(44, len["W14"]);     // both core faces
        Assert.Equal(10, len["W16"]);
    }

    [Fact]
    public void Wall_prices_through_ComputeWall_only_when_mark_has_both_length_and_thickness()
    {
        var bands = ScheduleConcreteReader.WallBands(
            @"{""entries"":[{""mark"":""W1"",""levelTop"":""LEVEL 2"",""levelBottom"":""LEVEL 1"",""thicknessIn"":12}]}");
        var len = ScheduleConcreteReader.WallLengthsByMark(@"{""marks"":[{""mark"":""W1"",""lengthFt"":20}]}");
        var ladder = new[] { "LEVEL 2", "LEVEL 1" };
        var r = ScheduleTakeoff.ComputeWall(ladder, ladder.Select(_ => 126.0).ToList(), len, bands);
        // 20 ft × 1 ft (12") = 20 sq.ft × 10.5 ft × 2 levels = 420 cu.ft = 15.56 cu.yd.
        Assert.Equal(15.56, r.TotalCuYd, 2);
    }

    [Fact]
    public void Empty_or_sizeless_json_yields_no_bands()
    {
        Assert.Empty(ScheduleConcreteReader.ColumnBands(@"{""entries"":[]}", Ladder5));
        Assert.Empty(ScheduleConcreteReader.ColumnBands(
            @"{""entries"":[{""mark"":""C1"",""levelTop"":""LEVEL 1"",""levelBottom"":""LEVEL 1"",""widthIn"":0,""depthIn"":0}]}", Ladder5));
    }
}
