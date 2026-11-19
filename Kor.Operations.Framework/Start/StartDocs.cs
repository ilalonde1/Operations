using System.Text.Json;

namespace Kor.Operations.Framework.Start;

/// <summary>
/// W1 + W6 (Operations docs/codex/CODEX-CLAUDE-WEAKNESSES-RESPONSE.md:56 and :138; Framework lanes/2026-11-19/
/// PLAN-W1-W9-CHECKS.md check 1): <c>kor start-docs --doc P [--doc P ...] --hook</c> on PreToolUse. Found 19 Nov: a fresh
/// Framework coordinator worked ~3 h on the C# port without reading docs/BUILD-PLAN.md, which Framework CLAUDE.md:28 names in
/// its start routine; nothing checked it. Until this session's transcript shows every line of every named doc DELIVERED by
/// the Read tool, every tool but the reading ones is refused, naming the doc and the lines still unread.
/// What counts as read: a Read tool result whose toolUseResult.file (filePath, startLine, numLines, totalLines - the tool's
/// own record of what it delivered, not what was asked) names the doc, after the session's newest compaction (a compaction
/// loses what was read before it). Lines read are unioned across reads; done when they cover every line the doc needs: the
/// whole file, or for "path#Heading" that heading's section as the file stands now (Required) - the Framework routine names
/// "docs/HANDOVER.md (STATE NOW)", not the whole 481-line handover. A section heading not found in the doc is refused.
/// COVERS: a doc never opened; a doc read in part (offset/limit, or cut by the tool); a read lost to a compaction.
/// DOES NOT COVER: understanding (a doc read is not a doc applied); a doc changed after it was read; a Bash cat / sed of the
/// doc (counts as UNREAD - Read is the one route, and the refusal says so); a doc the routine names but the settings line
/// omits. Same-class miss: a session that reads the doc, then acts against it. A doc not found is REFUSED, named (a broken line must not pass).
/// Lifted by KOR_START_GATE=off (reviewer and judge sessions, which have no start). A subagent passes (no start of its own).
/// Never wedges: after <see cref="StartGate.MaxDenials"/> refusals it opens and says what is unread.
/// </summary>
public static class StartDocs
{
    /// <summary>Tools that only read or ask: allowed before the docs are read (the read needs them).</summary>
    public static readonly HashSet<string> ReadingTools = new(StringComparer.Ordinal)
        { "Read", "Grep", "Glob", "ToolSearch", "AskUserQuestion", "TaskStop", "ListAgents" };

    public sealed record Coverage(int Total, SortedSet<int> Lines)
    {
        /// <summary>The unread runs of lines within [from, to] (the whole file when to is 0).</summary>
        public IReadOnlyList<(int From, int To)> MissingIn(int from, int to)
        {
            var end = to; // the CURRENT file decides (review 2291): a line past what the read delivered (a doc that grew) is unread, never skipped
            var gaps = new List<(int, int)>();
            int? start = null;
            for (var i = from; i <= end + 1; i++)
            {
                var miss = i <= end && !Lines.Contains(i);
                if (miss && start is null) start = i;
                if (!miss && start is { } s) { gaps.Add((s, i - 1)); start = null; }
            }
            return gaps;
        }
    }

    /// <summary>A --doc argument: "path" (the whole file) or "path#Heading" (from the first line starting with that heading
    /// to the line before the next heading of the same or higher level - e.g. docs/HANDOVER.md#STATE NOW, the routine's
    /// "Read docs/HANDOVER.md (STATE NOW)"; the newest STATE NOW is the first one).</summary>
    public sealed record Doc(string Arg, string Path, string? Section);

    public static Doc ParseDoc(string arg, string? cwd)
    {
        var i = arg.IndexOf('#');
        var (p, s) = i > 0 ? (arg[..i], arg[(i + 1)..].Trim()) : (arg, null);
        return new Doc(arg, System.IO.Path.IsPathRooted(p) || cwd is null ? p : System.IO.Path.Combine(cwd, p), string.IsNullOrEmpty(s) ? null : s);
    }

    /// <summary>The 1-based line range a doc needs read, from its CURRENT text; (0, 0) when a section is not found.</summary>
    public static (int From, int To) Required(Doc d, string[] text)
    {
        if (d.Section is null) return (1, text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var t = text[i].TrimStart();
            var level = t.TakeWhile(c => c == '#').Count();
            var head = level == 0 ? "" : t[level..].TrimStart();
            if (level == 0 || !head.StartsWith(d.Section, StringComparison.OrdinalIgnoreCase)
                || (head.Length > d.Section.Length && char.IsLetterOrDigit(head[d.Section.Length]))) continue;   // "#STATE" is not "STATEMENT"
            var end = text.Length;
            for (var j = i + 1; j < text.Length; j++)
            {
                var u = text[j].TrimStart();
                var l2 = u.TakeWhile(c => c == '#').Count();
                if (l2 > 0 && l2 <= level && u.Length > l2 && u[l2] == ' ') { end = j; break; }
            }
            return (i + 1, end);
        }
        return (0, 0);
    }

