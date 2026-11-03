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
/// Board row 1198 (3 Nov): Claude Code caps EACH hook's additionalContext at 10,000 chars, separately, so the hook's
/// record part leaves STATE NOW and LEGACY MEMORY to parts of their own: <c>--part state</c> (the handover's STATE NOW
/// whole) and <c>--part memory --page N --pages M</c> (the lane's imported memory index, split at whole lines; a page past
/// the content prints nothing). <c>--text</c> and <c>--part all</c> stay one page with every section.
/// </summary>
public static partial class Program
{
    /// <summary>kor start's write phase waits at most this long for any one lock (SET LOCK_TIMEOUT), then records nothing.</summary>
    internal static readonly TimeSpan StartWriteLockWait = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions HookJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private static int StartVerb(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainDb? db, string hookErrorLog)
    {
        StartTrace.Mark("begin");
        var module = o.Get("--module") ?? throw new ArgumentException("start needs --module <lane>");
        var part = o.Get("--part") ?? "all";
        if (part is not ("record" or "state" or "memory" or "board" or "all"))
            throw new ArgumentException($"--part is record, state, memory, board or all, not '{part}'");
        if (hook && o.Get("--text") is not null)
            throw new ArgumentException("--hook prints the SessionStart JSON and --text prints plain text: give one (review row 194)");
        if (hook && part == "all")
            throw new ArgumentException("--hook prints ONE part's SessionStart JSON: --part record, state, memory or board (one hook entry each)");
        int Count(string name) => o.Get(name) is null ? 1
            : o.Int(name) ?? throw new ArgumentException($"{name} takes a whole number, not '{o.Get(name)}'");
        var pages = Count("--pages");
        var page = Count("--page");
        if (part != "memory" && (o.Get("--page") is not null || o.Get("--pages") is not null))
            throw new ArgumentException("--page and --pages belong to --part memory");
        if (pages < 1 || page < 1 || page > pages) throw new ArgumentException($"--page {page} --pages {pages}: need 1 <= page <= pages");
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
            (string, IReadOnlyList<string>) Show(ModuleState s)
            {
                // the hook's record part is split (board row 1198): STATE NOW and LEGACY MEMORY are their own parts
                var rendered = (StartRender.Record(s, budget, split: hook), StartRender.WatchPaths(s.Fresh));
                StartTrace.Mark("rendered");
                return rendered;
            }
            // The installed kor: (1) READS in a SNAPSHOT transaction that writes nothing (BrainDb.Read: READ COMMITTED if
            // the database refuses snapshot) - found 3 Nov: while a kor test suite held rolled-back transactions, starts
            // came back PART LATE and "UNREADABLE: deadlocked"; (2) WRITES the freshness verdicts and query-check results in
            // a unit of work of its own that waits at most WriteLockWait for a lock, and commits ONLY through the gate - a
            // part the deadline already called late rolls back (review row 193). A write that meets another session's open
            // transaction records nothing and the page says so; the page is the read's either way.
            // A caller's unit of work (a test) does both on it and is never committed.
            var r = StartPart.Run(gate =>
                {
                    if (db is not null) return Show(ModuleState.Load(db, module, start, dir, freshnessCap: TimeSpan.FromSeconds(5)));
                    var read = BrainDb.Read(KorStandardsConnection.Login.Brain,
                                            d => ModuleState.Read(d, module, start, dir, freshnessCap: TimeSpan.FromSeconds(5)));
                    ModuleState shown;
                    try
                    {
                        shown = BrainDb.RetryOnceOnDeadlock(() =>
                        {
                            using var w = BrainDb.Open();
                            w.SetLockTimeout((int)StartWriteLockWait.TotalMilliseconds);
                            var s = ModuleState.Record(w, read, start.SessionId);
                            if (!gate.TryCommit(w.Commit)) throw new OperationCanceledException("kor start: the record part was late - rolled back");
                            return s;
                        }, BrainDb.IsDeadlock, Thread.Sleep);
                    }
                    catch (Exception e) when (BrainDb.IsBlockedWrite(e))
                    {
                        Log(e);
                        shown = ModuleState.NotRecorded(read, $"another session's open transaction held a lock: SQL {((Microsoft.Data.SqlClient.SqlException)e).Number}");
                    }
                    return Show(shown);
                },
                StartCache.PathFor(cacheDir, module, "record", hook), budget, deadline,
                $"read the record by hand: kor memory --module \"{module}\" and kor events --module \"{module}\"", () => DateTime.Now, Log);
            texts.Add(SecretGuard.Mask(r.Text)!);   // mistake 138: a page from the cache, written before the guard, is masked too
            watch = r.WatchPaths;
        }
        if (part is "state" or "memory")
        {
            // board row 1198: a side part - its own hook entry, its own 9,900 - only READS (SNAPSHOT, never committed)
            var state = part == "state";
            string Render(StartSide side) => state ? StartRender.State(side, budget) : StartRender.Memory(side, page, pages, budget);
            var r = StartPart.Run(() => (db is not null ? Render(ModuleState.ReadSide(db, module, dir, state))
                                         : Render(BrainDb.Read(KorStandardsConnection.Login.Brain, d => ModuleState.ReadSide(d, module, dir, state))),
                                         (IReadOnlyList<string>)Array.Empty<string>()),
                StartCache.PathFor(cacheDir, module, state ? "state" : $"memory{page}of{pages}", hook), budget, deadline,
                state ? $"read the handover's STATE NOW by hand (kor start --module \"{module}\" --part state --text)"
                      : $"read the lane's memory by hand: kor start --module \"{module}\" --part memory --text", () => DateTime.Now, Log);
            texts.Add(SecretGuard.Mask(r.Text)!);
        }
        if (part is "board" or "all")
        {
            var noHelpers = o.Get("--no-helpers") is not null;
            // --tag T with --owner: only the owners' rows tagged T (BoardState.Tagged), the header saying so with the full open
            // count. --tag T with NO --owner (the lead, 3 Nov: one product owner, tag-filtered board): every open row of ANY
            // owner tagged T, and no owner named (BoardState.LoadTagged) - the confidentiality wall's board.
            var tag = o.Get("--tag") is { } t ? (string.IsNullOrWhiteSpace(t) ? throw new ArgumentException("--tag needs a name") : t) : null;
            var tagOnly = tag is not null && o.Owners.Count == 0;
            if (tagOnly && noHelpers) throw new ArgumentException("--no-helpers belongs with --owner: --tag alone reads every owner's tagged rows");
            var owners = o.Owners.Count > 0 ? o.Owners : tagOnly ? new List<string>() : new List<string> { module };
            var command = BoardRender.FullBoardCommand(module, BoardState.Distinct(owners), noHelpers, tag);
            var r = StartPart.Run(() =>
                {
                    // the board is only READ: SNAPSHOT (READ COMMITTED if refused), never committed
                    var state = BrainDb.Read(KorStandardsConnection.Login.Board,
                                             b => tagOnly ? BoardState.LoadTagged(b, tag!) : BoardState.Load(b, owners, withHelpers: !noHelpers));
                    if (tag is not null && !tagOnly) state = state.FilterByTag(tag);
                    return (BoardRender.Render(state, DateTime.Now, budget, command), (IReadOnlyList<string>)Array.Empty<string>());
                },
                StartCache.PathFor(cacheDir, module, "board", hook), budget, deadline,
                "read the board by hand (Operations tools/SqlQuery/oq.py: oversight.vw_Board)", () => DateTime.Now, Log);
            texts.Add(SecretGuard.Mask(r.Text)!);   // mistake 138: a page from the cache, written before the guard, is masked too
        }
        // a memory page past the lane's content has nothing to say: the hook prints nothing (Claude Code adds no context).
        // After every part, so it reads the one part this hook ran (review row 247: placed before the board block it threw
        // on --part board, whose failure a hook logs and exits 0 on - THE BOARD would have vanished from every start).
        if (hook && part == "memory" && texts.Count == 1 && texts[0].Length == 0) return 0;
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
