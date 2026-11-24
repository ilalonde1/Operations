using System.Text.Json;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Enforce;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Cases-first differential for round 2: same-command path state, temporary/check outputs, cleanup, unresolved names,
/// and the role escape, including real calls. No recorded command is executed. Does not establish arbitrary program
/// effects, filesystem links, or complete shell semantics; an allowed test can still regenerate a project fixture.
/// </summary>
public sealed class CoordinatorRoleRound2Tests
{
    private const string F = "C:/VIsual Studio Projects/Framework";
    private const string R = "C:/VIsual Studio Projects/Operations";
    private const string S = "round2-session";
    private static string Expand(string s) => s.Replace("{R}", R).Replace("{F}", F)
        .Replace("{T}", Path.GetTempPath().Replace('\\', '/').TrimEnd('/'))
        .Replace("{S}", Path.Combine(Path.GetTempPath(), "claude", "hash", S, "scratchpad").Replace('\\', '/'));
    private static CoordinatorRoleGuard.Decision Decide(string tool, string input, string cwd = F, string session = S, string? role = null) =>
        CoordinatorRoleGuard.Evaluate(JsonSerializer.Serialize(new { tool_name = tool,
            tool_input = new Dictionary<string, string> { [Rules.InputFieldOf(tool)] = Expand(input) }, cwd, session_id = session }), role, F);

