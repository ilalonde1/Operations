using System.Text.Json;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Enforce;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Cases first: a path exception must prove its scope, and unknown cwd must stay unknown. Covers real Git cleanup
/// states, output existence/boundaries, cmd switches versus paths, and missing cwd in both shells. Commands are judged,
/// never executed. Does not cover filesystem races/links or arbitrary program effects: an allowed test can rewrite source.
/// The sandbox is outside TEMP and bin/obj so those broad allowances cannot make these cases pass accidentally.
/// </summary>
public sealed class CoordinatorRoleRound3Tests : IClassFixture<CoordinatorRoleRound3Sandbox>
{
    private readonly CoordinatorRoleRound3Sandbox _s;
    public CoordinatorRoleRound3Tests(CoordinatorRoleRound3Sandbox sandbox) => _s = sandbox;

    private CoordinatorRoleGuard.Decision Decide(string tool, string command, string? cwd) =>
        CoordinatorRoleGuard.Evaluate(JsonSerializer.Serialize(new { tool_name = tool, tool_input = new { command },
            cwd, session_id = "round3" }), "coordinator", null);

    private string Expand(string text) => text.Replace("{R}", _s.Repo).Replace("{E}", _s.Empty)
        .Replace("{T}", Path.GetTempPath().Replace('\\', '/').TrimEnd('/'))
        .Replace("{DRIVE}", Path.GetPathRoot(_s.Repo)!.Replace('\\', '/'));

    [Theory]
    [InlineData("published", false)]
    [InlineData("relative-pointer", false)]
    [InlineData("unpushed", true)]
    [InlineData("dirty", true)]
    [InlineData("untracked", true)]
    [InlineData("pruned", false)]
    [InlineData("pruned-source", true)]
    [InlineData("pruned-ignored-data", true)]
    [InlineData("pruned-not-ignored", true)]
    [InlineData("clone", true)]
    [InlineData("empty", false)]
    [InlineData("ordinary", true)]
    [InlineData("missing", true)]
    [InlineData("wrong-owner", true)]
    [InlineData("bad-entry", true)]
    public void CleanupRequiresEvidence(string state, bool refuse)
    {
        var path = _s.CleanupPath(state);
        foreach (var (tool, command) in new[] {
            ("Bash", "rm -rf '" + path + "'"),
            ("Bash", "rm -rf " + CoordinatorRoleRound3Sandbox.Msys(path).Replace(" ", "\\ ")),
            ("PowerShell", "Remove-Item -LiteralPath '" + path + "' -Recurse -Force") })
        {
            var d = Decide(tool, command, _s.Repo);
            Assert.True(d.Refuse == refuse, $"{state} / {tool}: {d}");
        }
    }

    [Theory]
    [InlineData("Bash", "rm -rf '{R}'")]
    [InlineData("PowerShell", "Remove-Item -LiteralPath '{R}/src' -Recurse")]
    [InlineData("Bash", "mkdir '{E}'")]
    [InlineData("PowerShell", "Set-Content -LiteralPath '{E}/data.json' x")]
    public void CleanupDoesNotAuthorizeOtherWrites(string tool, string command) =>
        Assert.True(Decide(tool, Expand(command), _s.Repo).Refuse);

