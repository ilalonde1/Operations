using Kor.Operations.Framework.Spend;

namespace Kor.Operations.Framework.Cli;

/// <summary>Board row 1892: <c>kor statusline</c>. Reads Claude Code's status line JSON on stdin, prints one short line, and stores a
/// reading at most once per <see cref="StatusLine.StoreEvery"/>. Always exits 0. The rules are <see cref="StatusLine"/>'s header.</summary>
public static partial class Program
{
    // Claude Code cancels a status line that a newer update overtakes, so the line goes first and the store has a limit of its own.
    // A store that misses it is logged and tried again five minutes on: the claim is taken before the try, so a DB that is down
    // costs one render per five minutes.
    private static readonly TimeSpan StatusLineStoreLimit = TimeSpan.FromSeconds(2.5);
    private const string StoreClaim = "usage-reading";

    private static int StatusLineVerb(Options o, Func<string?> stdin, TextWriter output, BrainDb? db, string hookErrorLog)
    {
        var dir = CacheDirFor(o.Get("--spend-root"));
        StatusInput? input = null;
        try
        {
            input = StatusLine.Parse(stdin());
            output.WriteLine(StatusLine.Text(input));
        }
        catch (Exception e)
        {
            output.WriteLine("[kor]");
            LogOnce(dir, hookErrorLog, o, e);
        }
        try
        {
            output.Flush();
            if (input is null || !StatusLine.HasReading(input, DateTimeOffset.UtcNow) || !SpendMeter.ShouldTry(dir, StoreClaim, DateTime.UtcNow, StatusLine.StoreEvery)) return 0;
            var store = Task.Run(() => db is not null
                ? StatusLine.Store(input, db)
                : BrainDb.Use3s(KorStandardsConnection.Login.Brain, d => StatusLine.Store(input, d)));
            if (!store.Wait(StatusLineStoreLimit))
            {
                store.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                throw new TimeoutException($"the reading's store took over {StatusLineStoreLimit.TotalSeconds:0.#} s and was left");
            }
            ForgetLogged(dir);
        }
        catch (Exception e) { LogOnce(dir, hookErrorLog, o, e is AggregateException { InnerException: { } inner } ? inner : e); }
        return 0;
    }

    private static string LoggedMarker(string dir) => Path.Combine(dir, "statusline-logged");

    /// <summary>The same failure is logged once, however many renders meet it; a store that works clears it.</summary>
    private static void LogOnce(string dir, string hookErrorLog, Options o, Exception e)
    {
        try
        {
            var text = e.GetType().Name + ": " + e.Message;
            var marker = LoggedMarker(dir);
            if (File.Exists(marker) && File.ReadAllText(marker) == text) return;
            Directory.CreateDirectory(dir);
            File.WriteAllText(marker, text);
            LogHookError(hookErrorLog, o, e);
        }
        catch { /* a status line never fails, not even on its own log */ }
    }

    private static void ForgetLogged(string dir)
    {
        try { File.Delete(LoggedMarker(dir)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The session's arguments before its settings and tools. It names its model (row 1892); --model picks another.</summary>
    internal static List<string> StartProofArgs(string? model) => new()
    {
        "-p", "--model", model ?? "opus", "--output-format", "json", "--permission-mode", "dontAsk", "--strict-mcp-config", "--effort", "low", "--max-turns", "1",
    };
}
