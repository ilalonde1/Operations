using System.Text.Json;
using System.Text.Json.Serialization;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Start;

namespace Kor.Operations.Framework.Cli;

/// <summary>
/// K6 - known at start (Framework docs/KNOWLEDGE-IDE-SPEC-K5-K6.md). It replaces board_render.py and kor_brain.py render
/// in every KOR repo's SessionStart:
/// <c>kor start --module M [--owner O]... [--no-helpers] [--part record|board|all] [--hook | --text] [--dir D]
/// [--session S] [--start-source startup|resume|clear|compact|fork] [--cache-dir D]</c>.
/// <c>--hook</c> reads the SessionStart JSON on stdin (session_id, source; garbage is ignored) and prints ONE part's
/// SessionStart JSON (additionalContext at most 9,900 chars; the record part adds watchPaths). Without it, plain text, uncut.
/// </summary>
public static partial class Program
{
    private static readonly JsonSerializerOptions HookJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private static int StartVerb(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainDb? db, string hookErrorLog)
    {
        StartTrace.Mark("begin");
        var module = o.Get("--module") ?? throw new ArgumentException("start needs --module <lane>");
        var part = o.Get("--part") ?? "all";
        if (part is not ("record" or "board" or "all")) throw new ArgumentException($"--part is record, board or all, not '{part}'");
        if (hook && o.Get("--text") is not null)
            throw new ArgumentException("--hook prints the SessionStart JSON and --text prints plain text: give one (review row 194)");
        if (hook && part == "all")
            throw new ArgumentException("--hook prints ONE part's SessionStart JSON: --part record or --part board (one hook entry each)");
        string? session = o.Get("--session"), source = o.Get("--start-source"), transcript = null;
        var dir = o.Get("--dir");
        if (hook && stdin() is { } raw)
        {
            try
            {
                var h = ClaudeCapture.Parse(raw);
                session ??= h.SessionId;
                source ??= h.Source;
                transcript = h.TranscriptPath;
                if (dir is null && h.Cwd is { } cwd && Directory.Exists(cwd)) dir = cwd;
            }
            catch (JsonException) { /* not the hook's JSON: start with no session (the startup rule) */ }
        }
        var cacheDir = o.Get("--cache-dir") ?? StartCache.DefaultDir;
        int? budget = hook ? StartRender.HookBudget : null;
        // A caller's unit of work (a test) is always waited out; the installed kor.exe gives each part 8 s.
        var deadline = db is not null ? Timeout.InfiniteTimeSpan : StartPart.Deadline;
        void Log(Exception e) => LogHookError(hookErrorLog, o, e);
        var texts = new List<string>();
        IReadOnlyList<string>? watch = null;
        if (part is "record" or "all")
        {
            var start = new ModuleState.Start(session, source, transcript);
            (string, IReadOnlyList<string>) Work(BrainDb d)
            {
                var s = ModuleState.Load(d, module, start, dir, freshnessCap: TimeSpan.FromSeconds(5));
                var rendered = (StartRender.Record(s, budget), StartRender.WatchPaths(s.Fresh));
                StartTrace.Mark("rendered");
                return rendered;
            }
            // The installed kor commits its freshness writes and query-check results ONLY through the gate: a part the
            // deadline already called late rolls back (review row 193). A caller's unit of work (a test) is never committed.
            var r = StartPart.Run(gate => db is not null ? Work(db) : BrainDb.RetryOnceOnDeadlock(() =>
                {
                    using var own = BrainDb.Open();
                    var result = Work(own);
                    if (!gate.TryCommit(own.Commit)) throw new OperationCanceledException("kor start: the record part was late - rolled back");
                    return result;
                }, BrainDb.IsDeadlock, Thread.Sleep),
                StartCache.PathFor(cacheDir, module, "record", hook), budget, deadline,
                $"read the record by hand: kor memory --module \"{module}\" and kor events --module \"{module}\"", () => DateTime.Now, Log);
            texts.Add(r.Text);
            watch = r.WatchPaths;
        }
        if (part is "board" or "all")
        {
            var owners = o.Owners.Count > 0 ? o.Owners : new List<string> { module };
            var noHelpers = o.Get("--no-helpers") is not null;
            var command = BoardRender.FullBoardCommand(module, BoardState.Distinct(owners), noHelpers);
            var r = StartPart.Run(() =>
                {
                    // the board is only READ: its unit of work is never committed
                    using var b = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
                    var state = BoardState.Load(b, owners, withHelpers: !noHelpers);
                    return (BoardRender.Render(state, DateTime.Now, budget, command), (IReadOnlyList<string>)Array.Empty<string>());
                },
                StartCache.PathFor(cacheDir, module, "board", hook), budget, deadline,
                "read the board by hand (Operations tools/SqlQuery/oq.py: oversight.vw_Board)", () => DateTime.Now, Log);
            texts.Add(r.Text);
        }
        if (hook)
        {
            var payload = new
            {
                hookSpecificOutput = new
                {
                    hookEventName = "SessionStart",
                    additionalContext = texts[0],
                    watchPaths = part == "record" && watch is { Count: > 0 } ? watch : null,
                },
            };
            output.WriteLine(JsonSerializer.Serialize(payload, HookJson));
        }
        else output.WriteLine(string.Join("\n\n", texts));
        return 0;
    }
}