    [Theory]
    [InlineData("Bash", "dotnet build -o .; echo x > src/a.cs", true)]
    [InlineData("PowerShell", "dotnet build --output '{R}'; Set-Content src/a.cs x", true)]
    [InlineData("Bash", "dotnet test --results-directory={DRIVE}; echo x > src/a.cs", true)]
    [InlineData("PowerShell", "dotnet test --results-directory '{R}/src'; 'x' > src/a.cs", true)]
    [InlineData("Bash", "dotnet build -o src > src/log.txt", true)]
    [InlineData("Bash", "dotnet build -o 'existing output'; echo x > 'existing output/log.txt'", true)]
    [InlineData("PowerShell", "dotnet build --output 'existing output'; Set-Content 'existing output/log.txt' x", true)]
    [InlineData("Bash", "dotnet test --output=src/a.cs; echo x > src/a.cs/child", true)]
    [InlineData("Bash", "dotnet build -o 'fresh output'; echo x > 'fresh output/log.txt'", false)]
    [InlineData("PowerShell", "dotnet test --results-directory 'fresh output'; Set-Content 'fresh output/log.txt' x", false)]
    [InlineData("Bash", "dotnet test --output=fresh; echo x > fresh/log.txt", false)]
    [InlineData("Bash", "dotnet build -o fresh; echo x > fresh/../src/a.cs", true)]
    [InlineData("Bash", "echo x > fresh/log.txt; dotnet build -o fresh", true)]
    [InlineData("Bash", "dotnet build -o obj/coord; echo x > obj/coord/log.txt", false)]
    [InlineData("PowerShell", "dotnet build -o bin/check; Set-Content bin/check/log.txt x", false)]
    [InlineData("Bash", "dotnet test --results-directory TestResults; echo x > TestResults/log.txt", false)]
    [InlineData("PowerShell", "dotnet test --results-directory '{T}'; Set-Content '{T}/role-r3.log' x", false)]
    [InlineData("Bash", "dotnet build -o .; echo x > obj/coord/log.txt", false)]
    [InlineData("PowerShell", "dotnet test --results-directory '{R}'; Set-Content '{T}/role-r3.log' x", false)]
    [InlineData("Bash", "dotnet build -o src", false)]
    public void OutputAllowanceUsesHookTimeFilesystem(string tool, string command, bool refuse)
    {
        var d = Decide(tool, Expand(command), _s.Repo);
        Assert.True(d.Refuse == refuse, d.ToString());
    }

    [Theory]
    [InlineData("Bash", "dotnet build -o '{R}'; echo x > '{R}/src/a.cs'", true)]
    [InlineData("PowerShell", "dotnet test --output '{R}/fresh'; Set-Content '{R}/fresh/log' x", false)]
    [InlineData("Bash", "dotnet build -o '{R}/src'; echo x > '{R}/src/a.cs'", true)]
    [InlineData("Bash", "dotnet test --results-directory '{R}/TestResults'; echo x > '{R}/TestResults/log'", false)]
    public void OutputMsysPaths(string tool, string command, bool refuse)
    {
        var d = Decide(tool, command.Replace("{R}", CoordinatorRoleRound3Sandbox.Msys(_s.Repo)), _s.Repo);
        Assert.True(d.Refuse == refuse, d.ToString());
    }

    [Theory]
    [InlineData("del /F /Q obj/coord/x", false)]
    [InlineData("erase /A:H /F obj/coord/x", false)]
    [InlineData("copy src/a.cs obj/coord/x /V", false)]
    [InlineData("xcopy src obj/coord /D", false)]
    [InlineData("xcopy src obj/coord /D:11-24-2026", false)]
    [InlineData("move obj/coord/x bin/check/x /V", false)]
    [InlineData("rd /S /Q obj/coord", false)]
    [InlineData("rmdir /S /Q obj/coord", false)]
    [InlineData("robocopy src obj/coord /MIR /XD obj", false)]
    [InlineData("mkdir /D obj/coord", false)]
    [InlineData("md /D obj/coord", false)]
    [InlineData("del /F /Q src/a.cs", true)]
    [InlineData("xcopy obj/coord src /D", true)]
    [InlineData("robocopy obj src /MIR /XD obj", true)]
    [InlineData("del /F '{R}/src/a.cs'", true)]
    [InlineData("del /F /c/project/source.cs", true)]
    [InlineData("del /F C:/project/source.cs", true)]
    [InlineData("del /LONGSWITCH obj/coord/x", true)]
    [InlineData("rm /F", true)]
    [InlineData("rm /y", true)]
    public void CmdSwitchesAreScopedToCmdPrograms(string command, bool refuse)
    {
        foreach (var tool in new[] { "Bash", "PowerShell" })
        {
            var d = Decide(tool, Expand(command), _s.Repo);
            Assert.True(d.Refuse == refuse, $"{tool}: {d}");
        }
    }

