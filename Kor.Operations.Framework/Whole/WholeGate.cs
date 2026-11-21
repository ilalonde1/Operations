using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kor.Operations.Framework.Whole;

/// <summary>
/// Board row 2139, the whole-source ledger gate. Ian 16 Nov (#133885, #133888): "you do cursory sample looks" / "Look at the whole
/// thing. Not a sample." Ian 20 Nov 18:4x (#135772): "make sure when I ask you to ingest or look or evaluate something you look at the
/// WHOLE THING. ALL of the avaialble data - rather than your programmed cursory look?" - "OK - so let's get it addressed".
///
/// THE LEDGER is read from the session's own transcript (<see cref="WholeTranscript"/>), off what checks PRINTED, not off the wording
/// of a reply (the W1-W9 plan's rule):
///   FLAG - in the result of a call that RAN something (a Bash/PowerShell command that is not only a reader, see <see cref="IsReader"/>;
///          an Agent/Task helper's report), a non-pass result: "label X of Y" / "X of Y label" / "X/Y label" with X &lt; Y and a pass
///          word in its cell (pass, ok, match, equal, N-sided ... -> Y-X flagged) or a fail word (-> X flagged); or "N failed" /
///          "FAILED: N" / "2 WRONG" with N &gt;= 1 (<see cref="Readings"/>).
///   HOLE - "OUT OF SCOPE", "NOT CHECKED", "not graded", "not looked at", "not opened", "UNPROVEN" in such a result, or in this
///          session's own Write/Edit of a brief, prompt or plan file (a look scoped to part of its source).
///   DISCHARGED - OPENED: a later line "OPENED n of N" (n = N flagged) holding the flag's key, after an UNCUT look (Bash/PowerShell/
///          Read whose output was not cut) of at least N non-empty lines; or SUPERSEDED: a later run printing the same label with no
///          deficit (X of X, or 0). A hole is OPENED by a line "OPENED" naming one of its words after any uncut look.
///   ACCOUNTED - discharged, or a line "NOT OPENED: &lt;key&gt;" (a flag) / "NOT CHECKED: &lt;one of its words&gt;" (a hole).
///
/// THE DOORS - where a conclusion leaves the session (<see cref="DoorOf"/>): Stop (the turn's assistant text; blocks once - the
/// stop_hook_active retry is allowed, so it never loops); SendMessage; a Write of a .md/.txt/.csv; a Bash/PowerShell board write
/// (oversight.Commitment, inline or in the oq.py -i file), kor record/state, kor_task return/accept, git commit; the EXPENSIVE STEP
/// (a command naming \\KOR-210, run-on-210, kor-runner or --deliver, outside a message flag). The narration written just before a
/// PreToolUse door is part of its text.
///
/// THE RULES (<see cref="WholeLedger.Decide"/>), every one naming the flag, its transcript line and the exact line that lifts it:
///   W1 ACCOUNT  - at Stop, SendMessage, a board write and kor record/state: every flag of this turn and every hole of the session is
///                 accounted for. Hiding a flag is refused; saying it plainly (NOT OPENED) is not. Case 1 L6490, case 3 L2763.
///   W2 DISMISS  - a door sentence holding artifact/artefact, noise, expected, spurious, false positive, harmless, cosmetic, benign,
///                 rounding, measuring error, not real, ignorable, flaky or transient AND a flag's number or label word, while that
///                 flag (from the last <see cref="Window"/> calls) is not discharged. NOT OPENED does not lift it. Case 1 L6469.
///   W3 STEP     - the expensive step or a board close (State done/dropped, kor_task accept) while a flag or hole from the last
///                 <see cref="Window"/> calls is not discharged. Case 1 L6516.
///   W4 SAMPLED  - a door SENTENCE claiming the whole of Y ("all Y", "none of the Y", "every one of the Y", "each of the Y", "Y of Y",
///                 Y &gt;= 5) AND stating a range over it ("3.1-3.4 in", "consistently", "between a and b", "under 4") - rule 12's
///                 "a range/distribution from it"; Y must appear in that cut output as a BARE COUNT (a line "37", "37 rows", "count:
///                 37"), never as the check's own "Y of Y" - when the
///                 latest output holding Y was CUT (claude_bash_sample_guard.py's cut list, WITHOUT its COUNTS escape - case 2's
///                 `| head -5; grep -c` printed 37 and five values; a Grep result paginated; a partial Read) and no later uncut call
///                 names the same source file. Case 2 L10567; its correction L10873 (sort | uniq -c over the same file) passes.
///
/// WHAT IT DOES NOT COVER, stated (CLAUDE.md rule 11): a population claim with no number ("all the pads are four-sided"); a flag
/// printed in a form the patterns miss ("4 pads irregular"); a look of N lines that are not the flagged items (it proves a big
/// enough uncut look ran after the flag, not that it showed THEM); a check's result only ever read by a reader (cat of a report
/// file holds no flag for it); a flag of an earlier turn under W1 (W2 and W3 still see the last <see cref="Window"/> calls); work
/// in another session. Errs toward refusing: a run that PRINTS quoted history (a transcript or record search that is not a listed
/// reader) reads as a check, and its quoted "4 of 180 ... not" is a flag. SAME-CLASS FAULT IT WOULD MISS: case 1 again if the first
/// check had printed "4 pads irregular" - no number with a polarity word, no flag, nothing to account for.
/// For a whole FOLDER or package, the file ledger is Framework tools/blind_look_check.py --package (one row per file, X of Y).
///
/// MEASURED (20 Nov 2026, kor whole --replay over the 173 main transcripts written 16-20 Nov in Framework, KOR.Drafter and
/// Operations; a session is taken to comply after its first refusal): 2,452 door moments, 229 refused - W1 141, W2 14, W3 86, W4 0.
/// The 563 distinct flags of dry run 2 were read whole; the misfire classes found were fixed (X/Y ids, far polarity words, grep -n
/// and diff lines, "not checked: 0", quoted mentions, ids and a ratio's Y as counts, how-to-read brief text), and what is left is
/// predominantly real non-pass results the sessions never accounted for (NOT ACCEPTED 6 of 14, FAIL n, NOT GRADED, NOT RUN/FETCHED,
/// LOOK 235 of 239, "not looked at"). The 50 distinct W4 cases of dry run 4 were read whole: 0 were the sampled-range fault, so W4
/// now needs both ends of its numeric range among the values the cut output showed (it still refuses case 2). Known residual
/// misfires: PAGE FILL page counts; "(c) expected FAIL states printed PASS: 0 of 215" (an inverted sense); a brief's own text
/// "or NOT CHECKED with why"; a gate's proof "50 of 50 DELIVER REFUSED".
/// HOLES: one this session wrote into its own brief holds for the session; one a check printed is judged over the last Window
/// calls, as flags are (a hole printed hours earlier blocked every message in the case-3 proof).
/// Fails OPEN on any fault (the hook logs it). KOR_WHOLE_GATE=off lifts it (reviewer and judge sessions).
/// </summary>
public static class WholeGate
{
    public const string GuardName = "kor-whole";
    public const int Window = 200;
    public const int MostNamed = 30;   // a session must see every key it is asked to account for (8 hid 5 of 13 in the case-3 proof)

