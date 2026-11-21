using System.Text.Json;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Stall;
using Kor.Operations.Framework.Whole;

namespace Kor.Operations.Framework.Cli;

/// <summary>
/// Board row 2139, <see cref="WholeGate"/> (the rules, the ledger and what it does not cover are its header). It runs INSIDE
/// <c>kor context --hook</c> (PreToolUse every tool, and Stop), which every enrolled KOR repo already wires - as ReadGuard does - so
/// no settings line is added (plan review 66e8ff5d). A call that is not a door costs one regex pass; a door reads the transcript tail.
/// Every refusal is a row in oversight.GuardRefusal (guard <see cref="WholeGate.GuardName"/>). Fails OPEN: any fault allows, logged.
/// <c>kor whole --hook</c>: the same decision alone (hook JSON on stdin). <c>kor whole --replay &lt;transcript.jsonl | list.txt&gt;
/// [--until LINE] [--quiet]</c>: every door moment of a real transcript through the same ledger - what the hook would have decided.
/// </summary>
public static partial class Program
{
    private static int WholeVerb(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainDb? board, string hookErrorLog)
    {
        if (o.Get("--replay") is { } replay)
            return WholeReplay(replay, o.Get("--until") is { } u ? int.Parse(u, System.Globalization.CultureInfo.InvariantCulture) : int.MaxValue,
                               o.Get("--quiet") is not null || o.Positional.Contains("--quiet"), output);
        var raw = stdin();
        if (string.IsNullOrWhiteSpace(raw)) { if (!hook) output.WriteLine("allow: no hook input"); return 0; }
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var ev = Str(root, "hook_event_name");
            var tool = Str(root, "tool_name");
            var input = root.TryGetProperty("tool_input", out var ti) ? ti : default;
            var path = StallTranscript.PathFor(Str(root, "transcript_path"), Str(root, "session_id"), Str(root, "agent_id"));
            var stopActive = root.TryGetProperty("stop_hook_active", out var sa) && sa.ValueKind == JsonValueKind.True;
            var v = WholeDecide(ev, tool, input, path, Str(root, "cwd"), stopActive);
            if (v is null || !v.Value.Verdict.Refuse) { if (!hook) output.WriteLine("allow"); return 0; }
            var logged = WholeLog(board, v.Value, Str(root, "session_id"), Str(root, "cwd"), hookErrorLog, o);
            output.WriteLine(hook ? WholeJson(ev, v.Value.Verdict.Reason!) : $"deny{(logged is null ? " (not logged)" : $" (oversight.GuardRefusal #{logged})")}: {v.Value.Verdict.Reason}");
        }
        catch (Exception e) when (e is not OutOfMemoryException) { LogHookError(hookErrorLog, o, e); if (!hook) output.WriteLine($"allow (fault, logged): {e.Message}"); }
        return 0;
    }

    /// <summary>The verdict for one hook moment, or null when it is not a door (or the gate is off, or the Stop is a retry).</summary>
    internal static (WholeLedger.Verdict Verdict, WholeGate.Door Door)? WholeDecide(string? ev, string? tool, JsonElement input, string? transcript, string? cwd, bool stopActive)
    {
        if (WholeGate.Off || transcript is null || !File.Exists(transcript)) return null;
        WholeGate.Door door;
        if (ev == "PreToolUse" && tool is not null)
        {
            var text = WholeTranscript.InputOf(tool, input);
            var file = input.ValueKind == JsonValueKind.Object && input.TryGetProperty("file_path", out var fp) && fp.ValueKind == JsonValueKind.String ? fp.GetString() : null;
            door = WholeGate.DoorOf(tool, text, file, p => ReadSqlFile(p, cwd));
            if (door.Kind == WholeGate.DoorKind.None) return null;
        }
        else if (ev == "Stop" && !stopActive) door = new WholeGate.Door(WholeGate.DoorKind.Reply, "", true, false, false);
        else return null;
        var ledger = new WholeLedger();
        foreach (var e in WholeTranscript.ReadTail(transcript)) ledger.Add(e);
        // a PreToolUse door's text carries the narration written just before it; a Stop's is the turn's assistant text
        door = door.Kind == WholeGate.DoorKind.Reply ? door with { Text = ledger.TurnText } : door with { Text = ledger.Narration + "\n" + door.Text };
        return (ledger.Decide(door), door);
    }

    /// <summary>kor context's call: the refusal text (logged), or null to go on. Any fault allows, logged.</summary>
    private static string? WholeRefusal(Options o, string ev, string? tool, JsonElement input, string path, string? cwd, bool stopActive,
                                        string? session, BrainDb? board, string hookErrorLog)
    {
        try
        {
            if (WholeDecide(ev, tool, input, path, cwd, stopActive) is not { } v || !v.Verdict.Refuse) return null;
            WholeLog(board, v, session, cwd, hookErrorLog, o);
            return v.Verdict.Reason;
        }
        catch (Exception e) when (e is not OutOfMemoryException) { LogHookError(hookErrorLog, o, e); return null; }
    }

    private static string? ReadSqlFile(string p, string? cwd)
    {
        try
        {
            var full = Path.IsPathRooted(p) || cwd is null ? p : Path.Combine(cwd, p);
            return File.Exists(full) && new FileInfo(full).Length < 2_000_000 ? File.ReadAllText(full) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    internal static string WholeJson(string? ev, string reason) => ev == "Stop"
        ? JsonSerializer.Serialize(new { decision = "block", reason })
        : JsonSerializer.Serialize(new { hookSpecificOutput = new { hookEventName = "PreToolUse", permissionDecision = "deny", permissionDecisionReason = reason } });

    internal static int? WholeLog(BrainDb? board, (WholeLedger.Verdict Verdict, WholeGate.Door Door) v, string? session, string? cwd, string hookErrorLog, Options o)
    {
        try
        {
            var repo = cwd is not null && Directory.Exists(cwd) && Git.TopLevelFast(cwd) is { } top ? Path.GetFileName(top.TrimEnd('/', '\\')) : null;
            var rule = $"row 2139 {v.Verdict.Rules} at {v.Door.Kind}";
            var matched = BrainStore.Clip(string.Join(" | ", v.Verdict.Named.Select(f => $"L{f.Line} {f.R.Key}")) is { Length: > 0 } n ? n : WholeGate.Clip(WholeGate.Squash(v.Door.Text), 400), 400) ?? "";
            return board is not null
                ? StallStore.LogGuardRefusal(board, WholeGate.GuardName, rule, session, repo, matched)
                : BrainDb.Use3s(KorStandardsConnection.Login.Board, b => StallStore.LogGuardRefusal(b, WholeGate.GuardName, rule, session, repo, matched));
        }
        catch (Exception e) when (e is not OutOfMemoryException) { LogHookError(hookErrorLog, o, e); return null; }
    }

    /// <summary>The dry run: each transcript's moments in order; before each PreToolUse door its verdict, and at each turn's end (the next
    /// human or peer prompt, or the end of the file) the Stop verdict on that turn's text. A Stop that was refused is judged once (the
    /// retry is allowed). Prints each refusal (unless --quiet) and one summary line per transcript and in total. Reads only.</summary>
    private static int WholeReplay(string what, int until, bool quiet, TextWriter output)
    {
        var files = what.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? File.ReadAllLines(what).Select(l => l.Trim()).Where(l => l.Length > 0).ToList()
            : new List<string> { what };
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        int doors = 0, refused = 0, flags = 0;
        foreach (var f in files)
        {
            List<WholeEv> evs;
            try { evs = WholeTranscript.ReadAll(f); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { output.WriteLine($"UNREADABLE {f}: {e.Message}"); continue; }
            var ledger = new WholeLedger();
            int fd = 0, fr = 0;
            var turnHasText = false;
            void Judge(WholeGate.Door door, int line, string tool)
            {
                fd++;
                var v = ledger.Decide(door);
                if (!v.Refuse) return;
                fr++;
                ledger.Comply(v.Named);
                foreach (var r in v.Rules.Split('+')) totals[r] = totals.GetValueOrDefault(r) + 1;
                if (!quiet)
                    output.WriteLine($"REFUSE {Path.GetFileName(f)} L{line} {door.Kind} ({tool}) {v.Rules}: " +
                                     string.Join(" | ", v.Named.Take(4).Select(x => $"L{x.Line} `{WholeGate.Clip(x.R.Key, 90)}`")) +
                                     (v.Rules.Contains("W4") ? " | " + WholeGate.Clip(WholeGate.Squash(v.Reason!.Split('\n').First(l => l.StartsWith("W4", StringComparison.Ordinal))), 200) : ""));
            }
            foreach (var e in evs)
            {
                if (e.Line > until) break;
                if (e.Kind == WholeKind.Prompt && turnHasText)
                {
                    Judge(new WholeGate.Door(WholeGate.DoorKind.Reply, ledger.TurnText, true, false, false), e.Line - 1, "Stop");
                    turnHasText = false;
                }
                if (e.Kind == WholeKind.Text) turnHasText = true;
                if (e.Kind == WholeKind.Call)
                {
                    var door = WholeGate.DoorOf(e.Tool, e.Input, e.FilePath, p => ReadSqlFile(p, e.Cwd));
                    if (door.Kind != WholeGate.DoorKind.None) Judge(door with { Text = ledger.Narration + "\n" + door.Text }, e.Line, e.Tool);
                }
                ledger.Add(e);
            }
            if (turnHasText) Judge(new WholeGate.Door(WholeGate.DoorKind.Reply, ledger.TurnText, true, false, false), Math.Min(until, evs.Count == 0 ? 0 : evs[^1].Line), "Stop");
            doors += fd; refused += fr; flags += ledger.Flags.Count;
            output.WriteLine($"REPLAY {Path.GetFileName(f)}: {ledger.Flags.Count} flags/holes in the ledger, {fd} door moments, {fr} refused.");
        }
        output.WriteLine($"TOTAL {files.Count} transcripts: {flags} flags/holes, {doors} door moments, {refused} refused; by rule: " +
                         string.Join(", ", totals.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key} {k.Value}")));
        return 0;
    }
}
