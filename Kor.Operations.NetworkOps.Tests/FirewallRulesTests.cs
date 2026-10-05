#nullable enable
using System.Globalization;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Rack;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The firewall (Netgate pfSense Plus fw01) over SNMP v2c, turned into interfaces, health and findings. The fixture below
// is the REAL walk taken off the live fw01 on 2026-10-05 (every value verbatim), so the parser is tested against what the
// box actually answers, not a guess at it.
//
// WHAT IT COVERS: the assigned interfaces named by ifAlias (WAN/WAN2/WAN3/LAN) and ONLY those (lo0/pflog0/enc0/ovpns1,
// which pfSense leaves unnamed, are dropped); up vs dormant vs down from ifOperStatus; the negotiated speed; throughput
// as a rate differenced from the previous read's stored octet facts; CPU averaged over hrProcessorLoad cores; memory from
// the UCD scalars; the PF state count and its limit; uptime and the recent-reboot line; a healthy box raising nothing; and
// the fault findings (pf not running, a link that was up going down, no WAN up, memory high, state table near its limit).
// WHAT IT DOES NOT: the SNMP transport itself (SnmpChannel, exercised only against the live device), the appsettings
// wiring, or the port-map panel (FirewallRead is built here but not yet drawn). A SAME-CLASS FAULT IT WOULD NOT CATCH: an
// interface whose ifAlias pfSense left blank but which matters, or a WAN negotiating BELOW its capable speed -- SNMP reports
// the speed now, not the maximum, so "1G on a 2.5G port" is shown, never flagged, until an expected speed is configured.
public sealed class FirewallRulesTests
{
    // The live fw01 walk, 2026-10-05: 4 physical NICs (igc0-3, Intel i226 2.5G) assigned WAN/WAN3/LAN/WAN2, plus system
    // interfaces; WAN2 is the live 1G internet path, WAN/WAN3 are dormant standby, LAN is up at 1G.
    internal static Dictionary<string, string> Walk() => new()
    {
        // ifDescr
        ["1.3.6.1.2.1.2.2.1.2.1"] = "igc0", ["1.3.6.1.2.1.2.2.1.2.2"] = "igc1", ["1.3.6.1.2.1.2.2.1.2.3"] = "igc2",
        ["1.3.6.1.2.1.2.2.1.2.4"] = "igc3", ["1.3.6.1.2.1.2.2.1.2.6"] = "lo0", ["1.3.6.1.2.1.2.2.1.2.9"] = "ovpns1",
        // ifAlias (pfSense role) -- 5..9 are empty strings on the box
        ["1.3.6.1.2.1.31.1.1.1.18.1"] = "WAN", ["1.3.6.1.2.1.31.1.1.1.18.2"] = "WAN3", ["1.3.6.1.2.1.31.1.1.1.18.3"] = "LAN",
        ["1.3.6.1.2.1.31.1.1.1.18.4"] = "WAN2", ["1.3.6.1.2.1.31.1.1.1.18.6"] = "", ["1.3.6.1.2.1.31.1.1.1.18.9"] = "",
        // ifOperStatus: 1 up, 2 down, 5 dormant
        ["1.3.6.1.2.1.2.2.1.8.1"] = "5", ["1.3.6.1.2.1.2.2.1.8.2"] = "5", ["1.3.6.1.2.1.2.2.1.8.3"] = "1",
        ["1.3.6.1.2.1.2.2.1.8.4"] = "1", ["1.3.6.1.2.1.2.2.1.8.6"] = "1", ["1.3.6.1.2.1.2.2.1.8.9"] = "1",
        // ifHighSpeed (Mb/s)
        ["1.3.6.1.2.1.31.1.1.1.15.1"] = "10", ["1.3.6.1.2.1.31.1.1.1.15.2"] = "10", ["1.3.6.1.2.1.31.1.1.1.15.3"] = "1000",
        ["1.3.6.1.2.1.31.1.1.1.15.4"] = "1000", ["1.3.6.1.2.1.31.1.1.1.15.6"] = "0", ["1.3.6.1.2.1.31.1.1.1.15.9"] = "0",
        // ifHCInOctets / ifHCOutOctets (Counter64)
        ["1.3.6.1.2.1.31.1.1.1.6.1"] = "489972", ["1.3.6.1.2.1.31.1.1.1.6.3"] = "1864000038977", ["1.3.6.1.2.1.31.1.1.1.6.4"] = "588892362637",
        ["1.3.6.1.2.1.31.1.1.1.10.3"] = "567307865581", ["1.3.6.1.2.1.31.1.1.1.10.4"] = "1863985703882",
        // ifInErrors (all zero on the box)
        ["1.3.6.1.2.1.2.2.1.14.3"] = "0", ["1.3.6.1.2.1.2.2.1.14.4"] = "0",
        // hrProcessorLoad: 4 cores, 1/1/0/1
        ["1.3.6.1.2.1.25.3.3.1.2.99"] = "1", ["1.3.6.1.2.1.25.3.3.1.2.104"] = "1", ["1.3.6.1.2.1.25.3.3.1.2.109"] = "0", ["1.3.6.1.2.1.25.3.3.1.2.114"] = "1",
        // scalars
        ["1.3.6.1.2.1.1.1.0"] = "Netgate pfSense Plus fw01.korstructural.com 25.07.1-RELEASE FreeBSD 15.0-CURRENT amd64",
        ["1.3.6.1.2.1.1.5.0"] = "fw01.korstructural.com",
        ["1.3.6.1.2.1.1.3.0"] = "49190765",                 // sysUpTime, hundredths: ~5.7 days
        ["1.3.6.1.4.1.2021.4.5.0"] = "4065420", ["1.3.6.1.4.1.2021.4.6.0"] = "2637224",   // memTotalReal / memAvailReal, KB
        ["1.3.6.1.4.1.12325.1.200.1.1.1.0"] = "1",          // pf running
        ["1.3.6.1.4.1.12325.1.200.1.3.1.0"] = "3969",       // state count
        ["1.3.6.1.4.1.12325.1.200.1.5.1.0"] = "397000",     // state limit
    };

