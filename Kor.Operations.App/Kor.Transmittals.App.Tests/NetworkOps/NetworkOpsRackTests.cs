#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.App.NetworkOps;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;

namespace Kor.Operations.App.Tests.NetworkOps;

/// <summary>
/// The Rack section: the tile takes the WORST device's colour, rows run worst-first then in outage order, a
/// healthy device says what it is doing, and a rack device's own window speaks of devices, not PCs.
/// WHAT IT DOES NOT COVER: what the service reads from the devices (the rack rules' tests) or how it looks (the
/// render test draws it). A fault this would not catch: a device the service never reports at all.
/// </summary>
public sealed class NetworkOpsRackTests
{
    private static readonly DateTime Now = new(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>The rack as the first live sweep read it on 2026-09-30 (the render test's fixture too).</summary>
    internal static FleetSnapshot Rack(DateTime nowUtc)
    {
        var read = nowUtc.AddMinutes(-2);
        DeviceRow D(int id, string name, string kind, string summary) => new(id, name, read, read, kind, summary);
        var devices = new List<DeviceRow>
        {
            D(101, "ESXi host .10 (production)", "Host", "5 of 6 VMs running · 314 sensors · CPU 12% · RAM 78%"),
            D(102, "ESXi host .16 (standby)", "Host", "2 of 4 VMs running · 249 sensors · CPU 3% · RAM 34%"),
            D(103, "UC3200 SAN", "Storage", "UC3200 · 12 disks · all healthy"),
            D(104, "NAS01 (Veeam repository)", "Storage", "DS1621+ · 6 disks · all healthy"),
            D(105, "Synology02 (Veeam repository)", "Storage", "DS1522+ · 5 disks · all healthy"),
            D(106, "Veeam backups (BK01)", "Backup", "1 of 3 jobs OK") with { MeshNodeId = "node//BK01node", MeshConnected = true },
            D(107, "Eaton 5PX UPS", "UPS", "Mains · 27 min · load 34%"),
            D(108, "APC SRT1500 UPS", "UPS", "Mains · 52 min · load 24%"),
            D(109, "UniFi network", "Network", "11 of 13 devices checking in"),
            D(110, "Core switch (EdgeSwitch 10G)", "Network", "10 ports up · up 4.2 days"),
            D(111, "Internet (Netgate + Shaw)", "Internet", "out via 184.71.160.54 · worst loss 0%"),
            D(112, "KOR-APP01 (apps, SQL, NetworkOps)", "Server", "up 4.2 days · C: 50 GB free, D: 169 GB free"),
            D(113, "KOR-DC01 (domain controller, DNS, DHCP)", "Server", "up 4.2 days · C: 49 GB free"),
        };
        FleetFinding F(long id, string device, string rule, Severity s, string title, string evidence)
            => new(id, device, rule, s, title, evidence, nowUtc.AddHours(-6), read, null, null, null, null);
        var findings = new List<FleetFinding>
        {
            F(1, "Veeam backups (BK01)", "veeam.failed:Kor-FS01", Severity.Critical, "Backup job Kor-FS01 FAILED", "last run Wed 30 Sep 02:44 ended Failed"),
            F(2, "Veeam backups (BK01)", "veeam.warning:Kor-VMs-New", Severity.Warning, "Backup job Kor-VMs-New finished with warnings", "last run Tue 29 Sep 17:00 ended Warning"),
            F(3, "UniFi network", "unifi.offline:f4:92:bf:ae:1f:23", Severity.Warning, "USF5P 192.168.1.53 is offline", "USF5P at 192.168.1.53 last checked in 87 days ago"),
            F(4, "UniFi network", "unifi.offline:74:83:c2:07:f0:b7", Severity.Warning, "USF5P 192.168.1.59 is offline", "USF5P at 192.168.1.59 has never checked in to this controller"),
            F(5, "UC3200 SAN", "syno.update", Severity.Info, "A DSM update is available", "running DSM 6.2-23036; install it in a maintenance window, not live"),
            F(6, "KOR-DC01 (domain controller, DNS, DHCP)", "server.unpatched", Severity.Warning, "Server has not been patched", "last update installed 71 days ago"),
        };
        var facts = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["ESXi host .10 (production)"] = new Dictionary<string, string> { ["hw.model"] = "Lenovo ThinkSystem SR650", ["esxi.version"] = "VMware ESXi 7.0.2 build-17867351" },
            ["Veeam backups (BK01)"] = new Dictionary<string, string>(),
        };
        return new FleetSnapshot(devices, facts, findings, [], new ServiceBeat("KOR-APP01", nowUtc.AddHours(-1), nowUtc.AddSeconds(-20), "0.6.0"));
    }

    [Fact]
    public void The_tile_is_the_worst_devices_colour_and_rows_run_worst_first_then_in_outage_order()
    {
        var vm = new NetworkOpsCommandCenterViewModel(NetworkOpsClient.Unconfigured("test"));
        vm.ApplyRack(Rack(Now), Now);

        Assert.Equal("Veeam backups (BK01)", vm.Rack[0].Name);                 // the critical one first
        Assert.Equal(HealthState.Critical, vm.Rack[0].State);
        Assert.Same(NetworkOpsBrushes.Critical, vm.RackBrush);
        Assert.Equal("10 / 13", vm.RackHeadline);                             // Veeam critical, UniFi and DC01 attention
        Assert.StartsWith("Rack: 1 critical, 2 need attention", vm.RackSubline);
        var healthy = vm.Rack.Where(r => r.State == HealthState.Healthy).Select(r => r.Kind).ToList();
        Assert.Equal("Internet", healthy[0]);                                  // the line in leads the healthy ones
        Assert.Equal("5 of 6 VMs running · 314 sensors · CPU 12% · RAM 78%", vm.Rack.Single(r => r.Name.StartsWith("ESXi host .10")).Headline);
    }

    [Fact]
    public void A_rack_devices_window_speaks_of_devices_and_reads_the_rack()
    {
        var snap = Rack(Now);
        var vm = new NetworkOpsDeviceViewModel(NetworkOpsClient.Unconfigured("test"), snap, snap.Devices.Single(d => d.Name == "ESXi host .10 (production)"));
        Assert.True(vm.IsRack);
        Assert.Equal("Read this device now", vm.CheckButtonText);
        Assert.Equal("Other devices", vm.OthersHeading);
        Assert.Contains("Lenovo ThinkSystem SR650", vm.IdentityLine);
        Assert.Contains("5 of 6 VMs running", vm.HardwareLine);
        Assert.StartsWith("Read every 5 minutes", vm.FreshnessLine);
    }

    [Fact]
    public void Find_and_only_problems_filter_the_rack_too()
    {
        var vm = new NetworkOpsCommandCenterViewModel(NetworkOpsClient.Unconfigured("test"));
        vm.ApplyRack(Rack(Now), Now);
        vm.FilterText = "storage";   // by kind: the SAN and both Synologys
        Assert.Equal(["UC3200 SAN", "NAS01 (Veeam repository)", "Synology02 (Veeam repository)"], vm.Rack.Select(r => r.Name));
        vm.FilterText = "";
        vm.ProblemsOnly = true;
        Assert.Equal(["Veeam backups (BK01)", "KOR-DC01 (domain controller, DNS, DHCP)", "UniFi network", "UC3200 SAN"], vm.Rack.Select(r => r.Name));
    }

    [Fact]
    public void A_rack_that_has_not_been_read_for_15_minutes_is_stale()
    {
        var old = Rack(Now.AddMinutes(-30));
        var vm = new NetworkOpsCommandCenterViewModel(NetworkOpsClient.Unconfigured("test"));
        vm.ApplyRack(old, Now);
        Assert.All(vm.Rack, r => Assert.True(r.IsStale));
    }
}
