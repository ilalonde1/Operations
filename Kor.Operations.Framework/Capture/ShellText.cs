using System.Text;
using System.Text.RegularExpressions;

namespace Kor.Operations.Framework.Capture;

/// <summary>The shell a command is written for: the Bash tool (Git Bash), the PowerShell tool, or cmd.exe inside one.</summary>
public enum Shell { Bash, PowerShell, Cmd }

/// <summary>
/// K7 (Framework docs/KNOWLEDGE-IDE-SPEC-K4-K7.md): where a command's text is DATA and where it is shell STRUCTURE, per
/// shell. <see cref="Neutralise"/> returns a copy of the same length in which data cannot look like structure: the bodies of
/// heredocs / here-strings (<see cref="ToolClassifier.BlankHeredocs"/>), comments, and every separator inside a quote or
/// escaped are blanked. Unlike K3's <see cref="ToolClassifier.Neutralise(string)"/> it MODELS the escapes, which was the
/// spec's named gap:
/// <list type="bullet">
/// <item>PowerShell: the backtick escapes the next character everywhere (<c>`;</c> <c>`|</c> <c>`"</c> and a backtick
/// line continuation are data, not structure); <c>""</c> inside "..." and <c>''</c> inside '...' are escaped quotes;
/// <c>#</c> after whitespace starts a comment, <c>&lt;# ... #&gt;</c> is a block comment.</item>
/// <item>Bash: <c>\x</c> escapes outside quotes and inside "..." (so <c>\;</c> in <c>find -exec</c> and <c>\"</c> are data),
/// nothing escapes inside '...', <c>$'...'</c> takes backslash escapes; <c>#</c> at the start of a word is a comment.</item>
/// <item>cmd: <c>^</c> escapes; only "..." quotes.</item>
/// <item>Command substitution is STRUCTURE, even inside "...": bash <c>$( )</c> and backticks, PowerShell <c>$( )</c> - a
/// command there runs.</item>
/// </list>
/// Not modelled (reported in <see cref="Result.Unproved"/>, never silently): a quote or substitution never closed; a
/// heredoc never closed. Known limits: a "&lt;&lt;TAG" that is not a heredoc (<c>$((1&lt;&lt;2))</c>) blanks later lines until
/// TAG (K3's rule); bash's <c>${x#y}</c> and arithmetic are read as plain text; a PowerShell comment after a non-space
/// character (<c>a#b</c> is a word) follows bash's word rule.
/// </summary>
public static class ShellText
{
    /// <summary>The neutral copy, and why it may not be the real structure (null = proved).</summary>
    public sealed record Result(string Neutral, string? Unproved);

    private enum F { Code, Dq, Sq, AnsiSq, Backtick }

    /// <summary>The characters that end a command segment, per shell (on the neutral copy only). Braces separate in
    /// PowerShell (a script block runs its commands); in bash they are a group only as words, so a leading "{ " is skipped
    /// as a prefix instead (find's {} stays an argument).</summary>
    public static bool IsSeparator(char ch, Shell shell) =>
        ch is ';' or '&' or '|' or '(' or ')' or '\n' || (shell == Shell.Bash ? ch == '`' : ch is '{' or '}');

