using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kor.Operations.Framework.Recall;

/// <summary>
/// Board row 1998 (RESET item 1; Ian 10 Nov 12:3x: "This is THE issue. You repeat and duplicate and triplicate work."): a measurement,
/// analysis or audit whose requested subject already has a recorded result is refused unless the moment says what input changed since.
/// It is the hook <c>kor repeat --hook</c> (PreToolUse) on the recall hook's own BM25 index file (<see cref="BrainRecall"/>); nothing new is read.
///
/// WHEN IT LOOKS. Only at the START of a piece of work, read off the tool's own statement of intent (<see cref="Intent"/>): a Bash or
/// PowerShell call's description; an Agent call's description; a Write that CREATES a file (its name and markdown title - a brief, a plan,
/// an analysis doc). Never a prompt, a message, an edit or a write over an existing file; never a description that opens by searching,
/// reading or recording earlier work (<see cref="NotWork"/>). And only when the intent carries a work word (<see cref="IsWork"/>: measure,
/// analysis, audit, breakdown or "break X down", survey, census, investigate, characterise, tally, "read X off the sheets").
///
/// WHAT REFUSES. A finding or dead end - never a decision, lesson, legacy project memory or board row - at score <see cref="Floor"/>
/// or more that shares at least <see cref="TopicWordsHit"/> intent words AND names the requested subject in its name/lead. The body
/// and command still rank candidates, but incidental mentions there and shared job identifiers cannot establish the question answered.
/// <see cref="Subject"/> reads a bounded lexical subject, not a semantic proof: "slab thickness" differs from "slab deflection".
/// Explicit job/set identifiers in the question must also occur in the result's name/lead; identifiers never supply its subject.
/// A work word used only as a modifier ("measurement cases") does not start a measurement. At most three results are displayed.
///
/// HOW IT IS LIFTED. The moment's text (description, command, document or prompt) says "inputs changed since #N: what" for one of the
/// relevant result records, even outside the top three: then it goes ahead and the nonempty reason is logged
/// (oversight.GuardRefusal, guard <see cref="ReasonGuardName"/>). A record the
/// author reads and finds answers the question is the point: use it, do not redo it.
///
/// WHAT IT DOES NOT DO. It does not compare inputs itself (data, code, period) - the author states them; a false "inputs changed" goes
/// through, logged. It sees only what is recorded in brain.Memory: an audit that lives only as files (docs/codex) is not seen, unless a
/// record names it (mistake 145's prior audits were files; the records it finds there are the Standard Details plans that name Codex
/// audits). A repeat phrased with no work word ("rerun where_week.py") is not looked at. A result recorded with none of the intent's
/// words is missed (no synonyms, as <see cref="BrainRecall"/>). A result whose subject is only in its body, not its name/lead, can be
/// missed too. It does not infer a changed input from a command's filesystem operations, or prove equal job/data/code/period scope.
/// </summary>
public static class RepeatGate
{
    public const string GuardName = "kor-repeat";
    public const string ReasonGuardName = "kor-repeat-reason";

    /// <summary>Set 10 Nov: the actual repeat (19:25:41Z, #129845) scores 32.9 on its moment; the replay's wrong refusals scored 26.7-75.6,
    /// so the floor does not separate them - the shared intent words do most of the work (RepeatGateTests, row 1998 evidence).</summary>
    public const double Floor = 20.0;
    public const int TopicWordsHit = 3;
    public const int MostIntentChars = 400;
    public static readonly string[] ResultKinds = { "finding", "dead-end" };
    private static readonly string[] WorkStems = { "measur", "analy", "audit", "breakdown", "survey", "census", "investigat", "characteri", "tally", "talli" };

    /// <summary>Reading a value off the drawings is this office's measurement (10 Nov: OverLORD asked S11 to read slab f'c and the S111
    /// load table, already read rows). "read" alone is every second tool call, so only "read ... off/from the sheet(s)/drawing(s)/PDF/set".</summary>
    private static readonly Regex ReadOff = new(@"(?i)\bread(?:s|ing)?\b.{0,80}?\b(?:off|from)\b.{0,40}?\b(?:sheets?|drawings?|pdfs?|set|schedules?)\b", RegexOptions.CultureInvariant);
    private const string ReadOffWork = "read-off";

