#nullable enable
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Rack;
using Kor.Operations.NetworkOps.Transport;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// What is plugged into each core switch port (Ian, 2026-10-02: "which IP / device name is attached to each port"): the
// switch's MAC address table (Q-BRIDGE, else BRIDGE), named on APP01 (Service/Rack/MacDirectory), drawn as a tile per port.
//
// WHAT IT COVERS: reading the MAC table in both MIB forms (the MAC is the OID's last six numbers; bridge port -> ifIndex),
// a MAC on two VLANs counted once, CPU entries (port 0) dropped; a port's text named where possible, MACs last, at most six
// and "+N more", always fitting its 400-character fact; a tile per port with something plugged in; APP01's ARP read in
// the right shape (on this PC's real table).
// WHAT IT DOES NOT: the EdgeSwitch actually filling either table, or DNS naming -- read live after the deploy (2026-10-02).
// A SAME-CLASS FAULT IT WOULD NOT CATCH: a device that never talks to APP01 is not in its ARP table, so it shows as a MAC
// (or as the UniFi switch it sits behind) -- that is the network's truth, not a fault, but it is not a name.
public sealed class SwitchPortsTests
{
    private const string Ifs = "1.3.6.1.2.1.2.2.1.2";

    internal static Dictionary<string, string> Walk() => new()
    {
        [$"{Ifs}.1"] = "\"Slot: 0 Port: 1 10G - Level\"", [$"{Ifs}.2"] = "\"Slot: 0 Port: 2 10G - Level\"", [$"{Ifs}.3"] = "\"CPU Interface for Slot: 5 Port: 1\"",
        ["1.3.6.1.2.1.2.2.1.8.1"] = "1", ["1.3.6.1.2.1.2.2.1.8.2"] = "1", ["1.3.6.1.2.1.2.2.1.8.3"] = "1",
        [$"{EdgeSwitchRules.BasePortIfIndex}.1"] = "1", [$"{EdgeSwitchRules.BasePortIfIndex}.2"] = "2",
        // VLAN 1: ESXi on port 1; a UniFi switch and two PCs behind it on port 2. VLAN 20: the ESXi MAC again on port 1.
        [$"{EdgeSwitchRules.QFdbPort}.1.0.80.86.26.1.16"] = "1",
        [$"{EdgeSwitchRules.QFdbPort}.20.0.80.86.26.1.16"] = "1",
        [$"{EdgeSwitchRules.QFdbPort}.1.116.131.194.10.120.36"] = "2",
        [$"{EdgeSwitchRules.QFdbPort}.1.0.1.2.3.4.5"] = "2",
        [$"{EdgeSwitchRules.QFdbPort}.1.0.1.2.3.4.6"] = "2",
        [$"{EdgeSwitchRules.QFdbPort}.1.0.1.2.3.4.7"] = "0",   // the switch's own MAC (port 0): not a device
    };

    [Fact]
    public void The_MAC_table_is_read_per_port_once_per_MAC()
    {
        var macs = EdgeSwitchRules.MacsByIfIndex(Walk());
        Assert.Equal(["00:50:56:1a:01:10"], macs["1"]);                                  // two VLANs, one device
        Assert.Equal(3, macs["2"].Count);
        Assert.False(macs.ContainsKey("0"));
    }

    [Fact]
    public void The_plain_BRIDGE_table_is_used_when_the_VLAN_one_is_empty()
    {
        var v = Walk().Where(kv => !kv.Key.StartsWith(EdgeSwitchRules.QFdbPort, StringComparison.Ordinal)).ToDictionary(kv => kv.Key, kv => kv.Value);
        v[$"{EdgeSwitchRules.FdbPort}.0.80.86.26.1.16"] = "1";
        Assert.Equal(["00:50:56:1a:01:10"], EdgeSwitchRules.MacsByIfIndex(v)["1"]);
    }

    [Fact]
    public void Each_port_says_what_is_attached_named_where_possible()
    {
        var names = new Dictionary<string, string> { ["00:50:56:1a:01:10"] = "VMHOST01 (192.168.1.10)", ["74:83:c2:0a:78:24"] = "UniFi USF5P 192.168.1.60" };
        var read = EdgeSwitchRules.Evaluate(Walk(), new Dictionary<string, string>(), m => names.GetValueOrDefault(m));
        Assert.Equal("VMHOST01 (192.168.1.10)", read.Facts["port.attached:1"]);
        Assert.StartsWith("UniFi USF5P 192.168.1.60; 00:01:02:03:04:05", read.Facts["port.attached:2"]);   // names first, MACs after
        Assert.Equal(3, read.Metrics.Single(m => m.Metric == "port.devices" && m.Subject.Contains("Port: 2")).Value);
        Assert.DoesNotContain(read.Metrics, m => m.Metric == "port.up" && m.Subject.StartsWith("CPU", StringComparison.Ordinal));

        var tiles = RackComponents.Of(read.Facts, read.Metrics.Select(m => new DeviceReading(m.Metric, m.Subject, m.Value, DateTime.UtcNow)).ToList(), [])
            .Where(t => t.Kind == "port").ToList();
        Assert.Equal(["Port 1", "Port 2"], tiles.Select(t => t.Title));
        Assert.Equal(("UniFi USF5P 192.168.1.60", "+2 more behind it"), (tiles[1].Line1, tiles[1].Line2));
        Assert.Contains("00:01:02:03:04:06", tiles[1].Detail);
    }

    [Fact]
    public void A_MAC_nobody_can_name_says_its_maker_when_it_is_one_on_KORs_network()
    {
        // As learned on the core switch 2026-10-02, ports 9-12: the NAS's and the hosts' storage ports, never in APP01's ARP.
        Assert.Equal("Synology 00:11:32:fe:dd:d7", EdgeSwitchRules.AttachedText(["00:11:32:fe:dd:d7"], _ => null));
        Assert.Equal("VMware 00:50:56:6e:d0:07", EdgeSwitchRules.AttachedText(["00:50:56:6e:d0:07"], _ => null));
        Assert.Equal("38:68:dd:55:de:b9", EdgeSwitchRules.AttachedText(["38:68:dd:55:de:b9"], _ => null));   // not on the list: no guess
        Assert.Equal("KOR-FS01 (192.168.1.31)", EdgeSwitchRules.AttachedText(["00:50:56:8b:00:01"], _ => "KOR-FS01 (192.168.1.31)"));   // a name beats a maker
    }

    [Fact]
    public void An_uplink_full_of_devices_still_fits_its_fact()
    {
        var many = Enumerable.Range(0, 200).Select(i => $"00:00:00:00:{i / 256:x2}:{i % 256:x2}").ToList();
        var text = EdgeSwitchRules.AttachedText(many, m => $"A-VERY-LONG-DEVICE-NAME-THAT-GOES-ON-AND-ON-{m} (192.168.100.200)");
        Assert.True(text.Length <= 400, $"{text.Length} characters: the store would cut it");
        Assert.EndsWith("; +194 more", text);
    }

    [Fact]
    public void APP01s_ARP_table_reads_as_MAC_to_IP()
    {
        var arp = ArpTable.Read();   // this PC's real table: the shape is what matters
        Assert.All(arp, kv =>
        {
            Assert.Matches("^[0-9a-f]{2}(:[0-9a-f]{2}){5}$", kv.Key);
            Assert.True(System.Net.IPAddress.TryParse(kv.Value, out _), kv.Value);
        });
        Assert.NotEmpty(arp);        // every PC on a network has at least its gateway's entry
    }
}
