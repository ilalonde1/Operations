#nullable enable
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.Rack;

namespace Kor.Operations.NetworkOps.Core.Network;

/// <summary>
/// What is plugged in where, by name and person (Ian, 2026-10-02: "I want it so I can see which DEVICES by name (and user)
/// ... see it clearly and then once it's perfected, be able to create documentation from it").
///
/// THE RULES, each in one place:
///   1 TREE      Every UniFi device hangs from its parent's MAC, at the parent's port, through its own uplink port. The
///               root is the core switch (its MAC is a known name). Switches are listed parent first.
///   2 OCCUPANT  Who is on a port is the SWITCH'S word: the last device it saw connect there (its port table). Only where
///               it recorded none, the most recently seen client the controller placed on that port. A UniFi device on a
///               port makes the port a link (down to it, or up to the parent).
///   3 ONE PLACE A device is AT one port at most: the one it most recently connected to. Every other client the controller
///               recorded on a port is listed there as "also seen" -- a desk's history, or devices beyond a link -- so no
///               client is dropped: each of the controller's clients is an occupant, also seen, wireless, or unplaced,
///               exactly once.
///   4 NAME      fleet PC (our agent's MACs) > UniFi device > a known name (rack) > DHCP host name > controller host name >
///               maker > the MAC. Every endpoint says which.
///   5 USER      the fleet PC's person, as the caller found it (usual / signed in now / last signed in).
/// The controller's "last seen" is shown as a date, never as "online": how often it writes it is not established (on
/// 2026-10-02 every client read 23 h or older at 21:00 -- after hours, and KOR-1001 was on the VPN, not a switch port, so
/// that evening proves nothing either way). "Online" needs a live source: the agent's connection, a current lease.
/// </summary>
public static class NetworkMaps
{
    public static NetworkMap Build(UniFiSite site, IReadOnlyList<FleetPc> fleet, IReadOnlyList<DhcpLease> leases, KnownNames known)
    {
        var devices = site.Devices.ToDictionary(d => d.Mac, StringComparer.Ordinal);
        var clients = site.Clients.GroupBy(c => c.Mac).ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.LastSeen).First(), StringComparer.Ordinal);
        var pcByMac = new Dictionary<string, FleetPc>(StringComparer.Ordinal);
        foreach (var pc in fleet) foreach (var m in pc.Macs) pcByMac.TryAdd(Mac(m), pc);
        var leaseByMac = leases.GroupBy(l => Mac(l.Mac)).ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.State == "Active").First(), StringComparer.Ordinal);

        NetEndpoint Describe(string mac, string? ip, long seen)
        {
            pcByMac.TryGetValue(mac, out var pc);
            devices.TryGetValue(mac, out var dev);
            clients.TryGetValue(mac, out var cl);
            leaseByMac.TryGetValue(mac, out var lease);
            ip = ip is { Length: > 0 } ? ip : cl?.Ip is { Length: > 0 } cip ? cip : dev?.Ip is { Length: > 0 } dip ? dip : lease?.Ip;
            var maker = EdgeSwitchRules.MakerOf(mac) ?? (cl?.Maker is { Length: > 0 } mk ? mk : null);
            var (name, source) =
                pc is not null ? (pc.Name, "NetworkOps agent")
                : dev is not null ? (dev.Label, "UniFi device")
                : known.ByMac.TryGetValue(mac, out var km) ? (km, "rack")
                : ip is not null && known.ByIp.TryGetValue(ip, out var ki) ? (ki, "rack")
                : lease?.HostName is { Length: > 0 } hn ? (Short(hn), "DHCP")
                : cl?.Hostname is { Length: > 0 } ch ? (ch, "UniFi client")
                : maker is not null ? ($"{maker} device", "maker")
                : (mac, "MAC only");
            return new NetEndpoint(mac, name, source, ip, pc?.Name, pc?.User, pc?.UserSource, maker,
                seen > 0 ? DateTimeOffset.FromUnixTimeSeconds(seen).UtcDateTime : null, lease?.State, dev?.Kind);
        }

        // 1 TREE: parent first, from the root (the core switch, or any device whose parent the controller does not manage).
        var switches = site.Devices.Where(d => d.Type == "usw").ToList();
        var children = site.Devices.Where(d => d.Uplink?.Mac is not null).ToLookup(d => d.Uplink!.Mac!, StringComparer.Ordinal);
        var ordered = new List<(UniFiDev Dev, int Depth)>();
        void Walk(UniFiDev d, int depth)
        {
            if (ordered.Any(o => o.Dev.Mac == d.Mac)) return;
            ordered.Add((d, depth));
            foreach (var c in children[d.Mac].Where(c => c.Type == "usw").OrderBy(c => c.Uplink!.Port ?? 0).ThenBy(c => c.Label, StringComparer.OrdinalIgnoreCase))
                Walk(c, depth + 1);
        }
        foreach (var top in switches.Where(s => s.Uplink?.Mac is not { } p || !devices.ContainsKey(p)).OrderBy(s => s.Label, StringComparer.OrdinalIgnoreCase))
            Walk(top, 1);
        foreach (var s in switches) Walk(s, 1);   // a loop or a dangling parent still lists every switch

        // 2+3 OCCUPANT, ONE PLACE: each MAC sits at the port it most recently connected to, by the switches' own word.
        // A switch's own uplink port holds its parent, which is on every child's uplink (the core switch on BMZ-SW01's AND
        // SW02's): that is not a place the parent IS, so uplink ports claim nothing.
        bool IsUplink(UniFiDev s, UniFiPort p) => s.Uplink?.LocalPort == p.Port || (p.LastMac is not null && s.Uplink?.Mac == p.LastMac);
        var claims = switches.SelectMany(s => s.PortTable.Where(p => p.LastMac is not null && !IsUplink(s, p)).Select(p => (Sw: s.Mac, p.Port, Mac: p.LastMac!, p.ConnectedAt)))
            .GroupBy(c => c.Mac).ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.ConnectedAt).First(), StringComparer.Ordinal);
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var portsBySwitch = new Dictionary<string, List<NetPort>>(StringComparer.Ordinal);
        // Where each UniFi device hangs is the tree's fact (rule 1), stronger than a port's last-seen: that port is a link,
        // the child is on it, and whatever the switch last recorded there is kept as also seen.
        var childAt = site.Devices.Where(d => d.Uplink is { Mac: not null, Port: not null })
            .GroupBy(d => (d.Uplink!.Mac!, d.Uplink.Port!.Value)).ToDictionary(g => g.Key, g => g.First());
        foreach (var (s, _) in ordered)
        {
            var ports = new List<NetPort>();
            foreach (var p in s.PortTable.OrderBy(p => p.Port))
            {
                string? mac = null; string? ip = null; long seen = 0;
                var connected = false;
                var displaced = new List<NetEndpoint>();
                if (IsUplink(s, p))
                {
                    mac = s.Uplink?.Mac ?? p.LastMac;
                    ip = p.LastMac == mac ? p.LastIp : null;
                }
                else if (childAt.TryGetValue((s.Mac, p.Port), out var child))
                {
                    mac = child.Mac; ip = child.Ip;
                    if (p.LastMac is { } other && other != child.Mac && claims.TryGetValue(other, out var w) && w.Sw == s.Mac && w.Port == p.Port)
                    {
                        placed.Add(other);
                        displaced.Add(Describe(other, p.LastIp, p.ConnectedAt) with { SeenIsConnected = true });
                    }
                }
                else if (p.LastMac is { } lm && claims.TryGetValue(lm, out var won) && won.Sw == s.Mac && won.Port == p.Port) { mac = lm; ip = p.LastIp; seen = p.ConnectedAt; connected = true; }
                else if (p.LastMac is null)
                {
                    // The switch recorded no one: the controller's most recent client on this port, if it is not placed by a switch elsewhere.
                    var c = site.Clients.Where(c => c.Wired && c.UplinkMac == s.Mac && c.Port == p.Port && !claims.ContainsKey(c.Mac)).OrderByDescending(c => c.LastSeen).FirstOrDefault();
                    if (c is not null) { mac = c.Mac; ip = c.Ip; seen = c.LastSeen; }
                }
                var kind = IsUplink(s, p) ? "uplink"
                    : childAt.ContainsKey((s.Mac, p.Port)) ? "link"
                    : mac is null ? "empty"
                    : "device";
                if (mac is not null && kind != "uplink") placed.Add(mac);
                ports.Add(new NetPort(p.Port, p.Speed, p.Poe, kind, mac is null ? null : Describe(mac, ip, seen) with { SeenIsConnected = connected }, displaced));
            }
            portsBySwitch[s.Mac] = ports;
        }

        // Every other wired client the controller recorded on a port: "also seen" there (history, or beyond a link).
        var wireless = new List<NetEndpoint>();
        var unplaced = new List<NetEndpoint>();
        // The tree's members are not endpoints: the UniFi devices, and the parents their uplinks lead to (the core switch).
        var tree = site.Devices.Select(d => d.Mac).Concat(site.Devices.Select(d => d.Uplink?.Mac).OfType<string>()).ToHashSet(StringComparer.Ordinal);
        foreach (var c in clients.Values.OrderBy(c => c.Mac, StringComparer.Ordinal))
        {
            if (placed.Contains(c.Mac) || tree.Contains(c.Mac)) continue;
            if (!c.Wired) { wireless.Add(Describe(c.Mac, c.Ip, c.LastSeen) with { Via = c.UplinkName }); continue; }
            if (c.UplinkMac is { } up && c.Port is { } port && portsBySwitch.TryGetValue(up, out var ports) && ports.FindIndex(x => x.Number == port) is var i and >= 0)
                ports[i] = ports[i] with { AlsoSeen = [.. ports[i].AlsoSeen, Describe(c.Mac, c.Ip, c.LastSeen)] };
            else unplaced.Add(Describe(c.Mac, c.Ip, c.LastSeen) with { Via = c.UplinkName });
        }

        var list = ordered.Select(o => new NetSwitch(o.Dev.Mac, o.Dev.Label, o.Dev.Model, o.Dev.Ip,
                o.Dev.Uplink?.Mac, o.Dev.Uplink?.Mac is { } pm ? (devices.TryGetValue(pm, out var pd) ? pd.Label : known.ByMac.GetValueOrDefault(pm) ?? pm) : null,
                o.Dev.Uplink?.Port, o.Dev.Uplink?.LocalPort, o.Depth, portsBySwitch[o.Dev.Mac]))
            .ToList();
        var aps = site.Devices.Where(d => d.Type == "uap").OrderBy(d => d.Label, StringComparer.OrdinalIgnoreCase)
            .Select(d => new NetAccessPoint(d.Mac, d.Label, d.Model, d.Ip, wireless.Where(w => w.Via == d.Label).OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();
        return new NetworkMap(DateTimeOffset.FromUnixTimeSeconds(site.Now).UtcDateTime, list, aps,
            wireless.Where(w => !aps.Any(a => a.Name == w.Via)).ToList(), unplaced);
    }

    /// <summary>aa-BB-cc... / AA:BB:... -> aa:bb:...: the one spelling every source is compared in.</summary>
    public static string Mac(string? mac) => (mac ?? "").Trim().ToLowerInvariant().Replace('-', ':');

    // "KOR-101.int.korstructural.com" -> "KOR-101"
    private static string Short(string host) => host.Split('.')[0];
}

