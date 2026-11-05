using System.Globalization;
using System.Text;
using System.Text.Json;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Context;
using Kor.Operations.Framework.Stall;

namespace Kor.Operations.Framework.Cli;

/// <summary>G5, the context cliff (board row 1474): <c>kor context --hook</c> on PreToolUse (every tool), UserPromptSubmit
/// and Stop, and <c>kor context --replay</c>, its dry run. The rules, the measured cliff and what they do not cover are
/// <see cref="ContextGuard"/>'s header.</summary>
public static partial class Program
{
    public const string ContextGuardName = "kor-context";

    /// <summary>
    /// Reads the hook JSON, then the session's own transcript tail (the subagent's file when agent_id is given) and the
    /// auto-compact settings the session sees (from its cwd), and decides by hook_event_name:
    /// PreToolUse - deny at STOP unless the call is a handover step (one oversight.GuardRefusal row, Guard kor-context),
    /// additionalContext once per step in WARN; UserPromptSubmit - additionalContext in WARN (once per step) and at STOP
    /// (always); Stop - block once at STOP until the handover is written (main sessions only). Bad input or another event:
    /// nothing printed; no transcript: nothing either, except the spend line on a first prompt. A prompt and, at most once per
    /// ten minutes, a tool call also carry the spend line (G6, row 1559), read with the newest reading from the DB. Exit 0 always
    /// in hook mode; the DB is otherwise touched only to log a refusal.
    /// </summary>
    private static int ContextVerb(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainDb? board, string hookErrorLog)
    {
        if (o.Get("--replay") is { } replay)
            return ContextReplay(replay, o.Get("--since"), o.Get("--auto-compact-window"), o.Get("--auto-compact-model"), output);
        var raw = stdin();
        if (string.IsNullOrWhiteSpace(raw)) { if (!hook) output.WriteLine("allow: no hook input"); return 0; }
        string? ev, tool, session, transcript, agent, cwd, scratch;
        string text = "";
        bool stopActive;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { if (!hook) output.WriteLine("allow: the input is not a JSON object"); return 0; }
            ev = Str(root, "hook_event_name");
            tool = Str(root, "tool_name");
            session = Str(root, "session_id");
            transcript = Str(root, "transcript_path");
            agent = Str(root, "agent_id");
            cwd = Str(root, "cwd");
            scratch = Str(root, "scratchpad_dir");
            stopActive = root.TryGetProperty("stop_hook_active", out var sa) && sa.ValueKind == JsonValueKind.True;
            if (tool is not null && root.TryGetProperty("tool_input", out var ti)) text = ContextGuard.TextOf(tool, ti);
        }
        catch (JsonException) { if (!hook) output.WriteLine("allow: the input is not JSON"); return 0; }
        var path = StallTranscript.PathFor(transcript, session, agent);
        if (path is null || !File.Exists(path))
        {
            // a session's first prompt comes before its transcript: no context to read yet, but the spend line is still owed
            if (!hook) output.WriteLine("allow: no transcript to read");
            else if (ev == "UserPromptSubmit" && SpendLineForHook(o, session, prompt: true, hookErrorLog) is { } first)
                output.WriteLine(JsonSerializer.Serialize(new { hookSpecificOutput = new { hookEventName = "UserPromptSubmit", additionalContext = first } }));
            return 0;
        }
        var sub = !string.IsNullOrWhiteSpace(agent);
        var cs = CompactSettings.Read(cwd is not null && Directory.Exists(cwd) ? cwd : null);
        if (o.Get("--ceiling") is { } capText)
            cs = cs with { Cap = long.TryParse(capText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cap) && cap > 0 ? cap : throw new ArgumentException($"--ceiling takes a whole number of tokens, not '{capText}'") };
        var r = ContextTranscript.ReadFile(path, cs);
        var module = o.Get("--module");
        var ceiling = ContextGuard.CeilingOf(r.Model, cs);
        var head = $"kor context: {ContextGuard.N(r.Tokens)} of {ContextGuard.N(ceiling)} ({r.Model ?? "no model"}; {cs.Describe(r.Model)}; read {r.Basis}{(r.OverCliff ? ", PAST THE CLIFF" : "")})";

