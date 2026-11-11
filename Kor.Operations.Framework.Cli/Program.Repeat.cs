using System.Globalization;
using System.Text.Json;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Recall;

namespace Kor.Operations.Framework.Cli;

/// <summary>
/// Board row 1998, <see cref="RepeatGate"/>: <c>kor repeat --hook</c> on PreToolUse (Bash, PowerShell, Write, Agent) refuses the start of a
/// measurement, analysis or audit whose topic already has a recorded result, until the call says "inputs changed since #N: ..." for a named
/// record. It reads the same local index as <c>kor recall --hook</c> (<see cref="HookIndex"/>), and only when the call's intent carries a
/// work word: any other call costs the JSON parse and nothing else. Every refusal, and every "inputs changed" that lifts one, is a row in
/// oversight.GuardRefusal (guards <see cref="RepeatGate.GuardName"/> / <see cref="RepeatGate.ReasonGuardName"/>; judge them with
/// Framework tools/guard_log.py). Fails OPEN: any fault allows, logged.
/// <c>kor repeat "intent" [--as-of ISO-UTC]</c>: the dry run on one intent, on an index built now from the database (as of the time given).
/// </summary>
public static partial class Program
{
    private static int RepeatVerb(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainDb? db, BrainDb? board, string hookErrorLog)
    {
        void log(Exception e) => LogHookError(hookErrorLog, o, e);
        if (hook)
        {
            try { return RepeatHook(stdin(), output, db, board, log); }
            catch (Exception e) when (e is not OutOfMemoryException) { log(e); return 0; }
        }
        if (o.Get("--replay") is { } replay) return RepeatReplay(replay, output, db, board);
        var text = o.Positional.ElementAtOrDefault(1);
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("repeat needs the intent: kor repeat \"<a description, or a new file's name and first line>\" [--as-of ISO-UTC]");
        DateTime? asOf = o.Get("--as-of") is { } a ? DateTime.Parse(a, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal) : null;
        var ix = WithStores((b, w) => BrainRecall.Build(b, w, asOf), db, board, 15);
        var rank = o.Get("--command") is { } cmd ? RepeatGate.RankText("Bash", text, text + "\n" + cmd) : text;
        var v = RepeatGate.Decide(text, rank, ix, rank);
        output.WriteLine(v.Refuse ? "REFUSE " + v.Reason
                         : v.AllowedFor is { } id ? $"ALLOW: inputs changed since #{id}: {v.AllowedBecause}"
                         : $"ALLOW: no result record holds a work word of the intent and {RepeatGate.TopicWordsHit} more of its words at score >= {RepeatGate.Floor}" +
                           (RepeatGate.HasWork(text) ? "" : " (the intent carries no work word: the hook would not look)"));
        return 0;
    }

