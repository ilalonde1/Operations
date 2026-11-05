using Kor.Operations.Framework.Records;
using Kor.Operations.Framework.Spend;

namespace Kor.Operations.Framework.Cli;

/// <summary>G6, the spend line (board row 1559): <c>kor spend</c> prints it, filling the count for as long as it takes, and
/// <c>kor context --hook</c> carries it (<see cref="SpendLineForHook"/>). The rules, what it reads and what it does not cover
/// are <see cref="SpendMeter"/>'s header.</summary>
public static partial class Program
{
    private static readonly TimeSpan HookScanBudget = TimeSpan.FromSeconds(2.5);

    private static int SpendVerb(Options o, TextWriter output)
    {
        output.WriteLine(SpendLine(o.Get("--spend-root"), TimeSpan.FromMinutes(10)));
        return 0;
    }

    /// <summary>The newest recorded screen reading (a DB read of at most 3 s; none or a failure: no reading, and the line says so).</summary>
    private static ScreenReading? NewestReading()
    {
        try
        {
            return BrainDb.Use3s(KorStandardsConnection.Login.Brain, d => SpendMeter.ParseReading(RecordStore.LiveByName(d, "Framework", SpendMeter.ReadingRecord)?.Description));
        }
        catch (Exception e) when (e is Microsoft.Data.SqlClient.SqlException or InvalidOperationException or ArgumentException) { return null; }
    }

    private static string SpendLine(string? root, TimeSpan budget)
    {
        var reading = root is null ? NewestReading() : null;   // a test's root has no record behind it
        var count = SpendMeter.Count(root ?? SpendMeter.DefaultProjectsRoot, CacheDirFor(root), DateTimeOffset.Now, reading?.At, budget);
        return SpendMeter.Line(count, reading);
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
            // a prompt always says it, and starts the session's ten minutes, so the tool call after it does not repeat it
            if (!SpendMeter.ShouldSay(CacheDirFor(root), session, DateTime.UtcNow) && !prompt) return null;
            return SpendLine(root, HookScanBudget);
        }
        catch (Exception e) { LogHookError(hookErrorLog, o, e); return null; }
    }

    private static string? JoinSay(string? first, string? second) =>
        first is null ? second : second is null ? first : first + "\n" + second;
}
