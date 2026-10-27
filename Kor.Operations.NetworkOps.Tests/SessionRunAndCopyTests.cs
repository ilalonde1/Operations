using Kor.Operations.NetworkOps.Core.Actions;
using Kor.Operations.NetworkOps.Core.Copy;
using Kor.Operations.NetworkOps.Service;
using Kor.Operations.NetworkOps.Service.Mcp;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// A session's run and copy through MCP (networkops_run / networkops_copy / networkops_copy_status) and `netops copy`.
//
// WHAT THIS COVERS: the refusals a session meets before anything runs -- one named machine (never "all" or a list), a
// purpose, and the protected machines (Ian's PC, the licensed Revit box) only with who approved it; that copy paths
// are full local or UNC paths and cannot break out of their quoted string; that the copy never carries a deleting
// robocopy switch; that the copy id is the only thing copy-status binds; and that the MCP tools reach a machine only
// through SessionApi.RunAsync (the audited path `netops run` uses).
// WHAT IT DOES NOT: that robocopy really copies, that the machine's computer account may write to the share, or that
// the scheduled task outlives the agent's job -- only a live copy proves those (the first is recorded as an action).
// A SAME-CLASS FAULT IT WOULD NOT CATCH: a protected machine reached under another name (an IP address, or a rack
// name with its bracket) -- the check compares the name as given, and APP01's name lookup is what matches it.
public sealed class SessionRunAndCopyTests
{
    private static NetworkOpsOptions Options() => new();

