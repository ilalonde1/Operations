using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Enforce;

/// <summary>
/// Ian's coordinator boundary (#139076): delegate project writes and product runs; briefs, handovers, git, kor and
/// checks remain available. Shared ShellText/CommandSegments and DrafterPythonGuard's tokenizer are used, not a new hook.
/// COVERS: named file tools, literal shell targets including directory changes/previous directory/stack, redirects,
/// common write/copy/move/delete commands, named product runners and shell wrappers within CommandSegments' depth/size
/// limits. Checks every segment, including after an allowed verb. The named install-kor.ps1 check is exempt from inspection.
/// DOES NOT COVER: arbitrary program effects, variables/aliases/functions generated at runtime, symlinks/junctions, shell
/// grammar beyond CommandSegments, or tools not enrolled in enforce. In particular dotnet test can rewrite fixtures, and
/// python -c can write files. This holds ordinary work against momentum; it is not a sandbox. Self-demotion requires the
/// same typed lift as project work. Unresolved write targets refuse; unknown programs pass.
/// </summary>
public static partial class CoordinatorRoleGuard
{
    public const string GuardName = "coordinator-role";
    public const string Approval = "IAN-APPROVED-COORDINATOR-WORK";
    public sealed record Decision(bool Refuse, string Rule, string? Target);
    private static readonly Decision Allow = new(false, "coordinator work or check", null);
    private static readonly string[] ChangesDir = { "cd", "chdir", "set-location", "sl", "pushd", "push-location" };
    private static readonly string[] FirstTarget = { "set-content", "sc", "add-content", "ac", "out-file" };
    private static readonly string[] AllTargets = { "tee", "tee-object", "touch", "mkdir", "md", "new-item", "ni", "rm", "rmdir", "del", "erase", "remove-item", "ri", "rd" };
    private static readonly string[] Copies = { "cp", "copy", "copy-item", "cpi", "xcopy", "install", "ln" };
    private static readonly string[] Moves = { "mv", "move", "move-item", "mi", "ren", "rename", "rename-item" };
    private static readonly string[] ValueFlags = { "-value", "-inputobject", "-encoding", "-width", "-itemtype", "-type", "-filter", "-include", "-exclude", "-m" };

