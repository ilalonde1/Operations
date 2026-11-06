using System.Text.Json;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Spend;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Board row 1892: kor statusline and the real reading in the spend line. COVERS: Claude Code's documented JSON read; one short line;
/// a reading stored at most once per five minutes, on the live KorStandards in a rolled-back unit of work; no percent, no write and
/// no claim on the five minutes; bad input or a failing store still exits 0 with a line, logged once; the stored reading read back
/// through kor spend; the spend line saying REAL, the age as stored and that it can be older; a reading past its reset, or a failed read,
/// falling back to the estimate; start-proof naming its model. Each test counts only its own session's rows, so live readings never move it.
/// DOES NOT COVER: Claude Code's real status line (one real JSON went through the built kor, Framework docs/G6-SPEND-LINE.md); how long kor
/// takes to start (measured there); the 2.5 s limit on the store (read by review: it would need a DB that hangs). The spend-line round trip
/// reads the newest row, so a live store in the same instant would move it. Same-class fault it would miss: Claude Code renaming a field.
/// The line then shows no week and nothing is stored, and only the age in the spend line shows it.
/// </summary>
public class StatusLineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "row1892-" + Guid.NewGuid().ToString("N"));
    private string Log => Path.Combine(_root, "hook.log");
    private readonly string _session = "zz-row1892-" + Guid.NewGuid().ToString("N");
    private static readonly long Resets = DateTimeOffset.UtcNow.AddDays(3).ToUnixTimeSeconds();

    public StatusLineTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    // Claude Code's docs (statusline, "Available data"): the shape, with the fields this verb reads
    private string Json(double? week = 41.2) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["session_id"] = _session,
        ["model"] = new { id = "claude-opus-5-5", display_name = "Opus 5.5" },
        ["context_window"] = new { used_percentage = 8 },
        ["rate_limits"] = week is null ? null : new
        {
            five_hour = new { used_percentage = 23.5, resets_at = Resets },
            seven_day = new { used_percentage = week, resets_at = Resets },
        },
    });

    private (int Code, string Out) Run(string? stdin, BrainDb? db = null)
    {
        var output = new StringWriter();
        var code = Program.Run(new[] { "statusline", "--spend-root", _root, "--hook" }, () => stdin, output, new StringWriter(), Log, db);
        return (code, output.ToString());
    }

    // This test's own rows, newest first. READPAST: another test's open transaction on brain.Event does not hold this read.
    private List<string> Stored(BrainDb db)
    {
        using var c = db.Command("SELECT Detail FROM brain.Event WITH (READPAST) WHERE Module = @m AND Detail LIKE @s ORDER BY Id DESC");
        c.Parameters.Add("@m", System.Data.SqlDbType.NVarChar, 60).Value = StatusLineNames.Module;
        c.Parameters.Add("@s", System.Data.SqlDbType.NVarChar, 200).Value = "%" + _session + "%";
        using var r = c.ExecuteReader();
        var rows = new List<string>();
        while (r.Read()) rows.Add(r.GetString(0));
        return rows;
    }

    private int LogLines => File.Exists(Log) ? File.ReadAllLines(Log).Length : 0;

    [Fact]
    public void TheDocumentedJsonIsReadAndTheLineIsShort()
    {
        var s = StatusLine.Parse(Json());
        Assert.Equal(41.2, s.WeekPercent);
        Assert.Equal(Resets, s.WeekResetsAt);
        Assert.Equal(23.5, s.FiveHourPercent);
        Assert.Equal("Opus 5.5", s.Model);
        Assert.Equal(_session, s.Session);
        var line = StatusLine.Text(s);
        Assert.Contains("41", line);
        Assert.Contains("Opus 5.5", line);
        Assert.DoesNotContain('\n', line);
        Assert.True(line.Length <= 80, line);
    }

    [Fact]
    public void AReadingIsStoredAtMostOncePerFiveMinutes()
    {
        using var db = BrainDb.OpenRolledBack();
        var first = Run(Json(), db);
        Assert.Equal(0, first.Code);
        Assert.Contains("41", first.Out);
        Assert.Single(Stored(db));
        Assert.Equal(0, Run(Json(50), db).Code);
        Assert.Single(Stored(db));                // inside the five minutes
        var stamp = Directory.EnumerateFiles(Path.Combine(_root, ".spend")).Single();
        File.SetLastWriteTimeUtc(stamp, DateTime.UtcNow - StatusLine.StoreEvery - TimeSpan.FromSeconds(1));
        Assert.Equal(0, Run(Json(50), db).Code);
        var rows = Stored(db);
        Assert.Equal(2, rows.Count);              // past them
        Assert.Contains("used_percentage" + (char)34 + ":50", rows[0]);
    }

    [Fact]
    public void NoPercentIsNoWriteAndNoClaimOnTheFiveMinutes()
    {
        using var db = BrainDb.OpenRolledBack();
        var r = Run(Json(null), db);
        Assert.Equal(0, r.Code);
        Assert.Contains("Opus 5.5", r.Out);
        Assert.Empty(Stored(db));
        Assert.False(Directory.Exists(Path.Combine(_root, ".spend")) && Directory.EnumerateFiles(Path.Combine(_root, ".spend")).Any(f => !f.EndsWith("logged", StringComparison.Ordinal)));
        Run(Json(), db);                           // the first real percent is stored at once
        Assert.Single(Stored(db));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public void BadInputStillGivesALineAndIsLoggedOnce(string? stdin)
    {
        for (var i = 0; i < 3; i++)
        {
            var r = Run(stdin);
            Assert.Equal(0, r.Code);
            Assert.False(string.IsNullOrWhiteSpace(r.Out));
        }
        Assert.Equal(1, LogLines);
    }

    [Fact]
    public void AStoreThatFailsStillGivesTheLineAndIsLoggedOnce()
    {
        var dead = BrainDb.OpenRolledBack();
        dead.Dispose();
        var stamp = () => Directory.EnumerateFiles(Path.Combine(_root, ".spend")).Where(f => !f.EndsWith("logged", StringComparison.Ordinal)).ToList();
        for (var i = 0; i < 2; i++)
        {
            var r = Run(Json(), dead);
            Assert.Equal(0, r.Code);
            Assert.Contains("41", r.Out);
            foreach (var f in stamp()) File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddHours(-1));   // so the second run tries again
        }
        Assert.Equal(1, LogLines);
    }

    [Fact]
    public void TheStoredReadingIsWhatTheSpendLineShows()
    {
        using var db = BrainDb.OpenRolledBack();
        Run(Json(), db);
        var output = new StringWriter();
        Assert.Equal(0, Program.Run(new[] { "spend", "--spend-root", _root }, () => null, output, new StringWriter(), Log, db));
        var line = output.ToString();
        Assert.StartsWith(SpendMeter.Marker, line);
        Assert.Contains("REAL", line);
        Assert.Contains("41.2%", line);
        Assert.DoesNotContain("ESTIMATE", line);
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 6, 9, 0, 0, TimeSpan.FromHours(-7));
    private static SpendCount Count => new(SpendMeter.WeekStart(Now), 3_000_000_000, 1_000_000, 10, 0);

    [Fact]
    public void TheSpendLineSaysItIsRealAndHowOld()
    {
        var line = SpendMeter.Line(Count, new ScreenReading(77, Now.AddDays(-1)), real: new UsageReading(82.5, Now.AddMinutes(-7), Now.AddDays(3)), now: Now);
        Assert.Contains("REAL", line);
        Assert.Contains("82.5%", line);
        Assert.Contains("7 min old as stored", line);
        Assert.Contains("can be older", line);
        Assert.Contains("status line", line);
        Assert.Contains("3,000M", line);
        Assert.DoesNotContain("ESTIMATE", line);
        Assert.DoesNotContain("77", line);
        Assert.Contains("2 h 5 min", SpendMeter.Line(Count, null, real: new UsageReading(82.5, Now.AddMinutes(-125), Now.AddDays(3)), now: Now));
    }

    [Fact]
    public void AReadingPastItsResetOrAMissingOneLeavesTheEstimate()
    {
        var screen = new ScreenReading(77, Now.AddDays(-1));
        var past = new UsageReading(99, Now.AddDays(-4), Now.AddMinutes(-1));
        foreach (var line in new[] { SpendMeter.Line(Count, screen, real: past, now: Now), SpendMeter.Line(Count, screen, now: Now) })
        {
            Assert.Contains("ESTIMATE", line);
            Assert.Contains("77%", line);
            Assert.DoesNotContain("REAL", line);
        }
        // a DB that cannot answer gives no reading of either kind: the estimate's line, and it says so
        var failed = SpendMeter.Line(Count, null, readingFailed: true, now: Now);
        Assert.Contains("could not be read", failed);
        Assert.DoesNotContain("REAL", failed);
        // no reset named: this week's start decides
        Assert.Contains("ESTIMATE", SpendMeter.Line(Count, screen, real: new UsageReading(99, Now.AddDays(-9), null), now: Now));
        Assert.Contains("REAL", SpendMeter.Line(Count, screen, real: new UsageReading(80, Now.AddHours(-1), null), now: Now));
    }

    [Theory]
    [InlineData(null, "opus")]
    [InlineData("haiku", "haiku")]
    public void StartProofNamesItsModel(string? asked, string expected)
    {
        var args = Program.StartProofArgs(asked);
        Assert.Equal(expected, args[args.IndexOf("--model") + 1]);
    }
}
