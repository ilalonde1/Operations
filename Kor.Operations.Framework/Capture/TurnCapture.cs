using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kor.Operations.Framework.Capture;

/// <summary>
/// Board row 1393 (Ian, 3 Nov: "It's supposed to be automatic... That's the point of the korbrain isn't it?"): what a TURN
/// produced, read off the session's transcript (the Stop hook's transcript_path), so the Stop capture-check asks for a
/// record at every turn that produced something - not only after a commit.
///
/// A TURN is everything from the newest PROMPT to the reply that ends it. A prompt is a user line that is not a tool
/// result, not Claude Code's own meta text ("Stop hook feedback", images, skills, reminders), not a compaction summary:
/// Ian's message (origin human, or no origin on an older/`claude -p` line), a peer's cross-session message (origin peer -
/// isMeta on the line, so it is recognised by its origin), a background task's notification (origin task-notification),
/// or a slash command. A "Stop hook feedback" line is NOT a prompt: the turn it continues stays the same turn (that is
/// what keeps the ask to once per turn).
///
/// A turn ASKS (<see cref="Evaluate"/>) when it holds any of:
/// <list type="number">
/// <item>FAILED RUN: a recognised run (<see cref="ToolClassifier"/>: test, build, migration, stage_gate, virtual_drafter,
/// *_check.py) whose outcome is fail / refused / stopped / invalid - not a command a hook refused (it never ran). Cleared,
/// so not asked, by a LATER pass in the same turn: a build or test by any passing build or test, a *_check.py by a pass of
/// the same script (the edit-build-fix loop, a red-first run made green). A pipeline run's failure is never cleared.</item>
/// <item>PIPELINE OUTCOME: a stage_gate run, or a virtual_drafter run / check-plot / promote, that finished in the turn
/// with an outcome (not 'unknown'; not virtual_drafter check, the preflight) - in the foreground, or in the background
/// with its task-notification starting this turn.</item>
/// <item>FAILURE READ: a pipeline's failure line, as the pipeline prints it at the start of a line (GATE FAILED: / STEP
/// FAILED: / "  FAIL name: ..." / stage_gate: STOPPED|REFUSED / "NAME FAIL: N finding(s)"), in a tool result of the turn
/// - a run log tailed, a run.json printed (3 Nov: Halifax v5 STOPPED on DIMTEXT FAIL, read off the log, not asked).
/// Not when the same line was already in a tool result of an earlier turn (<see cref="Turn.SeenBefore"/>: a re-read of
/// history), nor a pending state ("not available yet").</item>
/// <item>IAN'S DECISION: the turn's prompt is Ian's own typed words (origin human; never a peer, a notification, a
/// `claude -p` script, pasted or wrapped text - see <see cref="IsDecision"/>) and reads as a decision: an answer
/// (yes/go/do it/proceed; ok/no only to a question the previous reply asked), an imperative aimed at the work at a
/// sentence's start (do / run / fix / let's / don't / never / always / use ... - not a rebuke of the session's manner),
/// or a choosing phrase (instead of, rather than, from now on, we must, I want it, I don't want, go ahead, we'll use).
/// His words are quoted in the ask and kept, credential values masked, on the capture-ask event the decision cites.</item>
/// <item>DIAGNOSIS: the turn's final reply says why something happened (root cause, the cause was, caused by, turned out,
/// the reason is, that's / which is why, the bug/fault/problem was, it was because, why it failed) - not inside a
/// hypothetical (if / whether / might / once / will / means ...), not a cause still unknown, not "which is why I ..."
/// (the session's own method).</item>
/// </list>
/// and NOTHING was recorded in it: no `kor record` / `kor issue` (a 'record' event of this session at or after the turn's
/// start, live; a kor record/issue or kor_brain.py note command in the turn, in a replay). Never asked: a `claude -p`
/// turn (entrypoint other than cli: its final text is read by a script), a turn whose reply is Claude Code's own error
/// (spend limit, API error - the session cannot answer).
///
/// PEERS (decided here, row 1393): a cross-session message relaying Ian ("OverLORD, from Ian: ...") does NOT count as
/// Ian's decision in the session that receives it. The words are the peer's, quoting him second-hand; the session Ian
/// actually typed them into (the OverLORD's, origin human there) is the one asked to record them verbatim. So a peer's
/// own words can never be filed as Ian's.
///
/// Does NOT cover: the quality or truth of what is recorded, or of "nothing to record" (the session may say so and stop);
/// a decision Ian gives inside a long message whose sentences all end in '?' or carry none of the choice words; a
/// diagnosis phrased without the listed words ("the 5'-0" moved because ..." with no "because the" form is missed); WORDS
/// cannot tell a "which is why" that explains a DESIGN from one that names a CAUSE, so the first is asked too (measured:
/// requiring a went-wrong word lost about 85 real diagnoses to save about 40 such asks - each answered in one line); a
/// failure read from a log whose line was seen only before the transcript's last 4 MB (live) is asked once more; a
/// finding made in a turn that ends while a background run is still going (its outcome is asked in the turn its
/// notification starts); subagent turns (Claude Code fires SubagentStop, not Stop, for them - not wired); netops and oq.py
/// commands (no outcome a classifier can read), except as a failure the session's own reply diagnoses; a red-first test
/// run that is not made green in the same turn (asked; the session says it was the expected red). Same-class fault it
/// would miss: a diagnosis or decision in a turn where the session recorded something UNRELATED - any record in the turn
/// satisfies the ask.
/// </summary>
public static class TurnCapture
{
    // ---- the transcript ------------------------------------------------------------------------------------------------
    /// <param name="Entrypoint">the line's entrypoint: "cli" = an interactive session, "sdk-cli" = `claude -p` (a review,
    /// a probe, a cold test - its prompt is a script's, never Ian's).</param>
    public sealed record Prompt(int Line, DateTimeOffset? At, string Origin, string? PeerName, string Text, string? Entrypoint = null);

