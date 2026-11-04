using System.Globalization;
using System.Text.Json;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Stall;

namespace Kor.Operations.Framework.Cli;

/// <summary>G1, the stall guard (board row 1308): <c>kor stall --hook</c> on PreToolUse and <c>kor stall --replay</c>, its
/// dry run. The rules, and what they do not cover, are <see cref="StallGuard"/>'s header.</summary>
public static partial class Program
{
    /// <summary>
    /// PreToolUse (Bash|PowerShell|WebFetch|mcp__.*). Reads the hook JSON, then the session's own transcript tail (the
    /// subagent's when agent_id is given), and decides. Refused: the deny JSON naming both failures, and one
    /// oversight.GuardRefusal row (Guard kor-stall). The DB is read only when a refusal is pending (is there a diagnosis?);
    /// when it cannot be read the call is ALLOWED and the hook error log says so. Bad input, no transcript, another tool:
    /// allow, nothing printed. Exit 0 always in hook mode.
    /// </summary>
    private static int StallVerb(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainDb? db, BrainDb? board, string hookErrorLog)
    {
        if (o.Get("--replay") is { } replay) return StallReplay(replay, o.Get("--since"), o.Get("--window-hours"), o.Get("--never-records") is not null, output);
        var raw = stdin();
        if (string.IsNullOrWhiteSpace(raw)) { if (!hook) output.WriteLine("allow: no hook input"); return 0; }
        string? tool, session, transcript, agent, cwd, useId;
        string text;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { if (!hook) output.WriteLine("allow: the input is not a JSON object"); return 0; }
            tool = Str(root, "tool_name");
            session = Str(root, "session_id");
            transcript = Str(root, "transcript_path");
            agent = Str(root, "agent_id");
            cwd = Str(root, "cwd");
            useId = Str(root, "tool_use_id");
            text = tool is not null && root.TryGetProperty("tool_input", out var ti) ? StallTranscript.TextOf(tool, ti) : "";
        }
        catch (JsonException) { if (!hook) output.WriteLine("allow: the input is not JSON"); return 0; }
        if (tool is null || !StallGuard.Judged(tool) || text.Length == 0)
        {
            if (!hook) output.WriteLine($"allow: tool {tool ?? "-"} is not judged by G1, or has no input");
            return 0;
        }
        var path = StallTranscript.PathFor(transcript, session, agent);
        if (path is null || !File.Exists(path)) { if (!hook) output.WriteLine("allow: no transcript to read"); return 0; }
        // the call being decided may already be on disk as a tool_use with no result: it is not history
        var history = StallTranscript.ReadTail(path).Where(c => c.Id != useId && c.Answered).ToList();
        var now = DateTimeOffset.UtcNow;
        var next = new StallCall(0, now, useId ?? "", tool, text, null, false, null);
        void Log(Exception e) => LogHookError(hookErrorLog, o, e);
        var dbDown = false;
        string? Diagnosed(StallTarget t, DateTimeOffset after)
        {
            try
            {
                return db is not null ? StallStore.Diagnosis(db, t.Name, after)
                                      : BrainDb.Read3s(d => StallStore.Diagnosis(d, t.Name, after));
            }
            catch (Exception e)
            {
                // no database: the diagnosis cannot be read, so the call is allowed (fails open), said in the log
                Log(new InvalidOperationException($"kor stall: the diagnosis for '{t.Name}' could not be read ({e.GetType().Name}: {e.Message}) - allowed (fails open)"));
                dbDown = true;
                return "no database (fails open)";
            }
        }
        var d = StallGuard.Decide(next, history, Diagnosed, now);
        if (!d.Refuse)
        {
            if (!hook) output.WriteLine($"allow: {d.Reason}{(dbDown ? " [db unreadable]" : "")}");
            return 0;
        }
        int? logged = null;
        try
        {
            var dir = cwd is not null && Directory.Exists(cwd) ? cwd : null;
            var repo = dir is null ? null : Git.TopLevelFast(dir) is { } top ? Path.GetFileName(top.TrimEnd('/', '\\')) : null;
            logged = board is not null
                ? StallStore.LogRefusal(board, d, session, repo, text)
                : BrainDb.Use3s(KorStandardsConnection.Login.Board, b => StallStore.LogRefusal(b, d, session, repo, text));
        }
        catch (Exception e) { Log(e); }   // the refusal stands whether or not it was logged
        output.WriteLine(hook
            ? JsonSerializer.Serialize(new { hookSpecificOutput = new { hookEventName = "PreToolUse", permissionDecision = "deny", permissionDecisionReason = d.Reason } })
            : $"deny{(logged is null ? " (not logged)" : $" (oversight.GuardRefusal #{logged})")}: {d.Reason}");
        return 0;
    }


    private const string RecordedOnRefusal = "the record the refusal asked for (the replay assumes it was made)";

    /// <summary>
    /// Row 1308's dry run: <c>kor stall --replay &lt;transcript.jsonl | list.txt&gt; [--since ISO-UTC] [--window-hours H]
    /// [--never-records]</c>. Each transcript's calls in order through <see cref="StallGuard.Decide"/>, as the hook would
    /// have decided them; a diagnosis is read off the transcript (<see cref="StallGuard.DiagnosedInTranscript"/>).
    /// What happens after a refusal is not in the transcript, so it is modelled, two ways:
    /// <list type="bullet">
    /// <item>default (the false-positive count): the session does what the deny says - records a diagnosis naming the
    /// target - and the call then runs. Each stall is refused ONCE (pass 5's judges: 70 of 96 refusals were "repeats"
    /// after the target had answered again, which the session would have seen).</item>
    /// <item>--never-records (the seeds): the session never records, so every later attempt on the stalled target is
    /// refused and never runs.</item>
    /// </list>
    /// One JSON line per refusal and per call a transcript diagnosis cleared, then one summary line. Reads only.
    /// </summary>
    private static int StallReplay(string what, string? sinceText, string? windowText, bool neverRecords, TextWriter output)
    {
        var files = what.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? File.ReadAllLines(what).Select(l => l.Trim()).Where(l => l.Length > 0).ToList()
            : new List<string> { what };
        DateTimeOffset? since = sinceText is null ? null : DateTimeOffset.Parse(sinceText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        TimeSpan? window = windowText is null ? null : TimeSpan.FromHours(double.Parse(windowText, CultureInfo.InvariantCulture));
        int unreadable = 0, judged = 0, refused = 0, cleared = 0, gapUnder1 = 0, gapUnder3 = 0, gaps = 0;
        var failures = new Dictionary<string, int>();
        var byClass = new Dictionary<string, int>();
        foreach (var f in files)
        {
            List<StallCall> calls;
            if (!File.Exists(f)) { unreadable++; output.WriteLine(JsonSerializer.Serialize(new { transcript = f, error = "not found" })); continue; }
            try { calls = StallTranscript.ReadAll(f); }
            catch (Exception e) { unreadable++; output.WriteLine(JsonSerializer.Serialize(new { transcript = f, error = e.Message })); continue; }
            var ran = new List<StallCall>();
            var recorded = new HashSet<(string, DateTimeOffset)>();      // stalls seen (target, second failure): a later one is a repeat
            var recordedAt = new Dictionary<string, DateTimeOffset>();   // the record a deny asked for, assumed made at the refusal
            StallCall? prev = null;
            foreach (var c in calls)
            {
                var inWindow = since is null || (c.At is { } at && at >= since);
                if (inWindow && StallGuard.Judged(c.Tool) && !string.IsNullOrEmpty(c.Text) && c.At is { } now)
                {
                    judged++;
                    // how long the previous call's result had been on disk when this call was made (the lag question)
                    if (prev?.ResultAt is { } pr) { gaps++; var g = now - pr; if (g < TimeSpan.FromSeconds(1)) gapUnder1++; if (g < TimeSpan.FromSeconds(3)) gapUnder3++; }
                    var snapshot = ran;
                    var d = StallGuard.Decide(c, ran, (t, after) =>
                        !neverRecords && recordedAt.TryGetValue(t.Key, out var r) && r > after ? RecordedOnRefusal : StallGuard.DiagnosedInTranscript(snapshot, t, after), now, window);
                    if (d.Refuse)
                    {
                        refused++;
                        // the record the deny asks for names every stalled target of the call
                        recordedAt[d.Target!.Key] = now;
                        foreach (var also in d.Also) recordedAt[also.Key] = now;
                        var cls = d.Second!.Class + "/" + d.Target!.Kind;
                        byClass[cls] = byClass.GetValueOrDefault(cls) + 1;
                        output.WriteLine(JsonSerializer.Serialize(new
                        {
                            verdict = "refused", repeat = !recorded.Add((d.Target.Key, d.Second.Call.ResultAt ?? now)), transcript = f, line = c.Line, at = c.At, tool = c.Tool,
                            target = SecretGuard.Mask(d.Target.Key), kind = d.Target.Kind, cls = d.Second.Class,
                            first = new { line = d.First!.Call.Line, at = d.First.Call.ResultAt, evidence = SecretGuard.Mask(d.First.Evidence) },
                            second = new { line = d.Second.Call.Line, at = d.Second.Call.ResultAt, evidence = SecretGuard.Mask(d.Second.Evidence) },
                            age_min = Math.Round((now - (d.First.Call.ResultAt ?? now)).TotalMinutes, 1),
                            text = Head(SecretGuard.Mask(c.Text) ?? "", 300),
                        }, ReplayJson));
                        prev = c;
                        if (neverRecords) continue;   // never recorded: a refused call never runs
                    }
                    else if (d.ClearedBy is not null && d.ClearedBy != RecordedOnRefusal && !d.ClearedBy.EndsWith(RecordedOnRefusal, StringComparison.Ordinal))
                    {
                        cleared++;
                        output.WriteLine(JsonSerializer.Serialize(new { verdict = "allowed-diagnosed", transcript = f, line = c.Line, at = c.At, tool = c.Tool, why = d.ClearedBy, text = Head(SecretGuard.Mask(c.Text) ?? "", 200) }, ReplayJson));
                    }
                }
                if (inWindow && StallGuard.Classify(c) is { } fl) failures[fl.Class] = failures.GetValueOrDefault(fl.Class) + 1;
                ran.Add(c);
                if (StallGuard.Judged(c.Tool)) prev = c;
            }
        }
        output.WriteLine($"replay ({(neverRecords ? "--never-records: a refused session never records, every later attempt is refused" : "a refused session records, then the call runs: one refusal per stall")}): " +
                         $"{files.Count} transcript(s), {unreadable} unreadable; {judged} judged call(s){(since is null ? "" : $" since {since:O}")}; " +
                         $"refused {refused} of {judged}, allowed {judged - refused} ({cleared} of them after a diagnosis in the transcript); window {(window ?? StallGuard.Window).TotalHours} h; " +
                         $"refusals by class/target: {string.Join(", ", byClass.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))}; " +
                         $"failures by class: {string.Join(", ", failures.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))}; " +
                         $"previous result on disk when the next call was made: under 1 s {gapUnder1} of {gaps}, under 3 s {gapUnder3} of {gaps}");
        return 0;
    }
}
