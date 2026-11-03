using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Enforce;

/// <summary>A live brain.[Check] row (KorStandards 172, brain.vw_LiveCheck) as kor enforce uses it.</summary>
public sealed record LiveCheck(int Id, int MemoryId, string MemoryKind, string MemoryName, string Module, string Tool, string Field,
                               string Pattern, IReadOnlyList<string> MustRefuse, IReadOnlyList<string> MustAllow, string Says)
{
    public static IReadOnlyList<string> ParseExamples(string json) =>
        JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
}

/// <summary>What a check matched: the segment's original text and where it came from (the command, a sub-shell, a script).</summary>
public sealed record RuleMatch(string Text, string Origin);

/// <summary>
/// K7 (Framework docs/KNOWLEDGE-IDE-SPEC-K4-K7.md): a check's pattern against a tool call's structured input - never the
/// reply's prose. Field command: the pattern, wrapped <c>\A(?:P)</c>, IgnoreCase, 200 ms timeout (a timeout = no match),
/// against each <see cref="CommandSegments"/> segment's ORIGINAL text, so a quoted mention, a heredoc body and a commit
/// message never match, while <c>cd X &amp;&amp; E</c> does - and against the segments of a script the command runs.
/// Field file_path: the path with '\' as '/'.
/// </summary>
public static class Rules
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);

    public static readonly string[] Tools = { "Bash", "PowerShell", "Write", "Edit" };

    public static string FieldOf(string tool) => tool is "Write" or "Edit" ? "file_path" : "command";

    /// <summary>
    /// Review row 231: tools that write a file but have no checks of their own - MultiEdit (tool_input.file_path) and
    /// NotebookEdit (tool_input.notebook_path). Each is held to EVERY file_path check (Write's and Edit's), so a recorded
    /// path rule cannot be got round through them (a MultiEdit can create a file; a NotebookEdit edits one). A check is never
    /// recorded for them (check-add keeps <see cref="Tools"/>).
    /// </summary>
    public static readonly string[] FileAliases = { "MultiEdit", "NotebookEdit" };

    /// <summary>Every tool kor enforce reads: <see cref="Tools"/> + <see cref="FileAliases"/>.</summary>
    public static bool Enforced(string tool) => Tools.Contains(tool) || FileAliases.Contains(tool);

    /// <summary>The tool_input field holding what a check matches: notebook_path for NotebookEdit, file_path for MultiEdit.</summary>
    public static string InputFieldOf(string tool) => tool switch
    {
        "NotebookEdit" => "notebook_path",
        "MultiEdit" => "file_path",
        _ => FieldOf(tool),
    };

    /// <summary>Which checks (by their Tool) apply to a call of <paramref name="tool"/>.</summary>
    public static bool Applies(string checkTool, string tool) =>
        checkTool == tool || (FileAliases.Contains(tool) && checkTool is "Write" or "Edit");

    public static Shell ShellOf(string tool) => tool == "PowerShell" ? Shell.PowerShell : Shell.Bash;

    /// <summary>The compiled pattern, or null with the reason when it does not compile.</summary>
    public static Regex? Compile(string pattern, out string? error)
    {
        try
        {
            error = null;
            return new Regex(@"\A(?:" + pattern + ")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
        }
        catch (ArgumentException e)
        {
            error = e.Message;
            return null;
        }
    }

    /// <summary>The first segment the check matches, or null. <paramref name="cwd"/> null = scripts are not read.</summary>
    public static RuleMatch? Matches(LiveCheck check, string input, string? cwd = null) =>
        Matches(check.Pattern, check.Tool, input, cwd);

    public static RuleMatch? Matches(string pattern, string tool, string input, string? cwd = null)
    {
        var rx = Compile(pattern, out _);
        if (rx is null || string.IsNullOrEmpty(input)) return null;
        if (FieldOf(tool) == "file_path")
        {
            var p = input.Replace('\\', '/');
            return IsMatch(rx, p) ? new RuleMatch(p, "file_path") : null;
        }
        foreach (var seg in CommandSegments.Of(input, ShellOf(tool), cwd))
            if (IsMatch(rx, seg.Text)) return new RuleMatch(seg.Text, seg.Origin);
        return null;
    }

    private static bool IsMatch(Regex rx, string s)
    {
        try { return rx.IsMatch(s); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    /// <summary>
    /// Why the check cannot be trusted (empty = it passes its known answers): the pattern compiles; every MustRefuse
    /// example matches; no MustAllow example matches; and for each MustRefuse E (command checks), the derived MENTIONS do
    /// NOT match - <c>echo "E"</c>, <c>echo 'E'</c>, <c>git commit -m "E"</c>, a heredoc / here-string holding E - while
    /// <c>cd "C:/x" &amp;&amp; E</c> DOES. E inside double quotes is escaped the shell's way (bash \" \$ \` \\, PowerShell `" `$ ``),
    /// so the mention is a real one, and the escape model is tested by every check.
    /// </summary>
    public static IReadOnlyList<string> KnownAnswerFailures(string pattern, string tool, IReadOnlyList<string> mustRefuse, IReadOnlyList<string> mustAllow)
    {
        var fails = new List<string>();
        if (Compile(pattern, out var err) is null) { fails.Add($"the pattern does not compile: {err}"); return fails; }
        if (!Tools.Contains(tool)) { fails.Add($"tool '{tool}' is not Bash, PowerShell, Write or Edit"); return fails; }
        if (mustRefuse.Count == 0) fails.Add("no refuse example");
        if (mustAllow.Count == 0) fails.Add("no allow example");
        for (var i = 0; i < mustRefuse.Count; i++)
        {
            var e = mustRefuse[i];
            if (Matches(pattern, tool, e) is null) fails.Add($"refuse example {i + 1} is not matched");
            if (FieldOf(tool) != "command") continue;
            foreach (var (label, text) in Mentions(e, ShellOf(tool)))
                if (Matches(pattern, tool, text) is not null) fails.Add($"refuse example {i + 1} as a mention ({label}) is matched");
            if (Matches(pattern, tool, $"cd \"C:/x\" && {e}") is null) fails.Add($"refuse example {i + 1} after `cd \"C:/x\" &&` is not matched");
        }
        for (var i = 0; i < mustAllow.Count; i++)
            if (Matches(pattern, tool, mustAllow[i]) is not null) fails.Add($"allow example {i + 1} is matched");
        return fails;
    }

    public static IReadOnlyList<string> KnownAnswerFailures(LiveCheck c) => KnownAnswerFailures(c.Pattern, c.Tool, c.MustRefuse, c.MustAllow);

    /// <summary>The derived mentions of an example: data that names it, which must never be refused.</summary>
    public static IEnumerable<(string Label, string Text)> Mentions(string e, Shell shell)
    {
        var dq = shell == Shell.PowerShell
            ? e.Replace("`", "``").Replace("\"", "`\"").Replace("$", "`$")
            : e.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`");
        yield return ("echo \"E\"", $"echo \"{dq}\"");
        yield return ("git commit -m \"E\"", $"git commit -m \"{dq}\"");
        if (!e.Contains('\'')) yield return ("echo 'E'", $"echo '{e}'");
        else if (shell == Shell.PowerShell) yield return ("echo 'E'", $"echo '{e.Replace("'", "''")}'");
        if (shell == Shell.PowerShell)
            yield return ("here-string", $"$m = @'\n{e}\n'@\ngit commit -m $m");
        else
            yield return ("heredoc", $"git commit -F - <<'EOF'\n{e}\nEOF");
    }
}
