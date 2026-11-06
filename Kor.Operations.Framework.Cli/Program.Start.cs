using System.Text.Json;
using System.Text.Json.Serialization;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Start;

namespace Kor.Operations.Framework.Cli;

/// <summary>
/// K6 - known at start (Framework docs/KNOWLEDGE-IDE-SPEC-K5-K6.md). It replaces board_render.py and kor_brain.py render
/// in every KOR repo's SessionStart:
/// <c>kor start --module M [--owner O]... [--no-helpers] [--tag T] [--part record|state|memory|records|board|all] [--page N --pages M] [--hook | --text] [--dir D]
/// [--session S] [--start-source startup|resume|clear|compact|fork] [--cache-dir D]</c>.
/// <c>--hook</c> reads the SessionStart JSON on stdin (session_id, source; garbage is ignored) and prints ONE part's
/// SessionStart JSON (additionalContext at most 9,900 chars; the record part adds watchPaths). Without it, plain text, uncut.
/// Board row 1198 (3 Nov): Claude Code caps EACH hook's additionalContext at 10,000 chars, separately, so the hook's
/// record part leaves STATE NOW and LEGACY MEMORY to parts of their own: <c>--part state</c> (the handover's STATE NOW
/// whole) and <c>--part memory --page N --pages M</c> (the lane's imported memory index, split at whole lines; a page past
/// the content prints nothing). 3 Nov: <c>--part records --page N --pages M</c> carries the module's records the same way
/// (the record part names them with their counts); with <c>--text</c> and a --page it prints that one page, without one
/// every record. <c>--text</c> and <c>--part all</c> stay one page with every section.
/// Row 1569 (5 Nov): <c>--part pages --page N --pages W</c> is THE PAGES, the whole memory on as many pages as it needs (<see cref="StartRender.PagesBody"/>),
/// with <c>--part record --pages-wired W</c> beside it; state, memory and records stay only for the old wiring, until a repo's settings move.
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
        if (part is not ("record" or "state" or "memory" or "records" or "pages" or "board" or "all"))
            throw new ArgumentException($"--part is record, state, memory, records, pages, board or all, not '{part}'");
        if (hook && o.Get("--text") is not null)
            throw new ArgumentException("--hook prints the SessionStart JSON and --text prints plain text: give one (review row 194)");
        if (hook && part == "all")
            throw new ArgumentException("--hook prints ONE part's SessionStart JSON: --part record, state, memory, records or board (one hook entry each)");
        int Count(string name) => o.Get(name) is null ? 1
            : o.Int(name) ?? throw new ArgumentException($"{name} takes a whole number, not '{o.Get(name)}'");
        var pages = Count("--pages");
        var page = Count("--page");
        var paged = o.Get("--page") is not null || o.Get("--pages") is not null;
        if (part is not ("memory" or "records" or "pages") && paged)
            throw new ArgumentException("--page and --pages belong to --part memory, records or pages");
        var pagesWired = o.Get("--pages-wired") is null ? (int?)null
            : o.Int("--pages-wired") ?? throw new ArgumentException($"--pages-wired takes a whole number, not '{o.Get("--pages-wired")}'");
        if (pagesWired is not null && part != "record") throw new ArgumentException("--pages-wired belongs to --part record");
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
        var exe = (Environment.ProcessPath ?? "kor").Replace('\\', '/');
        // row 1569: every hook of one start prints from the one read of THE PAGES (StartPages)
        PagesSnapshot SharedPages() => StartPages.Shared(cacheDir, module, session, source, hook, StartPart.Deadline, () =>
        {
            var start = new ModuleState.Start(session, source, transcript);
            var (side, resume, end) = db is not null ? ModuleState.ReadPages(db, module, start, dir)
                : BrainDb.Read(KorStandardsConnection.Login.Brain, d => ModuleState.ReadPages(d, module, start, dir));
            return StartRender.BuildPages(side, resume, end);
        }, () => DateTime.Now);
        if (part == "pages")
        {
            var r = StartPart.Run(() => (paged || hook ? StartRender.Page(SharedPages(), page, pages, exe, session) : StartRender.WholePages(SharedPages()),
                                         (IReadOnlyList<string>)Array.Empty<string>()),
                StartCache.PathFor(cacheDir, module, $"pages{page}of{pages}{(paged ? "" : "all")}", hook), budget, deadline,
                $"read the memory by hand: \"{exe}\" start --module \"{module}\" --part pages --text", () => DateTime.Now, Log);
            texts.Add(SecretGuard.Mask(r.Text)!);
        }
        if (part is "record" or "all")
        {
            var start = new ModuleState.Start(session, source, transcript);
            if (source == "compact" && session is not null) StartGate.Rearm(cacheDir, session);   // a compaction loses the bodies
            (string, IReadOnlyList<string>) Show(ModuleState s)
            {
                // the hook's record part is split (board row 1198): STATE NOW and LEGACY MEMORY are their own parts
                var wiring = pagesWired is { } wired ? new PagesWiring(wired, SharedPages().Pages.Count) : null;
                var rendered = (StartRender.Record(s, budget, split: hook, wiring, exe, session), StartRender.WatchPaths(s.Fresh));
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
        if (part is "state" or "memory" or "records")
        {
            // board row 1198: a side part - its own hook entry, its own 9,900 - only READS (SNAPSHOT, never committed)
            var state = part == "state";
            // records (the lead, 3 Nov): paged at the hook's 9,900 in the hook AND in --text when --page/--pages is given (one page
            // as the session reads it); --text without them prints every record
            var records = part == "records";
            int? pageBudget = budget ?? (records && paged ? StartRender.HookBudget : null);
            string Render(StartSide side) => state ? StartRender.State(side, budget)
                : records ? StartRender.RecordsPage(side, page, pages, pageBudget) : StartRender.Memory(side, page, pages, budget);
            var r = StartPart.Run(() => (db is not null ? Render(ModuleState.ReadSide(db, module, dir, state))
                                         : Render(BrainDb.Read(KorStandardsConnection.Login.Brain, d => ModuleState.ReadSide(d, module, dir, state))),
                                         (IReadOnlyList<string>)Array.Empty<string>()),
                StartCache.PathFor(cacheDir, module, state ? "state" : records ? $"records{page}of{pages}{(paged ? "" : "all")}" : $"memory{page}of{pages}", hook), budget, deadline,
                state ? $"read the handover's STATE NOW by hand (kor start --module \"{module}\" --part state --text)"
                      : records ? $"read the lane's records by hand: kor start --module \"{module}\" --part records --text (or kor memory --module \"{module}\")"
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
                // review row 311: a tag-only board has its own cache and its own by-hand line (never the whole vw_Board)
                StartCache.PathFor(cacheDir, module, BoardRender.CachePart(tag, tagOnly), hook), budget, deadline,
                BoardRender.ByHand(tag, tagOnly, command), () => DateTime.Now, Log);
            texts.Add(SecretGuard.Mask(r.Text)!);   // mistake 138: a page from the cache, written before the guard, is masked too
        }
        // a memory page past the lane's content has nothing to say: the hook prints nothing (Claude Code adds no context).
        // After every part, so it reads the one part this hook ran (review row 247: placed before the board block it threw
        // on --part board, whose failure a hook logs and exits 0 on - THE BOARD would have vanished from every start).
        if (hook && part is "memory" or "records" or "pages" && texts.Count == 1 && texts[0].Length == 0) return 0;
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

    /// <summary>
    /// Row 1569: <c>kor read --module M --subject "words" [--page N] [--all]</c> - the bodies for a session's subject, whole, one
    /// page at a time (<see cref="BodyRead"/>). It reads and writes nothing else.
    /// </summary>
    private static int ReadVerb(Options o, TextWriter output, BrainDb? db)
    {
        var module = o.Get("--module") ?? throw new ArgumentException("read needs --module <lane>");
        var subject = o.Get("--subject") ?? throw new ArgumentException("read needs --subject \"<words for your task>\"");
        if (BodyRead.Terms(subject).Count == 0) throw new ArgumentException("--subject needs a word of two letters or more");
        var page = o.Get("--page") is null ? 1 : o.Int("--page") ?? throw new ArgumentException($"--page takes a whole number, not '{o.Get("--page")}'");
        var rows = db is not null ? ModuleState.ReadBodies(db, module)
            : BrainDb.Read(KorStandardsConnection.Login.Brain, d => ModuleState.ReadBodies(d, module));
        var exe = (Environment.ProcessPath ?? "kor").Replace('\\', '/');
        output.WriteLine(BodyRead.Pages(BodyRead.Match(rows, subject), subject, module, exe, page, o.Get("--all") is not null, out _));
        return 0;
    }

    /// <summary>
    /// Row 1569: <c>kor start-gate --module M --hook</c> on PreToolUse, every tool. Allow at once when this session's gate is open
    /// (a marker file, no transcript read). Else: <see cref="StartGate"/> on the session's own transcript. Bad input, a subagent, no
    /// transcript, KOR_START_GATE=off: allow, nothing printed. Exit 0 always in hook mode; never reads the database.
    /// </summary>
    private static int StartGateVerb(Options o, Func<string?> stdin, TextWriter output, bool hook)
    {
        void Say(string text) { if (!hook) output.WriteLine(text); }
        var module = o.Get("--module") ?? throw new ArgumentException("start-gate needs --module <lane>");
        if (StartGate.Lifted(Environment.GetEnvironmentVariable)) { Say("allow: KOR_START_GATE=off"); return 0; }
        var raw = stdin();
        if (string.IsNullOrWhiteSpace(raw)) { Say("allow: no hook input"); return 0; }
        string? tool, session, transcript, agent, command;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { Say("allow: the input is not a JSON object"); return 0; }
            tool = Str(root, "tool_name");
            session = Str(root, "session_id");
            transcript = Str(root, "transcript_path");
            agent = Str(root, "agent_id");
            command = StartGate.CommandOf(root);
        }
        catch (JsonException) { Say("allow: the input is not JSON"); return 0; }
        if (tool is null || session is null || agent is not null || transcript is null || !File.Exists(transcript)) { Say("allow: no session transcript, or a subagent"); return 0; }
        var cacheDir = o.Get("--cache-dir") ?? StartCache.DefaultDir;
        var open = StartGate.OpenMarker(cacheDir, session);
        if (File.Exists(open)) { Say("allow: this session's gate is open"); return 0; }
        if (StartGate.IsReadCommand(tool, command)) { Say("allow: the read the gate asks for"); return 0; }
        var exe = (Environment.ProcessPath ?? "kor").Replace('\\', '/');
        var d = StartGate.Decide(StartGate.Scan(StartGate.ReadLines(transcript), module), module, exe, session);
        var deniedPath = StartGate.DeniedMarker(cacheDir, session);
        void Mark(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        if (d.Allow)
        {
            Mark(open, d.Reason);
            Say("allow: " + d.Reason);
            return 0;
        }
        var denied = File.Exists(deniedPath) && int.TryParse(File.ReadAllText(deniedPath), out var n) ? n : 0;
        if (denied >= StartGate.MaxDenials)
        {
            // never wedge a session: it opens, and says what is not read
            Mark(open, "gave up: " + d.Reason);
            var text = $"START GATE OPENED after {denied} refusals WITHOUT the read it asks for: {d.Reason} What that would hold is NOT read: answer it 'not checked'.";
            output.WriteLine(hook ? JsonSerializer.Serialize(new { hookSpecificOutput = new { hookEventName = "PreToolUse", additionalContext = text } }) : "allow: " + text);
            return 0;
        }
        var lastAt = File.Exists(deniedPath) ? File.GetLastWriteTime(deniedPath) : (DateTime?)null;
        var next = StartGate.NextDenied(denied, lastAt, DateTime.Now);
        if (next != denied || lastAt is null) Mark(deniedPath, next.ToString(System.Globalization.CultureInfo.InvariantCulture));
        output.WriteLine(hook
            ? JsonSerializer.Serialize(new { hookSpecificOutput = new { hookEventName = "PreToolUse", permissionDecision = "deny", permissionDecisionReason = d.Reason } })
            : "deny: " + d.Reason);
        return 0;
    }
}
