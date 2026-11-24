using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Enforce;

public static partial class CoordinatorRoleGuard
{
    private static List<string> RoleArguments(List<string> args, out string? session)
    {
        session = null;
        var positionals = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a == "--session") { session = args.ElementAtOrDefault(++i); continue; }
            if (a.StartsWith("--session=", StringComparison.Ordinal)) { session = a[10..]; continue; }
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                if (!a.Contains('=') && i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    && (a == "--because" || args[i + 1] is not ("role" or "set" or "worker" or "coordinator"))) i++;
                continue;
            }
            positionals.Add(a);
        }
        return positionals;
    }

    private static bool Under(string path, string root) => path.Equals(root.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> TempRoots()
    {
        yield return "C:/tmp";
        foreach (var p in new[] { Path.GetTempPath(), Environment.GetEnvironmentVariable("TEMP"), Environment.GetEnvironmentVariable("TMP") })
            if (Resolve(p, null) is { } resolved) yield return resolved;
    }

    // No git-ignore heuristic: ignored work/ bundles are project data. Checking .git also handles worktree pointer files
    // without launching git on every output path. A missing old worktree can be identified by its real main sibling.
    private static string? RepositoryRoot(string path)
    {
        try
        {
            for (var d = new DirectoryInfo(path); d is not null; d = d.Parent)
            {
                if (Directory.Exists(Path.Combine(d.FullName, ".git")) || File.Exists(Path.Combine(d.FullName, ".git")))
                    return d.FullName.Replace('\\', '/').TrimEnd('/');
                if (MainSibling(d.FullName.Replace('\\', '/')) is not null) return d.FullName.Replace('\\', '/').TrimEnd('/');
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return null;
    }

    private static string? MainSibling(string path)
    {
        // Cleanup is a ROOT sibling, not a prefix match on arbitrary nested paths.
        var parent = Path.GetDirectoryName(path);
        if (parent is null) return null;
        var name = Path.GetFileName(path);
        for (var i = name.LastIndexOf('-'); i > 0; i = name.LastIndexOf('-', i - 1))
        {
            var main = Path.Combine(parent, name[..i]);
            if (Directory.Exists(Path.Combine(main, ".git"))) return main.Replace('\\', '/');
        }
        return null;
    }

    private static bool IsRemoval(string program) => program is "rm" or "rmdir" or "del" or "erase" or "remove-item" or "ri" or "rd";

    private static bool CleanupRoot(string path)
    {
        if (path.IndexOfAny(new[] { '*', '?' }) >= 0 || MainSibling(path) is not { } main) return false;
        var listed = Git.Run(main, "worktree", "list", "--porcelain");
        if (listed is null) return false; // no failed git command is evidence of absence
        var live = listed.Split('\n').Where(l => l.StartsWith("worktree ", StringComparison.Ordinal))
            .Any(l => string.Equals(Resolve(l[9..].TrimEnd('\r'), null), path, StringComparison.OrdinalIgnoreCase));
        if (!live) return true;
        var tracked = Git.Run(path, "ls-files", "-z");
        return tracked is not null && tracked.Length == 0;
    }

    /// <summary>Only literal assignments earlier in this command. No process environment except TEMP/TMP; no execution
    /// of substitutions. Loop assignments are tainted. Each shell/script child gets a copy; its changes cannot escape.</summary>
    private sealed class ShellState
    {
        private static readonly Regex Variable = new(@"\$(?:env:)?[A-Za-z_][A-Za-z0-9_]*|\$\{(?:env:)?[A-Za-z_][A-Za-z0-9_]*\}|%[A-Za-z_][A-Za-z0-9_]*%",
            RegexOptions.CultureInvariant);
        private readonly Dictionary<string, string?> _values;
        private readonly HashSet<string> _loopAssigned;
        private readonly string _source;
        private readonly List<(int Start, int End, string? Binding)> _loops;
        private int _position;
        public List<string> Outputs { get; }

        public ShellState(string input, Shell shell)
        {
            var comparer = shell == Shell.PowerShell ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            _values = new(comparer);
            _loopAssigned = new(comparer);
            _source = ProtectAssignments(input, shell);
            _loops = new();
            Outputs = new();
            foreach (var name in new[] { "TEMP", "TMP" }) _values[name] = Environment.GetEnvironmentVariable(name);
            // Find loop bodies in structural text, not quoted data. Bash done and PS closing brace bound the taint.
            var neutral = ShellText.Neutralise(_source, shell).Neutral;
            var loopTokens = Regex.Matches(neutral, @"(?:\A|[;{}\n&|])\s*(?:(?:do|then|else)\s+)*(?<kind>for|foreach|while|until|done)\b", RegexOptions.IgnoreCase).Cast<Match>().ToList();
            foreach (var loop in loopTokens.Where(t => t.Groups["kind"].Value != "done"))
            {
                var start = loop.Groups["kind"].Index;
                var end = neutral.Length;
                var nesting = 1;
                if (shell == Shell.Bash)
                {
                    foreach (var token in loopTokens.Where(t => t.Index > loop.Index))
                    {
                        nesting += token.Groups["kind"].Value == "done" ? -1 : 1;
                        if (nesting == 0) { end = token.Index + token.Length; break; }
                    }
                }
                else if (neutral.IndexOf('{', start) is var open && open >= 0)
                {
                    for (var i = open + 1; i < neutral.Length; i++)
                    {
                        if (neutral[i] == '{') nesting++;
                        if (neutral[i] == '}' && --nesting == 0) { end = i + 1; break; }
                    }
                }
                var body = _source[start..end];
                foreach (Match a in Regex.Matches(body, @"(?:\$)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*="))
                    _loopAssigned.Add(a.Groups["name"].Value);
                var variable = Regex.Match(body, @"\A(?:for|foreach)\s*\(?\s*\$?(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase);
                _loops.Add((start, end, variable.Success ? variable.Groups["name"].Value : null));
            }
        }

        private ShellState(ShellState source)
        {
            _values = new(source._values, source._values.Comparer);
            _loopAssigned = new(source._loopAssigned, source._loopAssigned.Comparer);
            _source = source._source;
            _loops = source._loops;
            _position = source._position;
            Outputs = new(source.Outputs);
        }
        public ShellState Copy() => new(this);
        public static bool HasVariable(string s) => s.Contains('$') || s.Contains('%') || s.Contains('\uE000');

        public static string ProtectAssignments(string input, Shell shell)
        {
            if (shell == Shell.PowerShell)
                input = Regex.Replace(input, @"\$\{((?:env:)?[A-Za-z_][A-Za-z0-9_]*)\}", "\uE001$1\uE002");
            if (shell != Shell.Bash) return input;
            // CommandSegments strips environment prefixes; a stand-alone quoted assignment can otherwise be mistaken
            // for an executable prefix and expose SQL/code inside the value as shell structure. Export keeps it intact.
            var neutral = ShellText.Neutralise(input, shell).Neutral;
            var starts = Regex.Matches(neutral, @"(?:\A|[;\n&|])\s*(?:(?:do|then|else)\s+)*(?<name>[A-Za-z_][A-Za-z0-9_]*=)")
                .Select(m => m.Groups["name"].Index).Reverse();
            foreach (var start in starts)
            {
                var end = start;
                while (end < neutral.Length && !ShellText.IsSeparator(neutral[end], shell)) end++;
                var assignment = input[start..end].TrimEnd();
                // An environment prefix belongs to the following executable, which CommandSegments must still see.
                if (Regex.IsMatch(assignment, "\\A[A-Za-z_][A-Za-z0-9_]*=(?:\"[^\"]*\"|'[^']*'|[^\\s'\"]*)\\z"))
                    input = input.Insert(start, "export ");
            }
            return input;
        }

        public bool Assign(string text, Shell shell)
        {
            var at = _source.IndexOf(text, _position, StringComparison.Ordinal);
            if (at >= 0)
            {
                foreach (var loop in _loops.Where(l => l.Start >= _position && l.Start <= at))
                    if (loop.Binding is { } binding) _values[binding] = null;
                _position = at + text.Length;
            }
            var pattern = shell == Shell.PowerShell ? @"\A\$(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>.*)\z"
                : @"\A(?:export\s+)?(?<name>[A-Za-z_][A-Za-z0-9_]*)=(?<value>.*)\z";
            var m = Regex.Match(text, pattern, RegexOptions.Singleline);
            if (!m.Success) return false;
            var name = m.Groups["name"].Value;
            var value = RestoreBraces(m.Groups["value"].Value.Trim());
            var literal = Regex.IsMatch(value, "\\A(?:\"[^\"]*\"|'[^']*'|[^\\s'\";<>|&]+)\\z");
            if (!literal || value.Contains("$(", StringComparison.Ordinal) || value.Contains('`'))
            {
                _values[name] = null; // reassignment from an expression must invalidate an earlier literal
                return false;
            }
            var single = value.StartsWith('\'');
            if (value.StartsWith('"') || single) value = value[1..^1];
            if (!single) value = Expand(value);
            var inLoop = at >= 0 ? _loops.Any(l => at >= l.Start && at < l.End) : _loopAssigned.Contains(name);
            _values[name] = !inLoop && !HasVariable(value) ? value : null;
            return true;
        }

        private string Expand(string value) => Variable.Replace(value, m =>
        {
            var name = m.Value.Trim('$', '{', '}', '%');
            if (name.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
            {
                name = name[4..];
                return name is "TEMP" or "TMP" ? Environment.GetEnvironmentVariable(name) ?? m.Value : m.Value;
            }
            return _values.TryGetValue(name, out var v) && v is not null ? v : m.Value;
        });

        private static string RestoreBraces(string s) => Regex.Replace(s, "\uE001([^\uE002]+)\uE002", "${$1}");

        public (List<string> Words, List<string> Redirects) Parse(string text, Shell shell)
        {
            // Protect single-quoted variables from expansion. They are literal filenames and still fail closed.
            if (shell != Shell.Cmd) text = Regex.Replace(text, "'[^']*'", m => m.Value.Replace('$', '\uE000'));
            var (words, redirects) = DrafterPythonGuard.Parse(text, shell);
            string E(string s) => Expand(RestoreBraces(s)).Replace('\uE000', '$');
            return (words.Select(E).ToList(), redirects.Select(E).ToList());
        }

        public void CheckOutputs(string program, List<string> args, string? dir)
        {
            if (program != "dotnet" || args.FirstOrDefault() is not ("build" or "test")) return;
            for (var i = 1; i < args.Count; i++)
            {
                var flag = args[i].Split('=', 2);
                if (flag[0] is not ("--results-directory" or "-o" or "--output")) continue;
                var value = flag.Length == 2 ? flag[1] : args.ElementAtOrDefault(++i);
                if (Resolve(value, dir) is { } path) Outputs.Add(path);
            }
        }
    }
}
