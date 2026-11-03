using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kor.Operations.Framework.Capture;

/// <summary>
/// K3: what a shell command did that git does not see - a test run, a build, a migration applied, a stage-gate run.
/// Pure: the command text and its output in, an event shape (or null = record nothing) out. A command is recognised
/// only where the tool sits in COMMAND position (start, or after ; &amp;&amp; || | ( or a call operator) of the
/// command with its quoted text and heredoc/here-string bodies neutralised (<see cref="Neutralise"/>), so
/// <c>grep "dotnet test" notes.md</c>, <c>git commit -m "fix; dotnet test passes"</c> and a commit-message heredoc line
/// starting "dotnet test" are not test runs. The neutraliser does not model escaped quotes (\" \'), $'...' strings,
/// or where a "&lt;&lt;TAG" really starts a heredoc. Same-class faults it therefore still has (review row 86):
/// FALSE POSITIVE - a separator inside a quote that also holds an escaped quote; FALSE NEGATIVE - an unbalanced quote
/// outside any string (<c>don't</c> unquoted, an escaped \') hides every later command, and a "&lt;&lt;TAG" that is
/// not a heredoc (<c>-m "use &lt;&lt;EOF"</c>, <c>$((1&lt;&lt;2))</c>) blanks every later line until one equals TAG.
/// Migration and gate file names are FOUND on the neutral copy but READ from the original, widened to the whole token
/// (<see cref="NameAt"/>; a quoted name to its closing quote), so a '(' or ';' inside a quoted name neither cuts it
/// short nor hands back its later half (review row 87).
///
/// Outcome is read from the OUTPUT, never assumed: a fail marker beats a pass marker; with neither, a non-zero exit
/// (PostToolUseFailure's "Exit code N") is 'fail', otherwise 'unknown'.
///
/// Does NOT see: a test or build run inside a script the command calls (only the command line is matched); output a
/// tool wrote to a file instead of stdout; the exit code of a PostToolUse (success) call, which the docs do not carry.
/// </summary>
public static class ToolClassifier
{
    public sealed record ToolEvent(string Kind, string Ref, string Outcome, string Detail);

    private const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    // Command position: start of text/line, or after ; & | ( - "&&" and "||" end in & and |, pwsh's "&" call operator too.
    private const string Pos = @"(?:^|[;&|(\n])\s*";
    // An optional folder before the tool: a quoted one may hold spaces ("C:/VIsual Studio Projects/..."), a bare one not.
    private const string PathPrefix = @"(?:[""'][^""'\n]*?[\\/]|[""']|[^""'\s;&|]*[\\/])?";

    private static readonly Regex DotnetTest = new(Pos + PathPrefix + @"dotnet(?:\.exe)?[""']?\s+test\b(?<args>[^\n]*)", O);
    private static readonly Regex DotnetBuild = new(Pos + PathPrefix + @"dotnet(?:\.exe)?[""']?\s+build\b(?<args>[^\n]*)", O);
    private static readonly Regex Sqlcmd = new(Pos + PathPrefix + @"sqlcmd(?:\.exe)?[""']?\s[^\n;|]*?-i\s+[""']?[^""'\n]*?[\\/]db[\\/](?<file>\d{3}_[^""'\\/\n]+?\.sql)", O);
    private static readonly Regex KorSql = new(@"(?:^|[;&|(\n]|-File)\s*" + PathPrefix + @"Invoke-KorSqlScript(?:\.ps1)?[""']?(?<rest>[^\n;|]*)", O);
    // Lazy and space-tolerant: a blanked character may sit inside the name; NameAt reads it whole and MigrationName decides.
    private static readonly Regex SqlFile = new(@"(?<file>\d{3}_[^""'\\/\n]+?\.sql)", O);
    private static readonly Regex StageGate = new(Pos + PathPrefix + @"python[\w.]*[""']?(?:\s+-\w+)*\s+" + PathPrefix + @"stage_gate\.py[""']?\s+run\s+[""']?(?<file>[^""'\s;&|]+)", O);

    private static readonly Regex ExitCode = new(@"\AExit code (?<n>-?\d+)", O);