    [Theory]
    [InlineData("Bash", "cd .; echo x > obj/coord/log", true)]
    [InlineData("PowerShell", "Set-Location '.'; Set-Content obj/coord/log x", true)]
    [InlineData("Bash", "pushd relative; cd child; echo x > bin/check/log", true)]
    [InlineData("PowerShell", "Push-Location 'relative'; Set-Location child; Set-Content TestResults/log x", true)]
    [InlineData("Bash", "cd '{R}'; cd src; echo x > a.cs", true)]
    [InlineData("Bash", "cd '{R}'; echo x > obj/coord/log", false)]
    [InlineData("PowerShell", "Set-Location '{R}'; Set-Content bin/check/log x", false)]
    [InlineData("Bash", "cd relative; echo x > '{R}/obj/coord/log'", false)]
    [InlineData("PowerShell", "Set-Location relative; Set-Content '{R}/TestResults/log' x", false)]
    [InlineData("Bash", "cd relative; dotnet test -o fresh; echo x > fresh/log", true)]
    public void MissingCwdDoesNotInheritHookDirectory(string tool, string command, bool refuse)
    {
        var d = Decide(tool, Expand(command), null);
        Assert.True(d.Refuse == refuse, d.ToString());
        if (refuse && !command.Contains("{R}", StringComparison.Ordinal))
        {
            Assert.StartsWith("unresolved", d.Rule);
            Assert.Contains("assign it in the same command or use a literal path", CoordinatorRoleGuard.Says(d));
        }
    }

    [Fact]
    public void SandboxIsNotAnAllowedOutputLocation()
    {
        Assert.False(CoordinatorRoleGuard.AllowedPath(_s.Repo + "/src/a.cs", "round3"));
        Assert.False(CoordinatorRoleGuard.AllowedPath(_s.Empty, "round3"));
    }
}

/// <summary>All mutations and removals stay in this uniquely owned sandbox. No remote network, push, or user hooks.</summary>
public sealed class CoordinatorRoleRound3Sandbox : IDisposable
{
    public string Root { get; }
    public string Repo => Root + "/Repo";
    public string Empty => Repo + "-empty";
    private readonly string _hooks;
    private bool _variants;

    public CoordinatorRoleRound3Sandbox()
    {
        var parent = new DirectoryInfo(AppContext.BaseDirectory);
        while (parent is not null && !File.Exists(Path.Combine(parent.FullName, ".git")) && !Directory.Exists(Path.Combine(parent.FullName, ".git")))
            parent = parent.Parent;
        if (parent is null) throw new InvalidOperationException("Test checkout root not found");
        Root = Path.Combine(parent.FullName, ".codex_role", "round3", "temp", Guid.NewGuid().ToString("N")).Replace('\\', '/');
        _hooks = Root + "/no-hooks";
        Directory.CreateDirectory(_hooks);
        Directory.CreateDirectory(Repo + "/src");
        Directory.CreateDirectory(Empty);
        G(Repo, "init", "-q", "-b", "main");
        G(Repo, "config", "core.hooksPath", _hooks);
        G(Repo, "config", "core.excludesFile", Root + "/no-global-ignore");
        File.WriteAllText(Repo + "/.gitignore", "obj/\nbin/\nTestResults/\nwork/\n!notignored/obj/\n");
        File.WriteAllText(Repo + "/src/a.cs", "original\n");
        G(Repo, "add", ".");
        G(Repo, "commit", "-q", "-m", "fixture seed");
        foreach (var dir in new[] { "existing output", "obj/coord", "bin/check", "TestResults" }) Directory.CreateDirectory(Repo + "/" + dir);
    }

