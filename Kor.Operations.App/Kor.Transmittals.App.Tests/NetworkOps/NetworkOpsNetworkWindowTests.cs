#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Kor.Operations.App.NetworkOps;
using Kor.Operations.NetworkOps.Core.Network;
using Xunit;

namespace Kor.Operations.App.Tests.NetworkOps;

/// <summary>
/// The Network window (Ian, 2026-10-02: "see which DEVICES by name (and user) ... see it clearly"), on the real map of that
/// evening (the NetworkOps test fixtures: the controller, DC01's leases, the fleet).
///
/// WHAT IT COVERS: the left list is the tree -- every switch once, each indented under the switch it hangs from, then the
/// access points; a switch's page lists EVERY port in front-panel order (empty ones too), a desk's PC with its person
/// (domain dropped), links up and down named, a port's history under it; a search finds a person anywhere and says where;
/// the headline counts.
/// WHAT IT DOES NOT: the map's rules (NetworkMapTests in NetworkOps) or how it looks (the render test draws it).
/// A SAME-CLASS FAULT IT WOULD NOT CATCH: a switch whose port table the controller left empty shows no rows at all -- it is
/// listed, but its page reads "Nothing here." rather than saying the controller sent no ports.
/// </summary>
public sealed class NetworkOpsNetworkWindowTests
{
    private static string Fx(string name) => File.ReadAllText(Path.Combine(XamlStaticResourceOrderTests.GetRepoRoot(),
        "Kor.Operations.NetworkOps.Tests", "Fixtures", "netmap", name));

    private sealed record FleetRow(string Name, List<string> Macs, string? LastUser);

    internal static NetworkMapResponse RealMap() => Build(live: false);

    /// <summary>The same evening's map with the controller's LIVE read: ports up now, devices connected now.</summary>
    internal static NetworkMapResponse LiveMap() => Build(live: true);

