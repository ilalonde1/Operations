using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Context;

namespace Kor.Operations.Framework.Enforce;

/// <summary>
/// Board row 1935 (Ian, 6 Nov): KOR.Drafter's tools/ went from 487 to 514 .py files after the 3 Nov C# port order, with nothing
/// to stop it. A call that CREATES a new .py under tools/ in the KOR.Drafter checkout is refused, whichever session or start
/// folder makes it, unless Ian's own latest message holds <see cref="Approval"/>. Each refusal is one oversight.GuardRefusal
/// row (Guard <see cref="GuardName"/>). It runs inside kor enforce because enforce already holds these tools in all 6 enrolled
/// repos, so no settings entry is added. It is not a brain.Check, because a check is a pattern on the call: it cannot ask
/// whether the file exists (a Write replacing a file must pass) and it has no typed-word lift.
///
/// FAILS OPEN: a read error, an illegal or unresolvable path, a folder that is not a checkout, an unreadable prompt record or
/// any exception means ALLOW, because a guard fault must never stop a Revit run or a job's work. Nothing here runs the
/// pipeline's tools or blocks them: only a call that would create a file is refused.
///
/// COVERS: Write, Edit (an empty old_string creates a file) and MultiEdit on a path that does not exist yet, relative paths from
/// the hook's cwd, Git Bash's /c/ form, '..' resolved. Bash and PowerShell, on the same segments kor enforce reads (so also inside
/// bash -c, pwsh -Command, cmd /c and a .sh/.ps1/.cmd the command runs): a redirect outside quotes; tee, Tee-Object, touch,
/// New-Item (with -Name too), the -Path, -LiteralPath, -FilePath or else first word of Set-Content, Add-Content and Out-File, install, ln; the destination of cp,
/// mv, git mv (from git -C's folder), copy, move, xcopy, robocopy (past their /Y switches), Copy-Item, Move-Item, ren, Rename-Item,
/// including a .py or *.py copied into a folder under tools/; a relative path after a cd.
///
/// PASSES: an Edit or a Write of an existing .py; a new .py elsewhere; a .pyc; the scratchpad; anything that only reads or runs
/// a tool; a mention in quotes, a comment, a commit message, a heredoc body or the text a cmdlet writes.
///
/// DOES NOT COVER (the same-class fault it misses first): A PYTHON FILE WRITTEN BY A PROGRAM THE SESSION RUNS. python gen.py that
/// opens tools/x.py, python -c or python - with an open() in its text, a generator, git checkout/restore/apply/stash pop, tar,
/// unzip. The hook sees the command line, and the file is named only inside the program. Also: a path held in a variable
/// ($f, built at run time, as <see cref="CommandSegments"/> says); a whole folder copied into tools/ (cp -r, robocopy /E); a
/// cmdlet flag it does not know that takes a value, read as the first word (Out-File -NoClobber is a switch, -Width is listed);
/// cp -t; curl -o and wget -O; a .pyw or .pyi; a checkout not named KOR.Drafter (the office has one, decision "nothing built on
/// the side"); a session outside the 6 enrolled repos, which runs no kor enforce; a kor older than this build. The lift reads
/// claude_prompt_recorder.py's record, as claude_api_guard.py does, so: words past that record's 4000-character clip are not seen;
/// a lead's relayed prompt to a helper reads as a user message, so a lead quoting the words lifts its helper; a session set on
/// getting round it can write that file. This holds an honest session against momentum and is not a sandbox.
/// </summary>
public static class DrafterPythonGuard
{
    public const string GuardName = "kor-drafter-python";
    public const string Approval = "IAN-APPROVED-PYTHON";
    public const string RuleName = "no new Python under KOR.Drafter tools (board row 1935)";

    /// <param name="Path">the new file, resolved.</param>
    /// <param name="How">the step that creates it.</param>
    public sealed record Refusal(string Path, string How);

