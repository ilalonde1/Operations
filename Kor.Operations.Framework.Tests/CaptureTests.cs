using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    // review row 88: a name ends where its pattern ends - never widened past ) > , - and an earlier NNN_ token is skipped
    [InlineData("x=$(sqlcmd -S s -i db/130_a.sql)", "migration", "130_a.sql")]
    [InlineData("sqlcmd -i db/130_a.sql>log.txt", "migration", "130_a.sql")]
    [InlineData("sqlcmd -i db/130_a.sql,db/131_b.sql", "migration", "130_a.sql")]
    [InlineData("Invoke-KorSqlScript -Retry 100_x -Path 130_a.sql", "migration", "130_a.sql")]
    [InlineData("x=$(python -I tools/stage_gate.py run p.json)", "gate", "p.json")]
    [InlineData("python -I tools/stage_gate.py run p.json>out.txt", "gate", "p.json")]
    public void FileNamesAreReadWholeFromTheOriginal(string command, string kind, string expected)
    {
        var t = ToolClassifier.Classify(command, "");
        Assert.Equal(kind, t!.Kind);
        Assert.Equal(expected, t.Ref);
    }


    // ---- THE SOURCE RULE: a result is cited only to a PROVED path; anything not proved is UNKNOWN --------------------
    // One table for the whole class (review rows 93-96 and #74/#125 were five-plus instances of it, one at a time).
    // A fake file system, so every row is exact; cwd is C:/a unless the row says otherwise.
    private static readonly string[] FakeDirs =
    {
        "C:/a", "C:/a/sub", "C:/a/Framework", "C:/a/Operations", "C:/x", "C:/x/sub", "C:/y", "C:/first", "C:/first/sub",
        "C:/second", "C:/Program Files (x86)/x", "D:/", "C:/VIsual Studio Projects/Operations", "C:/proj",
    };
    private static readonly string[] FakeFiles =
        { "C:/x/App.csproj", "C:/y/B.csproj", "C:/x/db/169_A.sql", "C:/a/db/169_A.sql", "C:/a/p.json", "C:/x/p.json" };

    private static SourceResolver.Probe Fake(string? cdPath = null)
    {
        var dirs = FakeDirs.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = FakeFiles.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new SourceResolver.Probe(d => dirs.Contains(Path.GetFullPath(d)), f => files.Contains(Path.GetFullPath(f)),
                                        d => Path.GetFullPath(d) == Path.GetFullPath("C:/proj"), cdPath);
    }

    public static IEnumerable<object?[]> SourceTable() => new List<object?[]>
    {
        // shell, command, CDPATH, expected proved path (null = UNKNOWN)
        // -- proved: the folder
        new object?[] { "bash", "dotnet test", null, "C:/a" },
        new object?[] { "bash", "cd C:/x && dotnet test", null, "C:/x" },
        new object?[] { "bash", "cd \"/c/VIsual Studio Projects/Operations\" && dotnet build", null, "C:/VIsual Studio Projects/Operations" },  // #74
        new object?[] { "bash", "cd sub && dotnet test", null, "C:/a/sub" },
        new object?[] { "bash", "cd C:/first && cd sub && dotnet build", null, "C:/first/sub" },      // relative to the CURRENT folder
        new object?[] { "bash", "cd C:/first && cd C:/second && dotnet build", null, "C:/second" },
        new object?[] { "powershell", "Set-Location -Path 'C:/y'; dotnet test", null, "C:/y" },
        new object?[] { "bash", "cd -P C:/x && dotnet test", null, "C:/x" },                          // row 94: flags skipped
        new object?[] { "bash", "cd -L -- sub && dotnet test", null, "C:/a/sub" },
        new object?[] { "bash", "cd \"C:/Program Files (x86)/x\" && dotnet test", null, "C:/Program Files (x86)/x" },
        new object?[] { "bash", "cd C:/x 2>/dev/null && dotnet test", null, "C:/x" },
        new object?[] { "bash", "(cd C:/x && dotnet test)", null, "C:/x" },
        new object?[] { "bash", "cd /d && dotnet test", null, "D:/" },                                 // Git Bash's D:/
        new object?[] { "bash", "pushd C:/x && make && popd && dotnet test", null, "C:/a" },           // row 93
        new object?[] { "bash", "pushd C:/x; pushd C:/y; popd; dotnet test", null, "C:/x" },
        new object?[] { "powershell", "Push-Location C:/x; dotnet test; Pop-Location", null, "C:/x" },
        new object?[] { "bash", "(cd C:/x; make) && dotnet test", null, "C:/a" },                      // a subshell ends
        new object?[] { "bash", "x=$(cd C:/x && pwd); dotnet test", null, "C:/a" },
        new object?[] { "powershell", "(Set-Location C:/x); dotnet test", null, "C:/x" },              // no subshell in PowerShell
        new object?[] { "bash", "dotnet test && cd C:/x", null, "C:/a" },                              // after the tool: no effect
        new object?[] { "bash", "echo 'cd C:/x' && dotnet test", null, "C:/a" },                       // text, not a command
        new object?[] { "bash", "cd ./sub && dotnet test", "C:/elsewhere", "C:/a/sub" },              // ./ never uses CDPATH
        new object?[] { "bash", "make && cd C:/x && dotnet test", null, "C:/x" },                     // && all the way: the tool ran only if cd did
        new object?[] { "bash", "cd C:/x 2>&1 && dotnet test", null, "C:/x" },                        // row 100: 2>&1 is a redirection
        new object?[] { "bash", "cd C:/x >&2 && dotnet test", null, "C:/x" },
        new object?[] { "bash", "echo 'if; cd C:/y' && cd C:/x && dotnet test", null, "C:/x" },       // quoted words are data
        // -- proved: what the tool ran on
        new object?[] { "bash", "dotnet test C:/x/App.csproj", null, "C:/x/App.csproj" },
        new object?[] { "bash", "cd C:/x && dotnet build App.csproj -c Release", null, "C:/x/App.csproj" },
        new object?[] { "bash", "dotnet test C:/proj --filter \"A|B\"", null, "C:/proj" },
        new object?[] { "bash", "cd ~ && dotnet test C:/x/App.csproj", null, "C:/x/App.csproj" },     // absolute: no folder needed
        new object?[] { "bash", "sqlcmd -S s -i C:/x/db/169_A.sql", null, "C:/x/db/169_A.sql" },
        new object?[] { "bash", "sqlcmd -S s -i db/169_A.sql", null, "C:/a/db/169_A.sql" },
        new object?[] { "powershell", "& \"C:/t/Invoke-KorSqlScript.ps1\" -Path \"C:/x/db/169_A.sql\" -Database K", null, "C:/x/db/169_A.sql" },
        new object?[] { "bash", "python -I tools/stage_gate.py run p.json", null, "C:/a/p.json" },
        new object?[] { "bash", "cd C:/x && python -I tools/stage_gate.py run p.json", null, "C:/x/p.json" },
        // -- UNKNOWN: a folder change that is not proved
        // Each UNKNOWN row also names its REASON, so a row cannot pass for the wrong reason (a disabled || check was
        // masked by the | check until the reason was asserted).
        new object?[] { "bash", "cd - && dotnet test", null, null, "'-' is not a folder" },
        new object?[] { "bash", "cd ~ && dotnet test", null, null, "is not a literal folder" },
        new object?[] { "bash", "cd $DIR && dotnet test", null, null, "is not a literal folder" },
        new object?[] { "powershell", "cd %TEMP%; dotnet test", null, null, "is not a literal folder" },
        new object?[] { "bash", "cd $DIR && cd sub && dotnet test", null, null, "relative to an unknown folder" },
        new object?[] { "bash", "cd && dotnet test", null, null, "no folder argument" },              // row 94: bare cd goes home
        new object?[] { "bash", "cd; dotnet test", null, null, "no folder argument" },
        new object?[] { "bash", "pushd && dotnet test", null, null, "no folder argument" },           // bare pushd swaps
        new object?[] { "bash", "pushd +1 && dotnet test", null, null, "'+1' is not a folder" },
        new object?[] { "bash", "cd -X C:/x && dotnet test", null, null, "'-X' is not a folder" },
        new object?[] { "bash", "cd C:/Program\\ Files/x && dotnet test", null, null, "backslash-escaped space" },   // row 95
        new object?[] { "bash", "cd /d C:/x; dotnet test", null, null, "a second argument" },         // row 96
        new object?[] { "bash", "cd C:/x C:/y && dotnet test", null, null, "a second argument" },
        new object?[] { "bash", "cd C:/nope; dotnet test", null, null, "the folder does not exist" }, // the cd failed
        new object?[] { "bash", "cd C:/x || dotnet test", null, null, "joined by ||" },               // runs only if cd failed
        new object?[] { "bash", "cd C:/x | dotnet test", null, null, "a pipeline stage" },
        new object?[] { "bash", "cd C:/x & dotnet test", null, null, "runs in the background" },
        new object?[] { "bash", "popd +1; dotnet test", null, null, "with arguments" },
        new object?[] { "bash", "source env.sh && dotnet test", null, null, "'source' may change the folder" },
        new object?[] { "powershell", ". ./profile.ps1; dotnet test", null, null, "'.' may change the folder" },
        new object?[] { "bash", "eval x; dotnet test", null, null, "'eval' may change the folder" },
        new object?[] { "bash", "eval cd C:/x; dotnet test", null, null, "'cd' in a position that is not modelled (after 'eval')" },
        new object?[] { "bash", "echo a) && dotnet test", null, null, "an unbalanced ')'" },
        new object?[] { "bash", "cd sub && dotnet test", "C:/elsewhere", null, "CDPATH is set" },
        // review row 100: a folder change in a position that is not modelled, or with control flow
        new object?[] { "bash", "{ cd C:/x; } && dotnet test", null, null, "a '{' group is not modelled" },
        new object?[] { "bash", "if true; then cd C:/x; fi; dotnet test", null, null, "control flow ('if')" },
        new object?[] { "bash", "if true\nthen\ncd C:/x\nfi\ndotnet test", null, null, "control flow ('if')" },
        new object?[] { "powershell", "if ($true) { Set-Location C:/x }; dotnet test", null, null, "control flow ('if')" },
        new object?[] { "bash", "command cd C:/x && dotnet test", null, null, "'cd' in a position that is not modelled (after 'command')" },
        new object?[] { "bash", "builtin cd C:/x && dotnet test", null, null, "'cd' in a position that is not modelled (after 'builtin')" },
        new object?[] { "bash", "X=1 cd C:/x && dotnet test", null, null, "'cd' in a position that is not modelled (after 'X=1')" },
        // review row 100: the joiner BEFORE a change
        new object?[] { "bash", "false && cd C:/x; dotnet test", null, null, "follows && and the tool does not depend on it" },
        new object?[] { "bash", "test -d C:/y || cd C:/x; dotnet test", null, null, "follows || (it may not have run)" },
        new object?[] { "bash", "echo | cd C:/x && dotnet test", null, null, "a pipeline stage" },
        // review row 100: the directory stack after a step that may have changed it
        new object?[] { "bash", "pushd C:/x; pushd C:/nope; popd; dotnet test", null, null, "may have changed the directory stack" },
        new object?[] { "bash", "pushd C:/x; source e.sh; popd; dotnet test", null, null, "may have changed the directory stack" },
        // review row 105: a pushd/popd joined by || | or a lone & may still have changed the stack
        new object?[] { "bash", "pushd C:/x; pushd C:/y || true; popd; dotnet test", null, null, "may have changed the directory stack" },
        new object?[] { "bash", "pushd C:/x; pushd C:/y; popd || true; popd; dotnet test", null, null, "may have changed the directory stack" },
        new object?[] { "powershell", "Push-Location C:/x; Push-Location C:/y | Out-Null; Pop-Location; dotnet test", null, null, "may have changed the directory stack" },
        new object?[] { "bash", "pushd C:/x; pushd C:/y & sleep 1; popd; dotnet test", null, null, "may have changed the directory stack" },
        // -- UNKNOWN: the structure is not proved
        new object?[] { "bash", "echo \\\"x\\\" && dotnet test", null, null, "an escaped quote" },
        new object?[] { "bash", "echo `pwd` && dotnet test", null, null, "a backtick" },
        new object?[] { "bash", "dotnet test && echo 'x", null, null, "a quote that is never closed" },
        new object?[] { "bash", "dotnet test; cat <<EOF", null, null, "a heredoc or here-string that is never closed" },
        new object?[] { "bash", "echo $((1<<2)); dotnet test", null, null, "a heredoc or here-string that is never closed" },
        new object?[] { "bash", "dotnet test; echo $'a'", null, null, "ANSI-C" },
        // -- UNKNOWN: what the tool ran on is not proved
        new object?[] { "bash", "dotnet test C:/x/App.csproj C:/y/B.csproj", null, null, "2 projects are named" },
        new object?[] { "bash", "dotnet test $PROJ", null, null, "is not literal" },
        new object?[] { "bash", "cd ~ && dotnet test App.csproj", null, null, "relative to an unknown folder" },
        new object?[] { "bash", "sqlcmd -S s -i C:/x/db/170_B.sql", null, null, "does not exist where the command ran" },
    };

    [Theory]
    [MemberData(nameof(SourceTable))]
    public void AResultIsCitedOnlyToAProvedPath(string shell, string command, string? cdPath, string? expected, string? why = null)
    {
        var t = ToolClassifier.Classify(command, BuildPassOut);
        Assert.True(t is not null, $"not classified: {command}");
        var s = SourceResolver.Resolve(command, shell, "C:/a", t!, Fake(cdPath));
        Assert.True(expected is null ? s.Path is null : s.Path is not null && Path.GetFullPath(expected) == s.Path,
                    $"{command}\n  expected {expected ?? "UNKNOWN"}\n  got      {s.Path ?? "UNKNOWN"} ({s.Why})");
        Assert.False(string.IsNullOrWhiteSpace(s.Why));
        if (why is not null) Assert.True(s.Why.Contains(why), $"{command}\n  expected reason containing: {why}\n  got: {s.Why}");
    }

    // ---- PowerShell: the REAL hook input, captured from this session on 2 Nov (Fixtures/, never a guessed shape) -------
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void TheRealPowerShellHookInputIsParsed()
    {
        var ok = ClaudeCapture.Parse(Fixture("powershell-posttooluse-real.json"));
        Assert.Equal("PowerShell", ok.ToolName);
        Assert.Equal("PostToolUse", ok.EventName);
        Assert.Contains("k3 probe stdout line", ok.Command);
        Assert.Contains("k3 probe stdout line", ok.Output);
        Assert.Contains("after", ok.Output);
        Assert.Null(ok.Error);
        var bad = ClaudeCapture.Parse(Fixture("powershell-posttoolusefailure-real.json"));
        Assert.Equal("PostToolUseFailure", bad.EventName);
        Assert.StartsWith("Exit code 3", bad.Error);
        Assert.Equal(3, ToolClassifier.ExitCodeOf(bad.Error!));
    }

    /// <summary>The real PowerShell input with only its command and output swapped for a build (the shape is untouched).</summary>
    private static string RealPowerShell(string fixture, string command, string? stdout, string? error)
    {
        var node = JsonNode.Parse(Fixture(fixture))!.AsObject();
        node["tool_input"]!["command"] = command;
        if (stdout is not null) node["tool_response"]!["stdout"] = stdout;
        if (error is not null) node["error"] = error;
        return node.ToJsonString();
    }

    [Fact]
    public void LiveAPowerShellBuildAndAFailedPowerShellTestAreCaptured()
    {
        using var db = BrainSession.BeginRolledBack();
        var repo = RepoDir();
        var leaf = Git.Leaf(Git.TopLevel(repo))!;          // the checkout's own name (review row 100), never a literal
        var build = RealPowerShell("powershell-posttooluse-real.json",
            $"Set-Location '{repo.Replace('\\', '/')}'; dotnet build Kor.Operations.Framework", BuildPassOut.Replace("\n", "\r\n"), null);
        var b = ReadBack(db, RunOk(db, new[] { "capture-tool" }, build), leaf, "build");
        Assert.Equal(("pass", leaf), (b.Outcome, b.Repo));
        Assert.Contains("\"shell\":\"powershell\"", b.Detail);
        var test = RealPowerShell("powershell-posttoolusefailure-real.json",
            $"Set-Location '{repo.Replace('\\', '/')}'; dotnet test Kor.Operations.Framework.Tests", null, "Exit code 1\r\n" + TestFailOut);
        var f = ReadBack(db, RunOk(db, new[] { "capture-tool" }, test), leaf, "test");
        Assert.Equal(("fail", leaf), (f.Outcome, f.Repo));
    }

    // ---- module attribution (#125): the module is the repo the tool ran on, unless --module is given -------------------
    [Fact]
    public void LiveTheModuleIsTheRepoTheCommandRanInNotTheSessionLane()
    {
        using var db = BrainSession.BeginRolledBack();
        var repo = RepoDir();
        var elsewhere = Path.Combine(Path.GetTempPath(), "kor-k3-cwd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(elsewhere);
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                session_id = "k3-module-session", cwd = elsewhere, hook_event_name = "PostToolUse", tool_name = "Bash",
                tool_input = new { command = $"cd \"{repo.Replace('\\', '/')}\" && dotnet build Kor.Operations.Framework" },
                tool_response = new { stdout = BuildPassOut, stderr = "" },
            });
            var leaf = Git.Leaf(Git.TopLevel(repo))!;      // the checkout's own name (review row 100), never a literal
            var row = ReadBack(db, RunOk(db, new[] { "capture-tool" }, json), leaf, "build");
            Assert.Equal(leaf, row.Module);
            Assert.Equal(Git.Run(repo, "rev-parse", "HEAD")!.Trim(), row.Commit);
            Assert.Contains("\"module_from\":\"the repo it ran on\"", row.Detail);
            Assert.Contains(Path.Combine(repo, "Kor.Operations.Framework").Replace("\\", "\\\\"), row.Detail);   // the project, proved
        }
        finally { Directory.Delete(elsewhere); }
    }

    [Fact]
    public void LiveAnUnknownSourceRecordsNoRepoOrCommitAndSaysWhy()
    {
        using var db = BrainSession.BeginRolledBack();
        var json = JsonSerializer.Serialize(new
        {
            session_id = "k3-unknown-session", cwd = RepoDir(), hook_event_name = "PostToolUse", tool_name = "Bash",
            tool_input = new { command = "cd ~ && dotnet build" }, tool_response = new { stdout = BuildPassOut, stderr = "" },
        });
        var row = ReadBack(db, RunOk(db, new[] { "capture-tool", "--module", Module }, json), Module, "build");
        Assert.Null(row.Repo);
        Assert.Null(row.Commit);
        Assert.Contains("\"source\":null", row.Detail);
        Assert.Contains("is not a literal folder", row.Detail);
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

    // ---- hook mode never fails, and a test never writes the REAL hook log ----------------------------------------------
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

    [Fact]
    public void TheTestOverloadNeverWritesTheRealHookLog()
    {
        // The only path to the real log is Main. The test overload logs a hook failure to a temp file: prove it on a
        // failing hook call, by the real log's length before and after (absent counts as -1).
        static long Size() => File.Exists(Program.DefaultHookErrorLog) ? new FileInfo(Program.DefaultHookErrorLog).Length : -1;
        var before = Size();
        Assert.Equal(0, Program.Run(new[] { "capture-tool", "--hook" }, new StringWriter(), new StringWriter()));
        Assert.Equal(before, Size());
    }

    private static string TempLog() => Path.Combine(Path.GetTempPath(), $"kor-k3-test-{Guid.NewGuid():N}.log");

    // ---- LIVE: each verb writes its row in a ROLLED-BACK transaction on the live KorStandards, read back inside it ------
    private static string RepoDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, ".git"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("the test is not running inside the Operations checkout");
    }

    private static int RunOk(BrainSession db, string[] args, string? stdin = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = Program.Run(args, () => stdin, output, error, TempLog(), db);
        Assert.True(code == 0, $"exit {code}: {error}");
        var text = output.ToString();
        var m = System.Text.RegularExpressions.Regex.Match(text, @"brain\.Event (\d+)");
        Assert.True(m.Success, $"no event id in: {text}");
        return int.Parse(m.Groups[1].Value);
    }

    private static StoredEvent ReadBack(BrainSession db, int id, string module, string kind)
    {
        var row = BrainStore.Last(module, 200, kind, db).SingleOrDefault(r => r.Id == id);
        Assert.NotNull(row);
        return row!;
    }

    private static StoredEvent ReadBack(BrainSession db, int id, string kind) => ReadBack(db, id, Module, kind);

    [Fact]
    public void LiveARolledBackSessionLeavesNothing()
    {
        // The guarantee every live test rests on: a row written in BeginRolledBack is gone once it is disposed.
        int id;
        using (var db = BrainSession.BeginRolledBack())
        {
            id = BrainStore.Insert(new BrainEvent(Module, "build", null, null, "rollback probe", "pass", null, "k3-test"), db);
            Assert.Contains(BrainStore.Last(Module, 50, "build", db), r => r.Id == id);
        }
        Assert.DoesNotContain(BrainStore.Last(Module, 50, "build"), r => r.Id == id);
    }

    [Fact]
    public void LiveCaptureGitPostCommitWritesTheHeadCommitWithBlobs()
    {
        using var db = BrainSession.BeginRolledBack();
        var dir = RepoDir();
        var head = Git.Run(dir, "rev-parse", "HEAD")!.Trim();
        var row = ReadBack(db, RunOk(db, new[] { "capture-git", "post-commit", "--dir", dir, "--module", Module }), "commit");
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
        using var db = BrainSession.BeginRolledBack();
        var dir = RepoDir();
        var head = Git.Run(dir, "rev-parse", "HEAD")!.Trim();
        var stdin = $"refs/heads/develop {head} refs/heads/develop {new string('0', 40)}\n";
        var row = ReadBack(db, RunOk(db, new[] { "capture-git", "pre-push", "origin", "https://github.com/kor/Operations.git", "--result", "1", "--dir", dir, "--module", Module }, stdin), "push");
        Assert.Equal("refused", row.Outcome);
        Assert.Equal(head, row.Commit);
        Assert.Contains("origin Operations.git:", row.Ref);
        Assert.Contains("-> refs/heads/develop", row.Ref);
    }

    [Fact]
    public void LiveCaptureGitCheckoutAndMergeWriteTheirKinds()
    {
        using var db = BrainSession.BeginRolledBack();
        var dir = RepoDir();
        var head = Git.Run(dir, "rev-parse", "HEAD")!.Trim();
        var c = ReadBack(db, RunOk(db, new[] { "capture-git", "post-checkout", head, head, "1", "--dir", dir, "--module", Module }), "checkout");
        Assert.Equal(head, c.Commit);
        Assert.StartsWith("branch ", c.Ref);
        var m = ReadBack(db, RunOk(db, new[] { "capture-git", "post-merge", "0", "--dir", dir, "--module", Module }), "merge");
        Assert.Equal(head, m.Commit);
    }

    [Fact]
    public void LiveCaptureToolRecordsAFailingTestFromPostToolUseFailure()
    {
        using var db = BrainSession.BeginRolledBack();
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
        var row = ReadBack(db, RunOk(db, new[] { "capture-tool", "--module", Module }, json), "test");
        Assert.Equal("fail", row.Outcome);
        Assert.Equal("dotnet test Kor.Operations.Framework.Tests", row.Ref);
        Assert.Equal(Git.Run(dir, "rev-parse", "HEAD")!.Trim(), row.Commit);
        Assert.Contains("k3-test-session", row.Actor);
    }

    [Fact]
    public void LiveCaptureToolRecordsAPassingBuildFromPostToolUse()
    {
        using var db = BrainSession.BeginRolledBack();
        var json = JsonSerializer.Serialize(new
        {
            session_id = "k3-test-session", cwd = RepoDir(), hook_event_name = "PostToolUse", tool_name = "Bash",
            tool_input = new { command = "dotnet build Kor.Operations.Framework.Cli" },
            tool_response = new { stdout = BuildPassOut + "\nServer=x;Password=hunter2;", stderr = "", interrupted = false },
        });
        var row = ReadBack(db, RunOk(db, new[] { "capture-tool", "--module", Module }, json), "build");
        Assert.Equal("pass", row.Outcome);
        Assert.DoesNotContain("hunter2", row.Detail);
    }

    [Fact]
    public void LiveCaptureToolOnANonMatchingCommandWritesNoRow()
    {
        using var db = BrainSession.BeginRolledBack();
        var json = JsonSerializer.Serialize(new
        {
            session_id = "k3-test-session", cwd = RepoDir(), hook_event_name = "PostToolUse", tool_name = "Bash",
            tool_input = new { command = "git status --short" }, tool_response = new { stdout = "", stderr = "" },
        });
        var output = new StringWriter();
        Assert.Equal(0, Program.Run(new[] { "capture-tool", "--module", Module }, () => json, output, new StringWriter(), TempLog(), db));
        Assert.Equal("nothing to record", output.ToString().Trim());
        Assert.Empty(BrainStore.Last(Module, 1000, null, db));   // this transaction wrote nothing under the module
    }

    [Fact]
    public void LiveCaptureCompactStoresTheSummaryAsTheLaneResume()
    {
        using var db = BrainSession.BeginRolledBack();
        var summary = $"K3 test compaction summary {Guid.NewGuid():N}\nPassword=should-not-land\nwhere we stopped: K3 tests";
        var json = JsonSerializer.Serialize(new
        {
            session_id = "k3-compact-session", transcript_path = "C:/x/k3c.jsonl", cwd = RepoDir(),
            hook_event_name = "PostCompact", trigger = "manual", compact_summary = summary,
        });
        var row = ReadBack(db, RunOk(db, new[] { "capture-compact", "--module", Module }, json), "compact");
        Assert.Equal("stored", row.Outcome);
        using var d = JsonDocument.Parse(row.Detail!);
        var laneId = d.RootElement.GetProperty("lane_state_id").GetInt32();
        var lane = BrainStore.LaneStateById(laneId, db);
        Assert.NotNull(lane);
        Assert.Equal(Module, lane!.Lane);
        Assert.Equal("compaction k3-compact-session", lane.WrittenBy);
        Assert.Contains("where we stopped: K3 tests", lane.ResumeBlock);
        Assert.DoesNotContain("should-not-land", lane.ResumeBlock);
    }

    [Fact]
    public void LiveCaptureEndRecordsTheTranscriptAndReason()
    {
        using var db = BrainSession.BeginRolledBack();
        var json = JsonSerializer.Serialize(new
        {
            session_id = "k3-end-session", transcript_path = "C:/x/k3e.jsonl", cwd = RepoDir(),
            hook_event_name = "SessionEnd", reason = "prompt_input_exit",
        });
        var sw = Stopwatch.StartNew();
        var row = ReadBack(db, RunOk(db, new[] { "capture-end", "--module", Module }, json), "session-end");
        Assert.Equal("C:/x/k3e.jsonl", row.Ref);
        Assert.Equal("prompt_input_exit", row.Outcome);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"capture-end took {sw.Elapsed}");
    }
}