/// <summary>Names the caller knows by MAC or IP: the rack (the core switch by its MAC, servers by address).</summary>
public sealed record KnownNames(IReadOnlyDictionary<string, string> ByMac, IReadOnlyDictionary<string, string> ByIp)
{
    public static readonly KnownNames None = new(new Dictionary<string, string>(), new Dictionary<string, string>());
}

/// <summary>A fleet PC as the port map needs it: its network cards' MACs and its person.</summary>
/// <param name="UserSource">"usual" (most often signed in, from stored checks), "signed in now", "last signed in", or null.</param>
public sealed record FleetPc(string Name, IReadOnlyList<string> Macs, string? User, string? UserSource);

/// <summary>A DHCP lease (DC01): Mac in any spelling.</summary>
public sealed record DhcpLease(string Ip, string Mac, string? HostName, string State, DateTime? Expires);

public sealed record NetworkMap(DateTime ReadUtc, IReadOnlyList<NetSwitch> Switches, IReadOnlyList<NetAccessPoint> AccessPoints,
    IReadOnlyList<NetEndpoint> OtherWireless, IReadOnlyList<NetEndpoint> Unplaced)
{
    /// <summary>Every endpoint once, where it is (rule 3): occupants, also-seen, wireless, unplaced. The ONE walk -- the store's
    /// rows, the documentation and the gate all read this.</summary>
    public IEnumerable<NetPlacement> Everything()
    {
        foreach (var s in Switches)
            foreach (var p in s.Ports)
            {
                // Every occupant that is not part of the tree (a UniFi device, or the parent an uplink leads to): a desk's PC.
                if (p.On is { UniFiKind: null } on && p.Kind != "uplink") yield return new(on, "port", s.Name, p.Number);
                foreach (var a in p.AlsoSeen) yield return new(a, "also-seen", s.Name, p.Number);
            }
        foreach (var ap in AccessPoints) foreach (var w in ap.Clients) yield return new(w, "wireless", ap.Name, null);
        foreach (var w in OtherWireless) yield return new(w, "wireless", w.Via, null);
        foreach (var u in Unplaced) yield return new(u, "unplaced", u.Via is { Length: > 0 } v ? v : null, null);
    }

    /// <summary>Where a fleet PC is plugged in, or null.</summary>
    public (NetSwitch Switch, NetPort Port)? PortOf(string pcName)
    {
        foreach (var s in Switches)
            foreach (var p in s.Ports)
                if (p.Kind == "device" && p.On?.Pc is { } pc && pc.Equals(pcName, StringComparison.OrdinalIgnoreCase)) return (s, p);
        return null;
    }
}

