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
///          an Agent/Task helper's report), a non-pass result: "label X of Y" / "X of Y label" with X &lt; Y and a pass
///          word in its cell (pass, ok, match, equal, N-sided ... -> Y-X flagged) or a fail word (-> X flagged); or "N failed" /
///          "FAILED: N" / "2 WRONG" with N &gt;= 1 (<see cref="Readings"/>).
///          Explicit missing values take precedence over an adjacent file total; expected FAIL states printed PASS
///          count false acceptances (zero is clean). Same-output comparisons by failing name can explain runner counts;
///          equal totals alone and bare baseline claims cannot. A 45-of-46 pass count still flags one failure.
///   HOLE - "OUT OF SCOPE", "NOT CHECKED", "not graded", "not looked at", "not opened", "UNPROVEN" in such a result, or in this
///          session's own Write/Edit of a brief, prompt or plan file (a look scoped to part of its source).
///          Cited historical examples and lists defining the hole vocabulary are not that brief's own exclusions.
///   DISCHARGED - OPENED: a later line "OPENED n of N" (n = N flagged) holding the flag's key, after an UNCUT look (Bash/PowerShell/
///          Read whose output was not cut) of at least N non-empty lines; or SUPERSEDED: a later run printing the same label with no
///          deficit (X of X, or 0). A hole is OPENED by a line "OPENED" naming one of its words after any uncut look.
///          An uncut TRX differential listing also opens failures when its distinct BOTH identities equal the declared
///          failure count, all are also in base, and none is new. This applies only to failures tied to the same TRX sources.
///          NOT LISTABLE: &lt;key&gt; - &lt;why, naming the inspected source&gt; also discharges a count, but ONLY after a later,
///          uncut tool output prints that population's total and its listable part. The bounded format is
///          "&lt;label&gt;: count N; listable: M" (or "pieces &gt; 1e-9 M"), followed by distinct records numbered 1..M,
///          as JSON objects or "1: description" lines; 0 &lt;= M &lt; N. The explanation must name a file read by that call.
///          A bare declaration, another population/source, a short/duplicate list or a refused/cut look cannot lift W3.
///   ACCOUNTED - discharged, or a line "NOT OPENED: &lt;key&gt;" (a flag) / "NOT CHECKED: &lt;one of its words&gt;" (a hole).
///          Casing is immaterial. Naming an excluded source as out of scope also accounts for it, but does not open it.
///          A ratio stating the same population, deficit and distinctive subject also accounts for the flag (including
///          a test summary's arithmetically verified Total). This is disclosure only; W2/W3 still require opening or rerunning.
///          Accounting counts immediately in assistant text and tool input, including a look's own input after its output
///          arrives. A prospective door cannot use its future output; denied tool input does not earn credit.
///
/// THE DOORS - where a conclusion leaves the session (<see cref="DoorOf"/>): Stop (the turn's assistant text; blocks once - the
/// stop_hook_active retry is allowed, so it never loops); SendMessage; a Write of a .md/.txt/.csv; a Bash/PowerShell board write
/// (oversight.Commitment, inline or in the oq.py -i file), kor_task return/accept, git commit; the EXPENSIVE STEP
/// (a command naming \\KOR-210, run-on-210, kor-runner or --deliver, outside a message flag). The narration written just before a
/// PreToolUse door is part of its text.
/// READ-ONLY LOOKS (including git reads and runner documentation) and work-saving kor record/state are ALLOWED. An expensive
/// command joined to them is still a door; a relative send script inherits its run-on-210 working directory. Recovery-stage
/// classification is separate from output provenance: a read of this session's own log still supplies findings.
///
/// THE RULES (<see cref="WholeLedger.Decide"/>), every one naming the flag, its transcript line and the exact line that lifts it:
///   W1 ACCOUNT  - at Stop, SendMessage and a board write: every flag of this turn, every hole a check printed in the
///                 last <see cref="Window"/> calls, and every hole this session wrote into its own brief (for the whole session) is
///                 accounted for. Hiding a flag is refused; saying it plainly (NOT OPENED) is not. Case 1 L6490, case 3 L2763.
///   NOT A CHECK - a reader's output; another hook's refusal; an output carrying this gate's own refusal; a line quoting a session
///                 by id and transcript line (dry-run and transcript-search output); a command naming --replay or a .jsonl.
///                 Also a bounded Python source-docstring read, or stored prose read through a known SQL script successfully
///                 written earlier in this transcript. Bounded literal printf SQL and quoted heredoc reader scripts also
///                 carry provenance; a review's explicitly attributed document quote is not its own measurement.
///                 A bounded Python print(open(...).read()) and marked git diff lines retain read provenance even in a
///                 mixed command. Explicitly attributed document counts in a review are quotations; current findings remain.
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
/// enough uncut look ran after the flag, not that it showed THEM); a result file whose producer/path was not visible in this
/// transcript; a flag of an earlier turn under W1 (W2 and W3 still see the last <see cref="Window"/> calls); work
/// in another session. Provenance remains bounded: an unknown reader or an unrecognised quotation can still be treated as a check.
/// A quoted claim is suppressed only in the recognised assessment form; an Agent/Task review's current findings still count.
/// The text cache knows successful Write/Edit/MultiEdit, bounded printf SQL and quoted cat heredocs in the parsed transcript.
/// Unknown scripts remain runs. Shell/Python recognition is bounded, not an interpreter or a proof of a script's semantics.
/// A known run's redirected/tee log or explicitly located TRX remains a result when read later (including background runs).
/// Successful Write/Edit replaces that provenance. Hidden script writes, external overwrites and cross-session producers
/// are not inferred. A shell substitution is a reader only when every enclosed stage is itself a reader.
/// Existing command-wide record/replay exemptions remain: a mixed command mentioning --replay can still hide a real check.
/// Baseline listings establish opening, not correctness of the baseline or a reason to ship a failing build. TRX paths are
/// resolved lexically using that command's variables/cwd; W4 still uses basenames. Symlinks, external overwrites and content
/// identity are not established. Accounting still relies on words. SAME-CLASS FAULT IT WOULD MISS: case 1 again if the first
/// check had printed "4 pads irregular" - no number with a polarity word, no flag, nothing to account for.
/// Round 4 same-class limits: unknown wrappers/aliases can hide an expensive action; reused labels can conflate accounting;
/// arbitrary wording can invert an unrecognised count; unmarked quoted prose in mixed output may still flag; and a tool can
/// print a dishonest population/listable count or numbered filler. NOT LISTABLE proves the stated look exists, not that no
/// other tool could enumerate the remainder. Baseline comparison text is trusted, not independently rerun.
/// For a whole FOLDER or package, the file ledger is Framework tools/blind_look_check.py --package (one row per file, X of Y).
///
/// MEASURED (20 Nov 2026, kor whole --replay over the 173 main transcripts written 16-20 Nov in Framework, KOR.Drafter and
/// Operations; a session is taken to comply after its first refusal): 2,452 door moments, 229 refused - W1 141, W2 14, W3 86, W4 0.
/// The 563 distinct flags of dry run 2 were read whole; the misfire classes found were fixed (X/Y ids, far polarity words, grep -n
/// and diff lines, "not checked: 0", quoted mentions, ids and a ratio's Y as counts, how-to-read brief text), and what is left is
/// predominantly real non-pass results the sessions never accounted for (NOT ACCEPTED 6 of 14, FAIL n, NOT GRADED, NOT RUN/FETCHED,
/// LOOK 235 of 239, "not looked at"). The 50 distinct W4 cases of dry run 4 were read whole: 0 were the sampled-range fault, so W4
/// now needs both ends of its numeric range among the values the cut output showed (it still refuses case 2). Known residual
/// misfires: PAGE FILL page counts; a brief's own text
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
    // ... and this gate's own refusal text (its listed keys, echoed by a proof run) is never a check's result
    // a line citing a SESSION and a transcript line quotes that session ("REFUSE e7287342-....jsonl L6490 ...", "== 17bc90f5 Y=8 ...",
    // "139b7ae2 L116 LOOK 235 of 239") - the row-2139 close was refused on 60 such lines its own analysis printed
    private static readonly Regex QuoteLine = new(@"^\s*(?:REFUSE |== )?[0-9a-f]{8}(?:-[0-9a-f]{4}){0,4}(?:-[0-9a-f]{12})?(?:\.jsonl)?\s+(?:L\d+|Y=)|kor whole \(board row 2139|^\s*- transcript line -?\d+: |\bW[1-4] - |\brecord #\d+|\[(?:finding|lesson|decision|dead-end|reference|feedback|project)[,\]]|\bboard row \d+ \[|^\s*\d+:(?!\d\d\b)|^\S+\.\w{1,5}:\d+[:-]|^[<>] |^\{[+-]", Ci);
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
    // A review assessing a quoted claim is not issuing that claim as its own measurement. Blank only the quote,
    // retaining any current findings elsewhere on the same line (and never treating a quoted clean result as a rerun).
    private static readonly Regex AssessedQuote = new(@"(?<quote>'[^'\r\n]+'|""[^""\r\n]+""|`[^`\r\n]+`)(?=\s+(?:is|was)\s+(?:a|the)\s+(?:judg(?:e)?ment|claim|quote|statement|summary)\b)", Ci);
    // GuardRefusal export: id | session UUID | repo | timestamp | rule/door | matched historical text.
    // Keep this at the line level: a command may print these records and a new check result together.
    private static readonly Regex RefusalRow = new(@"^\s*\d+\|[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\|[^|\r\n]*\|\d{4}-\d{2}-\d{2}T[^|\r\n]+\|row \d+ W[1-4](?:\+W[1-4])* at (?:Board|Message|Reply|Record|Report|Commit|Step)\|L\d+\b", Ci);
    private static readonly Regex StashRow = new(@"^\s*\d+\s+[0-9a-f]{7,40}\s+\d{4}-\d{2}-\d{2}\s+covered\b", Ci);
    private static readonly Regex StashTitle = new(@"\s+(?:WIP\s+)?On\s+\S+:.*$", Ci);
    private static readonly Regex HistoricalParagraph = new(@"^\s*(?:\d+\.\s*)?(?:[A-Za-z.]+\s+)?[0-9a-f]{8}(?:-[0-9a-f-]+)?\b.*\bL\d+[: ]", Ci);
    private static readonly Regex HoleVocabulary = new(@"\bout of scope\s*/\s*(?:not checked|not in scope)\b", Ci);
    private static readonly Regex TestTotals = new(@"\bFailed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+),\s*Skipped:\s*(?<skipped>\d+),\s*Total:\s*(?<total>\d+)\b", Ci);

    /// <summary>One result a check printed: Flagged items not passing (0 = a clean reading of that label), out of Of (0 = a count).</summary>
    public sealed record Reading(string Key, int Flagged, int Of, string Label, bool Hole)
    {
        // A test runner's Total is disclosure metadata, not part of the count-form check's identity. A later
        // clean rerun must still supersede "Failed: n" when its number of tests has changed.
        public int Population { get; init; }
    }

    private static int N(string s) => int.TryParse(s.Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : -1;

    /// <summary>Every reading in a check's output, line by line; a line's cells are split at | ; and tabs.</summary>
    public static List<Reading> Readings(string output, bool documentReview = false)
    {
        var list = new List<Reading>();
        foreach (var rawLine in output.Split('\n'))
        {
            // a long line (a JSON row, a CSV line) is read to 20,000 characters: at 2,000 a result past that point was never seen
            var line = rawLine.Length > 20_000 ? rawLine[..20_000] : rawLine;
            if (QuoteLine.IsMatch(line) || RefusalRow.IsMatch(line)) continue;
            // Assessment wording alone can be the checker's own summary. Require a review and an explicitly
            // attributed document before treating its quoted claim as historical (B2 L3808).
            if (documentReview && Regex.IsMatch(line, @"\b[\w/.-]+\.(?:md|txt|cs|py)\b.*?\s-\s+", Ci))
                line = AssessedQuote.Replace(line, m => new string(' ', m.Length));
            var totals = TestTotals.Match(line);
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
                    // A false acceptance is a failure of the checker: zero expected-FAIL states printed
                    // PASS is clean, despite PASS being the closest word (C L921, F L331).
                    if (Regex.IsMatch(cell[..m.Index], @"\bexpected FAIL states printed PASS:\s*$", Ci)) fail = true;
                    var flagged = fail.Value ? x : y - x;
                    var label = Squash(Regex.Replace(cell[lo..hi], @"\d[\d,]*\s+of\s+\d[\d,]*", " ")).ToLowerInvariant();
                    // The coverage count is live; the stash's old commit title is quoted metadata. "PARKED" in that
                    // title must not tie a later sentence about parked work and noisy guards to this coverage check.
                    if (StashRow.IsMatch(line)) label = StashTitle.Replace(label, "");
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
                var key = Squash(m.Value.Trim());
                // In "files 63 missing []" / "files 65 missing 0 []", missing labels the value
                // AFTER it. The file total before it is not a deficit. Keep nonzero explicit counts.
                if (w == "missing" && m.Groups["n"].Index < m.Groups["w"].Index &&
                    Regex.Match(line[(m.Index + m.Length)..], @"^\s+(?:(?<n>\d+)\b|(?<empty>\[\s*\]))") is { Success: true } value)
                {
                    n = value.Groups["empty"].Success ? 0 : N(value.Groups["n"].Value);
                    key = "missing " + n.ToString(CultureInfo.InvariantCulture);
                }
                // Keep a test summary's population only when its arithmetic checks out. That lets "817 of 819
                // green" disclose these two failures in plain words, without claiming they were opened or fixed.
                var population = totals.Success && w == "failed" && n == N(totals.Groups["failed"].Value) &&
                         (long)n + N(totals.Groups["passed"].Value) + N(totals.Groups["skipped"].Value) == N(totals.Groups["total"].Value)
                    ? N(totals.Groups["total"].Value) : 0;
                list.Add(new Reading(key, n, 0, w, false) { Population = population });
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
        var history = false;
        foreach (var line in e.Input.Split('\n'))
        {
            // Each cited list item starts its own historical passage; a heading or previous numbered item need
            // not be separated from it by a blank line. Only indented continuations inherit that provenance.
            if (HistoricalParagraph.IsMatch(line)) { history = true; continue; }
            if (history && line.Trim().Length > 0 && char.IsWhiteSpace(line[0])) continue;
            history = false;
            if (QuoteLine.IsMatch(line) || HoleVocabulary.IsMatch(line)) continue;
            if (BriefHole.Match(line) is { Success: true } h)
                list.Add(new Reading(Squash(Clip(line.Trim(), 160)), 1, 0, h.Value.ToLowerInvariant(), true));
        }
        return list;
    }

    // ---- what a command is ----

    private static readonly HashSet<string> ReaderWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "cat", "type", "head", "tail", "grep", "egrep", "fgrep", "rg", "findstr", "select-string", "sls", "sed", "ls", "dir", "get-childitem",
        "gci", "get-content", "gc", "git", "echo", "printf", "write-host", "write-output", "wc", "find", "sort", "uniq", "cut", "tr", "stat",
        "date", "pwd", "which", "where", "where-object", "select-object", "foreach-object", "format-table", "format-list", "measure-object", "tasklist",
        "cd", "set-location", "mkdir", "new-item", "cp", "copy-item", "mv", "rm", "touch", "tee", "jq", "file", "du", "df", "basename",
        "dirname", "realpath", "get-item", "test-path", "resolve-path", "true", "false", "sleep", "awk", "xargs", "column", "nl", "less", "more",
        "export", "set", "unset", "local", "for", "do", "done", "if", "then", "fi", "else", "while", "out-file",
    };
    // a dry run or a transcript search prints OTHER sessions' results: the row-2139 close itself was refused on 141 such flags
    // (kor whole --replay listings, .jsonl searches) - any lead who replays a guard would block itself the same way
    private static readonly Regex RecordReader = new(@"\bkor(?:\.exe)?""?\s+(?:recall|memory|start|read|events|checks|start-docs)\b|kor_brain\.py\s+(?:show|render)|guard_log\.py|board_render\.py|brain\.(?:Memory|Event|LaneState)|oversight\.(?:Commitment|GuardRefusal|vw_Board|Review)|--replay\b|\.jsonl\b", Ci);
    // A bounded source-documentation read observed at b6568d08 L715. Match the complete Python expression, not
    // a marker anywhere in an arbitrary program. Every remaining shell stage must still be a reader.
    private static readonly Regex PythonDocstring = new(@"\bpython(?:\.exe)?\s+(?:-I\s+)?-c\s+""\s*import ast\s*;\s*print\(\s*ast\.get_docstring\(\s*ast\.parse\(\s*open\('[^'\r\n]+',\s*encoding='utf-8'\)\.read\(\)\s*\)\s*\)\s*\)\s*""", Ci);
    private static readonly Regex PythonFileText = new(@"\bpython(?:\.exe)?\s+(?:-I\s+)?-c\s+""\s*print\(open\('(?<file>[^'\r\n]+)'(?:,\s*encoding='utf-8')?\)\.read\(\)\)\s*""", Ci);

    /// <summary>A command whose every stage is a reader (cat, grep, git, ls, sed, ...), or that reads records, the board or a transcript
    /// search: its output quotes what was already there - no check ran.</summary>
    public static bool IsReader(string command)
    {
        // Retain the pre-existing record/replay operation exemptions. Their command-wide nature is a known
        // limitation, separate from shell reader stages. A plain cat of a .jsonl does not exempt a later check.
        if (RecordReader.Matches(command).Any(m => !m.Value.Equals(".jsonl", StringComparison.OrdinalIgnoreCase)) ||
            command.Contains("<<", StringComparison.Ordinal) && RecordReader.IsMatch(command)) return true;
        var stages = ResolvedStages(Blank(PythonFileText.Replace(PythonDocstring.Replace(command, "cat source-documentation"), "cat file-text")), null);
        return stages is not null && stages.All(s => ReaderStage(s.Text) ||
            command.Contains(".jsonl", StringComparison.OrdinalIgnoreCase) && PythonProjection(s.Text));
    }

    private static bool ReaderStage(string stage)
    {
        if (Regex.Match(stage, @"^[A-Za-z_]\w*=\$\((?<read>[\s\S]*)\)$") is { Success: true } assignment)
            return IsReader(assignment.Groups["read"].Value);
        // A substitution or a PowerShell assignment can execute a check even when the visible command is echo.
        // Quoted awk/Python source is data; only expansion in the shell's own quote context matters here.
        if (ReaderSubstitutions(stage) is not { } expanded) return false;
        stage = expanded;
        if (RecordReader.IsMatch(stage)) return true;
        // A bounded PowerShell property read, as used at W L1047. Do not unwrap arbitrary script blocks.
        stage = Regex.Replace(stage, @"^\((Get-Content\s+[^\r\n]+)\)\.Count$", "$1", Ci);
        var args = ShellSplit(stage, words: true);
        if (args is null || args.Count == 0) return false;
        var first = args[0].Trim('"', '\'');
        var name = Path.GetFileName(first.Replace('\\', '/'));
        return ReaderWords.Contains(name);
    }

    // Only $(reader) is substituted, never arbitrary evaluation. This retains the provenance of git
    // subjects in D7's echo/loop commands without exempting a check inside the same substitution.
    private static string? ReaderSubstitutions(string text, Action<string>? inspect = null)
    {
        char quote = '\0';
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote == '\'') { if (c == '\'') quote = '\0'; continue; }
            if (c == '\'' && quote == '\0') { quote = c; continue; }
            if (c == '"') { quote = quote == '"' ? '\0' : '"'; continue; }
            if (c == '`') return null;
            if (c != '$' || i + 1 >= text.Length || text[i + 1] != '(') continue;
            var end = i + 2;
            var depth = 1;
            char innerQuote = '\0';
            for (; end < text.Length; end++)
            {
                var x = text[end];
                if (innerQuote != '\0') { if (x == innerQuote) innerQuote = '\0'; continue; }
                if (x is '\'' or '"') { innerQuote = x; continue; }
                if (x == '(') depth++;
                if (x == ')' && --depth == 0) break;
            }
            if (end == text.Length) return null;
            var inner = text[(i + 2)..end];
            inspect?.Invoke(inner);
            if (!IsReader(inner)) return null;
            text = text[..i] + "reader-output" + text[(end + 1)..];
            i += "reader-output".Length - 1;
        }
        return text;
    }

    // A deliberately bounded lexer, not a shell interpreter. Separators inside quotes and parentheses are data.
    // Unbalanced syntax and evaluation stay unknown. No command is executed to discover a variable or a path.
    private static List<string>? ShellSplit(string text, bool words = false)
    {
        var parts = new List<string>();
        int start = 0, depth = 0;
        char quote = '\0';
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\'' && i + 1 < text.Length &&
                (c == '`' || c == '\\' && text[i + 1] is '"' or '\'' or '\\' or '$')) { i++; continue; }
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if (c is '\'' or '"') { quote = c; continue; }
            if (c == '(') { depth++; continue; }
            if (c == ')') { if (--depth < 0) return null; continue; }
            if (depth != 0) continue;
            var separator = words ? char.IsWhiteSpace(c) : c is ';' or '\n' or '|' || c == '&' &&
                (i + 1 < text.Length && text[i + 1] == '&');
            if (!separator) continue;
            if (text[start..i].Trim() is { Length: > 0 } part) parts.Add(part);
            if (!words && i + 1 < text.Length && text[i + 1] == c && c is '|' or '&') i++;
            start = i + 1;
        }
        if (quote != '\0' || depth != 0) return null;
        if (text[start..].Trim() is { Length: > 0 } last) parts.Add(last);
        return parts;
    }

    private static bool HasEvaluation(string text)
    {
        char quote = '\0';
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote == '\'') { if (c == '\'') quote = '\0'; continue; }
            if (c == '\'' && quote == '\0') { quote = c; continue; }
            if (c == '"') { quote = quote == '"' ? '\0' : '"'; continue; }
            if (c == '`' || c == '$' && i + 1 < text.Length && text[i + 1] == '(') return true;
        }
        return false;
    }

    private static readonly Regex Assignment = new(@"^\$?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>""[^""]*""|'[^']*'|[^\s]+)(?<rest>\s+.*)?$", Ci | RegexOptions.Singleline);
    private static readonly Regex Variable = new(@"\$(?:\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}|(?<name>[A-Za-z_][A-Za-z0-9_]*))", RegexOptions.CultureInvariant);

    private static string Expand(string text, Dictionary<string, string> vars)
    {
        // Single-quoted arguments are literal in both shells. Preserve unknown variables so path identity cannot guess.
        return Regex.Replace(text, "'[^']*'|[^']+", m => m.Value.StartsWith('\'') ? m.Value :
            Variable.Replace(m.Value, v => vars.GetValueOrDefault(v.Groups["name"].Value) ?? v.Value));
    }

    private static List<(string Text, string? Cwd)>? ResolvedStages(string command, string? cwd)
    {
        var parts = ShellSplit(command);
        if (parts is null) return null;
        var result = new List<(string, string?)>();
        var vars = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            var stage = part;
            var assignment = Assignment.Match(stage);
            if (assignment.Success)
            {
                var value = assignment.Groups["value"].Value;
                var rest = assignment.Groups["rest"].Value.Trim();
                var literal = !HasEvaluation(value) && (!stage.StartsWith('$') || rest.Length == 0 &&
                    (value.StartsWith('"') || value.StartsWith('\'') || Regex.IsMatch(value, @"^\d+$")));
                var expanded = Expand(value, vars).Trim('"', '\'');
                if (literal && !expanded.Contains('$')) vars[assignment.Groups["name"].Value] = expanded;
                else vars.Remove(assignment.Groups["name"].Value);
                if (literal && rest.Length == 0) continue;
                if (literal) stage = rest; // Bash NAME=value command.
            }
            stage = Expand(stage, vars);
            var args = ShellSplit(stage, words: true);
            if (args is { Count: 2 } && args[0].ToLowerInvariant() is "cd" or "set-location")
            {
                cwd = WholeTranscript.PathKey(args[1].Trim('"', '\''), cwd);
            }
            result.Add((stage, cwd));
        }
        return result;
    }

    public static bool IsRun(WholeEv e) =>
        e.Kind == WholeKind.Call && (e.ReadsOwnResult || e.Tool is "Agent" or "Task" || (e.Tool is "Bash" or "PowerShell" && !e.ReadsStoredText && !IsReader(e.Input)));

    private static readonly Regex ResultFile = new(@"\.(?:log|trx|out|output|txt|xml)$", Ci);

    private static string? ResultPath(string value, string? cwd)
    {
        var path = value.Trim('"', '\'');
        return ResultFile.IsMatch(path) && path.IndexOfAny(new[] { '$', '`', '*', '?', '\r', '\n' }) < 0
            ? WholeTranscript.PathKey(path, cwd) : null;
    }

    /// <summary>Visible output destinations, with whether their producer ran a check. Reader overwrites
    /// invalidate provenance. This is lexical: no execution, filesystem reads, or guessed script outputs.</summary>
    internal static IEnumerable<(string Path, bool Run, bool Append)> ResultWrites(WholeEv e)
    {
        if (e.Tool is not ("Bash" or "PowerShell")) yield break;
        // The tool runner also assigns an output file when it backgrounds a command, even without >.
        // Bind that result to this call's classification, never to an arbitrary later filename mention.
        var background = Regex.Match(e.Output ?? "", @"(?:Command running in background with ID:|Command did not complete within)[\s\S]*?Output is being written to:\s*(?<p>[^\r\n]+?\.output)(?=\.\s|\r?\n|$)", Ci);
        if (background.Success && ResultPath(background.Groups["p"].Value, e.Cwd) is { } outputPath)
            yield return (outputPath, IsRun(e), false);
        var stages = ResolvedStages(e.Input, e.Cwd);
        if (stages is null) yield break;
        // Preserve the existing record/replay and known-script classification on the producer too:
        // a flag export is historical prose even though a Python helper writes its destination.
        var commandRun = IsRun(e);
        var previousRun = false;
        foreach (var (stage, cwd) in stages)
        {
            var args = ShellSplit(stage, words: true);
            if (args is null || args.Count == 0) continue;
            var tee = args[0].Equals("tee", StringComparison.OrdinalIgnoreCase) || args[0].Equals("Tee-Object", StringComparison.OrdinalIgnoreCase);
            var run = commandRun && (tee ? previousRun : !ReaderStage(stage));
            previousRun = run;
            for (var i = 0; i < args.Count; i++)
            {
                var arg = args[i];
                string? target = null;
                var append = tee && args.Any(a => a.Equals("-a", StringComparison.OrdinalIgnoreCase) || a.Equals("-Append", StringComparison.OrdinalIgnoreCase));
                if (Regex.Match(arg, @"^(?:[12*])?>>?(?<path>.*)$") is { Success: true } redirect)
                {
                    target = redirect.Groups["path"].Value is { Length: > 0 } attached ? attached : i + 1 < args.Count ? args[++i] : null;
                    append = arg.Contains(">>", StringComparison.Ordinal);
                }
                else if (arg.Equals("-RedirectStandardOutput", StringComparison.OrdinalIgnoreCase) || arg.Equals("-RedirectStandardError", StringComparison.OrdinalIgnoreCase))
                    target = i + 1 < args.Count ? args[++i] : null;
                else if (tee && i > 0 && !arg.StartsWith('-')) target = arg;
                if (target is not null && ResultPath(target, cwd) is { } path) yield return (path, run, append);
            }
            // A TRX basename is meaningful only with an explicit results directory; an absolute logger
            // destination needs none. Do not guess the project's default TestResults folder.
            var directory = Regex.Match(stage, @"--results-directory\s+(?<p>""[^""]+""|'[^']+'|[^\s]+)", Ci);
            foreach (Match trx in Regex.Matches(stage, @"\bLogFileName=(?<p>[^""';\r\n]+\.trx)\b", Ci))
            {
                var value = trx.Groups["p"].Value;
                var at = directory.Success ? WholeTranscript.PathKey(directory.Groups["p"].Value.Trim('"', '\''), cwd) : cwd;
                if ((Path.IsPathRooted(value) || directory.Success) && ResultPath(value, at) is { } path) yield return (path, run, false);
            }
        }
    }

    internal static bool ReadsOwnResult(WholeEv e, HashSet<string> results)
    {
        if (e.Tool == "Read") return results.Contains(WholeTranscript.PathKey(e.FilePath ?? e.Input, e.Cwd));
        if (e.Tool is not ("Bash" or "PowerShell")) return false;
        var stages = ResolvedStages(e.Input, e.Cwd);
        if (stages is null) return false;
        foreach (var (stage, cwd) in stages)
        {
            if (PythonFileText.Matches(stage).Any(m => ResultPath(m.Groups["file"].Value, cwd) is { } p && results.Contains(p))) return true;
            var nestedResult = false;
            ReaderSubstitutions(stage, inner => nestedResult |= ReadsOwnResult(e with { Input = inner, Cwd = cwd }, results));
            if (nestedResult) return true;
            var args = ShellSplit(stage, words: true);
            if (args is null || args.Count < 2 || !Regex.IsMatch(args[0], @"^(?:cat|type|head|tail|grep|rg|select-string|sls|get-content|gc)$", Ci)) continue;
            if (args.Skip(1).Any(a => ResultPath(a, cwd) is { } path && results.Contains(path))) return true;
        }
        return false;
    }

    internal static bool ReadsStoredResult(string tool, string command, string? cwd, HashSet<string> stored)
    {
        if (tool is not ("Bash" or "PowerShell")) return false;
        var stages = ResolvedStages(command, cwd);
        if (stages is null) return false;
        var source = false;
        foreach (var (stage, at) in stages)
        {
            var args = ShellSplit(stage, words: true);
            // Only an actual file-reader stage establishes the input, never the projection's destination.
            if (args is { Count: > 1 } && Regex.IsMatch(args[0], @"^(?:cat|type|head|tail|grep|rg|get-content|gc)$", Ci))
                source |= args.Skip(1).TakeWhile(a => !a.Contains('>')).Any(a => ResultPath(a, at) is { } p && stored.Contains(p));
            if (!ReaderStage(stage) && !(source && PythonProjection(stage))) return false;
        }
        return source;
    }

    internal static string ResultText(WholeEv e)
    {
        var output = e.Output ?? "";
        // A mixed command can read a git diff and then run something. Only the diff's marked
        // content is historical; a later unprefixed check result is still measured (C L3189).
        if (Regex.IsMatch(e.Input, @"\bgit\s+(?:show|diff)\b", Ci))
            output = Regex.Replace(output, @"(?m)^\+[^\r\n]*", "", Ci);
        var review = e.Tool is "Agent" or "Task" || Regex.IsMatch(e.Input, @"\bkor_review\.py[""']?\s+review\b", Ci);
        if (review)
        {
            // Explicit attribution of numbers in a document is not a new measurement. Do not
            // exempt reviews wholesale: their own findings (including unquoted deficits) remain.
            output = Regex.Replace(output, @"[^\r\n.!?]*(?:\b(?:brief|handover|response|document|block)\s+(?:says|states|cites|claims)\b|\bcould not check its numbers\b)[^\r\n.!?]*[.!?]?", "", Ci);
        }
        output = WithoutExplainedBaseline(output, e.Input);
        if (!e.ReadsOwnResult) return output;
        // TRX counters read from our own result file, not an XML example quoted in old documentation.
        return Regex.Replace(output, @"<Counters\b[^>]*>", m =>
        {
            var failed = Regex.Match(m.Value, @"\bfailed=""(?<n>\d+)""", Ci);
            var skipped = Regex.Match(m.Value, @"\bnotExecuted=""(?<n>\d+)""", Ci);
            return (failed.Success ? "\nFailed: " + failed.Groups["n"].Value : "") +
                (skipped.Success ? "\nSkipped: " + skipped.Groups["n"].Value : "") + "\n";
        }, Ci);
    }

    private static string WithoutExplainedBaseline(string output, string command)
    {
        // A same-output comparison by NAME can explain a runner's failures without manufacturing
        // one flag per repeated summary. Equal totals alone, a bare "baseline", or testdiff's
        // deficit summary without its evidence do not establish this relation.
        var comparison = Regex.Match(output, @"(?m)^\s*Baseline comparison: (?<n>\d+) of \k<n> failing names also in base; 0 only in new\s*$", Ci);
        var count = comparison.Success ? N(comparison.Groups["n"].Value) : -1;
        if (comparison.Success) output = output.Remove(comparison.Index, comparison.Length);
        if (count < 0 && Regex.IsMatch(command, @"\bcomm\s+-23\b", Ci) && Regex.IsMatch(command, @"\bcomm\s+-13\b", Ci))
        {
            // F L315/325: two counted failure-name lists followed by both EMPTY set differences.
            comparison = Regex.Match(output, @"(?m)^\s*(?<n>\d+)\s*\r?\n\s*\k<n>\s*\r?\n\s*in mine not base:\s*\r?\n\s*in base not mine:\s*(?=\r?\n(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun)\b|\r?\nShell cwd|\z)", Ci);
            if (comparison.Success) count = N(comparison.Groups["n"].Value);
        }
        if (count <= 0) return output;
        // Do not let one explained test population erase another count, a skip, or a geometry fault.
        return Regex.Replace(output, $@"\bFailed:\s*{count}\b", "", Ci);
    }

    private static readonly Regex SqlReaderCall = new(@"\bpython(?:\.exe)?\s+(?:-I\s+)?(?:tools[/\\]SqlQuery[/\\])?oq(?:wide)?\.py\s+[^;|&\r\n]+", Ci);
    private static readonly Regex SqlLiteralOrComment = new(@"N?'(?:''|[^'])*'|--[^\r\n]*|/\*[\s\S]*?\*/", Ci);
    private static readonly Regex SqlMutationOrAggregate = new(@"\b(?:INSERT|UPDATE|DELETE|MERGE|EXEC|EXECUTE|DROP|ALTER|CREATE|INTO)\b|\b(?:COUNT|SUM|AVG|MIN|MAX)\s*\(", Ci);

    /// <summary>A known SQL file or Python transcript reader, written successfully earlier in this transcript. Unknown files and
    /// mixed commands remain runs. No filesystem or database access: replay must use the script as it was at this call.</summary>
    internal static bool ReadsStoredText(string tool, string command, string? cwd, Func<string, string?> writtenFile)
    {
        if (tool is not ("Bash" or "PowerShell")) return false;
        var stages = ResolvedStages(command, cwd);
        if (stages is null) return false;
        var replaced = false;
        foreach (var (stage, at) in stages)
        {
            var readers = SqlReaderCall.Replace(stage, m =>
            {
                var arg = SqlFileArg.Match(m.Value);
                var path = arg.Success ? WholeTranscript.PathKey(arg.Groups[1].Value.Trim('"', '\''), at) : null;
                if (path is null || Regex.IsMatch(m.Value, @"\s-q\b", Ci) || writtenFile(path) is not { } sql || !StoredTextSql(sql)) return m.Value;
                replaced = true;
                return "cat stored-record-text";
            });
            // An on-disk transcript reader has the same provenance as its visible Python source. Only successful
            // writes in this transcript supply it; a later real program in the command must still be a reader.
            readers = Regex.Replace(readers, @"^python(?:\.exe)?\s+(?:-I\s+)?(?<file>""[^""]+\.py""|'[^']+\.py'|[^\s]+\.py)(?=\s|$)", m =>
            {
                var path = WholeTranscript.PathKey(m.Groups["file"].Value.Trim('"', '\''), at);
                if (writtenFile(path) is not { } source || !TranscriptReaderSource(source)) return m.Value;
                replaced = true;
                return "cat transcript-record-text";
            }, Ci);
            if (!IsReader(readers)) return false;
        }
        return replaced;
    }

    private static bool TranscriptReaderSource(string source) =>
        (source.Contains(".jsonl", StringComparison.Ordinal) || Regex.IsMatch(source, @"['""]tool_result['""]")) && source.Contains("json.loads(", StringComparison.Ordinal) &&
        source.Contains("open(", StringComparison.Ordinal) && Regex.IsMatch(source, @"['""]message['""]") &&
        // This is the existing transcript-search convention, not a Python verifier. Reject execution and writes
        // explicitly; computed/dynamic Python and semantic honesty remain outside what the gate can establish.
        !Regex.IsMatch(source, @"\b(?:subprocess|exec|eval|__import__)\b|(?<!re\.)\bcompile\b|\bos\.(?:system|popen|spawn\w*)\b|\.write\s*\(|open\([^\r\n]*,\s*['""][wax][bt+]*['""]", Ci);

    private static bool PythonProjection(string stage) =>
        Regex.IsMatch(stage, @"^python(?:\.exe)?\s+(?:-I\s+)?-c\s+", Ci) &&
        (stage.Contains("sys.stdin", StringComparison.Ordinal) || stage.Contains("json.loads(", StringComparison.Ordinal)) &&
        stage.Contains("print(", StringComparison.Ordinal) &&
        !Regex.IsMatch(stage, @"\b(?:subprocess|exec|eval|compile|__import__)\b|\bos\.(?:system|popen|spawn\w*)\b|\.write\s*\(", Ci);

    private static readonly Regex QuotedHeredoc = new(@"(?:^|[;\n]\s*)cat\s*>\s*(?<path>[^\r\n]+?)\s*<<'(?<end>[A-Za-z_]\w*)'\s*\r?\n(?<body>[\s\S]*?)\r?\n\k<end>(?=\r?\n|$)", RegexOptions.CultureInvariant);

    internal static IEnumerable<(string Path, string Body)> WrittenHeredocs(string command, string? cwd) =>
        QuotedHeredoc.Matches(command).Select(m => (WholeTranscript.PathKey(m.Groups["path"].Value.Trim('"', '\''), cwd), m.Groups["body"].Value));

    private static bool StoredTextSql(string sql)
    {
        var code = SqlLiteralOrComment.Replace(sql, " ");
        return !SqlMutationOrAggregate.IsMatch(code) && Regex.IsMatch(code, @"\bSELECT\b", Ci) &&
            Regex.IsMatch(code, @"\b(?:ResumeBlock|Evidence|Description|Matched|Note|Body|Says)\b", Ci) &&
            Regex.IsMatch(code, @"\b(?:FROM|JOIN)\s+(?:KorStandards\.)?(?:brain\.(?:LaneState|Memory|Event)|oversight\.(?:Commitment|GuardRefusal|Review))\b", Ci);
    }

    /// <summary>Successful literal printf SQL writes, including W1031's numeric-only ID list. Unknown interpolation
    /// invalidates provenance. This stores a classification template, never fabricated query results or executed SQL.</summary>
    internal static IEnumerable<(string Path, string? Sql)> PrintedSql(string command, string? cwd)
    {
        var stages = ResolvedStages(command, cwd);
        if (stages is null) yield break;
        // Only this bounded pipeline establishes a numeric CSV: grep extracts digits after '#', tr removes '#',
        // sort reorders them, paste joins with commas. The source before grep need not be trusted.
        var numeric = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (stage, at) in stages)
        {
            var assigned = Regex.Match(stage, @"^(?<name>[A-Za-z_]\w*)=");
            if (assigned.Success)
            {
                var name = assigned.Groups["name"].Value;
                numeric.Remove(name);
                if (Regex.IsMatch(stage, @"^[A-Za-z_]\w*=\$\([^\r\n]*\|\s*grep -oE [""']#\[0-9\]\{\d+\}[""']\s*\|\s*tr -d '#'\s*\|\s*sort -u\s*\|\s*paste -sd,\s*\)$")) numeric.Add(name);
            }
            var args = ShellSplit(stage, words: true);
            if (args is null || args.Count < 4 || args[0] != "printf") continue;
            var redirect = args.IndexOf(">");
            if (redirect < 2 || redirect + 2 != args.Count) continue;
            var path = WholeTranscript.PathKey(args[^1].Trim('"', '\''), at);
            if (!path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)) continue;
            var format = args[1];
            string? sql = null;
            if (format.Length >= 2 && format[0] is '\'' or '"' && format[^1] == format[0] && !HasEvaluation(format) && !format.Contains('$'))
            {
                format = format[1..^1].Replace("\\n", "\n");
                if (redirect == 2 && !format.Contains('%')) sql = format;
                else if (redirect == 3 && Regex.Matches(format, "%").Count == 1 &&
                    Regex.IsMatch(format, @"\bWHERE\s+Id\s+IN\s*\(%s\)", Ci) &&
                    Regex.Match(args[2], @"^""?\$(?<name>[A-Za-z_]\w*)""?$") is { Success: true } variable &&
                    numeric.Contains(variable.Groups["name"].Value)) sql = format.Replace("%s", "0");
            }
            yield return (path, sql is not null && StoredTextSql(sql) ? sql : null);
        }
    }

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
        (new Regex(@"\b(?:Get-Content|gc)\b[^;|\r\n]*\s-(?:Tail|TotalCount)\b", Ci), "Get-Content limit"),
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

    internal static HashSet<string> TrxSourcesOf(WholeEv e)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stages = ResolvedStages(e.Input, e.Cwd);
        if (stages is null) return paths;
        foreach (var (stage, cwd) in stages)
        {
            var args = ShellSplit(stage, words: true);
            if (args is null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var arg in args)
            {
                var p = arg.Trim('"', '\'');
                if (!p.EndsWith(".trx", StringComparison.OrdinalIgnoreCase)) continue;
                if (p.IndexOfAny(new[] { '$', '`', '\n', '\r' }) >= 0) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                paths.Add(WholeTranscript.PathKey(p, cwd));
            }
        }
        return paths;
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

    // This is narrower than IsReader, which classifies OUTPUT provenance and historically includes
    // copies/moves/git merges. Recovery is about whether this stage can itself spend or close work.
    private static bool RecoveryStage(string stage)
    {
        stage = stage.Trim();
        if (stage.StartsWith("& ", StringComparison.Ordinal)) stage = stage[2..].TrimStart();
        if (ReaderSubstitutions(stage) is not { } expanded) return false;
        expanded = PythonFileText.Replace(expanded, "cat file-text");
        var args = ShellSplit(expanded, words: true);
        if (args is null || args.Count == 0) return false;
        var program = Path.GetFileName(args[0].Trim('"', '\'').Replace('\\', '/'));
        if (program.Equals("kor", StringComparison.OrdinalIgnoreCase) || program.Equals("kor.exe", StringComparison.OrdinalIgnoreCase))
            return args.Count > 1 && Regex.IsMatch(args[1], @"^(?:record|state|recall|read|start|memory|events|checks|start-docs)$", Ci);
        if (program.Equals("git", StringComparison.OrdinalIgnoreCase))
        {
            var i = 1;
            while (i < args.Count && args[i].StartsWith('-'))
            {
                if (args[i] is "-C" or "-c" or "--git-dir" or "--work-tree") i += 2;
                else if (Regex.IsMatch(args[i], @"^(?:--(?:no-pager|paginate|literal-pathspecs)|--(?:git-dir|work-tree)=.+)$", Ci)) i++;
                else return false;
            }
            return i < args.Count && Regex.IsMatch(args[i], @"^(?:status|log|show|diff|rev-parse|merge-base|ls-remote|ls-files|ls-tree|cat-file|grep)$", Ci);
        }
        return Regex.IsMatch(program, @"^(?:cat|type|head|tail|grep|egrep|fgrep|rg|findstr|select-string|sls|sed|ls|dir|get-childitem|gci|get-content|gc|echo|printf|write-host|write-output|wc|sort|uniq|cut|tr|stat|date|pwd|which|where|where-object|select-object|foreach-object|format-table|format-list|measure-object|tasklist|cd|set-location|jq|file|du|df|basename|dirname|realpath|get-item|test-path|resolve-path|true|false|awk|column|nl|less|more)$", Ci);
    }

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
                var stages = ResolvedStages(Regex.Replace(blanked, @"(?m)^\s*#[^\r\n]*", ""), null);
                if (stages is { Count: > 0 } && stages.All(s => RecoveryStage(s.Text))) return Door.None;
                var sql = SqlFileArg.Match(input) is { Success: true } f ? readFile(f.Groups[1].Value.Trim('"', '\'')) : null;
                var both = input + (sql is null ? "" : "\n" + sql);
                var board = BoardRe.IsMatch(both) && Regex.IsMatch(both, @"\b(?:UPDATE|INSERT)\b", Ci);
                var close = (board && CloseRe.IsMatch(both)) || Regex.IsMatch(input, @"kor_task\.py""?\s+accept\b", Ci);
                var step = stages is null ? !CommitRe.IsMatch(blanked) && StepRe.IsMatch(blanked) :
                    stages.Any(s => !RecoveryStage(s.Text) && !CommitRe.IsMatch(s.Text) &&
                        (StepRe.IsMatch(s.Text) || s.Cwd is not null && StepRe.IsMatch(s.Cwd)));
                if (board) return new Door(DoorKind.Board, both, true, step, close);
                // Saving the work is always available, including output-file pipelines. A joined
                // expensive step or board/task close still gets judged on its own operation.
                if (!step && !close && Regex.IsMatch(blanked, @"\bkor(?:\.exe)?[""']?\s+(?:record|state)\b", Ci)) return Door.None;
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
        public HashSet<string> Sources { get; init; } = new(StringComparer.Ordinal);
        public HashSet<string> TrxSources { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public bool Brief { get; init; }   // a hole this session WROTE into its own brief: it holds for the whole session under W1
        public bool Accounted, Opened, Superseded, NotListable;
        public bool Forgiven;  // replay only: the session is taken to have complied after its first refusal (see Comply)
        public int MostLook;   // the most non-empty lines of an uncut look since the flag
        public bool Discharged => Opened || Superseded || NotListable || Forgiven;
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
        _narration.Clear();
        // a call another hook refused never ran: its result is that hook's text (records it named, rules it quoted), not a check's
        if (e.Output is not null && (e.Output.StartsWith("PreToolUse:", StringComparison.Ordinal) || e.Output.StartsWith("PostToolUse:", StringComparison.Ordinal))) return;
        Account(e.Input, e.Line);
        foreach (var h in WholeGate.BriefHoles(e)) _flags.Add(new Flag { Line = e.Line, CallNo = _calls, R = h, Brief = true });
        if (e.Output is null) return;
        // an output that carries this gate's own refusal (a proof run, a dry run, a self-check) quotes ledgers, it reports no check
        if (e.Output.Contains("kor whole (board row 2139", StringComparison.Ordinal)) return;
        var cut = WholeGate.IsCut(e);
        if (!cut && e.Tool is "Bash" or "PowerShell" or "Read")
        {
            var lines = e.Output.Split('\n').Count(l => l.Trim().Length > 0);
            foreach (var f in _flags) if (f.Line < e.Line && lines > f.MostLook) f.MostLook = lines;
        }
        if (WholeGate.IsRun(e))
        {
            var sources = WholeGate.SourcesOf(e);
            var trxSources = WholeGate.TrxSourcesOf(e);
            var review = e.Tool is "Agent" or "Task" || Regex.IsMatch(e.Input, @"\bkor_review\.py[""']?\s+review\b", RegexOptions.IgnoreCase);
            foreach (var r in WholeGate.Readings(WholeGate.ResultText(e), review))
            {
                if (r.Flagged > 0) { _flags.Add(new Flag { Line = e.Line, CallNo = _calls, R = r, Sources = sources, TrxSources = trxSources }); continue; }
                foreach (var f in _flags)
                    if (!f.Superseded && !f.R.Hole && f.R.Label == r.Label && f.R.Of == r.Of) f.Superseded = true;
            }
            // An uncut differential listing has already opened each failure. A summary alone, a partial list, a
            // NEW failure, or unrelated source cannot provide this evidence. Count the distinct identities, not filler lines.
            if (!cut && trxSources.Count > 0 && ListedBaselineFailures(e.Output) is { } count)
                foreach (var f in _flags)
                    if (!f.R.Hole && f.R.Flagged == count && FailureLabel.IsMatch(f.R.Label) &&
                        trxSources.SetEquals(f.TrxSources))
                    { f.Accounted = true; f.Opened = true; }
        }
        if (e.Tool is "Bash" or "PowerShell" or "Read" or "Grep")
            _seen.Add((e.Line, e.Output, cut, WholeGate.SourcesOf(e)));
        // PreToolUse cannot credit a look that has not run. Once its result is in the transcript,
        // its own accounting can use that result immediately, without a new assistant turn.
        Account(e.Input, e.Line);
    }

    private const RegexOptions AccountOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex OpenedLine = new(@"(?<!NOT )\bOPENED\s+(?<n>\d[\d,]*)\s+of\s+(?<m>\d[\d,]*)", AccountOptions);
    private static readonly Regex ScopeDisclosure = new(@"\b(?:not checked|not opened|not looked at|not graded|out of scope|not in scope)\b", AccountOptions);
    private static readonly Regex BaselineSummary = new(@"^\s*(?<n>\d+) failing (?:names|cases) in new;\s*(?<b>\d+) also failing in base;\s*0 only in new\s*$", AccountOptions | RegexOptions.Multiline);
    private static readonly Regex FailureLabel = new(@"\b(?:fail|failed|failing|failures)\b", AccountOptions);

    private static int? ListedBaselineFailures(string output)
    {
        var summary = BaselineSummary.Match(output);
        if (!summary.Success) return null;
        var n = Int(summary.Groups["n"].Value);
        if (n <= 0 || n != Int(summary.Groups["b"].Value)) return null;
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("NEW ", StringComparison.Ordinal)) return null;
            if (line.StartsWith("BOTH ", StringComparison.Ordinal)) identities.Add(line[5..].Trim());
        }
        return identities.Count == n && !identities.Contains("") ? n : null;
    }
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "them", "they", "from", "into", "only", "once", "list", "text", "cover", "covers",
        "step", "scope", "out", "not", "checked", "graded", "looked", "opened", "unproven", "codex", "each", "every", "item", "items",
    };

    /// <summary>Every accounting line in a text marks the flags it names (OPENED / NOT OPENED / NOT CHECKED).</summary>
    private void Account(string text, int line)
    {
        if (text.Length == 0 || (!text.Contains("OPENED", StringComparison.OrdinalIgnoreCase) && !ScopeDisclosure.IsMatch(text) &&
                                !text.Contains(" of ", StringComparison.OrdinalIgnoreCase) && !text.Contains("NOT LISTABLE:", StringComparison.OrdinalIgnoreCase))) return;
        foreach (var raw in text.Split('\n'))
        {
            var l = WholeGate.Squash(raw);
            var notOpened = l.Contains("NOT OPENED", StringComparison.OrdinalIgnoreCase);
            var notChecked = ScopeDisclosure.IsMatch(l);
            var opened = OpenedLine.Match(l);
            var openedHole = !notOpened && Regex.IsMatch(l, @"(?<!NOT )\bOPENED\b", AccountOptions);
            var notListable = Regex.Match(l, @"\bNOT LISTABLE:\s*(?<key>.+?)\s+-\s+(?<why>.+)", AccountOptions);
            var counts = l.Contains(" of ", StringComparison.OrdinalIgnoreCase)
                ? Regex.Split(l.Replace("**", "", StringComparison.Ordinal), @"(?<=[.!?])\s+").SelectMany(s => WholeGate.Readings(s))
                    .Where(r => !r.Hole && r.Of > 0 && r.Flagged > 0).ToList() : new List<WholeGate.Reading>();
            if (!notOpened && !notChecked && !opened.Success && !openedHole && counts.Count == 0 && !notListable.Success) continue;
            foreach (var f in _flags)
            {
                if (f.Line >= line || f.Discharged) continue;
                if (notListable.Success && !f.R.Hole &&
                    notListable.Groups["key"].Value.Equals(f.R.Key, StringComparison.OrdinalIgnoreCase) &&
                    HasPopulationLook(f, notListable.Groups["why"].Value))
                { f.Accounted = true; f.NotListable = true; continue; }
                // An asserted OPENED still needs its look; its copied deficit key cannot downgrade that assertion
                // to mere disclosure when the opening proof is absent.
                var population = f.R.Of > 0 ? f.R.Of : f.R.Population;
                if (!openedHole && !opened.Success && !f.R.Hole && population > 0 &&
                    counts.Any(r => r.Of == population && r.Flagged == f.R.Flagged && SameSubject(f.R, r))) f.Accounted = true;
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

    private bool HasPopulationLook(Flag flag, string why)
    {
        var named = WholeGate.SourcesOf(new WholeEv(0, WholeKind.Call, "Read", why, null, null, false));
        if (named.Count == 0) return false;
        foreach (var look in _seen)
        {
            if (look.Line <= flag.Line || look.Cut || !look.Sources.Overlaps(named)) continue;
            // The same call must print this population's total AND its enumerable part. For the
            // observed mesh format, count distinct numbered records, not unrelated filler.
            var subject = Regex.Escape(flag.R.Label);
            var total = Regex.Match(look.Output, $@"\b{subject}:\s*count\s+(?<n>\d+)\b", AccountOptions);
            if (!total.Success || Int(total.Groups["n"].Value) != flag.R.Flagged) continue;
            var listable = Regex.Match(look.Output, @"\b(?:listable:\s*|pieces\s*>\s*1e-9\s+)(?<n>\d+)\b", AccountOptions);
            if (!listable.Success) continue;
            var n = Int(listable.Groups["n"].Value);
            if (n < 0 || n >= flag.R.Flagged) continue;
            var rows = Regex.Matches(look.Output, @"(?m)^\s*(?<id>\d+)(?:\s+\{[^\r\n]+\}|[.:)]\s+\S[^\r\n]*)\s*$")
                .Select(m => Int(m.Groups["id"].Value)).ToList();
            if (rows.Count == n && rows.Distinct().Count() == n && rows.All(i => i >= 1 && i <= n)) return true;
        }
        return false;
    }

    private static bool SameSubject(WholeGate.Reading flag, WholeGate.Reading disclosure)
    {
        // Numeric coincidence is not disclosure. Keep distinctive words on both sides; the runner's unlabeled
        // Failed/Passed/Total summary can still be stated as "817 of 819 green" (A2's real correction).
        HashSet<string> Subject(WholeGate.Reading reading)
        {
            var label = reading.Label;
            var ratio = Regex.Match(reading.Key, @"\d[\d,]*\s+of\s+\d[\d,]*", AccountOptions);
            if (ratio.Success)
            {
                var after = reading.Key[(ratio.Index + ratio.Length)..];
                // "passed 45 of 46 checks: 22 sessions ..." names its population before the
                // extra timing/run counts. Those trailing words do not rename the test subject.
                if (Regex.Match(after, @"^\s+(?:checks|tests|cases|items)\b", AccountOptions) is { Success: true } unit)
                    after = after[..unit.Length];
                var polarity = Regex.Matches(after, @"[A-Za-z][A-Za-z0-9_-]*")
                    .FirstOrDefault(m => WholeGate.Pass.Contains(m.Value) || WholeGate.Fail.Contains(m.Value));
                if (polarity is not null) after = after[..(polarity.Index + polarity.Length)];
                label = reading.Key[..ratio.Index] + " " + after;
            }
            return Regex.Matches(label, @"[A-Za-z0-9]+(?:[-_][A-Za-z0-9]+)*")
            .Select(m => m.Value).Where(w => !WholeGate.Pass.Contains(w) && !WholeGate.Fail.Contains(w) &&
                !Common.Contains(w) && !Regex.IsMatch(w, @"^(?:of|in|new|base|test|tests|self-test|names|cases|coverage|check|checks|verdict|no|\d+)$", AccountOptions))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        return Subject(flag).SetEquals(Subject(disclosure));
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
        var before = _flags.Select(f => (f.Accounted, f.Opened, f.NotListable)).ToList();
        Account(door.Text, int.MaxValue);
        var v = Judge(door);
        if (v.Refuse)
            for (var i = 0; i < before.Count; i++) { _flags[i].Accounted = before[i].Accounted; _flags[i].Opened = before[i].Opened; _flags[i].NotListable = before[i].NotListable; }
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
                sb.Append("  Open each (an uncut look that shows them, then `OPENED n of n: <key> - <what they are>`), or fix and re-run the check so it prints no deficit. For an unlistable remainder, run an uncut look printing `<label>: count N; listable: M` and records numbered 1..M, then `NOT LISTABLE: <key> - <why, naming the inspected source file>`. Read-only looks and kor record/state remain available.\n");
            }
        }

        // W1: every flag of this turn and every hole of the session accounted for
        if (door.Account)
        {
            // this turn's flags (when the turn's prompt lies before the tail read, the last Window calls stand for it - review 2583);
            // a hole this session wrote into its own brief, for the whole session; a hole a check printed, over the last Window calls
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

    /// <summary>A numeric value range: "3.1-3.4", "3.1 to 3.4", "between 2.1 and 4.3", "5% - 10%", "3 kPa - 4 kPa" - a decimal or a
    /// unit on its first end, so labels ("L03-L05") and clock times ("07:35-07:5x") are not ranges.</summary>
    private static readonly Regex NumericRange = new(@"(?<![\w:.])(?<a>\d+\.\d+|\d+(?=\s*(?:in|mm|ft|kPa|psf|MPa|%)(?![A-Za-z])))\s*(?:in|mm|ft|kPa|psf|MPa|%)?\s*(?:-|–|—|to|and)\s*(?<b>\d+(?:\.\d+)?)(?![\w:])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>W4: the cut output (its index in the seen list) holding <paramref name="y"/> as a bare count AND both range ends
    /// <paramref name="a"/>, <paramref name="b"/> among its values, with no later uncut call over the same file; else null. Looked up by
    /// index, not by transcript line: two calls of one message can share a line (review 2584).</summary>
    private (int Line, bool Cut)? SampledOnly(string y, string a, string b)
    {
        if (SampledIndex(y) is not { } i) return null;
        var shown = _seen[i].Output;
        bool Has(string v) => Regex.IsMatch(shown, $@"(?<![\d.]){Regex.Escape(v)}(?![\d])");
        return Has(a) && Has(b) ? (_seen[i].Line, true) : null;
    }

    /// <summary>The index of the latest output holding <paramref name="y"/> as a bare count, when it was cut and no later uncut call read
    /// the same file; else null.</summary>
    private int? SampledIndex(string y)
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
            return i;
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