    public sealed record ToolCall(int Line, string Id, string Name, string? Command, string? Cwd, bool Background, string? Output,
                                  bool IsError, string? BackgroundTaskId);

    public sealed record Notification(int Line, string TaskId, string? ToolUseId, string Status, int? ExitCode, string? OutputFile,
                                      string Summary);

    public sealed record Turn(Prompt Start, string? PreviousReply, IReadOnlyList<ToolCall> Tools,
                              IReadOnlyList<Notification> Notifications, string FinalReply, int FinalReplyLine, int EndLine)
    {
        /// <summary>The pipeline failure lines (<see cref="GateFailSeen"/>) already in a tool result of an EARLIER turn of
        /// what was read - a re-read of one is history, not news (the lead, 3 Nov; dry run: 24 of 70 such asks).</summary>
        public IReadOnlySet<string> SeenBefore { get; init; } = new HashSet<string>();

        /// <summary>The line of the turn's first "Stop hook feedback", or null when no Stop hook blocked in it.</summary>
        public int? FirstStopLine { get; init; }

        /// <summary>The reply the FIRST Stop of the turn saw (the text before the first Stop-hook feedback).</summary>
        public string? FirstStopReply { get; init; }

        /// <summary>
        /// The turn as its FIRST Stop saw it (the tools and reply before the first Stop-hook feedback). Live, capture-check
        /// judges the turn at its first Stop and, if it did not ask there, again at a continuation Stop over the whole turn;
        /// the replay does the same - this first, then the whole turn with both replies (CaptureCheckReplay).
        /// </summary>
        public Turn AtFirstStop() => FirstStopLine is not { } at ? this : this with
        {
            Tools = Tools.Where(c => c.Line < at).ToList(),
            Notifications = Notifications.Where(n => n.Line < at).ToList(),
            FinalReply = FirstStopReply ?? "",
            FirstStopLine = null,
        };
    }

    /// <summary>Every turn of a transcript, in order (the replay). A missing or unreadable file is no turns.</summary>
    public static List<Turn> Turns(string path)
    {
        if (!File.Exists(path)) return new List<Turn>();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(fs, new UTF8Encoding(false));
        var lines = new List<string>();
        for (string? l; (l = r.ReadLine()) is not null;) lines.Add(l);
        return Build(lines, 1);
    }