    /// <summary>Read-only hook decision. role is the latest stored role; startFolder is the persisted origin, never today's cd.</summary>
    public static Decision Evaluate(string hookJson, string? role, string? startFolder)
    {
        using var doc = JsonDocument.Parse(hookJson);
        var root = doc.RootElement;
        var agent = S(root, "agent_id");
        var transcript = S(root, "transcript_path")?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(agent) || transcript?.Contains("/subagents/", StringComparison.OrdinalIgnoreCase) == true)
            return new(false, "subagent", null);
        var cwd = S(root, "cwd");
        role ??= SessionRoles.DefaultRole(startFolder ?? cwd);
        if (role != "coordinator") return new(false, role == "worker" ? "worker" : "no role", null);
        var tool = S(root, "tool_name");
        if (tool is null || !root.TryGetProperty("tool_input", out var ti)) return Allow;
        var input = S(ti, Rules.InputFieldOf(tool));
        if (input is null) return Allow;
        var session = S(root, "session_id");
        if (tool is "Write" or "Edit" or "MultiEdit" or "NotebookEdit")
            return Write(input, cwd, session, "project file edit") ?? new(false, "allowed coordinator file", Resolve(input, cwd));
        if (tool is not ("Bash" or "PowerShell")) return Allow;
        return ShellDecision(input, Rules.ShellOf(tool), cwd, session) ?? Allow;
    }

    public static string Says(Decision d) =>
        $"Coordinator role refuses {d.Rule}: {d.Target}. Delegate this work to a worker. " +
        (d.Rule.StartsWith("unresolved", StringComparison.Ordinal) ? "Unresolved target: assign it in the same command or use a literal path. " : "") +
        "You may write briefs and handovers, run git/kor, read files, and check a worker with dotnet build/test or install-kor.ps1. " +
        $"Only Ian's typed {Approval} in this session lifts this boundary for 12 hours; the lift is logged.";

    private static Decision? ShellDecision(string input, Shell shell, string? cwd, string? session)
    {
        var dir = cwd;
        string? previous = null;
        var stack = new Stack<string?>();
        var state = new ShellState(input, shell);
        // State is scoped by depth so a child cd cannot move its parent. The explicitly allowed install script's writes
        // are part of the check; its descendants are skipped, but later commands at the caller's depth are still judged.
        var frames = new Dictionary<int, (string? Dir, string? Previous, Stack<string?> Stack, ShellState State)>();
        var depth = 0;
        int? skipBelow = null;
        foreach (var seg in CommandSegments.Of(ShellState.ProtectAssignments(input, shell), shell, cwd))
        {
            if (skipBelow is { } skip && seg.Depth > skip) continue;
            skipBelow = null;
            if (seg.Depth > depth)
            {
                frames[depth] = (dir, previous, new Stack<string?>(stack.Reverse()), state);
                stack = new Stack<string?>(stack.Reverse());
                state = state.Copy();
            }
            else if (seg.Depth < depth && frames.TryGetValue(seg.Depth, out var frame))
                (dir, previous, stack, state) = frame;
            depth = seg.Depth;
            if (state.Assign(seg.Text, seg.Shell)) continue;
            var (words, redirects) = state.Parse(seg.Text, seg.Shell);
            if (words.Count == 0) continue;
            if (words[0] is "&" or "." && words.Count > 1) words.RemoveAt(0);
            var program = Prog(words[0]);
            var rest = words.Skip(1).ToList();
            var args = Arguments(rest, program == "sed");
            state.CheckOutputs(program, rest, dir);
            Decision? Target(string? word, string rule) => Write(word, dir, session, rule, state.Outputs);
            foreach (var target in redirects)
                if (Target(target, "shell write") is { } refusal) return refusal;
            var roleArgs = program == "kor" ? rest : program == "dotnet" && rest.Count > 0 && Prog(rest[0]) == "kor" ? rest.Skip(1).ToList()
                : program == "dotnet" && rest.Count > 1 && rest[0] == "exec" && Prog(rest[1]) == "kor" ? rest.Skip(2).ToList() : null;
            if (roleArgs is not null && RoleArguments(roleArgs, out var targetSession).Take(3).SequenceEqual(new[] { "role", "set", "worker" }))
            {
                if (targetSession == session || targetSession is null || ShellState.HasVariable(targetSession))
                    return new(true, "self-demotion", targetSession ?? session);
            }
            if (ChangesDir.Contains(program))
            {
                var target = rest.Contains("-") ? "-" : Named(rest, "-path", "-literalpath") ?? args.FirstOrDefault();
                if (program is "pushd" or "push-location") stack.Push(dir);
                var next = target == "-" ? previous : Resolve(target, dir) ?? target;
                previous = dir;
                dir = next; // unknown cd must NOT leave the old cwd in effect
                continue;
            }
            if (program is "popd" or "pop-location")
            {
                previous = dir;
                dir = stack.Count > 0 ? stack.Pop() : null;
                continue;
            }
            if (Product(program, words, dir, session) is { } product) return new(true, "product run", product);
            var script = CommandSegments.ScriptOf(seg.Text);
            if (Resolve(script, dir)?.EndsWith("/tools/install-kor.ps1", StringComparison.OrdinalIgnoreCase) == true)
                skipBelow = seg.Depth;
            if (FirstTarget.Contains(program))
            {
                var target = Named(rest, "-path", "-literalpath", "-filepath") ?? args.FirstOrDefault();
                if (Target(target, "shell write") is { } r) return r;
            }
            else if (AllTargets.Contains(program))
            {
                var named = Named(rest, "-path", "-literalpath", "-filepath");
                var targets = named is null ? args : new List<string> { named };
                var name = Named(rest, "-name");
                if (program is "new-item" or "ni" && name is not null)
                    targets = new List<string> { (named ?? args.FirstOrDefault(a => a != name) ?? ".") + "/" + name };
                foreach (var target in targets)
                {
                    if (IsRemoval(program) && Resolve(target, dir) is { } path && CleanupRoot(path)) continue;
                    if (Target(target, "shell write") is { } r) return r;
                }
            }
            else if (Copies.Contains(program) || Moves.Contains(program) || program == "robocopy")
            {
                var dest = Named(rest, "-destination", "-dest", "-target-directory", "-t") ?? (program == "robocopy" ? args.ElementAtOrDefault(1) : args.LastOrDefault());
                if (Target(dest, "shell destination") is { } r) return r;
                // A move removes the source too, even when its destination is an allowed scratchpad.
                if (Moves.Contains(program))
                    foreach (var source in args.Where(a => a != dest))
                        if (Target(source, "shell source removal") is { } moved) return moved;
            }
            else if (program == "sed" && rest.Any(w => w == "--in-place" || w.StartsWith("--in-place=", StringComparison.Ordinal) || w.StartsWith("-i", StringComparison.Ordinal)))
            {
                // First positional word is the expression (unless -e/-f supplied it).
                foreach (var target in args.Skip(rest.Contains("-e") || rest.Contains("-f") ? 0 : 1))
                    if (Target(target, "in-place edit") is { } r) return r;
            }
        }
        return new(false, "coordinator work or check", dir);
    }

    private static string? Product(string program, List<string> words, string? dir, string? session)
    {
        if (program is "takeoff" or "kor.operations.engineeringtools.takeoffcli") return Resolve(words[0], dir) ?? words[0];
        if (program.StartsWith("build_bundle", StringComparison.Ordinal) && words[0].EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            return Resolve(words[0], dir) ?? words[0];
        if (program == "dotnet" && words.Skip(1).Any(w => w.Contains("Kor.Operations.EngineeringTools.TakeoffCli", StringComparison.OrdinalIgnoreCase))
            && words.ElementAtOrDefault(1) is not ("build" or "test")) return words.First(w => w.Contains("TakeoffCli", StringComparison.OrdinalIgnoreCase));
        // Interpret only executable/script positions. A read or an echo mentioning a script is data.
        string? script = null;
        if (program.StartsWith("python", StringComparison.Ordinal) || program is "py")
        {
            for (var i = 1; i < words.Count; i++)
            {
                if (words[i] is "-c" or "-m") break;
                if (words[i] is "-X" or "-W") { i++; continue; }
                if (words[i].StartsWith('-')) continue;
                script = words[i]; break;
            }
            if (script is null) return null;
            var path = Resolve(script, dir);
            if (Prog(script).StartsWith("build_bundle", StringComparison.Ordinal) && script.EndsWith(".py", StringComparison.OrdinalIgnoreCase)) return path ?? script;
            // A build-output write exception does not turn execution of a work/ product script into a check.
            if (path?.Contains("/work/", StringComparison.OrdinalIgnoreCase) == true && !TempRoots().Any(t => Under(path, t))) return path;
            return null;
        }
        script = CommandSegments.ScriptOf(string.Join(" ", words.Select(w => "\"" + w + "\"")));
        if (script is null) return null;
        var full = Resolve(script, dir);
        return full is not null && (full.EndsWith("/docs/one-tool/run-on-210/send-set.ps1", StringComparison.OrdinalIgnoreCase)
                               || full.EndsWith("/docs/one-tool/run-on-210/start-on-210.ps1", StringComparison.OrdinalIgnoreCase)) ? full : null;
    }

    private static Decision? Write(string? word, string? dir, string? session, string rule, List<string>? outputs = null)
    {
        // Stream duplication and the null sink do not write a project file.
        if (word is "/dev/null" or "NUL" or "nul" || word is not null && Regex.IsMatch(word, @"\A&[0-9-]+\z")) return null;
        var target = Resolve(word, dir);
        if (target is not null && AllowedPath(target, session)) return null;
        if (target is not null && outputs?.Any(p => Under(target, p)) == true) return null;
        return new(true, target is null ? "unresolved " + rule : rule,
            target ?? (dir is not null && ShellState.HasVariable(dir) ? dir + "/" + word : word) ?? "(missing target)");
    }

    internal static bool AllowedPath(string path, string? session)
    {
        if (Regex.IsMatch(path, @"/docs/codex/CODEX-[^/]+\.md\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return true;
        if (Regex.IsMatch(path, @"/Framework/(?:docs/HANDOVER\.md|lanes(?:/.+)?)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return true;
        if (path.Equals("C:/KOR-Operations/push-exceptions.txt", StringComparison.OrdinalIgnoreCase)) return true;
        if (TempRoots().Any(t => Under(path, t))) return true;
        var repo = RepositoryRoot(path);
        return repo is not null && path.Length > repo.Length && path[(repo.Length + 1)..].Split('/').Any(p =>
            p.Equals("obj", StringComparison.OrdinalIgnoreCase) || p.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || p.Equals("TestResults", StringComparison.OrdinalIgnoreCase));
    }

    internal static string? Resolve(string? word, string? dir)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;
        word = word.Replace('\\', '/');
        if (word.StartsWith("//?/", StringComparison.Ordinal)) word = word[4..];
        if (word == "/tmp" || word.StartsWith("/tmp/", StringComparison.Ordinal)) word = "C:" + word;
        if (ShellState.HasVariable(word) || dir is not null && ShellState.HasVariable(dir) && !Path.IsPathRooted(word)) return null;
        if (dir is null && !Path.IsPathRooted(word) && !Regex.IsMatch(word, @"\A/[A-Za-z]/")) return null;
        var full = CommandSegments.ResolvePath(word, dir ?? Environment.CurrentDirectory)?.Replace('\\', '/');
        return full is { Length: > 3 } ? full.TrimEnd('/') : full;
    }

    private static List<string> Arguments(List<string> rest, bool sed)
    {
        var result = new List<string>();
        for (var i = 0; i < rest.Count; i++)
        {
            var w = rest[i];
            if (w.StartsWith('-')) { if (ValueFlags.Contains(w.ToLowerInvariant()) || sed && (w is "-e" or "-f")) i++; continue; }
            if (new[] { "/y", "/s", "/e", "/q", "/i", "/h", "/r" }.Contains(w, StringComparer.OrdinalIgnoreCase)) continue;
            result.Add(w);
        }
        return result;
    }
    private static string? Named(List<string> rest, params string[] flags)
    {
        for (var i = 0; i + 1 < rest.Count; i++)
            if (flags.Contains(rest[i], StringComparer.OrdinalIgnoreCase)) return rest[i + 1];
        return null;
    }
    private static string Prog(string word) => Path.GetFileNameWithoutExtension(word.Replace('\\', '/').Split('/')[^1]).ToLowerInvariant();
    internal static string? S(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
