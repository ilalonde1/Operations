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

    // ---- --tag T with NO --owner (the lead, 3 Nov: one product owner, tag-filtered board) ----

    private static readonly BoardRow[] AcrossOwners =
    {
        Row(11, "[was Revit Tools] [K6] old row, re-owned"),                      // Virtual Drafter
        Row(12, "[Revit Tools] a new front-door row", "Virtual Drafter helper"),
        Row(13, "[Revit Tools] filed by another lead", "Standard Details"),
        Row(14, "[was Standard Details] not this tag"),
        Row(15, "Revit Tools without brackets", "Framework"),
        Row(16, "a row that says [was Revit Tools] later, not at the start", "Framework helper"),
        Row(17, "[revit tools] another case", "Ops Lead"),
    };

    private static readonly string[] OwnerNames = { "Virtual Drafter", "Virtual Drafter helper", "Standard Details", "Framework", "Ops Lead", "Drafter" };

    [Fact]
    public void TagOnlyShowsExactlyTheTaggedRowsAcrossOwners()
    {
        var s = BoardState.TagOnly(AcrossOwners, "Revit Tools");
        Assert.Equal(new[] { 11, 12, 13 }, s.Rows.Select(r => r.Id).ToArray());
        Assert.Empty(s.Owners);
        Assert.False(s.OwedApplies);
        Assert.Equal("Revit Tools", s.Tag);
        var text = BoardRender.Render(s, new DateTime(2026, 11, 3, 12, 0, 0), null, "cmd");
        Assert.Equal(new[] { "- row 11 [open] [was Revit Tools] [K6] old row, re-owned",
                             "- row 12 [open] [Revit Tools] a new front-door row",
                             "- row 13 [open] [Revit Tools] filed by another lead" },
                     text.Split('\n').Where(l => l.StartsWith("- row ")).ToArray());
    }

    [Fact]
    public void TagOnlyNamesNoOwnerAnywhereNotInTheHeadLinesNorTheRowsNorTheCompactLine()
    {
        var s = BoardState.TagOnly(AcrossOwners, "Revit Tools");
        var cmd = BoardRender.FullBoardCommand("KOR.RevitTools", Array.Empty<string>(), false, "Revit Tools");
        var now = new DateTime(2026, 11, 3, 12, 0, 0);
        foreach (var text in new[] { BoardRender.Render(s, now, null, cmd), BoardRender.Render(s, now, 600, cmd) })
        {
            foreach (var owner in OwnerNames) Assert.DoesNotContain(owner, text);
            Assert.DoesNotContain("Owner", text);
            Assert.DoesNotContain("Per owner", text);
            Assert.DoesNotContain(" of the ", text.Split('\n')[0]);   // no count of anyone's untagged rows
        }
        Assert.Contains("COMPACT", BoardRender.Render(s, now, 600, cmd));   // the budgeted render did take the compact path
    }

    [Fact]
    public void TagOnlyRendersTheGoldenText()
    {
        var s = BoardState.TagOnly(AcrossOwners, "Revit Tools");
        var text = BoardRender.Render(s, new DateTime(2026, 11, 3, 12, 0, 0), null, "cmd");
        var lines = text.Split('\n');
        Assert.Equal("THE BOARD (source of truth) - KorStandards oversight.vw_Board, the open rows tagged [Revit Tools] (What starting " +
                     "\"[was Revit Tools]\" or holding \"[Revit Tools]\"): 3, read 2026-11-03 12:00. A new row for this work carries " +
                     "[Revit Tools] in What.", lines[0]);
        Assert.StartsWith("3 open row(s). THE GOAL IS NONE", lines[1]);
        Assert.Equal("- row 11 [open] [was Revit Tools] [K6] old row, re-owned", lines[2]);
        Assert.Equal("    done when: x", lines[3]);
        Assert.Equal("    budget: 10 min", lines[4]);
        Assert.Equal(11, lines.Length);
    }

    [Fact]
    public void TheOwnerFilteredTaggedBoardIsUnchanged()
    {
        // golden: the exact head lines b39f64e6 rendered for --owner X --tag T (the text in that commit's live run)
        var text = BoardRender.Render(State().FilterByTag("Revit Tools"), new DateTime(2026, 11, 3, 12, 0, 0), null, "cmd");
        var lines = text.Split('\n');
        Assert.Equal("THE BOARD (source of truth) - KorStandards oversight.vw_Board, Owners 'Virtual Drafter', 'Virtual Drafter helper' " +
                     "(every one of them this lead's), read 2026-11-03 12:00 - FILTERED to the rows tagged [Revit Tools] (What starting " +
                     "\"[was Revit Tools]\" or holding \"[Revit Tools]\"): 4 of the 9 open row(s) of these owners; the rest are the same " +
                     "product's, on the unfiltered board", lines[0]);
        Assert.Equal("Per owner: 'Virtual Drafter' 3; 'Virtual Drafter helper' 1", lines[2]);
        Assert.Equal("- row 1 [open] (Owner: Virtual Drafter) [was Revit Tools] [K6] old Revit Tools row", lines[3]);
    }

    [Fact]
    public void ALateOrUnreadableTagOnlyBoardNeverServesAnOwnerBoardsCache()
    {
        // review row 311: the owner-filtered board (tagged or not) saved its render; the tag-only board then fails to read
        var dir = Path.Combine(Path.GetTempPath(), "kor-boardtag-" + Guid.NewGuid().ToString("N"));
        try
        {
            var now = new DateTime(2026, 11, 3, 12, 0, 0);
            var ownerText = BoardRender.Render(State().FilterByTag("Revit Tools"), now, null, "cmd");
            Assert.Contains("Virtual Drafter", ownerText);
            foreach (var hook in new[] { true, false })
            {
                StartCache.Save(StartCache.PathFor(dir, "KOR.RevitTools", BoardRender.CachePart("Revit Tools", tagOnly: false), hook), ownerText);
                StartCache.Save(StartCache.PathFor(dir, "KOR.RevitTools", BoardRender.CachePart(null, tagOnly: false), hook), ownerText);
            }
            var cmd = BoardRender.FullBoardCommand("KOR.RevitTools", Array.Empty<string>(), false, "Revit Tools");
            foreach (var hook in new[] { true, false })
            {
                var path = StartCache.PathFor(dir, "KOR.RevitTools", BoardRender.CachePart("Revit Tools", tagOnly: true), hook);
                var r = StartPart.Run(() => throw new TimeoutException("db down"), path, 9900, TimeSpan.FromSeconds(5),
                                      BoardRender.ByHand("Revit Tools", tagOnly: true, cmd), () => now);
                Assert.False(r.Live);
                Assert.Contains("No cached render on this machine", r.Text);
                foreach (var owner in OwnerNames) Assert.DoesNotContain(owner, r.Text);
                Assert.DoesNotContain("Owner", r.Text);
                Assert.Contains("CHARINDEX(N'[Revit Tools]', What)", r.Text);   // the by-hand read is the TAGGED rows only
                Assert.DoesNotContain("oq.py: oversight.vw_Board)", r.Text);
                // a good tag-only render is cached under its own name and served on the next failure
                var good = BoardRender.Render(BoardState.TagOnly(AcrossOwners, "Revit Tools"), now, null, cmd);
                StartCache.Save(path, good);
                var again = StartPart.Run(() => throw new TimeoutException("db down"), path, null, TimeSpan.FromSeconds(5),
                                          BoardRender.ByHand("Revit Tools", tagOnly: true, cmd), () => now);
                Assert.EndsWith(good, again.Text);
                foreach (var owner in OwnerNames) Assert.DoesNotContain(owner, again.Text);
            }
            // the untagged board's cache name is what it was before --tag
            Assert.Equal(Path.Combine(dir, "start_KOR.RevitTools_board.txt"), StartCache.PathFor(dir, "KOR.RevitTools", BoardRender.CachePart(null, false), true));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void TheTagOnlyFullBoardCommandNamesNoOwner()
    {
        Assert.Equal("C:/KOR-Operations/kor/kor.exe start --module \"KOR.RevitTools\" --part board --tag \"Revit Tools\" --text",
                     BoardRender.FullBoardCommand("KOR.RevitTools", Array.Empty<string>(), false, "Revit Tools"));
    }

    [Fact]
    public void TheFullBoardCommandCarriesTheTag()
    {
        Assert.EndsWith("--owner \"Virtual Drafter\" --tag \"Revit Tools\" --text",
                        BoardRender.FullBoardCommand("KOR.RevitTools", new[] { "Virtual Drafter" }, false, "Revit Tools"));
        Assert.EndsWith("--owner \"Virtual Drafter\" --text", BoardRender.FullBoardCommand("KOR.RevitTools", new[] { "Virtual Drafter" }, false));
    }
}

/// <summary>
/// The tag-only board's SQL (BoardState.LoadTagged) on the live vw_Board, in a rolled-back unit of work: three rows under a
/// tag no real row carries, filed under three owners, plus two that hold the tag text but are not tagged by the rule. Covers:
/// exactly the three come back, from every owner, and the render names none of the owners. Does NOT cover: the CLI wiring
/// (proved by the run in the commit message), a real row's What naming an owner (the render prints What as written).
/// </summary>
public class BoardTagLiveTests
{
    [Fact]
    public void LiveTagOnlyReadsEveryOwnersTaggedRowsAndNamesNoOwner()
    {
        var tag = "kor-test " + Guid.NewGuid().ToString("N")[..8];
        using var b = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        var made = new Dictionary<int, string>();
        foreach (var (owner, what) in new[]
                 {
                     ("Virtual Drafter", $"[was {tag}] re-owned row"),
                     ("Standard Details", $"[{tag}] another lead's"),
                     ("Framework helper", $"in the middle [{tag}] too"),
                     ("Ops Lead", $"says [was {tag}] later, not at the start"),
                     ("Virtual Drafter", $"{tag} without brackets"),
                 })
        {
            using var c = b.Command("INSERT INTO oversight.Commitment (Owner, What, DoneWhen, State, RecordedBy, Budget) " +
                                    "VALUES (@o, @w, N'x', N'open', N'BoardTagLiveTests', N'1 min'); SELECT CAST(SCOPE_IDENTITY() AS int)");
            c.Parameters.AddWithValue("@o", owner);
            c.Parameters.AddWithValue("@w", what);
            made[(int)c.ExecuteScalar()!] = what;
        }
        var s = BoardState.LoadTagged(b, tag);
        var tagged = made.Where(m => !m.Value.StartsWith("says") && !m.Value.EndsWith("brackets")).Select(m => m.Key).OrderBy(i => i);
        Assert.Equal(tagged.ToArray(), s.Rows.Select(r => r.Id).OrderBy(i => i).ToArray());
        Assert.Equal(new[] { "Framework helper", "Standard Details", "Virtual Drafter" },
                     s.Rows.Select(r => r.Owner!).OrderBy(o => o, StringComparer.Ordinal).ToArray());
        var text = BoardRender.Render(s, DateTime.Now, StartRender.HookBudget, "cmd");
        foreach (var owner in new[] { "Virtual Drafter", "Standard Details", "Framework", "Ops Lead", "Owner" })
            Assert.DoesNotContain(owner, text);
        var head = text.Split('\n')[0];
        Assert.Contains($"the open rows tagged [{tag}]", head);
        Assert.Contains("): 3, read ", head);
    }
}
