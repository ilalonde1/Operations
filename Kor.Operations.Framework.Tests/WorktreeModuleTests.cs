using System.Text.Json;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Records;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Folder-derived module/lane identity must name the main checkout; source identity must keep the worktree.
/// Covers real git worktrees, capture/Stop asks, compact, start/end, explicit-record refusal (including deleted
/// names), and the nine refusal-log conversion sites. SQL is rolled back, with post-disposal checks.
/// Does not cover hook installation, historical-row migration, Python writers, or arbitrary explicit module overrides.
/// Same-class miss: an external writer can still insert a worktree lane directly into SQL.
/// </summary>
[Collection(LiveDb.Name)]
public sealed class WorktreeModuleTests : IClassFixture<WorktreeModuleRepos>
{
    private readonly WorktreeModuleRepos repos;
    public WorktreeModuleTests(WorktreeModuleRepos repos) => this.repos = repos;

    private static ClaudeCapture.HookInput Hook(string cwd, string? session = null) =>
        ClaudeCapture.Parse(JsonSerializer.Serialize(new { cwd, session_id = session ?? "zz-worktree-module",
            source = "startup", tool_name = "Bash", tool_input = new { command = "dotnet build sample.csproj" },
            tool_response = new { stdout = "Build succeeded.\n0 Warning(s)\n0 Error(s)" },
            compact_summary = "zz-worktree-module rolled-back resume", reason = "clear" }));

    private static T Project<T>(string? path, Func<T> action)
    {
        var old = Environment.GetEnvironmentVariable("CLAUDE_PROJECT_DIR");
        try { Environment.SetEnvironmentVariable("CLAUDE_PROJECT_DIR", path); return action(); }
        finally { Environment.SetEnvironmentVariable("CLAUDE_PROJECT_DIR", old); }
    }

    [Theory]
    [InlineData("KOR.Drafter", "KOR.Drafter")]
    [InlineData("KOR.Drafter-etabs", "KOR.Drafter")]
    [InlineData("KOR.Drafter-wt-selfcheck-core", "KOR.Drafter")]
    [InlineData("KOR.Drafter-comparison", "KOR.Drafter")]
    [InlineData("unrelated-name", "KOR.Drafter")]
    [InlineData("relative-gitfile", "KOR.Drafter")]
    [InlineData("own-name-with-dashes", "own-name-with-dashes")]
    [InlineData("KOR Inspections Bookings", "KOR Inspections Bookings")]
    [InlineData("KOR.RevitTools", "KOR.RevitTools")]
    public void CwdAndProjectFolderNameTheMainModule(string folder, string expected)
    {
        var path = repos.PathOf(folder);
        Assert.Equal(expected, Project(null, () => ClaudeCapture.ModuleOf(Hook(path), null)));
        Assert.Equal(expected, Project(path, () => ClaudeCapture.ModuleOf(Hook(repos.Main), null)));
        Assert.Equal("explicit", Project(path, () => ClaudeCapture.ModuleOf(Hook(path), "explicit")));
    }

    [Fact]
    public void NonRepoKeepsItsLeafAndNullKeepsUnknown()
    {
        // The OS temp root is outside this checkout; no files are created there.
        var path = Path.GetTempPath();
        Assert.Null(Git.TopLevel(path));
        Assert.Equal(Git.Leaf(path), Project(null, () => ClaudeCapture.ModuleOf(Hook(path), null)));
        Assert.Equal("unknown", Project(null, () => ClaudeCapture.ModuleOf(Hook(path) with { Cwd = null }, null)));
    }

