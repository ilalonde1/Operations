using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Cli;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// K3 - captured automatically. The classifier tests are pure (real-shaped stdout). The LIVE tests write to the REAL
/// KorStandards brain.Event / brain.LaneState as korbrain_app under a module named zz-test-k3-&lt;time&gt; and read the
/// row back; DELETE is denied to brain_rw by design (162), so the rows stay, marked by that module name.
///
/// Covers: each verb's row lands with the right kind/commit/ref/outcome; hook mode exits 0 silently on bad input.
/// Does NOT cover: the hook wiring itself (settings.json, .git/hooks shims) - proved by the real commit in Framework;
/// a session that is killed before its hook runs.
/// </summary>
public class CaptureTests
{
    private static readonly string Module = $"zz-test-k3-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}";

    // ---- real-shaped output samples (dotnet 8 / VSTest, the shape this solution's runs print) -------------------------
    private const string TestPassOut = """
          Determining projects to restore...
          All projects are up-to-date for restore.
          Kor.Operations.Framework -> C:\VIsual Studio Projects\Operations\Kor.Operations.Framework\bin\Debug\net8.0-windows\Kor.Operations.Framework.dll
        Test run for C:\VIsual Studio Projects\Operations\Kor.Operations.Framework.Tests\bin\Debug\net8.0-windows\Kor.Operations.Framework.Tests.dll (.NETCoreApp,Version=v8.0)
        VSTest version 17.14.1 (x64)

        Starting test execution, please wait...
        A total of 1 test files matched the specified pattern.

        Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7, Duration: 2 s - Kor.Operations.Framework.Tests.dll (net8.0)
        """;

    private const string TestFailOut = """
        Starting test execution, please wait...
        A total of 1 test files matched the specified pattern.
          Failed Kor.Operations.Framework.Tests.SkeletonTests.VersionPrintsKor [4 ms]
          Error Message:
           Assert.StartsWith() Failure: String start does not match
        Failed!  - Failed:     1, Passed:     6, Skipped:     0, Total:     7, Duration: 2 s - Kor.Operations.Framework.Tests.dll (net8.0)
        """;

    private const string BuildFailOut = """
        C:\VIsual Studio Projects\Operations\X\A.cs(12,5): error CS0104: 'Path' is an ambiguous reference between 'System.IO.Path' and 'X.Path' [C:\VIsual Studio Projects\Operations\X\X.csproj]

        Build FAILED.

            0 Warning(s)
            1 Error(s)
        """;

    private const string BuildPassOut = """
          Kor.Operations.Framework -> C:\x\Kor.Operations.Framework.dll

        Build succeeded.
            0 Warning(s)
            0 Error(s)
        """;

    // ---- the classifier ---------------------------------------------------------------------------------------------
    [Fact]
    public void PassingTestRunIsTestPass()
    {
        var t = ToolClassifier.Classify("set -o pipefail && dotnet test Kor.Operations.Framework.Tests --filter \"Speed!=Slow\" 2>&1 | tail -30", TestPassOut);
        Assert.NotNull(t);
        Assert.Equal("test", t!.Kind);
        Assert.Equal("pass", t.Outcome);
        Assert.Equal("dotnet test Kor.Operations.Framework.Tests --filter \"Speed!=Slow\"", t.Ref);
        Assert.Contains("Passed!", t.Detail);
    }

    [Fact]
    public void FailingTestRunIsTestFailEvenWithAZeroExitAndNamesTheFailure()
    {
        // known-bad: a pipe can make the exit 0 (Operations rule 13) - the output decides, not the exit code.
        var t = ToolClassifier.Classify("cd \"C:/VIsual Studio Projects/Operations\" && dotnet test", TestFailOut, exitCode: 0);
        Assert.Equal("fail", t!.Outcome);
        Assert.Contains("SkeletonTests.VersionPrintsKor", t.Detail);
    }

