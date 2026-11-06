using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kor.Operations.Framework.Start;

/// <summary>
/// Board rows 1569 and 1861: <c>kor start-gate</c> on PreToolUse. A session's first work tool is refused until its own transcript shows
/// every memory page it was meant to hold, all of one subject's bodies OR a read that found its subject truly in no body, and then the
/// session's own sentence saying so with those numbers. It reads what was DELIVERED (the session's transcript), not what was wired, so a
/// page lost to a hook timeout or short wiring is named and can be read by hand.
/// Compares: page numbers against the highest "of M"; one subject's pages against its "of P", from THIS lane's read; the assistant's text,
/// written after that read, against the sentence those numbers make; the command's path against the kor this gate runs as.
/// Does NOT cover: whether the session understood the bodies; whether its subject is its real one (a session can read "zzz", which is
/// in no body, and say so: it passes, and "zzz" stands in its transcript in its own words); whether the sentence is the FIRST thing it
/// said, only that it came after the read and before any work; a subagent (allowed: agent_id, it has no start of its own) nor the
/// KOR_START_GATE=off sessions (the reviewer's and the task judge's: neither has Bash). Same-class miss: a delivered page or body that
/// quotes a whole page head, or a whole 'KOR BODIES' head naming this lane, counts as that read. After <see cref="MaxDenials"/> refusals,
/// the parallel calls of one turn counting once, it opens and says what is not read, so that a broken database cannot wedge a session.
/// </summary>
public static class StartGate
{
    public const int MaxDenials = 6;

    /// <summary>Only this lane's read: "kor read --module Other" would otherwise pass as this session's. A head counting 0 bodies is no read.</summary>
    private static Regex BodyHeadOf(string module) =>
        new(Regex.Escape(BodyRead.Head) + @"([^']*)' - page (\d+) of (\d+) - \d+ of (\d+) bodies read through this page \(lanes " + Regex.Escape(module) + " and ", RegexOptions.CultureInvariant);

    private static Regex NothingHeadOf(string module) =>
        new(Regex.Escape(BodyRead.NothingHead) + @"([^']*)' - 0 of 0 bodies, no word of it is in any of [\d,]+ bodies \(lanes " + Regex.Escape(module) + " and ", RegexOptions.CultureInvariant);

    /// <summary>A real page head names its module: another lane's page, or a comment quoting "page 3 of 20", is not this session's.</summary>
    private static Regex PageHeadOf(string module) =>
        new(Regex.Escape(StartRender.PagesHead) + @"(\d+) of (\d+) - module '" + Regex.Escape(module) + "'", RegexOptions.CultureInvariant);

