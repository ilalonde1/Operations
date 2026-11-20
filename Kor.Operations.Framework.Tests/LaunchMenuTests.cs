using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Enroll;
using Kor.Operations.Framework.Launch;
using Kor.Operations.Framework.Start;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Row 2188, kor launch. COVERS: every enrolled repo has a description; the board source read from each of the 7 REAL repos'
/// .claude/settings.json (owners as their session start reads them, RevitTools by tag only); quoted owners; no board hook ->
/// null; the menu names no owner for a tagged board and says NOT READ instead of dropping a repo; finding claude on PATH vs npm;
/// an elevated window is REFUSED (exit 3) before anything is read or started; --pick --dry-run starts nothing.
/// The live board read is covered only through PickDryRunStartsNothingAndNamesTheFolder (live KorStandards, rolled back),
/// which needs the sibling checkouts, the board login and claude installed - as on the dev PC and a kor-claude window.
/// DOES NOT COVER: the interactive loop and a real claude start (proven by hand in a kor-claude window, row 2188 Evidence);
/// that Render never prints an owner is true by construction (it never prints BoardRow.Owner), so the wall half of the
/// tagged-board test cannot fail today - it guards a future change that adds owners to the menu.
/// Same-class fault it would miss: a repo whose settings.json wires the board hook under a matcher that never fires.
/// </summary>
public class LaunchMenuTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void EveryEnrolledRepoHasADescription() =>
        Assert.All(EnrollCheck.Repos, r => Assert.True(LaunchMenu.Descriptions.ContainsKey(r.Folder), r.Folder));

    [Theory]
    [InlineData("Framework", "Framework", null)]
    [InlineData("Operations", "Operations", null)]
    [InlineData("KOR.Drafter", "Virtual Drafter", null)]
    [InlineData("NetworkOps", "NetworkOps", null)]
    [InlineData("KOR Inspections Bookings", "Inspections Bookings", null)]
    [InlineData("KOR Website", "KOR Website", null)]
    [InlineData("KOR.RevitTools", null, "Revit Tools")]
    public void TheBoardIsReadFromEachRealReposOwnSettings(string folder, string? owner, string? tag)
    {
        var settings = Path.Combine(Root, folder, ".claude", "settings.json");
        Assert.True(File.Exists(settings), $"no {settings}: the menu reads repos as siblings of this one");
        var b = LaunchMenu.BoardOf(File.ReadAllText(settings));
        Assert.NotNull(b);
        Assert.Equal(tag, b!.Tag);
        if (owner is null) Assert.Empty(b.Owners);
        else Assert.Equal(new[] { owner }, b.Owners);
    }

    [Fact]
    public void QuotedOwnersAndModuleFallback()
    {
        const string json = """{"hooks":{"SessionStart":[{"hooks":[{"type":"command","command":"C:/KOR-Operations/kor/kor.exe start --module \"KOR.Drafter\" --part board --owner \"Virtual Drafter\" --hook"}]}]}}""";
        Assert.Equal(new[] { "Virtual Drafter" }, LaunchMenu.BoardOf(json)!.Owners);
        const string noOwner = """{"hooks":{"SessionStart":[{"hooks":[{"command":"kor.exe start --module Framework --part board --hook"}]}]}}""";
        Assert.Equal(new[] { "Framework" }, LaunchMenu.BoardOf(noOwner)!.Owners);
        Assert.Null(LaunchMenu.BoardOf("""{"hooks":{"SessionStart":[{"hooks":[{"command":"kor.exe start --module X --part state --hook"}]}]}}"""));
        Assert.Equal("Revit Tools lead", new BoardSource(Array.Empty<string>(), "Revit Tools").Lead);
    }

    [Fact]
    public void TheMenuNamesNoOwnerForATaggedBoardAndNeverDropsARepo()
    {
        var rows = Enumerable.Range(1, 7).Select(i => new BoardRow(i, "Virtual Drafter", "open", null, $"[Revit Tools] item {i}", null, null, null, null)).ToList();
        var lines = LaunchMenu.Render(new[]
        {
            new LaunchEntry(1, "KOR.RevitTools", @"C:\x", "d", "Revit Tools lead", rows, null),
            new LaunchEntry(2, "Framework", @"C:\y", "d", "Framework lead", null, "login failed"),
        }).ToList();
        Assert.DoesNotContain(lines, l => l.Contains("Virtual Drafter"));
        Assert.Contains(lines, l => l.Contains("7 open row(s)"));
        Assert.Contains(lines, l => l.Contains("... and 2 more"));
        Assert.Contains(lines, l => l.Contains("2. Framework"));
        Assert.Contains(lines, l => l.Contains("board NOT READ: login failed"));
    }

    [Fact]
    public void ClaudeIsFoundOnPathThenNpm()
    {
        var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\a\claude.cmd", @"C:\Users\k\AppData\Roaming\npm\claude.cmd" };
        Assert.Equal(@"C:\a\claude.cmd", LaunchMenu.FindClaude(@"C:\none;C:\a", @"C:\Users\k\AppData\Roaming", have.Contains));
        Assert.Equal(@"C:\Users\k\AppData\Roaming\npm\claude.cmd", LaunchMenu.FindClaude(@"C:\none", @"C:\Users\k\AppData\Roaming", have.Contains));
        Assert.Null(LaunchMenu.FindClaude(@"C:\none", null, have.Contains));
    }

    [Fact]
    public void AnElevatedWindowIsRefusedBeforeAnythingIsReadOrStarted()
    {
        var out_ = new StringWriter();
        var err = new StringWriter();
        var log = Path.Combine(Path.GetTempPath(), $"kor-launch-test-{Guid.NewGuid():N}.log");
        using var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        var exit = Program.LaunchVerb(Program.Options.Parse(new[] { "launch", "--pick", "1", "--dry-run" }), out_, err, board, log,
                                      elevated: () => true);
        Assert.Equal(3, exit);
        Assert.Contains("REFUSED", err.ToString());
        Assert.DoesNotContain("Starting claude", out_.ToString());
    }

    [Fact]
    public void PickDryRunStartsNothingAndNamesTheFolder()
    {
        var out_ = new StringWriter();
        var log = Path.Combine(Path.GetTempPath(), $"kor-launch-test-{Guid.NewGuid():N}.log");
        using var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        var exit = Program.LaunchVerb(Program.Options.Parse(new[] { "launch", "--root", Root, "--pick", "1", "--dry-run" }),
                                      out_, new StringWriter(), board, log, elevated: () => false);
        var text = out_.ToString();
        if (text.Contains("claude was not found")) { Assert.Equal(4, exit); return; }
        Assert.Equal(0, exit);
        Assert.Contains($"Starting claude in {Path.Combine(Root, "Framework")}", text);
        Assert.Contains("Framework lead |", text);
    }
}