    /// <summary><c>kor repeat --replay transcript.jsonl</c>: every Bash, PowerShell, Agent and Write call of a real transcript through the gate,
    /// on ONE index built now (so a record written after the call can be named - read each line for that). A Write counts as creating its
    /// file when no earlier call in the transcript named that path. Prints each refusal and the totals. Reads only.</summary>
    private static int RepeatReplay(string path, TextWriter output, BrainDb? db, BrainDb? board)
    {
        if (!File.Exists(path)) throw new ArgumentException($"no transcript at {path}");
        var ix = WithStores((b, w) => BrainRecall.Build(b, w), db, board, 15);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int calls = 0, looked = 0, refused = 0, lifted = 0;
        foreach (var line in File.ReadLines(path))
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); } catch (JsonException) { continue; }
            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object
                    || !msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
                foreach (var c in content.EnumerateArray())
                {
                    if (!c.TryGetProperty("type", out var t) || t.GetString() != "tool_use" || !c.TryGetProperty("input", out var input)) continue;
                    var tool = c.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (tool is not ("Bash" or "PowerShell" or "Agent" or "Task" or "Write")) continue;
                    calls++;
                    var file = input.ValueKind == JsonValueKind.Object && input.TryGetProperty("file_path", out var fp) && fp.ValueKind == JsonValueKind.String ? fp.GetString() : null;
                    var intent = RepeatGate.Intent(tool, input, p => seen.Contains(p));
                    if (file is not null) seen.Add(file);
                    if (intent is null) continue;
                    looked++;
                    var mt = BrainRecall.MomentText(tool, input) ?? intent;
                    var v = RepeatGate.Decide(intent, mt, ix, RepeatGate.RankText(tool, intent, mt));
                    if (v.AllowedFor is not null) { lifted++; output.WriteLine($"LIFTED ({tool}) {BrainRecall.FirstSentence(intent, 140)} -> inputs changed since #{v.AllowedFor}"); }
                    if (!v.Refuse) continue;
                    refused++;
                    output.WriteLine($"REFUSE ({tool}) {BrainRecall.FirstSentence(intent.Replace('\n', ' '), 160)}\n  => " +
                                     string.Join(" | ", v.Named.Select(r => $"{r.Doc.Ref} {r.Doc.Kind} {r.Doc.Name} {r.Score.ToString("0.0", CultureInfo.InvariantCulture)} [{string.Join(",", r.Hit.Take(8))}]")));
                }
            }
        }
        output.WriteLine($"REPLAY {Path.GetFileName(path)}: {calls} Bash/PowerShell/Agent/Write calls, {looked} carried a work word, {refused} refused, {lifted} lifted by 'inputs changed since'.");
        return 0;
    }

    private static int RepeatHook(string? raw, TextWriter output, BrainDb? db, BrainDb? board, Action<Exception> log)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        string? session, cwd, tool, intent, moment;
        using (var doc = JsonDocument.Parse(raw))
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return 0;
            string? S(string n) => root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (S("hook_event_name") != "PreToolUse" || !root.TryGetProperty("tool_input", out var input)) return 0;
            session = S("session_id");
            cwd = S("cwd");
            tool = S("tool_name");
            intent = RepeatGate.Intent(tool, input, p => File.Exists(Path.IsPathRooted(p) || cwd is null ? p : Path.Combine(cwd, p)));
            if (intent is null) return 0;
            moment = BrainRecall.MomentText(tool, input) ?? intent;
        }
        var rankText = RepeatGate.RankText(tool, intent, moment);
        var ix = HookIndex(BrainRecall.QueryWords(rankText), db, board);
        var verdict = RepeatGate.Decide(intent, moment, ix, rankText);
        if (!verdict.Refuse && verdict.AllowedFor is null) return 0;
        var matched = BrainStore.Clip(verdict.Refuse
            ? $"{tool}: {BrainRecall.FirstSentence(intent, 160)} => {string.Join(", ", verdict.Named.Select(r => r.Doc.Ref))}"
            : $"{tool}: inputs changed since #{verdict.AllowedFor}: {verdict.AllowedBecause} ({BrainRecall.FirstSentence(intent, 120)})", 400) ?? "";
        try
        {
            var repo = cwd is not null && Directory.Exists(cwd) && Git.TopLevelFast(cwd) is { } top ? Path.GetFileName(top.TrimEnd('/', '\\')) : null;
            var guard = verdict.Refuse ? RepeatGate.GuardName : RepeatGate.ReasonGuardName;
            var rule = verdict.Refuse ? $"row 1998: repeat of #{verdict.Named[0].Doc.Id}" : $"row 1998: inputs changed since #{verdict.AllowedFor}";
            if (board is not null) Stall.StallStore.LogGuardRefusal(board, guard, rule, session, repo, matched);
            else BrainDb.Use3s(KorStandardsConnection.Login.Board, b => Stall.StallStore.LogGuardRefusal(b, guard, rule, session, repo, matched));
        }
        catch (Exception e) when (e is not OutOfMemoryException) { log(e); }   // the verdict stands whether or not it was logged
        if (verdict.Refuse)
            output.WriteLine(JsonSerializer.Serialize(new { hookSpecificOutput = new { hookEventName = "PreToolUse", permissionDecision = "deny", permissionDecisionReason = verdict.Reason } }, RecallJson));
        return 0;
    }
}
