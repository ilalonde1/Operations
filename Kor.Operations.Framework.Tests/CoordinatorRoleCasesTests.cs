using System.Reflection;
using System.Text.Json;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Enforce;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Cases committed before the implementation. Reflection keeps this project buildable on 206615f3: absence of the
/// policy is an assertion failure, not a compiler error. The real cases preserve the tool, operative input, cwd and session
/// from the named JSONL line; file contents are deliberately not copied. No recorded command is ever executed.
/// COVERS: literal file targets, shell directory state, allowed work, role/subagent boundaries, and the replay entry point.
/// DOES NOT COVER: arbitrary program effects, Claude applying a refusal, live role storage or approval provenance (separate
/// integration tests). Same-class miss: a permitted dotnet test can itself rewrite a project fixture.
/// </summary>
public sealed class CoordinatorRoleCasesTests
{
    private const string Framework = "C:/VIsual Studio Projects/Framework";
    private const string Repo = "C:/VIsual Studio Projects/KOR.Drafter";
    private const string Session = "coordinator-case-session";

    public sealed record Case(string Id, string Tool, string Input, string Cwd, string Session, bool Refuse);

    public static IEnumerable<object[]> RealCases() =>
        JsonSerializer.Deserialize<List<Case>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "coordinator-role-cases.json")))!
            .Select(c => new object[] { c.Id, c.Tool, c.Input, c.Cwd, c.Session, c.Refuse });

    private static JsonElement Evaluate(string tool, string input, string cwd = Framework, string? role = null,
                                       string? agent = null, string? start = null, string session = Session, string? scratch = null)
    {
        var type = typeof(DrafterPythonGuard).Assembly.GetType("Kor.Operations.Framework.Enforce.CoordinatorRoleGuard");
        Assert.NotNull(type);
        var method = type.GetMethod("Evaluate", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var raw = JsonSerializer.Serialize(new
        {
            tool_name = tool, tool_input = new Dictionary<string, string> { [Rules.InputFieldOf(tool)] = input },
            cwd, session_id = session, agent_id = agent, scratchpad_dir = scratch,
        });
        return JsonSerializer.SerializeToElement(method.Invoke(null, new object?[] { raw, role, start }));
    }

    [Theory]
    [MemberData(nameof(RealCases))]
    public void RealTranscriptCall(string id, string tool, string input, string cwd, string session, bool refuse)
    {
        var result = Evaluate(tool, input, cwd, session: session);
        Assert.True(result.GetProperty("Refuse").GetBoolean() == refuse, $"{id}: {result}");
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("Rule").GetString()));
        if (refuse) Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("Target").GetString()));
    }

    [Theory]
    [InlineData("Edit", "{R}/src/existing.cs")]
    [InlineData("Write", "{R}/work/bundle/manifest.json")]
    [InlineData("MultiEdit", "{R}/tests/Test.cs")]
    [InlineData("NotebookEdit", "{R}/work/analysis.ipynb")]
    [InlineData("Write", "{F}/tools/new.cs")]
    [InlineData("Bash", "cd \"{R}\" && echo x > src/new.cs")]
    [InlineData("Bash", "cd \"{R}\"; tee src/new.cs <<'EOF'\nx\nEOF")]
    [InlineData("Bash", "cd \"{R}\" && cat > src/new.cs <<'EOF'\nx\nEOF")]
    [InlineData("Bash", "cd '/c/VIsual Studio Projects/KOR.Drafter'; mkdir -p work/bundle")]
    [InlineData("Bash", "pushd \"{R}\"; rm -f work/x.json")]
    [InlineData("Bash", "cd \"{R}\"; cd \"{F}\"; cd -; sed -i 's/a/b/' work/x.py")]
    [InlineData("Bash", "pushd \"{R}\"; pushd \"{F}\"; popd; touch work/x.json")]
    [InlineData("Bash", "cp {S}/x.cs \"{R}/src/x.cs\"")]
    [InlineData("Bash", "mv \"{R}/src/x.cs\" {S}/x.cs")]
    [InlineData("Bash", "cd \"{R}\"; cp a.cs b.cs")]
    [InlineData("Bash", "echo x >> \"{R}/data/file.txt\"")]
    [InlineData("PowerShell", "Set-Location -Path '{R}'; Set-Content -LiteralPath src/x.cs -Value x")]
    [InlineData("PowerShell", "pushd '{R}'; 'x' | Out-File -Encoding utf8 src/x.cs")]
    [InlineData("PowerShell", "'x' | Out-File -FilePath '{R}/src/x.cs'")]
    [InlineData("PowerShell", "New-Item -Path '{R}/src' -Name x.cs -ItemType File")]
    [InlineData("PowerShell", "Copy-Item '{S}/x.cs' -Destination '{R}/src/x.cs'")]
    [InlineData("PowerShell", "Move-Item '{R}/src/x.cs' -Destination '{S}/x.cs'")]
    [InlineData("PowerShell", "Remove-Item -LiteralPath '{R}/work/x.json'")]
    [InlineData("Bash", "cd \"{R}\"; pwsh -File docs/one-tool/run-on-210/send-set.ps1 -Job 1")]
    [InlineData("PowerShell", "& '{R}/docs/one-tool/run-on-210/start-on-210.ps1'")]
    [InlineData("Bash", "takeoff.exe model-audit bundle")]
    [InlineData("Bash", "dotnet run --project '{R}/Kor.Operations.EngineeringTools.TakeoffCli' -- model-audit bundle")]
    [InlineData("Bash", "python -I '{R}/work/_acceptance/build_bundle_v2.py'")]
    [InlineData("Bash", "cd \"{R}/work\"; python -I fix.py")]
    [InlineData("Bash", "python -I /c/other/build_bundle_new.py")]
    [InlineData("Bash", "cd \"{R}\"; dotnet test tests --filter Fixture > work/test-log.txt")]
    public void WorkIsRefusedByItsTarget(string tool, string input) =>
        Assert.True(Evaluate(tool, Expand(input)).GetProperty("Refuse").GetBoolean());

    [Theory]
    [InlineData("Write", "{R}/docs/codex/CODEX-A.md")]
    [InlineData("Edit", "{F}/docs/HANDOVER.md")]
    [InlineData("Write", "{F}/lanes/2026-11-24/brief.txt")]
    [InlineData("Write", "C:/KOR-Operations/push-exceptions.txt")]
    [InlineData("Write", "{S}/held-back.cs")]
    [InlineData("Bash", "cd \"{R}\"; git status; git diff; git add src/x.cs; git commit -m 'Edit src/x.cs'; git push")]
    [InlineData("Bash", "cd \"{R}\"; dotnet build src -c Debug && dotnet test tests --filter Fixture")]
    [InlineData("Bash", "cd \"{R}\"; pwsh -File tools/install-kor.ps1; kor --version")]
    [InlineData("Bash", "kor record --kind finding --says 'takeoff.exe model-audit bundle'")]
    [InlineData("Bash", "python -I '{F}/tools/kor_review.py' review")]
    [InlineData("Bash", "python -I '{F}/tools/board_render.py'")]
    [InlineData("Bash", "python -I '{F}/tools/guard_log.py'")]
    [InlineData("Bash", "cat '{R}/work/build_bundle.py'; rg 'takeoff.exe' '{R}/src'")]
    [InlineData("Bash", "echo 'sed -i src/x.cs'")]
    [InlineData("Bash", "git commit -F - <<'EOF'\nrm src/x.cs\nEOF")]
    [InlineData("PowerShell", "'a' | Out-File -FilePath '{F}/lanes/test.txt'")]
    [InlineData("Bash", "cd \"{R}\"; cd \"{F}\"; echo brief > lanes/brief.txt")]
    [InlineData("Bash", "echo x > /dev/null 2>&1")]
    public void CoordinatingAndCheckingPass(string tool, string input) =>
        Assert.False(Evaluate(tool, Expand(input)).GetProperty("Refuse").GetBoolean());

    [Theory]
    [InlineData("worker", null, Framework, false)]
    [InlineData("coordinator", null, Repo, true)]
    [InlineData(null, "helper", Framework, false)]
    [InlineData(null, null, Repo, false)]
    [InlineData(null, null, Framework, true)]
    public void SessionRoleAndSubagents(string? role, string? agent, string start, bool refuse) =>
        Assert.Equal(refuse, Evaluate("Edit", Repo + "/src/x.cs", Repo, role, agent, start).GetProperty("Refuse").GetBoolean());

    [Fact]
    public void ScratchOfAnotherSessionAndLookalikeAllowedPathsAreNotExceptions()
    {
        foreach (var path in new[] { Scratch.Replace(Session, "other-session") + "/x.cs", Repo + "/docs/codex/CODEX-x.md.cs",
                                    Framework + "/lanes/../tools/x.cs", Repo + "/docs/HANDOVER.md" })
            Assert.True(Evaluate("Write", path).GetProperty("Refuse").GetBoolean());
    }

    [Fact]
    public void ReplayIsAReadOnlyCliVerb()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "role-replay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "session.jsonl");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                type = "assistant", cwd = Framework, sessionId = Session,
                message = new { content = new[] { new { type = "tool_use", id = "call1", name = "Edit", input = new { file_path = Repo + "/src/a.cs" } } } },
            }) + "\n");
            var output = new StringWriter();
            Assert.Equal(0, Program.Run(new[] { "role", "replay", path }, () => null, output, new StringWriter(), Path.Combine(root, "errors.log")));
            Assert.Contains("L1 REFUSE", output.ToString());
            Assert.Contains("/src/a.cs", output.ToString());
        }
        finally { Directory.Delete(root, true); }
    }

    private static string Scratch => Path.Combine(Path.GetTempPath(), "claude", "hash", Session, "scratchpad").Replace('\\', '/');
    private static string Expand(string text) => text.Replace("{F}", Framework).Replace("{R}", Repo).Replace("{S}", Scratch);
}