    public static ToolEvent? Classify(string? command, string? output, int? exitCode = null)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        output ??= "";
        exitCode ??= ExitCodeOf(output);
        // Match on a copy where quoted text and heredoc bodies cannot look like a command; it has the SAME length, so a
        // match's position reads the original text (a Ref keeps --filter "A|B" as written).
        var neutral = Neutralise(command);
        Match m;
        if ((m = DotnetTest.Match(neutral)).Success)
            return Make("test", "dotnet test" + Args(m.Groups["args"], command), TestOutcome(output, exitCode), command, output, TestSummary);
        if ((m = DotnetBuild.Match(neutral)).Success)
            return Make("build", "dotnet build" + Args(m.Groups["args"], command), BuildOutcome(output, exitCode), command, output, BuildSummary);
        string? name;
        if ((m = Sqlcmd.Match(neutral)).Success && (name = NameAt(command, neutral, m.Groups["file"].Index, m.Groups["file"].Length, MigrationName)) is not null)
            return Make("migration", name, MigrationOutcome(output, exitCode), command, output, MigrationSummary);
        if ((m = KorSql.Match(neutral)).Success && SqlFile.Match(m.Groups["rest"].Value) is { Success: true } f
            && (name = NameAt(command, neutral, m.Groups["rest"].Index + f.Index, f.Length, MigrationName)) is not null)
            return Make("migration", name, MigrationOutcome(output, exitCode), command, output, MigrationSummary);
        if ((m = StageGate.Match(neutral)).Success && (name = NameAt(command, neutral, m.Groups["file"].Index, m.Groups["file"].Length, null)) is not null)
            return Make("gate", name, GateOutcome(output, exitCode), command, output, GateSummary);
        return null;
    }

    private static readonly Regex MigrationName = new(@"^\d{3}_[^""'\s\\/]+\.sql$", O);

    /// <summary>
    /// The whole file name the match sits in, read from the ORIGINAL text (review row 87: a blanked '(' or ';' inside a
    /// quoted name must not cut it short or hand back its later half). The match span is widened left and right to the
    /// token's ends: whitespace, a quote, or a path separator (left only) in the original, or a REAL shell separator
    /// (one still present in the neutral copy). A widened name that no longer fits <paramref name="shape"/> is a miss.
    /// </summary>
    internal static string? NameAt(string original, string neutral, int index, int length, Regex? shape)
    {
        bool Real(int i) => neutral[i] is ';' or '&' or '|' && neutral[i] == original[i];
        bool End(int i) => char.IsWhiteSpace(original[i]) || original[i] is '"' or '\'' || Real(i);
        var start = index;
        while (start > 0 && !End(start - 1) && original[start - 1] is not ('/' or '\\')) start--;
        var end = index + length;
        if (start > 0 && original[start - 1] is '"' or '\'')
        {
            // A quoted name runs to its closing quote (spaces included: "C:/VIsual Studio Projects/x/stage_gates.json").
            var close = original.IndexOf(original[start - 1], start);
            end = Math.Max(end, close < 0 ? original.Length : close);
        }
        else
            while (end < original.Length && !End(end)) end++;
        var name = original[start..end];
        return shape is null || shape.IsMatch(name) ? name : null;
    }

    private static readonly Regex HeredocStart = new(@"(?<!<)<<(?!<)-?\s*[""']?(?<tag>\w+)[""']?", RegexOptions.CultureInvariant);
    private static readonly Regex HereStringStart = new(@"@[""']\s*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The command with every character that is DATA rather than shell structure replaced by a space, length kept:
    /// separators (; &amp; | ( newline) inside '...' or "..." quotes, and the body lines of a bash heredoc (&lt;&lt;TAG ...
    /// TAG) or a PowerShell here-string (@' ... '@, @" ... "@). Quote characters themselves stay, so a quoted tool path
    /// still matches. Not handled: backslash-escaped quotes inside a quote, $'...' strings, nested command substitution.
    /// </summary>
    internal static string Neutralise(string command)
    {
        var c = command.Replace('\r', ' ').ToCharArray();
        // 1. heredoc / here-string bodies, line by line.
        var lines = new string(c).Split('\n');
        var pos = 0;
        string? endTag = null;
        foreach (var line in lines)
        {
            if (endTag is not null)
            {
                var done = endTag == "'@" || endTag == "\"@" ? line.TrimStart().StartsWith(endTag) : line.Trim() == endTag;
                if (done) endTag = null;
                else for (var i = 0; i < line.Length; i++) c[pos + i] = ' ';
            }
            else if (HeredocStart.Match(line) is { Success: true } h) endTag = h.Groups["tag"].Value;
            else if (HereStringStart.Match(line) is { Success: true } s) endTag = s.Value.TrimEnd()[1] + "@";
            pos += line.Length + 1;
        }
        // 2. separators inside quotes (a quote may span lines).
        char quote = '\0';
        for (var i = 0; i < c.Length; i++)
        {
            var ch = c[i];
            if (quote == '\0') { if (ch is '"' or '\'') quote = ch; }
            else if (ch == quote) quote = '\0';
            else if (ch is ';' or '&' or '|' or '(' or '\n') c[i] = ' ';
        }
        return new string(c);
    }

    public static int? ExitCodeOf(string output)
    {
        var m = ExitCode.Match(output.TrimStart());
        return m.Success && int.TryParse(m.Groups["n"].Value, out var n) ? n : null;
    }

    // ---- outcomes ---------------------------------------------------------------------------------------------------
    private static readonly Regex TestFail = new(@"^\s*Failed!\s|^\s*Test Run Failed\b|Test summary: total: \d+, failed: [1-9]|^\s*Build FAILED\b|\bbuild failed\b|\berror [A-Z]{2,}\d{3,}\b", O | RegexOptions.Multiline);
    private static readonly Regex TestPass = new(@"^\s*Passed!\s|^\s*Test Run Successful\b|Test summary: total: \d+, failed: 0\b", O | RegexOptions.Multiline);
    private static readonly Regex BuildFail = new(@"^\s*Build FAILED\b|\bbuild failed\b|\berror [A-Z]{2,}\d{3,}\b|\s[1-9]\d* Error\(s\)", O | RegexOptions.Multiline);
    private static readonly Regex BuildPass = new(@"\bBuild succeeded\b", O);
    private static readonly Regex MigFail = new(@"\bMsg \d+, Level \d+|Exception\b|SCRIPT LEFT \d+ TRANSACTION|POST-CHECK FAILED|POST-CHECK NOT RUN|\bSqlcmd: Error\b", O);
    private static readonly Regex MigDone = new(@"\bDONE\b", RegexOptions.CultureInvariant);      // the result cell, upper case
    private static readonly Regex MigTested = new(@"\bTESTED\b", RegexOptions.CultureInvariant);

    internal static string TestOutcome(string o, int? exit) =>
        TestFail.IsMatch(o) ? "fail" : TestPass.IsMatch(o) ? "pass" : Fallback(exit);

    internal static string BuildOutcome(string o, int? exit) =>
        BuildFail.IsMatch(o) ? "fail" : BuildPass.IsMatch(o) ? "pass" : Fallback(exit);

    internal static string MigrationOutcome(string o, int? exit) =>
        MigFail.IsMatch(o) || exit is > 0 or < 0 ? "fail" : MigDone.IsMatch(o) ? "applied" : MigTested.IsMatch(o) ? "tested" : "unknown";

    internal static string GateOutcome(string o, int? exit) =>
        o.Contains("stage_gate: PASS") ? "pass"
        : o.Contains("stage_gate: STOPPED") ? "stopped"
        : o.Contains("PIPELINE INVALID") ? "invalid"
        : o.Contains("stage_gate: REFUSED") ? "refused"
        : Fallback(exit);

    private static string Fallback(int? exit) => exit is > 0 or < 0 ? "fail" : "unknown";

    // ---- detail: the command, the lines that say what happened, the last lines ---------------------------------------
    private static readonly Regex TestSummary = new(@"Passed!|Failed!|Test Run|Test summary|Total tests|^\s*Failed\s+\S|error [A-Z]{2,}\d{3,}|Build FAILED|build failed", O | RegexOptions.Multiline);
    private static readonly Regex BuildSummary = new(@"Build succeeded|Build FAILED|build failed|error [A-Z]{2,}\d{3,}|Warning\(s\)|Error\(s\)", O);
    private static readonly Regex MigrationSummary = new(@"\bDONE\b|\bTESTED\b|Msg \d+|Exception|applied \d+ batch|POST-CHECK|SCRIPT LEFT|Rolled back", O);
    private static readonly Regex GateSummary = new(@"stage_gate:|\[\d+/\d+\]", O);

    private static ToolEvent Make(string kind, string reference, string outcome, string command, string output, Regex summary)
    {
        var lines = (Redact.Text(output) ?? "").Replace("\r", "").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
        var detail = new
        {
            command = Clip(Redact.Text(command)!, 400),
            summary = lines.Where(l => summary.IsMatch(l)).Take(20).Select(l => Clip(l, 300)),
            tail = lines.Skip(Math.Max(0, lines.Count - 5)).Select(l => Clip(l, 300)),
            lines = lines.Count,
        };
        return new ToolEvent(kind, Clip(Redact.Text(reference)!.Trim(), 400), outcome, JsonSerializer.Serialize(detail, GitCapture.Json));
    }

    /// <summary>The arguments after the verb, up to the next command or redirection.</summary>
    private static string Args(Group neutralRest, string original)
    {
        // Cut on the neutral text (a separator there is real structure), then read the same span of the original.
        var cutLength = (Regex.Split(neutralRest.Value, @"&&|\|\||[|;]|\s\d?>").FirstOrDefault() ?? "").Length;
        var cut = original.Substring(neutralRest.Index, cutLength).Replace('\r', ' ').Trim();
        return cut.Length == 0 ? "" : " " + cut;
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 3)] + "...";
}
