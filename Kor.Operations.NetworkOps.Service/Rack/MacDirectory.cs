#nullable enable
using System.Collections.Concurrent;
using System.Net;
using Kor.Operations.NetworkOps.Core.Rack;
using Kor.Operations.NetworkOps.Service.Store;
using Kor.Operations.NetworkOps.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service.Rack;

// Names a MAC the core switch has learned on a port (Ian, 2026-10-02: "which IP / device name is attached to each port"):
//   1. a UniFi device NetworkOps knows by MAC (fact unifi.device:{mac})    -> "UniFi BMZ-AP01 [Outside Boardroom]"
//   2. else APP01's ARP table gives its IP, and the IP is named by          -> "KOR-FS01 (192.168.1.20)"
//      a rack device's configured address, else reverse DNS (cached 1 h)
//   3. else the bare IP, else nothing (the rule shows the MAC).
// Read on APP01, where the service runs: it talks to every server, NAS, host and switch every 5 minutes, so the rack is in
// its ARP table. A PC behind a UniFi switch is mostly not -- its port shows as that switch "+N more", which is the truth.
internal sealed class MacDirectory(NetworkOpsStore store, IOptions<NetworkOpsOptions> options, ILogger<MacDirectory> log)
{
    private static readonly TimeSpan DnsFor = TimeSpan.FromHours(1);
    private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(2);
    private readonly ConcurrentDictionary<string, (string? Name, DateTime AtUtc)> _dns = new(StringComparer.Ordinal);

    /// <summary>A labeller for one sweep: every lookup it needs is done up front (ARP, the rack's facts, DNS for the ARP IPs).</summary>
    public async Task<Func<string, string?>> LabellerAsync(CancellationToken ct)
    {
        IReadOnlyDictionary<string, string> arp;
        try { arp = ArpTable.Read(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "APP01's ARP table could not be read"); arp = new Dictionary<string, string>(); }

        var unifi = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var rack = await store.FleetSnapshotAsync(ct, rack: true).ConfigureAwait(false);
            foreach (var facts in rack.FactsByDevice.Values)
                foreach (var (key, value) in facts)
                    if (key.StartsWith("unifi.device:", StringComparison.Ordinal) && UniFiDevice.Parse(value) is { } d)
                        unifi[key["unifi.device:".Length..]] = $"UniFi {d.Label}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "UniFi device names could not be read for the port map"); }

        // A rack device configured by IP is named by its own name ("KOR-MESH01 (remote control, MeshCentral)" -> "KOR-MESH01").
        var rackByIp = options.Value.Rack.Where(r => IPAddress.TryParse(r.Address, out _))
            .GroupBy(r => r.Address).ToDictionary(g => g.Key, g => Short(g.First().Name), StringComparer.Ordinal);
        // APP01's default gateway is the firewall (192.168.1.1, the Netgate): it has no DNS name to be found by.
        foreach (var gw in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                     .SelectMany(n => n.GetIPProperties().GatewayAddresses).Select(g => g.Address)
                     .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !a.Equals(IPAddress.Any)))
            rackByIp.TryAdd(gw.ToString(), "Firewall (gateway)");

        var names = new ConcurrentDictionary<string, string?>(StringComparer.Ordinal);
        await Parallel.ForEachAsync(arp.Values.Distinct(), new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (ip, c) =>
            names[ip] = rackByIp.TryGetValue(ip, out var r) ? r : await ReverseDnsAsync(ip, c).ConfigureAwait(false));

        return mac =>
        {
            if (unifi.TryGetValue(mac, out var u)) return u;
            if (!arp.TryGetValue(mac, out var ip)) return null;
            return names.TryGetValue(ip, out var n) && n is { Length: > 0 } ? $"{n} ({ip})" : ip;
        };
    }

    private async Task<string?> ReverseDnsAsync(string ip, CancellationToken ct)
    {
        if (_dns.TryGetValue(ip, out var hit) && DateTime.UtcNow - hit.AtUtc < DnsFor) return hit.Name;
        string? name = null;
        try
        {
            using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cap.CancelAfter(DnsTimeout);
            var entry = await Dns.GetHostEntryAsync(ip, cap.Token).ConfigureAwait(false);
            name = entry.HostName is { Length: > 0 } h && h != ip ? h.Split('.')[0].ToUpperInvariant() : null;
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or OperationCanceledException && !ct.IsCancellationRequested) { name = null; }
        _dns[ip] = (name, DateTime.UtcNow);
        return name;
    }

    private static string Short(string rackName) => rackName.IndexOf(" (", StringComparison.Ordinal) is var i and > 0 ? rackName[..i] : rackName;
}