    [Fact]
    public void WorktreeAndNestedCwdResolveWithoutAGitExecutable()
    {
        var wt = repos.PathOf("KOR.Drafter-etabs");
        var nested = Directory.CreateDirectory(Path.Combine(wt, "nested", "folder")).FullName;
        var old = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "");
            Assert.Equal(wt.Replace('\\', '/'), Git.TopLevelFast(nested));
            Assert.Equal("KOR.Drafter", Project(null, () => ClaudeCapture.ModuleOf(Hook(nested), null)));
        }
        finally { Environment.SetEnvironmentVariable("PATH", old); }
    }

    [Theory]
    // The first and third ask->record pairs were reread from the transcripts. The missing second is brief-supplied.
    [InlineData("KOR.Drafter-etabs", "c2ad5988-6082-4890-b0bb-2a69792f929d")]
    [InlineData("KOR.Drafter-wt-selfcheck-core", "dd6412f2")]
    [InlineData("KOR.Drafter-comparison", "cd2499ed-046a-4e0b-9392-a3e8c95f0137")]
    public void TranscriptCommitAsksNameMainModuleAndWorktreeSource(string folder, string session)
    {
        var wt = repos.PathOf(folder);
        var e = GitCapture.Build("post-commit", Array.Empty<string>(), wt, null, null, null, session);
        var stored = new StoredEvent(1, DateTimeOffset.UtcNow, e.Module, e.Kind, e.Repo, e.Commit, e.Ref,
            e.Outcome, e.Detail, e.Actor, e.Session);
        var ask = SessionCapture.Reason(new[] { stored }, session);
        using var db = BrainDb.OpenRolledBack();
        var uniqueSession = "zz-worktree-" + Guid.NewGuid().ToString("N");
        BrainStore.Insert(e with { Session = uniqueSession }, db);
        var decision = Project(wt, () => SessionCapture.Check(db, Hook(wt, uniqueSession), null, readTurn: _ => null));
        Assert.True(decision.Block);
        Assert.Contains("--module KOR.Drafter --kind", decision.Reason);
        Assert.Equal("KOR.Drafter", BrainStore.EventById(decision.AskEventId!.Value, db)!.Module);
        Assert.Contains($"--session {session} --module KOR.Drafter --kind", ask);
        Assert.Contains($"--dir <the {folder} checkout>", ask);
        Assert.Equal("KOR.Drafter", e.Module);
        Assert.Equal(folder, e.Repo);
        Assert.Equal(Git.Run(wt, "rev-parse", "HEAD")!.Trim(), e.Commit);
        Assert.Contains("worktree-only.txt", e.Detail);
    }

    [Theory]
    [InlineData("post-commit")]
    [InlineData("post-merge")]
    [InlineData("post-checkout")]
    [InlineData("pre-push")]
    public void EveryGitHookUsesMainModuleAndRetainsSource(string hook)
    {
        var wt = repos.PathOf("KOR.Drafter-etabs");
        var e = GitCapture.Build(hook, Array.Empty<string>(), wt, null, null, null);
        Assert.Equal("KOR.Drafter", e.Module);
        Assert.Equal("KOR.Drafter-etabs", e.Repo);
        Assert.Equal("KOR.Drafter", GitCapture.CommitAt(wt, "HEAD", null).Module);
    }

    [Fact]
    public void ToolStartEndAndUnknownToolSourceUseMainModule()
    {
        var wt = repos.PathOf("KOR.Drafter-etabs");
        Project(wt, () =>
        {
            var h = Hook(wt);
            var tool = ClaudeCapture.Tool(h, null)!;
            Assert.Equal("KOR.Drafter", tool.Module);
            Assert.Equal("KOR.Drafter-etabs", tool.Repo);
            Assert.Equal(Git.Run(wt, "rev-parse", "HEAD")!.Trim(), tool.Commit);
            Assert.Equal("KOR.Drafter", SessionCapture.Start(h, null).Module);
            Assert.Equal("KOR.Drafter-etabs", SessionCapture.Start(h, null).Repo);
            Assert.Equal("KOR.Drafter", ClaudeCapture.End(h, null).Module);
            var unknown = ClaudeCapture.Tool(h with { Command = "cd ~ && dotnet build" }, null)!;
            Assert.Equal("KOR.Drafter", unknown.Module);
            Assert.Null(unknown.Repo);
            return true;
        });
    }

    [Fact]
    public void CompactWritesMainLaneAndEventAndRollsBack()
    {
        var session = "zz-worktree-" + Guid.NewGuid().ToString("N");
        Project(repos.PathOf("KOR.Drafter-etabs"), () =>
        {
            using var db = BrainDb.OpenRolledBack();
            var result = ClaudeCapture.Compact(Hook(repos.Main, session), null, db);
            Assert.Equal("KOR.Drafter", BrainStore.EventById(result.EventId, db)!.Module);
            using var q = db.Command("SELECT Lane FROM brain.LaneState WHERE Id=@id");
            q.Parameters.AddWithValue("@id", result.LaneStateId!.Value);
            Assert.Equal("KOR.Drafter", q.ExecuteScalar());
            return true;
        });
        AssertNoSessionRows(session);
    }

    [Theory]
    [InlineData("KOR.Drafter-etabs")]
    [InlineData("KOR.Drafter-wt-selfcheck-core")]
    [InlineData("KOR.Drafter-plan")]
    [InlineData("KOR.Drafter-integration")]
    [InlineData("KOR.Drafter-quantities")]
    [InlineData("unrelated-name")]
    public void RecordRefusesWorktreeNamesIncludingRemovedFolders(string module)
    {
        using var db = BrainDb.OpenRolledBack();
        var r = Record(db, module);
        Assert.Equal(1, r.Code);
        Assert.Equal($"--module {module} is a worktree of KOR.Drafter: use --module \"KOR.Drafter\"", r.Error.Trim());
        Assert.Equal("", r.Output);
    }

    [Theory]
    [InlineData("KOR.Drafter")]
    [InlineData("KOR.RevitTools")]
    [InlineData("KOR Inspections Bookings")]
    [InlineData("own-name-with-dashes")]
    public void RecordAllowsMainOrIndependentModuleAndKeepsWorktreeCitation(string module)
    {
        var session = "zz-worktree-" + Guid.NewGuid().ToString("N");
        using (var db = BrainDb.OpenRolledBack())
        {
            var r = Record(db, module, session);
            Assert.True(r.Code == 0, r.Error);
            var row = RecordStore.LiveByName(db, module, session)!;
            Assert.Equal("KOR.Drafter-etabs", row.SourceRepo);
            Assert.Equal("worktree-only.txt", row.SourcePath);
            Assert.Equal(Git.Run(repos.PathOf(row.SourceRepo!), "rev-parse", "HEAD:worktree-only.txt")!.Trim(), row.SourceBlob);
            Assert.Equal(repos.PathOf(row.SourceRepo!).Replace('\\', '/'), RepoLocator.Find(row.SourceRepo!, repos.Main));
            var report = Freshness.CheckRepo(db, row.SourceRepo!, repos.Main);
            Assert.Equal(Verdict.Current, Assert.Single(report.Rows, x => x.Id == row.Id).Verdict);
        }
        AssertNoSessionRows(session);
    }

    private (int Code, string Output, string Error) Record(BrainDb db, string module, string? session = null)
    {
        session ??= "zz-worktree-" + Guid.NewGuid().ToString("N");
        var output = new StringWriter(); var error = new StringWriter();
        var code = Program.Run(new[] { "record", "--module", module, "--kind", "finding", "--name", session,
            "--session", session, "--says", "worktree module regression", "--source", "git:worktree-only.txt",
            "--dir", repos.PathOf("KOR.Drafter-etabs") }, () => null, output, error, Path.Combine(repos.Root, "errors.log"), db);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void RefusalLoggingNamesMainRepoAndRollsBack()
    {
        var session = "zz-worktree-" + Guid.NewGuid().ToString("N");
        using (var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board))
        {
            var raw = JsonSerializer.Serialize(new { session_id = session, cwd = repos.PathOf("KOR.Drafter-etabs"),
                tool_name = "Bash", tool_input = new { command = "sqlcmd -S h -Q \"UPDATE example SET flag=1\" | tail -1" } });
            var output = new StringWriter();
            Program.Run(new[] { "enforce", "--module", "KOR.Drafter", "--hook", "--cache-dir", repos.Root, "--prompts-dir", repos.Root }, () => raw,
                output, new StringWriter(), Path.Combine(repos.Root, "errors.log"), board: board);
            Assert.Contains("A SQL write read through a cut", output.ToString());
            using var q = board.Command("SELECT Repo FROM oversight.GuardRefusal WHERE Session=@s");
            q.Parameters.AddWithValue("@s", session);
            Assert.Equal("KOR.Drafter", q.ExecuteScalar());
        }
        using var after = BrainDb.OpenRead(KorStandardsConnection.Login.Board, true);
        using var count = after.Command("SELECT COUNT(*) FROM oversight.GuardRefusal WHERE Session=@s");
        count.Parameters.AddWithValue("@s", session);
        Assert.Equal(0, count.ExecuteScalar());
    }

    [Theory]
    [InlineData("Program.Enforce.cs", 3)]
    [InlineData("Program.Context.cs", 2)]
    [InlineData("Program.Start.cs", 1)]
    [InlineData("Program.Stall.cs", 1)]
    [InlineData("Program.Repeat.cs", 1)]
    [InlineData("Program.Whole.cs", 1)]
    public void EveryKnownGuardRepoWriterUsesTheSharedResolver(string file, int sites)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "Kor.Operations.Framework.Cli"))) root = root.Parent;
        Assert.NotNull(root);
        var lines = File.ReadAllLines(Path.Combine(root.FullName, "Kor.Operations.Framework.Cli", file));
        var writers = lines.Where(l => l.Contains("var repo =")).ToArray();
        Assert.Equal(sites, writers.Length);
        Assert.All(writers, l => Assert.Contains("Git.ModuleOf(", l));
    }

    private static void AssertNoSessionRows(string session)
    {
        using var db = BrainDb.OpenRead(KorStandardsConnection.Login.Brain, true);
        using var q = db.Command("SELECT (SELECT COUNT(*) FROM brain.Event WHERE Session=@s) + " +
            "(SELECT COUNT(*) FROM brain.LaneState WHERE Session=@s) + (SELECT COUNT(*) FROM brain.Memory WHERE Name=@s)");
        q.Parameters.AddWithValue("@s", session);
        Assert.Equal(0, q.ExecuteScalar());
    }
}

