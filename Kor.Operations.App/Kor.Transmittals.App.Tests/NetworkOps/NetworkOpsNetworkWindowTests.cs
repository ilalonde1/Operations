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

    internal static NetworkMapResponse RealMap()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var fleet = JsonSerializer.Deserialize<List<FleetRow>>(Fx("fleet-2026-10-02.json"), web)!
            .Select(f => new FleetPc(f.Name, f.Macs, f.LastUser is { Length: > 0 } u ? u : null, f.LastUser is { Length: > 0 } ? "usual" : null)).ToList();
        var leases = JsonSerializer.Deserialize<List<DhcpLease>>(Fx("dhcp-2026-10-02.json"), web)!;
        var map = NetworkMaps.Build(UniFiSite.Parse(Fx("unifi-2026-10-02.json")), fleet, leases,
            new KnownNames(new Dictionary<string, string> { ["74:ac:b9:aa:84:a3"] = "Core switch" }, new Dictionary<string, string> { ["192.168.1.16"] = "ESXi host .16" }));
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
