#nullable enable
using System.Net;
using System.Net.Sockets;

namespace Kor.Operations.NetworkOps.Transport;

// Reachability on this network is TCP 445, never ICMP: EDMONTON-01 drops ping while serving
// SMB, and a ping gate reports a running workstation as offline (2026-08-13).
//
// Every A record is tried at once. The dual-homed boxes (KOR-218N, EDMONTON-01, KOR-210,
// SPARE9) register an address on the isolated Perform net 192.168.55.x as well as their office
// one, and DNS may hand the .55 address out first. A single connect to the host NAME spends
// the whole timeout on it: that reported KOR-218N "offline" all of 2026-09-28 while it was up
// on 192.168.1.45 and Ian was connected to it.
public static class SmbReachability
{
    public const int SmbPort = 445;

    public static async Task<ReachabilityResult> ProbeAsync(string host, int port = SmbPort, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(3);
        IPAddress[] addrs;
        try
        {
            addrs = (await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false))
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToArray();
        }
        catch (SocketException)
        {
            return new ReachabilityResult(host, false, null, Array.Empty<IPAddress>());
        }
        if (addrs.Length == 0) return new ReachabilityResult(host, false, null, addrs);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(limit);
        var attempts = addrs.Select(a => TryConnectAsync(a, port, cts.Token)).ToList();
        while (attempts.Count > 0)
        {
            var done = await Task.WhenAny(attempts).ConfigureAwait(false);
            attempts.Remove(done);
            var hit = await done.ConfigureAwait(false);
            if (hit is not null)
            {
                cts.Cancel();   // stop the slower attempts
                return new ReachabilityResult(host, true, hit, addrs);
            }
        }
        return new ReachabilityResult(host, false, null, addrs);
    }

    private static async Task<IPAddress?> TryConnectAsync(IPAddress address, int port, CancellationToken ct)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            await client.ConnectAsync(address, port, ct).ConfigureAwait(false);
            return client.Connected ? address : null;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return null;
        }
    }
}

/// <param name="AnsweredOn">The address that accepted the connection -- the one to trust when a host is dual-homed.</param>
public sealed record ReachabilityResult(string Host, bool Reachable, IPAddress? AnsweredOn, IReadOnlyList<IPAddress> Resolved);