    private static readonly DateTime Now = new(2026, 10, 5, 23, 30, 0, DateTimeKind.Utc);
    private static readonly Dictionary<string, string> NoPrev = new();

    [Fact]
    public void Only_the_assigned_interfaces_are_read_named_by_their_pfSense_role()
    {
        var (ifaces, _) = FirewallRules.ReadInterfaces(Walk(), NoPrev, Now);
        Assert.Equal(["WAN2", "LAN", "WAN", "WAN3"], ifaces.Select(i => i.Role));   // active WAN then LAN, dormant last
        Assert.DoesNotContain(ifaces, i => i.Nic is "lo0" or "ovpns1");             // unnamed system interfaces dropped

        var wan2 = ifaces.Single(i => i.Role == "WAN2");
        Assert.Equal("igc3", wan2.Nic);
        Assert.Equal("up", wan2.State);
        Assert.Equal(1000, wan2.SpeedMbps);
        Assert.True(wan2.IsWan && wan2.Up);

        Assert.Equal("dormant", ifaces.Single(i => i.Role == "WAN").State);          // standby WAN, no carrier
        Assert.Equal("up", ifaces.Single(i => i.Role == "LAN").State);
    }

    [Fact]
    public void A_healthy_firewall_raises_nothing_and_summarises_what_matters()
    {
        var read = FirewallRules.Evaluate(Walk(), NoPrev, Now);
        Assert.True(read.Reachable);
        Assert.Empty(read.Findings);                                                  // nothing wrong on the live box
        Assert.Equal("pfSense Plus 25.07.1-RELEASE", read.Facts["fw.model"]);
        Assert.Equal("fw01.korstructural.com", read.Facts["fw.hostname"]);
        Assert.Equal(1, read.Metrics.Single(m => m.Metric == "fw.cpu.pct").Value);    // avg(1,1,0,1)
        Assert.Equal(35, read.Metrics.Single(m => m.Metric == "fw.mem.pct").Value);   // (4065420-2637224)/4065420
        Assert.Equal(3969, read.Metrics.Single(m => m.Metric == "fw.states").Value);
        Assert.Equal(1000, read.Metrics.Single(m => m.Metric == "fw.if.speed.mbps" && m.Subject == "WAN2").Value);
        Assert.Contains("WAN2 1000Mb", read.Summary);
        Assert.Contains("up 5.7d", read.Summary);
    }

