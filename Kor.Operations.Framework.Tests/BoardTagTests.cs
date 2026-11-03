using Kor.Operations.Framework.Start;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// kor start --part board --tag T (the lead, 3 Nov: ONE owner for the product; Revit Tools' rows re-owned to 'Virtual
/// Drafter' with "[was Revit Tools] " before What, and a new RevitTools row carries "[Revit Tools]"). Covers: the filter keeps
/// EXACTLY the rows whose What starts with "[was T]" or holds "[T]" - and not "[was T] " later in the text, another tag, a
/// tag without brackets or in another case; the head line says the board is FILTERED and gives the owners' full open
/// count; the COMPACT line's full-board command carries the --tag; the untagged board is byte-identical to the render
/// before --tag existed (the board-golden tests in StartTests still pass unchanged, and here the same state renders the
/// same with and without the new code path). Does NOT cover: the live rows (proved by the run in the commit message).
/// </summary>
public class BoardTagTests
{
    private static BoardRow Row(int id, string what, string owner = "Virtual Drafter") =>
        new(id, owner, "open", null, what, "x", null, null, "10 min");

    private static readonly BoardRow[] Rows =
    {
        Row(1, "[was Revit Tools] [K6] old Revit Tools row"),
        Row(2, "[Revit Tools] a new RevitTools row"),
        Row(3, "Drafter work that mentions [Revit Tools] in the middle"),
        Row(4, "[was Standard Details] a Drafter row"),
        Row(5, "Revit Tools without brackets"),
        Row(6, "[revit tools] another case"),
        Row(7, "a row that says [was Revit Tools] later, not at the start"),
        Row(8, "[Revit Tools helper] a longer tag", "Virtual Drafter helper"),
        Row(9, "[was Revit Tools] a helper's old row", "Virtual Drafter helper"),
    };

    private static BoardState State() => new(new[] { "Virtual Drafter", "Virtual Drafter helper" }, Rows, false,
                                             Array.Empty<OwedRow>(), "live", Array.Empty<GuardRow>(), "live");

    [Fact]
    public void TheFilterKeepsExactlyTheTaggedRows()
    {
        var f = State().FilterByTag("Revit Tools");
        // 7 holds "[was Revit Tools]" not at the start - and no "[Revit Tools]": out. 3 holds "[Revit Tools]": in.
        Assert.Equal(new[] { 1, 2, 3, 9 }, f.Rows.Select(r => r.Id).ToArray());
        Assert.Equal(Rows.Length, f.OpenBeforeTag);
        Assert.Equal("Revit Tools", f.Tag);
    }

    [Fact]
    public void TheHeadLineSaysItIsFilteredAndGivesTheFullCount()
    {
        var text = BoardRender.Render(State().FilterByTag("Revit Tools"), new DateTime(2026, 11, 3, 12, 0, 0), null, "cmd");
        var head = text.Split('\n')[0];
        Assert.Contains("FILTERED to the rows tagged [Revit Tools]", head);
        Assert.Contains($"4 of the {Rows.Length} open row(s) of these owners", head);
        Assert.Contains("- row 1 ", text);
        Assert.DoesNotContain("- row 4 ", text);
        Assert.DoesNotContain("- row 7 ", text);
    }

    [Fact]
    public void TheUntaggedBoardRendersAsBefore()
    {
        var now = new DateTime(2026, 11, 3, 12, 0, 0);
        var s = State();
        var viaRender = BoardRender.Render(s, now, null, "cmd");
        // the pre --tag call shape: Rows(...) with no tag arguments
        var viaRows = BoardRender.Rows(s.Owners, s.Rows, "live", null, null, now, "cmd");
        Assert.Equal(viaRows, viaRender);
        Assert.DoesNotContain("FILTERED", viaRender);
        Assert.Equal(Rows.Length, viaRender.Split('\n').Count(l => l.StartsWith("- row ")));
    }

    [Fact]
    public void TheFullBoardCommandCarriesTheTag()
    {
        Assert.EndsWith("--owner \"Virtual Drafter\" --tag \"Revit Tools\" --text",
                        BoardRender.FullBoardCommand("KOR.RevitTools", new[] { "Virtual Drafter" }, false, "Revit Tools"));
        Assert.EndsWith("--owner \"Virtual Drafter\" --text", BoardRender.FullBoardCommand("KOR.RevitTools", new[] { "Virtual Drafter" }, false));
    }
}
