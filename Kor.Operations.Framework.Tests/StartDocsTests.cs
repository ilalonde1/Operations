using System.Text.Json;
using Kor.Operations.Framework.Start;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// W1/W6, kor start-docs (StartDocs). COVERS: a doc never read is refused; a part-read names the unread lines; two part-reads
/// together count; a compaction after the read loses it; a #Section needs only its own lines; a section heading not in the
/// doc is refused; a doc not on disk is REFUSED, named; a Bash cat (a tool result with no toolUseResult.file) does not count; a
/// Read of ANOTHER file of the same name does not count.
/// DOES NOT COVER: the hook verb's stdin, markers and MaxDenials (StartGateVerb's tested pattern; replayed by hand 19 Nov on
/// session a0ed29c9's real transcript: cut before its BUILD-PLAN Read -> deny naming both docs, a Read allowed, whole
/// transcript after its STATE NOW Read -> allow); whether the session understood what it read.
/// Same-class fault it would miss: a session that reads the doc and then acts against it.
/// </summary>
public class StartDocsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "startdocs-" + Guid.NewGuid().ToString("N"));
    private string Plan => Path.Combine(_dir, "docs", "BUILD-PLAN.md");
    private string Handover => Path.Combine(_dir, "docs", "HANDOVER.md");

    public StartDocsTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "docs"));
        File.WriteAllLines(Plan, Enumerable.Range(1, 20).Select(i => $"plan line {i}"));
        File.WriteAllLines(Handover, new[] { "# H", "", "## STATE NOW - today", "now 1", "now 2", "", "## STATE NOW (history) - before", "old 1", "old 2" });
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static string ReadResult(string path, int start, int num, int total) => JsonSerializer.Serialize(new
    {
        type = "user",
        message = new { role = "user", content = new[] { new { type = "tool_result", tool_use_id = "t1", content = "..." } } },
        toolUseResult = new { type = "text", file = new { filePath = path, startLine = start, numLines = num, totalLines = total } },
    });

    private const string Compaction = "{\"type\":\"user\",\"isCompactSummary\":true,\"message\":{\"content\":\"summary\"}}";

    // JSON spacing must not preserve pre-compaction coverage. Malformed result metadata earns no coverage.
    // Does not prove comprehension or content identity after a same-length file edit.
    [Theory]
    [InlineData("\"isCompactSummary\": true")]
    [InlineData("\"isCompactSummary\" : true")]
    public void FormattedCompactionStillLosesTheRead(string field)
    {
        var summary = "{\"type\":\"user\"," + field + ",\"message\":{\"content\":\"summary\"}}";
        Assert.False(Decide(new[] { ReadResult(Plan, 1, 20, 20), summary }, "docs/BUILD-PLAN.md").Allow);
    }

    [Fact]
    public void InvalidReadMetadataDoesNotCrashTheGate()
    {
        var bad = ReadResult(Plan, 1, 20, 20).Replace("\"numLines\":20", "\"numLines\":\"twenty\"");
        Assert.False(Decide(new[] { bad }, "docs/BUILD-PLAN.md").Allow);
    }
    private const string BashCat = "{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"t2\",\"content\":\"plan line 1\"}]},\"toolUseResult\":{\"stdout\":\"plan line 1\"}}";

    private StartGate.Decision Decide(IEnumerable<string> lines, params string[] args)
    {
        var docs = args.Select(a => StartDocs.ParseDoc(a, _dir)).ToList();
        return StartDocs.Decide(docs, StartDocs.Scan(lines, docs), p => File.Exists(p) ? File.ReadAllLines(p) : null);
    }

    [Fact]
    public void ADocNeverRead_IsRefused_NamingIt()
    {
        var d = Decide(Array.Empty<string>(), "docs/BUILD-PLAN.md");
        Assert.False(d.Allow);
        Assert.Contains("docs/BUILD-PLAN.md (not read)", d.Reason);
        Assert.Contains("a Bash cat, sed or Get-Content does NOT count", d.Reason);
    }

    [Fact]
    public void APartRead_NamesTheUnreadLines_AndTwoPartsTogetherCount()
    {
        var half = ReadResult(Plan, 1, 10, 20);
        var d = Decide(new[] { half }, "docs/BUILD-PLAN.md");
        Assert.False(d.Allow);
        Assert.Contains("lines 11-20 unread", d.Reason);
        Assert.True(Decide(new[] { half, ReadResult(Plan, 11, 10, 20) }, "docs/BUILD-PLAN.md").Allow);
    }

    [Fact]
    public void ACompactionAfterTheRead_LosesIt()
    {
        var read = ReadResult(Plan, 1, 20, 20);
        Assert.True(Decide(new[] { read }, "docs/BUILD-PLAN.md").Allow);
        Assert.False(Decide(new[] { read, Compaction }, "docs/BUILD-PLAN.md").Allow);
        Assert.True(Decide(new[] { read, Compaction, read }, "docs/BUILD-PLAN.md").Allow);
    }

    [Fact]
    public void ASection_NeedsOnlyItsOwnLines()   // lines 3-6 of the handover: the newest STATE NOW, not the history
    {
        var d = Decide(new[] { ReadResult(Handover, 1, 6, 9) }, "docs/HANDOVER.md#STATE NOW");
        Assert.True(d.Allow, d.Reason);
        var part = Decide(new[] { ReadResult(Handover, 1, 4, 9) }, "docs/HANDOVER.md#STATE NOW");
        Assert.False(part.Allow);
        Assert.Contains("section 'STATE NOW', lines 3-6", part.Reason);
        Assert.Contains("lines 5-6 unread", part.Reason);
    }

    [Fact]
    public void ASectionHeadingNotInTheDoc_IsRefused()
    {
        var d = Decide(new[] { ReadResult(Handover, 1, 9, 9) }, "docs/HANDOVER.md#RESUME");
        Assert.False(d.Allow);
        Assert.Contains("no heading 'RESUME' in it", d.Reason);
    }

    [Fact]
    public void ADocNotOnDisk_IsRefused_Named()   // live probe 19 Nov: a wrong root made every doc 'not on disk' and the gate opened
    {
        var d = Decide(new[] { ReadResult(Plan, 1, 20, 20) }, "docs/BUILD-PLAN.md", "docs/GONE.md");
        Assert.False(d.Allow);
        Assert.Contains("docs/GONE.md (NOT FOUND at", d.Reason);
        Assert.DoesNotContain("BUILD-PLAN.md (", d.Reason);   // the read doc is not named as owed
    }

    [Fact]
    public void ABashCat_AndAReadOfAnotherFileOfTheSameName_DoNotCount()
    {
        var other = ReadResult(Path.Combine(_dir, "elsewhere", "BUILD-PLAN.md"), 1, 20, 20);
        Assert.False(Decide(new[] { BashCat, other }, "docs/BUILD-PLAN.md").Allow);
    }

    [Fact]
    public void ReadingTools_AreExactlyTheReadingOnes()   // review 2291: an added write-capable tool must turn this red
    {
        Assert.Equal(new[] { "AskUserQuestion", "Glob", "Grep", "ListAgents", "Read", "TaskStop", "ToolSearch" },
                     StartDocs.ReadingTools.OrderBy(t => t, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void ADocThatGrewAfterTheRead_NeedsItsNewLines()   // review 2291: the current file decides, not the read's total
    {
        var read = ReadResult(Plan, 1, 20, 20);
        Assert.True(Decide(new[] { read }, "docs/BUILD-PLAN.md").Allow);
        File.AppendAllLines(Plan, new[] { "plan line 21", "plan line 22" });
        var d = Decide(new[] { read }, "docs/BUILD-PLAN.md");
        Assert.False(d.Allow);
        Assert.Contains("lines 21-22 unread", d.Reason);
    }

    [Fact]
    public void AHeadingIsMatchedAsAWord()   // review 2291: '#STATE' must not take a 'STATEMENT' heading
    {
        File.WriteAllLines(Handover, new[] { "# H", "## STATEMENT of work", "x", "## STATE NOW", "now", "## Other", "y" });
        var d = Decide(new[] { ReadResult(Handover, 4, 2, 7) }, "docs/HANDOVER.md#STATE");
        Assert.True(d.Allow, d.Reason);
    }
}