    /// <summary>
    /// A kor read or a kor start --part pages, alone, from THE kor. An ALLOW-LIST (reviews 571, 575 and 577 each found a new way past a list of what to
    /// refuse: a chain, parentheses, a newline after kor): the whole command is the call operator at most, a path, the verb, and
    /// arguments of word characters and . : / \ - = + only, a quoted one also holding spaces, commas and #; blanks are spaces and tabs; one
    /// line end at most. The path must then BE the kor this gate runs as (<see cref="SameKor"/>): ./kor would run whatever has that name.
    /// </summary>
    private static readonly Regex ReadCommand = new(
        @"\A[ \t]*(?:&[ \t]*)?" +
        @"(?:""(?<kor>[\w.:/\\ -]+)""|'(?<kor>[\w.:/\\ -]+)'|(?<kor>[\w.:/\\-]+))" +
        @"[ \t]+(?:read|start(?:" + Arg + @")*[ \t]+--part[ \t]+pages)(?:" + Arg + @")*[ \t]*(?:\r?\n)?\z",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// The same file by its literal path: either slash, any case, Git Bash's /c/ for C:/, .exe or not. Literal on purpose, so a ".." is not this kor.
    /// The gate's own path is the installed kor because the settings line names it; a proof on a built kor is held to that built kor.
    /// </summary>
    private static bool SameKor(string path, string exe)
    {
        static string Norm(string p)
        {
            var s = p.Replace('\\', '/').ToLowerInvariant();
            if (s.Length > 3 && s[0] == '/' && char.IsLetter(s[1]) && s[2] == '/') s = s[1] + ":" + s[2..];
            return s.EndsWith(".exe", StringComparison.Ordinal) ? s[..^4] : s;
        }
        return Norm(path) == Norm(exe);
    }

    // "#" only inside quotes: bare, it starts a comment in both shells, and a comment could hide the --part pages this check read
    private const string Arg = @"[ \t]+(?:""[\w.:/\\ =#+,-]*""|'[\w.:/\\ =#+,-]*'|[\w.:/\\=+-]+)";

    /// <summary>Parallel calls of one turn are refused together and count once: a refusal inside this window of the last is the same refusal.</summary>
    public static readonly TimeSpan SameRefusal = TimeSpan.FromSeconds(20);

    /// <summary>The refusal count after this refusal.</summary>
    public static int NextDenied(int denied, DateTime? lastAt, DateTime now) =>
        lastAt is { } at && now - at < SameRefusal ? Math.Max(denied, 1) : denied + 1;

    public sealed class Seen
    {
        public HashSet<int> Pages { get; } = new();
        public int Total { get; set; }
        public Dictionary<string, (HashSet<int> Pages, int Total, int Count)> Bodies { get; } = new();

        /// <summary>Subjects a real read found in no body at all: nothing was read, and the session may only go on by saying so.</summary>
        public HashSet<string> Nothing { get; } = new();

        /// <summary>The sentence the session said after its read, with the numbers that read gave; null until it has.</summary>
        public string? Stated { get; set; }

        public IEnumerable<(string Subject, int Count)> Read =>
            Bodies.Where(b => b.Value.Total > 0 && Enumerable.Range(1, b.Value.Total).All(b.Value.Pages.Contains)).Select(b => (b.Key, b.Value.Count));
        public bool BodiesDone => Read.Any();
        public List<int> MissingPages => Enumerable.Range(1, Total).Where(p => !Pages.Contains(p)).ToList();
    }

    public sealed record Decision(bool Allow, string Reason);

    /// <summary>KOR_START_GATE=off: a session with no Bash (kor_review.py's reviewer, kor_task.py's judge) could never run the read.</summary>
    public static bool Lifted(Func<string, string?> env) => string.Equals(env("KOR_START_GATE"), "off", StringComparison.OrdinalIgnoreCase);

    /// <param name="exe">the kor this gate runs as; a command from any other path is not the read.</param>
    public static bool IsReadCommand(string tool, string? command, string exe)
    {
        if (tool is not ("Bash" or "PowerShell") || command is null) return false;
        var m = ReadCommand.Match(command);
        return m.Success && SameKor(m.Groups["kor"].Value, exe);
    }

    public static string ReadSentence(int pages, int bodies, string subject) => $"pages {pages} of {pages} held, bodies {bodies} of {bodies} read for subject {subject}";
    public static string NothingSentence(int pages, string subject) => $"pages {pages} of {pages} held, bodies 0 of 0 for subject {subject}: it truly matches nothing";

    /// <summary>What the assistant wrote on a transcript line, for the sentence: lower case, no markdown or quote marks, blanks closed up.</summary>
    private static IEnumerable<string> AssistantText(string line)
    {
        var said = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String || t.GetString() != "assistant") return said;
            if (!root.TryGetProperty("message", out var m) || m.ValueKind != JsonValueKind.Object || !m.TryGetProperty("content", out var c) || c.ValueKind != JsonValueKind.Array) return said;
            foreach (var block in c.EnumerateArray())
                if (block.ValueKind == JsonValueKind.Object && block.TryGetProperty("type", out var bt) && bt.ValueKind == JsonValueKind.String && bt.GetString() == "text"
                    && block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    said.Add(Regex.Replace(Regex.Replace(text.GetString()!, @"[*`'""]", ""), @"\s+", " ").ToLowerInvariant());
        }
        catch (JsonException) { /* a line being written: nothing said on it yet */ }
        return said;
    }

