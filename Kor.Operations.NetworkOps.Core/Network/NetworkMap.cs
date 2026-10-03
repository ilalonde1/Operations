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
///   4 NAME      fleet PC (our agent's MACs) > UniFi device > a configured rack device (by IP) > DHCP host name > what APP01
///               knows (its ARP table, reverse DNS) > controller host name > maker > the MAC. Every endpoint says which.
///   5 USER      the fleet PC's person, as the caller found it (usual / signed in now / last signed in).
/// The controller's "last seen" is shown as a date, never as "online": how often it writes it is not established (on
/// 2026-10-02 every client read 23 h or older at 21:00 -- after hours, and KOR-1001 was on the VPN, not a switch port, so
/// that evening proves nothing either way). "Online" needs a live source: the agent's connection, a current lease.
/// </summary>
public static class NetworkMaps
{
    /// <param name="live">The controller's LIVE API read (ports up now, clients connected now), when it could be read: rule 6.
    /// Without it the map is the database's -- who was last on each port -- and says nothing about now.</param>
    /// <param name="core">The core switch's own read (it is an EdgeSwitch: UniFi knows only its MAC): rule 7.</param>
    public static NetworkMap Build(UniFiSite site, IReadOnlyList<FleetPc> fleet, IReadOnlyList<DhcpLease> leases, KnownNames known, UniFiLive? live = null,
        CoreSwitchRead? core = null)
    {
        var devices = site.Devices.ToDictionary(d => d.Mac, StringComparer.Ordinal);
        var clients = site.Clients.GroupBy(c => c.Mac).ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.LastSeen).First(), StringComparer.Ordinal);
        var pcByMac = new Dictionary<string, FleetPc>(StringComparer.Ordinal);
        foreach (var pc in fleet) foreach (var m in pc.Macs) pcByMac.TryAdd(Mac(m), pc);
        var leaseByMac = leases.GroupBy(l => Mac(l.Mac)).ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.State == "Active").First(), StringComparer.Ordinal);

        // 6 LIVE: who is connected NOW and where, which ports are up NOW -- the controller's own live word, the strongest
        // there is. It decides a port's occupant over the database's history (2026-10-02: 70 of 70 live positions matched
        // the database; what it adds is "now": KOR-1001 was on the VPN, its port's record was three days old).
        var liveClients = (live?.Clients ?? []).GroupBy(c => c.Mac).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var livePorts = (live?.Devices ?? []).SelectMany(d => d.Ports.Select(p => (d.Mac, P: p))).GroupBy(x => (x.Mac, x.P.Port)).ToDictionary(g => g.Key, g => g.First().P);
        var liveUp = (live?.Devices ?? []).ToDictionary(d => d.Mac, d => d.State == 1, StringComparer.Ordinal);
        var liveAt = liveClients.Values.Where(c => c.Wired && c.SwMac is not null && c.SwPort is not null)
            .GroupBy(c => (c.SwMac!, c.SwPort!.Value)).ToDictionary(g => g.Key, g => g.ToList());

        NetEndpoint Describe(string mac, string? ip, long seen)
        {
            pcByMac.TryGetValue(mac, out var pc);
            devices.TryGetValue(mac, out var dev);
            clients.TryGetValue(mac, out var cl);
            leaseByMac.TryGetValue(mac, out var lease);
            liveClients.TryGetValue(mac, out var lc);
            ip = ip is { Length: > 0 } ? ip : lc?.Ip is { Length: > 0 } lip ? lip : cl?.Ip is { Length: > 0 } cip ? cip : dev?.Ip is { Length: > 0 } dip ? dip : lease?.Ip;
            var maker = EdgeSwitchRules.MakerOf(mac) ?? (cl?.Maker is { Length: > 0 } mk ? mk : null);
            var (name, source) =
                pc is not null ? (pc.Name, "NetworkOps agent")
                : dev is not null ? (dev.Label, "UniFi device")
                : ip is not null && known.ByIp.TryGetValue(ip, out var ki) ? (ki, "rack")
                : lease?.HostName is { Length: > 0 } hn ? (Short(hn), "DHCP")
                : known.ByMac.TryGetValue(mac, out var km) ? (km, "APP01")   // its ARP table + reverse DNS (the firewall, by being the gateway)
                : cl?.Hostname is { Length: > 0 } ch ? (ch, "UniFi client")
                : lc?.Hostname is { Length: > 0 } lh ? (lh, "UniFi client")
                : maker is not null ? ($"{maker} device", "maker")
                : (mac, "MAC only");
            return new NetEndpoint(mac, name, source, ip, pc?.Name, pc?.User, pc?.UserSource, maker,
                seen > 0 ? DateTimeOffset.FromUnixTimeSeconds(seen).UtcDateTime : null, lease?.State, dev?.Kind,
                ConnectedNow: live is null ? null : lc is not null || (liveUp.TryGetValue(mac, out var up) && up));
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
        // A device connected now is where the live read says, whatever a port's history says (rule 6 over rule 3).
        foreach (var lc in liveAt.Values.SelectMany(x => x)) claims[lc.Mac] = (lc.SwMac!, lc.SwPort!.Value, lc.Mac, long.MaxValue);
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
                else if (liveAt.TryGetValue((s.Mac, p.Port), out var here) && here.Count > 1 && p.LastMac is { } box && !liveClients.ContainsKey(box)
                         && claims.TryGetValue(box, out var bw) && bw.Sw == s.Mac && bw.Port == p.Port)
                {
                    // Several live devices on one port, and the switch's own last device there is not live anywhere: that is the
                    // box they are behind (ESXi host .16 on SW01 port 49, its VMs live behind it). The box stays; they are also seen.
                    mac = box; ip = p.LastIp; seen = p.ConnectedAt; connected = true;
                    foreach (var o in here) { placed.Add(o.Mac); displaced.Add(Describe(o.Mac, o.Ip, 0)); }
                }
                else if (liveAt.TryGetValue((s.Mac, p.Port), out here))
                {
                    // Connected here now. More than one is a hub or unmanaged switch behind the port: a fleet PC first, the rest also seen.
                    // Who is "on" it: a fleet PC; else the switch's own last device there (the box -- ESXi .16's own address is
                    // live too, behind it its VMs); else the longest connected.
                    var first = here.OrderBy(c => pcByMac.ContainsKey(c.Mac) ? 0 : c.Mac == p.LastMac ? 1 : 2).ThenByDescending(c => c.Uptime).First();
                    mac = first.Mac; ip = first.Ip; connected = true;
                    seen = first.Uptime is { } up && live!.Now > up ? live.Now - up : 0;
                    foreach (var o in here.Where(c => c.Mac != first.Mac)) { placed.Add(o.Mac); displaced.Add(Describe(o.Mac, o.Ip, 0)); }
                    if (p.LastMac is { } other && other != first.Mac && !liveClients.ContainsKey(other) && claims.TryGetValue(other, out var w) && w.Sw == s.Mac && w.Port == p.Port)
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
                livePorts.TryGetValue((s.Mac, p.Port), out var lp);
                var on = mac is null ? null : Describe(mac, ip, seen) with { SeenIsConnected = connected };
                // What is plugged into a port that is up IS connected now; into one that is down, is not (the port's own live word).
                if (on is not null && kind == "device" && lp is not null) on = on with { ConnectedNow = lp.Up };
                ports.Add(new NetPort(p.Port, p.Speed, p.Poe, kind, on, displaced,
                    Up: lp?.Up, SpeedNow: lp is { Up: true } ? lp.Speed : null, Module: lp?.Sfp, PoeWatts: lp?.PoeW is > 0 ? lp.PoeW : null));
            }
            portsBySwitch[s.Mac] = ports;
        }

        // 7 CORE: the switch the top UniFi switches hang from is not UniFi (an EdgeSwitch ES-16-XG -- Ian, 2026-10-02: "I'm not
        // understanding why that switch isn't listed?"). Its own read (SNMP, every sweep) is its panel: each port, up now,
        // and what it has learned there. A port holding a top switch's own MAC is the link to that switch (and gives it its
        // "hangs from core port N"); any other port's occupant is the box itself -- a fleet PC, else a named device, VMware
        // addresses last (VMs live behind hosts) -- and the rest are behind it. Its MAC table is live: up = connected now.
        var rootMac = switches.Select(s => s.Uplink?.Mac).FirstOrDefault(m => m is not null && !devices.ContainsKey(m));
        var coreParentPort = new Dictionary<string, int>(StringComparer.Ordinal);
        List<NetPort>? corePorts = null;
        if (core is not null && rootMac is not null)
        {
            var topSwitches = switches.Where(s => s.Uplink?.Mac == rootMac).Select(s => s.Mac).ToHashSet(StringComparer.Ordinal);
            corePorts = [];
            foreach (var cp in core.Ports)
            {
                var link = cp.Macs.FirstOrDefault(topSwitches.Contains);
                if (link is not null)
                {
                    coreParentPort.TryAdd(link, cp.Port);
                    corePorts.Add(new NetPort(cp.Port, cp.SpeedMbps ?? 0, false, "link", Describe(link, null, 0), [], Up: cp.Up, SpeedNow: cp.Up ? cp.SpeedMbps : null));
                    continue;
                }
                int Rank(string m) => pcByMac.ContainsKey(m) ? 0 : EdgeSwitchRules.MakerOf(m) == "VMware" ? 3
                    : Describe(m, null, 0).NameSource is "maker" or "MAC only" ? 2 : 1;
                var here = cp.Macs.Where(m => !placed.Contains(m) && !devices.ContainsKey(m) && m != rootMac).OrderBy(Rank).ThenBy(m => m, StringComparer.Ordinal).ToList();
                NetEndpoint? on = here.Count == 0 ? null : Describe(here[0], null, 0) with { ConnectedNow = cp.Up };
                var behind = here.Skip(1).Select(m => Describe(m, null, 0) with { ConnectedNow = cp.Up }).ToList();
                foreach (var m in here) placed.Add(m);
                corePorts.Add(new NetPort(cp.Port, cp.SpeedMbps ?? 0, false, on is null ? "empty" : "device", on, behind, Up: cp.Up, SpeedNow: cp.Up ? cp.SpeedMbps : null));
            }
        }

        // Every other wired client the controller recorded on a port: "also seen" there (history, or beyond a link).
        var wireless = new List<(NetEndpoint E, string? ApMac)>();
        var unplaced = new List<NetEndpoint>();
        // The tree's members are not endpoints: the UniFi devices, and the parents their uplinks lead to (the core switch).
        var tree = site.Devices.Select(d => d.Mac).Concat(site.Devices.Select(d => d.Uplink?.Mac).OfType<string>()).ToHashSet(StringComparer.Ordinal);
        // A client's access point / switch is matched by MAC, and shown by its CURRENT name: the controller keeps the name it
        // had when the client last connected, so after the BMZ -> KOR rename (2026-10-02) a name match lost every wireless client.
        string ViaName(UniFiClient c) => c.UplinkMac is { } m && devices.TryGetValue(m, out var d) ? d.Label : c.UplinkName;
        foreach (var c in clients.Values.OrderBy(c => c.Mac, StringComparer.Ordinal))
        {
            if (placed.Contains(c.Mac) || tree.Contains(c.Mac)) continue;
            if (!c.Wired)
            {
                // On an access point now, if the live read says so; else the one the controller last recorded.
                var apMac = liveClients.TryGetValue(c.Mac, out var lw) && lw.ApMac is { } la ? la : c.UplinkMac;
                var via = apMac is not null && devices.TryGetValue(apMac, out var apDev) ? apDev.Label : ViaName(c);
                wireless.Add((Describe(c.Mac, c.Ip, c.LastSeen) with { Via = via }, apMac));
                continue;
            }
            if (c.UplinkMac is { } up && c.Port is { } port && portsBySwitch.TryGetValue(up, out var ports) && ports.FindIndex(x => x.Number == port) is var i and >= 0)
                ports[i] = ports[i] with { AlsoSeen = [.. ports[i].AlsoSeen, Describe(c.Mac, c.Ip, c.LastSeen)] };
            else unplaced.Add(Describe(c.Mac, c.Ip, c.LastSeen) with { Via = ViaName(c) });
        }

        var shift = corePorts is null ? 0 : 1;   // with the core shown, everything hangs one level below it
        var list = ordered.Select(o => new NetSwitch(o.Dev.Mac, o.Dev.Label, o.Dev.Model, o.Dev.Ip,
                o.Dev.Uplink?.Mac, o.Dev.Uplink?.Mac is { } pm ? (devices.TryGetValue(pm, out var pd) ? pd.Label : pm == rootMac && core is not null ? core.Name : known.ByMac.GetValueOrDefault(pm) ?? pm) : null,
                // UniFi does not know the core's port numbers: the core's own MAC table does.
                o.Dev.Uplink?.Port ?? (coreParentPort.TryGetValue(o.Dev.Mac, out var cpp) ? cpp : null),
                o.Dev.Uplink?.LocalPort, o.Depth + shift, portsBySwitch[o.Dev.Mac]))
            .ToList();
        if (corePorts is not null) list.Insert(0, new NetSwitch(rootMac!, core!.Name, "EdgeSwitch (not UniFi)", core.Ip, null, null, null, null, 1, corePorts, IsCore: true));
        var aps = site.Devices.Where(d => d.Type == "uap").OrderBy(d => d.Label, StringComparer.OrdinalIgnoreCase)
            .Select(d => new NetAccessPoint(d.Mac, d.Label, d.Model, d.Ip, wireless.Where(w => w.ApMac == d.Mac).Select(w => w.E).OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();
        return new NetworkMap(DateTimeOffset.FromUnixTimeSeconds(site.Now).UtcDateTime, list, aps,
            wireless.Where(w => !aps.Any(a => a.Mac == w.ApMac)).Select(w => w.E).ToList(), unplaced)
        { LiveUtc = live is null ? null : DateTimeOffset.FromUnixTimeSeconds(live.Now).UtcDateTime };
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
    /// <summary>When the controller's live API was read for this map; null = the database only (no "now" in it).</summary>
    public DateTime? LiveUtc { get; init; }

    /// <summary>Every endpoint once, where it is (rule 3): occupants, also-seen, wireless, unplaced. The ONE walk -- the store's
    /// rows, the documentation and the gate all read this.</summary>
    public IEnumerable<NetPlacement> Everything()
    {
        foreach (var s in Switches)
            foreach (var p in s.Ports)
            {
                // Every occupant that is not part of the tree (a UniFi device, or the parent an uplink leads to): a desk's PC.
                if (p.On is { UniFiKind: null } on && p.Kind != "uplink") yield return new(on, "port", s.Name, p.Number, s.Mac);
                foreach (var a in p.AlsoSeen) yield return new(a, "also-seen", s.Name, p.Number, s.Mac);
            }
        foreach (var ap in AccessPoints) foreach (var w in ap.Clients) yield return new(w, "wireless", ap.Name, null, ap.Mac);
        foreach (var w in OtherWireless) yield return new(w, "wireless", w.Via, null, null);
        foreach (var u in Unplaced) yield return new(u, "unplaced", u.Via is { Length: > 0 } v ? v : null, null, null);
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
/// <param name="SwitchMac">The switch / access point by MAC: what a move is judged by (a renamed switch is not a move).</param>
public sealed record NetPlacement(NetEndpoint Endpoint, string Placement, string? Switch, int? Port, string? SwitchMac = null)
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
/// <param name="IsCore">The core switch (an EdgeSwitch, read by NetworkOps itself, not by UniFi): "network:core" opens it.</param>
public sealed record NetSwitch(string Mac, string Name, string Model, string? Ip, string? ParentMac, string? ParentName, int? ParentPort,
    int? UplinkPort, int Depth, IReadOnlyList<NetPort> Ports, bool IsCore = false);

/// <param name="Kind">device | link (a UniFi device hangs here) | uplink (toward the parent) | empty</param>
/// <param name="AlsoSeen">Other clients the controller recorded on this port: the desk's history, or devices beyond a link.</param>
/// <param name="Up">The port's link NOW (live read); null when there was no live read.</param>
/// <param name="SpeedNow">Its link speed now, in Mb/s, when up.</param>
/// <param name="Module">What is in an SFP cage ("SFP-H10GB-CU1M" = a 1 m 10G DAC), from the live read.</param>
/// <param name="PoeWatts">Power it is giving a phone / access point now.</param>
public sealed record NetPort(int Number, int SpeedMbps, bool Poe, string Kind, NetEndpoint? On, IReadOnlyList<NetEndpoint> AlsoSeen,
    bool? Up = null, int? SpeedNow = null, string? Module = null, double? PoeWatts = null);

public sealed record NetAccessPoint(string Mac, string Name, string Model, string? Ip, IReadOnlyList<NetEndpoint> Clients);

/// <param name="NameSource">NetworkOps agent | UniFi device | rack | DHCP | APP01 | UniFi client | maker | MAC only</param>
/// <param name="SeenUtc">When the switch saw it connect, or the controller last recorded it -- a date, not "online".</param>
/// <param name="Lease">DC01's lease state for it (Active, ...), or null.</param>
/// <param name="UniFiKind">For a UniFi device: Switch / Access point / Gateway.</param>
/// <param name="Via">Wireless or unplaced: the access point or uplink the controller named.</param>
/// <param name="SeenIsConnected">SeenUtc is when the SWITCH saw it connect (connected since), not when the controller last
/// recorded it: ESXi host .16's link on BMZ-SW01 port 49 read "seen 2024-09-26" -- up since then, not gone since then.</param>
/// <param name="ConnectedNow">On the network NOW by the controller's live read; null when there was no live read -- never
/// guessed from a date.</param>
public sealed record NetEndpoint(string Mac, string Name, string NameSource, string? Ip, string? Pc, string? User, string? UserSource,
    string? Maker, DateTime? SeenUtc, string? Lease, string? UniFiKind, string? Via = null, bool SeenIsConnected = false, bool? ConnectedNow = null);

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

// ---- the core switch, read by NetworkOps itself (an EdgeSwitch: UniFi knows only its MAC) ----

/// <summary>The core switch's own read (SNMP, every rack sweep): its ports as they are now and what each has learned.</summary>
public sealed record CoreSwitchRead(string Name, string? Ip, IReadOnlyList<CoreSwitchPort> Ports, DateTime ReadUtc);

public sealed record CoreSwitchPort(int Port, bool Up, int? SpeedMbps, IReadOnlyList<string> Macs);

// ---- the controller's LIVE API read (Service/Rack/UniFiApi: stat/device + stat/sta, projected to these fields) ----

/// <summary>What the controller knows NOW: every device's state and ports, every client connected.</summary>
public sealed record UniFiLive(long Now, IReadOnlyList<LiveDevice> Devices, IReadOnlyList<LiveClient> Clients)
{
    public static UniFiLive Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        static string? S(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static long? L(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;
        static double? D(JsonElement e, string p) => e.TryGetProperty(p, out var v) ? v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) => x,
            _ => null,
        } : null;
        static bool B(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.True;
        static IEnumerable<JsonElement> A(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];

        var devices = A(r, "devices").Select(d => new LiveDevice(NetworkMaps.Mac(S(d, "mac")), S(d, "name") ?? "", (int)(L(d, "state") ?? 0),
            A(d, "ports").Select(p => new LivePort((int)(L(p, "port") ?? 0), B(p, "up"), (int)(L(p, "speed") ?? 0), D(p, "poeW"), S(p, "media"), S(p, "sfp"))).ToList())).ToList();
        var clients = A(r, "clients").Select(c => new LiveClient(NetworkMaps.Mac(S(c, "mac")), S(c, "ip"), S(c, "hostname") ?? S(c, "name"), B(c, "wired"),
            S(c, "swMac") is { } sw ? NetworkMaps.Mac(sw) : null, (int?)L(c, "swPort"), S(c, "apMac") is { } ap ? NetworkMaps.Mac(ap) : null, L(c, "uptime"))).ToList();
        return new UniFiLive(L(r, "now") ?? 0, devices, clients);
    }
}

/// <param name="State">The controller's state: 1 = connected.</param>
public sealed record LiveDevice(string Mac, string Name, int State, IReadOnlyList<LivePort> Ports);

public sealed record LivePort(int Port, bool Up, int Speed, double? PoeW, string? Media, string? Sfp);

/// <param name="Uptime">Seconds it has been connected.</param>
public sealed record LiveClient(string Mac, string? Ip, string? Hostname, bool Wired, string? SwMac, int? SwPort, string? ApMac, long? Uptime);