    private const RegexOptions Ci = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public static bool Off => string.Equals(Environment.GetEnvironmentVariable("KOR_WHOLE_GATE"), "off", StringComparison.OrdinalIgnoreCase);

    // ---- what a check printed ----

    private static readonly string[] PassWords =
    {
        "pass", "passed", "passes", "passing", "ok", "match", "matched", "matches", "matching", "equal", "valid", "clean", "resolved",
        "checked", "graded", "opened", "looked", "verified", "confirmed", "covered", "correct", "agree", "agrees", "identical", "green",
        "four-sided", "closed", "complete",
    };
    private static readonly string[] FailWords =
    {
        "fail", "failed", "fails", "failing", "failure", "failures", "differ", "differs", "different", "mismatch", "mismatches",
        "mismatched", "missing", "absent", "not", "non", "unresolved", "invalid", "flagged", "wrong", "misleading", "unproven",
        "unchecked", "ungraded", "unverified", "refused", "rejected", "broken", "bad", "errors", "error", "skipped",
        "disagreements", "disagree", "disagrees",
    };
    internal static readonly HashSet<string> Pass = new(PassWords, StringComparer.OrdinalIgnoreCase);
    internal static readonly HashSet<string> Fail = new(FailWords, StringComparer.OrdinalIgnoreCase);
    private static readonly Regex Sided = new(@"^\d+-sided$", Ci);

    private const string Num = @"\d{1,3}(?:,\d{3})+|\d+";
    // "X of Y" only: an "X/Y" form read ids and paths as results (review 2527/2528, items 1/6/7) in the first dry run (20 Nov 19:1x)
    private static readonly Regex Ratio = new($@"(?<![\d.,/#])(?<x>{Num})\s+of\s+(?<y>{Num})(?![.\d/])", Ci);
    /// <summary>A line that QUOTES history rather than reporting a check: a record line (recall, a refusal naming records), a grep -n hit.</summary>
    // (dry run 2, 563 distinct flags read 20 Nov 19:2x: grep -n / grep -rn hits '93:2. Walls', 'letter.txt:585:', diff and word-diff
    // lines '< ...', '{+...' quote files, not results)
    private static readonly Regex QuoteLine = new(@"\brecord #\d+|\[(?:finding|lesson|decision|dead-end|reference|feedback|project)[,\]]|\bboard row \d+ \[|^\s*\d+:(?!\d\d\b)|^\S+\.\w{1,5}:\d+[:-]|^[<>] |^\{[+-]", Ci);
    /// <summary>A number used as an ordinal, an id or a name ("pass 3 rejected", "row 12 failed", "GuardRefusal 9119 refused") is not a
    /// count; nor is the Y of a ratio read again ("45 of 2714 failing" is 45 flagged, not 2714).</summary>
    private static readonly Regex Ordinal = new(@"(?:\b(?:pass|round|step|run|row|rows|item|line|page|version|review|reviews|phase|part|stage|attempt|try|fix|v|id|ids|refusal|refusals|guardrefusal|record|records|commit|of)|#)\s*$", Ci);
    /// <summary>"not checked: 0", "NOT CHECKED 0" - nothing left unchecked, so no hole.</summary>
    private static readonly Regex ZeroHole = new(@"^[\s:=|]*0\b", Ci);
    private const int Near = 30;
    private static readonly string FailAlt = string.Join("|", FailWords.Where(w => w is not ("not" or "non" or "bad" or "error")).OrderByDescending(w => w.Length));
    private static readonly Regex CountForm = new($@"(?<![\w.-])(?<n>{Num})\s+(?<w>{FailAlt})\b|\b(?<w>{FailAlt})\s*[:=]\s*(?<n>{Num})(?![\w.])", Ci);
    private static readonly Regex HolePhrase = new(@"\b(?:OUT OF SCOPE|NOT CHECKED|not graded|not looked at|not opened|UNPROVEN|not in scope)\b", Ci);
    // a brief's exclusion of part of the SOURCE; not how-to-read instructions ("search, do not read whole", "FORBIDDEN: never read")
    private static readonly Regex BriefHole = new(@"\b(?:out of scope|not in scope|excluded from (?:the )?(?:look|check|review|scope|audit))\b", Ci);
    private static readonly Regex BriefFile = new(@"brief|prompt|plan", Ci);
    private static readonly Regex Words = new(@"[A-Za-z][A-Za-z0-9'_-]*|\d+-sided", Ci);