    private static readonly string[] FileTools = { "Write", "Edit", "MultiEdit", "NotebookEdit" };
    private static readonly string[] NamesItsTarget = { "touch", "tee", "tee-object", "new-item", "ni", "install", "ln" };
    private static readonly string[] WritesToItsFirstWord = { "set-content", "sc", "add-content", "ac", "out-file" };
    private static readonly string[] CmdVerbs = { "copy", "xcopy", "robocopy", "move" };
    private static readonly Regex CmdSwitch = new(@"\A/[A-Za-z0-9?]+(?::\S*)?\z", RegexOptions.CultureInvariant);
    private static readonly string[] CopiesToALastWord = { "cp", "copy", "copy-item", "cpi", "mv", "move", "move-item", "mi", "xcopy" };
    private static readonly string[] Renames = { "ren", "rename", "rename-item", "rni" };
    private static readonly string[] ChangesDir = { "cd", "chdir", "pushd", "set-location", "sl" };
    private static readonly string[] ValueFlags = { "-value", "-inputobject", "-itemtype", "-type", "-encoding", "-filter", "-m", "-width" };

    private static readonly Regex InCheckoutTools = new(@"\A(?<repo>.*/KOR\.Drafter)/tools/.+\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The refusal for this call, or null. Never throws for a call it cannot read: see FAILS OPEN.</summary>
    /// <param name="input">the tool's file path, or its command.</param>
    public static Refusal? Decide(string tool, string input, string? cwd, string? scratchpad = null)
    {
        try
        {
            var dir = cwd is not null && Directory.Exists(cwd) ? cwd : null;
            if (FileTools.Contains(tool))
                return NewTool(input, dir, scratchpad) is { } f ? new Refusal(f, $"{tool} of a file that does not exist") : null;
            if (tool is not ("Bash" or "PowerShell")) return null;
            foreach (var seg in CommandSegments.Of(input, tool == "PowerShell" ? Shell.PowerShell : Shell.Bash, dir))
            {
                var (words, redirects) = Parse(seg.Text, seg.Shell);
                foreach (var r in redirects)
                    if (NewTool(r, dir, scratchpad) is { } f) return new Refusal(f, $"redirect in `{Clip(seg.Text)}`");
                if (words.Count == 0) continue;
                if (words[0] == "&" && words.Count > 1) words.RemoveAt(0);
                var prog = Prog(words[0]);
                if (ChangesDir.Contains(prog))
                {
                    var target = Args(words).LastOrDefault();
                    var moved = target is null || dir is null ? null : CommandSegments.ResolvePath(target, dir);
                    if (moved is not null && Directory.Exists(moved)) dir = moved;
                    else if (moved is null && target is not null) { var abs = CommandSegments.ResolvePath(target, Path.GetTempPath()); if (abs is not null && Path.IsPathRooted(target) && Directory.Exists(abs)) dir = abs; }
                    continue;
                }
                if (Target(prog, words, dir, scratchpad) is { } t) return new Refusal(t, $"`{Clip(seg.Text)}`");
            }
            return null;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or RegexMatchTimeoutException) { return null; }
    }

    /// <summary>Does Ian's latest recorded message hold <see cref="Approval"/>? The record is claude_prompt_recorder.py's, the
    /// file claude_api_guard.py reads. No record: no. A record that cannot be read: yes (fails open).</summary>
    public static bool Approved(string? session, string? promptsDir = null)
    {
        var path = Path.Combine(promptsDir ?? Path.Combine(Path.GetTempPath(), "claude-asked"), (string.IsNullOrWhiteSpace(session) ? "unknown" : session) + ".jsonl");
        try
        {
            var last = File.ReadAllLines(path).LastOrDefault(l => l.Trim().Length > 0);
            if (last is null) return false;
            using var doc = JsonDocument.Parse(last);
            return doc.RootElement.TryGetProperty("prompt", out var p) && p.ValueKind == JsonValueKind.String
                   && (p.GetString() ?? "").Contains(Approval, StringComparison.Ordinal);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return false; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException) { return true; }
    }

    public static string Says(Refusal r) =>
        $"No new Python under KOR.Drafter tools/ (board row 1935; Ian, 6 Nov). This would create {r.Path.Replace('\\', '/')}: {r.How}. " +
        "Build it in C#: KOR.Drafter's src, or kor in Operations (Kor.Operations.Framework). Or get Ian's typed words: his latest message " +
        $"must hold {Approval}. An Edit of an existing .py, a Write replacing one, a new .py elsewhere and the scratchpad all pass.";

    /// <summary>The resolved path when this names a .py under tools/ of a KOR.Drafter checkout that does not exist yet, else null.</summary>
    private static string? NewTool(string word, string? dir, string? scratchpad)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;
        if (dir is null && !IsRooted(word)) return null;
        var full = CommandSegments.ResolvePath(word, dir ?? Path.GetTempPath());
        if (full is null) return null;
        var slashed = full.Replace('\\', '/');
        if (!slashed.EndsWith(".py", StringComparison.OrdinalIgnoreCase)) return null;
        var m = InCheckoutTools.Match(slashed);
        if (!m.Success) return null;
        var repo = m.Groups["repo"].Value;
        var git = repo + "/.git";
        if (!Directory.Exists(repo + "/tools") || !(Directory.Exists(git) || File.Exists(git))) return null;
        if (File.Exists(full) || Directory.Exists(full)) return null;
        return ContextGuard.UnderScratchpad(slashed, scratchpad) ? null : full;
    }

