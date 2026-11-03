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
/// Migration and gate file NAMES are matched on the ORIGINAL text of the command's segment (the neutral copy only
/// finds the command and where its segment ends), so a '(' or ';' inside a quoted name stays in it (review rows 87,
/// 88). Misses by design: one event per command - the first kind that matches wins (test, build, migration, gate), so
/// <c>sqlcmd -i db/1.sql &amp;&amp; dotnet test</c> is a test; a sqlcmd comma list (-i a.sql,b.sql) records only the
/// first file; a gate name whose opening quote is never closed runs to the end of its line.
///
/// Outcome is read from the OUTPUT, never assumed: a fail marker beats a pass marker; with neither, a non-zero exit
/// (PostToolUseFailure's "Exit code N") is 'fail', otherwise 'unknown'.
///
/// Does NOT see: a test or build run inside a script the command calls (only the command line is matched); output a
/// tool wrote to a file instead of stdout; the exit code of a PostToolUse (success) call, which the docs do not carry.
/// </summary>
public static class ToolClassifier
{
    /// <param name="At">where in the command the tool was found (the directory it ran in is decided by any cd before it).</param>
    public sealed record ToolEvent(string Kind, string Ref, string Outcome, string Detail, int At = 0);

    // The verb alone (a bare "cd" counts - review row 94); its arguments run to the next real separator or ')'.
    private static readonly Regex Cd = new(Pos + @"(?:(?<pop>popd|Pop-Location)|(?<verb>cd|chdir|pushd|Set-Location|sl|Push-Location))(?=$|[\s;&|)])(?<args>[^;&|\n)]*)", O);
    private static readonly Regex ArgToken = new(@"""[^""]*""|'[^']*'|\S+", RegexOptions.CultureInvariant);
    private static readonly string[] CdFlags = { "-p", "-l", "-e", "-@", "--", "-path", "-literalpath" };

    /// <param name="Changed">a cd / pushd / popd came before the tool, so the hook's cwd does NOT say where it ran.</param>
    /// <param name="Dir">where it ran, or null = UNKNOWN (an unmodelled target): the caller records no repo or commit.</param>
    public sealed record CdResult(bool Changed, string? Dir);

    /// <summary>
    /// The folder the tool at <paramref name="at"/> ran in, replaying every cd-like command in command position before
    /// it (found on the neutral copy, read from the original), starting from the hook's cwd - which is the folder BEFORE
    /// the command ran (#74 on 2 Nov: an Operations build was cited against Framework's HEAD). cd / chdir / sl /
    /// Set-Location replace the folder; pushd / Push-Location push the old one first; popd / Pop-Location restore it
    /// (review row 93). The folder is the first argument after the known flags (<see cref="Target"/>). A relative path
    /// resolves against the folder current at that point; a Git Bash path (/c/x) becomes C:/x. These make the folder
    /// UNKNOWN until an absolute cd or a popd - the event then carries no repo/commit rather than a guessed one: a bare
    /// cd or pushd (review row 94), cd -, an option not in the known list, ~, $VAR, %VAR%. Still wrong, same class:
    /// a cd inside a subshell that ended before the tool ( (cd x; make) &amp;&amp; dotnet test ) is replayed as if it
    /// persisted; a cd hidden behind a function or alias, or run by a script the command calls, is not seen.
    /// </summary>
    public static CdResult DirectoryBefore(string command, int at, string? cwd)
    {
        var neutral = Neutralise(command);
        var current = cwd is null ? null : Full(cwd);
        var stack = new Stack<string?>();
        var changed = false;
        for (var m = Cd.Match(neutral); m.Success && m.Index < at; m = m.NextMatch())
        {
            changed = true;
            if (m.Groups["pop"].Success)
            {
                if (stack.Count > 0) current = stack.Pop();
                continue;
            }
            var g = m.Groups["args"];
            var target = Resolve(Target(command.Substring(g.Index, g.Length)), current);
            if (m.Groups["verb"].Value.ToLowerInvariant() is "pushd" or "push-location") stack.Push(current);
            current = target;
        }
        return new CdResult(changed, current);
    }

    /// <summary>
    /// The folder argument of a cd-like command (the original text of its arguments): the first token after the known
    /// flags (-P -L -e -@ -- -Path -LiteralPath), quotes removed. Null - UNKNOWN - when there is none (a bare cd goes
    /// home, a bare pushd swaps the top two folders) or the next token is another option.
    /// </summary>
    internal static string? Target(string args)
    {
        foreach (Match t in ArgToken.Matches(args))
        {
            var v = t.Value;
            if (CdFlags.Contains(v.ToLowerInvariant())) continue;
            if (v.StartsWith('-')) return null;               // "-" (previous folder) or an option not modelled
            return v.Trim('"', '\'');
        }
        return null;
    }

    private static string? Resolve(string? dir, string? current)
    {
        if (string.IsNullOrEmpty(dir) || dir.StartsWith('~') || dir.Contains('$') || dir.Contains('%')) return null;
        if (Regex.Match(dir, @"^/([a-zA-Z])(/.*)?$") is { Success: true } msys)
            dir = msys.Groups[1].Value.ToUpperInvariant() + ":" + (msys.Groups[2].Success ? msys.Groups[2].Value : "/");
        if (Path.IsPathRooted(dir)) return Full(dir);
        return current is null ? null : Full(Path.Combine(current, dir));
    }

    private static string? Full(string path)
    {
        try { return Path.GetFullPath(path); } catch { return null; }
    }

    private const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    // Command position: start of text/line, or after ; & | ( - "&&" and "||" end in & and |, pwsh's "&" call operator too.
    private const string Pos = @"(?:^|[;&|(\n])\s*";
    // An optional folder before the tool: a quoted one may hold spaces ("C:/VIsual Studio Projects/..."), a bare one not.
    private const string PathPrefix = @"(?:[""'][^""'\n]*?[\\/]|[""']|[^""'\s;&|]*[\\/])?";

    private static readonly Regex DotnetTest = new(Pos + PathPrefix + @"dotnet(?:\.exe)?[""']?\s+test\b(?<args>[^\n]*)", O);
    private static readonly Regex DotnetBuild = new(Pos + PathPrefix + @"dotnet(?:\.exe)?[""']?\s+build\b(?<args>[^\n]*)", O);
    // Structure, on the neutral copy: the tool in command position, and its segment up to a real ; | or newline.
    private static readonly Regex Sqlcmd = new(Pos + PathPrefix + @"sqlcmd(?:\.exe)?[""']?(?<rest>\s[^\n;|]*)", O);
    private static readonly Regex KorSql = new(@"(?:^|[;&|(\n]|-File)\s*" + PathPrefix + @"Invoke-KorSqlScript(?:\.ps1)?[""']?(?<rest>[^\n;|]*)", O);
    private static readonly Regex StageGate = new(Pos + PathPrefix + @"python[\w.]*[""']?(?:\s+-\w+)*\s+" + PathPrefix + @"stage_gate\.py[""']?\s+run\s+[""']?(?<file>[^""'\s;&|()<>]+)", O);
    // Names, on the ORIGINAL segment: NNN_ + no whitespace, quote or slash, ending at the FIRST ".sql" (lazy), so
    // "130_a.sql)" / "130_a.sql>log" / "130_a.sql,db/131_b.sql" give 130_a.sql and "130_a;131_b.sql" stays whole.
    private const string MigrationName = @"(?<file>\d{3}_[^""'\s\\/]+?\.sql)";
    private static readonly Regex SqlcmdFile = new(@"-i\s*[""']?(?:[^""'\n]*?[\\/])?db[\\/]" + MigrationName, O);
    private static readonly Regex SqlFile = new(@"(?<![^\s""'\\/=,(])" + MigrationName, O);

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
            return Make("test", "dotnet test" + Args(m.Groups["args"], command), TestOutcome(output, exitCode), command, output, TestSummary, m.Index);
        if ((m = DotnetBuild.Match(neutral)).Success)
            return Make("build", "dotnet build" + Args(m.Groups["args"], command), BuildOutcome(output, exitCode), command, output, BuildSummary, m.Index);
        // Migrations and gates (review rows 87, 88 - the rule, stated once): the NEUTRAL copy decides structure only - is
        // the tool in command position, and where its command segment ends (a real ; | or newline). The NAME is matched
        // on the ORIGINAL text of that segment, so a '(' or ';' inside a quoted name is part of it, and a name ends where
        // its pattern ends (a migration: the first ".sql"; a gate: its closing quote, or for a bare name the first
        // whitespace, quote, ; & | ( ) < or > - a ',' stays in a bare gate name: "run p.json,x" gives "p.json,x").
        if ((m = Sqlcmd.Match(neutral)).Success && SqlcmdFile.Match(Segment(command, m.Groups["rest"])) is { Success: true } s)
            return Make("migration", s.Groups["file"].Value, MigrationOutcome(output, exitCode), command, output, MigrationSummary, m.Index);
        if ((m = KorSql.Match(neutral)).Success && SqlFile.Match(Segment(command, m.Groups["rest"])) is { Success: true } k)
            return Make("migration", k.Groups["file"].Value, MigrationOutcome(output, exitCode), command, output, MigrationSummary, m.Index);
        if ((m = StageGate.Match(neutral)).Success)
            return Make("gate", GateName(command, m.Groups["file"]), GateOutcome(output, exitCode), command, output, GateSummary, m.Index);
        return null;
    }

    /// <summary>The original text of a group found on the neutral copy (same length, so same positions).</summary>
    private static string Segment(string original, Group neutralGroup) => original.Substring(neutralGroup.Index, neutralGroup.Length);

    /// <summary>
    /// A gate's pipeline file. Unquoted text is identical in both copies (only quoted text and heredoc bodies are
    /// blanked), so the matched value is the name. A quoted one runs, in the original, to its closing quote - or to the
    /// end of the line when the quote is never closed.
    /// </summary>
    private static string GateName(string original, Group file)
    {
        var i = file.Index;
        if (i == 0 || original[i - 1] is not ('"' or '\'')) return file.Value;
        var close = original.IndexOfAny(new[] { original[i - 1], '\n' }, i);
        return original[i..(close < 0 ? original.Length : close)];
    }

    private static readonly Regex HeredocStart = new(@"(?<!<)<<(?!<)-?\s*[""']?(?<tag>\w+)[""']?", RegexOptions.CultureInvariant);
    private static readonly Regex HereStringStart = new(@"@[""']\s*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The command with every character that is DATA rather than shell structure replaced by a space, length kept:
    /// separators (; &amp; | ( ) newline) inside '...' or "..." quotes, and the body lines of a bash heredoc (&lt;&lt;TAG ...
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
            else if (ch is ';' or '&' or '|' or '(' or ')' or '\n') c[i] = ' ';
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

    private static ToolEvent Make(string kind, string reference, string outcome, string command, string output, Regex summary, int at)
    {
        var lines = (Redact.Text(output) ?? "").Replace("\r", "").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
        var detail = new
        {
            command = Clip(Redact.Text(command)!, 400),
            summary = lines.Where(l => summary.IsMatch(l)).Take(20).Select(l => Clip(l, 300)),
            tail = lines.Skip(Math.Max(0, lines.Count - 5)).Select(l => Clip(l, 300)),
            lines = lines.Count,
        };
        return new ToolEvent(kind, Clip(Redact.Text(reference)!.Trim(), 400), outcome, JsonSerializer.Serialize(detail, GitCapture.Json), at);
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