/// <param name="Placement">port | also-seen | wireless | unplaced</param>
/// <param name="Switch">The switch (port, also-seen), access point (wireless), or what the controller last named (unplaced).</param>
public sealed record NetPlacement(NetEndpoint Endpoint, string Placement, string? Switch, int? Port)
{
    public string Where => Placement switch
    {
        "port" => $"{Switch} port {Port}",
        "also-seen" => $"{Switch} port {Port} (also seen)",
        "wireless" => $"{Switch} (wireless)",
        _ => Switch is null ? "unplaced" : $"unplaced (last on {Switch})",
    };
}

/// <param name="Depth">1 = hangs from the core (or a device the controller does not manage); 2 = from one of those; ...</param>
public sealed record NetSwitch(string Mac, string Name, string Model, string? Ip, string? ParentMac, string? ParentName, int? ParentPort,
    int? UplinkPort, int Depth, IReadOnlyList<NetPort> Ports);

/// <param name="Kind">device | link (a UniFi device hangs here) | uplink (toward the parent) | empty</param>
/// <param name="AlsoSeen">Other clients the controller recorded on this port: the desk's history, or devices beyond a link.</param>
public sealed record NetPort(int Number, int SpeedMbps, bool Poe, string Kind, NetEndpoint? On, IReadOnlyList<NetEndpoint> AlsoSeen);

