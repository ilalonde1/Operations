#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Power;
using Kor.Operations.NetworkOps.Core.Rack;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The rack rules, on what the devices actually returned on 2026-09-30 (Fixtures/rack, captured through the
// same read-only channels the service uses), plus the faults this rack has actually had, recreated.
//
// WHAT THIS COVERS: every rule set parses a real reply; a healthy rack raises nothing alarming; each fault
// class fires when its evidence is present (the 35-min clock, PTP, blind sensors, a dead PSU/disk/fan, a
// degraded RAID, a stale or failed backup, a switch or AP gone quiet, traffic leaving by the wrong WAN).
// WHAT IT DOES NOT COVER: the transports (SSH, SNMP, REST) -- those are proven by the live sweep; nor whether
// the thresholds are the right ones for KOR (they are stated in each rule). A fault this would NOT catch:
// a device that answers with stale data (a hung controller still serving its last database state).
public sealed class RackRulesTests
{
    private static string Fx(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rack", name));

    /// <summary>snmpwalk -On text -> OID (no leading dot) -> value, as the service's channel returns it.</summary>
    internal static Dictionary<string, string> Walk(string text)
    {
        var d = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(text, @"^\.?([\d.]+) = (\w+): (.*)$", RegexOptions.Multiline))
        {
            var v = m.Groups[3].Value.Trim();
            if (m.Groups[2].Value == "Timeticks" && Regex.Match(v, @"^\((\d+)\)") is { Success: true } t) v = t.Groups[1].Value;
            d[m.Groups[1].Value] = v.Trim('"');
        }
        return d;
    }

    private static readonly string[] Production = ["Kor-DC01", "vcenter", "Kor-FS01", "Kor-APP01", "Kor-RDS01", "Kor-BK01", "KOR-UNIFI01"];

    // ---------------------------------------------------------------- ESXi

    [Fact]
    public void Host_10_as_read_is_healthy_apart_from_the_leftover_Veeam_mount()
    {
        var at = DateTime.Parse("2026-09-30T09:41:57Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
        var r = EsxiRules.Evaluate(Fx("esxi-10.json"), Production, at);
        Assert.True(r.Reachable);
        Assert.DoesNotContain(r.Findings, f => f.Severity >= Severity.Warning);
        Assert.Contains(r.Findings, f => f.RuleKey == "esxi.stale-mount:VeeamBackup_KOR-BK01" && f.Severity == Severity.Info);
        Assert.Equal("Lenovo ThinkSystem SR650 -[7X06CTO1WW]-", r.Facts["hw.model"]);
        Assert.Equal(181, r.Metrics.Single(m => m.Metric == "sensors.count").Value);
    }

    [Fact]
    public void Host_16_with_CIM_off_is_reported_blind()
    {
        var r = EsxiRules.Evaluate(Fx("esxi-16.json"), Production, DateTime.Parse("2026-09-30T09:41:56Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal));
        Assert.Contains(r.Findings, f => f.RuleKey == "esxi.sensors-blind");
    }

    [Fact]
    public void The_35_minute_clock_PTP_and_a_red_PSU_are_all_raised()
    {
        var json = Fx("esxi-10.json")
            .Replace("\"ptp\": {\"policy\": \"off\", \"running\": false}", "\"ptp\": {\"policy\": \"on\", \"running\": true}");
        var j = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        j["ptp"] = System.Text.Json.Nodes.JsonNode.Parse("{\"policy\":\"on\",\"running\":true}");
        j["hostTimeUtc"] = "2026-09-30T09:06:57+00:00";   // 35 min slow
        j["sensors"]![0]!["state"] = "red";
        var r = EsxiRules.Evaluate(j.ToJsonString(), Production, DateTime.Parse("2026-09-30T09:41:57Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal));
        Assert.Contains(r.Findings, f => f.RuleKey == "esxi.clock" && f.Severity == Severity.Critical);
        Assert.Contains(r.Findings, f => f.RuleKey == "esxi.ptp");
        Assert.Contains(r.Findings, f => f.RuleKey.StartsWith("esxi.sensor:") && f.Severity == Severity.Critical);
    }

    [Fact]
    public void A_production_VM_that_is_off_is_critical()
    {
        var j = System.Text.Json.Nodes.JsonNode.Parse(Fx("esxi-10.json"))!;
        foreach (var vm in j["vms"]!.AsArray()) if ((string?)vm!["name"] == "Kor-DC01") vm["power"] = "poweredOff";
        var r = EsxiRules.Evaluate(j.ToJsonString(), Production, DateTime.Parse("2026-09-30T09:41:57Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal));
        Assert.Contains(r.Findings, f => f.RuleKey == "esxi.vm-off:Kor-DC01" && f.Severity == Severity.Critical);
    }

    // ---------------------------------------------------------------- Synology

    [Theory]
    [InlineData("synology-15.txt", "DS1621+", 0)]
    [InlineData("synology-105.txt", "DS1522+", 0)]
    [InlineData("synology-12.txt", "UC3200", 10)]
    public void All_three_Synologys_as_read_are_healthy(string file, string model, int volumeWarn)
    {
        var r = SynologyRules.Evaluate(Walk(Fx(file)), volumeWarn);
        Assert.Equal(model, r.Facts["hw.model"]);
        Assert.DoesNotContain(r.Findings, f => f.Severity >= Severity.Warning);
    }

    [Fact]
    public void The_Veeam_LUN_hosts_volumes_would_false_alarm_if_volume_space_were_checked_there()
    {
        // NAS01's volume is 1% free BY DESIGN (a thick LUN fills it): exactly why its threshold is 0.
        Assert.Contains(SynologyRules.Evaluate(Walk(Fx("synology-15.txt")), 10).Findings, f => f.RuleKey.StartsWith("syno.volume-full:"));
        Assert.DoesNotContain(SynologyRules.Evaluate(Walk(Fx("synology-15.txt")), 0).Findings, f => f.RuleKey.StartsWith("syno.volume-full:"));
    }

    [Fact]
    public void A_failed_PSU_a_failing_disk_and_a_degraded_RAID_are_critical()
    {
        var v = Walk(Fx("synology-15.txt"));
        v["1.3.6.1.4.1.6574.1.3.0"] = "2";            // power failed (the UC3200 PSU2 story)
        v["1.3.6.1.4.1.6574.2.1.1.13.2"] = "4";       // disk 3 failing
        v["1.3.6.1.4.1.6574.3.1.1.3.0"] = "11";       // volume degraded
        var f = SynologyRules.Evaluate(v, 0).Findings;
        Assert.Contains(f, x => x.RuleKey == "syno.power" && x.Severity == Severity.Critical);
        Assert.Contains(f, x => x.RuleKey == "syno.disk:Disk 3" && x.Severity == Severity.Critical);
        Assert.Contains(f, x => x.RuleKey == "syno.raid:Volume 1" && x.Severity == Severity.Critical);
    }

    [Fact]
    public void The_SAN_reports_its_pending_DSM_UC_update()
        => Assert.Contains(SynologyRules.Evaluate(Walk(Fx("synology-12.txt")), 10).Findings, f => f.RuleKey == "syno.update" && f.Severity == Severity.Info);

    // ---------------------------------------------------------------- Veeam

    [Fact]
    public void Veeam_as_read_shows_yesterdays_warning_and_nothing_stale()
    {
        var r = VeeamRules.Evaluate(Fx("veeam-jobs.json"), Fx("veeam-repos.json"), DateTime.Parse("2026-09-30T09:50:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal));
        Assert.Contains(r.Findings, f => f.RuleKey == "veeam.warning:Kor-VMs-New");
        Assert.DoesNotContain(r.Findings, f => f.RuleKey.StartsWith("veeam.stale:") || f.RuleKey.StartsWith("veeam.failed:") || f.RuleKey.StartsWith("veeam.repo"));
    }

    [Fact]
    public void A_failed_job_stays_failed_while_its_retry_runs_and_clears_only_when_a_run_finishes_well()
    {
        var at = DateTime.Parse("2026-09-30T10:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
        string Jobs(string status, string result) => Fx("veeam-jobs.json")
            .Replace("\"status\":\"running\",\"lastResult\":\"None\"", $"\"status\":\"{status}\",\"lastResult\":\"{result}\"");
        // 1. It failed and is idle: critical, and the result is remembered.
        var failed = VeeamRules.Evaluate(Jobs("inactive", "Failed"), Fx("veeam-repos.json"), at);
        Assert.Contains(failed.Findings, f => f.RuleKey == "veeam.failed:Kor-FS01");
        // 2. The retry is running ("None"): STILL failed (THE bug this guards: it used to clear here, then re-alert).
        var retrying = VeeamRules.Evaluate(Jobs("running", "None"), Fx("veeam-repos.json"), at, failed.Facts);
        Assert.Contains(retrying.Findings, f => f.RuleKey == "veeam.failed:Kor-FS01" && f.Evidence.Contains("in progress"));
        // 3. The retry finished well: cleared.
        var good = VeeamRules.Evaluate(Jobs("inactive", "Success"), Fx("veeam-repos.json"), at, retrying.Facts);
        Assert.DoesNotContain(good.Findings, f => f.RuleKey == "veeam.failed:Kor-FS01");
    }

    [Fact]
    public void Eight_days_without_a_run_is_the_silent_failure_and_is_critical()
    {
        var r = VeeamRules.Evaluate(Fx("veeam-jobs.json"), Fx("veeam-repos.json"), DateTime.Parse("2026-10-08T09:50:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal));
        Assert.Contains(r.Findings, f => f.RuleKey == "veeam.stale:Kor-VMs-New" && f.Severity == Severity.Critical);
    }

    // ---------------------------------------------------------------- UniFi, internet, core switch, UPS

    [Fact]
    public void UniFi_as_read_flags_the_two_flex_switches_that_never_check_in()
    {
        var r = UniFiRules.Evaluate(Fx("unifi.json"));
        var offline = r.Findings.Where(f => f.RuleKey.StartsWith("unifi.offline:")).ToList();
        Assert.Equal(2, offline.Count);
        Assert.Contains(offline, f => f.Evidence.Contains("192.168.1.53"));
        Assert.Contains(offline, f => f.Evidence.Contains("192.168.1.59") && f.Evidence.Contains("never"));
        Assert.All(offline, f => Assert.Equal(Severity.Warning, f.Severity));   // flex switches: one area, not the building
        Assert.Equal(11, r.Metrics.Single(m => m.Metric == "devices.online").Value);
    }

    [Fact]
    public void APP01_as_read_raises_its_stopped_services_and_71_days_unpatched()
    {
        var r = ServerRules.Evaluate(Fx("server-app01.json"));
        Assert.Contains(r.Findings, f => f.RuleKey == "server.service-stopped:Kor.Operations.Mcp");
        Assert.Contains(r.Findings, f => f.RuleKey == "server.service-stopped:Certify.Service");
        Assert.Contains(r.Findings, f => f.RuleKey == "server.unpatched");
        Assert.DoesNotContain(r.Findings, f => f.RuleKey.StartsWith("server.vss-writer:"));   // all 14 writers were stable
        Assert.DoesNotContain(r.Findings, f => f.RuleKey.Contains("AppXSvc") || f.RuleKey.Contains("ncstreamer"));   // demand-start noise
    }

    [Fact]
    public void A_VSS_writer_in_error_is_critical_but_one_mid_snapshot_is_not()
    {
        var j = System.Text.Json.Nodes.JsonNode.Parse(Fx("server-app01.json"))!;
        var writers = j[0]!["VssWriters"]!.AsArray();
        writers[0]!["State"] = "Failed"; writers[0]!["LastError"] = "Timed out";                 // FS01, 30 Sep 2026
        writers[1]!["State"] = "Waiting for completion"; writers[1]!["LastError"] = "No error";  // a backup in progress
        var f = ServerRules.Evaluate(j.ToJsonString()).Findings.Where(x => x.RuleKey.StartsWith("server.vss-writer:")).ToList();
        Assert.Single(f);
        Assert.Equal(Severity.Critical, f[0].Severity);
    }

    [Fact]
    public void Traffic_leaving_by_any_address_but_the_static_is_critical()
    {
        var pings = new Dictionary<string, (int, int, double)> { ["192.168.1.1"] = (4, 4, 1), ["1.1.1.1"] = (4, 4, 9) };
        Assert.DoesNotContain(InternetRules.Evaluate(new InternetCheck("184.71.160.54", "184.71.160.54", true, pings)).Findings, f => f.Severity >= Severity.Warning);
        Assert.Contains(InternetRules.Evaluate(new InternetCheck("50.64.46.253", "184.71.160.54", true, pings)).Findings, f => f.RuleKey == "net.public-ip" && f.Severity == Severity.Critical);
    }

    [Fact]
    public void Core_switch_as_read_draws_one_light_per_physical_port_in_front_panel_order()
    {
        var v = Walk(Fx("edgeswitch.txt"));
        var read = EdgeSwitchRules.Evaluate(v, new Dictionary<string, string>());
        var physical = v.Where(kv => kv.Key.StartsWith("1.3.6.1.2.1.2.2.1.2.", StringComparison.Ordinal) && EdgeSwitchRules.PortNumber(kv.Value.Trim('"')) is not null).ToList();
        Assert.Equal(physical.Count, read.Metrics.Count(m => m.Metric == "port.up"));                      // CPU / VLAN interfaces are not ports
        var tile = RackComponents.Of(read.Facts, read.Metrics.Select(m => new DeviceReading(m.Metric, m.Subject, m.Value, DateTime.UtcNow)).ToList(), [])
            .Single(t => t.Kind == "ports");
        Assert.Equal(physical.Count, tile.Lights!.Count);
        // 10 interfaces up that day -- 9 front-panel ports and the always-up CPU interface, which is not a port.
        Assert.Equal(10, read.Metrics.Single(m => m.Metric == "ports.up").Value);
        Assert.Equal(9, tile.Lights.Count(l => l >= 1));
        Assert.Equal($"9 of {physical.Count} up", tile.Line1);
        Assert.Equal(16, physical.Count);                                                                   // a 16-port switch
        Assert.Equal(12, EdgeSwitchRules.PortNumber("Slot: 0 Port: 12 10G - Level"));
        Assert.Null(EdgeSwitchRules.PortNumber(" CPU Interface for Slot: 5 Port: 1"));                      // counted as port 1 until anchored
        Assert.Null(EdgeSwitchRules.PortNumber(" Link Aggregate 3"));
    }

    [Fact]
    public void Core_switch_as_read_records_its_up_ports_and_a_port_that_drops_is_raised()
    {
        var v = Walk(Fx("edgeswitch.txt"));
        var first = EdgeSwitchRules.Evaluate(v, new Dictionary<string, string>());
        Assert.Equal("EdgeSwitch 16-Port 10G", first.Facts["hw.model"]);
        Assert.Equal(10, first.Metrics.Single(m => m.Metric == "ports.up").Value);
        Assert.DoesNotContain(first.Findings, f => f.Severity >= Severity.Warning);

        var upPort = v.First(kv => kv.Key.StartsWith("1.3.6.1.2.1.2.2.1.8.") && kv.Value == "1").Key;
        var dropped = new Dictionary<string, string>(v) { [upPort] = "2" };
        var second = EdgeSwitchRules.Evaluate(dropped, first.Facts);
        Assert.Single(second.Findings, f => f.RuleKey.StartsWith("switch.port-down:"));
    }

    [Fact]
    public void Every_finding_family_a_rack_rule_can_raise_has_a_written_explanation()
    {
        // Read the rule sources (the families are string literals at each Raise), so a new rule without an
        // explanation fails here instead of showing "no explanation written" on the page.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.NetworkOps.Core", "Rack"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var families = Directory.GetFiles(Path.Combine(dir!.FullName, "Kor.Operations.NetworkOps.Core", "Rack"), "*.cs")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"Raise\(\$?""([a-z0-9.\-]+)").Select(m => m.Groups[1].Value))
            .Append(RackResult.UnreachableRule)
            .Distinct().ToList();
        Assert.True(families.Count >= 40, $"only {families.Count} families found: the scan is broken, not the rules");
        var missing = families.Where(f => Kor.Operations.NetworkOps.Core.Learning.Knowledge.For(f) is null).ToList();
        Assert.True(missing.Count == 0, "no Knowledge entry for: " + string.Join(", ", missing));
    }

    [Fact]
    public void The_shipped_rack_configuration_is_complete_and_every_channel_is_pinned()
    {
        var o = PowerTests.Shipped();
        var collectors = new[] { "Esxi", "Synology", "Veeam", "UniFi", "Internet", "CoreSwitch", "Ups", "WindowsServer", "Mesh", "MeshServer" };
        var kinds = new[] { RackKinds.Host, RackKinds.Storage, RackKinds.Ups, RackKinds.Backup, RackKinds.Network, RackKinds.Internet, RackKinds.Server };
        // 13 read directly, + FS01 and RDS01 through remote control only, + KOR-MESH01 itself (2026-09-30).
        Assert.Equal(16, o.Rack.Count);
        if (o.Rack.Any(d => d.Collector is "Mesh" or "MeshServer"))
            Assert.Equal(64, o.MeshCertSha256.Length);   // MeshCentral is pinned like every other channel
        Assert.Equal(o.Rack.Count, o.Rack.Select(d => d.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var d in o.Rack)
        {
            Assert.True(d.Name.Length <= 64, $"{d.Name}: longer than NetworkOps.Devices.Name");
            Assert.Contains(d.Collector, collectors);
            Assert.Contains(d.Kind, kinds);
            if (d.Collector == "Esxi") Assert.True(o.EsxiHostKeys.TryGetValue(d.Address, out var pins) && pins.Count > 0, $"{d.Name}: no pinned host key");
            if (d.Collector == "UniFi") Assert.NotEmpty(d.HostKeys);
            if (d.Collector == "Veeam") Assert.Equal(64, d.CertSha256.Length);
            if (d.Collector == "Ups") Assert.Contains(o.Ups, u => u.Name == d.UpsName);
        }
        // Every piece of the rack the chain shuts down is also watched.
        Assert.All(o.PowerChain.Hosts, h => Assert.Contains(o.Rack, d => d.Collector == "Esxi" && d.Address == h));
        Assert.All(o.PowerChain.Storage, s => Assert.Contains(o.Rack, d => d.Collector == "Synology" && d.Address == s.Address));
        Assert.Equal("184.71.160.54", o.ExpectedPublicIp);
    }

    [Fact]
    public void A_UPS_on_battery_is_critical_and_on_mains_is_quiet()
    {
        var now = DateTime.UtcNow;
        Assert.Empty(UpsRules.Evaluate(new UpsReading("E", now, true, PowerSource.Mains, 0, 29, 100, 31, false, false, null)).Findings);
        Assert.Contains(UpsRules.Evaluate(new UpsReading("E", now, true, PowerSource.Battery, 120, 20, 90, 31, false, false, null)).Findings, f => f.RuleKey == "ups.on-battery");
        Assert.Contains(UpsRules.Evaluate(new UpsReading("E", now, true, PowerSource.Mains, 0, 9, 100, 31, false, false, null)).Findings, f => f.RuleKey == "ups.short-runtime");
    }
}
