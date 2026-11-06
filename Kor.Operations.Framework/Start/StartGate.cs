using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kor.Operations.Framework.Start;

/// <summary>
/// Board row 1569: <c>kor start-gate</c> on PreToolUse. A session's first work tool is refused until its own transcript shows every
/// memory page it was meant to hold and all of one subject's bodies. It reads what was DELIVERED (the session's transcript), not
/// what was wired, so a page lost to a hook timeout or short wiring is named and can be read by hand.
/// Compares: page numbers against the highest "of M"; one subject's pages against its "of P". Does NOT cover: whether the session
/// understood them; whether its subject is its real one; a subagent (allowed: agent_id, it has no start of its own) nor the
/// KOR_START_GATE=off sessions (the reviewer's). Same-class miss: a tool result that merely quotes a page head counts as that page.
/// After <see cref="MaxDenials"/> refusals it opens and says so, so that a broken database cannot wedge a session.
/// </summary>
public static class StartGate
{
    public const int MaxDenials = 6;

    private static readonly Regex PageHead = new(Regex.Escape(StartRender.PagesHead) + @"(\d+) of (\d+)\b", RegexOptions.CultureInvariant);
    private static readonly Regex BodyHead = new(Regex.Escape(BodyRead.Head) + @"([^']*)' - page (\d+) of (\d+)\b", RegexOptions.CultureInvariant);

    /// <summary>A kor read or a kor start --part pages, alone: nothing chained behind it.</summary>
    private static readonly Regex ReadCommand = new(
        @"^\s*(?:&\s*)?[""']?[^""';&|<>]*\bkor(?:\.exe)?[""']?\s+(?:read\b|start\b[^;&|<>]*--part\s+pages\b)[^;&|<>]*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public sealed class Seen
    {
        public HashSet<int> Pages { get; } = new();
        public int Total { get; set; }
        public Dictionary<string, (HashSet<int> Pages, int Total)> Bodies { get; } = new();
        public bool BodiesDone => Bodies.Values.Any(b => b.Total > 0 && Enumerable.Range(1, b.Total).All(b.Pages.Contains));
        public List<int> MissingPages => Enumerable.Range(1, Total).Where(p => !Pages.Contains(p)).ToList();
    }

    public sealed record Decision(bool Allow, string Reason);

    public static bool IsReadCommand(string tool, string? command) =>
        tool is "Bash" or "PowerShell" && command is not null && ReadCommand.IsMatch(command);

    /// <summary>The session's transcript lines: page heads in delivered hook context and tool results only - never in what the assistant wrote.</summary>
    public static Seen Scan(IEnumerable<string> lines)
    {
        var seen = new Seen();
        foreach (var line in lines)
        {
            // a compaction loses the bodies, and SessionStart re-fires the pages: the bodies are owed again, the pages are not
            if (line.Contains("\"isCompactSummary\":true", StringComparison.Ordinal)) { seen.Bodies.Clear(); continue; }
            var attachment = line.Contains("\"type\":\"attachment\"", StringComparison.Ordinal);
            var result = line.Contains("\"tool_result\"", StringComparison.Ordinal);
            if (!attachment && !result) continue;
            foreach (Match m in PageHead.Matches(line))
            {
                var total = int.Parse(m.Groups[2].Value);
                if (total != seen.Total && total > 0) { if (total > seen.Total) { seen.Pages.Clear(); seen.Total = total; } else continue; }
                seen.Pages.Add(int.Parse(m.Groups[1].Value));
            }
            foreach (Match m in BodyHead.Matches(line))
            {
                var subject = m.Groups[1].Value;
                var total = int.Parse(m.Groups[3].Value);
                if (!seen.Bodies.TryGetValue(subject, out var b) || b.Total != total) b = (new HashSet<int>(), total);
                b.Pages.Add(int.Parse(m.Groups[2].Value));
                seen.Bodies[subject] = b;
            }
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
        if (!seen.BodiesDone)
            return new Decision(false, $"You hold {seen.Total} of {seen.Total} memory pages. The bodies are not read yet: run \"{exe}\" read --module \"{module}\" --subject \"<words for your task>\" " +
                                       "alone, every page it names, then say 'pages X of Y held, bodies A of B read for subject S' and go on (row 1569).");
        return new Decision(true, $"pages {seen.Total} of {seen.Total} held; bodies read");
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