    /// <summary>One result a check printed: Flagged items not passing (0 = a clean reading of that label), out of Of (0 = a count).</summary>
    public sealed record Reading(string Key, int Flagged, int Of, string Label, bool Hole);

    private static int N(string s) => int.TryParse(s.Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : -1;

    /// <summary>Every reading in a check's output, line by line; a line's cells are split at | ; and tabs.</summary>
    public static List<Reading> Readings(string output)
    {
        var list = new List<Reading>();
        foreach (var rawLine in output.Split('\n'))
        {
            // a long line (a JSON row, a CSV line) is read to 20,000 characters: at 2,000 a result past that point was never seen
            var line = rawLine.Length > 20_000 ? rawLine[..20_000] : rawLine;
            if (QuoteLine.IsMatch(line)) continue;
            var ratioFlagged = new HashSet<int>();
            foreach (var cell in line.Split('|', ';', '\t'))
            {
                foreach (Match m in Ratio.Matches(cell))
                {
                    int x = N(m.Groups["x"].Value), y = N(m.Groups["y"].Value);
                    if (x < 0 || y <= 0 || x > y || y > 10_000_000) continue;
                    // the polarity word NEAREST the ratio, within Near characters either side (first dry run: a long line's far
                    // words made 'cut at 300,000 of 3,366,086 characters ... I saw all' a flag)
                    int lo = Math.Max(0, m.Index - Near), hi = Math.Min(cell.Length, m.Index + m.Length + Near);
                    while (lo > 0 && !char.IsWhiteSpace(cell[lo - 1]) && m.Index - lo < Near + 20) lo--;            // whole words in the key
                    while (hi < cell.Length && !char.IsWhiteSpace(cell[hi]) && hi - m.Index - m.Length < Near + 20) hi++;
                    bool? fail = null;
                    var best = int.MaxValue;
                    foreach (Match w in Words.Matches(cell[lo..hi]))
                    {
                        var at = lo + w.Index;
                        var dist = at < m.Index ? m.Index - (at + w.Length) : at - (m.Index + m.Length);
                        if (dist < 0 || dist >= best) continue;
                        if (Fail.Contains(w.Value)) { fail = true; best = dist; }
                        else if (Pass.Contains(w.Value) || Sided.IsMatch(w.Value)) { fail = false; best = dist; }
                    }
                    if (fail is null) continue;
                    var flagged = fail.Value ? x : y - x;
                    var label = Squash(Regex.Replace(cell[lo..hi], @"\d[\d,]*\s+of\s+\d[\d,]*", " ")).ToLowerInvariant();
                    // the KEY is the window around the ratio, never the whole cell: a refusal shows it whole and a session copies it
                    // into its NOT OPENED / OPENED line (review 2583: a 2,000-character cell clipped to 140 could not be accounted for)
                    list.Add(new Reading(Squash(cell[lo..hi].Trim()), flagged, y, Clip(label, 60), false));
                    if (flagged > 0) ratioFlagged.Add(flagged);
                }
            }
            foreach (Match m in CountForm.Matches(line))
            {
                var n = N(m.Groups["n"].Value);
                if (n < 0 || ratioFlagged.Contains(n)) continue;
                if (m.Groups["n"].Index < m.Groups["w"].Index && Ordinal.IsMatch(line[..m.Index])) continue;
                var w = m.Groups["w"].Value.ToLowerInvariant();
                list.Add(new Reading(Squash(m.Value.Trim()), n, 0, w, false));
            }
            foreach (Match h in HolePhrase.Matches(line))
            {
                if (ZeroHole.IsMatch(line[(h.Index + h.Length)..])) continue;
                // a quoted mention ("not checked" in a brief's rules or a reply's instructions) is not a hole
                if (h.Index > 0 && line[h.Index - 1] is '"' or '\'' or '`') continue;
                // a hole's key: the phrase and what follows it (accounting needs only one of its words, see WholeLedger.HoleWords)
                // the words BEFORE the phrase name what was not checked ("the .rvt (not opened)") - the proof on 4f85ab91 had
                // keys "not opened)" and "Not checked" that no line could name, so the key spans 60 characters either side
                int a = Math.Max(0, h.Index - 60), z = Math.Min(line.Length, h.Index + h.Length + 60);
                while (a > 0 && !char.IsWhiteSpace(line[a - 1]) && h.Index - a < 80) a--;
                list.Add(new Reading(Squash(line[a..z].Trim()), 1, 0, h.Value.ToLowerInvariant(), true));
                break;
            }
        }
        return list;
    }

    /// <summary>Holes a session WROTE into its own brief, prompt or plan (a look scoped to part of its source; case 3 L2099).</summary>
    public static List<Reading> BriefHoles(WholeEv e)
    {
        var list = new List<Reading>();
        if (e.Kind != WholeKind.Call || e.Tool is not ("Write" or "Edit" or "MultiEdit") || e.FilePath is null) return list;
        if (!BriefFile.IsMatch(Path.GetFileName(e.FilePath))) return list;
        foreach (var line in e.Input.Split('\n'))
            if (BriefHole.Match(line) is { Success: true } h)
                list.Add(new Reading(Squash(Clip(line.Trim(), 160)), 1, 0, h.Value.ToLowerInvariant(), true));
        return list;
    }

    // ---- what a command is ----

    private static readonly HashSet<string> ReaderWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "cat", "type", "head", "tail", "grep", "egrep", "fgrep", "rg", "findstr", "select-string", "sls", "sed", "ls", "dir", "get-childitem",
        "gci", "get-content", "gc", "git", "echo", "printf", "write-host", "write-output", "wc", "find", "sort", "uniq", "cut", "tr", "stat",
        "date", "pwd", "which", "where", "where-object", "select-object", "foreach-object", "format-table", "format-list", "measure-object",
        "cd", "set-location", "mkdir", "new-item", "cp", "copy-item", "mv", "rm", "touch", "tee", "jq", "file", "du", "df", "basename",
        "dirname", "realpath", "get-item", "test-path", "resolve-path", "true", "false", "sleep", "awk", "xargs", "column", "nl", "less", "more",
        "export", "set", "unset", "local", "for", "do", "done", "if", "then", "fi", "else", "while",
    };
    private static readonly Regex RecordReader = new(@"\bkor(?:\.exe)?""?\s+(?:recall|memory|start|read|events|checks|start-docs)\b|kor_brain\.py\s+(?:show|render)|guard_log\.py|board_render\.py|brain\.(?:Memory|Event|LaneState)|oversight\.(?:Commitment|GuardRefusal|vw_Board|Review)", Ci);
    private static readonly Regex Segment = new(@"&&|\|\||\||;|\n", Ci);