    [Fact]
    public void BuildOutcomesComeFromTheOutput()
    {
        Assert.Equal("fail", ToolClassifier.Classify("dotnet build X.csproj", BuildFailOut)!.Outcome);
        Assert.Equal("pass", ToolClassifier.Classify("dotnet build X.csproj", BuildPassOut)!.Outcome);
        Assert.Equal("build", ToolClassifier.Classify("dotnet build X.csproj", BuildPassOut)!.Kind);
        // Ref keeps the arguments and drops the redirections.
        Assert.Equal("dotnet build X.csproj --nologo -v q", ToolClassifier.Classify("dotnet build X.csproj --nologo -v q 2>&1 > out.txt", BuildPassOut)!.Ref);
    }

    [Fact]
    public void ACompileErrorInATestRunIsAFail()
    {
        Assert.Equal("fail", ToolClassifier.Classify("dotnet test", BuildFailOut)!.Outcome);
    }

    [Fact]
    public void NoMarkerAndANonZeroExitIsFailOtherwiseUnknown()
    {
        Assert.Equal("fail", ToolClassifier.Classify("dotnet test", "Exit code 1\nsomething went wrong")!.Outcome);
        Assert.Equal("unknown", ToolClassifier.Classify("dotnet test", "no summary here")!.Outcome);
    }

    [Fact]
    public void MigrationsAreRecognisedWithTheirFileAndOutcome()
    {
        var s = ToolClassifier.Classify("sqlcmd -S KOR-APP01\\SQLEXPRESS -E -i \"C:\\VIsual Studio Projects\\KOR.Drafter\\db\\168_TheRecordKnowsItsSource.sql\"",
                                        "plan  mode\nresult\n------\nTESTED\nRolled back - nothing kept.");
        Assert.Equal("migration", s!.Kind);
        Assert.Equal("168_TheRecordKnowsItsSource.sql", s.Ref);
        Assert.Equal("tested", s.Outcome);

        var k = ToolClassifier.Classify("pwsh -File \"C:/VIsual Studio Projects/KOR.Drafter/db/tools/Invoke-KorSqlScript.ps1\" -Path \"C:/VIsual Studio Projects/KOR.Drafter/db/168_TheRecordKnowsItsSource.sql\" -Database KorStandards -Confirm \"THE RECORD KNOWS ITS SOURCE\"",
                                        "--- batch 2\nresult rows_before\n------ -----------\nDONE   1100\napplied 3 batch(es) from x");
        Assert.Equal("migration", k!.Kind);
        Assert.Equal("168_TheRecordKnowsItsSource.sql", k.Ref);
        Assert.Equal("applied", k.Outcome);

        Assert.Equal("fail", ToolClassifier.Classify("& .\\db\\tools\\Invoke-KorSqlScript.ps1 -Path .\\db\\169_X.sql -Database KorStandards",
                                                     "Exception calling \"ExecuteReader\": 168: a source rule did not hold")!.Outcome);
    }

    [Fact]
    public void StageGateRunsAreGates()
    {
        var g = ToolClassifier.Classify("python -I \"C:/VIsual Studio Projects/Framework/tools/stage_gate.py\" run stage_gates.json",
                                        "stage_gate: framework  repo Framework  HEAD 3caceb0\n  [1/2] compile (seconds)  PASS  1s\nstage_gate: PASS - 2 of 2 stages passed (3s)");
        Assert.Equal("gate", g!.Kind);
        Assert.Equal("stage_gates.json", g.Ref);
        Assert.Equal("pass", g.Outcome);
        Assert.Equal("stopped", ToolClassifier.Classify("python -I tools/stage_gate.py run p.json", "stage_gate: STOPPED at stage 1 of 2")!.Outcome);
    }

    [Theory]
    [InlineData("grep -rn \"dotnet test\" docs")]
    [InlineData("git log --oneline -5")]
    [InlineData("echo dotnet build")]
    [InlineData("cat db/168_TheRecordKnowsItsSource.sql")]
    [InlineData("python -I tools/stage_gate.py check p.json")]
    [InlineData("")]
    public void ANonMatchingCommandRecordsNothing(string command)
    {
        Assert.Null(ToolClassifier.Classify(command, TestPassOut));
    }