    public static string Norm(string p) => Path.GetFullPath(p.Replace('/', Path.DirectorySeparatorChar)).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();

    /// <summary>What each doc's Read results delivered, after the newest compaction.</summary>
    public static Dictionary<string, Coverage> Scan(IEnumerable<string> lines, IReadOnlyCollection<Doc> docs)
    {
        var want = docs.Select(d => Norm(d.Path)).ToHashSet(StringComparer.Ordinal);
        var seen = new Dictionary<string, Coverage>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (line.Contains("\"isCompactSummary\":true", StringComparison.Ordinal)) { seen.Clear(); continue; }
            if (!line.Contains("\"toolUseResult\"", StringComparison.Ordinal) || !line.Contains("\"filePath\"", StringComparison.Ordinal)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (!doc.RootElement.TryGetProperty("toolUseResult", out var tur) || tur.ValueKind != JsonValueKind.Object) continue;
                if (!tur.TryGetProperty("file", out var f) || f.ValueKind != JsonValueKind.Object) continue;
                if (!f.TryGetProperty("filePath", out var fp) || fp.ValueKind != JsonValueKind.String) continue;
                var path = Norm(fp.GetString()!);
                if (!want.Contains(path)) continue;
                int Int(string name) => f.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;
                var start = Math.Max(1, Int("startLine"));
                var num = Int("numLines");
                var total = Int("totalLines");
                if (!seen.TryGetValue(path, out var c) || c.Total != total) c = new Coverage(total, new SortedSet<int>());
                for (var i = start; i < start + num && i <= total; i++) c.Lines.Add(i);
                seen[path] = c;
            }
            catch (JsonException) { /* a line being written */ }
        }
        return seen;
    }

    /// <summary>Allow, or the refusal naming each unread doc and its unread lines. A doc not found, or a named section not found
    /// in it, is refused (a broken settings line or a renamed heading must never silently pass).</summary>
    public static StartGate.Decision Decide(IReadOnlyCollection<Doc> docs, Dictionary<string, Coverage> seen, Func<string, string[]?> read)
    {
        var owed = new List<string>();
        foreach (var d in docs)
        {
            // a doc not found is a broken settings line or a wrong root - REFUSED, named (live probe 19 Nov: with no
            // CLAUDE_PROJECT_DIR and a subfolder cwd every doc read 'not on disk' and the gate opened with nothing read)
            if (read(d.Path) is not { } text) { owed.Add($"{d.Arg} (NOT FOUND at {d.Path} - fix the settings line or the project root)"); continue; }
            var (from, to) = Required(d, text);
            if (from == 0) { owed.Add($"{d.Arg} (no heading '{d.Section}' in it - fix the settings line)"); continue; }
            var what = d.Section is null ? "" : $" section '{d.Section}', lines {from}-{to}";
            if (!seen.TryGetValue(Norm(d.Path), out var c)) { owed.Add($"{d.Arg}{what} (not read)"); continue; }
            var gaps = c.MissingIn(from, to);
            if (gaps.Count > 0) owed.Add($"{d.Arg}{what} (lines {string.Join(", ", gaps.Select(g => g.From == g.To ? $"{g.From}" : $"{g.From}-{g.To}"))} unread)");
        }
        if (owed.Count == 0) return new StartGate.Decision(true, "start docs read: " + string.Join(", ", docs.Select(x => x.Arg)) + ".");
        return new StartGate.Decision(false,
            "START DOCS UNREAD (W1/W6, Framework lanes/2026-11-19/PLAN-W1-W9-CHECKS.md check 1): this repo's start routine names "
            + "these and your transcript shows them not wholly read: " + string.Join("; ", owed) + ". Read each with the Read tool "
            + "(a Bash cat, sed or Get-Content does NOT count); until then every tool but Read, Grep, Glob, ToolSearch, "
            + "AskUserQuestion, TaskStop and ListAgents is refused - Bash and kor included.");
    }

    /// <summary>A compaction loses the docs: the record part (kor start, source compact) re-arms this gate as it does StartGate.</summary>
    public static void Rearm(string cacheDir, string session)
    {
        try { File.Delete(OpenMarker(cacheDir, session)); File.Delete(DeniedMarker(cacheDir, session)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* stays open: it only asks again */ }
    }

    public static string OpenMarker(string cacheDir, string session) => Path.Combine(cacheDir, "docs-gate", Safe(session) + ".open");
    public static string DeniedMarker(string cacheDir, string session) => Path.Combine(cacheDir, "docs-gate", Safe(session) + ".denied");
    private static string Safe(string s) => new(s.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
}
