#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using PC = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.PageContent;
using TT = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.TextToken;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class MarkRowScheduleReaderTests
{
    private static TT W(string text, double x, double y) => new(text, x, y, x - 8, y - 3, x + 8, y + 3);

    private static PC Page(params TT[] words) =>
        new(1, 3000, 1800, words, new List<VectorPageReader.GeomPath>());

    private static IEnumerable<TT> Row(string mark, string text, double x, double y)
    {
        yield return W(mark, x, y);
        double cx = x + 44;
        foreach (string token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return W(token, cx, y);
            cx += 36;
        }
    }

    [Fact]
    public void SettingsOverrideTheFallbackVocabularyAndPlausibleRange()
    {
        var settings = new Dictionary<string, RuleSetting>(StringComparer.OrdinalIgnoreCase)
        {
            ["dxf.schedule.column.mark-patterns"] =
                new("dxf.schedule.column.mark-patterns", double.NaN, RuleSettings.TextUnits, "test", "test", "test")
                {
                    Text = "^TC\\d{2}$;^PC\\d{2}-[A-Z]$",
                },
            ["dxf.schedule.column.min-dimension-mm"] =
                new("dxf.schedule.column.min-dimension-mm", 300, "mm", "test", "test", "test"),
            ["dxf.schedule.column.row-width-pts"] =
                new("dxf.schedule.column.row-width-pts", 260, "pts", "test", "test", "test"),
        };

        var options = MarkRowScheduleReader.ApplyRules(MarkRowScheduleReader.ColumnDefaults(), settings);
        Assert.Contains("dxf.schedule.column.heading-words", options.SettingKeys);
        Assert.Equal(300, options.MinDimensionMm);

        var words = new List<TT>();
        words.AddRange(Row("TC02", "16\" x 40\" 45 MPa", 120, 430));
        words.AddRange(Row("PC03-A", "42\" x 42\" 45 MPa", 120, 400));
        words.AddRange(Row("PC1", "12\" x 24\" 45 MPa", 120, 370));

        var rows = MarkRowScheduleReader.ReadSchedule(Page(words.ToArray()), options);

        Assert.Equal(new[] { "TC02", "PC03-A" }, rows.Select(r => r.Mark).ToArray());
        Assert.All(rows, r => Assert.Equal(MarkRowScheduleReader.MarkRoute.PatternFallback, r.Route));
        Assert.All(rows, r => Assert.Contains("dxf.schedule.column.mark-patterns", r.SettingKeys));
    }

    [Fact]
    public void LocatedScheduleReadsLiteralFirstColumnMarksInsteadOfGuessingTheirShape()
    {
        var options = MarkRowScheduleReader.ColumnDefaults() with
        {
            MarkPatterns = ["^NOPE$"],
        };
        var words = new List<TT>
        {
            W("PARKADE", 100, 500), W("COLUMN", 170, 500), W("SCHEDULE", 250, 500),
        };
        words.AddRange(Row("C02-A", "24\" x 24\" 45 MPa", 120, 430));
        words.AddRange(Row("C03-B", "30\" x 30\" 45 MPa", 120, 400));
        words.AddRange(Row("PC03-A", "42\" x 42\" 45 MPa", 120, 370));
        words.AddRange(Row("GC11-C", "36\" x 36\" 45 MPa", 120, 340));

        var rows = MarkRowScheduleReader.ReadSchedule(Page(words.ToArray()), options);

        Assert.Equal(new[] { "C02-A", "C03-B", "PC03-A", "GC11-C" }, rows.Select(r => r.Mark).ToArray());
        Assert.All(rows, r => Assert.Equal(MarkRowScheduleReader.MarkRoute.ScheduleColumn, r.Route));
    }

    [Fact]
    public void PatternFallbackStillNamesTheRouteItUsed()
    {
        var words = Row("PC03-A", "42\" x 42\" 45 MPa", 120, 430).ToArray();

        var row = Assert.Single(MarkRowScheduleReader.ReadSchedule(Page(words), MarkRowScheduleReader.ColumnDefaults()));

        Assert.Equal("PC03-A", row.Mark);
        Assert.Equal(MarkRowScheduleReader.MarkRoute.PatternFallback, row.Route);
    }

    [Fact]
    public void ColumnScheduleReaderCarriesLiteralMarkAndRouteThroughItsAdapter()
    {
        var words = new List<TT>
        {
            W("PARKADE", 100, 500), W("COLUMN", 170, 500), W("SCHEDULE", 250, 500),
        };
        words.AddRange(Row("PC03-A", "42\" x 42\" 45 MPa", 120, 430));

        var row = Assert.Single(ColumnScheduleReader.ReadSchedule(Page(words.ToArray())));

        Assert.Equal("PC03-A", row.Mark);
        Assert.Equal(42 * PrintedLength.MmPerInch, row.WidthMm, 0.6);
        Assert.Equal(MarkRowScheduleReader.MarkRoute.ScheduleColumn, row.Route);
    }

    [Fact]
    public void OwnershipIsResolvedPerMarkColumnBeforeRowsAreKept()
    {
        var words = new List<TT>
        {
            W("PARKADE", 2459, 726), W("COLUMN", 2530, 726), W("SCHEDULE", 2630, 726),
            W("FOUNDATION", 2030, 295), W("SCHEDULE", 2150, 295),
        };
        words.AddRange(Row("TC01", "16\" x 40\" 45 MPa", 2346, 439));
        words.AddRange(Row("TC02", "16\" x 40\" 45 MPa", 2346, 214));

        var rows = MarkRowScheduleReader.ReadSchedule(Page(words.ToArray()), MarkRowScheduleReader.ColumnDefaults());

        Assert.Equal(new[] { "TC01", "TC02" }, rows.Select(r => r.Mark).ToArray());
        Assert.All(rows, r => Assert.Contains("COLUMN", r.Heading!.Value.Title));
    }

    [Fact]
    public void FlatShearWallRowsReadThicknessAndStrengthByCellShape()
    {
        var words = new List<TT>
        {
            W("SHEAR", 100, 500), W("WALL", 170, 500), W("SCHEDULE", 240, 500),
        };
        words.AddRange(Row("SWA", "12\" 35 MPa 15M @ 14\" EACH FACE", 110, 440));
        words.AddRange(Row("SWB", "45 MPa 12\" 15M @ 12\" EACH FACE", 110, 410));
        words.AddRange(Row("SWC", "12\" 45 MPa", 110, 380));
        words.AddRange(Row("SWD", "16\" 55 MPa", 110, 350));

        var rows = ScheduleGridReader.ReadFlatWallRows(Page(words.ToArray()));

        Assert.Equal(new[] { "SWA", "SWB", "SWC", "SWD" }, rows.Select(r => r.Mark).ToArray());
        Assert.All(rows.Take(3), r => Assert.Equal(12, r.ThicknessIn, 1));
        Assert.Equal(16, rows.Single(r => r.Mark == "SWD").ThicknessIn, 1);
        Assert.Equal(new[] { 35.0, 45.0, 45.0, 55.0 }, rows.Select(r => r.StrengthMPa!.Value).ToArray());
    }

    [Fact]
    public void RebarSpacingAloneIsNotAFlatWallThickness()
    {
        var words = new List<TT>
        {
            W("SHEAR", 100, 500), W("WALL", 170, 500), W("SCHEDULE", 240, 500),
        };
        words.AddRange(Row("SWE", "35 MPa 15M @ 14\" EACH FACE", 110, 440));

        Assert.Empty(ScheduleGridReader.ReadFlatWallRows(Page(words.ToArray())));
    }
}
