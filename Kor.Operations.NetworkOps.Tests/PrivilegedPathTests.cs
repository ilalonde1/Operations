#nullable enable
using System.Text.RegularExpressions;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// THE CLASS (named after its second instance, 2026-09-30, per CLAUDE.md rule 11):
//   CODE RUNNING AS SYSTEM OR AS AN ADMINISTRATOR MUST NEVER ACT ON A PATH THAT A NON-ADMINISTRATOR CAN CREATE OR
//   REPLACE -- AT ANY LEVEL OF THAT PATH.
// Instance 1 (Codex audit): the agent's key and scripts lived under C:\ProgramData, where any user can pre-create the
// folder and own it, then swap a script that SYSTEM runs. Instance 2 (Codex re-check): the installer's clean-up of
// that old folder trusted C:\ProgramData\KorOperations, which a user can make a junction to Program Files -- and the
// installer, as an administrator, would delete the new install through it.
//
// WHAT IT COVERS: the agent's source and the installer's source name no user-writable root -- ProgramData /
// CommonApplicationData, the TEMP folders, user profiles, Public, C:\Windows\Temp -- as a string or a special-folder
// lookup. Program Files and the Windows folder (admin-only) are what they may use.
// WHAT IT DOES NOT: a path assembled at run time from something the user controls (an environment variable such as
// %TEMP% read in the agent's own process is SYSTEM's, but a value read from a user-writable registry key would not
// be); the one-shot network route, which stages scripts in C:\Windows\Temp under random names created by an
// administrator and is outside this check's two files' scope; anything the PowerShell scripts themselves touch.
// A SAME-CLASS FAULT IT WOULD NOT CATCH: a junction planted under Program Files by an administrator-level user --
// by definition already able to do anything, so not this class's threat.
public sealed class PrivilegedPathTests
{
    private static readonly Regex UserWritable = new(
        @"ProgramData|CommonApplicationData|GetTempPath|LocalApplicationData|ApplicationData\b|UserProfile|%TEMP%|%TMP%|\\Users\\|\\Public\\|Windows\\Temp",
        RegexOptions.IgnoreCase);

    public static TheoryData<string> PrivilegedSources()
    {
        var root = RepoRoot();
        var files = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(root, "Kor.Operations.NetworkOps.Agent"), "*.cs")) files.Add(Path.GetRelativePath(root, f));
        files.Add(Path.Combine("Kor.Operations.NetworkOps.Transport", "RemoteAgentInstall.cs"));
        return files;
    }

    [Theory]
    [MemberData(nameof(PrivilegedSources))]
    public void Privileged_code_names_no_user_writable_location(string relative)
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), relative));
        // Comments may explain the rule (and name the places it keeps out); code may not use them.
        var offending = lines.Select((l, i) => (Line: i + 1, Code: l.Split("//")[0]))
            .Where(x => UserWritable.IsMatch(x.Code)).Select(x => $"{relative}:{x.Line}: {x.Code.Trim()}").ToList();
        Assert.True(offending.Count == 0, "privileged code touching a user-writable location:\n" + string.Join("\n", offending));
    }

    [Fact]
    public void The_scan_sees_the_files_it_is_meant_to()
    {
        var n = PrivilegedSources().Cast<object[]>().Count();
        Assert.True(n >= 8, $"only {n} files: the agent folder moved or the scan is broken");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.NetworkOps.Agent"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found above " + AppContext.BaseDirectory);
    }
}