    /// <summary>"break this week's tokens down" (10 Nov 19:25Z, framework-84's re-run of #129845's measurement) is a breakdown.</summary>
    private static readonly Regex BreakDown = new(@"(?i)\bbreak(?:s|ing)?\b(?:\s+\S+){0,6}?\s+down\b", RegexOptions.CultureInvariant);

    private static HashSet<string> WorkIn(string intent)
    {
        var w = BrainRecall.Words(intent).Where(IsWork).ToHashSet(StringComparer.Ordinal);
        if (ReadOff.IsMatch(intent)) w.Add(ReadOffWork);
        if (BreakDown.IsMatch(intent)) w.Add("breakdown");
        return w;
    }

    private static readonly Regex Changed = new(@"(?i)inputs?\s+changed\s+since\s+(?:record\s+)?#?\s*(\d+)\s*:?\s*(.{0,300})", RegexOptions.CultureInvariant);

    private static readonly Regex ChangedMarker = new(@"(?i)inputs?\s+changed\s+since\s+(?:record\s+)?#?\s*\d+\s*:?", RegexOptions.CultureInvariant);
    private static readonly Regex SubjectEnd = new(@"(?i)\b(?:on|in|at|against|by|per|from|for|with|versus|vs)\b|\band\s+(?:read|draw|run|build)\b|[;()]", RegexOptions.CultureInvariant);

    // Use the gate's existing work vocabulary, not a second list that silently drops noun/inflected forms.
    private static Match[] WorkTokens(string text) => Regex.Matches(text, @"\b[\p{L}]+\b")
        .Where(m => IsWork(BrainRecall.Stem(m.Value))).ToArray();

    private static string QuestionText(string intent)
    {
        // A suffix explanation must not change the question it purports to lift. A prefix can contain the work itself.
        var marker = ChangedMarker.Match(intent);
        return marker.Success && HasWork(intent[..marker.Index])
            ? intent[..marker.Index] : ChangedMarker.Replace(intent, "");
    }