    [Theory]   // review row 85: a separator inside quotes or a heredoc body is data, not a command boundary
    [InlineData("git commit -m \"fix; dotnet test now passes\"")]
    [InlineData("echo \"a && dotnet build\"")]
    [InlineData("echo 'x | dotnet test'")]
    [InlineData("git commit -m \"first line\ndotnet test was green\"")]
    [InlineData("git commit -q -F - <<'EOF'\nK3 commit message\ndotnet test 35 of 35\nsqlcmd -i C:/x/db/169_A.sql\nEOF\ngit log -1")]
    [InlineData("git commit -F - <<EOF\ndotnet build passes\nEOF")]
    [InlineData("git commit -m @'\ndotnet test passes\n'@")]
    public void TextInsideQuotesOrHeredocsIsNotACommand(string command)
    {
        Assert.Null(ToolClassifier.Classify(command, TestPassOut));
    }

    [Fact]
    public void ACommandAfterAHeredocEndsIsStillSeen()
    {
        var t = ToolClassifier.Classify("cat > a.txt <<'EOF'\nhello; world\nEOF\ndotnet test X.Tests --filter \"A|B\"", TestPassOut);
        Assert.Equal("test", t!.Kind);
        Assert.Equal("dotnet test X.Tests --filter \"A|B\"", t.Ref);   // the quoted | is kept as written
    }

    [Fact]
    public void NeutraliseKeepsTheLength()
    {
        const string c = "git commit -m \"a; b\" && dotnet test <<'EOF'\nx|y\nEOF";
        Assert.Equal(c.Length, ToolClassifier.Neutralise(c).Length);
    }

    [Theory]   // review row 87: a blanked character inside a quoted name must not change the name recorded
    [InlineData("python -I tools/stage_gate.py run \"gate(1).json\"", "gate", "gate(1).json")]
    [InlineData("python -I \"C:/VIsual Studio Projects/Framework/tools/stage_gate.py\" run \"C:/VIsual Studio Projects/Framework/stage_gates.json\"", "gate", "C:/VIsual Studio Projects/Framework/stage_gates.json")]
    [InlineData("pwsh -File \"C:/x/Invoke-KorSqlScript.ps1\" -Path \"C:/x/db/130_a;131_b.sql\" -Database KorStandards", "migration", "130_a;131_b.sql")]
    [InlineData("sqlcmd -S s -i \"C:/x/db/130_a(b).sql\"", "migration", "130_a(b).sql")]
    [InlineData("python -I tools/stage_gate.py run p.json; echo done", "gate", "p.json")]
    public void FileNamesAreReadWholeFromTheOriginal(string command, string kind, string expected)
    {
        var t = ToolClassifier.Classify(command, "");
        Assert.Equal(kind, t!.Kind);
        Assert.Equal(expected, t.Ref);
    }

    [Fact]
    public void ANegativeExitFailsAMigrationWithNoMarker()
    {
        Assert.Equal("fail", ToolClassifier.Classify("sqlcmd -i C:/x/db/169_A.sql", "", exitCode: -1)!.Outcome);
    }

    [Fact]
    public void AMergeCommitRecordsItsFilesAgainstTheFirstParent()
    {
        // review row 85: plain diff-tree prints nothing for a merge. A real merge from this repo's history is used.
        var dir = RepoDir();
        var merge = Git.Run(dir, "rev-list", "--merges", "-1", "HEAD")?.Trim();
        Assert.False(string.IsNullOrEmpty(merge), "this repo has no merge commit to test against");
        var e = GitCapture.CommitAt(dir, merge!, Module);
        Assert.Equal(merge, e.Commit);
        using var d = JsonDocument.Parse(e.Detail!);
        Assert.Equal(2, d.RootElement.GetProperty("parents").GetArrayLength());
        var expected = Git.Run(dir, "diff", "--name-only", $"{merge}^1", merge!)!.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(expected > 0, "the chosen merge changed nothing against its first parent");
        Assert.Equal(expected, d.RootElement.GetProperty("files_total").GetInt32());
    }