    /// <summary>A command whose every stage is a reader (cat, grep, git, ls, sed, ...), or that reads records, the board or a transcript
    /// search: its output quotes what was already there - no check ran.</summary>
    public static bool IsReader(string command)
    {
        if (RecordReader.IsMatch(command)) return true;
        foreach (var seg in Segment.Split(Blank(command)))
        {
            var s = seg.Trim().TrimStart('(', '{', '!', ' ').Trim();
            if (s.Length == 0) continue;
            while (Regex.IsMatch(s, @"^[A-Za-z_][A-Za-z0-9_]*=\S*\s+")) s = Regex.Replace(s, @"^[A-Za-z_][A-Za-z0-9_]*=\S*\s+", "");
            var first = s.Split(' ', '\t')[0].Trim('"', '\'', '&', ' ');
            if (Regex.IsMatch(first, @"^[A-Za-z_][A-Za-z0-9_]*=")) continue;   // an assignment alone
            if (first.StartsWith('$') && s.Contains('=')) continue;             // $x = ... (PowerShell)
            var name = Path.GetFileName(first.Replace('\\', '/'));
            if (!ReaderWords.Contains(name)) return false;
        }
        return true;
    }

    public static bool IsRun(WholeEv e) =>
        e.Kind == WholeKind.Call && (e.Tool is "Agent" or "Task" || (e.Tool is "Bash" or "PowerShell" && !IsReader(e.Input)));

    // claude_bash_sample_guard.py's cut list (repo rule 7: raw patterns), ported; tail -n +N keeps every line after N, so it is no cut
    private static readonly (Regex Re, string How)[] Cuts =
    {
        (new Regex(@"\|\s*head\b", Ci), "head"),
        (new Regex(@"\|\s*tail\b(?!\s+-n\s*\+)(?!\s+\+)", Ci), "tail"),
        (new Regex(@"(?:^|[;&|]\s*)head\s+-", Ci), "head"),
        (new Regex(@"(?:^|[;&|]\s*)tail\s+-(?!n\s*\+)", Ci), "tail"),
        (new Regex(@"grep\b[^|;]*\s-\w*m\s*\d+", Ci), "grep -m"),
        (new Regex(@"sed\s+-n\s+['""]?\d+\s*,\s*\d+p", Ci), "sed -n range"),
        (new Regex(@"Select-Object\s+-First\b", Ci), "Select-Object -First"),
        (new Regex(@"\bcut\s+-c", Ci), "cut -c"),
    };
    private static readonly Regex HeadLimit = new(@"\G\|\s*(?:head|tail)(?:\s+-n)?\s*(?:-(\d+)|(\d+))?(?=\s*(?:$|[|;&)\n]))", Ci);

    /// <summary>The call's output may not be the whole: a cut command (unless it provably kept every line: the last stage of its pipeline,
    /// a readable head/tail limit, fewer lines printed - claude_bash_sample_guard.py row 2010), a paginated Grep, a partial Read.</summary>
    public static bool IsCut(WholeEv e)
    {
        if (e.Kind != WholeKind.Call) return false;
        if (e.Tool == "Read") return e.Partial;
        if (e.Tool == "Grep") return (e.Output ?? "").Contains("[Showing results with pagination", StringComparison.Ordinal);
        if (e.Tool is not ("Bash" or "PowerShell")) return false;
        var lines = (e.Output ?? "").Split('\n').Length;
        foreach (var (re, _) in Cuts)
            foreach (Match m in re.Matches(e.Input))
                if (!KeptWhole(e.Input, m.Index, lines)) return true;
        return false;
    }

