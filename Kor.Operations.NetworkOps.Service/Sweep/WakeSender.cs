#nullable enable
using System.Net;
using System.Net.Sockets;
using Kor.Operations.NetworkOps.Core.Actions;

namespace Kor.Operations.NetworkOps.Service.Sweep;

// Sends a magic packet from APP01, which sits on the office LAN (192.168.1.0/24) with the PCs, so a broadcast reaches
// them. Three bursts, to UDP 9 and 7, both to the limited broadcast and to the subnet's: a switch that has aged out a
// sleeping PC's MAC floods broadcasts, and some NICs only listen on one port.
internal static class WakeSender
{
    public static async Task SendAsync(byte[] mac, IReadOnlyList<IPAddress> broadcasts, CancellationToken ct)
    {
        var packet = MagicPacket.Build(mac);
        using var udp = new UdpClient { EnableBroadcast = true };
        for (var burst = 0; burst < 3; burst++)
        {
            foreach (var to in broadcasts)
                foreach (var port in new[] { 9, 7 })
                    await udp.SendAsync(packet, new IPEndPoint(to, port), ct).ConfigureAwait(false);
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
    }
}
