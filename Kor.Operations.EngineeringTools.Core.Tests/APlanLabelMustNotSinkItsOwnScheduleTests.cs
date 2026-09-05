#nullable enable
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

using PC = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.PageContent;
using TT = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.TextToken;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// One mark printed on the plan must not delete the schedule it belongs to.
/// </summary>
/// <remarks>
/// A table's rows are attributed to a heading once per mark column, from the column's TOPMOST row —
/// the only row guaranteed to sit under nothing but its own heading. That is right, and it has a
/// sharp edge: whatever is topmost in the column decides for every row under it.
///
/// The same marks a schedule declares are also printed all over the drawing. On 31138 page 9 the
/// plan label SF1 sits at x=1916 and the schedule's mark column at x=1915 — the same 15pt bucket,
/// one point apart. SF1 is far above the COLUMN SCHEDULE heading, so it anchored the column, owned
/// no column schedule, and ALL ELEVEN rows were dropped. `vector-sched` printed "Column schedule
/// (0 mark(s))", which reads exactly like a sheet that has no column schedule on it.
///
/// What keeps plan labels out is that a column or footing row states a SIZE — two or three
/// dimensions — while a plan label has at most something length-shaped near it. Accepting a lone
/// length is necessary for a flat shear-wall row (SWA | 12" | 35 MPa) and must not be the default;
/// it is Options.RequireDimensionPair, and it is a rule key.
///
/// WHAT THIS COVERS: that a plan label sharing the schedule's mark column, above the heading, does
/// not cost the schedule its rows; and that a flat wall schedule still reads, since it is the case
/// that needs the weaker filter.
///
/// WHAT IT DOES NOT COVER: a plan label that happens to have a full SIZE beside it — a dimension
/// string like "18\" x 36\"" next to a mark on the plan would still qualify, anchor the column, and
/// take the table down with it. Nothing here would catch that; only scoping rows to the heading's
/// own vertical extent would.
/// </remarks>
public sealed class APlanLabelMustNotSinkItsOwnScheduleTests
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

    /// <summary>31138 page 9, reduced: a plan label one point off the schedule's mark column.</summary>
    [Fact]
    public void APlanLabelInTheSameMarkColumnDoesNotCostTheScheduleItsRows()
    {
        var words = new List<TT>
        {
            // the heading, and the plan label ABOVE it in the same column of the sheet
            W("COLUMN", 1900, 700), W("SCHEDULE", 1960, 700),
            W("SF1", 1916, 1400), W("2400", 1960, 1400),   // a lone length beside a plan mark
        };
        words.AddRange(Row("PC1", "18\" x 36\" 65 MPa", 1915, 640));
        words.AddRange(Row("PC2", "18\" x 42\" 75 MPa", 1915, 600));

        var rows = ColumnScheduleReader.ReadSchedule(Page(words.ToArray()));

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Mark == "PC1");
        Assert.Contains(rows, r => r.Mark == "PC2");
    }

    /// <summary>
    /// And the case that needs the weaker filter: a flat wall row states one thickness, not a size.
    /// If this ever fails, the fix above has been applied too widely.
    /// </summary>
    [Fact]
    public void AFlatWallRowStillReadsWithOnlyAThickness()
    {
        var words = new List<TT> { W("SHEAR", 1840, 700), W("WALL", 1890, 700), W("SCHEDULE", 1950, 700) };
        words.AddRange(Row("SWA", "12\" 35 MPa", 1900, 640));
        words.AddRange(Row("SWD", "16\" 55 MPa", 1900, 600));

        var rows = MarkRowScheduleReader.ReadSchedule(
            Page(words.ToArray()), MarkRowScheduleReader.ShearWallDefaults());

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.NotNull(r.SingleLengthMm));
    }

    /// <summary>The convention is reachable as a setting, like every other one here.</summary>
    [Fact]
    public void RequiringASizePairIsARuleNotAConstant()
    {
        Assert.Contains(
            MarkRowScheduleReader.ColumnDefaults().SettingKeys,
            k => k.EndsWith("require-dimension-pair", StringComparison.Ordinal));

        Assert.True(MarkRowScheduleReader.ColumnDefaults().RequireDimensionPair);
        Assert.True(MarkRowScheduleReader.FootingDefaults().RequireDimensionPair);
        Assert.False(MarkRowScheduleReader.ShearWallDefaults().RequireDimensionPair);
    }
}