    private static bool KeptWhole(string cmd, int at, int printed)
    {
        var m = HeadLimit.Match(cmd, at);
        if (!m.Success) return false;
        var n = m.Groups[1].Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)
              : m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 10;
        var rest = cmd[(m.Index + m.Length)..];
        var end = Regex.Match(rest, @"[;\n)]|&&|\|\|");
        var stage = end.Success ? rest[..end.Index] : rest;
        if (stage.Contains('|') || Regex.IsMatch(stage, @"(?<!2)>")) return false;
        return printed < n;
    }

    private static readonly Regex SourceFile = new(@"[\w\-.]+\.(?:txt|csv|tsv|md|json|jsonl|log|e2k|f2k|xml|html|py|cs|sql|dxf|pdf|ps1|trx|out|output)\b", Ci);

    /// <summary>The file names (basenames) a call reads - the "same source" test of W4.</summary>
    public static HashSet<string> SourcesOf(WholeEv e)
    {
        var text = e.Tool is "Read" or "Grep" ? e.Input : e.Tool is "Bash" or "PowerShell" ? e.Input : "";
        return SourceFile.Matches(text.Replace('\\', '/')).Select(m => m.Value.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
    }

    // ---- where a conclusion leaves the session ----

    public enum DoorKind { None, Reply, Message, Report, Board, Record, Commit, Step }

    /// <summary>A door: its kind, the text judged, whether W1 applies, whether it is the expensive step or a board close (W3).</summary>
    public sealed record Door(DoorKind Kind, string Text, bool Account, bool Step, bool Close)
    {
        public static readonly Door None = new(DoorKind.None, "", false, false, false);
    }

    private static readonly Regex StepRe = new(@"\\\\KOR-210\b|//KOR-210\b|\bKOR-210[\\/]|run-on-210|kor-runner|--deliver\b", Ci);
    private static readonly Regex BoardRe = new(@"oversight\.Commitment", Ci);
    private static readonly Regex CloseRe = new(@"\bState\s*=\s*N?'(?:done|dropped)'|kor_task\.py""?\s+accept\b", Ci);
    private static readonly Regex RecordRe = new(@"\bkor(?:\.exe)?""?\s+(?:record|state)\b|kor_task\.py""?\s+(?:return|accept)\b", Ci);
    private static readonly Regex CommitRe = new(@"\bgit\b[^|;&\n]*\bcommit\b", Ci);
    private static readonly Regex SqlFileArg = new(@"oq(?:wide)?\.py""?\s+(?:-\w+\s+)*?-i\s+(""[^""]+""|'[^']+'|\S+)", Ci);
    private static readonly Regex MessageFlag = new(@"(?:-m|--message|--says|--body|--because|--description|--evidence|--what|--why|--note|--summary|--title|--done-when)\s+(""(?:[^""\\]|\\.)*""|'[^']*')", Ci);

    /// <summary>Message-flag values blanked (a commit message naming KOR-210 is not a send to it), positions kept.</summary>
    public static string Blank(string cmd) => MessageFlag.Replace(cmd, m => m.Value[..(m.Groups[1].Index - m.Index)] + new string(' ', m.Groups[1].Length));

    /// <summary>The door a PreToolUse call is, or <see cref="Door.None"/>. <paramref name="readFile"/> reads an oq.py -i .sql file
    /// (null when it cannot).</summary>
    public static Door DoorOf(string tool, string input, string? filePath, Func<string, string?> readFile)
    {
        switch (tool)
        {
            case "SendMessage": return new Door(DoorKind.Message, input, true, false, false);
            case "Write" when filePath is not null && Regex.IsMatch(filePath, @"\.(?:md|txt|csv)$", Ci):
                return new Door(DoorKind.Report, input, false, false, false);
            case "Bash" or "PowerShell":
            {
                var blanked = Blank(input);
                var sql = SqlFileArg.Match(input) is { Success: true } f ? readFile(f.Groups[1].Value.Trim('"', '\'')) : null;
                var both = input + (sql is null ? "" : "\n" + sql);
                var board = BoardRe.IsMatch(both) && Regex.IsMatch(both, @"\b(?:UPDATE|INSERT)\b", Ci);
                var close = (board && CloseRe.IsMatch(both)) || Regex.IsMatch(input, @"kor_task\.py""?\s+accept\b", Ci);
                var step = !CommitRe.IsMatch(blanked) && StepRe.IsMatch(blanked);
                if (board) return new Door(DoorKind.Board, both, true, step, close);
                if (RecordRe.IsMatch(blanked)) return new Door(DoorKind.Record, input, true, step, close);
                if (CommitRe.IsMatch(blanked)) return new Door(DoorKind.Commit, input, false, false, false);
                if (step) return new Door(DoorKind.Step, input, false, true, false);
                return Door.None;
            }
            default: return Door.None;
        }
    }

    public static string Squash(string s) => Regex.Replace(s, @"\s+", " ").Trim();
    public static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}

/// <summary>
/// The ledger of ONE session, built moment by moment (<see cref="Add"/>) so the hook (all moments, then one door) and the replay (a
/// door judged before each door moment is added) run the same code. Decide never adds the door's own call.
/// </summary>
public sealed class WholeLedger
{
    public sealed class Flag
    {
        public required int Line { get; init; }
        public required int CallNo { get; init; }
        public required WholeGate.Reading R { get; init; }
        public bool Brief { get; init; }   // a hole this session WROTE into its own brief: it holds for the whole session under W1
        public bool Accounted, Opened, Superseded;
        public bool Forgiven;  // replay only: the session is taken to have complied after its first refusal (see Comply)
        public int MostLook;   // the most non-empty lines of an uncut look since the flag
        public bool Discharged => Opened || Superseded || Forgiven;
    }

    /// <summary>Replay only: after a refusal the live session would have opened or accounted for what was named, so each flag refuses
    /// once and the dry run counts DISTINCT faults, not the cascade a session that never saw the gate produces.</summary>
    public void Comply(IEnumerable<Flag> named)
    {
        foreach (var f in named) { f.Accounted = true; f.Forgiven = true; }
    }

    public sealed record Verdict(bool Refuse, string Rules, string? Reason, IReadOnlyList<Flag> Named)
    {
        public static readonly Verdict Allow = new(false, "", null, Array.Empty<Flag>());
    }

    private readonly List<Flag> _flags = new();
    private readonly List<(int Line, string Output, bool Cut, HashSet<string> Sources)> _seen = new();
    private readonly List<string> _turn = new();
    private readonly StringBuilder _narration = new();
    private int _calls;
    private int _promptLine = int.MinValue;