    [Theory]
    [InlineData("all")]
    [InlineData("ALL")]
    [InlineData("KOR-206-N,KOR-207")]
    [InlineData("KOR-2*")]
    [InlineData("")]
    public void A_session_names_one_machine(string host)
        => Assert.NotNull(NetworkOpsMcpRun.Refusal(Options(), host, "reading the agent version for the census", null));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("because")]
    public void A_session_says_why(string? purpose)
        => Assert.Contains("purpose", NetworkOpsMcpRun.Refusal(Options(), "KOR-104N", purpose, null));

    [Theory]
    [InlineData("KOR-1001")]
    [InlineData("kor-302n")]
    public void A_protected_machine_needs_who_approved_it(string host)
    {
        Assert.Contains("protected", NetworkOpsMcpRun.Refusal(Options(), host, "switching Revit for the harvest", null));
        Assert.Null(NetworkOpsMcpRun.Refusal(Options(), host, "switching Revit for the harvest", "operations-ee 21:05 named action"));
    }

    [Fact]
    public void An_ordinary_machine_with_a_purpose_runs()
        => Assert.Null(NetworkOpsMcpRun.Refusal(Options(), "KOR-104N", "reading the agent version for the census", null));

    [Fact]
    public void The_approval_is_written_into_the_audit_line()
    {
        Assert.Equal("why it ran [approved by: Ian 22:45]", ProtectedHosts.AuditPurpose(" why it ran ", "Ian 22:45"));
        Assert.Equal("why it ran", ProtectedHosts.AuditPurpose("why it ran", null));
    }

    // ---- the protected machines on EVERY road (2026-10-27: until then only the MCP tools checked; `netops run` through
    // APP01 and `netops run --direct` both stepped around it). One rule in Core; each road must call it before it runs.

    [Theory]
    [InlineData("KOR-302N")]
    [InlineData("kor-1001")]
    [InlineData("KOR-302N (Revit bridge)")]
    public void A_protected_machine_is_refused_without_an_approval_and_runs_with_one(string name)
    {
        Assert.NotNull(ProtectedHosts.Refusal(ProtectedHosts.Default, name, null));
        Assert.NotNull(ProtectedHosts.Refusal(ProtectedHosts.Default, name, "  "));
        Assert.Null(ProtectedHosts.Refusal(ProtectedHosts.Default, name, "operations-ee 04:30, Ian's proxy"));
    }

    [Theory]
    [InlineData("KOR-104N")]
    [InlineData("KOR-FS01 (file server)")]
    [InlineData("KOR-302")]   // a different machine, not a prefix match of KOR-302N
    public void Other_machines_need_no_approval(string name)
        => Assert.Null(ProtectedHosts.Refusal(ProtectedHosts.Default, name, null));

    [Fact]
    public void The_service_and_the_mcp_tools_use_the_one_list()
        => Assert.Equal(ProtectedHosts.Default, new NetworkOpsOptions().SessionProtectedHosts);

    private static string Src(params string[] parts)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "Kor.Operations.NetworkOps.Service"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine([root!.FullName, .. parts]));
    }

    [Fact]
    public void The_APP01_road_refuses_before_it_starts_anything()
    {
        // SessionApi.RunAsync is every APP01 run: netops run, netops copy, and the MCP tools.
        var src = Src("Kor.Operations.NetworkOps.Service", "Api", "SessionApi.cs");
        var check = src.IndexOf("ProtectedHosts.Refusal(o.SessionProtectedHosts, dev.Name, approvedBy)", StringComparison.Ordinal);
        Assert.True(check > 0 && check < src.IndexOf("StartActionAsync(", StringComparison.Ordinal), "the refusal comes before the action starts");
        Assert.Contains("body.ApprovedBy", src);
        // The MCP tools hand the approval to it rather than deciding alone: the three tools, and their one call into it.
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(Src("Kor.Operations.NetworkOps.Service", "Mcp", "NetworkOpsMcpRun.cs"), @"approvedBy, ct\);").Count);
    }

    [Fact]
    public void The_direct_road_refuses_before_it_runs_and_logs_every_run()
    {
        var src = Src("Kor.Operations.NetworkOps.Cli", "Program.cs");
        var check = src.IndexOf("ProtectedHosts.Refusal(Kor.Operations.NetworkOps.Core.Actions.ProtectedHosts.Default, h, approvedBy)", StringComparison.Ordinal);
        var run = src.IndexOf("case \"run\":", StringComparison.Ordinal);
        Assert.True(check > 0 && check < run, "the refusal comes before the run");
        Assert.Contains("DirectRunLog.Write(", src);
        // The CLI's APP01 verbs send the approval.
        var verbs = Src("Kor.Operations.NetworkOps.Cli", "SessionVerbs.cs");
        Assert.Contains("new RemoteRunRequest(body, timeout, purpose, promptRun, approvedBy)", verbs);
        Assert.Contains("new RemoteRunRequest(script, timeout, audit, null, approvedBy)", verbs);
    }

    [Theory]
    [InlineData(null, @"C:\b")]
    [InlineData(@"relative\folder", @"C:\b")]
    [InlineData(@"C:\a""; Remove-Item C:\ -Recurse; """, @"C:\b")]
    [InlineData(@"C:\a", "D:\\b\r\nRemove-Item x")]
    [InlineData(@"C:\a\..\..\Windows", @"C:\b")]
    [InlineData(@"C:\a\*", @"C:\b")]
    [InlineData(@"\\KOR-FS01", @"C:\b")]
    [InlineData(@"C:\a", @"C:\a\")]
    public void A_copy_path_is_a_full_path_and_nothing_more(string? source, string destination)
        => Assert.NotNull(CopyScript.Invalid(source, destination, null));

    [Theory]
    [InlineData(@"C:\KOR.Drafter\tasks\out", @"\\KOR-FS01\E$\IT\copies\out", null)]
    [InlineData(@"\\Kor-fs01\Drafting\KOR-Standards\template\MASTER", @"C:\Temp\master", "*.rvt")]
    [InlineData(@"C:\Users\Nelson's files", @"\\KOR-FS01\Drafting\Nelson's", null)]
    public void Ordinary_copies_are_accepted(string source, string destination, string? files)
        => Assert.Null(CopyScript.Invalid(source, destination, files));

    [Theory]
    [InlineData(@"..\x")]
    [InlineData(@"sub\*.rvt")]
    [InlineData("a\"b")]
    public void Files_is_one_name_or_wildcard(string files)
        => Assert.NotNull(CopyScript.Invalid(@"C:\a", @"C:\b", files));

    [Theory]
    [InlineData(@"C:\a\", @"C:\a")]
    [InlineData(@"C:\", @"C:\.")]
    [InlineData(@"\\KOR-FS01\Drafting\", @"\\KOR-FS01\Drafting")]
    public void A_trailing_backslash_never_reaches_robocopy(string given, string expected)
        => Assert.Equal(expected, CopyScript.Norm(given));

    [Fact]
    public void Values_are_bound_as_quoted_strings_an_apostrophe_cannot_break()
    {
        var s = CopyScript.Start("20261027-061500-a1b2c3", @"C:\Users\Nelson's files", @"\\KOR-FS01\IT\x';Remove-Item C:\;'", null, false, 30);
        Assert.Contains(@"$Source = 'C:\Users\Nelson''s files'", s);
        Assert.Contains(@"$Destination = '\\KOR-FS01\IT\x'';Remove-Item C:\;'''", s);
        Assert.Contains("$Overwrite = $false", s);
        Assert.Contains("$WaitSeconds = 30", s);
    }

    [Fact]
    public void The_wait_is_capped_under_the_run_ceiling()
        => Assert.Contains($"$WaitSeconds = {CopyScript.MaxWaitSeconds}\n", CopyScript.Start(CopyScript.NewId(), @"C:\a", @"C:\b", null, false, 9999));

    [Fact]
    public void A_copy_never_deletes()
    {
        var s = CopyScript.Start(CopyScript.NewId(), @"C:\a", @"C:\b", null, true, 10);
        foreach (var bad in new[] { "/MIR", "/PURGE", "/MOV", "/MOVE", "Remove-Item" })
            Assert.DoesNotContain(bad, s, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Without_overwrite_files_already_there_are_kept()
        => Assert.Contains("'/XC','/XN','/XO'", CopyScript.Start(CopyScript.NewId(), @"C:\a", @"C:\b", null, false, 10));

    [Fact]
    public void Copy_status_binds_only_a_copy_id()
    {
        Assert.True(CopyScript.IsId(CopyScript.NewId()));
        Assert.False(CopyScript.IsId("x'; Remove-Item C:\\ -Recurse; '"));
        Assert.Throws<ArgumentException>(() => CopyScript.Status("../../evil"));
        Assert.StartsWith("$CopyId = '20261027-061500-a1b2c3'\n", CopyScript.Status("20261027-061500-a1b2c3"));
    }

    [Fact]
    public void The_mcp_run_tools_reach_a_machine_only_through_the_audited_session_run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "Kor.Operations.NetworkOps.Service"))) root = root.Parent;
        Assert.NotNull(root);
        var src = File.ReadAllText(Path.Combine(root!.FullName, "Kor.Operations.NetworkOps.Service", "Mcp", "NetworkOpsMcpRun.cs"));
        Assert.Contains("SessionApi.RunAsync(", src);
        Assert.DoesNotContain("MachineRunner>().RunAsync", src);
        Assert.DoesNotContain("StartActionAsync", src);
    }
}