        switch (ev)
        {
            case "PreToolUse" when tool is not null:
            {
                var d = ContextGuard.Decide(r, tool, text, scratch, sub, module, cs);
                if (d.Deny)
                {
                    int? logged = null;
                    try
                    {
                        var repo = cwd is not null && Directory.Exists(cwd) && Git.TopLevelFast(cwd) is { } top ? Path.GetFileName(top.TrimEnd('/', '\\')) : null;
                        var rule = $"G5 STOP {ContextGuard.N(r.Tokens)}/{ContextGuard.N(ceiling)}{(sub ? " subagent" : "")}";
                        var matched = $"{tool}: {text}";
                        logged = board is not null
                            ? StallStore.LogGuardRefusal(board, ContextGuardName, rule, session, repo, matched)
                            : BrainDb.Use3s(KorStandardsConnection.Login.Board, b => StallStore.LogGuardRefusal(b, ContextGuardName, rule, session, repo, matched));
                    }
                    catch (Exception e) { LogHookError(hookErrorLog, o, e); }   // the refusal stands whether or not it was logged
                    output.WriteLine(hook
                        ? JsonSerializer.Serialize(new { hookSpecificOutput = new { hookEventName = "PreToolUse", permissionDecision = "deny", permissionDecisionReason = d.Say } })
                        : $"{head}\ndeny{(logged is null ? " (not logged)" : $" (oversight.GuardRefusal #{logged})")}: {d.Say}");
                    return 0;
                }
                var callSay = JoinSay(d.Say, hook ? SpendLineForHook(o, session, prompt: false, hookErrorLog) : null);
                if (callSay is not null)
                    output.WriteLine(hook
                        ? JsonSerializer.Serialize(new { hookSpecificOutput = new { hookEventName = "PreToolUse", additionalContext = callSay } })
                        : $"{head}\nallow + warn: {callSay}");
                else if (!hook) output.WriteLine($"{head}\nallow: {d.Why}");
                return 0;
            }
            case "UserPromptSubmit":
            {
                var level = ContextGuard.LevelOf(r, cs);
                string? say = level switch
                {
                    ContextLevel.Stop => ContextGuard.StopText(r, sub, module, r.HandoverWritten, cs),
                    ContextLevel.Warn when ContextGuard.ShouldWarn(r.Tokens, ceiling, r.LastWarned) => ContextGuard.WarnText(r, cs),
                    _ => null,
                };
                say = JoinSay(say, hook ? SpendLineForHook(o, session, prompt: true, hookErrorLog) : null);
                if (say is not null)
                    output.WriteLine(hook ? JsonSerializer.Serialize(new { hookSpecificOutput = new { hookEventName = "UserPromptSubmit", additionalContext = say } }) : $"{head}\nsay: {say}");
                else if (!hook) output.WriteLine($"{head}\nsilent ({level})");
                return 0;
            }
            case "Stop":
            {
                var d = ContextGuard.DecideStop(r, sub, stopActive, module, cs);
                if (d.Deny) output.WriteLine(hook ? JsonSerializer.Serialize(new { decision = "block", reason = d.Say }) : $"{head}\nblock: {d.Say}");
                else if (!hook) output.WriteLine($"{head}\nlet it stop: {d.Why}");
                return 0;
            }
            default:
                if (!hook) output.WriteLine($"{head}\nnothing to do for event {ev ?? "-"}");
                return 0;
        }
    }

    /// <summary>
    /// Row 1474's dry run: <c>kor context --replay &lt;transcript.jsonl | list.txt&gt; [--since ISO-UTC]
    /// [--auto-compact-window N [--auto-compact-model M (claude-opus-5-5)]]</c>. Every tool call of each transcript, in order,
    /// through <see cref="ContextGuard.Decide"/> on the reading <see cref="ContextTranscript.Read(IReadOnlyList{string}, int, bool, long?, CompactSettings, bool)"/>
    /// takes at that line - what the hook would have decided. Without --auto-compact-window the ceiling is the model's window
    /// (auto-compact off, as it was for the corpus's cliffs); with it, that model's ceiling is the given auto-compact window.
    /// After STOP the transcript goes on as it really did, so every later work call is counted as one the hook would have
    /// refused. One JSON line per segment (a stretch between compactions) that reached WARN, then one summary line. Reads only.
    /// </summary>
    private static int ContextReplay(string what, string? sinceText, string? acWindow, string? acModel, TextWriter output)
    {
        var files = what.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? File.ReadAllLines(what).Select(l => l.Trim()).Where(l => l.Length > 0).ToList()
            : new List<string> { what };
        DateTimeOffset? since = sinceText is null ? null : DateTimeOffset.Parse(sinceText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var cs = CompactSettings.Off;
        if (acWindow is not null)
        {
            var w = long.Parse(acWindow, CultureInfo.InvariantCulture);
            var m = CompactSettings.Canonical(acModel ?? "claude-opus-5-5")!;
            cs = new CompactSettings(true, null, new[] { new CompactSettings.Layer("replay", true, null, false, new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase) { [m] = w }) });
        }
        int unreadable = 0, judged = 0, denied = 0, allowedAtStop = 0, warnSays = 0;
        var sessions = new Dictionary<string, (bool Sub, bool Warned, bool Stopped)>();
        int segments = 0, segWarned = 0, segStopped = 0, segTrigger = 0, segTriggerStopFirst = 0;
        var endKinds = new Dictionary<string, int>();
        foreach (var f in files)
        {
            var sub = ContextTranscript.IsSubagentPath(f);
            List<string> lines;
            try
            {
                using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var rd = new StreamReader(fs, new UTF8Encoding(false));
                lines = new List<string>();
                for (string? l; (l = rd.ReadLine()) is not null;) lines.Add(l);
            }
            catch (Exception e) { unreadable++; output.WriteLine(JsonSerializer.Serialize(new { transcript = f, error = e.Message })); continue; }
            var seg = new Seg();
            void Close(string kind, int line, DateTimeOffset? at, long? pre)
            {
                // a segment that ended before the window had none of its calls judged: it is not counted
                if (since is not null && at is { } endAt && endAt < since) { seg = new Seg(); return; }
                segments++;
                var trigger = ContextGuard.CeilingOf(seg.Model, cs) - ContextGuard.TriggerMargin;
                // the cliff kinds are "cliff, then compact-x" and "cliff (no compaction after)": a cliff below the trigger still reached the cliff (review row 514)
                var reached = seg.Peak >= trigger || kind.StartsWith("cliff", StringComparison.Ordinal) || kind.StartsWith("compact-", StringComparison.Ordinal) && (pre ?? 0) >= trigger;
                if (reached) { segTrigger++; if (seg.StopLine is not null) segTriggerStopFirst++; }
                endKinds[kind + (seg.StopLine is not null ? " (stopped)" : seg.WarnLine is not null ? " (warned)" : "")] =
                    endKinds.GetValueOrDefault(kind + (seg.StopLine is not null ? " (stopped)" : seg.WarnLine is not null ? " (warned)" : "")) + 1;
                if (seg.WarnLine is not null || reached)
                {
                    if (seg.WarnLine is not null) segWarned++;
                    if (seg.StopLine is not null) segStopped++;
                    var wasStopped = sessions.TryGetValue(f, out var s) && s.Stopped;
                    var wasWarned = sessions.TryGetValue(f, out var prev) && prev.Warned;
                    sessions[f] = (sub, wasWarned || seg.WarnLine is not null, wasStopped || seg.StopLine is not null);
                    output.WriteLine(JsonSerializer.Serialize(new
                    {
                        transcript = f, sub, model = seg.Model, ceiling = ContextGuard.CeilingOf(seg.Model, cs),
                        warn = new { line = seg.WarnLine, at = seg.WarnAt, tokens = seg.WarnTokens },
                        stop = seg.StopLine is null ? null : new { line = seg.StopLine, at = seg.StopAt, tokens = seg.StopTokens, first_refused = seg.FirstDenied },
                        end = new { kind, line, at, pre, peak = seg.Peak, reached_trigger = reached },
                        minutes_warn_to_end = at is { } e1 && seg.WarnAt is { } w1 ? Math.Round((e1 - w1).TotalMinutes, 1) : (double?)null,
                        minutes_stop_to_end = at is { } e2 && seg.StopAt is { } s2 ? Math.Round((e2 - s2).TotalMinutes, 1) : (double?)null,
                        refused = seg.Denied, allowed_at_stop = seg.AllowedAtStop,
                    }, ReplayJson));
                }
                else if (!sessions.ContainsKey(f)) sessions[f] = (sub, false, false);
                seg = new Seg();
            }
            DateTimeOffset? lastAt = null;
            for (var i = 0; i < lines.Count; i++)
            {
                var raw = lines[i];
                var isA = raw.Contains("\"type\":\"assistant\"", StringComparison.Ordinal);
                var isC = raw.Contains("\"compact_boundary\"", StringComparison.Ordinal);
                if (!isA && !isC) continue;
                JsonDocument doc;
                try { doc = JsonDocument.Parse(raw); } catch (JsonException) { continue; }
                using (doc)
                {
                    var d = doc.RootElement;
                    if (d.ValueKind != JsonValueKind.Object) continue;
                    if (!sub && d.TryGetProperty("isSidechain", out var sc) && sc.ValueKind == JsonValueKind.True) continue;
                    DateTimeOffset? at = DateTimeOffset.TryParse(Str(d, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var ts) ? ts : null;
                    if (at is not null) lastAt = at;
                    if (Str(d, "type") == "system" && Str(d, "subtype") == "compact_boundary")
                    {
                        string trig = "?"; long? pre = null;
                        if (d.TryGetProperty("compactMetadata", out var cm) && cm.ValueKind == JsonValueKind.Object)
                        {
                            trig = Str(cm, "trigger") ?? "?";
                            if (cm.TryGetProperty("preTokens", out var pt) && pt.ValueKind == JsonValueKind.Number) pre = pt.GetInt64();
                        }
                        Close(seg.Cliff ? $"cliff, then compact-{trig}" : $"compact-{trig}", i + 1, at, pre);
                        continue;
                    }
                    if (Str(d, "type") != "assistant" || !d.TryGetProperty("message", out var m) || m.ValueKind != JsonValueKind.Object) continue;
                    if (Str(m, "model") == "<synthetic>")
                    {
                        if (m.TryGetProperty("content", out var cc) && cc.ValueKind == JsonValueKind.Array && cc.EnumerateArray().Any(b =>
                                b.ValueKind == JsonValueKind.Object && (Str(b, "text") ?? "").StartsWith("Prompt is too long", StringComparison.OrdinalIgnoreCase)))
                            seg.Cliff = true;
                        continue;
                    }
                    var usage = ContextTranscript.Usage(m);
                    if (usage > seg.Peak) { seg.Peak = usage; seg.Model = Str(m, "model") ?? seg.Model; }
                    if (since is not null && (at is null || at < since)) continue;
                    foreach (var (tool, text, _) in ContextTranscript.ToolUses(m))
                    {
                        judged++;
                        var r = ContextTranscript.Read(lines, i + 1, sub, seg.LastWarned, cs, handover: false);
                        var dec = ContextGuard.Decide(r, tool, text, null, sub, "M", cs);
                        if (dec.Level == ContextLevel.Warn && dec.Say is not null)
                        {
                            warnSays++;
                            seg.LastWarned = r.Tokens;
                            if (seg.WarnLine is null) { seg.WarnLine = i + 1; seg.WarnAt = at; seg.WarnTokens = r.Tokens; }
                        }
                        if (dec.Level == ContextLevel.Stop)
                        {
                            if (seg.WarnLine is null) { seg.WarnLine = i + 1; seg.WarnAt = at; seg.WarnTokens = r.Tokens; }   // a jump straight past WARN
                            if (seg.StopLine is null) { seg.StopLine = i + 1; seg.StopAt = at; seg.StopTokens = r.Tokens; }
                            if (dec.Deny) { denied++; seg.Denied++; seg.FirstDenied ??= $"{tool}: {Head(SecretGuard.Mask(text) ?? "", 200)}"; }
                            else
                            {
                                allowedAtStop++; seg.AllowedAtStop++;
                                output.WriteLine(JsonSerializer.Serialize(new { verdict = "allowed-at-stop", transcript = f, line = i + 1, at, tool, why = dec.Why, text = Head(SecretGuard.Mask(text) ?? "", 200) }, ReplayJson));
                            }
                        }
                    }
                }
            }
            Close(seg.Cliff ? "cliff (no compaction after)" : "transcript end", lines.Count, lastAt, null);
        }
        int Count(bool s, Func<(bool Sub, bool Warned, bool Stopped), bool> p) => sessions.Values.Count(v => v.Sub == s && p(v));
        output.WriteLine($"replay: {files.Count} transcript(s), {unreadable} unreadable; ceiling {(acWindow is null ? "the model's window (auto-compact off)" : $"{acModel ?? "claude-opus-5-5"} auto-compact window {acWindow}, other models their window")}; " +
                         $"{judged} judged tool call(s){(since is null ? "" : $" since {since:O}")}; " +
                         $"main sessions warned {Count(false, v => v.Warned)} of {Count(false, _ => true)}, stopped {Count(false, v => v.Stopped)} of {Count(false, _ => true)}; " +
                         $"subagent sessions warned {Count(true, v => v.Warned)} of {Count(true, _ => true)}, stopped {Count(true, v => v.Stopped)} of {Count(true, _ => true)}; " +
                         $"segments {segments}, warned {segWarned}, stopped {segStopped}; reached the trigger (ceiling - {ContextGuard.N(ContextGuard.TriggerMargin)}) or the cliff: {segTrigger}, STOP came first in {segTriggerStopFirst} of {segTrigger}; " +
                         $"calls refused {denied}, allowed at STOP as handover steps {allowedAtStop}, warnings said {warnSays}; " +
                         $"segment ends: {string.Join(", ", endKinds.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))}");
        return 0;
    }

    private sealed class Seg
    {
        public long Peak; public string? Model; public bool Cliff; public long? LastWarned;
        public int? WarnLine; public DateTimeOffset? WarnAt; public long WarnTokens;
        public int? StopLine; public DateTimeOffset? StopAt; public long StopTokens; public string? FirstDenied;
        public int Denied; public int AllowedAtStop;
    }
}
