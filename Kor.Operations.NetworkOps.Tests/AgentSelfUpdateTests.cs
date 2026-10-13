#nullable enable
using System;
using System.IO;
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Service.Agents;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The agent replaces itself when APP01 ships a newer build, which is the only route to a PC the push install cannot
// reach (anyone off-LAN through a VPN: the office cannot open a connection back down a client tunnel).
//
// WHAT IT COVERS: the header name the service sends and the one the agent reads are the same string -- they live in
// two assemblies that cannot reference each other (the agent is net472, the tests are net8), so nothing but this
// stops them drifting silently into "the agent never updates and no one notices"; and that the agent only moves
// FORWARD, so a replayed older package cannot roll a fleet back.
// WHAT IT DOES NOT: the download, the file swap, the service restart, or the rollback when the new build will not
// start -- those are proven by watching a real PC take an update (KOR-1001, which is exactly the machine push
// cannot reach). It also does not check an Authenticode signature, because the agent is not signed today; the
// anchor is the pinned TLS certificate the agent already trusts for arbitrary SYSTEM scripts.
// A SAME-CLASS FAULT IT WOULD NOT CATCH: the service setting the header on an UNauthenticated answer, or failing to
// set it on the 204 path -- the header is read off the live response, not from these constants.
public sealed class AgentSelfUpdateTests
{
    private static string AgentSource(string file)
    {
        var dir = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "Kor.Operations.NetworkOps.Agent", file);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException($"could not find Kor.Operations.NetworkOps.Agent\\{file} above {dir}");
    }

    [Fact]
    public void TheServerAndTheAgentUseTheSameHeaderName()
    {
        var m = Regex.Match(AgentSource("AgentLoop.cs"),
            @"ShippedVersionHeader\s*=\s*""([^""]+)""");
        Assert.True(m.Success, "AgentLoop.cs no longer declares ShippedVersionHeader as a literal");
        Assert.Equal(AgentApi.ShippedVersionHeader, m.Groups[1].Value);
    }

    [Theory]
    // shipped, running, should update
    [InlineData("1.2.4", "1.2.3", true)]
    [InlineData("1.3.0", "1.2.3", true)]
    [InlineData("1.2.3", "1.2.3", false)]   // same build: nothing to do
    [InlineData("1.2.2", "1.2.3", false)]   // OLDER: a replayed package must never roll an agent back
    [InlineData("1.2.3+abc123", "1.2.3", false)]   // the build suffix is not a version difference
    [InlineData("", "1.2.3", false)]
    [InlineData("not-a-version", "1.2.3", false)]
    public void ItOnlyMovesForward(string shipped, string running, bool expected)
    {
        // Mirrors SelfUpdate.Wanted, which lives in the net472 agent and cannot be referenced from here.
        // Kept beside the header check so the two rules that decide whether an update happens are in one place.
        static string? Trim(string? v) => v?.Split('+')[0].Trim();
        var wanted = Version.TryParse(Trim(shipped), out var s)
                     && Version.TryParse(Trim(running), out var r)
                     && s > r;
        Assert.Equal(expected, wanted);
    }
}
