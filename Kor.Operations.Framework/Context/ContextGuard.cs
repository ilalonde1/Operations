using System.Globalization;
using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Context;

public enum ContextLevel { Ok, Warn, Stop }

/// <summary>What the guard decided for one hook call. <see cref="Deny"/>: refuse the tool (PreToolUse); <see cref="Say"/>:
/// text the model is given (PreToolUse/UserPromptSubmit additionalContext, or a Stop block's reason); null = silent.</summary>
public sealed record ContextDecision(ContextLevel Level, bool Deny, string? Say, string Why);

/// <summary>
/// G5, the context line (Framework board row 1474; Ian, 3 Nov 18:58: "We should never even get to the point of
/// compacting"; 4 Nov: "We MUST learn to be more efficient with token usage. That does NOT mean we restrict functionality").
/// A session's context is read off its own transcript and warned at <see cref="WarnTokens"/>; at <see cref="StopTokens"/>
/// every work tool is REFUSED at PreToolUse and it is told to write its handover and end, so a fresh session continues.
/// Every request re-sends the context, so a turn's cost grows with it (record #124807). Ian or the lead starts the fresh
/// session.
///
/// WHY THIS GUARD EXISTS: the three cliffs below happened while Ian's user settings (~/.claude/settings.json) held
/// "autoCompactEnabled": false. The last auto-compaction of the corpus was 31 Oct 18:49Z. With auto-compact off, nothing
/// stands between a long turn and the API's refusal: a synthetic assistant line, model "&lt;synthetic&gt;",
/// isApiErrorMessage true, error "invalid_request", text "Prompt is too long". Once it fires, every later prompt fails the
/// same way until someone runs /compact. At 21:26 on 3 Nov Ian turned auto-compact ON, with
/// modelSettings.claude-opus-5-5.autoCompactWindow 900000. The settings are his; G5 reads them and never writes them.
/// Even with auto-compact on, Ian wants never to reach compaction, so G5 forces the handover before it. The ceiling is
/// therefore whichever comes first: the model's window, or the auto-compact window the session's settings give that model
/// (<see cref="CompactSettings"/>: env, then project local, project and user files, in Claude Code's documented order).
/// With the 1M window, the 149 auto-compactions fired at 966.7k-976k, about 33k under the window (the docs say "about
/// 967K"). A 900k window would by the same margin fire near 867k. That is an ESTIMATE until the first 900k-window
/// auto-compaction is seen; its measured preTokens then replaces it.
///
/// THE CLIFF, MEASURED (3 Nov 2026, 1,074 transcripts, 610 main and 464 subagent, in the 13 KOR folders, modified since
/// 4 Oct; read by script, usage fields only):
/// <list type="bullet">
/// <item>9 "Prompt is too long" lines in 3 sessions, all claude-opus-5-5. Each followed a last good request of 971,276
/// (KOR.Drafter 006abbce, 3 Nov 18:56:46 local, mid-turn, port plan half-committed), 973,313 (Operations eb9075b2,
/// 1 Nov 13:37) and 977,326 (Framework d0313f99, 3 Nov 13:50; then 6 refusals over 45 min, until Ian ran /compact).</item>
/// <item>No opus-5-5 request above 977,326 ever succeeded. The failed requests were about 975k-981k, so the measured cliff
/// is <see cref="MeasuredCliff"/>, 97.5% of the 1M window. claude-opus-5 and claude-opus-4-8 ran at up to 999,612.</item>
/// <item>006abbce's last hour: 770k at 17:54, 807k at 18:13 (its last turn began; that ONE turn ran 43 minutes), 900k at
/// 18:41, 953k at 18:52, 971k at 18:56. That is about 200k tokens in an hour.</item>
/// </list>
///
/// THE READING: the last real assistant line's message.usage. input_tokens + cache_read_input_tokens +
/// cache_creation_input_tokens is what the request sent; output_tokens is added because the next request re-sends it.
/// A compact_boundary after that line resets the reading to its compactMetadata.postTokens. A "Prompt is too long" after
/// it means the session is OVER the cliff: STOP, whatever the number says. A main transcript's sidechain lines are
/// skipped. A subagent is read from its own transcript (agent_id, through StallTranscript.PathFor), because a subagent
/// has a context of its own.
///
/// THE WINDOW comes from the model id on that same line, never assumed (the lead, 3 Nov). It is 1,000,000 for the models
/// the docs list with a native 1M window ("Fable 5.1, Fable 5, Sonnet 5 and later, Haiku 5.5, and Opus 4.7 and later",
/// code.claude.com model-config) and for any id carrying "[1m]". Every other id, an unknown one included, takes 200,000:
/// a wrong guess warns early, never late. All 7 model ids in the corpus are 1M ids.
///
/// THE THRESHOLDS: WARN <see cref="WarnTokens"/> and STOP <see cref="StopTokens"/> on every ceiling large enough to hold
/// them; on a smaller one (a 200k model, --ceiling) <see cref="WarnFraction"/> and <see cref="StopFraction"/> of it, so no
/// ceiling is reached unwarned. The first version stopped at 85% of the ceiling, set from the cliff alone; Ian's line
/// replaced it (OverLORD's brief, 4 Nov). The cliff is why a session must stop at all, the bill is why at 300k. From 300k
/// the cliff is 675k away, so one call's result plus the handover cannot reach it: main-session request-to-request growth
/// in the 3 Nov corpus, n 64,545, p50 1,240, p99 12,227, p99.9 25,032, max 91,214; a handover costs about 3k-40k. What
/// these thresholds do to the 30-day corpus, by name, is Framework docs/G5-CONTEXT-STOPS.md.
///
/// The warning is repeated once per <see cref="WarnStepFraction"/> of the ceiling (25k on 1M) of growth. Live, the hook reads its
/// own last warning (<see cref="WarnMarker"/>) back off the transcript tail; the replay keeps it in memory.
///
/// WHERE IT IS SAID: PreToolUse hookSpecificOutput.additionalContext (WARN) and permissionDecision deny with its reason
/// (STOP), both of which the model reads; UserPromptSubmit additionalContext (WARN and STOP, so a prompt to a stopped
/// session is answered with the handover); and a Stop block, once (only when stop_hook_active is false), when a main
/// session at STOP tries to end its turn before the handover is written. "Written" means a successful kor state AND a
/// successful Edit/Write of a *HANDOVER*.md, both after the reading crossed STOP.
///
/// ALLOWED AT STOP (<see cref="HandoverAllows"/>): Read/Edit/Write/MultiEdit of a *HANDOVER*.md or *RESUME*.md; Read/Write/Edit of a
/// scratchpad file named for the state (*STATE*, *HANDOVER*: the file kor state --file reads); Bash/PowerShell when EVERY
/// segment is kor state, kor record, git status/diff/log/show/rev-parse (no --output), ls, git add of *HANDOVER*.md paths only, git commit
/// of *HANDOVER*.md paths named, with only a message, date, Get-Date, cd, pwd, echo, a literal assignment, or a heredoc cat (a commit message),
/// with no redirect to a file; TaskStop/KillShell (stop background work); SendMessage for a SUBAGENT only (its report to
/// its lead - a main session tells Ian in plain text); ToolSearch only to load those. Everything else is denied, git push
/// included (the fresh session reviews and pushes). The first dry run allowed any scratchpad file and any SendMessage, and
/// 408 SendMessage calls (leads dispatching work), 225 scratchpad Writes, 145 scratchpad Reads and 53 scratchpad Edits
/// of the 870 allowed were work, not handover: hence these limits.
///
/// DOES NOT COVER: a session set on getting round it. This holds an honest session to its handover against momentum; it is not
/// a sandbox, and a shell has more spellings than this reads (the plain cases, each refused and tested, are in ContextTests).
/// A session that grows without calling a tool (a long text answer: no PreToolUse fires; the next
/// prompt's UserPromptSubmit still says STOP); one call whose result jumps past the line (a subagent once grew 191,340 in
/// one request: a call allowed at 299k lands near 490k before refusals start, far from the cliff but over the line); a
/// model the docs do not list, judged on 200k (warned at 140k, stopped at 170k even if its window is 1M); a transcript that
/// lags behind the session (the reading is the last line ON DISK: a result not yet written is not counted); history
/// beyond the tail read (<see cref="ContextTranscript.MaxScan"/>), so a STOP crossing older than that counts the whole
/// tail as after it; the week's spend (G5 reads one session's context; the week is G6, board row 1559); a live run or a
/// cleanup the stopped session cannot finish (refused like any work: its handover names it first and the fresh session
/// does it, the decision in docs/G5-CONTEXT-STOPS.md); a handover that is written but says nothing useful (G5 checks that it was written, not what it says); an auto-compact
/// window set by managed settings or by a --settings/--autocompact flag at launch (not read: see
/// <see cref="CompactSettings"/>).
/// Same-class fault it would miss: a session at 240k that answers one prompt with one large Read and then a text-only reply:
/// nothing warned before the Read, a text reply fires no PreToolUse, so only its next prompt hears of it.
/// </summary>
public static class ContextGuard
{
    public const long OneMillion = 1_000_000;
    public const long TwoHundredK = 200_000;
    /// <summary>Ian's lines (4 Nov, OverLORD's brief): warned at 250k of context, at 300k the handover and the end.</summary>
    public const long WarnTokens = 250_000;
    public const long StopTokens = 300_000;
    /// <summary>On a ceiling too small to hold those lines (a 200k model, --ceiling): warn and stop at these fractions of it.</summary>
    public const double WarnFraction = 0.70;
    public const double StopFraction = 0.85;
    public const double WarnStepFraction = 0.025;