    [Fact]
    public void SecretLinesNeverReachTheDetail()
    {
        var t = ToolClassifier.Classify("dotnet test", "Server=x;User Id=u;Password=hunter2;\n" + TestPassOut);
        Assert.False(Redact.HasSecretMarker(t!.Detail));
        Assert.DoesNotContain("hunter2", t.Detail);
    }

    [Fact]
    public void DiffTreeParsesAddModifyDeleteAndRename()
    {
        const string a = "1111111111111111111111111111111111111111", b = "2222222222222222222222222222222222222222", z = "0000000000000000000000000000000000000000";
        var raw = $":000000 100644 {z} {a} A\0docs/new.md\0:100644 100644 {a} {b} M\0src/x.cs\0:100644 000000 {b} {z} D\0old.txt\0:100644 100644 {a} {a} R100\0from.cs\0to.cs\0";
        var f = GitCapture.ParseDiffTree(raw);
        Assert.Equal(4, f.Count);
        Assert.Equal(("docs/new.md", a), (f[0].Path, f[0].Blob));
        Assert.Equal(("src/x.cs", b), (f[1].Path, f[1].Blob));
        Assert.Equal(("old.txt", (string?)null), (f[2].Path, f[2].Blob));
        Assert.Equal(("to.cs", "from.cs", "R100"), (f[3].Path, f[3].From, f[3].Status));
    }

    [Fact]
    public void ShaAcceptsOnlyLowercase40Hex()
    {
        Assert.NotNull(BrainStore.Sha(new string('a', 40)));
        Assert.Null(BrainStore.Sha(new string('A', 40)));
        Assert.Null(BrainStore.Sha("abc"));
    }

    // ---- hook mode never fails -------------------------------------------------------------------------------------
    [Theory]
    [InlineData("capture-tool")]
    [InlineData("capture-compact")]
    [InlineData("capture-end")]
    public void HookModeExitsZeroSilentlyOnBadStdin(string verb)
    {
        // Each failure goes to the log the caller names (never the real one - review row 85), tagged with the module.
        var log = TempLog();
        foreach (var input in new[] { "{not json", "[1,2]", "", null })
        {
            var output = new StringWriter();
            var error = new StringWriter();
            Assert.Equal(0, Program.Run(new[] { verb, "--hook", "--module", Module }, () => input, output, error, log));
            Assert.Equal("", output.ToString());
            Assert.Equal("", error.ToString());
        }
        var lines = File.ReadAllLines(log);
        Assert.Equal(4, lines.Length);
        Assert.All(lines, l => Assert.Contains($"{verb} module={Module}", l));
        File.Delete(log);
    }

    [Fact]
    public void HookModeCaptureGitOutsideARepoExitsZeroSilently()
    {
        var log = TempLog();
        var output = new StringWriter();
        var error = new StringWriter();
        var notRepo = Path.Combine(Path.GetTempPath(), "kor-no-repo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(notRepo);
        Assert.Equal(0, Program.Run(new[] { "capture-git", "post-commit", "--dir", notRepo, "--hook" }, () => null, output, error, log));
        Assert.Equal(0, Program.Run(new[] { "capture-git", "no-such-hook", "--hook" }, () => null, output, error, log));
        Assert.Equal("", output.ToString() + error.ToString());
        Assert.Equal(2, File.ReadAllLines(log).Length);
        Directory.Delete(notRepo);
        File.Delete(log);
    }

    private static string TempLog() => Path.Combine(Path.GetTempPath(), $"kor-k3-test-{Guid.NewGuid():N}.log");

    // ---- LIVE: each verb writes its row, read back as korbrain_app ---------------------------------------------------
    private static string RepoDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, ".git"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("the test is not running inside the Operations checkout");
    }