    public static Result Neutralise(string command, Shell shell)
    {
        var c = command.Replace('\r', ' ').ToCharArray();
        string? unproved = ToolClassifier.BlankHeredocs(c) ? null : "a heredoc or here-string that is never closed";
        var n = c.Length;
        // frames: the innermost context is last; a Code frame opened by $( carries its paren depth (-1 = the top level)
        var stack = new List<(F Kind, int Depth)> { (F.Code, -1) };
        void Blank(int i) { if (i >= 0 && i < n) c[i] = ' '; }
        bool Escapes(char ch) => shell switch { Shell.PowerShell => ch == '`', Shell.Bash => ch == '\\', _ => ch == '^' };
        for (var i = 0; i < n; i++)
        {
            var ch = c[i];
            var (kind, depth) = stack[^1];
            if (kind is F.Code or F.Backtick)
            {
                if (Escapes(ch) && i + 1 < n)
                {
                    Blank(i); Blank(i + 1);
                    // a line continuation written with CRLF: the escape is followed by \r (now a space) then \n - both go
                    if (command[i + 1] == '\r' && i + 2 < n && c[i + 2] == '\n') { Blank(i + 2); i++; }
                    i++;
                    continue;
                }
                if (ch == '#' && shell != Shell.Cmd && (i == 0 || char.IsWhiteSpace(c[i - 1]) || IsSeparator(c[i - 1], shell)))
                {
                    // a comment runs to the end of the line (the newline stays: it is a real separator)
                    for (; i < n && c[i] != '\n'; i++) c[i] = ' ';
                    i--;
                    continue;
                }
                if (shell == Shell.PowerShell && ch == '<' && i + 1 < n && c[i + 1] == '#')
                {
                    var end = new string(c).IndexOf("#>", i + 2, StringComparison.Ordinal);
                    var stop = end < 0 ? n : end + 2;
                    for (var j = i; j < stop; j++) if (c[j] != '\n') c[j] = ' ';
                    if (end < 0) unproved ??= "a block comment that is never closed";
                    i = stop - 1;
                    continue;
                }
                if (ch == '"') { stack.Add((F.Dq, 0)); continue; }
                if (ch == '\'' && shell != Shell.Cmd)
                {
                    stack.Add((shell == Shell.Bash && i > 0 && c[i - 1] == '$' ? F.AnsiSq : F.Sq, 0));
                    continue;
                }
                if (shell == Shell.Bash && ch == '`')
                {
                    if (kind == F.Backtick) stack.RemoveAt(stack.Count - 1);
                    else stack.Add((F.Backtick, 0));
                    continue;
                }
                if (ch == '$' && i + 1 < n && c[i + 1] == '(' && shell != Shell.Cmd) { stack.Add((F.Code, 0)); i++; continue; }
                if (kind == F.Code && depth >= 0)
                {
                    if (ch == '(') stack[^1] = (F.Code, depth + 1);
                    else if (ch == ')')
                    {
                        if (depth == 0) stack.RemoveAt(stack.Count - 1);
                        else stack[^1] = (F.Code, depth - 1);
                    }
                }
                continue;
            }
            if (kind == F.Dq)
            {
                if (shell == Shell.Bash && ch == '\\' && i + 1 < n) { Blank(i); Blank(i + 1); i++; continue; }
                if (shell == Shell.PowerShell && ch == '`' && i + 1 < n) { Blank(i); Blank(i + 1); i++; continue; }
                if (shell == Shell.PowerShell && ch == '"' && i + 1 < n && c[i + 1] == '"') { Blank(i); Blank(i + 1); i++; continue; }
                if (ch == '"') { stack.RemoveAt(stack.Count - 1); continue; }
                if (ch == '$' && i + 1 < n && c[i + 1] == '(' && shell != Shell.Cmd) { stack.Add((F.Code, 0)); i++; continue; }
                if (shell == Shell.Bash && ch == '`') { stack.Add((F.Backtick, 0)); continue; }
                if (IsSeparator(ch, shell) || ch == '`' || ch == '\'') Blank(i);
                continue;
            }
            if (kind == F.AnsiSq && ch == '\\' && i + 1 < n) { Blank(i); Blank(i + 1); i++; continue; }
            if (shell == Shell.PowerShell && ch == '\'' && i + 1 < n && c[i + 1] == '\'') { Blank(i); Blank(i + 1); i++; continue; }
            if (ch == '\'') { stack.RemoveAt(stack.Count - 1); continue; }
            if (IsSeparator(ch, shell) || ch == '`' || ch == '"') Blank(i);
        }
        if (stack.Count > 1)
            unproved ??= stack[^1].Kind switch
            {
                F.Dq or F.Sq or F.AnsiSq => "a quote that is never closed",
                F.Backtick => "a backtick substitution that is never closed",
                _ => "a $( ) that is never closed",
            };
        return new Result(new string(c), unproved);
    }
}

