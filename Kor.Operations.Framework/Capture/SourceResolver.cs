using System.Text.RegularExpressions;

namespace Kor.Operations.Framework.Capture;

/// <summary>
/// K3 - WHAT A TOOL RESULT IS CITED TO. The rule, stated once (review rows 93-96 were five instances of one class: a
/// result cited to a folder that was guessed, not proved):
///
///   A result is cited to the path it ran on - the project, .sql file or pipeline named in the command, else the folder
///   the command was in - and ONLY when that path is PROVED. Anything not proved is UNKNOWN: no repo, no commit, and the
///   reason is recorded. Never a guess.
///
/// PROVED means every step from the hook's cwd (the folder BEFORE the command ran) to the path is modelled and the
/// path exists:
/// - the command's structure is proved (<see cref="ToolClassifier.Scan"/>: no escaped quote, $'...', backtick,
///   unclosed quote or unclosed heredoc);
/// - every folder change before the tool is replayed: cd / chdir / sl / Set-Location replace the folder, pushd /
///   Push-Location push first, popd / Pop-Location restore; in bash a ( ... ) or $( ... ) group restores the folder when
///   it closes (a subshell), in PowerShell it does not (the location is the runspace's). A change is proved only when it
///   has exactly one literal folder argument after the known flags (-P -L -e -@ -- -Path -LiteralPath), that folder
///   exists, and it is joined to what follows by &amp;&amp;, ; , a newline or a closing ')'. UNKNOWN otherwise: no
///   argument (cd goes home, pushd swaps), '-' or '+N', another option, a second argument, ~ $VAR %VAR%, a backslash-
///   escaped space, a relative cd not starting with '.' while CDPATH is set (bash), a folder that does not exist (the cd failed), a change
///   joined by || (the tool runs only if it FAILED), | (a pipeline stage) or a lone &amp; (background), popd with
///   arguments, an unbalanced ')', and anything else in command position that may change the folder (source, '.',
///   eval, exec, Invoke-Expression, iex);
/// - the path the tool ran on: a test/build's literal arguments that name an existing project or solution file, or a
///   folder holding one (none = the folder; more than one = UNKNOWN; a non-literal argument = UNKNOWN); a migration's or
///   gate's path token, which must be literal and exist.
///
/// Not provable from the command text, so NOT covered: a folder change hidden in an alias or shell function, or in a
/// script the command runs (they cannot be seen); a project chosen by a Directory.Build.props or a global.json.
/// </summary>
public static class SourceResolver
{
    /// <summary>What the resolver may ask of the file system (tests pass a fake one).</summary>
    public sealed record Probe(Func<string, bool> DirExists, Func<string, bool> FileExists, Func<string, bool> DirHasProject,
                               string? CdPath)
    {
        public static Probe Real => new(Directory.Exists, File.Exists, HasProject, Environment.GetEnvironmentVariable("CDPATH"));

        private static bool HasProject(string dir)
        {
            try { return Directory.EnumerateFiles(dir).Any(IsProjectFile); } catch { return false; }
        }
    }

    /// <param name="Path">the proved path (a file or folder), or null = UNKNOWN.</param>
    /// <param name="Why">how it was proved, or why it could not be.</param>
    public sealed record Source(string? Path, string Why);