    private static int RunOk(string[] args, string? stdin = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = Program.Run(args, () => stdin, output, error, TempLog());
        Assert.True(code == 0, $"exit {code}: {error}");
        var text = output.ToString();
        var m = System.Text.RegularExpressions.Regex.Match(text, @"brain\.Event (\d+)");
        Assert.True(m.Success, $"no event id in: {text}");
        return int.Parse(m.Groups[1].Value);
    }

    private static StoredEvent ReadBack(int id, string kind)
    {
        var row = BrainStore.Last(Module, 200, kind).SingleOrDefault(r => r.Id == id);
        Assert.NotNull(row);
        return row!;
    }

    [Fact]
    public void LiveCaptureGitPostCommitWritesTheHeadCommitWithBlobs()
    {
        var dir = RepoDir();
        var head = Git.Run(dir, "rev-parse", "HEAD")!.Trim();
        var id = RunOk(new[] { "capture-git", "post-commit", "--dir", dir, "--module", Module });
        var row = ReadBack(id, "commit");
        Assert.Equal(head, row.Commit);
        Assert.Equal(Git.Leaf(Git.TopLevel(dir)), row.Repo);
        using var d = JsonDocument.Parse(row.Detail!);
        Assert.True(d.RootElement.GetProperty("files_total").GetInt32() > 0);
        var first = d.RootElement.GetProperty("files")[0];
        var path = first.GetProperty("path").GetString()!;
        // The blob recorded is the blob at that commit (K5 compares exactly this against HEAD:<path>).
        if (first.GetProperty("blob").GetString() is { } blob)
            Assert.Equal(Git.Run(dir, "rev-parse", $"{head}:{path}")!.Trim(), blob);
        Assert.False(string.IsNullOrEmpty(d.RootElement.GetProperty("message").GetString()));
    }

    [Fact]
    public void LiveCaptureGitPrePushRecordsEachRefAndTheGuardResult()
    {
        var dir = RepoDir();
        var head = Git.Run(dir, "rev-parse", "HEAD")!.Trim();
        var stdin = $"refs/heads/develop {head} refs/heads/develop {new string('0', 40)}\n";
        var id = RunOk(new[] { "capture-git", "pre-push", "origin", "https://github.com/kor/Operations.git", "--result", "1", "--dir", dir, "--module", Module }, stdin);
        var row = ReadBack(id, "push");
        Assert.Equal("refused", row.Outcome);
        Assert.Equal(head, row.Commit);
        Assert.Contains("origin Operations.git:", row.Ref);
        Assert.Contains("-> refs/heads/develop", row.Ref);
    }

    [Fact]
    public void LiveCaptureGitCheckoutAndMergeWriteTheirKinds()
    {
        var dir = RepoDir();
        var head = Git.Run(dir, "rev-parse", "HEAD")!.Trim();
        var c = ReadBack(RunOk(new[] { "capture-git", "post-checkout", head, head, "1", "--dir", dir, "--module", Module }), "checkout");
        Assert.Equal(head, c.Commit);
        Assert.StartsWith("branch ", c.Ref);
        var m = ReadBack(RunOk(new[] { "capture-git", "post-merge", "0", "--dir", dir, "--module", Module }), "merge");
        Assert.Equal(head, m.Commit);
    }

    [Fact]
    public void LiveCaptureToolRecordsAFailingTestFromPostToolUseFailure()
    {
        var dir = RepoDir();
        var json = JsonSerializer.Serialize(new
        {
            session_id = "k3-test-session",
            transcript_path = "C:/x/k3.jsonl",
            cwd = dir,
            hook_event_name = "PostToolUseFailure",
            tool_name = "Bash",
            tool_input = new { command = "dotnet test Kor.Operations.Framework.Tests", description = "Run tests" },
            error = "Exit code 1\n" + TestFailOut,
            is_interrupt = false,
        });
        var row = ReadBack(RunOk(new[] { "capture-tool", "--module", Module }, json), "test");
        Assert.Equal("fail", row.Outcome);
        Assert.Equal("dotnet test Kor.Operations.Framework.Tests", row.Ref);
        Assert.Equal(Git.Run(dir, "rev-parse", "HEAD")!.Trim(), row.Commit);
        Assert.Contains("k3-test-session", row.Actor);
    }