public sealed class WorktreeModuleRepos : IDisposable
{
    public string Root { get; } = Path.Combine(AppContext.BaseDirectory, "wt-" + Guid.NewGuid().ToString("N")[..12]);
    public string Main => PathOf("KOR.Drafter");
    public string PathOf(string name) => Path.Combine(Root, name);

    public WorktreeModuleRepos()
    {
        Directory.CreateDirectory(Root);
        foreach (var name in new[] { "KOR.Drafter", "own-name-with-dashes", "KOR.RevitTools", "KOR Inspections Bookings" })
        {
            var dir = Directory.CreateDirectory(PathOf(name)).FullName;
            Run(dir, "init", "-q");
            Run(dir, "config", "core.longpaths", "true");
            Run(dir, "config", "user.name", "worktree module test");
            Run(dir, "config", "user.email", "worktree-module@example.invalid");
            Run(dir, "config", "core.hooksPath", Directory.CreateDirectory(Path.Combine(dir, "empty-hooks")).FullName);
            File.WriteAllText(Path.Combine(dir, "sample.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Run(dir, "add", "sample.csproj");
            Run(dir, "-c", "commit.gpgsign=false", "commit", "-qm", "main checkout");
        }
        foreach (var name in new[] { "KOR.Drafter-etabs", "KOR.Drafter-wt-selfcheck-core", "KOR.Drafter-comparison", "unrelated-name", "relative-gitfile", "KOR.Drafter-plan" })
        {
            var wt = PathOf(name);
            Run(Main, "worktree", "add", "--detach", wt, "HEAD");
            File.WriteAllText(Path.Combine(wt, "worktree-only.txt"), name);
            Run(wt, "add", "worktree-only.txt");
            Run(wt, "-c", "commit.gpgsign=false", "commit", "-qm", "worktree-only source");
        }
        Run(Main, "worktree", "remove", "--force", PathOf("KOR.Drafter-plan"));
        var relative = PathOf("relative-gitfile");
        var gitdir = File.ReadAllText(Path.Combine(relative, ".git"))[8..].Trim();
        File.SetAttributes(Path.Combine(relative, ".git"), FileAttributes.Normal);
        File.WriteAllText(Path.Combine(relative, ".git"), "gitdir: " + Path.GetRelativePath(relative, gitdir).Replace('\\', '/') + "\n");
    }

    private static void Run(string dir, params string[] args) => Assert.NotNull(Git.Run(dir, args));

    public void Dispose()
    {
        // Root is the unique directory this fixture created under the test binary directory.
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(Root, true);
    }
}
