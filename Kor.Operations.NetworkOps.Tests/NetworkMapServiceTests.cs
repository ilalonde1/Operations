#nullable enable
using Kor.Operations.NetworkOps.Service.Network;
using Kor.Operations.NetworkOps.Service.Store;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The service's two rules around the port map: reading DC01's leases as the remote channel returns them, and naming a PC's
// person when most people are signed out (13 of 32 had anyone signed in at 21:00 on 2026-10-02).
//
// WHAT IT COVERS: the probe's lease list read bare or wrapped in another array, the real 107 leases of 2026-10-02 all read,
// a lease with no MAC or IP skipped; the usual person is the most often signed in, a built-in Administrator only when
// nobody else ever is, and with no history whoever is on now.
// WHAT IT DOES NOT: the SQL that counts sign-ins (JSON_VALUE over both payload shapes) -- read live after deploy, 2026-10-02.
// A SAME-CLASS FAULT IT WOULD NOT CATCH: a shared PC used by two people equally gets whichever was on most recently, with
// no hint that it is shared.
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

    [Fact]
    public void The_usual_person_is_the_one_most_often_signed_in()
    {
        var t = new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
        Assert.Equal(("kor\\markb", "usual"), NetworkOpsStore.UsualUser([("kor\\markb", 40, t), ("kor\\Administrator", 60, t.AddHours(5)), ("kor\\ilalonde", 3, t.AddHours(6))], null));
        Assert.Equal(("kor\\Administrator", "usual"), NetworkOpsStore.UsualUser([("kor\\Administrator", 2, t)], null));    // nobody else ever
        Assert.Equal(("kor\\gdow", "signed in now"), NetworkOpsStore.UsualUser(null, "kor\\gdow"));
        Assert.Equal((null, null), NetworkOpsStore.UsualUser([], ""));
    }
}