    public IReadOnlyList<Flag> Flags => _flags;
    public string TurnText => string.Join("\n", _turn);
    public string Narration => _narration.ToString();

    public void Add(WholeEv e)
    {
        switch (e.Kind)
        {
            case WholeKind.Prompt:
                _promptLine = e.Line;
                _turn.Clear();
                _narration.Clear();
                return;
            case WholeKind.Text:
                _turn.Add(e.Input);
                _narration.Append(e.Input).Append('\n');
                Account(e.Input, e.Line);
                return;
        }
        _calls++;
        Account(e.Input, e.Line);
        _narration.Clear();
        foreach (var h in WholeGate.BriefHoles(e)) _flags.Add(new Flag { Line = e.Line, CallNo = _calls, R = h, Brief = true });
        if (e.Output is null) return;
        // a call another hook refused never ran: its result is that hook's text (records it named, rules it quoted), not a check's
        if (e.Output.StartsWith("PreToolUse:", StringComparison.Ordinal) || e.Output.StartsWith("PostToolUse:", StringComparison.Ordinal)) return;
        var cut = WholeGate.IsCut(e);
        if (!cut && e.Tool is "Bash" or "PowerShell" or "Read")
        {
            var lines = e.Output.Split('\n').Count(l => l.Trim().Length > 0);
            foreach (var f in _flags) if (f.Line < e.Line && lines > f.MostLook) f.MostLook = lines;
        }
        if (WholeGate.IsRun(e))
        {
            foreach (var r in WholeGate.Readings(e.Output))
            {
                if (r.Flagged > 0) { _flags.Add(new Flag { Line = e.Line, CallNo = _calls, R = r }); continue; }
                foreach (var f in _flags)
                    if (!f.Superseded && !f.R.Hole && f.R.Label == r.Label && f.R.Of == r.Of) f.Superseded = true;
            }
        }
        if (e.Tool is "Bash" or "PowerShell" or "Read" or "Grep")
            _seen.Add((e.Line, e.Output, cut, WholeGate.SourcesOf(e)));
    }

    private static readonly Regex OpenedLine = new(@"(?<!NOT )\bOPENED\s+(?<n>\d[\d,]*)\s+of\s+(?<m>\d[\d,]*)", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "them", "they", "from", "into", "only", "once", "list", "text", "cover", "covers",
        "step", "scope", "out", "not", "checked", "graded", "looked", "opened", "unproven", "codex", "each", "every", "item", "items",
    };

