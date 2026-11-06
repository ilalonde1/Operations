using System.Text;
using System.Text.Json;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Enforce;
using Kor.Operations.Framework.Records;

namespace Kor.Operations.Framework.Cli;

/// <summary>
/// K7 - enforced (Framework docs/KNOWLEDGE-IDE-SPEC-K4-K7.md, P5): a recorded issue or lesson with a check refuses the
/// repeat. Verbs: <c>kor enforce --hook</c> (PreToolUse, Bash|PowerShell|Write|Edit|MultiEdit|NotebookEdit), <c>check-add</c>, <c>check-retire</c>,
/// <c>check-test</c>, <c>checks</c>. And mistake 138's read-only <c>secret-scan</c> (ids and counts only).
/// </summary>
public static partial class Program
{
    // ---- kor enforce --hook -------------------------------------------------------------------------------------------
    /// <summary>
    /// PreToolUse. Reads the hook JSON (tool_name, tool_input.command | file_path, cwd, session_id). The first live check for
    /// this module or Office whose known answers pass and whose pattern matches -> the deny JSON naming brain.Check #id and
    /// brain.Memory #id, and ONE oversight.GuardRefusal row (Board login; Matched through SecretGuard). A check failing its
    /// known answers is skipped and said in the hook error log. Bad input, another tool, no DB and no cache -> allow
    /// (nothing printed). Exit 0 always in hook mode.
    /// </summary>
    private static int EnforceVerb(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainDb? db, BrainDb? board, string hookErrorLog)
    {
        var module = o.Get("--module") ?? throw new ArgumentException("enforce needs --module <lane>");
        var raw = stdin();
        if (string.IsNullOrWhiteSpace(raw)) { if (!hook) output.WriteLine("allow: no hook input"); return 0; }
        string? tool, input, cwd, session, scratch;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { if (!hook) output.WriteLine("allow: the input is not a JSON object"); return 0; }
            tool = Str(root, "tool_name");
            cwd = Str(root, "cwd");
            session = Str(root, "session_id");
            scratch = Str(root, "scratchpad_dir");
            input = null;
            if (tool is not null && root.TryGetProperty("tool_input", out var ti) && ti.ValueKind == JsonValueKind.Object)
                input = Str(ti, Rules.InputFieldOf(tool));
        }
        catch (JsonException) { if (!hook) output.WriteLine("allow: the input is not JSON"); return 0; }
        if (tool is null || !Rules.Enforced(tool) || string.IsNullOrEmpty(input))
        {
            if (!hook) output.WriteLine($"allow: tool {tool ?? "-"} is not enforced or has no {(tool is null ? "input" : Rules.InputFieldOf(tool))}");
            return 0;
        }
        void Log(Exception e) => LogHookError(hookErrorLog, o, e);
        if (DrafterPythonRefusal(o, tool, input, cwd, session, scratch, output, hook, board, Log)) return 0;
        IReadOnlyList<LiveCheck>? checks;
        string source;
        if (db is not null) { checks = CheckStore.Live(db).Select(s => s.Check).ToList(); source = "db (caller's)"; }
        else
            checks = CheckCache.Get(o.Get("--cache-dir") ?? CheckCache.DefaultDir,
                () => BrainDb.Read3s(d => CheckStore.Live(d).Select(s => s.Check).ToList()), DateTimeOffset.UtcNow, Log, out source);
        if (checks is null)
        {
            Log(new InvalidOperationException("kor enforce: no database and no cache - nothing enforced (fails open)"));
            if (!hook) output.WriteLine("allow: no database and no cache - nothing enforced (fails open)");
            return 0;
        }
        var dir = cwd is not null && Directory.Exists(cwd) ? cwd : null;
        foreach (var c in CheckStore.For(checks, module).Where(c => Rules.Applies(c.Tool, tool)).OrderBy(c => c.Id))
        {
            var fails = Rules.KnownAnswerFailures(c);
            if (fails.Count > 0)
            {
                Log(new InvalidOperationException($"brain.Check #{c.Id} skipped - it fails its known answers: {string.Join("; ", fails)}"));
                continue;
            }
            var m = Rules.Matches(c, input, dir);
            if (m is null) continue;
            var shown = SecretGuard.Mask(m.Text)!;
            var reason = $"kor enforce: brain.Check #{c.Id} ({c.Module}, enforcing {c.MemoryKind} brain.Memory #{c.MemoryId} {c.MemoryName}) refuses this: {c.Says} " +
                         $"Matched{(m.Origin == "command" ? "" : $" in {m.Origin}")}: {Clip(shown, 300)}. " +
                         $"A mention (in quotes, a commit message, a heredoc) is never matched. If the check is wrong: kor check-retire {c.Id} --because \"...\".";
            int? logged = null;
            try
            {
                var repo = dir is null ? null : Git.TopLevelFast(dir) is { } top ? Path.GetFileName(top.TrimEnd('/', '\\')) : null;
                logged = board is not null
                    ? CheckStore.LogRefusal(board, c.Id, session, repo, m.Text)
                    : BrainDb.Use3s(KorStandardsConnection.Login.Board, b => CheckStore.LogRefusal(b, c.Id, session, repo, m.Text));
            }
            catch (Exception e) { Log(e); }   // the refusal stands whether or not it was logged
            output.WriteLine(hook
                ? JsonSerializer.Serialize(new
                {
                    hookSpecificOutput = new { hookEventName = "PreToolUse", permissionDecision = "deny", permissionDecisionReason = reason },
                })
                : $"deny{(logged is null ? " (not logged)" : $" (oversight.GuardRefusal #{logged})")}: {reason}");
            return 0;
        }
        if (!hook) output.WriteLine($"allow: no live check for {module} or Office matches ({checks.Count} live, read from {source})");
        return 0;
    }

    /// <summary>Board row 1935, <see cref="DrafterPythonGuard"/>: true when it answered (deny, and one GuardRefusal row). It reads
    /// no database to decide; any fault in it is logged and allows.</summary>
    private static bool DrafterPythonRefusal(Options o, string tool, string input, string? cwd, string? session, string? scratch, TextWriter output, bool hook, BrainDb? board, Action<Exception> log)
    {
        DrafterPythonGuard.Refusal? r;
        try
        {
            r = DrafterPythonGuard.Decide(tool, input, cwd, scratch);
            if (r is null || DrafterPythonGuard.Approved(session, o.Get("--prompts-dir"))) return false;
        }
        catch (Exception e) { log(e); return false; }
        var reason = DrafterPythonGuard.Says(r);
        int? logged = null;
        try
        {
            var dir = cwd is not null && Directory.Exists(cwd) ? cwd : null;
            var repo = dir is null ? null : Git.TopLevelFast(dir) is { } top ? Path.GetFileName(top.TrimEnd('/', '\\')) : null;
            logged = board is not null
                ? Stall.StallStore.LogGuardRefusal(board, DrafterPythonGuard.GuardName, DrafterPythonGuard.RuleName, session, repo, $"{tool}: {r.How}: {r.Path}")
                : BrainDb.Use3s(KorStandardsConnection.Login.Board, b => Stall.StallStore.LogGuardRefusal(b, DrafterPythonGuard.GuardName, DrafterPythonGuard.RuleName, session, repo, $"{tool}: {r.How}: {r.Path}"));
        }
        catch (Exception e) { log(e); }   // the refusal stands whether or not it was logged
        output.WriteLine(hook
            ? JsonSerializer.Serialize(new { hookSpecificOutput = new { hookEventName = "PreToolUse", permissionDecision = "deny", permissionDecisionReason = reason } })
            : $"deny{(logged is null ? " (not logged)" : $" (oversight.GuardRefusal #{logged})")}: {reason}");
        return true;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 3)] + "...";

    // ---- check-add / check-retire / check-test / checks ----------------------------------------------------------------
    /// <summary>
    /// kor check-add --memory ID --module M|Office --tool Bash[,PowerShell] --pattern P --refuse E [--refuse E2 ...]
    /// --allow A [--allow A2 ...] --says S. One row per tool. Refuses a target that is not a live issue or lesson, and a rule
    /// that fails its known answers (each tool's), before anything is written.
    /// </summary>
    private static int CheckAdd(Options o, TextWriter output, BrainDb? db)
    {
        var memory = o.Int("--memory") ?? throw new ArgumentException("check-add needs --memory <brain.Memory id of the issue or lesson>");
        var module = o.Get("--module") ?? throw new ArgumentException("check-add needs --module <lane> | Office");
        var tools = (o.Get("--tool") ?? throw new ArgumentException("check-add needs --tool Bash|PowerShell|Write|Edit"))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pattern = o.Get("--pattern") ?? throw new ArgumentException("check-add needs --pattern <.NET regex>");
        var says = o.Get("--says") ?? throw new ArgumentException("check-add needs --says \"what to do instead\"");
        var refuse = o.All("--refuse");
        var allow = o.All("--allow");
        if (refuse.Count == 0 || allow.Count == 0) throw new ArgumentException("check-add needs at least one --refuse and one --allow example");
        var by = Who(o.Get("--session") ?? BrainStore.CurrentSession());
        var ids = BrainDb.Use(db, d => tools.Select(t => CheckStore.Add(d, memory, module, t, pattern, refuse, allow, says, by)).ToList());
        foreach (var (id, t) in ids.Zip(tools))
            output.WriteLine($"added brain.Check #{id}: {module} {t} enforcing brain.Memory #{memory} - {refuse.Count} refuse and {allow.Count} allow examples pass, and every mention of a refuse example is allowed");
        return 0;
    }

    private static int CheckRetire(Options o, TextWriter output, BrainDb? db)
    {
        if (!int.TryParse(o.Positional.ElementAtOrDefault(1), out var id)) throw new ArgumentException("check-retire needs a brain.Check id");
        var because = o.Get("--because") ?? throw new ArgumentException("check-retire needs --because \"...\"");
        BrainDb.Use(db, d => { CheckStore.Retire(d, id, RecordStore.Actor(), because); return 0; });
        output.WriteLine($"retired brain.Check #{id}: {because}");
        return 0;
    }

    /// <summary>
    /// kor check-test [--module M] - every live check's known answers (exit 1 if any fails). With --tool T and --command C
    /// (or --path P) [--cwd D]: which check would refuse that call - nothing is logged.
    /// </summary>
    private static int CheckTest(Options o, TextWriter output, BrainDb? db)
    {
        var module = o.Get("--module");
        var all = BrainDb.Use(db, d => CheckStore.Live(d)).Select(s => s.Check).ToList();
        var checks = module is null ? all : CheckStore.For(all, module).ToList();
        if (o.Get("--tool") is { } tool)
        {
            var input = o.Get("--command") ?? o.Get("--path") ?? throw new ArgumentException("check-test --tool needs --command C or --path P");
            var hit = checks.Where(c => c.Tool == tool && Rules.KnownAnswerFailures(c).Count == 0)
                            .Select(c => (c, m: Rules.Matches(c, input, o.Get("--cwd") ?? Environment.CurrentDirectory))).FirstOrDefault(x => x.m is not null);
            output.WriteLine(hit.m is null ? $"allow: none of {checks.Count} live check(s) matches"
                                           : $"deny: brain.Check #{hit.c.Id} matches {SecretGuard.Mask(hit.m.Text)} ({hit.m.Origin})");
            return 0;
        }
        var failed = 0;
        foreach (var c in checks)
        {
            var fails = Rules.KnownAnswerFailures(c);
            if (fails.Count > 0) failed++;
            output.WriteLine(fails.Count == 0 ? $"PASS brain.Check #{c.Id} {c.Module} {c.Tool} ({c.MustRefuse.Count} refuse, {c.MustAllow.Count} allow, mentions allowed)"
                                              : $"FAIL brain.Check #{c.Id} {c.Module} {c.Tool}: {string.Join("; ", fails)}");
        }
        output.WriteLine($"{checks.Count - failed} of {checks.Count} live check(s) pass their known answers");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>kor checks [--module M] [--hits N] - the live checks reaching M (its own + Office), each with its refusal
    /// count from oversight.GuardRefusal; --hits N lists only those refused at least N times.</summary>
    private static int Checks(Options o, TextWriter output, BrainDb? db, BrainDb? board)
    {
        var module = o.Get("--module");
        var min = o.Int("--hits") ?? 0;
        var all = BrainDb.Use(db, d => CheckStore.Live(d));
        var rows = module is null ? all : all.Where(s => CheckStore.For(new[] { s.Check }, module).Any()).ToList();
        var hits = board is not null ? CheckStore.Hits(board) : BrainDb.Read(KorStandardsConnection.Login.Board, CheckStore.Hits);
        var shown = rows.Where(s => hits.GetValueOrDefault(s.Check.Id) >= min).ToList();
        output.WriteLine($"{shown.Count} of {rows.Count} live check(s){(module is null ? "" : $" reaching {module}")} with at least {min} hit(s)");
        foreach (var s in shown)
        {
            var c = s.Check;
            output.WriteLine($"brain.Check #{c.Id} {c.Module} {c.Tool} hits={hits.GetValueOrDefault(c.Id)} enforcing {c.MemoryKind} brain.Memory #{c.MemoryId} {c.MemoryName}");
            output.WriteLine($"   pattern: {c.Pattern}");
            output.WriteLine($"   says: {c.Says}");
        }
        return 0;
    }

    // ---- secret-scan (mistake 138) ---------------------------------------------------------------------------------------
    /// <summary>
    /// Read-only: SecretGuard as a classifier over every brain.Memory Body and Description, brain.LaneState ResumeBlock and
    /// brain.Event Detail/Ref (SNAPSHOT, nothing written). Prints ONLY counts, ids, the field and the rule names - never text.
    /// </summary>
    private static int SecretScan(Options o, Func<string?> stdin, TextWriter output, BrainDb? db)
    {
        if (o.Get("--text") is not null) return SecretScanText(o, stdin, output);
        // read only: the caller's unit of work, or a SNAPSHOT read that is never committed
        var explain = o.Get("--explain") is not null;
        var offsets = o.Get("--offsets") is not null;
        var lines = db is not null ? SecretScanLines(db, explain, offsets)
                                   : BrainDb.Read(KorStandardsConnection.Login.Brain, x => SecretScanLines(x, explain, offsets));
        foreach (var l in lines) output.WriteLine(l);
        return 0;
    }

    /// <summary>
    /// <c>kor secret-scan --text -</c>: the text on stdin (kor_brain.py's note/state guard - one rule set, SecretGuard's).
    /// Clean: prints "clean", exit 0. A value: prints SecretGuard.Refuse's one line (rule, length, where - never the value),
    /// exit 1. No stdin: usage, exit 2. Touches no database.
    /// </summary>
    private static int SecretScanText(Options o, Func<string?> stdin, TextWriter output)
    {
        if (o.Positional.ElementAtOrDefault(1) != "-") throw new ArgumentException("secret-scan --text reads stdin: kor secret-scan --text -");
        var text = stdin();
        if (text is null)
        {
            output.WriteLine("kor secret-scan --text -: no text on stdin");
            return 2;
        }
        try { SecretGuard.Refuse(text, o.Get("--what") ?? "text"); }
        catch (SecretRefusedException e)
        {
            output.WriteLine(e.Message);
            return 1;
        }
        output.WriteLine("clean");
        return 0;
    }

    /// <summary>A SQL N'' expression that rebuilds <paramref name="s"/> exactly: printable ASCII in literals (quotes doubled,
    /// '$' as NCHAR(36) so sqlcmd never reads a $(variable)), every other character as NCHAR(n).</summary>
    internal static string SqlExpression(string s)
    {
        if (s.Length == 0) return "N''";
        var parts = new List<string>();
        var lit = new StringBuilder();
        void Flush() { if (lit.Length > 0) { parts.Add("N'" + lit + "'"); lit.Clear(); } }
        foreach (var ch in s)
        {
            if (ch >= 0x20 && ch <= 0x7E && ch != '$') lit.Append(ch == '\'' ? "''" : ch.ToString());
            else { Flush(); parts.Add($"NCHAR({(int)ch})"); }
        }
        Flush();
        return string.Join(" + ", parts);
    }

    /// <param name="explain">per hit also: rule, credential word, separator, length, classes, entropy - never text.</param>
    /// <param name="offsets">per hit also: 0-based UTF-16 offset, length and the up to 40 characters BEFORE the value (its
    /// anchor, as JSON and as a SQL expression) - for a redaction migration's guard. Never the value or anything after its start.</param>
    internal static IReadOnlyList<string> SecretScanLines(BrainDb d, bool explain = false, bool offsets = false)
    {
        var lines = new List<string>();
        void Scan(string table, string sql, string[] fields)
        {
            using var c = d.Command(sql, 120);
            using var r = c.ExecuteReader();
            int rows = 0, flagged = 0, liveFlagged = 0;
            var ids = new List<string>();
            while (r.Read())
            {
                rows++;
                var id = r.GetInt32(0);
                var live = r.GetInt32(1) == 1;
                var found = new List<string>();
                for (var i = 0; i < fields.Length; i++)
                {
                    if (r.IsDBNull(2 + i)) continue;
                    var hits = SecretGuard.Find(r.GetString(2 + i));
                    if (hits.Count > 0) found.Add($"{fields[i]}:{string.Join("+", hits.Select(h => h.Rule).Distinct())}x{hits.Count}");
                    if (hits.Count > 0 && explain) found.AddRange(SecretGuard.Explain(r.GetString(2 + i)).Select(x => "\n      " + x));
                    if (hits.Count > 0 && offsets)
                        found.AddRange(SecretGuard.Anchors(r.GetString(2 + i)).Select(a =>
                            $"\n      {table}.{fields[i]} #{id} rule={a.Hit.Rule} off0={a.Hit.Index} len={a.Hit.Length} " +
                            $"anchorStart0={a.AnchorStart} anchorLen={a.Anchor.Length} " +
                            $"anchor={System.Text.Json.JsonSerializer.Serialize(a.Anchor)}" +
                            $"\n         anchorSql={SqlExpression(a.Anchor)}"));
                }
                if (found.Count == 0) continue;
                flagged++;
                if (live) liveFlagged++;
                ids.Add($"#{id}{(live ? "" : " (not live)")} {string.Join(" ", found)}");
            }
            lines.Add($"{table}: {rows} row(s) scanned, {flagged} flagged ({liveFlagged} live)");
            lines.AddRange(ids.Select(x => "   " + x));
        }
        Scan("brain.Memory", "SELECT Id, CASE WHEN RetiredAtUtc IS NULL AND Status <> N'superseded' THEN 1 ELSE 0 END, Body, Description FROM brain.Memory ORDER BY Id",
             new[] { "Body", "Description" });
        Scan("brain.LaneState", "SELECT Id, 1, ResumeBlock FROM brain.LaneState ORDER BY Id", new[] { "ResumeBlock" });
        Scan("brain.Event", "SELECT Id, 1, Detail, Ref FROM brain.Event ORDER BY Id", new[] { "Detail", "Ref" });
        return lines;
    }
}
