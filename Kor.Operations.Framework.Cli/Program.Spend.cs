using Kor.Operations.Framework.Spend;

namespace Kor.Operations.Framework.Cli;

/// <summary>G6, the spend line (board row 1559): <c>kor spend</c> prints it, filling the count for as long as it takes, and
/// <c>kor context --hook</c> carries it (<see cref="SpendLineForHook"/>). The rules, what it reads and what it does not cover
/// are <see cref="SpendMeter"/>'s header.</summary>
public static partial class Program
{
    // The whole line gets HookLineBudget in a hook, because the hook's output waits on it, G5's WARN and STOP included, and the
    // hook's timeout is 10 s (review rows 501, 503). Past it the line is left out this time; G5's text still goes.
    private static readonly TimeSpan HookLineBudget = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HookScanBudget = TimeSpan.FromSeconds(1.5);

    private static int SpendVerb(Options o, TextWriter output)
    {
        output.WriteLine(SpendLine(o.Get("--spend-root"), TimeSpan.FromMinutes(10)));
        return 0;
    }

    /// <summary>The newest recorded screen reading. Failed true: the DB could not say, which is not the same as none recorded.</summary>
    private static (ScreenReading? Reading, bool Failed) NewestReading()
    {
        try
        {
            return (BrainDb.Use3s(KorStandardsConnection.Login.Brain, d =>
            {
                using var c = d.Command("SELECT TOP 1 Description FROM brain.Memory WHERE Lane = N'Framework' AND Name = @n AND RetiredAtUtc IS NULL AND Kind <> N'issue'", 3);
                c.Parameters.Add("@n", System.Data.SqlDbType.NVarChar, 120).Value = SpendMeter.ReadingRecord;
                return SpendMeter.ParseReading(c.ExecuteScalar() as string);
            }), false);
        }
        catch (Exception e) when (e is Microsoft.Data.SqlClient.SqlException or InvalidOperationException or ArgumentException) { return (null, true); }
    }

    private static string SpendLine(string? root, TimeSpan budget)
    {
        var (reading, failed) = root is null ? NewestReading() : (null, false);   // a test's root has no record behind it
        var count = SpendMeter.Count(root ?? SpendMeter.DefaultProjectsRoot, CacheDirFor(root), DateTimeOffset.Now, reading?.At, budget);
        return SpendMeter.Line(count, reading, failed);
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
            // a prompt always says it and restarts the session's ten minutes, so the tool call after it does not repeat it
            if (prompt) { try { SpendMeter.Stamp(CacheDirFor(root), session, DateTime.UtcNow); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
            else if (!SpendMeter.ShouldSay(CacheDirFor(root), session, DateTime.UtcNow)) return null;
            var line = Task.Run(() => SpendLine(root, HookScanBudget));
            return line.Wait(HookLineBudget) ? line.Result : null;
        }
        catch (Exception e) { LogHookError(hookErrorLog, o, e); return null; }
    }

    private static string? JoinSay(string? first, string? second) =>
        first is null ? second : second is null ? first : first + "\n" + second;
}
