using System.Text.Json;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Enforce;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Board row 1935: a new .py under KOR.Drafter tools/ is refused unless Ian's latest message carries the words. Runs against
/// a throwaway checkout (a folder named KOR.Drafter holding .git and tools/existing.py), never the real one, which a live job
/// is using. The logged row is on live KorStandards in a rolled-back transaction.
/// Covers: Write, Edit, MultiEdit; redirect, tee, cp, mv, git mv, New-Item, Set-Content, Out-File, Copy-Item, a cp into the
/// folder, a sub-shell, a script the command runs, a relative path after cd; what passes; the lift; the fail-open; the row.
/// Does NOT cover: Claude Code applying the deny (the live proof pipes real hook JSON through the built kor).
/// </summary>
[Collection(LiveDb.Name)]
public sealed class DrafterPythonGuardTests : IDisposable
{
    private readonly string _root;
    private readonly string _repo;
    private string Tools => _repo.Replace('\\', '/') + "/tools";

    public DrafterPythonGuardTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"zz-py-{Guid.NewGuid():N}");
        _repo = Path.Combine(_root, "KOR.Drafter");
        Directory.CreateDirectory(Path.Combine(_repo, ".git"));
        Directory.CreateDirectory(Path.Combine(_repo, "tools", "sub"));
        File.WriteAllText(Path.Combine(_repo, "tools", "existing.py"), "# here\n");
        File.WriteAllText(Path.Combine(_repo, "seed.py"), "# here\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private DrafterPythonGuard.Refusal? D(string tool, string input, string? cwd = null, string? scratch = null) =>
        DrafterPythonGuard.Decide(tool, input, cwd ?? _root, scratch);

    [Theory]
    [InlineData("Write")]
    [InlineData("Edit")]       // an Edit with an empty old_string creates a file
    [InlineData("MultiEdit")]
    public void AFileToolCreatingANewPyUnderToolsIsRefused(string tool)
    {
        Assert.NotNull(D(tool, Tools + "/new_tool.py"));
        Assert.NotNull(D(tool, Tools + "/sub/new_tool.py"));
        Assert.NotNull(D(tool, Tools + "/newfolder/new_tool.py"));
        Assert.NotNull(D(tool, Tools.Replace('/', '\\') + "\\new_tool.py"));
        Assert.NotNull(D(tool, "tools/new_tool.py", _repo));
    }

    [Theory]
    [InlineData("Write", "{T}/existing.py")]            // a Write replacing an existing file
    [InlineData("Edit", "{T}/existing.py")]
    [InlineData("Write", "{T}/../newscript.py")]        // the repo root, not tools/
    [InlineData("Write", "{T}/notes.txt")]
    [InlineData("Write", "{T}/new_tool.pyc")]
    [InlineData("Write", "{T}/new_tool.cs")]
    [InlineData("Write", "C:/elsewhere/tools/new_tool.py")]
    public void AFileToolThatIsNotANewToolPasses(string tool, string path) =>
        Assert.Null(D(tool, path.Replace("{T}", Tools)));

    [Fact]
    public void TheRepoMustBeACheckoutAndTheScratchpadPasses()
    {
        // a folder that happens to be named KOR.Drafter with no .git is no checkout (a scratch copy, a fixture)
        var other = Path.Combine(_root, "scratch", "KOR.Drafter", "tools");
        Directory.CreateDirectory(other);
        Assert.Null(D("Write", other.Replace('\\', '/') + "/new_tool.py"));
        Assert.Null(D("Write", Tools + "/new_tool.py", scratch: _repo));
    }

    [Theory]
    [InlineData("Bash", "cat > \"{T}/new_tool.py\" <<'EOF'\nprint(1)\nEOF")]
    [InlineData("Bash", "echo x >> \"{T}/new_tool.py\"")]
    [InlineData("Bash", "echo x>\"{T}/new_tool.py\"")]
    [InlineData("Bash", "echo x | tee -a \"{T}/new_tool.py\"")]
    [InlineData("Bash", "cp /tmp/a.py \"{T}/new_tool.py\"")]
    [InlineData("Bash", "cp -f /tmp/new_tool.py \"{T}/\"")]
    [InlineData("Bash", "mv \"{T}/existing.py\" \"{T}/renamed.py\"")]
    [InlineData("Bash", "git mv \"{T}/existing.py\" \"{T}/renamed.py\"")]
    [InlineData("Bash", "touch \"{T}/new_tool.py\"")]
    [InlineData("Bash", "bash -c 'touch \"{T}/new_tool.py\"'")]
    [InlineData("PowerShell", "New-Item -ItemType File \"{T}/new_tool.py\"")]
    [InlineData("PowerShell", "Set-Content -Path \"{T}/new_tool.py\" -Value 'x'")]
    [InlineData("PowerShell", "'x' | Out-File \"{T}/new_tool.py\"")]
    [InlineData("PowerShell", "Copy-Item C:/x/a.py -Destination \"{T}/new_tool.py\"")]
    [InlineData("PowerShell", "Copy-Item C:/x/new_tool.py -Destination \"{T}\"")]
    [InlineData("PowerShell", "Rename-Item \"{T}/existing.py\" \"{T}/renamed.py\"")]
    [InlineData("PowerShell", "cmd /c \"copy C:\\x\\a.py {T}/new_tool.py\"")]
    public void AShellCommandCreatingANewPyIsRefused(string tool, string command) =>
        Assert.NotNull(D(tool, command.Replace("{T}", Tools)));

    [Fact]
    public void ARelativeTargetIsReadAfterCd()
    {
        Assert.NotNull(D("Bash", $"cd \"{_repo.Replace('\\', '/')}\" && cat > tools/new_tool.py <<'EOF'\nx\nEOF"));
        Assert.NotNull(D("Bash", "cp ../a.py tools/new_tool.py", _repo));
        Assert.Null(D("Bash", "cp ../a.py tools/new_tool.py", _root));
    }

    [Fact]
    public void AScriptTheCommandRunsIsRead()
    {
        var script = Path.Combine(_root, "go.sh");
        File.WriteAllText(script, $"touch \"{Tools}/new_tool.py\"\n");
        Assert.NotNull(D("Bash", "bash go.sh"));
    }

    [Theory]
    [InlineData("Bash", "python \"{T}/existing.py\"")]                                   // running a job's tool
    [InlineData("Bash", "python -I \"{T}/existing.py\" --out C:/x/out.pdf > C:/x/log.txt 2>&1")]
    [InlineData("Bash", "cat \"{T}/new_tool.py\"")]                                      // a read
    [InlineData("Bash", "ls \"{T}\" | grep new_tool.py")]
    [InlineData("Bash", "echo \"cat > {T}/new_tool.py\"")]                               // a mention
    [InlineData("Bash", "echo '> {T}/new_tool.py'")]
    [InlineData("Bash", "git commit -m \"cp a.py {T}/new_tool.py\"")]
    [InlineData("Bash", "git commit -F - <<'EOF'\ncp a.py {T}/new_tool.py\ncat > {T}/new_tool.py\nEOF")]
    [InlineData("Bash", "cp \"{T}/existing.py\" /tmp/copy.py")]                          // out of tools/
    [InlineData("Bash", "cp /tmp/existing.py \"{T}/\"")]                                 // replaces an existing file
    [InlineData("Bash", "cp /tmp/a.py \"{T}/existing.py\"")]
    [InlineData("Bash", "cp \"{T}/existing.py\" \"{T}/../backup.py\"")]
    [InlineData("Bash", "cp -r \"{T}\" /tmp/backup")]
    [InlineData("Bash", "echo x > /tmp/new_tool.py")]
    [InlineData("Bash", "git add \"{T}/existing.py\"")]
    [InlineData("PowerShell", "Get-Content \"{T}/new_tool.py\"")]
    [InlineData("PowerShell", "Test-Path \"{T}/new_tool.py\"")]
    [InlineData("PowerShell", "Set-Content -Path C:/x/notes.txt -Value '{T}/new_tool.py'")]
    [InlineData("PowerShell", "Write-Output '> {T}/new_tool.py'")]
    public void ACommandThatCreatesNoNewToolPasses(string tool, string command) =>
        Assert.Null(D(tool, command.Replace("{T}", Tools)));

    [Fact]
    public void TheLiftIsIansLatestMessageOnly()
    {
        var dir = Path.Combine(_root, "asked");
        Directory.CreateDirectory(dir);
        var line = (string p) => JsonSerializer.Serialize(new { prompt = p });
        Assert.False(DrafterPythonGuard.Approved("s1", dir));   // no record: no words
        File.WriteAllLines(Path.Combine(dir, "s1.jsonl"), new[] { line("IAN-APPROVED-PYTHON go"), line("carry on") });
        Assert.False(DrafterPythonGuard.Approved("s1", dir));   // an older message does not hold
        File.WriteAllLines(Path.Combine(dir, "s1.jsonl"), new[] { line("carry on"), line("yes IAN-APPROVED-PYTHON for this one") });
        Assert.True(DrafterPythonGuard.Approved("s1", dir));
        File.WriteAllLines(Path.Combine(dir, "s2.jsonl"), new[] { line("ian-approved-python") });
        Assert.False(DrafterPythonGuard.Approved("s2", dir));   // the exact token, as the API guard reads it
    }

    private string HookJson(string tool, object input, string session) =>
        JsonSerializer.Serialize(new { session_id = session, cwd = _root, hook_event_name = "PreToolUse", tool_name = tool, tool_input = input });

    private string Enforce(string stdin, string asked, BrainDb db, BrainDb board, bool hook = true)
    {
        var o = new StringWriter();
        var args = new List<string> { "enforce", "--module", "zz-test-py", "--prompts-dir", asked };
        if (hook) args.Add("--hook");
        Assert.Equal(0, Program.Run(args.ToArray(), () => stdin, o, new StringWriter(), Path.Combine(_root, "hook.log"), db, board));
        return o.ToString();
    }

    [Fact]
    public void TheHookDeniesNamesTheRoutesLogsOneRowAndLifts()
    {
        var asked = Path.Combine(_root, "asked");
        Directory.CreateDirectory(asked);
        using var db = BrainDb.OpenRolledBack();
        using var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        var session = $"zz-py-{Guid.NewGuid():N}";
        int Rows()
        {
            using var c = board.Command("SELECT COUNT(*) FROM oversight.GuardRefusal WHERE Guard = @g AND [Session] = @s");
            c.Parameters.AddWithValue("@g", DrafterPythonGuard.GuardName);
            c.Parameters.AddWithValue("@s", session);
            return Convert.ToInt32(c.ExecuteScalar());
        }

        var denied = Enforce(HookJson("Write", new { file_path = Tools + "/new_tool.py", content = "x" }, session), asked, db, board);
        using (var j = JsonDocument.Parse(denied))
        {
            var h = j.RootElement.GetProperty("hookSpecificOutput");
            Assert.Equal("deny", h.GetProperty("permissionDecision").GetString());
            var why = h.GetProperty("permissionDecisionReason").GetString()!;
            Assert.Contains("C#", why);
            Assert.Contains("IAN-APPROVED-PYTHON", why);
            Assert.Contains("new_tool.py", why);
        }
        Assert.Equal(1, Rows());

        // the Edit of an existing tool, and a new .py elsewhere, are not this guard's: no refusal from it, no row
        var ok = Enforce(HookJson("Edit", new { file_path = Tools + "/existing.py", old_string = "a", new_string = "b" }, session), asked, db, board);
        Assert.DoesNotContain(DrafterPythonGuard.Approval, ok);
        Assert.Equal(1, Rows());

        File.WriteAllLines(Path.Combine(asked, session + ".jsonl"), new[] { JsonSerializer.Serialize(new { prompt = "IAN-APPROVED-PYTHON" }) });
        var lifted = Enforce(HookJson("Write", new { file_path = Tools + "/new_tool.py", content = "x" }, session), asked, db, board);
        Assert.DoesNotContain(DrafterPythonGuard.Approval, lifted);
        Assert.Equal(1, Rows());
    }

    [Fact]
    public void AGuardFaultAllows()
    {
        // an illegal path, a repo whose tools/ cannot be seen, an unreadable prompt record: nothing is refused for a fault
        Assert.Null(D("Write", "C:/x/KOR.Drafter/tools/\0bad.py"));
        Assert.Null(D("Write", "Z:/not/mounted/KOR.Drafter/tools/new_tool.py"));
        Assert.Null(D("Bash", "cat > \"unclosed"));
        var asked = Path.Combine(_root, "asked");
        Directory.CreateDirectory(Path.Combine(asked, "dir.jsonl"));   // a folder where the file should be: the read throws
        Assert.True(DrafterPythonGuard.Approved("dir", asked));         // fails open: a read error never refuses
    }
}
