using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Enroll;

/// <summary>A KOR repo the framework enrols: its folder (beside the others), its brain module (= the folder's leaf, as
/// ClaudeCapture.ModuleOf names it) and the pre-push guard scripts its pre-push shim must keep running.</summary>
public sealed record KorRepo(string Folder, string[] PrePushGuards)
{
    public string Module => Folder;
}

/// <summary>
/// <c>kor enroll-check</c> (Framework board row 1209, mistake 139: capture was wired only in Framework while "all repos" was
/// claimed). Three checks per repo, each naming what is missing:
/// <list type="number">
/// <item>.claude/settings.json holds every framework hook (<see cref="Required"/>): the kor start parts record, state,
/// memory (pages 1..M, every one) and board; capture-start; capture-tool on PostToolUse AND PostToolUseFailure covering
/// Bash and PowerShell; capture-check; capture-compact; capture-end; kor enforce; kor fresh --file-changed; and this
/// check itself on SessionStart. Each with --hook; a --module, where given, is the repo's module.</item>
/// <item>The git hooks (git's own hooks path, so core.hooksPath is honoured): post-commit, post-merge and post-checkout
/// each call <c>kor capture-git &lt;that hook&gt;</c>; pre-push calls <c>capture-git pre-push</c>, still runs each of the
/// repo's guards and exits with their result (<c>exit $rc</c>). Comment lines never count.</item>
/// <item>brain.Event: a module where a session RAN in the last N days (a session-end or compact event with a session id:
/// both are written by lane-level hooks, so the session was in this repo) but no session-start was written in those days
/// fails - capture-start is not reaching the DB there. N = 7 by default: the measure on 3 Nov was 46 of 46 Framework
/// sessions that ended had a start, and 5 modules had their first start that day, so any shorter window would pass a
/// module idle since its last start and a longer one would hide a break for a week more.</item>
/// </list>
/// What it does NOT check: that kor.exe at the hook's path is the installed version, or that a hook actually ran (only
/// the session check reads what was BUILT, and only for session-start); hooks outside the framework list (the Python
/// guards); settings.local.json or user settings, which could add or shadow a hook; a guard line that runs but whose
/// exit code is thrown away by something other than the shim's last line. Same-class fault it would miss: a SessionStart
/// hook whose command is right but whose timeout is too short to finish.
/// </summary>
public static class EnrollCheck
{
    public const int DefaultDays = 7;

    private const string ReviewGuard = "Framework/tools/git_hooks/pre_push_review.py";
    private const string OwnershipGuard = "Operations/tools/git_hooks/pre_push.py";

    /// <summary>The six KOR repos (as Framework tools/framework_selftest.py FOLDERS), with their pre-push guards as wired on
    /// 3 Nov: the review guard everywhere (F3), the ownership guard where a repo had it before F3.</summary>
    public static readonly IReadOnlyList<KorRepo> Repos = new[]
    {
        new KorRepo("Framework", new[] { ReviewGuard }),
        new KorRepo("Operations", new[] { OwnershipGuard, ReviewGuard }),
        new KorRepo("KOR.Drafter", new[] { OwnershipGuard, ReviewGuard }),
        new KorRepo("KOR.RevitTools", new[] { ReviewGuard }),
        new KorRepo("NetworkOps", new[] { ReviewGuard }),
        new KorRepo("KOR Inspections Bookings", new[] { ReviewGuard }),
    };

    public static readonly string[] GitCaptureHooks = { "post-commit", "post-merge", "post-checkout" };

    /// <summary>One framework hook: the settings event, the tools its matcher must cover (none = no matcher needed), the
    /// kor verb and what its arguments must say.</summary>
    public sealed record Hook(string Name, string Event, string[] Tools, string Verb, Func<KorCall, bool> Fits);

    /// <summary>A kor call found in a settings hook: the event, its matcher, the verb and the parsed options.</summary>
    public sealed record KorCall(string Event, string? Matcher, string Verb, IReadOnlyList<string> Args)
    {
        public string? Opt(string name)
        {
            var i = Args.ToList().IndexOf(name);
            return i >= 0 && i + 1 < Args.Count ? Args[i + 1] : null;
        }