    /// <summary>
    /// The session's transcript lines: page and body heads in delivered hook context and tool results only - never in what the assistant
    /// wrote. The assistant's text counts only for the sentence, and only once the read it states has been delivered.
    /// </summary>
    public static Seen Scan(IEnumerable<string> lines, string module)
    {
        var seen = new Seen();
        var pageHead = PageHeadOf(module);
        var bodyHead = BodyHeadOf(module);
        var nothingHead = NothingHeadOf(module);
        foreach (var line in lines)
        {
            // A compaction loses everything read before it: only what is delivered after the newest one counts. Its SessionStart hooks
            // finish after the summary is written (ModuleState.NewestCompactSummary, live 3 Nov: summary 09:44:58.35, record part
            // 09:44:59.58). Not seen in a transcript: none of the newest 150 here holds a compaction; were the order ever the other way,
            // the gate asks for the pages by hand, and opens after MaxDenials.
            if (line.Contains("\"isCompactSummary\":true", StringComparison.Ordinal)) { seen.Bodies.Clear(); seen.Nothing.Clear(); seen.Stated = null; seen.Pages.Clear(); seen.Total = 0; continue; }
            if (line.Contains("\"type\":\"assistant\"", StringComparison.Ordinal) && seen.Total > 0 && seen.MissingPages.Count == 0)
            {
                var owed = seen.Read.Select(r => ReadSentence(seen.Total, r.Count, r.Subject)).Concat(seen.Nothing.Select(s => NothingSentence(seen.Total, s))).ToList();
                if (owed.Count > 0)
                    foreach (var said in AssistantText(line))
                        if (owed.FirstOrDefault(o => said.Contains(o, StringComparison.Ordinal)) is { } match) seen.Stated = match;
            }
            var attachment = line.Contains("\"type\":\"attachment\"", StringComparison.Ordinal);
            var result = line.Contains("\"tool_result\"", StringComparison.Ordinal);
            if (!attachment && !result) continue;
            foreach (Match m in pageHead.Matches(line))
            {
                var total = int.Parse(m.Groups[2].Value);
                if (total != seen.Total && total > 0) { if (total > seen.Total) { seen.Pages.Clear(); seen.Total = total; } else continue; }
                seen.Pages.Add(int.Parse(m.Groups[1].Value));
            }
            foreach (Match m in bodyHead.Matches(line))
            {
                var count = int.Parse(m.Groups[4].Value);
                if (count == 0) continue;   // an old kor's head for an empty read: 0 of 0 is no read
                var subject = m.Groups[1].Value;
                var total = int.Parse(m.Groups[3].Value);
                if (!seen.Bodies.TryGetValue(subject, out var b) || b.Total != total) b = (new HashSet<int>(), total, count);
                b.Pages.Add(int.Parse(m.Groups[2].Value));
                seen.Bodies[subject] = b;
            }
            foreach (Match m in nothingHead.Matches(line)) seen.Nothing.Add(m.Groups[1].Value);
        }
        return seen;
    }

    public static Decision Decide(Seen seen, string module, string exe, string? session)
    {
        if (seen.Total == 0)
            return new Decision(false, $"Your memory pages are not in your context. Read them first: {StartRender.ReadPageCommand(module, exe, session, 1, 1)} " +
                                       "(its head names how many there are; add --page N --pages M for each).");
        var missing = seen.MissingPages;
        if (missing.Count > 0)
            return new Decision(false, $"MEMORY LEFT OUT: you hold pages {seen.Pages.Count} of {seen.Total}; missing {string.Join(", ", missing)}. Read each, one command at a time: " +
                                       StartRender.ReadPageCommand(module, exe, session, missing[0], seen.Total));
        if (seen.Stated is { } stated) return new Decision(true, stated);
        const string asText = "Write it as text in your reply, then call your tool again; the gate reads it from your transcript";
        if (seen.Read.FirstOrDefault() is { Subject: not null } read)
            return new Decision(false, $"You hold {seen.Total} of {seen.Total} memory pages and have read the bodies. Now say so, in these words: '{ReadSentence(seen.Total, read.Count, read.Subject)}'. {asText} (row 1861).");
        if (seen.Nothing.FirstOrDefault() is { } none)
            return new Decision(false, $"You read 0 of 0 bodies for subject '{none}': NOTHING WAS READ. If other words describe your task, read with those: \"{exe}\" read --module \"{module}\" --subject \"<plain words>\". " +
                                       $"If this truly is your subject, say so, in these words: '{NothingSentence(seen.Total, none)}'. {asText} (row 1861).");
        return new Decision(false, $"You hold {seen.Total} of {seen.Total} memory pages. The bodies are not read yet: run \"{exe}\" read --module \"{module}\" --subject \"<plain words for your task>\" " +
                                   "alone, on one line with nothing chained (a subject holds letters, digits, spaces and . : / - = # + , only; other characters are refused), every page it names, then say 'pages X of Y held, bodies A of B read for subject S' and go on (row 1569).");
    }

    /// <summary>Whole lines of a transcript being written by Claude Code (opened for shared read).</summary>
    public static IEnumerable<string> ReadLines(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        for (var line = sr.ReadLine(); line is not null; line = sr.ReadLine()) yield return line;
    }

    public static string OpenMarker(string cacheDir, string session) => Path.Combine(cacheDir, "gate", Safe(session) + ".open");
    public static string DeniedMarker(string cacheDir, string session) => Path.Combine(cacheDir, "gate", Safe(session) + ".denied");

    /// <summary>A compaction loses the bodies: the record part re-arms the gate on a compact start.</summary>
    public static void Rearm(string cacheDir, string session)
    {
        try { File.Delete(OpenMarker(cacheDir, session)); File.Delete(DeniedMarker(cacheDir, session)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the gate stays open: it only asks again */ }
    }

    private static string Safe(string s) => new(s.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());

    /// <summary>The hook's text from the hook's JSON: a tool's command, or null.</summary>
    public static string? CommandOf(JsonElement root) =>
        root.TryGetProperty("tool_input", out var ti) && ti.ValueKind == JsonValueKind.Object && ti.TryGetProperty("command", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString() : null;
}
