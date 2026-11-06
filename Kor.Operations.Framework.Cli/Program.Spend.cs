using Kor.Operations.Framework.Spend;

namespace Kor.Operations.Framework.Cli;

/// <summary>G6, the spend line (board row 1559): <c>kor spend</c> prints it, filling the count for as long as it takes, and
/// <c>kor context --hook</c> carries it (<see cref="SpendLineForHook"/>). The rules, what it reads and what it does not cover
/// are <see cref="SpendMeter"/>'s header.</summary>
public static partial class Program
{
    // The whole line gets HookLineBudget in a hook, because the hook's output waits on it, G5's WARN and STOP included, and the
    // hook's timeout is 10 s (review rows 501, 503). Past it the line is left out and the timeout logged; G5's text still goes. A tool
    // call does not try again until ten minutes on, a prompt always does (review row 523). The cut-off scan's progress is lost when
    // the process exits and the next try reads it again; the cache file stays whole, because only a finished write replaces it.
    private static readonly TimeSpan HookLineBudget = TimeSpan.FromSeconds(3);
    // Inside it: the DB gets 1 s and the scan 1.5 s, so a slow DB costs only the reading: the local count still goes (review row 504)
    private static readonly TimeSpan HookReadingWait = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HookScanBudget = TimeSpan.FromSeconds(1.5);

    private static int SpendVerb(Options o, TextWriter output, BrainDb? db = null)
    {
        output.WriteLine(SpendLine(o.Get("--spend-root"), TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10), db));
        return 0;
    }

    /// <summary>The newest real reading and the newest recorded screen reading, in one read (SNAPSHOT: an open write on brain.Event does
    /// not hold it). Failed true: the DB could not say, which is not the same as none recorded.</summary>
    private static (Readings? Reading, bool Failed) NewestReadings(BrainDb? db)
    {
        Readings Both(BrainDb d)
        {
            using var c = d.Command("SELECT TOP 1 Description FROM brain.Memory WHERE Lane = N'Framework' AND Name = @n AND RetiredAtUtc IS NULL AND Kind <> N'issue'", 3);
            c.Parameters.Add("@n", System.Data.SqlDbType.NVarChar, 120).Value = SpendMeter.ReadingRecord;
            var screen = SpendMeter.ParseReading(c.ExecuteScalar() as string);
            return new Readings(screen, StatusLine.Newest(d));
        }
        try { return (db is not null ? Both(db) : BrainDb.Read3s(Both), false); }
        catch (Exception e) when (e is Microsoft.Data.SqlClient.SqlException or InvalidOperationException or ArgumentException) { return (null, true); }
    }

    private sealed record Readings(ScreenReading? Screen, UsageReading? Real);

    private static string SpendLine(string? root, TimeSpan readingWait, TimeSpan budget, BrainDb? db = null)
    {
        Readings? readings = null;
        var failed = false;
        if (root is null || db is not null)   // a test's root has no record behind it, unless the test gives it the DB
        {
            (readings, failed) = SpendMeter.ReadingWithin(() => NewestReadings(db), readingWait);
        }
        var count = SpendMeter.Count(root ?? SpendMeter.DefaultProjectsRoot, CacheDirFor(root), DateTimeOffset.Now, readings?.Screen?.At, budget);
        return SpendMeter.Line(count, readings?.Screen, failed, readings?.Real, DateTimeOffset.Now);
    }

    private static string CacheDirFor(string? root) => root is null ? SpendMeter.DefaultCacheDir : Path.Combine(root, ".spend");

    /// <summary>The line for a hook, or null: at every prompt, and at most once per SayEvery on a tool call. "--spend-root off" says nothing.
    /// It never fails a hook: an error is logged and the line is left out, and a refusal is never made here.</summary>
    private static string? SpendLineForHook(Options o, string? session, bool prompt, string hookErrorLog)
    {
        var root = o.Get("--spend-root");
        if (root == "off") return null;
        try
        {
            // The ten minutes count ATTEMPTS, whether or not the line lands: so a slow line or a failing disk costs a tool call at
            // most once per ten minutes (review row 505). A prompt always tries, and restarts them.
            if (prompt) { try { SpendMeter.Stamp(CacheDirFor(root), session, DateTime.UtcNow); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
            else if (!SpendMeter.ShouldTry(CacheDirFor(root), session, DateTime.UtcNow)) return null;
            return SpendMeter.LineWithin(() => SpendLine(root, HookReadingWait, HookScanBudget), HookLineBudget, e => LogHookError(hookErrorLog, o, e));
        }
        catch (Exception e) { LogHookError(hookErrorLog, o, e); return null; }
    }

    private static string? JoinSay(string? first, string? second) =>
        first is null ? second : second is null ? first : first + "\n" + second;
}