    public static string Msys(string path) => "/" + char.ToLowerInvariant(path[0]) + path[2..];

    private string G(string dir, params string[] args) =>
        Git.Run(dir, TimeSpan.FromSeconds(20), new[] { "-c", "core.hooksPath=" + _hooks, "-c", "commit.gpgsign=false",
            "-c", "user.name=role-test", "-c", "user.email=role-test@example.invalid" }.Concat(args).ToArray())
        ?? throw new InvalidOperationException("Fixture git failed: " + string.Join(' ', args));

    public string CleanupPath(string state)
    {
        if (!_variants)
        {
            // Seed a local bare origin with the published HEAD, then fetch its remote-tracking ref. No push is needed.
            G(Root, "clone", "--bare", "-q", Repo, Root + "/origin.git");
            G(Repo, "remote", "add", "origin", Root + "/origin.git");
            G(Repo, "fetch", "-q", "origin");
            foreach (var name in new[] { "published", "relative-pointer", "unpushed", "dirty", "untracked", "pruned",
                "pruned-source", "pruned-ignored-data", "pruned-not-ignored", "bad-entry" })
                G(Repo, "worktree", "add", "-q", "--detach", Repo + "-" + name, "HEAD");
            G(Repo + "-unpushed", "commit", "-q", "--allow-empty", "-m", "not on origin");
            File.AppendAllText(Repo + "-dirty/src/a.cs", "changed\n");
            File.WriteAllText(Repo + "-untracked/new.cs", "new\n");
            // A local config hiding untracked files must not turn uncommitted data into a cleanup candidate.
            G(Repo, "config", "status.showUntrackedFiles", "no");
            foreach (var name in new[] { "pruned", "pruned-source", "pruned-ignored-data", "pruned-not-ignored" })
            {
                var path = Repo + "-" + name;
                DeleteOwned(Repo + "/.git/worktrees/" + Path.GetFileName(path));
                foreach (var entry in Directory.EnumerateFileSystemEntries(path))
                    if (Path.GetFileName(entry) != ".git") DeleteOwned(entry);
                var extra = name switch { "pruned-source" => "src/a.cs", "pruned-ignored-data" => "work/bundle.json",
                    "pruned-not-ignored" => "notignored/obj/keep.bin", _ => "project/obj/build.dll" };
                Directory.CreateDirectory(Path.GetDirectoryName(path + "/" + extra)!);
                File.WriteAllText(path + "/" + extra, "data");
            }
            File.SetAttributes(Repo + "-relative-pointer/.git", FileAttributes.Normal);
            File.WriteAllText(Repo + "-relative-pointer/.git", "gitdir: ../Repo/.git/worktrees/Repo-relative-pointer\n");
            File.WriteAllText(Repo + "/.git/worktrees/Repo-bad-entry/HEAD", "invalid\n");
            G(Root, "clone", "-q", Repo, Repo + "-clone");
            Directory.CreateDirectory(Repo + "-ordinary");
            File.WriteAllText(Repo + "-ordinary/keep.txt", "uncommitted data");
            Directory.CreateDirectory(Repo + "-wrong-owner");
            File.WriteAllText(Repo + "-wrong-owner/.git", "gitdir: ../Other/.git/worktrees/Repo-wrong-owner\n");
            _variants = true;
        }
        return Repo + "-" + state;
    }

    private void DeleteOwned(string path)
    {
        var full = Path.GetFullPath(path).Replace('\\', '/');
        if (!full.Equals(Root, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(Root + "/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing removal outside test sandbox: " + full);
        if (Directory.Exists(full))
        {
            foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(full, true);
        }
        else if (File.Exists(full)) { File.SetAttributes(full, FileAttributes.Normal); File.Delete(full); }
    }

    public void Dispose() => DeleteOwned(Root);
}