    public static IEnumerable<object[]> RealCases() =>
        JsonSerializer.Deserialize<List<CoordinatorRoleCasesTests.Case>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "coordinator-role-round2-cases.json")))!.Select(c => new object[] { c.Id, c.Tool, c.Input, c.Cwd, c.Session, c.Refuse });

    [Theory, MemberData(nameof(RealCases))]
    public void NamedReplayCall(string id, string tool, string input, string cwd, string session, bool refuse)
    {
        // Recreate this historical empty-root cleanup inside the sandbox. The real folder is now absent, which the
        // stricter round-3 rule refuses; complete transcript replay still evaluates the untouched original input.
        using var cleanup = id == "b6568d08 L6533" ? new CoordinatorRoleRound3Sandbox() : null;
        if (cleanup is not null)
            input = input.Replace(@"C:\VIsual Studio Projects\Operations-kor-whole-narrow", cleanup.Empty.Replace('/', '\\'));
        var d = Decide(tool, input, cwd, session);
        Assert.True(d.Refuse == refuse, $"{id}: {d}");
        // L1234 fixes the cwd but its later loop-generated p$id.txt is still unresolved (brief item 5).
        if (id.EndsWith(" L1234", StringComparison.Ordinal)) Assert.Contains("$id", d.Target);
    }

    [Theory]
    [InlineData("Bash", "S=\"{S}\"; echo x > $S/x.txt")]
    [InlineData("Bash", "export S='{S}' && cd \"$S\" && mkdir -p held && echo x > held/x.txt")]
    [InlineData("Bash", "X=/tmp/probe\nY=$X/held\ncd $Y; touch x")]
    [InlineData("Bash", "X=/tmp/probe || echo x > ${X}/x.txt")]
    [InlineData("Bash", "X=/c/tmp/probe; echo x > $X/x.txt")]
    [InlineData("PowerShell", "$X = '{S}'; Set-Location $X; 'x' | Out-File held.txt")]
    [InlineData("PowerShell", "$X = \"C:/tmp/probe\"; $Y = \"$X/held\"; Set-Content -LiteralPath $Y/x.txt -Value x")]
    [InlineData("PowerShell", "$X='{S}'; cd $X; New-Item -ItemType Directory held")]
    [InlineData("Bash", "echo x > /tmp/x.txt")]
    [InlineData("Bash", "echo x > C:/tmp/x.txt")]
    [InlineData("Bash", "echo x > $TEMP/x.txt")]
    [InlineData("Bash", "echo x > ${TEMP}/x.txt")]
    [InlineData("Bash", "echo x > $TMP/x.txt")]
    [InlineData("Bash", "rm -rf '{T}/kor-scope-probe'; mkdir -p '{T}/kor-scope-probe'")]
    [InlineData("PowerShell", "Set-Content -LiteralPath $env:TEMP/x.txt -Value x")]
    [InlineData("PowerShell", "'x' > $env:TMP\\x.txt")]
    [InlineData("Bash", "cmd /c echo x > %TEMP%/x.txt")]
    [InlineData("Write", "{T}/other-session/scratchpad/x.cs")]
    [InlineData("Bash", "cd '{R}'; dotnet test tests --results-directory obj/coord > obj/coord-live.log")]
    [InlineData("PowerShell", "Set-Location '{R}'; dotnet build -o 'check output' > 'check output/build.log'")]
    [InlineData("Bash", "cd '/c/VIsual Studio Projects/Operations'; dotnet test tests --output=checks > checks/test.log")]
    [InlineData("Bash", "cd '{R}'; dotnet test --results-directory checks; cat checks/x.trx > checks/summary.txt")]
    [InlineData("Write", "{R}/project/obj/coord.txt")]
    [InlineData("Edit", "{R}/project/bin/Debug/x.json")]
    [InlineData("PowerShell", "Set-Content '{R}/TestResults/x.txt' x")]
    [InlineData("PowerShell", @"Remove-Item -LiteralPath '\\?\C:\VIsual Studio Projects\Operations-round2-absent' -Recurse")]
    [InlineData("Bash", "rm -rf '/c/VIsual Studio Projects/Operations-round2-absent'")]
    [InlineData("Bash", "D='C:/VIsual Studio Projects/Operations-round2-absent'; rm -rf \"$D\"")]
    public void ScratchTempCheckOutputAndCleanupPass(string tool, string input)
    {
        // Round 3 requires an actually empty folder. Retain these three cases' syntax, but stop relying on an absent
        // real sibling being disposable; every created/removed folder belongs to the test sandbox.
        using var cleanup = input.Contains("Operations-round2-absent", StringComparison.Ordinal) ? new CoordinatorRoleRound3Sandbox() : null;
        if (cleanup is not null)
            input = input.Replace(@"\\?\C:\VIsual Studio Projects\Operations-round2-absent", @"\\?\" + cleanup.Empty.Replace('/', '\\'))
                .Replace("/c/VIsual Studio Projects/Operations-round2-absent", CoordinatorRoleRound3Sandbox.Msys(cleanup.Empty))
                .Replace("C:/VIsual Studio Projects/Operations-round2-absent", cleanup.Empty);
        var d = Decide(tool, input);
        Assert.False(d.Refuse, d.ToString());
    }

    [Theory]
    [InlineData("Edit", "{R}/src/a.cs")]
    [InlineData("Write", "/c/VIsual Studio Projects/Operations/tests/a.cs")]
    [InlineData("MultiEdit", "C:\\VIsual Studio Projects\\Operations\\data\\a.json")]
    [InlineData("Bash", "D='/c/VIsual Studio Projects/KOR.Drafter/work/set'; rm -rf \"$D\"")]
    [InlineData("PowerShell", "$D = 'C:/VIsual Studio Projects/KOR.Drafter/work/set'; Remove-Item -LiteralPath $D -Recurse")]
    [InlineData("Bash", "X='{R}'; cd $X; echo x > src/a.cs")]
    [InlineData("Bash", "python -I C:/VIsual\\ Studio\\ Projects/KOR.Drafter/work/build_bundle.py")]
    [InlineData("PowerShell", "& '{R}/docs/one-tool/run-on-210/send-set.ps1'")]
    [InlineData("Bash", "T='{R}/bin/takeoff.exe'; $T model-audit bundle")]
    [InlineData("Bash", "echo x > $UNKNOWN/x")]
    [InlineData("PowerShell", "Set-Content $UNKNOWN/x x")]
    [InlineData("Bash", "X=$(pwd); echo x > $X/x")]
    [InlineData("Bash", "for i in 1 2; do X=/tmp; done; echo x > $X/x")]
    [InlineData("PowerShell", "foreach ($i in 1,2) { $X = 'C:/tmp' }; Set-Content $X/x x")]
    [InlineData("Bash", "X=/tmp; X=$(pwd); echo x > $X/x")]
    [InlineData("Bash", "echo x > /tmp/../project.txt")]
    [InlineData("Bash", "echo x > C:/tmp/../project.txt")]
    [InlineData("PowerShell", "Set-Content '{R}/obj/../src/a.cs' x")]
    [InlineData("Write", "{R}/object/a.txt")]
    [InlineData("Bash", "cd '{R}'; dotnet test > checks/x.log; dotnet test --output checks")]
    [InlineData("Bash", "cd '{R}'; dotnet test -o checks; echo x > checks/../src/a.cs")]
    [InlineData("Bash", "cd '{R}'; dotnet run --output checks > checks/x.log")]
    [InlineData("PowerShell", "Remove-Item -LiteralPath '{R}/src' -Recurse")]
    [InlineData("Bash", "rm -rf '{R}'")]
    [InlineData("Bash", "rm -rf '{R}-coordinator-role'")]
    [InlineData("Bash", "rm -rf '{R}-round2-absent/src'")]
    [InlineData("Bash", "mkdir '{R}-round2-absent'")]
    public void ProjectWorkAndUnresolvedTargetsStillRefuse(string tool, string input) => Assert.True(Decide(tool, input).Refuse);

    [Theory]
    [InlineData("Bash", "echo x > $UNKNOWN/x", "$UNKNOWN")]
    [InlineData("PowerShell", "Set-Content ${UNKNOWN}/x x", "${UNKNOWN}")]
    [InlineData("Bash", "cd $UNKNOWN; echo x > relative.txt", "$UNKNOWN")]
    public void UnresolvedMessageNamesVariableAndRemedy(string tool, string input, string variable)
    {
        var text = CoordinatorRoleGuard.Says(Decide(tool, input));
        Assert.Contains(variable, text);
        Assert.Contains("assign it in the same command or use a literal path", text);
    }

    [Theory]
    [InlineData("Bash", "kor role set worker --session round2-session", true)]
    [InlineData("PowerShell", "& 'C:/KOR-Operations/kor/kor.exe' role set worker --session round2-session", true)]
    [InlineData("Bash", "X=round2-session; kor role set worker --session=$X", true)]
    [InlineData("Bash", "kor role set worker --session other-worker", false)]
    [InlineData("PowerShell", "kor role set worker --session other-worker", false)]
    [InlineData("Bash", "kor role set coordinator --session round2-session", false)]
    public void SelfDemotionBoundary(string tool, string input, bool refuse) => Assert.Equal(refuse, Decide(tool, input).Refuse);
}

