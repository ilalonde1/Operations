#nullable enable
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.Network;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.NetworkOps.Tests;

// The port map (Ian, 2026-10-02: "which DEVICES by name (and user) ... see it clearly"), on the REAL reads of that evening:
// the UniFi controller (Fixtures/netmap/unifi-2026-10-02.json, kor-unifi-status), DC01's DHCP leases, and every PC's last
// NetworkOps check (its network cards' MACs and who was signed in).
//
// WHAT IT COVERS: every fleet PC with a MAC is on exactly one switch port, named by its agent; no MAC is on two ports; every
// one of the controller's clients appears exactly once (occupant, also seen, wireless or unplaced) -- nothing dropped,
// nothing doubled; the tree joins up (every switch listed after its parent; the core switch named as the root; each
// switch's own uplink port is "uplink", the port it hangs from on its parent is "link"); the switch's own word decides a
// port with a history (BMZ-SW01 port 5: a USW Mini, then KOR-PERFORM3, then KOR-1001 -- the last the switch saw connect
// there; KOR-1001 itself was on the VPN that evening, so "on the port" means "last connected there", not "there now").
// WHAT IT DOES NOT: whether a name is RIGHT (a DHCP host name can be stale or wrong: KOR-218N's lease says "Henry Perform IP");
// the core switch's own ports (its MAC table is not in this read); whether a device is online now -- the controller's
// "last seen" cadence is not established, so the map shows dates, never "online".
// A SAME-CLASS FAULT IT WOULD NOT CATCH: a PC whose network card changed since its last check (new dock, new NIC) is named
// by DHCP or the maker instead of its agent, and the fleet count here does not notice because it counts MACs it was given.
public sealed class NetworkMapTests(ITestOutputHelper output)
{
    private static string Fx(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "netmap", name));

    private const string CoreMac = "74:ac:b9:aa:84:a3";   // the EdgeSwitch 10G at 192.168.1.11, read from APP01's neighbour table 2026-10-02

    private sealed record FleetRow(string Name, List<string> Macs, string? LastUser);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    internal static IReadOnlyList<FleetPc> Fleet()
        => JsonSerializer.Deserialize<List<FleetRow>>(Fx("fleet-2026-10-02.json"), Web)!
            .Select(f => new FleetPc(f.Name, f.Macs, f.LastUser is { Length: > 0 } u ? u : null, f.LastUser is { Length: > 0 } ? "signed in now" : null)).ToList();

    internal static IReadOnlyList<DhcpLease> Leases()
        => JsonSerializer.Deserialize<List<DhcpLease>>(Fx("dhcp-2026-10-02.json"), Web)!;

    internal static NetworkMap Map()
        => NetworkMaps.Build(UniFiSite.Parse(Fx("unifi-2026-10-02.json")), Fleet(), Leases(),
            new KnownNames(new Dictionary<string, string> { [CoreMac] = "Core switch (EdgeSwitch 10G)" }, new Dictionary<string, string>()));

    [Fact]
    public void Every_fleet_PC_with_a_MAC_is_on_exactly_one_port_named_by_its_agent()
    {
        var map = Map();
        var withMac = Fleet().Where(f => f.Macs.Count > 0).ToList();
        var lost = withMac.Where(f => map.PortOf(f.Name) is null).Select(f => f.Name).ToList();
        output.WriteLine($"fleet PCs on a port: {withMac.Count - lost.Count} of {withMac.Count}");
        Assert.True(lost.Count == 0, "not on any port: " + string.Join(", ", lost));
        var occupants = map.Switches.SelectMany(s => s.Ports).Where(p => p.On?.Pc is not null).GroupBy(p => p.On!.Pc!).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(occupants.Count == 0, "on two ports: " + string.Join(", ", occupants));
        Assert.All(withMac, f => Assert.Equal("NetworkOps agent", map.PortOf(f.Name)!.Value.Port.On!.NameSource));
    }

    [Fact]
    public void No_MAC_is_on_two_ports_and_every_client_appears_exactly_once()
    {
        var map = Map();
        var site = UniFiSite.Parse(Fx("unifi-2026-10-02.json"));
        // The tree is not endpoints: the UniFi devices, and the parents their uplinks lead to (the core switch).
        var deviceMacs = site.Devices.Select(d => d.Mac).Concat(site.Devices.Select(d => d.Uplink?.Mac).OfType<string>()).ToHashSet();
        var expected = site.Clients.Select(c => c.Mac).Distinct().Where(m => !deviceMacs.Contains(m)).ToHashSet();

        var seen = map.Everything().GroupBy(e => e.Endpoint.Mac).ToDictionary(g => g.Key, g => g.Select(x => x.Where).ToList());
        var doubled = seen.Where(kv => kv.Value.Count > 1).Select(kv => $"{kv.Key}: {string.Join(" + ", kv.Value)}").ToList();
        Assert.True(doubled.Count == 0, "in two places: " + string.Join("; ", doubled));
        var dropped = expected.Where(m => !seen.ContainsKey(m)).ToList();
        output.WriteLine($"controller clients accounted for: {expected.Count - dropped.Count} of {expected.Count}");
        Assert.True(dropped.Count == 0, "dropped: " + string.Join(", ", dropped));

        foreach (var g in map.Everything().GroupBy(e => e.Where.Contains("(also seen)") ? "also seen" : e.Where.Contains("(wireless)") ? "wireless" : e.Where.StartsWith("unplaced") ? "unplaced" : "on a port"))
            output.WriteLine($"  {g.Key}: {g.Count()}");
        foreach (var g in map.Everything().GroupBy(e => e.Endpoint.NameSource).OrderByDescending(g => g.Count()))
            output.WriteLine($"  named by {g.Key}: {g.Count()}");
    }

    [Fact]
    public void The_tree_joins_up_from_the_core_switch()
    {
        var map = Map();
        Assert.Equal(10, map.Switches.Count);                                                     // every UniFi switch, once
        Assert.Equal(map.Switches.Count, map.Switches.Select(s => s.Mac).Distinct().Count());
        foreach (var (s, i) in map.Switches.Select((s, i) => (s, i)))
        {
            if (s.ParentMac == CoreMac) { Assert.Equal(1, s.Depth); Assert.Equal("Core switch (EdgeSwitch 10G)", s.ParentName); continue; }
            var parent = map.Switches.ToList().FindIndex(p => p.Mac == s.ParentMac);
            if (parent < 0) continue;                                                              // hangs from an access point / unmanaged: depth 1
            Assert.True(parent < i, $"{s.Name} is listed before its parent");
            Assert.Equal(map.Switches[parent].Depth + 1, s.Depth);
            Assert.Equal("link", map.Switches[parent].Ports.Single(p => p.Number == s.ParentPort).Kind);   // where it hangs, on the parent
        }
        Assert.All(map.Switches.Where(s => s.UplinkPort is not null), s => Assert.Equal("uplink", s.Ports.Single(p => p.Number == s.UplinkPort).Kind));
        Assert.Equal(["BMZ-SW01", "BMZ-SW02"], map.Switches.Where(s => s.ParentMac == CoreMac).Select(s => s.Name).Order());
    }

    [Fact]
    public void A_port_with_a_history_is_the_switchs_last_word_and_keeps_the_rest_as_also_seen()
    {
        var p5 = Map().Switches.Single(s => s.Name == "BMZ-SW01").Ports.Single(p => p.Number == 5);
        Assert.Equal("KOR-1001", p5.On!.Name);
        Assert.Equal("kor\\ilalonde", p5.On.User);
        Assert.Contains(p5.AlsoSeen, a => a.Name == "KOR-PERFORM3");                             // DHCP-named: no agent on it
    }

    [Fact]
    public void The_map_survives_the_trip_to_netops_and_reads_as_text()
    {
        // The service serialises it (ASP.NET, web defaults); netops reads it back: the same map must come out.
        var sent = new NetworkMapResponse(DateTime.UtcNow, ["a note"], Map());
        var back = JsonSerializer.Deserialize<NetworkMapResponse>(JsonSerializer.Serialize(sent, Web), Web)!;
        Assert.Equal(sent.Map.Everything().Count(), back.Map.Everything().Count());
        Assert.Equal(("BMZ-SW01", 5), (back.Map.PortOf("KOR-1001")!.Value.Switch.Name, back.Map.PortOf("KOR-1001")!.Value.Port.Number));

        var text = NetworkMapText.Render(back);
        Assert.All(back.Map.Switches, s => Assert.Contains(s.Name, text));
        Assert.Contains("KOR-207 | kor\\markb", text);
        var hit = NetworkMapText.Render(back, "markb");
        Assert.Contains("USW Flex - Mark port 3", hit);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "netops-network-fixture.txt"), text);   // looked at, 2026-10-02
    }

    // THE CLASS: anything joined on a device's NAME breaks when it is renamed. Found twice on 2026-10-02 at the BMZ -> KOR
    // rename: the move history (switch names; fixed with 011) and the access points' wireless clients (all three showed 0,
    // because each client remembers its AP by the name it had). A differential: rename EVERY UniFi device -- the clients keep
    // the old names, exactly as the live controller does -- and everything but the names must come out the same.
    // COVERS: every placement (device, placement, switch by MAC, port), each access point's clients, the tree (parent, port,
    // depth). DOES NOT: the store's rows (IsMove is tested apart) or the app. WOULD NOT CATCH: a join on a PC's name -- PCs
    // are named by their agent and are not renamed here.
    [Fact]
    public void Renaming_every_UniFi_device_changes_nothing_but_names()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(Fx("unifi-2026-10-02.json"))!;
        foreach (var d in json["devices"]!.AsArray()) d!["name"] = "Renamed " + (string?)d["mac"];
        var renamed = NetworkMaps.Build(UniFiSite.Parse(json.ToJsonString()), Fleet(), Leases(),
            new KnownNames(new Dictionary<string, string> { [CoreMac] = "Core switch (EdgeSwitch 10G)" }, new Dictionary<string, string>()));
        var before = Map();

        static IEnumerable<string> Where(NetworkMap m) => m.Everything().Select(p => $"{p.Endpoint.Mac} {p.Placement} {p.SwitchMac} {p.Port}").Order();
        Assert.Equal(Where(before), Where(renamed));
        // Lists sort by name, so their ORDER may differ: compared in MAC order.
        Assert.Equal(before.AccessPoints.Select(a => (a.Mac, a.Clients.Count)).Order(), renamed.AccessPoints.Select(a => (a.Mac, a.Clients.Count)).Order());
        Assert.Equal(before.Switches.Select(s => (s.Mac, s.ParentMac, s.ParentPort, s.Depth)).Order(), renamed.Switches.Select(s => (s.Mac, s.ParentMac, s.ParentPort, s.Depth)).Order());
        Assert.Equal(31, renamed.AccessPoints.Sum(a => a.Clients.Count) + renamed.OtherWireless.Count);
        Assert.True(renamed.AccessPoints.Sum(a => a.Clients.Count) > 0, "the access points lost their wireless clients");
        Assert.All(renamed.AccessPoints.SelectMany(a => a.Clients), c => Assert.StartsWith("Renamed ", c.Via));   // shown by the CURRENT name
    }

    // Rule 6, the LIVE read (Ian, 2026-10-02: "the old firewall IS GONE. So you tell me what's saying it's a .1 address" --
    // the database's record of SW03 port 7; and he was on the VPN, not at KOR-1001's port). Fixture: the controller's live
    // API that evening (unifi-live-2026-10-02.json, projected: no keys).
    // COVERS: each PC says connected now or not (29 of 30; KOR-1001 not); a dead port reads down (SW03 port 7, the old
    // firewall's record); the live read moves no placement where it agrees with the database (70 of 70 did); nothing
    // dropped or doubled with it. DOES NOT: a device that MOVED between the two reads (none had) -- IsMove and the claim
    // override cover the shape, not real data. WOULD NOT CATCH: a client the live API lists on the wrong port.
    private static UniFiLive Live() => UniFiLive.Parse(Fx("unifi-live-2026-10-02.json"));

    private static NetworkMap LiveMap() => NetworkMaps.Build(UniFiSite.Parse(Fx("unifi-2026-10-02.json")), Fleet(), Leases(),
        new KnownNames(new Dictionary<string, string> { [CoreMac] = "Core switch (EdgeSwitch 10G)" }, new Dictionary<string, string>()), Live());

    [Fact]
    public void With_the_live_read_every_PC_says_whether_it_is_connected_now()
    {
        var map = LiveMap();
        var pcs = Fleet().Where(f => f.Macs.Count > 0).Select(f => map.PortOf(f.Name)!.Value.Port.On!).ToList();
        output.WriteLine($"fleet PCs connected now: {pcs.Count(e => e.ConnectedNow == true)} of {pcs.Count}");
        Assert.Equal(29, pcs.Count(e => e.ConnectedNow == true));
        Assert.False(pcs.Single(e => e.Pc == "KOR-1001").ConnectedNow);             // on the VPN that evening
        Assert.All(Map().Everything(), p => Assert.Null(p.Endpoint.ConnectedNow));  // no live read: no claim about now
        Assert.NotNull(map.LiveUtc);
    }

    [Fact]
    public void A_dead_port_reads_down_and_its_old_device_not_connected()
    {
        // By MAC: the database fixture was read before the BMZ -> KOR rename (the lesson of that rename, again).
        var p7 = LiveMap().Switches.Single(s => s.Mac == "78:45:58:e5:fa:ca").Ports.Single(p => p.Number == 7);       // SW03
        Assert.False(p7.Up);
        Assert.False(p7.On!.ConnectedNow);                                              // the old firewall's record, three days old
        var dac = LiveMap().Switches.Single(s => s.Mac == "74:83:c2:13:f1:c2").Ports.Single(p => p.Number == 49);     // SW01
        Assert.Equal((true, 10000, "SFP-H10GB-CU1M"), (dac.Up!.Value, dac.SpeedNow!.Value, dac.Module));   // ESXi .16, 10G on a 1 m DAC
        Assert.True(dac.On!.ConnectedNow);                                              // the host, with its VMs live behind it
        Assert.Contains(dac.AlsoSeen, a => a.Mac == "00:50:56:1a:01:27");               // KOR-MESH01
    }

    [Fact]
    public void The_live_read_moves_nothing_where_it_agrees_and_drops_nothing()
    {
        static IEnumerable<string> Where(NetworkMap m) => m.Everything().Where(p => p.Placement == "port").Select(p => $"{p.Endpoint.Mac} {p.SwitchMac} {p.Port}").Order();
        Assert.Equal(Where(Map()), Where(LiveMap()));
        var all = LiveMap().Everything().GroupBy(p => p.Endpoint.Mac).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(all.Count == 0, "in two places with the live read: " + string.Join(", ", all));
    }

    [Fact]
    public void Desk_switches_show_whose_desk_it_is()
    {
        var map = Map();
        Assert.Equal(("USW Flex Mini - Server Room PCs", 5), (map.PortOf("KOR-208-N")!.Value.Switch.Name, map.PortOf("KOR-208-N")!.Value.Port.Number));
        Assert.Equal(("USW Flex - Mark", 3), (map.PortOf("KOR-207")!.Value.Switch.Name, map.PortOf("KOR-207")!.Value.Port.Number));
        Assert.Equal("kor\\markb", map.PortOf("KOR-207")!.Value.Port.On!.User);
    }
}