    [Fact]
    public void LiveCaptureToolRecordsAPassingBuildFromPostToolUse()
    {
        var json = JsonSerializer.Serialize(new
        {
            session_id = "k3-test-session", cwd = RepoDir(), hook_event_name = "PostToolUse", tool_name = "Bash",
            tool_input = new { command = "dotnet build Kor.Operations.Framework.Cli" },
            tool_response = new { stdout = BuildPassOut + "\nServer=x;Password=hunter2;", stderr = "", interrupted = false },
        });
        var row = ReadBack(RunOk(new[] { "capture-tool", "--module", Module }, json), "build");
        Assert.Equal("pass", row.Outcome);
        Assert.DoesNotContain("hunter2", row.Detail);
    }

    [Fact]
    public void LiveCaptureToolOnANonMatchingCommandWritesNoRow()
    {
        var json = JsonSerializer.Serialize(new
        {
            session_id = "k3-test-session", cwd = RepoDir(), hook_event_name = "PostToolUse", tool_name = "Bash",
            tool_input = new { command = "git status --short" }, tool_response = new { stdout = "", stderr = "" },
        });
        var output = new StringWriter();
        Assert.Equal(0, Program.Run(new[] { "capture-tool", "--module", Module }, () => json, output, new StringWriter(), TempLog()));
        Assert.Equal("nothing to record", output.ToString().Trim());
        // No row in the test module may carry this command (a count would not do: the module is shared by the class).
        Assert.DoesNotContain(BrainStore.Last(Module, 1000), r => r.Detail?.Contains("git status --short") == true || r.Ref?.Contains("git status") == true);
    }

    [Fact]
    public void LiveCaptureCompactStoresTheSummaryAsTheLaneResume()
    {
        var summary = $"K3 test compaction summary {Guid.NewGuid():N}\nPassword=should-not-land\nwhere we stopped: K3 tests";
        var json = JsonSerializer.Serialize(new
        {
            session_id = "k3-compact-session", transcript_path = "C:/x/k3c.jsonl", cwd = RepoDir(),
            hook_event_name = "PostCompact", trigger = "manual", compact_summary = summary,
        });
        var row = ReadBack(RunOk(new[] { "capture-compact", "--module", Module }, json), "compact");
        Assert.Equal("stored", row.Outcome);
        using var d = JsonDocument.Parse(row.Detail!);
        var laneId = d.RootElement.GetProperty("lane_state_id").GetInt32();
        var lane = BrainStore.LaneStateById(laneId);
        Assert.NotNull(lane);
        Assert.Equal(Module, lane!.Lane);
        Assert.Equal("compaction k3-compact-session", lane.WrittenBy);
        Assert.Contains("where we stopped: K3 tests", lane.ResumeBlock);
        Assert.DoesNotContain("should-not-land", lane.ResumeBlock);
    }

    [Fact]
    public void LiveCaptureEndRecordsTheTranscriptAndReason()
    {
        var json = JsonSerializer.Serialize(new
        {
            session_id = "k3-end-session", transcript_path = "C:/x/k3e.jsonl", cwd = RepoDir(),
            hook_event_name = "SessionEnd", reason = "prompt_input_exit",
        });
        var sw = Stopwatch.StartNew();
        var row = ReadBack(RunOk(new[] { "capture-end", "--module", Module }, json), "session-end");
        Assert.Equal("C:/x/k3e.jsonl", row.Ref);
        Assert.Equal("prompt_input_exit", row.Outcome);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"capture-end took {sw.Elapsed}");
    }
}