/// <summary>
/// K7: one command segment - a command the shell would run - as ORIGINAL text, starting at its first word (env
/// assignments and sudo/time/env/nohup/exec/command/xargs prefixes skipped). <see cref="Origin"/> says where it came from:
/// the tool call itself, a <c>bash -c</c> / <c>pwsh -Command</c> / <c>cmd /c</c> string inside it, or a script file the
/// command runs.
/// </summary>
public sealed record CommandSegment(string Text, Shell Shell, string Origin, int Depth);

/// <summary>
/// K7: <see cref="Of"/> splits a command on the separators of its NEUTRAL copy (<see cref="ShellText"/>): ; &amp; | ( ) { }
/// newline (and a bash backtick), so &amp;&amp; || and pipes end a segment, and a segment whose neutral text is blank (a heredoc
/// body line) is no command. The pattern is then matched on the ORIGINAL text of each segment.
/// Closes the spec's same-class miss "a repeat hidden inside a script the command runs": when a segment RUNS a script file
/// (<c>pwsh tools/x.ps1</c>, <c>pwsh -File x.ps1</c>, <c>&amp; .\x.ps1</c>, <c>. ./x.ps1</c>, <c>bash x.sh</c>, <c>./x.sh</c>,
/// <c>cmd /c x.cmd</c>, <c>x.bat</c>), the script's text AS IT IS ON DISK - what will run, which is HEAD plus any uncommitted
/// edit - is segmented in its own shell and its segments are matched too, to depth 3 (a script calling a script), at most
/// 10 files of at most 1 MB. A relative path is read from the hook's cwd, moved by any <c>cd</c> / <c>Set-Location</c> /
/// <c>pushd</c> segment before it. Also unwrapped: <c>bash|sh -c "..."</c>, <c>pwsh|powershell -Command "..."</c>,
/// <c>-EncodedCommand</c> (base64 UTF-16), <c>cmd /c ...</c>.
/// Does NOT see: a script run by an interpreter it does not model (python x.py, node x.js - their text is not shell); a
/// script whose path is built at run time ($dir/x.ps1, Join-Path); a command assembled in a variable and run with
/// Invoke-Expression or eval; a function defined earlier in the session.
/// </summary>
public static class CommandSegments
{
    public const int MaxDepth = 3;
    public const int MaxScripts = 10;
    public const long MaxScriptBytes = 1_000_000;

    private static readonly Regex Prefix = new(
        @"\A(?:[A-Za-z_][A-Za-z0-9_]*=(?:""[^""]*""|'[^']*'|\S*)\s+|(?:sudo|time|nohup|exec|env|command|builtin|xargs|then|else|do|!)\s+|\{\s+)+",
        RegexOptions.CultureInvariant);

    private static readonly Regex SubShell = new(
        @"\A[""']?(?:[^\s""';&|]*[\\/])?(?<sh>bash|sh|zsh|pwsh|powershell|cmd)(?:\.exe)?[""']?" +
        @"(?<opts>(?:\s+-{1,2}(?!c\b|Command\b|EncodedCommand\b|enc\b|e\b)[A-Za-z]\w*(?:\s+(?![-/])[^\s""']+)?)*)" +
        @"\s+(?<flag>-c|-Command|-EncodedCommand|-enc|-e|/c|/k)\s+(?<arg>[\s\S]+)\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ChangeDir = new(@"\A(?:cd|Set-Location|sl|pushd|chdir)(?:\s+-(?:Path|LiteralPath))?\s+(?<d>.+)\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] ScriptExt = { ".ps1", ".psm1", ".sh", ".bash", ".cmd", ".bat" };
    private static readonly string[] Interpreters = { "bash", "sh", "zsh", "pwsh", "powershell", "cmd", "source", ".", "call", "&" };

