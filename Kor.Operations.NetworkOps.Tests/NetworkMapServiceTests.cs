#nullable enable
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Network;
using Kor.Operations.NetworkOps.Service.Network;
using Kor.Operations.NetworkOps.Service.Store;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The service's two rules around the port map: reading DC01's leases as the remote channel returns them, and naming a PC's
// person when most people are signed out (13 of 32 had anyone signed in at 21:00 on 2026-10-02).
//
// WHAT IT COVERS: the probe's lease list read bare or wrapped in another array, the real 107 leases of 2026-10-02 all read,
// a lease with no MAC or IP skipped; the usual person is the most often signed in, a built-in Administrator only when
// nobody else ever is, and with no history whoever is on now. Link findings: every device the store holds open is diffed
// even when nothing raises it now (so it clears), and one device under two names is diffed once with what it raises.
// WHAT IT DOES NOT: the SQL that counts sign-ins (JSON_VALUE over both payload shapes) -- read live after deploy, 2026-10-02;
// the SQL behind DevicesWithOpenFindingAsync and the name -> DeviceId lookup -- read live after deploy (the open link-fault
// count goes from 23 to what the current rule raises).
// A SAME-CLASS FAULT IT WOULD NOT CATCH: a shared PC used by two people equally gets whichever was on most recently, with
// no hint that it is shared; a link finding on a RETIRED device stays open (the store query skips retired devices).
public sealed class NetworkMapServiceTests
{
    [Fact]
    public void The_leases_are_read_bare_or_wrapped()
    {
        var bare = """[{"Ip":"192.168.1.51","Mac":"d8-bb-c1-2c-ea-09","HostName":"KOR-101.int.korstructural.com","State":"Active","Expires":"2026-10-08T08:49:24Z"},{"Ip":"","Mac":"x"}]""";
        var one = Assert.Single(NetworkMapService.ParseLeases(bare));
        Assert.Equal("KOR-101.int.korstructural.com", one.HostName);
        Assert.Single(NetworkMapService.ParseLeases("[" + bare + "]"));

        var real = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "netmap", "dhcp-2026-10-02.json"));
        Assert.Equal(107, NetworkMapService.ParseLeases(real).Count);
    }

    // Ian, 2026-10-02: "is there a way to change the names of the devices from BMZ to KOR without breaking things?"
    [Fact]
    public void Renaming_a_switch_is_not_a_move_but_changing_port_or_switch_is()
    {
        var sw01 = "74:83:c2:13:f1:c2";
        Assert.False(NetworkOpsStore.IsMove(new("port", "BMZ-SW01", 5, sw01), new("port", "KOR-SW01", 5, sw01)));   // renamed
        Assert.True(NetworkOpsStore.IsMove(new("port", "BMZ-SW01", 25, sw01), new("port", "BMZ-SW01", 5, sw01)));    // KOR-1001, 09-29
        Assert.True(NetworkOpsStore.IsMove(new("port", "BMZ-SW01", 5, sw01), new("port", "BMZ-SW02", 5, "e0:63:da:8a:30:d9")));
        Assert.True(NetworkOpsStore.IsMove(new("port", "BMZ-SW01", 5, null), new("port", "KOR-SW01", 5, sw01)));      // a pre-011 row: by name
        Assert.False(NetworkOpsStore.IsMove(new("also-seen", "BMZ-SW01", 5, sw01), new("port", "BMZ-SW01", 7, sw01))); // history is not a move
    }

    // The live API's device records carry the device's auth key and SSH host key: only named fields may leave UniFiApi.
    [Fact]
    public void The_live_read_keeps_only_named_fields_and_reads_back_as_the_live_shape()
    {
        var devices = System.Text.Json.Nodes.JsonNode.Parse("""
            [{"mac":"74:83:c2:13:f1:c2","name":"KOR-SW01","state":1,"uptime":5,"x_authkey":"SECRET-A","x_ssh_hostkey":"SECRET-B",
              "port_table":[{"port_idx":49,"up":true,"speed":10000,"poe_enable":false,"media":"SFP+","sfp_found":true,"sfp_part":"SFP-H10GB-CU1M","name":"SFP+ 1"},
                            {"port_idx":7,"up":false,"speed":0,"poe_enable":true,"poe_power":"3.21","media":"GE","sfp_found":false,"sfp_part":"x"}]}]
            """)!.AsArray();
        var clients = System.Text.Json.Nodes.JsonNode.Parse("""
            [{"mac":"E8:97:44:09:5C:00","ip":"192.168.1.112","hostname":"KOR-1001","is_wired":true,"sw_mac":"74:83:c2:13:f1:c2","sw_port":5,"uptime":600,"x_password":"SECRET-C"}]
            """)!.AsArray();
        var json = UniFiApiProject(devices, clients);
        Assert.DoesNotContain("SECRET", json);
        var live = Kor.Operations.NetworkOps.Core.Network.UniFiLive.Parse(json);
        var p49 = live.Devices.Single().Ports.Single(p => p.Port == 49);
        Assert.Equal((true, 10000, "SFP-H10GB-CU1M"), (p49.Up, p49.Speed, p49.Sfp));
        Assert.Null(live.Devices.Single().Ports.Single(p => p.Port == 7).Sfp);           // no module: no part, whatever the field holds
        Assert.Equal(3.21, live.Devices.Single().Ports.Single(p => p.Port == 7).PoeW);   // the API sends PoE watts as a string
        var c = live.Clients.Single();
        Assert.Equal(("e8:97:44:09:5c:00", "74:83:c2:13:f1:c2", 5), (c.Mac, c.SwMac, c.SwPort!.Value));
    }

    private static string UniFiApiProject(System.Text.Json.Nodes.JsonArray d, System.Text.Json.Nodes.JsonArray c)
        => Kor.Operations.NetworkOps.Service.Rack.UniFiApi.Project(d, c, 1_790_999_999).ToJsonString();

    [Fact]
    public void The_usual_person_is_the_one_most_often_signed_in()
    {
        var t = new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
        Assert.Equal(("kor\\markb", "usual"), NetworkOpsStore.UsualUser([("kor\\markb", 40, t), ("kor\\Administrator", 60, t.AddHours(5)), ("kor\\ilalonde", 3, t.AddHours(6))], null));
        Assert.Equal(("kor\\Administrator", "usual"), NetworkOpsStore.UsualUser([("kor\\Administrator", 2, t)], null));    // nobody else ever
        Assert.Equal(("kor\\gdow", "signed in now"), NetworkOpsStore.UsualUser(null, "kor\\gdow"));
        Assert.Equal((null, null), NetworkOpsStore.UsualUser([], ""));
    }

    // 2026-10-05: 23 link-fault findings raised by 0.32's dropped-packet rule were still open two days after 0.33.1 retired
    // it -- the refresh only revisited devices it remembered raising, and a restart forgot them all.
    [Fact]
    public void A_link_finding_open_in_the_store_is_revisited_even_when_nothing_raises_it()
    {
        var fault = new Finding(NetworkFindings.LinkRule, Severity.Warning, "Network link fault", "KOR-SW01 port 4: the link is HALF-DUPLEX");
        var targets = NetworkMapService.LinkFindingTargets([(7, fault)], [12, 7]);

        Assert.Equal(2, targets.Count);
        Assert.Equal(fault, targets.Single(t => t.DeviceId == 7).Finding);   // raised now AND open: kept, not cleared
        Assert.Null(targets.Single(t => t.DeviceId == 12).Finding);          // open, raised by nothing now: cleared
        Assert.Empty(NetworkMapService.LinkFindingTargets([], []));
    }

    [Fact]
    public void A_device_seen_under_two_names_is_diffed_once_with_what_it_raises()
    {
        // NAS01 on the map is "NAS01 (Veeam repository)" in the store; both resolve to one DeviceId. Diffing once per name
        // would clear under one name what the other just raised, every refresh.
        var fault = new Finding(NetworkFindings.LinkRule, Severity.Info, "Network link degraded", "KOR-SW01 port 30: UniFi rates this port's experience 80%");
        var one = Assert.Single(NetworkMapService.LinkFindingTargets([(31, fault)], [31]));
        Assert.Equal((31, fault), (one.DeviceId, one.Finding));
    }
}