    /// <summary>
    /// The LAST turn of a transcript (the Stop hook): the last <see cref="HistoryBytes"/> (4 MB) are read, then more in
    /// chunks until the last turn's prompt and the turn before it are in, so a 100 MB transcript costs only its tail. The
    /// failure lines of the turns read before it are its <see cref="Turn.SeenBefore"/> - history further back than what
    /// was read is not seen (a re-read of an older failure asks once). Null when no prompt is found within
    /// <paramref name="maxBytes"/>.
    /// </summary>
    public static Turn? LastTurn(string? path, long maxBytes = 64L << 20)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var len = fs.Length;
        long take = Math.Min(len, HistoryBytes);
        while (true)
        {
            var whole = take >= len;
            var all = Tail(fs, len, take, whole);
            // line numbers: exact when the whole file was read; else counted back from the end (-1 = the last line)
            var turns = Build(all, whole ? 1 : -1);
            // done when the last turn's prompt AND the turn before it (its reply decides a yes/no answer) were read
            if (whole || turns.Count >= 2 || take >= maxBytes) return turns.Count > 0 ? turns[^1] : null;
            take = Math.Min(len, take * 4);
        }
    }

    public const long HistoryBytes = 4L << 20;

    private static List<string> Tail(FileStream fs, long len, long take, bool whole)
    {
        fs.Seek(len - take, SeekOrigin.Begin);
        var buf = new byte[take];
        fs.ReadExactly(buf, 0, (int)take);
        var all = Encoding.UTF8.GetString(buf).Split('\n').ToList();
        if (!whole) all.RemoveAt(0);   // a partial first line
        if (all.Count > 0 && all[^1].Trim().Length == 0) all.RemoveAt(all.Count - 1);
        return all;
    }

    /// <summary>
    /// The tool calls (by id) of a transcript whose ids are in <paramref name="ids"/> - a background launch can be hours of
    /// lines before the notification that ends it. One forward pass, parsing only the lines that hold one of the ids.
    /// </summary>
    public static Dictionary<string, ToolCall> FindLaunches(string path, IReadOnlyCollection<string> ids)
    {
        var found = new Dictionary<string, ToolCall>();
        if (ids.Count == 0 || !File.Exists(path)) return found;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(fs, new UTF8Encoding(false));
        var no = 0;
        for (string? l; (l = r.ReadLine()) is not null;)
        {
            no++;
            if (!l.Contains("\"tool_use\"") || !ids.Any(id => l.Contains(id, StringComparison.Ordinal))) continue;
            foreach (var t in Build(new List<string> { FakePrompt, l }, no - 1).SelectMany(t => t.Tools))
                if (ids.Contains(t.Id)) found[t.Id] = t;
        }
        return found;
    }

    // a prompt line placed before a lone assistant line, so Build reads its tool calls
    private const string FakePrompt = "{\"type\":\"user\",\"origin\":{\"kind\":\"human\"},\"message\":{\"role\":\"user\",\"content\":\"x\"}}";

    private sealed class Pending
    {
        public Prompt? Start;
        public string? PrevReply;
        public readonly List<ToolCall> Tools = new();
        public readonly Dictionary<string, int> ToolIndex = new();
        public readonly List<Notification> Notes = new();
        public readonly StringBuilder Reply = new();
        public int ReplyLine;
        public int End;
        public int? FirstStop;
        public string? FirstStopReply;
    }

    private static readonly Regex NoteBlock = new(@"<task-notification>.*?(?:</task-notification>|\z)", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex NoteTag = new(@"<(?<t>task-id|tool-use-id|output-file|status|summary)>(?<v>.*?)</\k<t>>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex ExitIn = new(@"exit code (?<n>-?\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <param name="firstLineNo">the 1-based number of lines[0]; a negative number = unknown (numbered -N from the end).</param>
    private static List<Turn> Build(List<string> lines, int firstLineNo)
    {
        var turns = new List<Turn>();
        var p = new Pending();
        string? lastReply = null;
        bool? sidechainMode = null;
        void Close()
        {
            if (p.Start is not null)
            {
                var reply = p.Reply.ToString().Trim();
                turns.Add(new Turn(p.Start, p.PrevReply, p.Tools.ToList(), p.Notes.ToList(), reply, p.ReplyLine, p.End)
                          { FirstStopLine = p.FirstStop, FirstStopReply = p.FirstStopReply });
                if (reply.Length > 0) lastReply = reply;
            }
        }
        for (var i = 0; i < lines.Count; i++)
        {
            var no = firstLineNo > 0 ? firstLineNo + i : i - lines.Count;
            var raw = lines[i];
            if (raw.Length == 0 || !(raw.Contains("\"type\":\"user\"") || raw.Contains("\"type\":\"assistant\""))) continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(raw); } catch (JsonException) { continue; }
            using (doc)
            {
                var d = doc.RootElement;
                var type = S(d, "type");
                if (type is not ("user" or "assistant")) continue;
                var side = d.TryGetProperty("isSidechain", out var sc) && sc.ValueKind == JsonValueKind.True;
                sidechainMode ??= side;
                if (side != sidechainMode) continue;
                if (!d.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) continue;
                msg.TryGetProperty("content", out var content);
                DateTimeOffset? at = DateTimeOffset.TryParse(S(d, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var ts) ? ts : null;
                if (type == "assistant")
                {
                    if (p.Start is null) continue;
                    if (content.ValueKind != JsonValueKind.Array) continue;
                    foreach (var b in content.EnumerateArray())
                    {
                        var bt = S(b, "type");
                        if (bt == "text")
                        {
                            p.Reply.Append(S(b, "text")).Append('\n');
                            p.ReplyLine = no;
                        }
                        else if (bt == "tool_use")
                        {
                            // a reply is the text AFTER the last tool call
                            p.Reply.Clear();
                            var input = b.TryGetProperty("input", out var inp) && inp.ValueKind == JsonValueKind.Object ? inp : default;
                            var cmd = input.ValueKind == JsonValueKind.Object ? S(input, "command") : null;
                            var bg = input.ValueKind == JsonValueKind.Object && input.TryGetProperty("run_in_background", out var rb) && rb.ValueKind == JsonValueKind.True;
                            var id = S(b, "id") ?? "";
                            p.ToolIndex[id] = p.Tools.Count;
                            p.Tools.Add(new ToolCall(no, id, S(b, "name") ?? "", cmd, S(d, "cwd"), bg, null, false, null));
                        }
                    }
                    p.End = no;
                    continue;
                }
                // user
                if (content.ValueKind == JsonValueKind.Array && content.GetArrayLength() > 0
                    && content.EnumerateArray().Any(b => S(b, "type") == "tool_result"))
                {
                    string? bgTask = d.TryGetProperty("toolUseResult", out var tur) && tur.ValueKind == JsonValueKind.Object ? S(tur, "backgroundTaskId") : null;
                    foreach (var b in content.EnumerateArray())
                    {
                        if (S(b, "type") != "tool_result") continue;
                        var id = S(b, "tool_use_id") ?? "";
                        if (!p.ToolIndex.TryGetValue(id, out var k)) continue;
                        p.Tools[k] = p.Tools[k] with
                        {
                            Output = Text(b.TryGetProperty("content", out var c) ? c : default),
                            IsError = b.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True,
                            BackgroundTaskId = bgTask ?? p.Tools[k].BackgroundTaskId,
                        };
                    }
                    p.End = no;
                    continue;
                }
                var text = Text(content);
                var origin = d.TryGetProperty("origin", out var o) && o.ValueKind == JsonValueKind.Object ? S(o, "kind") : null;
                var meta = d.TryGetProperty("isMeta", out var im) && im.ValueKind == JsonValueKind.True;
                var compact = d.TryGetProperty("isCompactSummary", out var ic) && ic.ValueKind == JsonValueKind.True;
                var kind = PromptOrigin(origin, meta, compact, text);
                // a subagent's transcript (every line a sidechain): its prompt is the PARENT session's words, never Ian's
                // (dry run 3 Nov: 412 of 567 subagent turns read as Ian's decisions before this)
                if (sidechainMode == true && kind == "human") kind = "agent";
                if (kind is null)
                {
                    p.End = no;
                    // Claude Code's own text inside the turn (Stop hook feedback, images, reminders). After Stop-hook feedback
                    // the reply that follows is the turn's reply, not the one the hook answered.
                    if (text.TrimStart().StartsWith("Stop hook feedback", StringComparison.Ordinal))
                    {
                        if (p.FirstStop is null) { p.FirstStop = no; p.FirstStopReply = p.Reply.ToString().Trim(); }
                        p.Reply.Clear();
                    }
                    continue;
                }
                Close();
                p = new Pending { PrevReply = lastReply, End = no };
                var peer = kind == "peer" && o.ValueKind == JsonValueKind.Object ? S(o, "name") : null;
                p.Start = new Prompt(no, at, kind, peer, text, S(d, "entrypoint"));
                if (kind == "task-notification")
                    // one message can carry several notifications (seen in 3 of 522 transcripts): each block is its own
                    foreach (Match block in NoteBlock.Matches(text))
                    {
                        var tags = NoteTag.Matches(block.Value).GroupBy(m => m.Groups["t"].Value)
                                          .ToDictionary(g => g.Key, g => g.First().Groups["v"].Value.Trim());
                        var summary = tags.GetValueOrDefault("summary") ?? "";
                        int? exit = ExitIn.Match(summary) is { Success: true } em && int.TryParse(em.Groups["n"].Value, out var n) ? n : null;
                        p.Notes.Add(new Notification(no, tags.GetValueOrDefault("task-id") ?? "", tags.GetValueOrDefault("tool-use-id"),
                                                     tags.GetValueOrDefault("status") ?? "", exit, tags.GetValueOrDefault("output-file"), summary));
                    }
            }
        }
        Close();
        // each turn learns the failure lines of every turn before it (of what was read)
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < turns.Count; i++)
        {
            turns[i] = turns[i] with { SeenBefore = new HashSet<string>(seen, StringComparer.Ordinal) };
            foreach (var c in turns[i].Tools)
                if (c.Output is { } o)
                    foreach (Match g in GateFailSeen.Matches(o)) seen.Add(g.Value.Trim());
        }
        return turns;
    }

    /// <summary>The prompt's origin, or null when the line is not a prompt (Claude Code's own text inside a turn).</summary>
    internal static string? PromptOrigin(string? origin, bool meta, bool compact, string text)
    {
        if (compact) return null;
        switch (origin)
        {
            case "human": return "human";
            case "peer": return "peer";
            case "task-notification": return "task-notification";
            case not null: return meta ? null : origin;
        }
        var t = text.TrimStart();
        if (t.StartsWith("<task-notification>", StringComparison.Ordinal)) return "task-notification";
        if (t.StartsWith("Another Claude session sent a message", StringComparison.Ordinal) || t.StartsWith("<cross-session-message", StringComparison.Ordinal)) return "peer";
        if (meta) return null;
        if (t.StartsWith("<command-name>", StringComparison.Ordinal) || t.StartsWith("<local-command", StringComparison.Ordinal)) return "command";
        if (t.StartsWith("[Request interrupted", StringComparison.Ordinal)) return null;
        return "human";
    }

    private static string? S(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Text(JsonElement c)
    {
        if (c.ValueKind == JsonValueKind.String) return c.GetString() ?? "";
        if (c.ValueKind != JsonValueKind.Array) return "";
        var sb = new StringBuilder();
        foreach (var b in c.EnumerateArray())
            if (b.ValueKind == JsonValueKind.Object && S(b, "text") is { } t) sb.Append(t).Append('\n');
            else if (b.ValueKind == JsonValueKind.String) sb.Append(b.GetString()).Append('\n');
        return sb.ToString();
    }

    // ---- what a turn produced ----------------------------------------------------------------------------------------
    public sealed record Reason(string Kind, int Line, string Text);

    /// <summary>A recognised run in the turn: its event shape, and where (the tool_use line, or the notification's).</summary>
    public sealed record Run(int Line, ToolClassifier.ToolEvent Event, bool Background, ToolCall Call, Notification? Note);

    /// <summary>Foreground runs: each Bash/PowerShell call whose command the classifier recognises and that finished in
    /// the turn (a background launch is not one - its result comes as a notification).</summary>
    public static List<Run> ForegroundRuns(Turn t) =>
        t.Tools.Where(c => c.Name is "Bash" or "PowerShell" && !c.Background && c.BackgroundTaskId is null && c.Output is not null
                           && !ToolClassifier.IsHookRefusal(c.Output))
               .Select(c => (c, e: ToolClassifier.Classify(c.Command, c.Output, c.IsError ? ToolClassifier.ExitCodeOf(c.Output ?? "") ?? 1 : null)))
               .Where(x => x.e is not null).Select(x => new Run(x.c.Line, x.e!, false, x.c, null)).ToList();

    /// <summary>
    /// Background runs whose result arrived in this turn: the turn's task-notification names the tool_use; its command is
    /// found in <paramref name="launches"/> (every tool call of the transcript read so far, by id), its output read from the
    /// notification's output file - and, when that is empty and the command sent its stdout to a file ("&gt; f", "&gt;&gt; f"),
    /// that file's last 256 KB. Exit code from the notification's summary.
    /// </summary>
    public static List<Run> BackgroundRuns(Turn t, IReadOnlyDictionary<string, ToolCall> launches, Func<string, string?>? read = null)
    {
        read ??= TailOf;
        var runs = new List<Run>();
        foreach (var n in t.Notifications)
        {
            if (n.ToolUseId is null || !launches.TryGetValue(n.ToolUseId, out var call) || call.Command is null) continue;
            if (ToolClassifier.Classify(call.Command, "", 0) is null) continue;   // not a recognised run: nothing read
            var output = n.OutputFile is { } f ? read(f) ?? "" : "";
            if (output.Trim().Length == 0 && RedirectTarget(call.Command, call.Cwd) is { } target) output = read(target) ?? "";
            // the exit code is only what the summary SAYS (review row 364: none was invented); a run that did not finish
            // on its own (killed, stopped - not completed nor failed) is no run outcome
            if (n.ExitCode is null && n.Status is not ("completed" or "failed")) continue;
            var e = ToolClassifier.Classify(call.Command, output, n.ExitCode);
            if (e is not null) runs.Add(new Run(n.Line, e, true, call, n));
        }
        return runs;
    }

    private static readonly Regex Redirect = new(@"(?<![0-9&])>>?\s*(?<f>""[^""]+""|'[^']+'|[^\s;&|<>()]+)", RegexOptions.CultureInvariant);

    /// <summary>The file a command sent its stdout to (the last "&gt; f" in it), absolute against its cwd; null if none.</summary>
    internal static string? RedirectTarget(string command, string? cwd)
    {
        var neutral = ToolClassifier.Neutralise(command);
        var m = Redirect.Matches(neutral).LastOrDefault();
        if (m is null) return null;
        var f = command.Substring(m.Groups["f"].Index, m.Groups["f"].Length).Trim('"', '\'');
        if (f.StartsWith("/dev/", StringComparison.Ordinal) || f.Contains('$') || f.Contains('%')) return null;
        f = Regex.Replace(f, @"^/([a-zA-Z])/", x => x.Groups[1].Value.ToUpperInvariant() + ":/");
        // a cd at the start of the command ("cd X && ...") is where a relative target lands
        var cd = Regex.Match(command, @"^\s*cd\s+(?<d>""[^""]+""|'[^']+'|\S+)\s*&&");
        var dir = cd.Success ? Regex.Replace(cd.Groups["d"].Value.Trim('"', '\''), @"^/([a-zA-Z])/", x => x.Groups[1].Value.ToUpperInvariant() + ":/") : cwd;
        try { return Path.IsPathRooted(f) ? Path.GetFullPath(f) : dir is null ? null : Path.GetFullPath(Path.Combine(dir, f)); }
        catch (Exception) { return null; }
    }

    /// <summary>The last 256 KB of a file as text, or null.</summary>
    internal static string? TailOf(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var take = (int)Math.Min(fs.Length, 256 * 1024);
            fs.Seek(fs.Length - take, SeekOrigin.Begin);
            var buf = new byte[take];
            fs.ReadExactly(buf, 0, take);
            return Encoding.UTF8.GetString(buf);
        }
        catch (Exception) { return null; }
    }

    private static readonly string[] Bad = { "fail", "refused", "stopped", "invalid" };

    /// <summary>
    /// A pipeline run whose outcome is asked whatever it is: stage_gate run, virtual_drafter run / check-plot / promote.
    /// NOT virtual_drafter check (the preflight - dry run 3 Nov: 5 of 12 sampled run-outcome asks were "check -&gt;
    /// unknown" before a run started; a failed check is still a failed run) and not a *_check.py (asked when it fails).
    /// </summary>
    public static bool IsPipeline(ToolClassifier.ToolEvent e) =>
        e.Kind == "gate" && (e.Ref.StartsWith("virtual_drafter ", StringComparison.Ordinal)
                                 ? !e.Ref.StartsWith("virtual_drafter check ", StringComparison.Ordinal) && e.Ref != "virtual_drafter check"
                                 : !e.Ref.Contains(".py", StringComparison.Ordinal));

    /// <summary>
    /// Why this turn should be asked, or [] (the rules in the class summary). <paramref name="runs"/> = the turn's
    /// foreground and background runs.
    /// </summary>
    public static List<Reason> Evaluate(Turn t, IReadOnlyList<Run> runs)
    {
        var reasons = new List<Reason>();
        // a non-interactive session (`claude -p`: kor_review's reviewers, probes, cold tests) is never asked: its prompt is a
        // script's, its result goes back to the script that reads its final text - a block would change that text
        if (t.Start.Entrypoint is { } ep && ep != "cli") return reasons;
        if (IsApiError(t.FinalReply)) return reasons;
        for (var i = 0; i < runs.Count; i++)
        {
            var r = runs[i];
            var bad = Bad.Contains(r.Event.Outcome);
            // cleared by a later pass in the turn (the edit-build-fix loop, a red-first run made green): a build or test by any
            // later passing build or test (a test run builds the code), a *_check.py by a later pass of the same script.
            // A stage_gate or virtual_drafter failure is never cleared: a pipeline run is the work.
            var later = runs.Skip(i + 1).Where(x => x.Event.Outcome == "pass").ToList();
            var cleared = bad && (r.Event.Kind is "test" or "build" && later.Any(x => x.Event.Kind is "test" or "build")
                                  || r.Event.Kind == "gate" && !IsPipeline(r.Event) && !r.Event.Ref.StartsWith("virtual_drafter ", StringComparison.Ordinal)
                                     && later.Any(x => x.Event.Ref.Split(' ')[0] == r.Event.Ref.Split(' ')[0]));
            var where = r.Background ? " (background, its result arrived in this turn)" : "";
            if (bad && !cleared) reasons.Add(new Reason("failed-run", r.Line, $"{r.Event.Kind} {r.Event.Outcome}: {Clip(r.Event.Ref, 160)}{where}"));
            // an 'unknown' outcome is not an outcome (the lead, 3 Nov): nothing was learned that the event does not hold
            else if (IsPipeline(r.Event) && r.Event.Outcome != "unknown") reasons.Add(new Reason("run-outcome", r.Line, $"{Clip(r.Event.Ref, 160)} -> {r.Event.Outcome}{where}"));
        }
        // a pipeline's failure READ in the turn (a run log tailed, a run.json printed): the outcome the session learned,
        // though no run of its own produced it (3 Nov, kor-drafter: Halifax v5 STOPPED on DIMTEXT FAIL, read off the log)
        if (reasons.All(r => r.Kind != "failed-run"))
            foreach (var c in t.Tools)
            {
                if (c.Name is not ("Bash" or "PowerShell") || c.Output is null || runs.Any(r => r.Call.Id == c.Id)) continue;
                if (GateFailSeen.Matches(c.Output).FirstOrDefault(g => !t.SeenBefore.Contains(g.Value.Trim()) && !PendingState.IsMatch(g.Value)) is { } g)
                {
                    reasons.Add(new Reason("failed-gate-seen", c.Line, Clip(g.Value.Trim(), 200)));
                    break;
                }
            }
        if (t.Start.Origin == "human" && IsDecision(t.Start.Text, t.PreviousReply) is { } words)
            reasons.Add(new Reason("ian-decision", t.Start.Line, words));
        if (Diagnosis(t.FinalReply) is { } d)
            reasons.Add(new Reason("diagnosis", t.FinalReplyLine, d));
        return reasons;
    }

    /// <summary>
    /// A pipeline's failure line as the pipeline PRINTS it, at the start of an output line: virtual_drafter's
    /// "GATE FAILED: x" / "STEP FAILED: x" / "  FAIL name: detail", stage_gate's "stage_gate: STOPPED|REFUSED", a check's
    /// "NAME FAIL: N finding(s)". Anchored, so a grep of the SOURCE ("tools/v.py:86:  raise SystemExit(f"GATE FAILED...")
    /// or a print statement does not count.
    /// </summary>
    private static readonly Regex GateFailSeen = new(
        @"^[ \t]*(?:GATE FAILED: \S[^\n]*|STEP FAILED: \S[^\n]*|FAIL [^\s:]+[^\n]*?: [^\n]*|stage_gate: (?:STOPPED|REFUSED)[^\n]*|[A-Z][A-Z0-9_ -]{2,40} FAIL: [1-9]\d* finding[^\n]*)$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    // a line that says something is not there YET is a pending state, not a failure (dry run 3 Nov: "FAIL KOR-D201-DET 01 |
    // text: Revit re-export DXF not available yet")
    private static readonly Regex PendingState = new(@"\bnot (?:available|made|ready|run|built|exported) yet\b|\bnot yet\b", I);

    // ---- Ian's decisions -----------------------------------------------------------------------------------------------
    private const RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    // an apostrophe may be typed ' ’ or " (Ian, 3 Nov: DON"T) or left out
    private const string Apos = @"['’""]?";
    // an ANSWER that decides on its own: yes / no / go / do it ... (a short message, or a reply to a question)
    private static readonly Regex Answer = new(@"^\W*(?:yes|yep|yeah|yup|go(?: ahead)?|do it|approved?|proceed|leave it)\b", I);
    // an acknowledgement, or a "no": a decision only when the previous reply asked something (dry run 3 Nov: "OK - it's
    // running", and "NO GUESSING" / "NO ASSUMING" - rebukes, not answers)
    private static readonly Regex Ack = new(@"^\W*(?:ok(?:ay)?|sure|agreed?|correct|fine|right|carry on|no|nope|nah)\b", I);
    // a rebuke of the session's manner is not a decision about the work (dry run 3 Nov: "Don't patronize me", "Stop
    // patronizing me", "Don't get off track", "Don't just run away and START BUILDING to look busy")
    private static readonly Regex Rebuke = new(
        @"^\W*(?:(?:please|and|but|so)\W+)*(?:don['’""]?t|do not|stop|never)\W+(?:just\W+|fucking\W+|you\W+)*" +
        @"(?:patroni|lectur|apologi|talk|say|tell me|get off|run away|bullshit|lie|lying|pretend|sell|ask me|asking|manag|baby|treat)", I);
    // ("Never guess or assume" is Ian's rule, not a rebuke: guess / assume are not in the list above)
    // choosing forms the dry run missed: "please go ahead", "We'll use 302N", a sentence that is only "Go", and an
    // IMPERATIVE aimed at the work at a sentence's start ("do mesh01, esxi and all the other non pcs", "Run Halifax v5.",
    // "Make the colors KOR palette") - the row's own "do X". Not "make sure" (an exhortation) nor a request for information
    // (tell / show / give / explain are not in the list).
    private static readonly Regex AlsoChoice = new(
        @"\bgo ahead\b|\bwe['’""]?ll use\b|^\W*go\W*$|" +
        // the verb must be followed by what it acts on - not by nothing, "it" / "this" / "that" alone, or "on" (dry run 3 Nov,
        // round 5: "FIX IT", "Update", "update on big picture" - demands and status requests); and "keep going / at it / it
        // going" continues, it does not choose
        @"^\W*(?:(?:yes|ok|okay|so|and|then|now|please|just)\W+)*(?:do|run|rerun|fix|add|build|deploy|restart|publish|commit|push|delete|remove|move|" +
        @"put|write|send|ship|merge|revert|install|update|change|rename|start|finish|switch|drop|hold|wait|skip|pull|copy|cancel|kill|wire|apply|" +
        @"record|retire|archive|clean|port|book|make(?! sure)|keep(?!\W+(?:going|at it|it going|it up)))\b(?=\W+(?!(?:it|this|that|on)\b\W*$)(?!on\b)\w)" +
        @"|\bcarry on with \w|^\W*(?:(?:ok|okay|now|so|then|and)\W+)*(?:get|go) back to \w", I);
    // Claude Code's session-start routine and housekeeping, pasted or typed: not a decision about the work
    private static readonly Regex Routine = new(@"five questions|\bthe handover\b|\bto (?:my|the) desktop\b", I);
    // a sentence that CHOOSES: an imperative at the sentence's start, or a choosing phrase anywhere in it. "don't" and
    // "never" mid-sentence are NOT choices ("I don't remember...", "a never ending path") - dry run 3 Nov, 8 of 45 sampled.
    private static readonly Regex Choice = new(
        // not "let's make sure" (an exhortation), "always <verb>ing" ("Always checking we're optimized"), "stop with"
        @"^(?:(?:yes|ok|okay|so|and|but|then|no|please|now)\W+)*(?:let" + Apos + @"s(?! make sure)|let us|don" + Apos + @"t|do not|never|always(?!\W+\w+ing\b)|stop(?! with\b)|leave it|stick with|go with|use)\b" +
        @"|\b(?:instead of|rather than|from now on|we must|must not|is not allowed|i want (?:it|this|that|you|them|everything|the|all)|" +
        @"i don" + Apos + @"t want|(?:we|you) don" + Apos + @"t need to|use \S+ as (?:the )?(?:base|default|source))\b", I);
    private static readonly Regex SentenceSplit = new(@"(?<=[.!?])\s+|\n+", RegexOptions.CultureInvariant);
    // text Ian pasted (another tool's output) is not his words (dry run 3 Nov: a Codex report read as a decision)
    // ... and neither is anything Claude Code wraps into the message: a relayed session's message, a notification, a
    // reminder, a local command's output (the lead, 3 Nov: Ian's words are ONLY the text he typed)
    private static readonly Regex Pasted = new(
        @"<(?<t>pasted_content|cross-session-message|task-notification|system-reminder|local-command-stdout|local-command-stderr|local-command-caveat|command-name|command-message|command-args)\b[^>]*>.*?(?:</\k<t>>|\z)",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    /// <summary>
    /// Ian's words when his message reads as a decision, else null. Pasted content (&lt;pasted_content&gt;) is removed first.
    /// An ANSWER (yes/no/go/do it/proceed/leave it first) counts when the previous reply asked a question (a '?' in its last
    /// 600 characters) or the message is at most 60 characters; an ACKNOWLEDGEMENT (ok/sure/fine/carry on) only when it
    /// asked. A CHOICE sentence counts when it is not itself a question: it STARTS with let's / don't / do not / never /
    /// always / stop / leave it / stick with / go with / use, or holds instead of / rather than / from now on / we must /
    /// must not / I want it|this|... / I don't want / we|you don't need to / use X as the base. The words are returned
    /// verbatim (whitespace folded), at most 400 characters - a credential in them is masked by the event write.
    /// </summary>
    public static string? IsDecision(string message, string? previousReply)
    {
        var m = Pasted.Replace(message, " ").Trim();
        if (m.Length == 0 || Routine.IsMatch(m)) return null;
        var asked = previousReply is not null && previousReply.TrimEnd() is var pr && pr[Math.Max(0, pr.Length - 600)..].Contains('?');
        var question = m.TrimEnd().EndsWith('?');
        if (!question && Answer.IsMatch(m) && (asked || m.Length <= 60)) return Fold(m);
        // an acknowledgement decides only on its own ("OK", "No.") or with a choice after it - "OK - I've closed.", "No - I
        // don't care anymore", "OK - everything is done, pushed" are remarks (dry run 3 Nov, round 5)
        if (!question && asked && Ack.Match(m) is { Success: true } ack)
        {
            var rest = m[(ack.Index + ack.Length)..].Trim(' ', '-', ',', '.', '!', '–', '—');
            if (rest.Length <= 12) return Fold(m);
        }
        // a message with "??" in it is rhetorical (dry run 3 Nov: "Am I not running 162???", "What's happening? Anything???",
        // "is it what he NEEDS????"): only an answer at its start makes it a decision
        if (m.Contains("??", StringComparison.Ordinal)) return null;
        foreach (var s in SentenceSplit.Split(m).Select(x => x.Trim()).Where(x => x.Length > 0))
            if (!s.EndsWith('?') && !Rebuke.IsMatch(s) && (Choice.IsMatch(s) || AlsoChoice.IsMatch(s))) return Fold(m);
        return null;
    }

    // ---- a diagnosis in the reply --------------------------------------------------------------------------------------
    private static readonly Regex DiagnosisWords = new(
        @"\broot cause\b|\bthe cause (?:is|was)\b|\bcaused by\b|\bturns? out\b|\bturned out\b|\bthe reason (?:is|was)\b|" +
        @"\b(?:that|this|which) is why\b|\bthat'?s why\b|\bthe (?:bug|fault|problem|culprit|regression) (?:is|was)\b|\bit (?:was|is) because\b|" +
        @"\bwhy it (?:failed|broke|stopped|hung)\b", I);

    private static readonly Regex Hypothetical = new(@"\b(?:if|whether|unless|until|in case|might|may|could|would|won['’]?t|means|once|will|guess|grouped)\b", I);

    // The "why" phrases (that's why / which is why / this is why / the reason is) and "turned out": NOT a diagnosis when the
    // clause after the phrase has I / we / my / our as its subject - the session explaining its own method ("That's why I
    // ran the hashes", "which is why I stopped here to check") - or when "turned out" is followed by nothing found ("turned
    // out to matter", "to be worth checking"). A why-phrase explaining a DESIGN ("which is why it dry-runs") cannot be told
    // from one explaining a CAUSE ("which is why your booking was saved an hour off") by words: requiring a went-wrong word
    // was tried (dry-run pass 4, 3 Nov) and lost about 85 real diagnoses to save about 40 false asks - so design
    // explanations are asked, and the session answers them in one line (named and justified in row 1393's dry run).
    private static readonly Regex WhyPhrase = new(@"\b(?:(?:that|this|which) is why|that'?s why|the reason (?:is|was)|turns? out|turned out)\b", I);
    private static readonly Regex SelfSubject = new(@"^\W*(?:I|we|my|our|I['’]?(?:m|ve|ll|d))\b", I);
    private static readonly Regex EmptyTurnOut = new(@"^\W*to (?:matter|be worth|be (?:mostly )?answered|be fine)\b", I);
    // a "root cause" that is not one: still unknown, not asserted, a heading
    private static readonly Regex NoCause = new(@"\b(?:unknown|not known|not yet|still|not going to|isn['’]?t known|no root cause|not in any)\b", I);

    /// <summary>The sentence of the reply that diagnoses, or null.</summary>
    public static string? Diagnosis(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply) || IsApiError(reply)) return null;
        // a hypothetical is not a diagnosis (dry run 3 Nov: "...delete them all if they turn out to be old build leftovers",
        // "Clean means the bug is in the reload"): the first match not preceded, in its sentence, by if / whether / unless
        // / until / in case / might / may / could / means
        Match? m = null;
        foreach (Match x in DiagnosisWords.Matches(reply))
        {
            var from = Math.Max(0, reply.LastIndexOfAny(new[] { '.', '\n', '!', '?', ':' }, Math.Max(0, x.Index - 1)) + 1);
            if (Hypothetical.IsMatch(reply[from..x.Index])) continue;
            var to = reply.IndexOfAny(new[] { '.', '\n' }, x.Index + x.Length);
            var sentence = reply[from..(to < 0 ? reply.Length : to)];
            var after = reply[(x.Index + x.Length)..(to < 0 ? reply.Length : to)];
            if (NoCause.IsMatch(sentence)) continue;
            if (WhyPhrase.IsMatch(x.Value) && (SelfSubject.IsMatch(after) || EmptyTurnOut.IsMatch(after))) continue;
            m = x;
            break;
        }
        if (m is null) return null;
        var start = Math.Max(0, reply.LastIndexOfAny(new[] { '.', '\n', '!', '?' }, Math.Max(0, m.Index - 1)) + 1);
        var end = reply.IndexOfAny(new[] { '.', '\n' }, m.Index + m.Length);
        return Fold(reply[start..(end < 0 ? reply.Length : end + 1)]);
    }

    private static readonly Regex ApiError = new(@"\A\s*(?:API Error|You['’]ve hit your|Credit balance is too low|Request too large|Prompt is too long)", RegexOptions.CultureInvariant);

    /// <summary>The reply is Claude Code's own error text (spend limit, API error): the session could not answer an ask
    /// (dry run 3 Nov: 3 asks fell on such turns), so the turn is never asked.</summary>
    public static bool IsApiError(string reply) => ApiError.IsMatch(reply);

    private static string Fold(string s)
    {
        var t = Regex.Replace(s, @"\s+", " ").Trim();
        return t.Length <= 400 ? t : t[..397] + "...";
    }

    private static string Clip(string s, int n) => s.Length <= n ? s : s[..(n - 3)] + "...";

    // ---- was something recorded in the turn (replay) -------------------------------------------------------------------
    private static readonly Regex RecordCommand = new(@"(?:^|[;&|(\n]|\s)(?:[^\s""';&|]*[\\/])?[""']?kor(?:\.exe)?[""']?\s+(?:record|issue)\b|kor_brain\.py[""']?\s+note\b", I);

    /// <summary>Replay only: the turn ran a `kor record` / `kor issue` (or the older kor_brain.py note) that did not fail.</summary>
    public static bool RecordedInTranscript(Turn t) =>
        t.Tools.Any(c => c.Name is "Bash" or "PowerShell" && c.Command is not null && !c.IsError
                         && RecordCommand.IsMatch(ToolClassifier.Neutralise(c.Command)));
}