    /// <summary>The smallest failed opus-5-5 request (971,276 sent + 3,992 out, 006abbce 3 Nov 18:56:46).</summary>
    public const long MeasuredCliff = 975_268;

    /// <summary>How far under the auto-compact window compaction fired with the 1M window: 1,000,000 - 966,753, the lowest of
    /// the corpus's 149 auto preTokens (an estimate for any other window until one is seen).</summary>
    public const long TriggerMargin = 33_247;

    /// <summary>What G5 reads of a call: the file path for the file tools (Read included), else StallTranscript's text.</summary>
    public static string TextOf(string tool, System.Text.Json.JsonElement input)
    {
        if (tool is "Read" or "Edit" or "Write" or "MultiEdit" && input.ValueKind == System.Text.Json.JsonValueKind.Object
            && input.TryGetProperty("file_path", out var fp) && fp.ValueKind == System.Text.Json.JsonValueKind.String)
            return fp.GetString() ?? "";
        return Stall.StallTranscript.TextOf(tool, input);
    }

    public const string WarnMarker = "[kor context] WARN at ";
    public const string StopMarker = "[kor context] STOP at ";

    private static readonly Regex OneMillionModel = new(
        @"^claude-(?:fable-5|sonnet-5|haiku-5-5|opus-4-[7-9]|opus-5)(?:[-.\[@]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The model's context window: 1M for the documented 1M ids and any "[1m]" id, else 200k (unknown = smaller).</summary>
    public static long WindowOf(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return TwoHundredK;
        var m = model.Trim();
        if (m.Contains("[1m]", StringComparison.OrdinalIgnoreCase)) return OneMillion;
        return OneMillionModel.IsMatch(m) ? OneMillion : TwoHundredK;
    }

    public static long WarnAt(long ceiling) => Math.Min(WarnTokens, (long)(ceiling * WarnFraction));
    public static long StopAt(long ceiling) => Math.Min(StopTokens, (long)(ceiling * StopFraction));

    /// <summary>The ceiling the thresholds must stay under: the model's window, or the auto-compact window when that is
    /// smaller and auto-compact is on (Ian, 3 Nov 21:26: Opus 5.5 at 900k).</summary>
    public static long CeilingOf(string? model, CompactSettings cs) => Math.Min(Math.Min(WindowOf(model), cs.WindowFor(model) ?? long.MaxValue), cs.Cap ?? long.MaxValue);

    public static ContextLevel LevelOf(ContextReading r, CompactSettings cs)
    {
        if (r.OverCliff) return ContextLevel.Stop;
        var w = CeilingOf(r.Model, cs);
        return r.Tokens >= StopAt(w) ? ContextLevel.Stop : r.Tokens >= WarnAt(w) ? ContextLevel.Warn : ContextLevel.Ok;
    }

    /// <summary>Warn again only after a step of growth since the last warning (or when there was none, or the reading fell).</summary>
    public static bool ShouldWarn(long tokens, long window, long? lastWarned) =>
        lastWarned is not { } last || tokens < last || tokens - last >= (long)(window * WarnStepFraction);

    public static string N(long n) => n.ToString("N0", CultureInfo.InvariantCulture);

    private static string Of(string? model, long w, CompactSettings cs) =>
        cs.Cap == w ? $"{model ?? "an unknown model"}'s ceiling {N(w)} (set by --ceiling)"
        : w < WindowOf(model) ? $"{model ?? "an unknown model"}'s auto-compact window {N(w)}" : $"{model ?? "an unknown model"}'s {N(w)}";

    public static string WarnText(ContextReading r, CompactSettings cs)
    {
        var w = CeilingOf(r.Model, cs);
        return $"{WarnMarker}{N(r.Tokens)} tokens of context ({Of(r.Model, w, cs)}). " +
               $"At {N(StopAt(w))} every work tool is refused and only the handover can be written, so a fresh session continues " +
               "(G5, board row 1474; every request re-sends this context, so each turn now costs that much). " +
               "Finish the piece in hand, hand big reads to helpers, and plan the handover now.";
    }

    /// <summary>The STOP text: what to write, in order, and who restarts the session.</summary>
    public static string StopText(ContextReading r, bool subagent, string? module, bool handoverWritten, CompactSettings cs)
    {
        var w = CeilingOf(r.Model, cs);
        var where = r.OverCliff ? $"PAST THE CLIFF (\"Prompt is too long\" after {N(r.Tokens)})" : $"{N(r.Tokens)} tokens of context ({Of(r.Model, w, cs)}; the line is {N(StopAt(w))})";
        var head = $"{StopMarker}{where}. STOP: a fresh session continues from here, so work tools are " +
                   "refused (G5, board row 1474). ";
        if (subagent)
            return head + "Report to your lead now with SendMessage: what is done, what is not, and where each piece is (paths, commits, " +
                   "board rows). Then end your turn. The lead decides what is restarted.";
        var m = string.IsNullOrWhiteSpace(module) ? "<module>" : module;
        if (handoverWritten)
            return head + "The handover is written (kor state and the HANDOVER.md edit are on record). Commit it if not yet committed " +
                   "(git add docs/HANDOVER.md && git commit docs/HANDOVER.md -m ...; no push), then tell Ian in one plain line that this session has stopped for a fresh " +
                   "start, with its handover written. Ian or the lead starts the fresh session.";
        return head + "Write the handover now, and nothing else: " +
               "1. Read docs/HANDOVER.md (or the repo's *HANDOVER*.md) and Edit its STATE NOW block, with times from `date`: FIRST anything LIVE " +
               "(a run, a process, a copy left open, a freeze half-done) with the exact command that finishes it, then what is done, " +
               "what is half-done and exactly where, the next step, the open board rows. " +
               $"2. Write the same block to STATE-NOW.md in your scratchpad and run `kor state --module {m} --file <that file>`. " +
               "3. `kor record` any finding not yet recorded. " +
               "4. `git add docs/HANDOVER.md && git commit docs/HANDOVER.md -m \"handover: ...\"` (no push: the fresh session reviews and pushes). " +
               "5. Tell Ian in one plain line that this session has stopped for a fresh start, with its handover written, naming anything LIVE. Ian or the lead " +
               "starts the fresh session. " +
               "Allowed now: Read/Edit/Write of a *HANDOVER*.md; Read/Write of STATE-NOW.md in the scratchpad; kor state; kor record; date; " +
               "git status/diff/log/show; ls; git add of the handover; git commit of the handover by its path; TaskStop. A chain with any other step in it is " +
               "refused whole: if a command is refused, run the allowed steps on their own and finish all 5. Nothing else runs until a fresh session.";
    }

    /// <summary>The PreToolUse decision for one call.</summary>
    /// <param name="text">the call's text as StallTranscript.TextOf gives it (a command, or a file path).</param>
    public static ContextDecision Decide(ContextReading r, string tool, string text, string? scratchpad, bool subagent, string? module, CompactSettings cs)
    {
        var level = LevelOf(r, cs);
        switch (level)
        {
            case ContextLevel.Ok:
                return new ContextDecision(level, false, null, $"{N(r.Tokens)} is under WARN ({N(WarnAt(CeilingOf(r.Model, cs)))})");
            case ContextLevel.Warn:
                var w = CeilingOf(r.Model, cs);
                return ShouldWarn(r.Tokens, w, r.LastWarned)
                    ? new ContextDecision(level, false, WarnText(r, cs), "WARN")
                    : new ContextDecision(level, false, null, $"WARN already said at {N(r.LastWarned ?? 0)}");
        }
        var why = HandoverAllows(tool, text, scratchpad, subagent);
        return why is null
            ? new ContextDecision(level, true, StopText(r, subagent, module, r.HandoverWritten, cs), $"refused at STOP: {tool} is not a handover step")
            : new ContextDecision(level, false, null, $"allowed at STOP: {why}");
    }

    /// <summary>The Stop event: block once, for a main session at STOP whose handover is not yet written.</summary>
    public static ContextDecision DecideStop(ContextReading r, bool subagent, bool stopHookActive, string? module, CompactSettings cs)
    {
        var level = LevelOf(r, cs);
        if (level != ContextLevel.Stop) return new ContextDecision(level, false, null, "under STOP");
        if (subagent) return new ContextDecision(level, false, null, "a subagent's report is its handover");
        if (r.HandoverWritten) return new ContextDecision(level, false, null, "the handover is written");
        if (stopHookActive) return new ContextDecision(level, false, null, "already blocked once (stop_hook_active)");
        return new ContextDecision(level, true, StopText(r, false, module, false, cs), "STOP and no handover yet");
    }

    // ---- the handover allowlist ------------------------------------------------------------------------------------

    private static readonly string[] FileTools = { "Read", "Edit", "Write", "MultiEdit" };
    private static readonly string[] StopTools = { "TaskStop", "KillShell" };
    private static readonly Regex Selected = new(@"select:([A-Za-z0-9_,\s]+)", RegexOptions.CultureInvariant);

    /// <summary>Why this call is a handover step (allowed at STOP), or null when it is work (refused).</summary>
    public static string? HandoverAllows(string tool, string text, string? scratchpad, bool subagent = false)
    {
        if (StopTools.Contains(tool)) return $"{tool} (stopping background work)";
        if (tool == "SendMessage") return subagent ? "SendMessage (a helper's report to its lead)" : null;
        if (tool == "ToolSearch")
        {
            // only to load the tools allowed here (its input JSON: "select:TaskStop,SendMessage")
            var m = Selected.Match(text);
            if (!m.Success) return null;
            var names = m.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return names.Length > 0 && names.All(n => StopTools.Contains(n) || (subagent && n == "SendMessage")) ? "ToolSearch for a stop tool" : null;
        }
        if (FileTools.Contains(tool))
        {
            if (IsHandoverFile(text)) return $"{tool} of the handover {Path.GetFileName(text)}";
            if (UnderScratchpad(text, scratchpad) && IsStateFile(text)) return $"{tool} of the state file {Path.GetFileName(text)}";
            return null;
        }
        if (tool is "Bash" or "PowerShell")
        {
            var shell = tool == "PowerShell" ? Shell.PowerShell : Shell.Bash;
            // a heredoc body is data (a commit message), never a step, wherever it sits ("$(cat <<'EOF' ... EOF)")
            // only the << token, the body and the terminator are blanked: the rest of the opener's line ("cat <<'EOF' > f",
            // "git commit -F - <<'EOF' && git push", review row 489) and what follows the terminator are still split and judged
            text = HeredocBody.Replace(text, m =>
            {
                var open = m.Groups["open"].Length;
                var rest = m.Groups["rest"].Value;
                return new string(' ', open) + rest + new string(' ', m.Length - open - rest.Length);
            });
            // 2>&1 and >/dev/null are no step of their own: blank them before splitting ('&' is a separator)
            var segs = CommandSegments.Split(HarmlessRedirect.Replace(text, m => new string(' ', m.Length)), shell);
            if (segs.Count == 0) return null;
            foreach (var s in segs)
                if (SegmentWrites(s) || !SegmentAllowed(s.Trim())) return null;
            return "every step is a handover step";
        }
        return null;
    }

    public static bool IsHandoverFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var bare = path.Trim().Trim('"', '\'');
        if (bare.StartsWith(':')) return false;   // git pathspec magic: ":!docs/HANDOVER.md" is everything BUT the handover
        var name = Path.GetFileName(bare.Replace('\\', '/'));
        // a RESUME note is a handover too (HOW-WE-BUILD: "wisdom lives outside sessions ... RESUME notes"; judge C, STOP 71:
        // a stand-down note to RESUME.md was refused in the first pass)
        return name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
               && (name.Contains("HANDOVER", StringComparison.OrdinalIgnoreCase) || name.Contains("RESUME", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A scratchpad file named for the resume block (STATE-NOW.md, state.txt, handover.md ...).</summary>
    public static bool IsStateFile(string path)
    {
        var name = Path.GetFileName(path.Trim().Trim('"', '\'').Replace('\\', '/'));
        return name.Contains("STATE", StringComparison.OrdinalIgnoreCase) || name.Contains("HANDOVER", StringComparison.OrdinalIgnoreCase);
    }

    public static bool UnderScratchpad(string path, string? scratchpad)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string Norm(string p) => p.Trim().Trim('"', '\'').Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        var p = Norm(path);
        if (p.Contains("/../") || p.EndsWith("/..")) return false;
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(scratchpad)) roots.Add(Norm(scratchpad));
        // Claude Code's own temp tree: the scratchpad lives under it when the hook input does not name one
        roots.Add(Norm(Path.Combine(Path.GetTempPath(), "claude")));
        return roots.Any(r => r.Length > 0 && p.StartsWith(r + "/", StringComparison.Ordinal));
    }

    // a redirect into a file (> f, >> f, Out-File, Set-Content, tee) is a write; 2>&1, >/dev/null and >$null are not
    private static readonly Regex HarmlessRedirect = new(@"(?:\d?>&\d|\d?>\s*(?:/dev/null|\$null|NUL))(?=\s|$|[;&|)])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WriteCmdlet = new(@"\b(?:Out-File|Set-Content|Add-Content|tee|Tee-Object)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // a quoted string is data: "...<noreply@anthropic.com>" in a commit message is no redirect (the live run, 3 Nov 21:50:
    // every handover commit carrying the Co-Authored-By trailer was refused while the whole command was checked)
    private static readonly Regex Quoted = new(@"""(?:[^""\\`]|[\\`].)*""|'[^']*'", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    /// <summary>Does ONE segment write a file: a redirect or a write cmdlet outside its quotes (a $( ) inside quotes is split
    /// off as a segment of its own, so a redirect there is still seen).</summary>
    private static bool SegmentWrites(string seg)
    {
        var bare = Quoted.Replace(HarmlessRedirect.Replace(seg, " "), " ");
        return bare.Contains('>') || WriteCmdlet.IsMatch(bare);
    }

    private static readonly Regex KorExe = new(@"^[""']?(?:[^\s""']*[\\/])?kor(?:\.exe)?[""']?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex HeredocBody = new(@"(?<open><<-?[ \t]*(['""]?)(?<tag>[A-Za-z_][A-Za-z0-9_]*)\1)(?<rest>[^\n]*)\n(?<body>.*?)\n[ \t]*\k<tag>(?=\s|$|\))",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex BashAssign = new(@"^[A-Za-z_][A-Za-z0-9_]*=", RegexOptions.CultureInvariant);
    private static readonly Regex PsVar = new(@"^\$[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly Regex QuotedTail = new(@"=\s*(?:""[^""$`]*""|'[^']*')\s*$", RegexOptions.CultureInvariant);
    private static readonly string[] GitReads = { "status", "diff", "log", "show", "rev-parse" };
    private static readonly string[] Harmless = { "date", "get-date", "cd", "set-location", "pwd", "ls", "echo", "write-output", "write-host", "true", ":" };

    // an allow-list of plain path characters, a drive prefix the one colon: a shell or git can turn anything else into
    // something other than this text after it is judged ($, backtick, braces, globs, pathspec magic; review rows 495, 496).
    // A backslash stays, for Windows paths; an escaped quote is refused for the whole git step (EscapedQuote)
    private static readonly Regex EscapedQuote = new(@"[\\`][""']", RegexOptions.CultureInvariant);
    private static readonly Regex PlainPath = new(@"^(?:[A-Za-z]:[\\/]|[A-Za-z0-9_./ -])[A-Za-z0-9_./\\ -]*$", RegexOptions.CultureInvariant);

    /// <summary>A handover file named as git will read it: no pathspec magic, however it is spelled (review row 495).</summary>
    private static bool IsPlainHandoverPath(string path) => IsHandoverFile(path) && PlainPath.IsMatch(path.Trim().Trim('"', '\''));

    /// <summary>A commit at STOP: only a message (-m, -F, --message, --file, with -q) and handover paths. An allow-list, because
    /// git takes abbreviations (--amen, --al) and -i, and a path other than the handover would commit work (review row 491).</summary>
    private static bool CommitIsHandoverOnly(IReadOnlyList<string> args)
    {
        var paths = 0;   // a commit with no path takes the whole index: work staged before STOP, or another lead's
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a is "-m" or "-F" or "--message" or "--file") { i++; continue; }
            if (a is "-q" or "--quiet" or "--") continue;
            if (a.StartsWith("--message=", StringComparison.Ordinal) || a.StartsWith("--file=", StringComparison.Ordinal)) continue;
            if (a.StartsWith("-m", StringComparison.Ordinal) && !a.StartsWith("--", StringComparison.Ordinal)) continue;   // -m"text"
            if (a.StartsWith('-') || !IsPlainHandoverPath(a)) return false;
            paths++;
        }
        return paths > 0;
    }

    private static bool SegmentAllowed(string seg)
    {
        var words = Enroll.EnrollCheck.SplitArgs(seg).Where(w => w.Trim().Length > 0).ToList();
        // a segment with no word (the closing quote of "$(cat <<'EOF' ... )") runs nothing
        if (words.Count == 0) return true;
        // a plain assignment of a literal (S="C:/.../scratchpad"; $f = 'x'): a subshell inside it is its own segment
        if (words.Count == 1 && BashAssign.IsMatch(words[0])) return true;
        if (words.Count == 3 && PsVar.IsMatch(words[0]) && words[1] == "=" && QuotedTail.IsMatch(seg)) return true;
        if (words[0] == "&" && words.Count > 1) words = words.Skip(1).ToList();   // PowerShell's call operator
        var prog = words[0];
        var name = Path.GetFileName(prog.Replace('\\', '/')).ToLowerInvariant();
        if (name.EndsWith(".exe", StringComparison.Ordinal)) name = name[..^4];
        if (Harmless.Contains(name)) return true;
        // a heredoc body feeding a commit message: cat <<'EOF' (its body lines are blank in the neutral text)
        if (name == "cat" && words.Count == 1) return true;   // what is left of `cat <<'EOF' ... EOF` once the heredoc is blanked
        if (KorExe.IsMatch(prog)) return words.Count > 1 && words[1] is "state" or "record";
        if (name == "git")
        {
            var i = 1;
            while (i < words.Count && words[i].StartsWith('-'))
                i += words[i] is "-C" or "-c" ? 2 : 1;   // git -C dir / -c k=v
            if (i >= words.Count) return false;
            var verb = words[i];
            var args = words.Skip(i + 1).ToList();
            // SplitArgs reads an escaped quote as opening a quote and the shell does not, so the words judged here would not be
            // the words git gets: Bash escapes with a backslash, PowerShell with a backtick (review rows 497, 498)
            if (verb is "add" or "commit" && EscapedQuote.IsMatch(seg)) return false;
            // --output writes a file, and git takes any abbreviation of it (--oneline is --on, so --ou cannot be it)
            if (GitReads.Contains(verb)) return !args.Any(a => a.StartsWith("--ou", StringComparison.OrdinalIgnoreCase));
            if (verb == "add")
            {
                var paths = args.Where(a => !a.StartsWith('-')).ToList();
                return paths.Count > 0 && paths.All(IsPlainHandoverPath) && !args.Any(a => a is "-A" or "--all" or "-u" or "--update" or ".");
            }
            if (verb == "commit") return CommitIsHandoverOnly(args);
            return false;
        }
        return false;
    }
}
