#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Core.Actions;
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Service.Sweep;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The fix catalog: the ONLY things the Command Center can run on a machine.
//
// WHAT THIS COVERS: every fix has its script; every family a fix is offered for is a real finding family (so a
// renamed rule cannot silently orphan its fix); inputs are validated and bound so they cannot break out of their
// string; the escape hatch is offered everywhere and last; a fix's verdict is read from its output.
// WHAT IT DOES NOT COVER: that a script actually fixes the fault on a real PC -- each is proven by running it
// (the first runs are recorded in NetworkOps.Actions), and fix learning measures it over time. A fault this would
// NOT catch: a script that "succeeds" without changing anything (its Result line would say so; nothing forces it to).
public sealed class FixCatalogTests
{
    [Fact]
    public void Every_fix_but_the_escape_hatch_has_an_embedded_script()
    {
        foreach (var f in FixCatalog.All.Where(f => f.Id != FixCatalog.RunCommand && f.Id != FixCatalog.UpdateAgent))
        {
            var script = FixCatalog.Script(f, f.ParamLabel is null ? null : "Spooler");
            Assert.False(string.IsNullOrWhiteSpace(script), $"{f.Id} has no script");
            Assert.Contains("Result", script);   // every fix states its own verdict
        }
    }

    [Fact]
    public void Every_family_a_fix_is_offered_for_is_a_real_finding_family()
    {
        var known = Knowledge.Families.ToHashSet();
        foreach (var f in FixCatalog.All)
            foreach (var fam in f.Families.Where(x => x != "*"))
                Assert.True(known.Contains(fam), $"{f.Id} is offered for '{fam}', which no rule raises (renamed?)");
    }

    [Fact]
    public void A_finding_gets_its_specific_fixes_first_and_the_escape_hatch_last()
    {
        var fixes = FixCatalog.For("low-disk:C");
        Assert.Equal("free-disk-space", fixes[0].Id);
        Assert.Equal(FixCatalog.RunCommand, fixes[^1].Id);
        Assert.Equal([FixCatalog.RunCommand], FixCatalog.For("mailbox-near-limit:x").Select(f => f.Id));   // nothing scriptable: just the hatch
    }

    [Fact]
    public void A_stopped_service_fix_takes_its_name_from_the_finding()
    {
        var f = FixCatalog.Get(FixCatalog.StartService)!;
        Assert.Equal("Certify.Service", FixCatalog.ParamFromFinding(f, "server.service-stopped:Certify.Service"));
    }

    [Theory]
    [InlineData("Certify.Service", true)]
    [InlineData("Kor.Operations.Mcp", true)]
    [InlineData("x'; Remove-Item C:\\ -Recurse; '", false)]
    [InlineData("a b", false)]
    [InlineData("", false)]
    public void A_service_name_is_a_bare_identifier_or_refused(string name, bool ok)
        => Assert.Equal(ok, FixCatalog.Invalid(FixCatalog.Get(FixCatalog.StartService)!, name) is null);

    [Fact]
    public void An_input_is_bound_as_one_single_quoted_string()
    {
        var script = FixCatalog.Script(FixCatalog.Get(FixCatalog.StartService)!, "It's.Svc");
        Assert.StartsWith("$Param = 'It''s.Svc'\n", script);   // a quote is doubled: it cannot end the string
    }

    [Fact]
    public void A_fix_that_takes_no_input_refuses_one_and_the_hatch_needs_a_script()
    {
        Assert.NotNull(FixCatalog.Invalid(FixCatalog.Get("restart-pc")!, "anything"));
        Assert.NotNull(FixCatalog.Invalid(FixCatalog.Get(FixCatalog.RunCommand)!, "  "));
        Assert.Null(FixCatalog.Invalid(FixCatalog.Get(FixCatalog.RunCommand)!, "Get-Date"));
    }

    [Fact]
    public void Update_the_agent_is_offered_for_the_agent_findings_and_routes_to_the_installer()
    {
        // The id the app queues (FixCatalog) must be the kind ActionRunner routes to the installer (Service).
        Assert.Equal(Kor.Operations.NetworkOps.Service.Agents.AgentInstaller.InstallKind, FixCatalog.UpdateAgent);
        Assert.True(Kor.Operations.NetworkOps.Service.Agents.AgentInstaller.IsAgentKind(FixCatalog.UpdateAgent));
        Assert.Contains(FixCatalog.UpdateAgent, FixCatalog.For("agent-outdated").Select(f => f.Id));
        Assert.Contains(FixCatalog.UpdateAgent, FixCatalog.For("agent-silent").Select(f => f.Id));
        Assert.DoesNotContain(FixCatalog.UpdateAgent, FixCatalog.For("low-disk:C").Select(f => f.Id));
        Assert.False(FixCatalog.Get(FixCatalog.UpdateAgent)!.Disruptive);
    }

    [Fact]
    public void Only_the_restarts_are_disruptive()
        => Assert.Equal(["restart-pc", FixCatalog.UpdateBios, FixCatalog.InstallUpdatesRestart], FixCatalog.All.Where(f => f.Disruptive).Select(f => f.Id));

    [Theory]
    [InlineData("[{\"Result\":\"Freed 3.2 GB on C:\",\"Steps\":[]}]", "Freed 3.2 GB on C:")]
    [InlineData("[\"hello\"]", "hello")]
    [InlineData("[]", "ran; no output")]
    public void The_verdict_is_read_from_the_output(string json, string expected)
        => Assert.Equal(expected, ActionRunner.ResultLine(json));

    [Fact]
    public void The_fix_scripts_return_plain_values()
    {
        // The probe-writing rule: no Get-Item / Get-Content results in the output (their PS graphs serialise to MBs).
        foreach (var f in FixCatalog.All.Where(f => f.Id != FixCatalog.RunCommand && f.Id != FixCatalog.UpdateAgent))
        {
            var script = FixCatalog.Script(f, f.ParamLabel is null ? null : "Spooler");
            // A bare call on its own line lands in the output; one piped onward (| ForEach-Object ...) does not.
            Assert.DoesNotMatch(new Regex(@"^\s*(Get-Item|Get-Content|Get-ChildItem)\b[^|\r\n]*$", RegexOptions.Multiline), script);
        }
    }
}