    [Fact]
    public void Throughput_is_a_rate_between_two_reads()
    {
        // 60 seconds ago WAN2 had taken in 75,000,000 fewer bytes -> 75e6 * 8 / 60 / 1e6 = 10.0 Mb/s down.
        var prev = new Dictionary<string, string>
        {
            ["fw.read.ticks"] = Now.AddSeconds(-60).Ticks.ToString(CultureInfo.InvariantCulture),
            ["fw.octin:WAN2"] = (588892362637 - 75_000_000).ToString(CultureInfo.InvariantCulture),
            ["fw.octout:WAN2"] = "1863985703882",
        };
        var (ifaces, _) = FirewallRules.ReadInterfaces(Walk(), prev, Now);
        Assert.Equal(10.0, ifaces.Single(i => i.Role == "WAN2").InMbps);
        Assert.Equal(0.0, ifaces.Single(i => i.Role == "WAN2").OutMbps);              // no change -> zero, not null
        Assert.Null(ifaces.Single(i => i.Role == "LAN").InMbps);                      // no prior octets for LAN -> no rate yet

        // The read stores the current octets + time so the NEXT sweep can difference them.
        var read = FirewallRules.Evaluate(Walk(), NoPrev, Now);
        Assert.Equal("588892362637", read.Facts["fw.octin:WAN2"]);
        Assert.Equal(Now.Ticks.ToString(CultureInfo.InvariantCulture), read.Facts["fw.read.ticks"]);
    }

    [Fact]
    public void A_counter_reset_after_a_reboot_shows_no_rate_rather_than_a_negative_spike()
    {
        var prev = new Dictionary<string, string>
        {
            ["fw.read.ticks"] = Now.AddSeconds(-60).Ticks.ToString(CultureInfo.InvariantCulture),
            ["fw.octin:WAN2"] = "999999999999999",   // higher than now: the counter reset
        };
        var (ifaces, _) = FirewallRules.ReadInterfaces(Walk(), prev, Now);
        Assert.Null(ifaces.Single(i => i.Role == "WAN2").InMbps);
    }

    [Fact]
    public void Pf_not_running_is_critical()
    {
        var v = Walk();
        v["1.3.6.1.4.1.12325.1.200.1.1.1.0"] = "0";
        var f = FirewallRules.Evaluate(v, NoPrev, Now).Findings.Single(x => x.RuleKey == "fw.pf-down");
        Assert.Equal(Severity.Critical, f.Severity);
    }

    [Fact]
    public void A_link_that_was_up_and_is_now_down_raises_a_WAN_warning_but_a_LAN_critical()
    {
        var v = Walk();
        v["1.3.6.1.2.1.2.2.1.8.4"] = "2";   // WAN2 down
        v["1.3.6.1.2.1.2.2.1.8.3"] = "2";   // LAN down
        var findings = FirewallRules.Evaluate(v, new Dictionary<string, string> { ["fw.up"] = "WAN2,LAN" }, Now).Findings;
        Assert.Equal(Severity.Warning, findings.Single(f => f.RuleKey == "fw.link-down:WAN2").Severity);
        Assert.Equal(Severity.Critical, findings.Single(f => f.RuleKey == "fw.link-down:LAN").Severity);
        Assert.Contains(findings, f => f.RuleKey == "fw.no-wan" && f.Severity == Severity.Critical);   // no WAN up at all
    }

    [Fact]
    public void A_state_table_near_its_limit_warns()
    {
        var v = Walk();
        v["1.3.6.1.4.1.12325.1.200.1.3.1.0"] = (397000 * 0.85).ToString("0", CultureInfo.InvariantCulture);
        Assert.Contains(FirewallRules.Evaluate(v, NoPrev, Now).Findings, f => f.RuleKey == "fw.states" && f.Severity == Severity.Warning);
    }

    [Fact]
    public void The_panel_read_carries_the_interfaces_and_health()
    {
        var read = FirewallRules.Read("Firewall (Netgate pfSense)", "192.168.1.1", Walk(), NoPrev, Now);
        Assert.Equal("192.168.1.1", read.Ip);
        Assert.Equal("pfSense Plus 25.07.1-RELEASE", read.Model);
        Assert.Equal(4, read.Interfaces.Count);
        Assert.Equal(1, read.CpuPct);
        Assert.Equal(35, read.MemUsedPct);
        Assert.Equal(3969, read.StatesUsed);
        Assert.Equal(397000, read.StatesLimit);
        Assert.True(read.UptimeHours is > 136 and < 137);
    }
}