    private static bool IsRooted(string w) => Path.IsPathRooted(w) || w.StartsWith('~');

    /// <summary>The new tool this program would write, or null.</summary>
    private static string? Target(string prog, List<string> words, string? dir, string? scratchpad)
    {
        var rest = words.Skip(1).ToList();
        if (prog == "git")
        {
            var i = 0;
            while (i < rest.Count && rest[i].StartsWith('-'))
            {
                // git -C moves where the paths after it are read from
                if (rest[i] == "-C" && i + 1 < rest.Count)
                    dir = dir is null && !IsRooted(rest[i + 1]) ? null : CommandSegments.ResolvePath(rest[i + 1], dir ?? Path.GetTempPath());
                i += rest[i] is "-C" or "-c" ? 2 : 1;
            }
            if (i >= rest.Count || rest[i] != "mv") return null;
            prog = "mv";
            rest = rest.Skip(i + 1).ToList();
        }
        var args = Args(rest.Prepend("").ToList(), CmdVerbs.Contains(prog));
        if (prog is "new-item" or "ni" && FlagValue(rest, "-name") is { } itemName)
        {
            // the parent is -Path, or the word that is not the name (PowerShell's positional Path)
            var parentPath = FlagValue(rest, "-path") ?? args.FirstOrDefault(a => a != itemName);
            return NewTool(parentPath is null ? itemName : parentPath + "/" + itemName, dir, scratchpad);
        }
        // a named path wins; else the first word names the file and a second is the text it writes
        if (WritesToItsFirstWord.Contains(prog))
        {
            var named = FlagValue(rest, "-path") ?? FlagValue(rest, "-literalpath") ?? FlagValue(rest, "-filepath");
            return named is not null ? NewTool(named, dir, scratchpad) : args.Count > 0 ? NewTool(args[0], dir, scratchpad) : null;
        }
        if (NamesItsTarget.Contains(prog)) return args.Select(a => NewTool(a, dir, scratchpad)).FirstOrDefault(n => n is not null);
        string? dest;
        IEnumerable<string> sources;
        if (prog == "robocopy" && args.Count >= 2)
        {
            dest = args[1];
            sources = args.Skip(2);
        }
        else if (Renames.Contains(prog) && args.Count >= 2)
        {
            // Rename-Item and ren take a bare new name, beside the old file
            var name = args[1];
            var parent = name.IndexOfAny(new[] { '/', '\\' }) < 0 ? Path.GetDirectoryName(args[0].Replace('\\', '/')) : null;
            return NewTool(string.IsNullOrEmpty(parent) ? name : parent + "/" + name, dir, scratchpad);
        }
        else if (CopiesToALastWord.Contains(prog) && args.Count >= 1)
        {
            dest = DestinationFlag(rest) ?? args[^1];
            sources = args.Where(a => !ReferenceEquals(a, dest) && a != dest);
        }
        else return null;
        if (NewTool(dest, dir, scratchpad) is { } direct) return direct;
        // a copy INTO a folder under tools/: the file it makes takes the source's name
        var folder = dir is null && !IsRooted(dest) ? null : CommandSegments.ResolvePath(dest, dir ?? Path.GetTempPath());
        if (folder is null || !Directory.Exists(folder)) return null;
        foreach (var s in sources)
        {
            if (!s.EndsWith(".py", StringComparison.OrdinalIgnoreCase)) continue;
            var inside = NewTool(Path.Combine(folder, Path.GetFileName(s.Replace('\\', '/'))), dir, scratchpad);
            if (inside is not null) return inside;
        }
        return null;
    }

