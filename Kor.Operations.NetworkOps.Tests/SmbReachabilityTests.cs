#nullable enable
using Kor.Operations.NetworkOps.Transport;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// Reachability must be "any address answers 445", not "the first address DNS returns
// answers": on 2026-09-28 the first-address version reported dual-homed KOR-218N offline all
// day while it was up. These run against loopback only; nothing leaves the machine.
public sealed class SmbReachabilityTests
{
    [Fact]
    public async Task An_unresolvable_host_is_unreachable_not_an_exception()
    {
        var r = await SmbReachability.ProbeAsync("kor-no-such-host.invalid", timeout: TimeSpan.FromSeconds(2));
        Assert.False(r.Reachable);
        Assert.Null(r.AnsweredOn);
    }

    [Fact]
    public async Task A_listening_port_on_any_address_is_reachable()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var r = await SmbReachability.ProbeAsync("localhost", port, TimeSpan.FromSeconds(3));
            Assert.True(r.Reachable);
            Assert.Equal(System.Net.IPAddress.Loopback, r.AnsweredOn);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task A_closed_port_is_unreachable()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();   // now nothing listens there
        var r = await SmbReachability.ProbeAsync("localhost", port, TimeSpan.FromSeconds(2));
        Assert.False(r.Reachable);
    }
}
