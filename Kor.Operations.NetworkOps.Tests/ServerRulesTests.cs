#nullable enable
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Rack;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// CLASS 2 (server side): "up is not the same as configured right." NetworkOps checked a server was reachable but never
// that it ran exactly one antivirus, or that its NICs were in a trusted firewall profile. The fixtures mirror the real
// 2026-10-05 readings (av-state.ps1): the five servers each ran Windows Defender in Normal mode AND Webroot (WRSVC) at
// once -- two active engines -- and a new NIC came up Public.
//
// WHAT IT COVERS: two active AV (Defender Normal+realtime AND Webroot Running) -> server.av-conflict; zero active ->
// server.av-none (Critical); the fixed state (Defender removed, Webroot only) raises NOTHING; a Public NIC ->
// server.nic-public; an OLD probe (no AV/NIC fields) stays silent rather than reading "no AV".
// WHAT IT DOES NOT: a third-party AV that is neither Defender nor Webroot (reads as none), Defender exclusions, a firewall
// rule other than the Public category. A SAME-CLASS FAULT IT WOULD MISS: Defender + CrowdStrike -- the count sees one.
public sealed class ServerRulesTests
{
    private static RackResult Eval(string json) => ServerRules.Evaluate(json);
    private const string Base = "\"Computer\":\"KOR-DC01\",\"OsCaption\":\"Microsoft Windows Server 2025 Standard\",\"OsBuild\":\"10.0.26100\",\"UptimeHours\":100,\"Disks\":[],\"VssWriters\":[],\"StoppedAutoServices\":[],\"PendingReboot\":false,\"LastUpdateDays\":5,\"StorageErrors24h\":[]";

    [Fact]
    public void Two_active_antiviruses_conflict()
    {
        var r = Eval("{" + Base + ",\"DefenderInstalled\":true,\"DefenderMode\":\"Normal\",\"DefenderRealtime\":true,\"WebrootStatus\":\"Running\",\"NicCategories\":[\"DomainAuthenticated\"]}");
        Assert.Single(r.Findings, f => f.RuleKey == "server.av-conflict" && f.Severity == Severity.Warning);
        Assert.DoesNotContain(r.Findings, f => f.RuleKey == "server.av-none");
        Assert.DoesNotContain(r.Findings, f => f.RuleKey == "server.nic-public");
        Assert.Equal("Windows Defender, Webroot", r.Facts["av.active"]);
    }

    [Fact]
    public void No_active_antivirus_is_critical()
    {
        var r = Eval("{" + Base + ",\"DefenderInstalled\":false,\"DefenderMode\":\"\",\"DefenderRealtime\":false,\"WebrootStatus\":\"Stopped\",\"NicCategories\":[\"Private\"]}");
        Assert.Single(r.Findings, f => f.RuleKey == "server.av-none" && f.Severity == Severity.Critical);
    }

    [Fact]
    public void Defender_removed_leaving_only_webroot_is_clean()
    {
        // The 6-Oct fix: Defender removed from the servers, Webroot kept -> exactly one active engine, nothing raised.
        var r = Eval("{" + Base + ",\"DefenderInstalled\":false,\"DefenderMode\":\"\",\"DefenderRealtime\":false,\"WebrootStatus\":\"Running\",\"NicCategories\":[\"DomainAuthenticated\"]}");
        Assert.DoesNotContain(r.Findings, f => f.RuleKey is "server.av-conflict" or "server.av-none");
        Assert.Equal("Webroot", r.Facts["av.active"]);
    }

    [Fact]
    public void A_public_nic_is_flagged()
    {
        var r = Eval("{" + Base + ",\"DefenderInstalled\":false,\"WebrootStatus\":\"Running\",\"NicCategories\":[\"Public\",\"DomainAuthenticated\"]}");
        Assert.Single(r.Findings, f => f.RuleKey == "server.nic-public" && f.Severity == Severity.Warning);
    }

    [Fact]
    public void An_old_probe_without_the_AV_fields_stays_silent()
    {
        var r = Eval("{" + Base + "}");   // ProbeVersion 1: no DefenderMode/WebrootStatus/NicCategories
        Assert.DoesNotContain(r.Findings, f => f.RuleKey is "server.av-conflict" or "server.av-none" or "server.nic-public");
    }
}