    private static string? DestinationFlag(List<string> rest)
    {
        for (var i = 0; i + 1 < rest.Count; i++)
            if (rest[i].StartsWith("-dest", StringComparison.OrdinalIgnoreCase)) return rest[i + 1];
        return null;
    }

    private static string? FlagValue(List<string> rest, string flag)
    {
        for (var i = 0; i + 1 < rest.Count; i++)
            if (rest[i].Equals(flag, StringComparison.OrdinalIgnoreCase)) return rest[i + 1];
        return null;
    }

    /// <summary>The words after the first that are not flags, nor the value of a flag that holds text rather than a path. For a
    /// cmd copy, a /Y or /R:3 is a switch; a path like /c/x has a second slash and is not.</summary>
    private static List<string> Args(List<string> words, bool cmdSwitches = false)
    {
        var args = new List<string>();
        for (var i = 1; i < words.Count; i++)
        {
            var w = words[i];
            if (w.Length == 0) continue;
            if (cmdSwitches && CmdSwitch.IsMatch(w)) continue;
            if (w.StartsWith('-'))
            {
                if (ValueFlags.Contains(w.ToLowerInvariant())) i++;
                continue;
            }
            args.Add(w);
        }
        return args;
    }

    private static string Prog(string word)
    {
        var name = word.Replace('\\', '/').Split('/')[^1].ToLowerInvariant();
        return name.EndsWith(".exe", StringComparison.Ordinal) ? name[..^4] : name;
    }

    private static string Clip(string s)
    {
        var one = s.Split('\n')[0];
        return one.Length <= 160 ? one : one[..157] + "...";
    }

    /// <summary>A segment's words with quotes removed, and the targets of its redirects (a '>' outside quotes). A 2> or *> is a
    /// redirect like any other; what it names is judged the same way.</summary>
    internal static (List<string> Words, List<string> Redirects) Parse(string seg, Shell shell)
    {
        var words = new List<string>();
        var redirects = new List<string>();
        var cur = new StringBuilder();
        var any = false;
        var nextIsRedirect = false;
        char q = '\0';
        void Flush()
        {
            if (!any && cur.Length == 0) return;
            if (nextIsRedirect) { redirects.Add(cur.ToString()); nextIsRedirect = false; }
            else words.Add(cur.ToString());
            cur.Clear();
            any = false;
        }
        for (var i = 0; i < seg.Length; i++)
        {
            var ch = seg[i];
            var next = i + 1 < seg.Length ? seg[i + 1] : '\0';
            if (q != '\0')
            {
                if (ch == q) { q = '\0'; continue; }
                if (q == '"' && shell == Shell.Bash && ch == '\\' && next == '"') { cur.Append('"'); i++; continue; }
                if (q == '"' && shell == Shell.PowerShell && ch == '`' && next != '\0') { cur.Append(next); i++; continue; }
                cur.Append(ch);
                continue;
            }
            // a comment runs to the end of the segment: a # at the start of a word, outside quotes, in bash or PowerShell
            if (ch == '#' && shell != Shell.Cmd && cur.Length == 0 && !any) break;
            if (ch is '"' or '\'' && !(ch == '\'' && shell == Shell.Cmd)) { q = ch; any = true; continue; }
            if (shell == Shell.Bash && ch == '\\' && next == ' ') { cur.Append(' '); i++; continue; }
            if (shell == Shell.PowerShell && ch == '`' && next != '\0') { cur.Append(next); i++; continue; }
            if (ch == '>')
            {
                // the stream number or * in front of it is part of the operator
                if (cur.Length > 0 && !any && cur.ToString().All(c => char.IsDigit(c) || c == '*')) cur.Clear();
                else Flush();
                if (next == '>') i++;
                nextIsRedirect = true;
                continue;
            }
            if (char.IsWhiteSpace(ch)) { Flush(); continue; }
            cur.Append(ch);
        }
        Flush();
        return (words, redirects);
    }
}
