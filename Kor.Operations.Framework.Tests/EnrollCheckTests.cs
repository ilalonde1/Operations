using System.Text.Json.Nodes;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Enroll;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Board row 1209 (mistake 139), kor enroll-check, on COPIES of the real files: this repo's .claude/settings.json and its
/// .git/hooks shims (read as git resolves the hooks path). Covers: the real copies pass; removing ANY ONE kor hook entry
/// of the real settings - each of them, enumerated from the file, not a list written here - fails, and the finding names
/// that hook (event, verb, and the start part or its memory or records page); a board entry with --tag in place of
/// --owner passes and one with neither fails; a narrowed matcher, a dropped --hook and a wrong --module
/// each fail naming it; a git hook file deleted, its capture-git line removed or commented out, a pre-push guard line
/// removed, or pre-push no longer exiting with its guards' result each fail naming it; the session rule (ran, none
/// started) fails and the other three cases pass; the live session counts read brain.Event; the verb's hook mode prints
/// ONE line and exits 0 on a failing repo, and its plain mode exits 1.
/// Does NOT cover: settings.local.json or user settings; that kor.exe at the hook path is the installed build; a hook
/// entry present but disabled by a matcher no tool name matches other than the ones listed.
/// </summary>
public class EnrollCheckTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, ".git"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string RealSettings() => File.ReadAllText(Path.Combine(RepoRoot(), ".claude", "settings.json"));

    private static KorRepo This => EnrollCheck.RepoOf(RepoRoot())!;

    /// <summary>(description, settings JSON with that one kor hook removed) for every kor hook entry in the real file.</summary>
    public static IEnumerable<object[]> EachKorHookRemoved()
    {
        var root = JsonNode.Parse(RealSettings())!;
        var hooks = root["hooks"]!.AsObject();
        foreach (var (ev, groups) in hooks.ToList())
            for (var g = 0; g < groups!.AsArray().Count; g++)
            {
                var hs = groups[g]!["hooks"]!.AsArray();
                for (var h = 0; h < hs.Count; h++)
                {
                    var cmd = hs[h]!["command"]?.GetValue<string>() ?? "";
                    if (!cmd.Contains("kor.exe")) continue;
                    var copy = JsonNode.Parse(root.ToJsonString())!;
                    copy["hooks"]![ev]![g]!["hooks"]!.AsArray().RemoveAt(h);
                    yield return new object[] { $"{ev}: {cmd[(cmd.IndexOf("kor.exe") + 8)..]}", copy.ToJsonString() };
                }
            }
    }

    [Fact]
    public void TheRealSettingsPass()
    {
        var findings = EnrollCheck.SettingsFindings(RealSettings(), This.Module);
        // G5's hooks reach this repo at install, after kor (Framework docs/G5-CONTEXT-STOPS.md): none yet is allowed, some is not
        if (findings.Count(f => f.Contains("row 1474", StringComparison.Ordinal)) == 3) findings.RemoveAll(f => f.Contains("row 1474", StringComparison.Ordinal));
        Assert.Empty(findings);
    }

    [Fact]
    public void EveryKorHookIsEnumerated()
    {
        // the real file holds 4 memory pages, 4 records pages, record, state, board, capture-start, enroll-check, capture-tool
        // x2 or x3, capture-check, capture-compact, capture-end, enforce, fresh: at least 20 - an enumeration that found few would
        // make the theory below pass by testing nothing
        Assert.True(EachKorHookRemoved().Count() >= 20, $"only {EachKorHookRemoved().Count()} kor hooks found");
    }

    [Theory]
    [MemberData(nameof(EachKorHookRemoved))]
    public void RemovingAnyOneKorHookFailsNamingIt(string removed, string settings)
    {
        var findings = EnrollCheck.SettingsFindings(settings, This.Module);
        Assert.NotEmpty(findings);
        var ev = removed[..removed.IndexOf(':')];
        var args = EnrollCheck.SplitArgs(removed[(removed.IndexOf(':') + 1)..]);
        var text = string.Join(" | ", findings);
        Assert.Contains(ev, text);
        Assert.Contains(args[0], text);   // the verb
        var part = args.IndexOf("--part") is var i and >= 0 ? args[i + 1] : null;
        if (part is not null) Assert.Contains($"--part {part}", text);
        if (args.IndexOf("--page") is var p and >= 0) Assert.Contains($"--page {args[p + 1]}", text);
    }

    private static string Mutate(Action<JsonNode> change)
    {
        var root = JsonNode.Parse(RealSettings())!;
        change(root);
        return root.ToJsonString();
    }

    private static IEnumerable<JsonNode> KorHooks(JsonNode root, string ev, string verb) =>
        root["hooks"]![ev]!.AsArray().SelectMany(g => g!["hooks"]!.AsArray())
            .Where(h => (h!["command"]?.GetValue<string>() ?? "").Contains($"kor.exe {verb}"))!;

    [Fact]
    public void ANarrowedMatcherFailsNamingTheToolLeftOut()
    {
        var s = Mutate(r =>
        {
            foreach (var g in r["hooks"]!["PostToolUse"]!.AsArray())
                if (g!["hooks"]!.AsArray().Any(h => (h!["command"]?.GetValue<string>() ?? "").Contains("capture-tool")))
                    g["matcher"] = "Bash";
        });
        Assert.Contains(EnrollCheck.SettingsFindings(s, This.Module), f => f.Contains("PostToolUse") && f.Contains("does not cover PowerShell"));
    }

    // 3 Nov (one product owner, tag-filtered board): a board entry with --tag and no --owner is a board; one with neither is not
    [Theory]
    [InlineData(" --tag \"Revit Tools\"", false)]
    [InlineData("", true)]
    public void ABoardEntryNeedsAnOwnerOrATag(string replacement, bool fails)
    {
        var s = Mutate(r =>
        {
            foreach (var h in KorHooks(r, "SessionStart", "start"))
            {
                var c = h!["command"]!.GetValue<string>();
                if (c.Contains("--part board"))
                    h["command"] = System.Text.RegularExpressions.Regex.Replace(c, @" --owner (""[^""]*""|\S+)", "")
                                       .Replace(" --no-helpers", "").Replace(" --hook", replacement + " --hook");
            }
        });
        Assert.DoesNotContain("--owner", s);   // the mutation took: the board entry has no owner left
        Assert.Equal(replacement.Length > 0, s.Contains("--tag"));
        var findings = EnrollCheck.SettingsFindings(s, This.Module);
        Assert.Equal(fails, findings.Any(f => f.Contains("--part board")));
    }

    [Fact]
    public void ADroppedHookFlagFailsNamingIt()
    {
        var s = Mutate(r =>
        {
            foreach (var h in KorHooks(r, "Stop", "capture-check"))
                h["command"] = h["command"]!.GetValue<string>().Replace(" --hook", "");
        });
        Assert.Contains(EnrollCheck.SettingsFindings(s, This.Module), f => f.Contains("Stop kor capture-check has no --hook"));
    }

    // row 1393: the Stop capture-check reads the ending turn off the transcript - an entry left at the old 15 s fails, naming it
    [Theory]
    [InlineData(15, true)]
    [InlineData(29, true)]
    [InlineData(30, false)]
    [InlineData(null, false)]   // not set: Claude Code's default, 60 s
    public void AStopCheckTimeoutUnder30SecondsFailsNamingIt(int? timeout, bool fails)
    {
        var s = Mutate(r =>
        {
            foreach (var h in KorHooks(r, "Stop", "capture-check"))
                if (timeout is { } t) h["timeout"] = t; else h.AsObject().Remove("timeout");
        });
        var findings = EnrollCheck.SettingsFindings(s, This.Module);
        Assert.Equal(fails, findings.Any(f => f.Contains("Stop kor capture-check has a timeout of") && f.Contains("under 30 s") && f.Contains("row 1393")));
    }

    [Fact]
    public void AWrongModuleFailsNamingIt()
    {
        var s = Mutate(r =>
        {
            foreach (var h in KorHooks(r, "PreToolUse", "enforce"))
                h["command"] = "C:/KOR-Operations/kor/kor.exe enforce --module \"SomewhereElse\" --hook";
        });
        Assert.Contains(EnrollCheck.SettingsFindings(s, This.Module), f => f.Contains("enforce names --module SomewhereElse"));
    }

    [Fact]
    public void NoSettingsFileFails() =>
        Assert.Equal(new[] { "no .claude/settings.json" }, EnrollCheck.SettingsFindings(null, This.Module));

    // ---- git hooks ----------------------------------------------------------------------------------------------------
    private static string CopyRealHooks()
    {
        var real = EnrollCheck.HooksDir(RepoRoot());
        Assert.NotNull(real);
        var dir = Path.Combine(Path.GetTempPath(), "kor-enroll-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        foreach (var h in EnrollCheck.GitCaptureHooks.Append("pre-push"))
            File.Copy(Path.Combine(real!, h), Path.Combine(dir, h));
        return dir;
    }

    private static void Rewrite(string dir, string hook, Func<string, bool> dropLine, Func<string, string>? change = null)
    {
        var p = Path.Combine(dir, hook);
        var lines = File.ReadAllLines(p).Where(l => !dropLine(l)).Select(l => change is null ? l : change(l)).ToArray();
        File.WriteAllLines(p, lines);
    }

    [Fact]
    public void TheRealGitHooksPass()
    {
        var dir = CopyRealHooks();
        try { Assert.Empty(EnrollCheck.GitHookFindings(dir, This)); }
        finally { Directory.Delete(dir, true); }
    }

    public static IEnumerable<object[]> GitHooks() => EnrollCheck.GitCaptureHooks.Append("pre-push").Select(h => new object[] { h });

    [Theory]
    [MemberData(nameof(GitHooks))]
    public void AGitHookWithoutItsCaptureLineFailsNamingIt(string hook)
    {
        foreach (var how in new[] { "deleted", "line removed", "line commented out" })
        {
            var dir = CopyRealHooks();
            try
            {
                if (how == "deleted") File.Delete(Path.Combine(dir, hook));
                else if (how == "line removed") Rewrite(dir, hook, l => l.Contains("capture-git"));
                else Rewrite(dir, hook, _ => false, l => l.Contains("capture-git") && !l.TrimStart().StartsWith('#') ? "# " + l : l);
                var findings = EnrollCheck.GitHookFindings(dir, This);
                Assert.True(findings.Any(f => f.Contains($"hooks/{hook}")), $"{hook} {how}: {string.Join(" | ", findings)}");
                Assert.DoesNotContain(findings, f => EnrollCheck.GitCaptureHooks.Append("pre-push").Where(h => h != hook).Any(o => f.Contains($"hooks/{o} ")));
            }
            finally { Directory.Delete(dir, true); }
        }
    }

    [Fact]
    public void APrePushThatDropsAGuardFailsNamingIt()
    {
        Assert.NotEmpty(This.PrePushGuards);
        foreach (var guard in This.PrePushGuards)
        {
            var dir = CopyRealHooks();
            try
            {
                var script = Path.GetFileName(guard);
                Rewrite(dir, "pre-push", l => !l.TrimStart().StartsWith('#') && l.Contains(script));
                Assert.Contains(EnrollCheck.GitHookFindings(dir, This), f => f.Contains($"no longer runs its guard {guard}"));
            }
            finally { Directory.Delete(dir, true); }
        }
    }

    [Fact]
    public void APrePushThatNoLongerExitsWithItsGuardsResultFails()
    {
        var dir = CopyRealHooks();
        try
        {
            Rewrite(dir, "pre-push", l => l.Trim() == "exit $rc");
            Assert.Contains(EnrollCheck.GitHookFindings(dir, This), f => f.Contains("exit $rc"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void APrePushThatThrowsAwayItsGuardsResultAfterExitRcFails()
    {
        // review row 285: `exit $rc` still in the file, but an `exit 0` after it decides
        var dir = CopyRealHooks();
        try
        {
            File.AppendAllText(Path.Combine(dir, "pre-push"), "exit 0\n");
            Assert.Contains(EnrollCheck.GitHookFindings(dir, This), f => f.Contains("does not end with its guards' result"));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- sessions -----------------------------------------------------------------------------------------------------
    [Theory]
    [InlineData(3, 0, true)]
    [InlineData(0, 0, false)]
    [InlineData(3, 1, false)]
    [InlineData(0, 2, false)]
    public void TheSessionRuleFailsOnlyWhenSessionsRanAndNoneStarted(int ran, int started, bool fails) =>
        Assert.Equal(fails, EnrollCheck.SessionFinding(ran, started, 7) is not null);

    [Fact]
    public void TheLiveSessionCountsReadBrainEvent()
    {
        // Operations wired capture-start and capture-end on 3 Nov (c8f9b4c9): both were written there that day (a
        // session-end at 11:18:47 -07:00, read 3 Nov) - so both halves of the count are checked against brain.Event
        // (review row 285: `ran >= 0` could not fail)
        var (ran, started) = EnrollCheck.SessionCounts("Operations", 7);
        Assert.True(started > 0, $"Operations: {started} session-start in 7 days");
        Assert.True(ran > 0, $"Operations: {ran} session(s) with a session-end or compact in 7 days");
        var (ran0, started0) = EnrollCheck.SessionCounts("no-such-module-" + Guid.NewGuid().ToString("N")[..6], 7);
        Assert.Equal((0, 0), (ran0, started0));
    }

    // ---- the verb -----------------------------------------------------------------------------------------------------
    [Fact]
    public void HookModePrintsOneLineAndExitsZeroOnAFailingRepo()
    {
        // a scratch git repo named like a KOR repo, with no settings and no hooks: every check fails
        var parent = Path.Combine(Path.GetTempPath(), "kor-enroll-verb-" + Guid.NewGuid().ToString("N")[..8]);
        var dir = Path.Combine(parent, "NetworkOps");
        Directory.CreateDirectory(dir);
        try
        {
            Assert.NotNull(Git.Run(dir, "init", "-q"));
            var output = new StringWriter(); var error = new StringWriter();
            var log = Path.Combine(parent, "hook.log");
            Assert.Equal(0, Program.Run(new[] { "enroll-check", "--hook", "--dir", dir }, () => null, output, error, log));
            var lines = output.ToString().ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
            Assert.Single(lines);
            Assert.StartsWith("kor enroll-check: FAIL - NetworkOps: no .claude/settings.json", lines[0]);
            Assert.Contains("no .git/hooks/post-commit", lines[0]);
            // plain mode: exit 1 FROM A FINDING - Program.Run also returns 1 on a caught exception, so the verdict line is
            // read and the error writer must stay empty (review row 288)
            output = new StringWriter();
            Assert.Equal(1, Program.Run(new[] { "enroll-check", "--dir", dir }, () => null, output, error, log));
            Assert.StartsWith("kor enroll-check: FAIL - NetworkOps: no .claude/settings.json", output.ToString());
            Assert.Equal("", error.ToString());
        }
        finally { try { Directory.Delete(parent, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
