using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Enroll;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Board row 2206: the copies rule of kor enroll-check, ported from KOR.Drafter tools/copies_check.py. Covers: the six cases
/// of copies_check_test.py (MaxCopies 6: seven copies fail, six part-named pass); planted faults - a clean merged copy is a
/// fault, a dirty one and an unreadable-status one are not (the unreadable one reads "not checked"); a non-part name; outside
/// home; fault order count, names, merged, not checked; an unresolved shared ref named; a deadline reached gives "not checked";
/// an expired cherry gives "not checked", never "not merged" or ok; merge-base exit 1 read as "no", not as a failure; every git
/// call carries -c safe.directory=* before its subcommand; branches matched to for-each-ref by the porcelain branch line;
/// porcelain paths with spaces; the --hook line capped; an exception is one ERROR line; Git.Exec's exit code and expiry.
/// Does NOT cover: a real KOR.Drafter worktree set (the live run in the row's report does); git cherry's patch-equivalence
/// itself (git's own). Same-class fault it would miss: a real git that prints porcelain keys this fake never sends.
/// </summary>
public class CopiesCheckTests
{
    private const string H = "C:/VIsual Studio Projects/";

    private static WorktreeCopy C(string leaf, string? branch = "x", string sha = "aaa") => new(H + leaf, branch is null ? null : "refs/heads/" + branch, sha);

    private static CopiesVerdict F(IReadOnlyList<WorktreeCopy> cs, Func<WorktreeCopy, MergeRead>? merged = null, Func<WorktreeCopy, bool?>? dirty = null) =>
        CopiesCheck.Faults(cs, merged ?? (_ => MergeRead.No), dirty ?? (_ => false));

    // ---- the six cases of copies_check_test.py ------------------------------------------------------------------------
    [Fact]
    public void AProductPartNamePasses()
    {
        Assert.Null(CopiesCheck.NameFault(H + "KOR.Drafter-wood-frame"));
        Assert.Null(CopiesCheck.NameFault("C:\\VIsual Studio Projects\\KOR.Drafter-catalogue"));
    }

    [Fact]
    public void ATaskNameFails()
    {
        Assert.Contains("product-part", CopiesCheck.NameFault(H + "KOR.Drafter-dimtext"));
        Assert.Contains("product-part", CopiesCheck.NameFault(H + "KOR.Drafter-codex-a3-07"));
    }

    [Fact]
    public void OutsideHomeFails()
    {
        Assert.Contains("not under", CopiesCheck.NameFault("C:/KOR.Drafter-work/kd-wood"));
        // nested one deeper inside home (the agent worktrees under KOR.Drafter/.claude/worktrees)
        Assert.Contains("not under", CopiesCheck.NameFault(H + "KOR.Drafter/.claude/worktrees/KOR.Drafter-plan"));
    }