    private static NetworkMapResponse Build(bool live)
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var fleet = JsonSerializer.Deserialize<List<FleetRow>>(Fx("fleet-2026-10-02.json"), web)!
            .Select(f => new FleetPc(f.Name, f.Macs, f.LastUser is { Length: > 0 } u ? u : null, f.LastUser is { Length: > 0 } ? "usual" : null)).ToList();
        var leases = JsonSerializer.Deserialize<List<DhcpLease>>(Fx("dhcp-2026-10-02.json"), web)!;
        var map = NetworkMaps.Build(UniFiSite.Parse(Fx("unifi-2026-10-02.json")), fleet, leases,
            new KnownNames(new Dictionary<string, string> { ["74:ac:b9:aa:84:a3"] = "Core switch" }, new Dictionary<string, string> { ["192.168.1.16"] = "ESXi host .16" }),
            live ? UniFiLive.Parse(Fx("unifi-live-2026-10-02.json")) : null);
        return new NetworkMapResponse(new DateTime(2026, 10, 3, 4, 25, 0, DateTimeKind.Utc), [], map);
    }

    [Fact]
    public void The_left_list_is_the_tree_then_the_access_points()
    {
        var m = new NetworkOpsNetworkModel(RealMap());
        var switches = m.Places.Where(p => p.Kind == "switch").ToList();
        Assert.Equal(10, switches.Count);
        Assert.Equal(["BMZ-SW01", "USW Flex Mini - Kate", "USW Flex Mini - Server Room PCs"], switches.Take(3).Select(p => p.Name));
        Assert.Equal(1, switches[0].Depth);
        Assert.Equal(2, switches[1].Depth);                                                   // indented under BMZ-SW01
        Assert.Equal(3, m.Places.Count(p => p.Kind == "ap"));
        Assert.True(m.Places.ToList().FindIndex(p => p.Kind == "ap") > m.Places.ToList().FindLastIndex(p => p.Kind == "switch"));
    }

    [Fact]
    public void The_firewall_card_maps_the_interfaces_and_the_health_line()
    {
        var fw = new FirewallRead("Firewall (Netgate pfSense)", "192.168.1.1", "pfSense Plus 25.07.1-RELEASE",
            [new FirewallInterface("WAN2", "igc3", "up", 1000, 12.3, 4.5, 0),
             new FirewallInterface("LAN", "igc2", "up", 1000, null, null, 0),
             new FirewallInterface("WAN", "igc0", "dormant", 10, null, null, 0)],
            CpuPct: 1, MemUsedPct: 35, StatesUsed: 2622, StatesLimit: 397000, UptimeHours: 136.6, ReadUtc: DateTime.UtcNow);
        var card = new NetworkOpsNetworkModel(RealMap() with { Firewall = fw }).Firewall;
        Assert.NotNull(card);
        Assert.Equal("Firewall (Netgate pfSense)", card!.Name);
        Assert.Equal("pfSense Plus 25.07.1-RELEASE · 192.168.1.1", card.Sub);
        Assert.Equal("CPU 1% · mem 35% · 2,622 of 397,000 states · up 5.7d", card.Health);
        Assert.Equal(["WAN2", "LAN", "WAN"], card.Interfaces.Select(i => i.Role));
        var wan2 = card.Interfaces[0];
        Assert.Equal(("igc3 · up", "1G", "12.3↓ 4.5↑ Mb/s"), (wan2.Detail, wan2.Speed, wan2.Flow));
        Assert.Equal("", card.Interfaces[1].Flow);        // LAN: no second read yet, so no throughput
        Assert.Equal("10M", card.Interfaces[2].Speed);    // the dormant standby WAN

        Assert.Null(new NetworkOpsNetworkModel(RealMap()).Firewall);   // no firewall in the response -> no card
    }

    [Fact]
    public void A_switch_page_is_every_port_with_each_desk_and_its_person()
    {
        var m = new NetworkOpsNetworkModel(RealMap());
        var sw01 = m.RowsOf(m.Places.First(p => p.Name == "BMZ-SW01"));
        Assert.Equal(Enumerable.Range(1, 52), sw01.Select(r => r.Port!.Value));                   // every port, in order
        var p5 = sw01.Single(r => r.Port == 5);
        Assert.Equal(("KOR-1001", "ilalonde"), (p5.Device, p5.Person));
        Assert.Contains("KOR-PERFORM3", p5.Also);                                                // the port's history
        Assert.StartsWith("↓ USW Flex Mini - Kate", sw01.Single(r => r.Port == 4).Device);
        Assert.StartsWith("↑ Core switch", sw01.Single(r => r.Port == 50).Device);
        Assert.Equal("ESXi host .16", sw01.Single(r => r.Port == 49).Device);
        Assert.Equal("—", sw01.Single(r => r.Port == 10).Device);                                // empty, said so
    }

    // Ian, 2026-10-02: "shows me the switches in small cards as ports with important info displayed and it's clickable which
    // brings you to the device page".
    [Fact]
    public void Each_switch_is_a_panel_of_every_port_and_a_tile_knows_where_it_leads()
    {
        var panels = new NetworkOpsNetworkModel(LiveMap()).Panels;
        Assert.Equal(10, panels.Count);
        var sw01 = panels.Single(p => p.Mac == "74:83:c2:13:f1:c2");
        Assert.Equal(Enumerable.Range(1, 52), sw01.Ports.Select(c => c.Number));
        var p2 = sw01.Ports.Single(c => c.Number == 2);
        Assert.Equal(("KOR-205", "on", "KOR-205"), (p2.Title, p2.State, p2.OpenName));            // a PC: opens its page
        var p5 = sw01.Ports.Single(c => c.Number == 5);
        Assert.Equal(("KOR-1001", "off"), (p5.Title, p5.State));                                   // on the VPN that evening
        var p4 = sw01.Ports.Single(c => c.Number == 4);
        Assert.Equal(("link", "74:ac:b9:a4:fc:fc"), (p4.State, p4.GoToSwitch));                    // a link: goes to Flex Mini - Kate
        var p49 = sw01.Ports.Single(c => c.Number == 49);
        Assert.Equal(("ESXi host .16", "10G"), (p49.OpenName, p49.Speed));                          // a rack device: its page
        Assert.Contains("Module: SFP-H10GB-CU1M", p49.Tip);
        Assert.True(sw01.Ports.Single(c => c.Number == 10).IsEmpty);
        Assert.Contains("ports up", sw01.Sub);

        var noLive = new NetworkOpsNetworkModel(RealMap()).Panels.Single(p => p.Mac == "74:83:c2:13:f1:c2").Ports.Single(c => c.Number == 2);
        Assert.Equal("known", noLive.State);                                                       // no live read: no claim about now
    }

    // Ian, 2026-10-02: "I do NOT want duplicate ways to see duplicated data I want duplicate ways to get into the same data."
    // A device's page and the Network surface each exist once; everything that shows a device or a switch goes there through
    // the one navigator. A second place building its own copy is a second way in. The Network surface was folded from a
    // standalone window into a console tab (2026-10-04): the navigator still owns the way in (OpenNetwork ->
    // INetworkTabHost.ShowNetwork), and the console is the single place that creates the view.
    [Fact]
    public void Only_one_place_creates_a_device_page_or_the_network_surface()
    {
        var dir = Path.Combine(XamlStaticResourceOrderTests.GetRepoRoot(), "Kor.Operations.App");
        var files = Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("Tests", StringComparison.Ordinal) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Select(f => (Name: Path.GetFileName(f), Text: File.ReadAllText(f)))
            .ToList();

        // The device page: created only by the navigator.
        var deviceOffenders = files
            .Where(f => f.Name != "NetworkOpsNavigator.cs" && f.Text.Contains("new NetworkOpsDeviceWindow(", StringComparison.Ordinal))
            .Select(f => f.Name).ToList();
        Assert.True(deviceOffenders.Count == 0, "creates a device page outside NetworkOpsNavigator: " + string.Join(", ", deviceOffenders));

        // The Network surface: created only by the console that hosts the tab; everything else reaches it through the navigator.
        var networkOffenders = files
            .Where(f => f.Name != "NetworkOpsCommandCenterWindow.xaml.cs" && f.Text.Contains("new NetworkOpsNetworkView(", StringComparison.Ordinal))
            .Select(f => f.Name).ToList();
        Assert.True(networkOffenders.Count == 0, "creates the Network surface outside its console host: " + string.Join(", ", networkOffenders));

        // The old standalone Network window is gone; nothing should resurrect it.
        var windowOffenders = files.Where(f => f.Text.Contains("new NetworkOpsNetworkWindow(", StringComparison.Ordinal)).Select(f => f.Name).ToList();
        Assert.True(windowOffenders.Count == 0, "the Network surface is a console tab now, not a window: " + string.Join(", ", windowOffenders));
    }

    [Fact]
    public void A_link_says_which_switch_and_which_port()
    {
        Assert.Equal(("74:83:c2:13:f1:c2", (int?)12), NetworkOpsNavigator.ParseOpens("network:74:83:c2:13:f1:c2#12"));
        Assert.Equal(("core", (int?)null), NetworkOpsNavigator.ParseOpens("network:core"));
        Assert.Equal(("", (int?)null), NetworkOpsNavigator.ParseOpens("network:"));
        Assert.Null(NetworkOpsNavigator.ParseOpens("something else"));
    }

    // "On KOR-SW01 · port 12" on a PC's own page: the map's answer, as a tile that opens the map at that port.
    [Fact]
    public void A_PCs_page_says_where_it_is_plugged_in_and_opens_that_port()
    {
        var now = DateTime.UtcNow;
        var dev = new Kor.Operations.NetworkOps.Core.Learning.DeviceRow(4, "KOR-208-N", now, now);
        var snap = new Kor.Operations.NetworkOps.Core.Learning.FleetSnapshot([dev], new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase), [], [], null);
        var vm = new NetworkOpsDeviceViewModel(NetworkOpsClient.Unconfigured("test"), snap, dev);
        vm.SetLastCheck(NetworkOpsComponentTilesTests.Kor208NCheck());
        vm.SetNetwork(LiveMap().Map);
        var net = vm.Components.Single(t => t.Part.Kind == "network");
        Assert.Equal(("USW Flex Mini - Server Room PCs · port 5", "connected now · 1G"), (net.Line1, net.Line2));
        Assert.Equal("network:74:83:c2:fb:d9:af#5", net.Part.Opens);
        Assert.Contains("Network window", net.ToolTip);
    }

    [Fact]
    public void A_search_finds_a_person_anywhere_and_says_where()
    {
        var m = new NetworkOpsNetworkModel(RealMap());
        var hit = Assert.Single(m.Search("markb"));
        Assert.Equal(("KOR-207", "USW Flex - Mark port 3"), (hit.Device, hit.Where));
        Assert.Empty(m.Search("   "));
        Assert.Contains(m.Search("192.168.1.16"), r => r.Device == "ESXi host .16");
        Assert.StartsWith("78 devices on 10 switches", m.Headline);
    }
}