[Collection(LiveDb.Name)]
public sealed class CoordinatorRoleRound2IntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "round2-" + Guid.NewGuid().ToString("N"));
    private readonly string _session = "zz-role-r2-" + Guid.NewGuid().ToString("N");
    public CoordinatorRoleRound2IntegrationTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void NewSessionKeepsSharedChecksDuringOutage()
    {
        var now = DateTimeOffset.UtcNow;
        var checks = CheckCache.Load(CheckCache.DefaultDir)?.Checks ?? new();
        CheckCache.Save(_root, new(now.AddMinutes(-5), checks));
        var result = CheckCache.GetSession(_root, _session, "C:/Framework", () => throw new IOException("offline"), now, _ => { }, out _);
        Assert.NotNull(result.Checks);
        Assert.Equal(checks.Count, result.Checks.Count);
        Assert.Equal("C:/Framework", result.StartFolder);
    }

    [Fact]
    public void RoleCacheFilesAreBoundedOnNewSession()
    {
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 140; i++)
        {
            var p = Path.Combine(_root, $"kor-role-old-{i}.json");
            File.WriteAllText(p, JsonSerializer.Serialize(new CheckCache.RoleSnapshot(now.AddDays(-60), null, null, "C:/Framework")));
            File.SetLastWriteTimeUtc(p, now.AddDays(-60).UtcDateTime);
        }
        CheckCache.GetSession(_root, _session, "C:/Framework", () => (new(), null), now, _ => { }, out _);
        Assert.InRange(Directory.GetFiles(_root, "kor-role-*.json").Length, 1, 128);
    }

    [Fact]
    public void LiftLogsOncePerApprovalIncludingSelfDemotion()
    {
        var transcript = Path.Combine(_root, "transcript.jsonl");
        var now = DateTimeOffset.UtcNow.AddSeconds(-2);
        void Approve(DateTimeOffset at)
        {
            File.WriteAllText(Path.Combine(_root, _session + ".jsonl"), JsonSerializer.Serialize(new { prompt = CoordinatorRoleGuard.Approval }));
            File.AppendAllText(transcript, JsonSerializer.Serialize(new { type = "user", sessionId = _session, timestamp = at,
                entrypoint = "cli", promptSource = "typed", message = new { role = "user", content = CoordinatorRoleGuard.Approval } }) + "\n");
        }
        using var db = BrainDb.OpenRolledBack();
        using var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        string Enforce(string command)
        {
            var output = new StringWriter();
            var raw = JsonSerializer.Serialize(new { tool_name = "Bash", tool_input = new { command }, session_id = _session,
                cwd = "C:/Framework", transcript_path = transcript });
            Program.Run(new[] { "enforce", "--hook", "--module", "zz-role-test", "--cache-dir", _root, "--prompts-dir", _root },
                () => raw, output, new StringWriter(), Path.Combine(_root, "errors.log"), db, board);
            return output.ToString();
        }
        var demote = "kor role set worker --session " + _session;
        Assert.Contains("Coordinator role refuses", Enforce(demote));
        Approve(now);
        Assert.Equal("", Enforce(demote));
        Assert.Equal("", Enforce("echo x > C:/project/src/a.cs"));
        int Lifts()
        {
            using var c = db.Command("SELECT COUNT(*) FROM brain.Event WHERE Session=@s AND Ref='role:lift'");
            c.Parameters.AddWithValue("@s", _session);
            return Convert.ToInt32(c.ExecuteScalar());
        }
        Assert.Equal(1, Lifts());
        Approve(now.AddSeconds(1));
        Assert.Equal("", Enforce(demote));
        Assert.Equal(2, Lifts());
        File.WriteAllText(Path.Combine(_root, _session + ".jsonl"), "");
        Assert.Contains("Coordinator role refuses", Enforce(demote));
    }
}