public sealed record NetAccessPoint(string Mac, string Name, string Model, string? Ip, IReadOnlyList<NetEndpoint> Clients);

/// <param name="NameSource">NetworkOps agent | UniFi device | rack | DHCP | UniFi client | maker | MAC only</param>
/// <param name="SeenUtc">When the switch saw it connect, or the controller last recorded it -- a date, not "online".</param>
/// <param name="Lease">DC01's lease state for it (Active, ...), or null.</param>
/// <param name="UniFiKind">For a UniFi device: Switch / Access point / Gateway.</param>
/// <param name="Via">Wireless or unplaced: the access point or uplink the controller named.</param>
/// <param name="SeenIsConnected">SeenUtc is when the SWITCH saw it connect (connected since), not when the controller last
/// recorded it: ESXi host .16's link on BMZ-SW01 port 49 read "seen 2024-09-26" -- up since then, not gone since then.</param>
public sealed record NetEndpoint(string Mac, string Name, string NameSource, string? Ip, string? Pc, string? User, string? UserSource,
    string? Maker, DateTime? SeenUtc, string? Lease, string? UniFiKind, string? Via = null, bool SeenIsConnected = false);

// ---- the controller's read (kor-unifi-status, 2026-10-02 shape) ----

public sealed record UniFiSite(long Now, IReadOnlyList<UniFiDev> Devices, IReadOnlyList<UniFiClient> Clients)
{
    public static UniFiSite Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        static string? S(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static long L(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
        static int? I(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
        static bool B(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.True;

        var devices = r.GetProperty("devices").EnumerateArray().Select(d => new UniFiDev(
            NetworkMaps.Mac(S(d, "mac")), S(d, "name") ?? "", S(d, "model") ?? "", S(d, "type") ?? "", S(d, "ip"),
            d.TryGetProperty("uplink", out var u) && u.ValueKind == JsonValueKind.Object
                ? new UniFiUplink(S(u, "mac") is { } um ? NetworkMaps.Mac(um) : null, I(u, "port"), I(u, "localPort")) : null,
            d.TryGetProperty("portTable", out var pt) && pt.ValueKind == JsonValueKind.Array
                ? pt.EnumerateArray().Select(p => new UniFiPort(I(p, "port") ?? 0, I(p, "speed") ?? 0, B(p, "poe"),
                    S(p, "lastMac") is { } lm ? NetworkMaps.Mac(lm) : null, S(p, "lastIp"), L(p, "connectedAt"))).ToList()
                : [])).ToList();
        var clients = r.TryGetProperty("clients", out var cs) && cs.ValueKind == JsonValueKind.Array
            ? cs.EnumerateArray().Select(c => new UniFiClient(NetworkMaps.Mac(S(c, "mac")), S(c, "hostname") ?? "", S(c, "ip") ?? "", B(c, "wired"),
                S(c, "uplinkMac") is { } um ? NetworkMaps.Mac(um) : null, S(c, "uplinkName") ?? "", I(c, "port"), L(c, "lastSeen"), S(c, "maker") ?? "")).ToList()
            : [];
        return new UniFiSite(r.GetProperty("now").GetInt64(), devices, clients);
    }
}

public sealed record UniFiDev(string Mac, string Name, string Model, string Type, string? Ip, UniFiUplink? Uplink, IReadOnlyList<UniFiPort> PortTable)
{
    /// <summary>Its name, or "USF5P 192.168.1.53" for one nobody named.</summary>
    public string Label => Name.Length > 0 ? Name : $"{Model} {Ip}".Trim();
    public string Kind => Type switch { "usw" => "Switch", "uap" => "Access point", "ugw" or "udm" or "uxg" => "Gateway", _ => Type };
}

public sealed record UniFiUplink(string? Mac, int? Port, int? LocalPort);

public sealed record UniFiPort(int Port, int Speed, bool Poe, string? LastMac, string? LastIp, long ConnectedAt);

public sealed record UniFiClient(string Mac, string Hostname, string Ip, bool Wired, string? UplinkMac, string UplinkName, int? Port, long LastSeen, string Maker);