    private const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex FolderOp = new(
        @"(?:^|[;&|(\n])\s*(?:(?<pop>popd|Pop-Location)|(?<verb>cd|chdir|pushd|Set-Location|sl|Push-Location)|(?<other>source|\.|eval|exec|Invoke-Expression|iex))(?=$|[\s;&|)])(?<args>[^;&|\n)]*)", O);
    private static readonly Regex ArgToken = new(@"""[^""]*""|'[^']*'|\S+", RegexOptions.CultureInvariant);
    private static readonly string[] CdFlags = { "-p", "-l", "-e", "-@", "--", "-path", "-literalpath" };
    private static readonly string[] ProjectExtensions = { ".csproj", ".fsproj", ".vbproj", ".proj", ".sln", ".slnx" };

    internal static bool IsProjectFile(string path) =>
        ProjectExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    /// <param name="shell">"powershell" for the PowerShell tool; anything else is read as bash (the Bash tool).</param>
    public static Source Resolve(string command, string? shell, string? cwd, ToolClassifier.ToolEvent t, Probe probe)
    {
        var (neutral, unproved) = ToolClassifier.Scan(command);
        if (unproved is not null) return new(null, $"the command's structure is not proved: {unproved}");
        var powershell = string.Equals(shell, "powershell", StringComparison.OrdinalIgnoreCase);
        var (folder, folderWhy) = Folder(command, neutral, powershell, cwd, t.At, probe);
        var target = t.TargetLength > 0 ? command.Substring(t.TargetIndex, t.TargetLength) : "";
        return t.Kind is "test" or "build"
            ? Project(target, folder, folderWhy, probe)
            : NamedFile(target, folder, folderWhy, probe, t.Kind);
    }

    // ---- the folder ---------------------------------------------------------------------------------------------------
    private sealed record State(string? Dir, string Why, Stack<(string? Dir, string Why)> Stack);

    internal static (string? Dir, string Why) Folder(string original, string neutral, bool powershell, string? cwd, int at, Probe probe)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return (null, "the hook gave no cwd");
        var start = Full(cwd);
        var s = start is not null && probe.DirExists(start)
            ? new State(start, "the hook's cwd", new Stack<(string?, string)>())
            : new State(null, "the hook's cwd does not exist", new Stack<(string?, string)>());

        // Events before the tool, in order: folder operations (at their verb) and, in bash, real ( and ) - subshells.
        var events = new List<(int At, Match? Op, char Paren)>();
        for (var m = FolderOp.Match(neutral); m.Success; m = m.NextMatch())
        {
            var verbAt = OpIndex(m);
            if (verbAt < at) events.Add((verbAt, m, '\0'));
        }
        if (!powershell)
            for (var i = 0; i < at && i < neutral.Length; i++)
                if (neutral[i] is '(' or ')') events.Add((i, null, neutral[i]));
        events.Sort((a, b) => a.At.CompareTo(b.At));

        var groups = new Stack<State>();
        foreach (var (_, op, paren) in events)
        {
            if (paren == '(') { groups.Push(s with { Stack = new Stack<(string?, string)>(s.Stack.Reverse()) }); continue; }
            if (paren == ')')
            {
                s = groups.Count > 0 ? groups.Pop() : s with { Dir = null, Why = "an unbalanced ')'" };
                continue;
            }
            s = Apply(original, neutral, op!, s, powershell, probe);
        }
        return (s.Dir, s.Why);
    }

    private static int OpIndex(Match m) =>
        m.Groups["pop"].Success ? m.Groups["pop"].Index : m.Groups["verb"].Success ? m.Groups["verb"].Index : m.Groups["other"].Index;

    private static State Apply(string original, string neutral, Match m, State s, bool powershell, Probe probe)
    {
        var name = (m.Groups["pop"].Success ? m.Groups["pop"] : m.Groups["verb"].Success ? m.Groups["verb"] : m.Groups["other"]).Value;
        if (m.Groups["other"].Success) return s with { Dir = null, Why = $"'{name}' may change the folder" };

        // How the change is joined to what follows: only && ; newline ) or the end prove it held.
        var after = neutral[(m.Groups["args"].Index + m.Groups["args"].Length)..].TrimStart(' ', '\t');
        if (after.StartsWith("||")) return s with { Dir = null, Why = $"'{name}' is joined by || (what follows runs only if it failed)" };
        if (after.StartsWith('|')) return s with { Dir = null, Why = $"'{name}' is a pipeline stage" };
        if (after.StartsWith('&') && !after.StartsWith("&&")) return s with { Dir = null, Why = $"'{name}' runs in the background" };

        var args = original.Substring(m.Groups["args"].Index, m.Groups["args"].Length);
        if (m.Groups["pop"].Success)
        {
            if (Tokens(args).Count > 0) return s with { Dir = null, Why = $"'{name}' with arguments" };
            if (s.Stack.Count == 0) return s;                       // an empty stack: the folder does not change
            var (dir, why) = s.Stack.Pop();
            return s with { Dir = dir, Why = why };
        }

        var (target, targetWhy) = Target(args);
        var push = name.ToLowerInvariant() is "pushd" or "push-location";
        var next = target is null ? (Dir: (string?)null, Why: $"'{name}': {targetWhy}") : Into(target, s, powershell, probe, name);
        if (push) s.Stack.Push((s.Dir, s.Why));
        return s with { Dir = next.Dir, Why = next.Why };
    }

    private static (string? Dir, string Why) Into(string target, State s, bool powershell, Probe probe, string name)
    {
        if (target.StartsWith('~') || target.Contains('$') || target.Contains('%'))
            return (null, $"'{name} {target}' is not a literal folder");
        var path = Msys(target);
        string? dir;
        if (System.IO.Path.IsPathRooted(path)) dir = Full(path);
        // bash searches CDPATH for a relative folder that does not start with '.' (cd ./x and cd ../x never use it).
        else if (!powershell && !string.IsNullOrEmpty(probe.CdPath) && !path.StartsWith('.'))
            return (null, $"'{name} {target}' is relative and CDPATH is set");
        else if (s.Dir is null) return (null, $"'{name} {target}' is relative to an unknown folder ({s.Why})");
        else dir = Full(System.IO.Path.Combine(s.Dir, path));
        if (dir is null || !probe.DirExists(dir)) return (null, $"'{name} {target}': the folder does not exist (the change failed)");
        return (dir, $"{name} {target}");
    }

    /// <summary>
    /// The one literal folder argument of a cd-like command, after the known flags; or null and why. Redirections end
    /// the arguments (cd C:/x 2>/dev/null).
    /// </summary>
    internal static (string? Target, string Why) Target(string args)
    {
        if (args.Contains("\\ ")) return (null, "a backslash-escaped space");
        var tokens = Tokens(args);
        var i = 0;
        while (i < tokens.Count && CdFlags.Contains(tokens[i].ToLowerInvariant())) i++;
        if (i == tokens.Count) return (null, "no folder argument (cd goes home, pushd swaps)");
        var v = tokens[i];
        if (v.StartsWith('-') || v.StartsWith('+')) return (null, $"'{v}' is not a folder");
        if (i + 1 < tokens.Count) return (null, "a second argument (bash and PowerShell refuse it; the folder did not change)");
        return (v.Trim('"', '\''), "");
    }

    private static List<string> Tokens(string args) =>
        ArgToken.Matches(args).Select(t => t.Value).TakeWhile(v => !Regex.IsMatch(v, @"^\d*[<>]")).ToList();

    // ---- what the tool ran on ------------------------------------------------------------------------------------------
    private static Source Project(string args, string? folder, string folderWhy, Probe probe)
    {
        var found = new List<string>();
        foreach (var token in Tokens(args))
        {
            if (token.StartsWith('-')) continue;                    // an option; its value is checked as a token below
            var v = token.Trim('"', '\'');
            if (v.StartsWith('~') || v.Contains('$') || v.Contains('%'))
                return new(null, $"the argument '{v}' is not literal (it may name the project)");
            var p = Msys(v);
            if (!System.IO.Path.IsPathRooted(p))
            {
                if (folder is null) return new(null, $"the argument '{v}' is relative to an unknown folder ({folderWhy})");
                p = System.IO.Path.Combine(folder, p);
            }
            var full = Full(p);
            if (full is null) continue;
            if (probe.FileExists(full) && IsProjectFile(full)) found.Add(full);
            else if (probe.DirExists(full) && probe.DirHasProject(full)) found.Add(full);
        }
        var distinct = found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count == 1) return new(distinct[0], "the project named in the command");
        if (distinct.Count > 1) return new(null, $"{distinct.Count} projects are named in the command");
        return folder is null ? new(null, folderWhy) : new(folder, $"the folder it ran in ({folderWhy})");
    }

    private static Source NamedFile(string token, string? folder, string folderWhy, Probe probe, string kind)
    {
        var v = token.Trim().Trim('"', '\'');
        if (v.Length == 0) return new(null, $"no {kind} file is named");
        if (v.StartsWith('~') || v.Contains('$') || v.Contains('%')) return new(null, $"the {kind} file '{v}' is not literal");
        var p = Msys(v);
        if (!System.IO.Path.IsPathRooted(p))
        {
            if (folder is null) return new(null, $"the {kind} file '{v}' is relative to an unknown folder ({folderWhy})");
            p = System.IO.Path.Combine(folder, p);
        }
        var full = Full(p);
        return full is not null && probe.FileExists(full)
            ? new(full, $"the {kind} file named in the command")
            : new(null, $"the {kind} file '{v}' does not exist where the command ran");
    }

    // ---- paths ---------------------------------------------------------------------------------------------------------
    /// <summary>A Git Bash path (/c/x) as a Windows one (C:/x).</summary>
    private static string Msys(string path) =>
        Regex.Match(path, @"^/([a-zA-Z])(/.*)?$") is { Success: true } m
            ? m.Groups[1].Value.ToUpperInvariant() + ":" + (m.Groups[2].Success ? m.Groups[2].Value : "/")
            : path;

    private static string? Full(string path)
    {
        try { return System.IO.Path.GetFullPath(path); } catch { return null; }
    }
}
