using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kor.Operations.Framework.Capture;

/// <summary>
/// K3: what a shell command did that git does not see - a test run, a build, a migration applied, a stage-gate run.
/// Pure: the command text and its output in, an event shape (or null = record nothing) out. A command is recognised
/// only where the tool sits in COMMAND position (start, or after ; &amp;&amp; || | ( or a call operator), so
/// <c>grep "dotnet test" notes.md</c> is not a test run.
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
    private static readonly Regex Sqlcmd = new(Pos + PathPrefix + @"sqlcmd(?:\.exe)?[""']?\s[^\n;|]*?-i\s+[""']?[^""'\n]*?[\\/]db[\\/](?<file>\d{3}_[^""'\s\\/]+\.sql)", O);
    private static readonly Regex KorSql = new(@"(?:^|[;&|(\n]|-File)\s*" + PathPrefix + @"Invoke-KorSqlScript(?:\.ps1)?[""']?(?<rest>[^\n;|]*)", O);
    private static readonly Regex SqlFile = new(@"(?<file>\d{3}_[^""'\s\\/]+\.sql)", O);
    private static readonly Regex StageGate = new(Pos + PathPrefix + @"python[\w.]*[""']?(?:\s+-\w+)*\s+" + PathPrefix + @"stage_gate\.py[""']?\s+run\s+[""']?(?<file>[^""'\s;&|]+)", O);

    private static readonly Regex ExitCode = new(@"\AExit code (?<n>-?\d+)", O);

    public static ToolEvent? Classify(string? command, string? output, int? exitCode = null)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        output ??= "";
        exitCode ??= ExitCodeOf(output);
        Match m;
        if ((m = DotnetTest.Match(command)).Success)
            return Make("test", "dotnet test" + Args(m.Groups["args"].Value), TestOutcome(output, exitCode), command, output, TestSummary);
        if ((m = DotnetBuild.Match(command)).Success)
            return Make("build", "dotnet build" + Args(m.Groups["args"].Value), BuildOutcome(output, exitCode), command, output, BuildSummary);
        if ((m = Sqlcmd.Match(command)).Success)
            return Make("migration", m.Groups["file"].Value, MigrationOutcome(output, exitCode), command, output, MigrationSummary);
        if ((m = KorSql.Match(command)).Success && SqlFile.Match(m.Groups["rest"].Value) is { Success: true } f)
            return Make("migration", f.Groups["file"].Value, MigrationOutcome(output, exitCode), command, output, MigrationSummary);
        if ((m = StageGate.Match(command)).Success)
            return Make("gate", m.Groups["file"].Value, GateOutcome(output, exitCode), command, output, GateSummary);
        return null;
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
        MigFail.IsMatch(o) || exit is > 0 ? "fail" : MigDone.IsMatch(o) ? "applied" : MigTested.IsMatch(o) ? "tested" : "unknown";

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
    private static string Args(string rest)
    {
        var cut = Regex.Split(rest, @"&&|\|\||[|;]|\s\d?>").FirstOrDefault() ?? "";
        cut = cut.Trim();
        return cut.Length == 0 ? "" : " " + cut;
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 3)] + "...";
}