    /// <summary>Every segment of the command, then every segment of every script it runs (depth-first).</summary>
    /// <param name="cwd">the folder the command runs in (the hook's cwd); null = scripts are not read.</param>
    public static IReadOnlyList<CommandSegment> Of(string command, Shell shell, string? cwd = null, bool readScripts = true)
    {
        var all = new List<CommandSegment>();
        var budget = new int[] { MaxScripts };
        Walk(command, shell, cwd, readScripts, "command", 0, all, budget, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return all;
    }

    /// <summary>The top-level split only: the segments' original text, in order (no unwrapping, no scripts).</summary>
    public static IReadOnlyList<string> Split(string command, Shell shell)
    {
        var neutral = ShellText.Neutralise(command, shell).Neutral;
        var text = command.Replace('\r', ' ');
        var list = new List<string>();
        var start = 0;
        for (var i = 0; i <= neutral.Length; i++)
        {
            if (i < neutral.Length && !ShellText.IsSeparator(neutral[i], shell)) continue;
            var first = start;
            while (first < i && char.IsWhiteSpace(neutral[first])) first++;
            if (first < i)
            {
                // a line continuation is ONE command: the pattern reads it joined (` in PowerShell, \ in bash, ^ in cmd)
                var cont = shell switch { Shell.PowerShell => @"`[ \t]?\n[ \t]*", Shell.Bash => @"\\[ \t]?\n[ \t]*", _ => @"\^[ \t]?\n[ \t]*" };
                var seg = Regex.Replace(text[first..i], cont, " ").TrimEnd();
                var p = Prefix.Match(seg);
                if (p.Success && p.Length < seg.Length) seg = seg[p.Length..];
                if (seg.Length > 0) list.Add(seg);
            }
            start = i + 1;
        }
        return list;
    }

    private static void Walk(string command, Shell shell, string? cwd, bool readScripts, string origin, int depth,
                             List<CommandSegment> all, int[] budget, HashSet<string> seen)
    {
        var dir = cwd;
        foreach (var seg in Split(command, shell))
        {
            all.Add(new CommandSegment(seg, shell, origin, depth));
            if (depth >= MaxDepth) continue;
            if (ChangeDir.Match(seg) is { Success: true } cd && dir is not null)
            {
                var d = ResolvePath(Unquote(cd.Groups["d"].Value.Trim()), dir);
                if (d is not null && Directory.Exists(d)) dir = d;
                continue;
            }
            if (SubShell.Match(seg) is { Success: true } sub)
            {
                var inner = InnerCommand(sub.Groups["arg"].Value.Trim(), sub.Groups["flag"].Value, sub.Groups["sh"].Value);
                if (inner is not null)
                {
                    Walk(inner, ShellOf(sub.Groups["sh"].Value), dir, readScripts, $"{origin} > {sub.Groups["sh"].Value.ToLowerInvariant()} {sub.Groups["flag"].Value}", depth + 1, all, budget, seen);
                    continue;
                }
            }
            if (!readScripts || dir is null) continue;
            var script = ScriptOf(seg);
            if (script is null) continue;
            var path = ResolvePath(script, dir);
            if (path is null || !File.Exists(path) || !seen.Add(Path.GetFullPath(path)) || budget[0] <= 0) continue;
            var info = new FileInfo(path);
            if (info.Length > MaxScriptBytes) continue;
            budget[0]--;
            string text;
            try { text = File.ReadAllText(path); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var sh = ext is ".ps1" or ".psm1" ? Shell.PowerShell : ext is ".cmd" or ".bat" ? Shell.Cmd : Shell.Bash;
            // a script runs in its CALLER's current folder (a relative path inside it is read from there, not beside it)
            Walk(text, sh, dir, readScripts, $"script {path.Replace('\\', '/')}", depth + 1, all, budget, seen);
        }
    }

    private static Shell ShellOf(string sh) => sh.ToLowerInvariant() switch
    {
        "pwsh" or "powershell" => Shell.PowerShell,
        "cmd" => Shell.Cmd,
        _ => Shell.Bash,
    };

    /// <summary>The command string given to a sub-shell, unquoted and unescaped; null when it cannot be read.</summary>
    internal static string? InnerCommand(string arg, string flag, string sh)
    {
        if (flag.Equals("-EncodedCommand", StringComparison.OrdinalIgnoreCase) || flag.Equals("-enc", StringComparison.OrdinalIgnoreCase)
            || (flag.Equals("-e", StringComparison.OrdinalIgnoreCase) && ShellOf(sh) == Shell.PowerShell))
        {
            try { return Encoding.Unicode.GetString(Convert.FromBase64String(Unquote(arg.Split(' ')[0]))); }
            catch (FormatException) { return null; }
        }
        if (arg.Length >= 2 && arg[0] == '"' && arg[^1] == '"')
        {
            var body = arg[1..^1];
            return ShellOf(sh) switch
            {
                Shell.PowerShell => body.Replace("`\"", "\"").Replace("\"\"", "\"").Replace("\\\"", "\""),
                Shell.Bash => Regex.Replace(body, @"\\([""\\$`])", "$1"),
                _ => body,
            };
        }
        if (arg.Length >= 2 && arg[0] == '\'' && arg[^1] == '\'')
            return ShellOf(sh) == Shell.PowerShell ? arg[1..^1].Replace("''", "'") : arg[1..^1];
        return arg;   // cmd /c dir & x - the rest of the line is the command
    }

    /// <summary>The script file a segment RUNS, or null: the first word when it is a script, or the first non-option
    /// argument of an interpreter (bash, sh, pwsh, powershell, cmd, source, '.', call) when that is one.</summary>
    internal static string? ScriptOf(string segment)
    {
        var words = Words(segment);
        if (words.Count == 0) return null;
        if (IsScript(words[0])) return words[0];
        var head = Path.GetFileNameWithoutExtension(words[0].Replace('\\', '/').Split('/')[^1]).ToLowerInvariant();
        if (words[0] is "." or "&") head = words[0];
        if (!Interpreters.Contains(head)) return null;
        for (var i = 1; i < words.Count; i++)
        {
            var w = words[i];
            if (IsScript(w)) return w;
            if (w.StartsWith('-') || (head == "cmd" && w.StartsWith('/'))) continue;
            return null;   // the first plain argument is not a script: it runs something else
        }
        return null;
    }

    private static bool IsScript(string w) => ScriptExt.Any(e => w.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>Words, quotes removed (a quoted word may hold spaces).</summary>
    private static List<string> Words(string s)
    {
        var words = new List<string>();
        var sb = new StringBuilder();
        char q = '\0';
        var any = false;
        foreach (var ch in s)
        {
            if (q != '\0') { if (ch == q) q = '\0'; else sb.Append(ch); continue; }
            if (ch is '"' or '\'') { q = ch; any = true; continue; }
            if (char.IsWhiteSpace(ch)) { if (any || sb.Length > 0) words.Add(sb.ToString()); sb.Clear(); any = false; continue; }
            sb.Append(ch);
        }
        if (any || sb.Length > 0) words.Add(sb.ToString());
        return words;
    }

    private static string Unquote(string s) =>
        s.Length >= 2 && (s[0] is '"' or '\'') && s[^1] == s[0] ? s[1..^1] : s;

    /// <summary>A path as the shell would read it: Git Bash's /c/x is C:/x, ~ is home, relative is under <paramref name="dir"/>.</summary>
    internal static string? ResolvePath(string p, string dir)
    {
        if (string.IsNullOrWhiteSpace(p)) return null;
        p = p.Trim();
        if (Regex.Match(p, @"\A/([A-Za-z])(/.*)?\z") is { Success: true } m) p = m.Groups[1].Value + ":" + (m.Groups[2].Success ? m.Groups[2].Value : "/");
        if (p == "~" || p.StartsWith("~/") || p.StartsWith("~\\"))
            p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p[1..];
        if (p.Contains('$') || p.Contains('%')) return null;   // built at run time: not read
        try { return Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(dir, p)); }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
    }
}