    /// <summary>Every accounting line in a text marks the flags it names (OPENED / NOT OPENED / NOT CHECKED).</summary>
    private void Account(string text, int line)
    {
        if (text.Length == 0 || (!text.Contains("OPENED", StringComparison.Ordinal) && !text.Contains("NOT CHECKED", StringComparison.Ordinal))) return;
        foreach (var raw in text.Split('\n'))
        {
            var l = WholeGate.Squash(raw);
            var notOpened = l.Contains("NOT OPENED", StringComparison.Ordinal);
            var notChecked = l.Contains("NOT CHECKED", StringComparison.Ordinal);
            var opened = OpenedLine.Match(l);
            var openedHole = !notOpened && Regex.IsMatch(l, @"(?<!NOT )\bOPENED\b", RegexOptions.CultureInvariant);
            if (!notOpened && !notChecked && !opened.Success && !openedHole) continue;
            foreach (var f in _flags)
            {
                if (f.Line >= line || f.Discharged) continue;
                if (f.R.Hole)
                {
                    var words = HoleWords(f.R.Key);
                    // a key with no distinctive word is accounted by naming the key itself
                    if (words.Count == 0 ? !l.Contains(f.R.Key, StringComparison.OrdinalIgnoreCase)
                                         : !words.Any(w => l.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;
                    if (notChecked || notOpened) f.Accounted = true;
                    else if (openedHole && f.MostLook > 0) { f.Accounted = true; f.Opened = true; }
                    continue;
                }
                if (!l.Contains(f.R.Key, StringComparison.OrdinalIgnoreCase)) continue;
                if (notOpened) { f.Accounted = true; continue; }
                if (opened.Success && Int(opened.Groups["n"].Value) == f.R.Flagged && Int(opened.Groups["m"].Value) == f.R.Flagged && f.MostLook >= f.R.Flagged)
                {
                    f.Accounted = true;
                    f.Opened = true;
                }
            }
        }
    }

    private static int Int(string s) => int.TryParse(s.Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : -1;

    public static List<string> HoleWords(string key) =>
        Regex.Matches(key, @"[A-Za-z0-9][A-Za-z0-9_-]{3,}").Select(m => m.Value).Where(w => !Common.Contains(w)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static readonly Regex Dismiss = new(@"\b(?:artifacts?|artefacts?|noise|noisy|expected|spurious|false positives?|harmless|cosmetic|benign|rounding|measuring error|measurement error|not real|ignorable|can be ignored|flaky|transient)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    /// <summary>A range or distribution of values: "3.1-3.4 in", "3.1 to 3.4", "between 2 and 6", "consistently", "at most", "under 4".</summary>
    private static readonly Regex RangeOver = new(@"\d(?:[\d.,]*)\s*(?:-|–|—|to)\s*\d|\bbetween\s+[\d.]+\s+and\s+[\d.]+|\bconsistently\b|\buniformly\b|\b(?:at most|at least|no more than|no less than|under|over|below|above|within)\s+[\d.]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WholeClaim = new(@"\b(?:all|none of(?: the| them)?|every one of(?: the| them)?|each of(?: the)?|all of(?: the| them)?)\s+(?<y>\d{1,3}(?:,\d{3})+|\d+)\b(?!\s*(?:%|in\b|mm\b|ft\b|ms\b|s\b|sec|min|h\b|hours?|days?|minutes?|seconds?|kb|mb|gb|k\b|x\b))|\b(?<a>\d{1,3}(?:,\d{3})+|\d+)\s+of\s+(?<b>\d{1,3}(?:,\d{3})+|\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The verdict for one door, on the moments added so far (the door's own call is not among them).</summary>
    public Verdict Decide(WholeGate.Door door)
    {
        if (door.Kind == WholeGate.DoorKind.None) return Verdict.Allow;
        // review 2583: the door's own accounting lines count for THIS verdict, but are kept only if the door is allowed - a refused
        // call never ran and a blocked reply was not sent, so their OPENED / NOT OPENED lines did not reach anyone
        var before = _flags.Select(f => (f.Accounted, f.Opened)).ToList();
        Account(door.Text, int.MaxValue);
        var v = Judge(door);
        if (v.Refuse)
            for (var i = 0; i < before.Count; i++) { _flags[i].Accounted = before[i].Accounted; _flags[i].Opened = before[i].Opened; }
        return v;
    }

    private Verdict Judge(WholeGate.Door door)
    {
        var recent = _flags.Where(f => f.CallNo > _calls - WholeGate.Window).ToList();
        var rules = new List<string>();
        var named = new List<WholeLedger.Flag>();
        var sb = new StringBuilder();

        // W2: a flag explained away unopened
        var dismissed = new List<Flag>();
        foreach (var s in Regex.Split(door.Text, @"(?<=[.!?])\s+|\n"))
        {
            if (!Dismiss.IsMatch(s)) continue;
            foreach (var f in recent)
                if (!f.Discharged && !f.R.Hole && !dismissed.Contains(f) && Mentions(s, f)) dismissed.Add(f);
        }
        if (dismissed.Count > 0)
        {
            rules.Add("W2");
            named.AddRange(dismissed);
            sb.Append("W2 - a flagged result is explained away (artifact / noise / expected / ...) without being opened. Ian: a flagged count is OPENED and looked at, never explained away (lesson #135770: 4 pads called a measuring artifact; SAFE hung on exactly those 4):\n");
            List(sb, dismissed);
            sb.Append("  Open them: run a look whose UNCUT output shows each flagged item, then write `OPENED n of n: <key> - <what each one is>`. Not opened is not a reason it is noise.\n");
        }

        // W4: a range or distribution over the whole, written from a sample (rule 12). Only a sentence that claims the whole of Y AND
        // states a range of values over it: a bare "84 of 84" rests on the count a check printed (dry run 20 Nov 19:1x: 127 distinct
        // claims refused on a count beside an unrelated cut, wc -l and grep -c ": OK$" counts, test totals)
        foreach (var sentence in Regex.Split(door.Text, @"(?<=[.!?])\s+|\n"))
        foreach (Match m in WholeClaim.Matches(sentence))
        {
            var y = m.Groups["y"].Success ? m.Groups["y"].Value : m.Groups["a"].Value == m.Groups["b"].Value ? m.Groups["a"].Value : null;
            if (y is null || Int(y) < 3 || rules.Contains("W4")) continue;
            // the RANGE must be numeric and both its ends must be among the values the cut output SHOWED (case 2: 3.1 and 3.4 were
            // in the five lines head -5 kept). Dry run 4 (20 Nov 19:4x): 50 of 50 distinct claims refused on any range phrase -
            // "L03-L05", "07:35-07:5x", "within 0.05" - rested on a count the check printed, none on the values of a sample
            if (NumericRange.Match(sentence) is not { Success: true } rg) continue;
            if (SampledOnly(y, rg.Groups["a"].Value, rg.Groups["b"].Value) is not { } at) continue;
            rules.Add("W4");
            sb.Append($"W4 - \"{WholeGate.Squash(m.Value)}\" claims the whole of {y}, but the latest output that holds {y} (transcript line {at.Line}) was CUT (head / tail / grep -m / a paginated Grep / a partial Read) and no later uncut call read the same file. Operations CLAUDE.md rule 12: a truncated command cannot support a claim about the whole. Count it whole (| sort | uniq -c over every line, no cut) and state X of Y.\n");
            break;
        }

        // W3: the expensive step or a close while a flag is unopened
        if (door.Step || door.Close)
        {
            var open = recent.Where(f => !f.Discharged).ToList();
            if (open.Count > 0)
            {
                rules.Add("W3");
                named.AddRange(open.Where(f => !named.Contains(f)));
                sb.Append($"W3 - {(door.Step ? "the expensive step (KOR-210 / --deliver)" : "a board close")} while {open.Count} flagged or out-of-scope item(s) from the last {WholeGate.Window} calls are not opened. Lesson #135770: a flagged count is opened and looked at BEFORE the expensive step. NOT OPENED does not lift this:\n");
                List(sb, open);
                sb.Append("  Open each (an uncut look that shows them, then `OPENED n of n: <key> - <what they are>`), or fix and re-run the check so it prints no deficit.\n");
            }
        }

        // W1: every flag of this turn and every hole of the session accounted for
        if (door.Account)
        {
            // this turn's flags; when the turn's prompt lies before the tail read (no prompt seen), the last Window calls stand for it
            // (review 2583), and holes of the whole tail as before
            var hidden = _flags.Where(f => !f.Accounted && !f.Discharged &&
                                           (f.Brief || (f.R.Hole && f.CallNo > _calls - WholeGate.Window) ||
                                            (_promptLine == int.MinValue ? f.CallNo > _calls - WholeGate.Window : f.Line > _promptLine))).ToList();
            if (hidden.Count > 0)
            {
                rules.Add("W1");
                named.AddRange(hidden.Where(f => !named.Contains(f)));
                sb.Append($"W1 - {hidden.Count} flagged / non-pass / out-of-scope item(s) a check printed are not accounted for in this {Say(door.Kind)}. Ian 20 Nov: look at the WHOLE thing - every item a check flags or leaves non-pass:\n");
                List(sb, hidden);
                sb.Append("  For each, one line: `OPENED n of n: <key> - <what each is>` (after an uncut look showing them), or `NOT OPENED: <key>`; for an out-of-scope part, `NOT CHECKED: <what was left out>`.\n");
            }
        }

        if (rules.Count == 0) return Verdict.Allow;
        var head = $"kor whole (board row 2139, the whole-source ledger gate): refused - {string.Join(", ", rules)}.\n";
        return new Verdict(true, string.Join("+", rules), head + sb + "Wrong refusal? Say so to the Framework lead with this text (guard kor-whole; judge it with Framework tools/guard_log.py).", named);
    }

    private static string Say(WholeGate.DoorKind k) => k switch
    {
        WholeGate.DoorKind.Reply => "reply",
        WholeGate.DoorKind.Message => "message",
        WholeGate.DoorKind.Board => "board write",
        WholeGate.DoorKind.Record => "record",
        _ => "text",
    };

    private static readonly Regex FlagTalk = new(@"\b(?:flag|flags|flagged|check|checks|checked|fail|fails|failed|failing|failures?|differ|differs|mismatch(?:es)?|missing|wrong|misleading|not graded|not checked)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The sentence is about this flag: it names one of the flag's distinctive label words (5+ letters, e.g. "4-sided"), or
    /// its flagged number together with words about a check's result ("The 4 my first check flagged"). A bare small number alone is not
    /// enough (dry run 2: "219 of 221 keys match ... expected" was tied to a "2 FAIL" flag by its digit).</summary>
    private static bool Mentions(string sentence, Flag f)
    {
        if (f.R.Label.Split(' ').Where(w => w.Length >= 5 && !Common.Contains(w) && !WholeGate.Fail.Contains(w) && !WholeGate.Pass.Contains(w))
                .Any(w => Regex.IsMatch(sentence, $@"(?<![\w-]){Regex.Escape(w)}(?![\w-])", RegexOptions.IgnoreCase)))
            return true;
        return Regex.IsMatch(sentence, $@"(?<![\d.,]){f.R.Flagged}(?![\d.,]\d|\d)") && FlagTalk.IsMatch(sentence);
    }

    /// <summary>W4: the latest output holding <paramref name="y"/>, when it was cut and no later uncut call read the same file; else null.</summary>
    /// <summary>A numeric value range: "3.1-3.4", "3.1 to 3.4", "between 2.1 and 4.3" - decimals or a unit, so labels ("L03-L05") and
    /// clock times ("07:35-07:5x") are not ranges.</summary>
    private static readonly Regex NumericRange = new(@"(?<![\w:.])(?<a>\d+\.\d+|\d+(?=\s*(?:in|mm|ft|kPa|psf|MPa|%)\b))\s*(?:in|mm|ft)?\s*(?:-|–|—|to|and)\s*(?<b>\d+(?:\.\d+)?)(?![\w:])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private (int Line, bool Cut)? SampledOnly(string y, string a, string b)
    {
        if (SampledOnly(y) is not { } at) return null;
        var shown = _seen.First(s => s.Line == at.Line).Output;
        bool Has(string v) => Regex.IsMatch(shown, $@"(?<![\d.]){Regex.Escape(v)}(?![\d])");
        return Has(a) && Has(b) ? at : null;
    }

    private (int Line, bool Cut)? SampledOnly(string y)
    {
        // Y as a BARE COUNT of the population (case 2: grep -c printed "37" on its own line; "37 total", "count: 37", "37 rows"), never a
        // check's own "Y of Y" or "X of Y" - a tail -1 of a check's summary line reports the whole the check counted (first dry run:
        // 20 W4 refusals on any digit a cut output held, "3 of 3", "148 of 148")
        var e = Regex.Escape(y);
        var count = new Regex($@"(?m)^\s*{e}\s*$|(?<![\d.,]){e}\s+(?:total|rows?|files?|items?|lines?|records?|outlines?|pads?|members?|sheets?|pages?|cases?|tests?|matches|results?|hits?)\b|\b(?:total|count|counted|found|n)\s*[:=]?\s*{e}(?![\d]|[.,]\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var ofY = new Regex($@"\b\d[\d,]*\s+of\s+{e}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (Int(y) < 5) return null;
        for (var i = _seen.Count - 1; i >= 0; i--)
        {
            if (!count.IsMatch(_seen[i].Output)) continue;
            if (ofY.IsMatch(_seen[i].Output)) return null;
            if (!_seen[i].Cut) return null;
            var src = _seen[i].Sources;
            for (var j = i + 1; j < _seen.Count; j++)
                if (!_seen[j].Cut && _seen[j].Sources.Overlaps(src)) return null;
            return (_seen[i].Line, true);
        }
        return null;
    }

    private static void List(StringBuilder sb, List<Flag> fs)
    {
        foreach (var f in fs.Take(WholeGate.MostNamed))
            sb.Append($"  - transcript line {f.Line}: {(f.R.Hole ? "out of scope / not checked" : $"{f.R.Flagged} flagged")} - key `{WholeGate.Clip(f.R.Key, 140)}`\n");
        if (fs.Count > WholeGate.MostNamed) sb.Append($"  - and {fs.Count - WholeGate.MostNamed} more (kor whole --replay <transcript> lists every one)\n");
    }
}