        public bool Has(string flag) => Args.Contains(flag);
    }

    private static readonly string[] Shell = { "Bash", "PowerShell" };
    private static readonly string[] EnforceTools = { "Bash", "PowerShell", "Write", "Edit", "MultiEdit", "NotebookEdit" };

    public static readonly IReadOnlyList<Hook> Required = new[]
    {
        new Hook("SessionStart kor start --part record", "SessionStart", Array.Empty<string>(), "start", c => c.Opt("--part") == "record"),
        new Hook("SessionStart kor start --part state", "SessionStart", Array.Empty<string>(), "start", c => c.Opt("--part") == "state"),
        new Hook("SessionStart kor start --part memory", "SessionStart", Array.Empty<string>(), "start", c => c.Opt("--part") == "memory"),
        // --tag T alone is a board too (3 Nov: one product owner, tag-filtered board - the front-door repo's start)
        new Hook("SessionStart kor start --part board --owner ... (or --tag ...)", "SessionStart", Array.Empty<string>(), "start",
                 c => c.Opt("--part") == "board" && (c.Opt("--owner") is not null || c.Opt("--tag") is not null)),
        new Hook("SessionStart kor capture-start", "SessionStart", Array.Empty<string>(), "capture-start", _ => true),
        new Hook("SessionStart kor enroll-check", "SessionStart", Array.Empty<string>(), "enroll-check", _ => true),
        new Hook("PostToolUse (Bash|PowerShell) kor capture-tool", "PostToolUse", Shell, "capture-tool", _ => true),
        new Hook("PostToolUseFailure (Bash|PowerShell) kor capture-tool", "PostToolUseFailure", Shell, "capture-tool", _ => true),
        new Hook("Stop kor capture-check", "Stop", Array.Empty<string>(), "capture-check", _ => true),
        new Hook("PostCompact kor capture-compact", "PostCompact", Array.Empty<string>(), "capture-compact", _ => true),
        new Hook("SessionEnd kor capture-end", "SessionEnd", Array.Empty<string>(), "capture-end", _ => true),
        new Hook("PreToolUse (Bash|PowerShell|Write|Edit|MultiEdit|NotebookEdit) kor enforce", "PreToolUse", EnforceTools, "enforce", _ => true),
        new Hook("FileChanged kor fresh --file-changed", "FileChanged", Array.Empty<string>(), "fresh", c => c.Has("--file-changed")),
    };