    [Fact]
    public void AMergedCopyFails()
    {
        var v = F(new[] { C("KOR.Drafter-catalogue") }, _ => MergeRead.Merged("fs01/x"));
        Assert.Contains(v.Faults, f => f.StartsWith("merged into fs01/x; remove it once its session has ended", StringComparison.Ordinal));
        Assert.DoesNotContain(v.Faults, f => f.Contains("remove it now", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SevenCopiesFailAndSixPartNamedPass()
    {
        var parts = new[] { "wood-frame", "plan", "revit", "etabs", "safe", "viewer", "catalogue" };
        var seven = parts.Select(p => C("KOR.Drafter-" + p)).ToList();
        Assert.Equal(6, CopiesCheck.MaxCopies);
        Assert.Contains(F(seven).Faults, f => f == "7 copies; at most 6 allowed");
        Assert.Empty(F(seven.Take(6).ToList()).Faults);
    }

    [Fact]
    public void NoCopiesPasses() => Assert.Empty(F(Array.Empty<WorktreeCopy>()).Faults);

    // ---- planted faults ---------------------------------------------------------------------------------------------
    [Fact]
    public void ADirtyMergedCopyIsNoFaultAndAnUnreadOneIsNotChecked()
    {
        var cs = new[] { C("KOR.Drafter-plan", "p"), C("KOR.Drafter-revit", "r"), C("KOR.Drafter-etabs", "e") };
        var v = F(cs, _ => MergeRead.Merged("fs01/m"), c => c.Branch switch { "refs/heads/p" => true, "refs/heads/r" => null, _ => false });
        Assert.Single(v.Merged);
        Assert.Contains("KOR.Drafter-etabs", v.Merged[0]);
        Assert.Equal(new[] { H + "KOR.Drafter-plan [p]" }, v.DirtyMerged);
        Assert.Single(v.NotChecked);
        Assert.Contains("KOR.Drafter-revit", v.NotChecked[0]);
        Assert.Contains("left alone", CopiesCheck.Line(v));
    }

    [Fact]
    public void FaultsComeInOrderCountNamesMergedNotChecked()
    {
        var cs = Enumerable.Range(0, 7).Select(i => C(i == 0 ? "KOR.Drafter-unread" : "KOR.Drafter-" + CopiesCheck.Parts.ElementAt(i), "b" + i)).ToList();
        var v = F(cs, c => c.Branch == "refs/heads/b0" ? MergeRead.Unread("deadline reached") : c.Branch == "refs/heads/b1" ? MergeRead.Merged("fs01/m") : MergeRead.No);
        var faults = v.Faults;
        Assert.Equal(4, faults.Count);
        Assert.StartsWith("7 copies", faults[0]);
        Assert.StartsWith("not a product-part name", faults[1]);
        Assert.StartsWith("merged into fs01/m", faults[2]);
        Assert.StartsWith("not checked:", faults[3]);
        var line = CopiesCheck.Line(v);
        Assert.StartsWith("kor copies-check: FAIL - 4 fault(s) (1 of them not checked) of 7 copies: 7 copies", line);
    }

    [Fact]
    public void TheHookLineIsCappedWithAndNMore()
    {
        var cs = Enumerable.Range(0, 60).Select(i => C($"KOR.Drafter-task-number-{i}-with-a-long-name")).ToList();
        var line = CopiesCheck.Line(F(cs), CopiesCheck.HookLineCap);
        Assert.True(line.Length <= CopiesCheck.HookLineCap, $"{line.Length} chars");
        Assert.Matches(@"\.\.\. and \d+ more$", line);
        Assert.Contains("60 copies; at most 6 allowed", line);   // the count fault comes first, so a cap never cuts it
        Assert.DoesNotContain("... and", CopiesCheck.Line(F(cs)));
    }

    [Fact]
    public void TheOkLineNamesTheSharedRefs()
    {
        var line = CopiesCheck.Line(F(new[] { C("KOR.Drafter-plan") }));
        Assert.StartsWith("kor copies-check: ok - 1 copy (at most 6)", line);
        Assert.Contains("fs01/standard-details-rendered-image-store or fs01/port-merge-19nov", line);
    }

    [Fact]
    public void PorcelainPathsWithSpacesAreTakenWhole()
    {
        var rows = CopiesCheck.ParseWorktrees(
            "worktree C:/VIsual Studio Projects/KOR.Drafter\nHEAD 111\nbranch refs/heads/main\n\n" +
            "worktree C:/VIsual Studio Projects/KOR.Drafter-plan\nHEAD 222\ndetached\nlocked\n\n" +
            "worktree C:/a b/c d\nHEAD 333\nbranch refs/heads/x y\nprunable gitdir file points to non-existent location\n");
        Assert.Equal(3, rows.Count);
        Assert.Equal(new WorktreeCopy(H + "KOR.Drafter-plan", null, "222"), rows[1]);
        Assert.Equal(new WorktreeCopy("C:/a b/c d", "refs/heads/x y", "333"), rows[2]);
    }

    // ---- git reads, through a fake runner ---------------------------------------------------------------------------
    private sealed class FakeGit
    {
        public readonly List<string[]> Calls = new();
        public string Worktrees = "worktree " + H + "KOR.Drafter\nHEAD 000\nbranch refs/heads/main\n\n";
        public Dictionary<string, string?> RevParse = new() { ["fs01/a"] = "tipA", ["fs01/b"] = "tipB" };
        public Dictionary<string, string> MergedBranches = new() { ["tipA"] = "", ["tipB"] = "" };
        public Func<string, string, Git.Result> Cherry = (tip, sha) => new Git.Result(0, "+ " + sha + "\n", false);
        public Func<string, string, Git.Result> IsAncestor = (sha, tip) => new Git.Result(1, "", false);
        public Func<string, Git.Result> Status = _ => new Git.Result(0, "", false);

        public void Add(string leaf, string? branch, string sha) =>
            Worktrees += $"worktree {H}{leaf}\nHEAD {sha}\n{(branch is null ? "detached" : "branch refs/heads/" + branch)}\n\n";

        public Git.Result Run(string dir, TimeSpan timeout, string[] args)
        {
            lock (Calls) Calls.Add(args);
            var a = args.Skip(2).ToArray();   // after -c safe.directory=*
            return a[0] switch
            {
                "worktree" => new Git.Result(0, Worktrees, false),
                "rev-parse" => RevParse.TryGetValue(a[3][..^"^{commit}".Length], out var s) && s is not null
                    ? new Git.Result(0, s + "\n", false) : new Git.Result(1, "", false),
                "for-each-ref" => new Git.Result(0, MergedBranches[a[1]["--merged=".Length..]], false),
                "cherry" => Cherry(a[1], a[2]),
                "merge-base" => IsAncestor(a[2], a[3]),
                "status" => Status(dir),
                _ => throw new InvalidOperationException(string.Join(" ", a)),
            };
        }
    }

    private static CopiesVerdict Read(FakeGit g, TimeSpan? deadline = null) =>
        CopiesCheck.Check(H + "KOR.Drafter", deadline ?? TimeSpan.FromSeconds(30), g.Run, new[] { "fs01/a", "fs01/b" });

    [Fact]
    public void EveryGitCallCarriesSafeDirectoryBeforeItsSubcommand()
    {
        var g = new FakeGit();
        g.Add("KOR.Drafter-plan", "p", "p1");
        g.Add("KOR.Drafter-revit", null, "r1");
        g.MergedBranches["tipB"] = "refs/heads/p\n";
        Read(g);
        Assert.True(g.Calls.Count >= 5, $"{g.Calls.Count} calls");
        Assert.All(g.Calls, a => Assert.Equal(new[] { "-c", "safe.directory=*" }, a.Take(2)));
        Assert.Equal(CopiesCheck.SafeDirectory, new[] { "-c", "safe.directory=*" });
    }

    [Fact]
    public void ABranchInAForEachRefMergedSetIsMergedByItsBranchLineAndCleanMeansAFault()
    {
        var g = new FakeGit();
        g.Add("KOR.Drafter-plan", "p", "p1");
        g.MergedBranches["tipB"] = "refs/heads/p\n";
        var v = Read(g);
        Assert.Equal(new[] { $"merged into fs01/b; remove it once its session has ended: {H}KOR.Drafter-plan [p]" }, v.Merged);
        Assert.DoesNotContain(g.Calls, a => a[2] == "cherry");   // no cherry for a copy already in a merged set
    }

    [Fact]
    public void ACleanMergedCopyIsAFaultAndADirtyOneIsNot()
    {
        var g = new FakeGit();
        g.Add("KOR.Drafter-plan", "p", "p1");
        g.Add("KOR.Drafter-revit", "r", "r1");
        g.Cherry = (tip, sha) => new Git.Result(0, "- " + sha + "\n", false);   // every commit patch-equivalent: merged
        g.Status = dir => new Git.Result(0, dir.EndsWith("revit", StringComparison.Ordinal) ? " M a.cs\n" : "", false);
        var v = Read(g);
        Assert.Single(v.Merged);
        Assert.Contains("KOR.Drafter-plan", v.Merged[0]);
        Assert.Equal(new[] { $"{H}KOR.Drafter-revit [r]" }, v.DirtyMerged);
    }

    [Fact]
    public void AStatusThatCannotBeReadFailsClosed()
    {
        var g = new FakeGit();
        g.Add("KOR.Drafter-plan", "p", "p1");
        g.MergedBranches["tipA"] = "refs/heads/p\n";
        g.Status = _ => new Git.Result(null, null, true);
        var v = Read(g);
        Assert.Empty(v.Merged);
        Assert.Single(v.NotChecked);
    }

    [Fact]
    public void AnUnresolvedSharedRefIsSkippedAndNamed()
    {
        var g = new FakeGit();
        g.Add("KOR.Drafter-plan", "p", "p1");
        g.RevParse["fs01/b"] = null;
        var v = Read(g);
        Assert.Equal(new[] { "fs01/b does not resolve" }, v.RefsSkipped);
        Assert.Contains("[shared ref(s) skipped: fs01/b does not resolve]", CopiesCheck.Line(v));
        Assert.Contains("none merged into fs01/a,", CopiesCheck.Line(v).Replace(" [", ",["));
    }

    [Fact]
    public void NoSharedRefResolvedMeansNotChecked()
    {
        var g = new FakeGit();
        g.Add("KOR.Drafter-plan", "p", "p1");
        g.RevParse.Clear();
        var v = Read(g);
        Assert.Single(v.NotChecked);
        Assert.StartsWith("kor copies-check: FAIL", CopiesCheck.Line(v));
    }

    [Fact]
    public void ADeadlineReachedGivesNotCheckedButKeepsNameAndCountFaults()
    {
        var g = new FakeGit();
        for (var i = 0; i < 7; i++) g.Add(i == 0 ? "KOR.Drafter-dimtext" : "KOR.Drafter-" + CopiesCheck.Parts.ElementAt(i), "b" + i, "s" + i);
        var calls = 0;
        CopiesCheck.Runner slow = (d, t, a) =>
        {
            if (Interlocked.Increment(ref calls) > 1) Thread.Sleep(120);   // the worktree list is quick; every read after it is slow
            return g.Run(d, t, a);
        };
        var v = CopiesCheck.Check(H + "KOR.Drafter", TimeSpan.FromMilliseconds(400), slow, new[] { "fs01/a", "fs01/b" });
        Assert.Single(v.Count);
        Assert.Single(v.Names);
        Assert.Empty(v.Merged);
        Assert.Contains(v.NotChecked, n => n.Contains("deadline reached") || n.Contains("ran out of time"));
        Assert.True(v.NotChecked.Count >= 5, $"{v.NotChecked.Count} not checked of 7");
        Assert.DoesNotContain("ok", CopiesCheck.Line(v)[..22]);
    }

    [Fact]
    public void AnExpiredCherryIsNotCheckedNeverNotMerged()
    {
        var g = new FakeGit();
        g.Add("KOR.Drafter-plan", "p", "p1");
        g.Cherry = (tip, sha) => new Git.Result(null, null, true);
        var v = Read(g);
        Assert.Equal(new[] { $"not checked: {H}KOR.Drafter-plan [p] (git cherry ran out of time)" }, v.NotChecked);
    }

    [Fact]
    public void ADetachedHeadUsesMergeBaseAndExitOneIsNo()
    {
        var g = new FakeGit();
        g.Add("KOR.Drafter-plan", null, "d1");
        g.Add("KOR.Drafter-revit", null, "d2");
        g.IsAncestor = (sha, tip) => new Git.Result(sha == "d2" && tip == "tipA" ? 0 : 1, "", false);
        var v = Read(g);
        Assert.Equal(new[] { $"merged into fs01/a; remove it once its session has ended: {H}KOR.Drafter-revit [detached]" }, v.Merged);
        Assert.Empty(v.NotChecked);   // d1: exit 1 then a '+' cherry - not merged, and read
        Assert.Contains(g.Calls, a => a[2] == "cherry" && a[4] == "d1");
    }

    [Fact]
    public void AMergeBaseThatFailsIsNotChecked()
    {
        var g = new FakeGit();
        g.Add("KOR.Drafter-plan", null, "d1");
        g.IsAncestor = (_, _) => new Git.Result(128, "", false);
        g.Cherry = (_, _) => new Git.Result(128, null, false);
        Assert.Single(Read(g).NotChecked);
    }

    // ---- the verb and the runner ------------------------------------------------------------------------------------
    [Fact]
    public void AnExceptionInTheCopiesRuleIsOneErrorLine()
    {
        var lines = Program.CopiesLines("C:/nowhere", hook: true, _ => throw new InvalidOperationException("boom\nsecond"));
        Assert.Single(lines);
        Assert.StartsWith("kor copies-check: ERROR - boom", lines[0]);
        Assert.DoesNotContain("\n", lines[0]);
    }

    [Fact]
    public void WithoutHookEachFaultFollowsOnItsOwnLine()
    {
        var v = F(new[] { C("KOR.Drafter-dimtext"), C("elsewhere/KOR.Drafter-plan") });
        var lines = Program.CopiesLines("C:/x", hook: false, _ => v);
        Assert.Equal(3, lines.Count);
        Assert.StartsWith("kor copies-check: FAIL - 2 fault(s)", lines[0]);
        Assert.All(lines.Skip(1), l => Assert.StartsWith("  KOR.Drafter copies: ", l));
    }

    [Fact]
    public void TheHookPrintsTheCopiesLineSecondAndExitsZeroWhateverTheEnrolmentDid()
    {
        // a scratch repo named KOR.Drafter: the enrolment fails (no settings, no hooks) or, with the DB out of reach, cannot
        // check - either way its line comes first, the copies line second (0 copies; the shared refs do not resolve here)
        var parent = Path.Combine(Path.GetTempPath(), "kor-copies-verb-" + Guid.NewGuid().ToString("N")[..8]);
        var dir = Path.Combine(parent, "KOR.Drafter");
        Directory.CreateDirectory(dir);
        try
        {
            Assert.NotNull(Git.Run(dir, "init", "-q"));
            var output = new StringWriter();
            Assert.Equal(0, Program.Run(new[] { "enroll-check", "--hook", "--dir", dir }, () => null, output, new StringWriter(), Path.Combine(parent, "hook.log")));
            var lines = output.ToString().ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
            Assert.Equal(2, lines.Length);
            Assert.StartsWith("kor enroll-check: ", lines[0]);
            Assert.StartsWith("kor copies-check: ok - 0 copies (at most 6)", lines[1]);
            Assert.Contains("fs01/port-merge-19nov does not resolve", lines[1]);
        }
        finally { try { Directory.Delete(parent, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    [Fact]
    public void GitExecGivesTheExitCodeAndRunStillGivesNullOnNonZero()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kor-copies-git-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            Assert.NotNull(Git.Run(dir, "init", "-q"));
            var r = Git.Exec(dir, TimeSpan.FromSeconds(10), "-c", "safe.directory=*", "rev-parse", "--verify", "-q", "no-such-ref^{commit}");
            Assert.Equal(1, r.ExitCode);
            Assert.False(r.Expired);
            Assert.Null(Git.Run(dir, TimeSpan.FromSeconds(10), "rev-parse", "--verify", "-q", "no-such-ref"));
            var expired = Git.Exec(dir, TimeSpan.Zero, "status");
            Assert.True(expired.Expired);
            Assert.False(expired.Ok);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
