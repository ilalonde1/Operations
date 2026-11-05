using System.Text.Json;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Spend;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// G6 (board row 1559), the spend line. COVERS: the week's start; one count per message id, its largest total; only new bytes
/// read; a line not yet ended left unread; a new week from nothing; the deadline leaving a partial count that says so; the
/// range from a reading; the throttle; the line carried by kor context and never a refusal.
/// DOES NOT COVER: that the raw count tracks Ian's screen (no test can: only his screen shows it); the read of the reading from
/// brain.Memory (a test's root skips it; the live probe shows it, docs/G6-SPEND-LINE.md); the 1 s and 3 s waits in the hook
/// (private, read by review, no slow DB to drive them); the real ~/.claude tree
/// (compared once with spend_meter.py, docs/G6-SPEND-LINE.md).
/// Same-class fault it would miss: a transcript format change that moves message.usage: the count would fall silently.
/// </summary>
public class SpendTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "g6-" + Guid.NewGuid().ToString("N"));
    private string CacheDir => Path.Combine(_root, ".spend");
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 15, 0, 0, TimeSpan.FromHours(-7));

    public SpendTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private static string Line(string id, DateTimeOffset at, long cacheRead, long output = 0) => JsonSerializer.Serialize(new
    {
        type = "assistant", timestamp = at.ToUniversalTime().ToString("O"),
        message = new { id, usage = new { input_tokens = 1, cache_read_input_tokens = cacheRead, cache_creation_input_tokens = 0, output_tokens = output } },
    });

    private string File1(params string[] lines)
    {
        var p = Path.Combine(_root, "a.jsonl");
        File.WriteAllText(p, string.Join("\n", lines) + "\n");
        return p;
    }

    private SpendCount Count(DateTimeOffset now, DateTimeOffset? readingAt = null) => SpendMeter.Count(_root, CacheDir, now, readingAt, TimeSpan.FromSeconds(30));

    [Fact]
    public void TheWeekStartsMondayNoonLocal()
    {
        var local = (int y, int mo, int d, int h) => new DateTimeOffset(new DateTime(y, mo, d, h, 0, 0, DateTimeKind.Local));
        Assert.Equal(local(2026, 11, 2, 12), SpendMeter.WeekStart(local(2026, 11, 4, 15)));   // Wednesday
        Assert.Equal(local(2026, 11, 2, 12), SpendMeter.WeekStart(local(2026, 11, 2, 12)));   // the moment itself
        Assert.Equal(local(2026, 10, 26, 12), SpendMeter.WeekStart(local(2026, 11, 2, 11)));  // Monday morning is last week
        Assert.Equal(local(2026, 11, 2, 12), SpendMeter.WeekStart(local(2026, 11, 8, 23)));   // Sunday
    }

    [Fact]
    public void OneCountPerMessageIdItsLargestAndNothingBeforeTheWeek()
    {
        var at = Now.AddHours(-1);
        File1(Line("m1", at, 100), Line("m1", at, 100, 50), Line("m2", at, 10), Line("old", Now.AddDays(-5), 999), "not json \"usage\"", "{\"message\":1}");
        var c = Count(Now);
        Assert.Equal(151 + 11, c.Raw);
        Assert.Equal(2, c.Requests);
        Assert.Equal(0, c.FilesLeft);
    }

    [Fact]
    public void OnlyNewBytesAreReadAndAnUnendedLineWaits()
    {
        var at = Now.AddHours(-1);
        var p = File1(Line("m1", at, 100));
        Assert.Equal(101, Count(Now).Raw);
        var half = Line("m2", at, 7);
        File.AppendAllText(p, half[..10]);
        Assert.Equal(101, Count(Now.AddMinutes(2)).Raw);
        File.AppendAllText(p, half[10..] + "\n");
        Assert.Equal(109, Count(Now.AddMinutes(4)).Raw);
        // no rescan inside ScanEvery
        File.AppendAllText(p, Line("m3", at, 1000) + "\n");
        Assert.Equal(109, Count(Now.AddMinutes(4).AddSeconds(30)).Raw);
        Assert.Equal(1110, Count(Now.AddMinutes(6)).Raw);
    }

    [Fact]
    public void ANewWeekCountsFromNothing()
    {
        File1(Line("m1", Now.AddHours(-1), 100));
        Assert.Equal(101, Count(Now).Raw);
        Assert.Equal(0, Count(Now.AddDays(7)).Raw);   // m1 is last week's
    }

    [Fact]
    public void ADeadlineLeavesAPartialCountThatSaysSo()
    {
        File1(Line("m1", Now.AddHours(-1), 100));
        var c = SpendMeter.Count(_root, CacheDir, Now, null, TimeSpan.FromSeconds(-1));
        Assert.Equal(1, c.FilesLeft);
        Assert.Contains("still to count", SpendMeter.Line(c, null));
        Assert.Equal(101, Count(Now).Raw);   // a partial cache is read on at the next call, whatever its age
    }

    [Fact]
    public void TheLineGivesARangeNamesItsSourceAndSaysItIsAnEstimate()
    {
        var read = new ScreenReading(77, new DateTimeOffset(2026, 11, 4, 14, 49, 0, TimeSpan.FromHours(-7)));
        File1(Line("before", read.At.AddMinutes(-5), 500_000_000), Line("after", read.At.AddMinutes(5), 150_000_000));
        var c = Count(Now, read.At);
        Assert.Equal(150_000_001, c.RawSinceReading);
        var line = SpendMeter.Line(c, read);
        Assert.StartsWith(SpendMeter.Marker, line);
        Assert.Contains("650M raw tokens", line);
        Assert.Contains("Ian's screen read 77%", line);
        Assert.Contains("about 79-87%", line);   // 77 + 150/68, 77 + 150/15
        Assert.Contains("ESTIMATE", line);
        Assert.Contains("Ian's Usage screen rules", line);
        Assert.DoesNotContain("\n", line);
    }

    [Fact]
    public void AReadingFromLastWeekOrNoneGivesRawOnly()
    {
        var c = new SpendCount(SpendMeter.WeekStart(Now), 5_000_000, 0, 1, 0);
        Assert.Contains("no reading of Ian's screen", SpendMeter.Line(c, null));
        Assert.Contains("no reading of Ian's screen", SpendMeter.Line(c, new ScreenReading(90, Now.AddDays(-5))));
        // a DB that could not answer is not a claim that none was recorded (review row 503)
        var failed = SpendMeter.Line(c, null, readingFailed: true);
        Assert.Contains("could not be read just now", failed);
        Assert.DoesNotContain("no reading", failed);
    }

    [Theory]
    [InlineData("77% at 2026-11-04T14:49:00-07:00 (Ian's Usage screen)", 77.0)]
    [InlineData("65.5% at 2026-11-04T00:13:00-07:00", 65.5)]
    [InlineData("77% at 2026-11-04T14:49:00-07:00: Ian's Usage screen, All models.", 77.0)]   // the live record's words
    public void AReadingIsReadFromTheHeadOfARecord(string says, double percent) => Assert.Equal(percent, SpendMeter.ParseReading(says)!.Percent);

    [Theory]
    [InlineData(null)]
    [InlineData("about 77 percent")]
    [InlineData("77% at noon")]
    public void ARecordWithNoReadingGivesNone(string? says) => Assert.Null(SpendMeter.ParseReading(says));

    [Fact]
    public void AToolCallTriesOncePerTenMinutesPerSession()
    {
        var t = DateTime.UtcNow;
        Assert.True(SpendMeter.ShouldTry(CacheDir, "s1", t));
        Assert.False(SpendMeter.ShouldTry(CacheDir, "s1", t.AddMinutes(9)));
        Assert.True(SpendMeter.ShouldTry(CacheDir, "s2", t.AddMinutes(9)));
        Assert.True(SpendMeter.ShouldTry(CacheDir, "s1", t.AddMinutes(11)));
        SpendMeter.Stamp(CacheDir, "s1", t.AddMinutes(20));                    // a prompt restarts the ten minutes
        Assert.False(SpendMeter.ShouldTry(CacheDir, "s1", t.AddMinutes(29)));
    }

    [Fact]
    public void AMarkerThatCannotBeWrittenStillLetsAPromptSpeakAndNeverCostsEveryToolCall()
    {
        File.WriteAllText(CacheDir, "a file where the cache folder goes");
        var transcript = Path.Combine(_root, "session.jsonl");
        File.WriteAllText(transcript, Line("m", DateTimeOffset.Now.AddMinutes(-1), 5_000) + "\n");
        string Hook(string ev, string? tool = null)
        {
            var output = new StringWriter();
            var input = JsonSerializer.Serialize(new { session_id = "zz-g6-disk", transcript_path = transcript, cwd = Path.GetTempPath(), hook_event_name = ev, tool_name = tool, tool_input = new { command = "dotnet build" } });
            Assert.Equal(0, Program.Run(new[] { "context", "--spend-root", _root, "--hook" }, () => input, output, new StringWriter(), Path.Combine(_root, "log")));
            return output.ToString();
        }
        Assert.Contains(SpendMeter.Marker, Hook("UserPromptSubmit"));
        Assert.Equal("", Hook("PreToolUse", "Bash").Trim());                    // a real tool call past the transcript check: no marker, no try
        Assert.False(SpendMeter.ShouldTry(CacheDir, "zz-g6-disk", DateTime.UtcNow));   // a file where the cache folder goes
        // the folder is there and the marker is not writable: a folder stands where the marker goes
        File.Delete(CacheDir);
        Directory.CreateDirectory(Path.Combine(CacheDir, "said-zz-g6-disk2"));
        Assert.False(SpendMeter.ShouldTry(CacheDir, "zz-g6-disk2", DateTime.UtcNow.AddDays(1)));
    }

    [Fact]
    public void KorContextCarriesTheLineAndNeverRefusesForIt()
    {
        var transcript = Path.Combine(_root, "session.jsonl");
        // a session under WARN, and a week far past any percentage: nothing here can be refused
        File.WriteAllText(transcript, Line("big", DateTimeOffset.Now.AddMinutes(-1), 5_000) + "\n");
        File.AppendAllText(Path.Combine(_root, "other.jsonl"), Line("week", DateTimeOffset.Now.AddMinutes(-1), 900_000_000_000) + "\n");
        string Hook(string ev, string? tool, string session = "zz-g6", string? path = null)
        {
            var output = new StringWriter();
            var input = JsonSerializer.Serialize(new { session_id = session, transcript_path = path ?? transcript, cwd = Path.GetTempPath(), hook_event_name = ev, tool_name = tool, tool_input = new { command = "dotnet build" } });
            Assert.Equal(0, Program.Run(new[] { "context", "--module", "M", "--spend-root", _root, "--hook" }, () => input, output, new StringWriter(), Path.Combine(_root, "log")));
            return output.ToString();
        }
        var prompt = Hook("UserPromptSubmit", null);
        Assert.Contains(SpendMeter.Marker, prompt);
        Assert.Contains(SpendMeter.Marker, Hook("UserPromptSubmit", null));   // every prompt
        Assert.Equal("", Hook("PreToolUse", "Bash").Trim());                  // the prompt started this session's ten minutes
        var call = Hook("PreToolUse", "Bash", "zz-g6-long-turn");
        Assert.Contains(SpendMeter.Marker, call);
        Assert.DoesNotContain("permissionDecision", call);
        Assert.Equal("", Hook("PreToolUse", "Bash", "zz-g6-long-turn").Trim());   // once per ten minutes on a tool call
        // a session's first prompt: no transcript exists yet, and the line is still said
        Assert.Contains(SpendMeter.Marker, Hook("UserPromptSubmit", null, "zz-g6-first", Path.Combine(_root, "not-yet.jsonl")));
        Assert.Equal("", Hook("PreToolUse", "Bash", "zz-g6-other", Path.Combine(_root, "not-yet.jsonl")).Trim());   // a tool call with no transcript stays silent
    }
}