    // kor.exe (or a bare kor) as the program, then its arguments
    private static readonly Regex KorProgram = new(@"(?:^|[\s""'/\\])kor(?:\.exe)?[""']?\s+(.*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>The arguments of a command line, split on blanks with "..." and '...' kept whole (quotes removed).</summary>
    public static List<string> SplitArgs(string s)
    {
        var args = new List<string>();
        var cur = new System.Text.StringBuilder();
        char? quote = null; var any = false;
        foreach (var ch in s)
        {
            if (quote is not null) { if (ch == quote) quote = null; else cur.Append(ch); continue; }
            if (ch is '"' or '\'') { quote = ch; any = true; continue; }
            if (char.IsWhiteSpace(ch)) { if (any || cur.Length > 0) args.Add(cur.ToString()); cur.Clear(); any = false; continue; }
            cur.Append(ch);
        }
        if (any || cur.Length > 0) args.Add(cur.ToString());
        return args;
    }

    /// <summary>Every kor call in a settings.json, or a single finding when the file cannot be read as settings.</summary>
    public static (List<KorCall> Calls, string? Unreadable) KorCalls(string? settingsJson)
    {
        var calls = new List<KorCall>();
        if (settingsJson is null) return (calls, "no .claude/settings.json");
        JsonDocument doc;
        try { doc = JsonDocument.Parse(settingsJson, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException e) { return (calls, $".claude/settings.json is not JSON ({e.Message})"); }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("hooks", out var hooks)
                || hooks.ValueKind != JsonValueKind.Object)
                return (calls, null);
            foreach (var ev in hooks.EnumerateObject())
            {
                if (ev.Value.ValueKind != JsonValueKind.Array) continue;
                foreach (var group in ev.Value.EnumerateArray())
                {
                    if (group.ValueKind != JsonValueKind.Object) continue;
                    var matcher = group.TryGetProperty("matcher", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                    if (!group.TryGetProperty("hooks", out var hs) || hs.ValueKind != JsonValueKind.Array) continue;
                    foreach (var h in hs.EnumerateArray())
                    {
                        if (h.ValueKind != JsonValueKind.Object || !h.TryGetProperty("command", out var cmd) || cmd.ValueKind != JsonValueKind.String) continue;
                        var km = KorProgram.Match(cmd.GetString()!);
                        if (!km.Success) continue;
                        var args = SplitArgs(km.Groups[1].Value);
                        if (args.Count == 0) continue;
                        calls.Add(new KorCall(ev.Name, matcher, args[0], args.Skip(1).ToList()));
                    }
                }
            }
        }
        return (calls, null);
    }

    /// <summary>Does a settings matcher fire for this tool? None, "" or "*" = every tool; otherwise Claude Code's
    /// matcher is a regular expression over the tool name (Bash|PowerShell), anchored here so "Bash" is not "BashX".</summary>
    public static bool Covers(string? matcher, string tool)
    {
        if (string.IsNullOrEmpty(matcher) || matcher == "*") return true;
        try { return Regex.IsMatch(tool, "^(?:" + matcher + ")$"); }
        catch (ArgumentException) { return string.Equals(matcher, tool, StringComparison.Ordinal); }
    }

    /// <summary>The framework hooks missing from a settings.json, by name; [] = all there.</summary>
    public static List<string> SettingsFindings(string? settingsJson, string module)
    {
        var (calls, unreadable) = KorCalls(settingsJson);
        if (unreadable is not null) return new List<string> { unreadable };
        var findings = new List<string>();
        foreach (var need in Required)
        {
            var fit = calls.Where(c => c.Event == need.Event && c.Verb == need.Verb && need.Fits(c)).ToList();
            var named = need.Name + " --hook";
            if (fit.Count == 0) { findings.Add($"missing {named}"); continue; }
            var hooked = fit.Where(c => c.Has("--hook")).ToList();
            if (hooked.Count == 0) { findings.Add($"{need.Name} has no --hook (a failure would fail the session)"); continue; }
            var wrong = hooked.Where(c => c.Opt("--module") is { } m && m != module).Select(c => c.Opt("--module")).Distinct().ToList();
            if (wrong.Count > 0) findings.Add($"{need.Name} names --module {string.Join(", ", wrong)}, not {module}");
            var uncovered = need.Tools.Where(t => !hooked.Any(c => Covers(c.Matcher, t))).ToList();
            if (uncovered.Count > 0) findings.Add($"{need.Name} does not cover {string.Join(", ", uncovered)}");
        }
        // memory pages: every page 1..M of the M the hooks declare
        var mem = calls.Where(c => c.Event == "SessionStart" && c.Verb == "start" && c.Opt("--part") == "memory" && c.Has("--hook")).ToList();
        var declared = mem.Select(c => int.TryParse(c.Opt("--pages"), out var n) ? n : 1).DefaultIfEmpty(0).Max();
        var pages = mem.Select(c => int.TryParse(c.Opt("--page"), out var p) ? p : 1).ToHashSet();
        var lost = Enumerable.Range(1, declared).Where(p => !pages.Contains(p)).ToList();
        if (lost.Count > 0) findings.Add($"missing SessionStart kor start --part memory --page {string.Join(",", lost)} of {declared}");
        return findings;
    }

    /// <summary>Non-comment lines of a hook script.</summary>
    private static List<string> CodeLines(string text) =>
        text.ReplaceLineEndings("\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();

    private static bool CallsCaptureGit(IEnumerable<string> lines, string hook) =>
        lines.Any(l => KorProgram.Match(l) is { Success: true } m && SplitArgs(m.Groups[1].Value) is var a
                       && a.Count >= 2 && a[0] == "capture-git" && a[1] == hook && a.Contains("--hook"));

    /// <summary>What is wrong with a repo's git hooks in <paramref name="hooksDir"/>; [] = all there.</summary>
    public static List<string> GitHookFindings(string? hooksDir, KorRepo repo)
    {
        if (hooksDir is null || !Directory.Exists(hooksDir)) return new List<string> { $"no git hooks folder ({hooksDir ?? "not a git repo"})" };
        var findings = new List<string>();
        foreach (var hook in GitCaptureHooks.Append("pre-push"))
        {
            var path = Path.Combine(hooksDir, hook);
            if (!File.Exists(path)) { findings.Add($"no .git/hooks/{hook}"); continue; }
            var lines = CodeLines(File.ReadAllText(path));
            if (!CallsCaptureGit(lines, hook)) findings.Add($".git/hooks/{hook} does not call kor capture-git {hook} --hook");
            if (hook != "pre-push") continue;
            foreach (var guard in repo.PrePushGuards)
                if (!lines.Any(l => l.Replace('\\', '/').Contains(guard, StringComparison.OrdinalIgnoreCase)))
                    findings.Add($".git/hooks/pre-push no longer runs its guard {guard}");
            // its LAST code line (review row 285: "contains" let `exit $rc` then `exit 0` through)
            if (lines.LastOrDefault() != "exit $rc") findings.Add(".git/hooks/pre-push does not end with its guards' result (exit $rc)");
        }
        return findings;
    }

    /// <summary>The repo's git hooks folder, as git resolves it (core.hooksPath honoured); null when git cannot say.</summary>
    public static string? HooksDir(string repoDir)
    {
        var p = Git.Run(repoDir, "rev-parse", "--git-path", "hooks")?.Trim();
        if (string.IsNullOrEmpty(p)) return null;
        return Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(repoDir, p));
    }

    /// <summary>Sessions that RAN in the module in the last <paramref name="days"/> days (distinct session ids on its
    /// session-end and compact events), and session-start events written there in the same days.</summary>
    public static (int Ran, int Started) SessionCounts(string module, int days, BrainDb? db = null) =>
        BrainDb.Use(db, d =>
        {
            using var c = d.Command(
                "SELECT (SELECT COUNT(DISTINCT Session) FROM brain.Event WHERE Module = @m AND Kind IN (N'session-end', N'compact') " +
                "AND Session IS NOT NULL AND AtUtc >= DATEADD(day, -@d, SYSDATETIMEOFFSET())), " +
                "(SELECT COUNT(*) FROM brain.Event WHERE Module = @m AND Kind = N'session-start' " +
                "AND AtUtc >= DATEADD(day, -@d, SYSDATETIMEOFFSET()))");
            c.Parameters.Add("@m", System.Data.SqlDbType.NVarChar, 60).Value = module;
            c.Parameters.Add("@d", System.Data.SqlDbType.Int).Value = days;
            using var r = c.ExecuteReader();
            r.Read();
            return (r.GetInt32(0), r.GetInt32(1));
        });

    /// <summary>The session finding, or null: sessions ran and not one session-start was written.</summary>
    public static string? SessionFinding(int ran, int started, int days) =>
        ran > 0 && started == 0
            ? $"{ran} session(s) ran here in {days} days (session-end/compact events) and no session-start was written - capture-start is not reaching brain.Event"
            : null;

    /// <summary>A repo's findings: settings, git hooks, then sessions (read through <paramref name="sessions"/>).</summary>
    public static List<string> Check(string repoDir, KorRepo repo, Func<string, (int Ran, int Started)> sessions, int days)
    {
        var settings = Path.Combine(repoDir, ".claude", "settings.json");
        var findings = SettingsFindings(File.Exists(settings) ? File.ReadAllText(settings) : null, repo.Module);
        findings.AddRange(GitHookFindings(HooksDir(repoDir), repo));
        var (ran, started) = sessions(repo.Module);
        if (SessionFinding(ran, started, days) is { } s) findings.Add(s);
        return findings;
    }

    /// <summary>The repo of this table a folder is, by its leaf name; null = not one of the six.</summary>
    public static KorRepo? RepoOf(string dir) =>
        Git.Leaf(dir) is { } leaf ? Repos.FirstOrDefault(r => string.Equals(r.Folder, leaf, StringComparison.OrdinalIgnoreCase)) : null;
}