    /// <summary>The thing being measured, not its job/set identifiers or a command's implementation details. Keep quantity words
    /// (including SQL's stop word "top"). Scope and breakdown dimensions follow the subject. For a noun title ("slab thickness
    /// analysis") use its preceding words; a filename such as README remains part of that document's subject.
    /// Deliberately conservative: an unrecognised grammatical form cannot establish a repeat.</summary>
    private static HashSet<string> Subject(string intent)
    {
        var text = QuestionText(intent);
        var split = BreakDown.Match(text);
        string phrase;
        var audit = false;
        if (split.Success)
            phrase = Regex.Replace(split.Value, @"(?i)^break(?:s|ing)?\s+|\s+down$", "");
        else
        {
            var matches = WorkTokens(text);
            if (matches.Length == 0)
            {
                var read = ReadOff.Match(text);
                return read.Success
                    ? MeasuredWords(Regex.Match(read.Value, @"(?i)^read(?:s|ing)?\s+(.*?)\s+(?:off|from)\b").Groups[1].Value)
                    : new(StringComparer.Ordinal);
            }
            var work = matches[^1];
            audit = work.Value.StartsWith("audit", StringComparison.OrdinalIgnoreCase);
            var before = Regex.Replace(text[..work.Index], @"(?i)^\s*(?:run|rerun|re-run)\s+", "").Trim();
            var after = text[(work.Index + work.Length)..].TrimStart(' ', ':', '-');
            // "SAFE analysis model" describes the type of an artifact, not an analysis being commissioned.
            // Keep "slab thickness analysis" (including a new document with that title) under the gate.
            if (work.Value.Equals("analysis", StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(after, @"(?i)^model\b")) return new(StringComparer.Ordinal);
            var measurement = work.Value.StartsWith("measurement", StringComparison.OrdinalIgnoreCase);
            if (measurement && Regex.IsMatch(after, @"(?i)^(?:cases?|tests?|runs?)\b")) return new(StringComparer.Ordinal);
            var nominal = Regex.IsMatch(work.Value, @"(?i)(?:ments?|tions?|sions?|sis)$");
            // A noun may precede its object ("analysis of thickness") or follow it ("thickness measurement on job X").
            // Keep that distinction before removing the leading preposition from a verb's object ("audit on bundle X").
            var suffix = nominal && before.Length > 0 && SubjectEnd.Match(after) is { Success: true, Index: 0 };
            // "audit on the bundle" has its object after the preposition; "measure X on job Y" ends before it.
            after = Regex.Replace(after, @"(?i)^(?:of|on)\s+", "");
            var end = SubjectEnd.Match(after);
            phrase = suffix ? before : end.Success ? after[..end.Index] : after;
            if (string.IsNullOrWhiteSpace(phrase)) phrase = before;
        }
        var words = MeasuredWords(phrase);
        // An audit of a bundle is not a repeat of every finding about the bundle's contents.
        if (audit && words.Count > 0) words.Add("audit");
        return words;
    }

    private static HashSet<string> MeasuredWords(string text) => SubjectWords(text)
        .Where(w => !IsWork(w) && !w.Any(char.IsDigit)).ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> SubjectWords(string text)
    {
        var words = BrainRecall.Words(text).ToHashSet(StringComparer.Ordinal);
        if (Regex.IsMatch(text, @"(?i)\btop(?:s)?\b")) words.Add("top");
        return words;
    }

    private static bool HoldsSubject(BrainRecall.Doc doc, HashSet<string> subject, bool lifting = false)
    {
        var words = SubjectWords(doc.Name + " " + doc.Lead);
        // A changed-input record may describe an input to the audit, rather than the audit's own outcome.
        return subject.Count > 0 && subject.All(w => (lifting && w == "audit") || words.Contains(w));
    }

    public sealed record Verdict(bool Refuse, string? Reason, int? AllowedFor, string? AllowedBecause, IReadOnlyList<BrainRecall.Ranked> Named);

    public static bool HasWork(string intent) => WorkIn(intent).Count > 0;

    /// <summary>A description that opens with one of these is not the start of a measurement: it searches, reads or records what exists,
    /// or files work on the board.</summary>
    private static readonly HashSet<string> NotWork = new(StringComparer.Ordinal)
    {
        "search", "find", "look", "read", "list", "show", "grep", "open", "view", "check", "verify", "confirm", "record", "add", "commit", "launch",
        "start", "stop", "update", "set", "mark", "copy", "move", "fix", "review", "recall", "query", "count",
    };

    /// <summary>The NotWork verbs as index stems: words any call carries, so they never count as a shared TOPIC word (row 2042).</summary>
    private static readonly HashSet<string> CommonStems = BrainRecall.Words(string.Join(" ", NotWork)).ToHashSet(StringComparer.Ordinal);

    public const int MostRankChars = 1_000;

    /// <summary>The text a moment is RANKED on: a new file's name and first line (its intent; on a whole brief every big record scores over
    /// 100); a command's or helper's whole moment text - description first, then the command or prompt - cut at <see cref="MostRankChars"/>,
    /// as the recall hook ranks it (10 Nov 19:25Z: "break this week's tokens down by session, kind and model" shares one word with #129845;
    /// with its command, the hook's trap A, 32.7).</summary>
    public static string RankText(string? tool, string intent, string? moment) =>
        tool is "Write" || string.IsNullOrEmpty(moment) ? intent : moment.Length > MostRankChars ? moment[..MostRankChars] : moment;

    /// <summary>A stem that names a kind of work this gate looks at (prefix: measur covers measure, measured, measurement).</summary>
    public static bool IsWork(string stem) => WorkStems.Any(w => stem.StartsWith(w, StringComparison.Ordinal));

    /// <summary>The tool's statement of intent, or null when this moment is not the start of a piece of work.
    /// <paramref name="exists"/>: whether a Write's file is already there (a rewrite is not a new piece of work).</summary>
    public static string? Intent(string? tool, JsonElement input, Func<string, bool> exists)
    {
        string? S(string n) => input.ValueKind == JsonValueKind.Object && input.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static string FirstLine(string? t) =>
            (t ?? "").Split('\n').Select(l => l.Trim().TrimStart('#', '-', '*', ' ').Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        static string Heading(string? t) =>
            (t ?? "").Split('\n').Take(5).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith('#'))?.TrimStart('#').Trim() ?? "";
        var intent = tool switch
        {
            "Bash" or "PowerShell" => S("description") ?? "",
            "Agent" or "Task" => S("description") ?? FirstLine(S("prompt")),
            // a new file: its NAME and its markdown title, never its first prose line (a lane brief opens with boilerplate, replay 10 Nov)
            "Write" when S("file_path") is { Length: > 0 } p && !exists(p) =>
                Path.GetFileNameWithoutExtension(p).Replace('-', ' ').Replace('_', ' ') + "\n" + Heading(S("content")),
            _ => "",
        };
        intent = Regex.Replace(intent.Trim(), @"^~?\s*\d+(?:\.\d+)?\s*(?:s|sec|min|mins|minutes?|h|hours?)\b\s*:?\s*", "", RegexOptions.IgnoreCase).Trim();
        if (intent.Length == 0) return null;
        // looking for, reading or recording earlier work is the OPPOSITE of repeating it (replay 10 Nov: 9 of 32 refusals were these)
        var verb = Regex.Match(intent, @"^[A-Za-z]+").Value.ToLowerInvariant();
        if (tool is not "Write" && NotWork.Contains(verb)) return null;
        if (intent.Length > MostIntentChars) intent = intent[..MostIntentChars];
        return WorkIn(intent).Count > 0 ? intent : null;
    }

    /// <summary>The verdict for one intent, ranked on the index; <paramref name="moment"/> is the whole text the "inputs changed" line is
    /// looked for in.</summary>
    public static Verdict Decide(string intent, string moment, BrainRecall.Index ix, string? rankText = null)
    {
        var none = new Verdict(false, null, null, null, Array.Empty<BrainRecall.Ranked>());
        var work = WorkIn(intent);
        if (work.Count == 0) return none;
        // a finding or dead end IS a result (its text need not say "measured"); it must share TopicWordsHit words of the intent itself -
        // a command's or prompt's other words rank it, but alone they matched job names and boilerplate (replay 10 Nov)
        // row 2042: nor the common verbs of NotWork (check, read, count, ...): 10 Nov 23:59 "Spot-check census counts and read its outline" was
        // refused on #131677 (migration 159) sharing only check/count/read, and 23:54 "Append scratch scripts to the census doc and read it back"
        // on #131210 sharing scratch/script/read - neither a repeat (GuardRefusal 5591, 5587; record #132289)
        var subject = Subject(intent);
        if (subject.Count == 0) return none;
        var question = BrainRecall.Words(QuestionText(intent));
        var scope = question.Where(BrainRecall.Names).ToHashSet(StringComparer.Ordinal);
        var own = question.Where(w => !IsWork(w) && !CommonStems.Contains(w)).ToHashSet(StringComparer.Ordinal);
        var named = BrainRecall.Rank(rankText ?? intent, ix)
            .Where(r => !r.Doc.Board && ResultKinds.Contains(r.Doc.Kind) && r.Score >= Floor && r.Hit.Count(own.Contains) >= TopicWordsHit
                && HoldsSubject(r.Doc, subject) && scope.IsSubsetOf(SubjectWords(r.Doc.Name + " " + r.Doc.Lead)))
            .Take(3).ToList();
        foreach (Match m in Changed.Matches(moment))
        {
            if (!int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) continue;
            var reason = m.Groups[2].Value.Trim();
            if (reason.Length > 0 && ix.Docs.Any(d => d.Id == id && !d.Board && ResultKinds.Contains(d.Kind) && HoldsSubject(d, subject, lifting: true)))
                return new Verdict(false, null, id, reason, named);
        }
        if (named.Count == 0) return none;
        var sb = new StringBuilder();
        sb.Append($"kor recall (board row 1998, no duplicate work): this starts a {string.Join("/", work)} whose topic already has a recorded result - read it first:\n");
        foreach (var r in named)
            sb.Append(BrainRecall.Line(r, 300)).Append($" (score {r.Score.ToString("0.0", CultureInfo.InvariantCulture)}, shared: {string.Join(" ", r.Hit.Take(10))})\n");
        sb.Append($"If it answers the question, use it; do not redo the work. If an input changed since (new data, new code, a new period, a question it did not answer), " +
                  $"say so in this call - in its description, the document's first lines or the prompt: \"inputs changed since #{named[0].Doc.Id}: <what changed>\" - and run it again; " +
                  "that line is logged. Show a record's whole text: kor memory, or kor recall \"<its name>\".");
        return new Verdict(true, sb.ToString(), null, null, named);
    }
}
