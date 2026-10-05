#nullable enable
using System.Globalization;
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Power;

namespace Kor.Operations.NetworkOps.Core.Rack;

/// <summary>
/// The UniFi site, from KOR-UNIFI01's read-only status command (the controller's own database). One device on
/// the page for the whole site; each switch or access point that stops checking in is its own finding.
/// </summary>
public static class UniFiRules
{
    public static RackResult Evaluate(string json, int offlineAfterSeconds = 300)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var now = r.GetProperty("now").GetInt64();
        var b = new RackBuilder();
        int online = 0, total = 0;
        var upgradable = new List<string>(); var unnamed = 0;
        foreach (var d in r.GetProperty("devices").EnumerateArray())
        {
            total++;
            var mac = d.GetProperty("mac").GetString() ?? "?";
            var model = d.GetProperty("model").GetString() ?? "?";
            var ip = d.GetProperty("ip").GetString() ?? "?";
            var name = d.GetProperty("name").GetString() is { Length: > 0 } n ? n : $"{model} {ip}";
            if (d.GetProperty("name").GetString() is not { Length: > 0 }) unnamed++;
            var seen = d.GetProperty("lastSeen").GetInt64();
            var age = seen > 0 ? now - seen : long.MaxValue;
            // Each device, kept (2026-10-02: the controller sent all of this every 5 minutes and only two counts were kept):
            // minutes since it checked in (-1 = never), and who it is -- so the page shows every AP and switch.
            b.Metric("unifi.seen.min", seen > 0 ? Math.Round(age / 60.0, 1) : -1, mac);
            // Every field bounded BEFORE serializing: a fact value is cut at 400 characters when stored, and cut JSON is
            // unreadable (the 0.17.2 lesson). 80+16+24+40+40 and the keys stay well under 400 whatever the controller holds.
            b.Fact($"unifi.device:{mac}", JsonSerializer.Serialize(new UniFiDevice(
                Cap(d.GetProperty("name").GetString(), 80), Cap(d.GetProperty("type").GetString(), 16), Cap(model, 24), Cap(ip, 40),
                Cap(d.GetProperty("version").GetString(), 40), d.GetProperty("upgradable").GetBoolean())));
            if (age <= offlineAfterSeconds) online++;
            else
            {
                // A main switch (lots of ports) going quiet is an outage; a flex, mini or access point is one area.
                var big = d.GetProperty("ports").GetInt32() > 8 || model.StartsWith("US48", StringComparison.Ordinal) || model.StartsWith("US24", StringComparison.Ordinal);
                b.Raise($"unifi.offline:{mac}", big ? Severity.Critical : Severity.Warning, $"{name} is offline",
                    seen == 0 ? $"{model} at {ip} has never checked in to this controller" : $"{model} at {ip} last checked in {Ago(age)} ago");
            }
            if (d.GetProperty("upgradable").GetBoolean()) upgradable.Add(name);
        }
        b.Metric("devices.online", online); b.Metric("devices.total", total);
        b.Fact("unifi.site", r.GetProperty("site").GetString());
        b.Fact("unifi.devices", total.ToString(CultureInfo.InvariantCulture));
        if (upgradable.Count > 0) b.Raise("unifi.firmware", Severity.Info, $"{upgradable.Count} UniFi device(s) have a firmware update", string.Join(", ", upgradable));
        if (unnamed > 0) b.Raise("unifi.unnamed", Severity.Info, $"{unnamed} UniFi device(s) have no name", "name them in the controller so a fault says WHERE");
        var alarms = r.GetProperty("openAlarms").GetArrayLength();
        b.Metric("alarms.open", alarms);
        if (alarms > 0) b.Raise("unifi.alarms", Severity.Warning, $"{alarms} open alarm(s) in UniFi",
            string.Join("; ", r.GetProperty("openAlarms").EnumerateArray().Take(5).Select(a => $"{a.GetProperty("device").GetString()} {a.GetProperty("message").GetString()}")));
        return b.Done($"{online} of {total} devices checking in");
    }

    private static string Ago(long s) => s < 3600 ? $"{s / 60} min" : s < 172800 ? $"{s / 3600} h" : $"{s / 86400} days";

    private static string Cap(string? s, int max) => s is null ? "" : s.Length <= max ? s : s[..max];
}

/// <summary>One UniFi device as the rack keeps it (fact unifi.device:{mac}): its name ("" = unnamed), type (uap / usw /
/// ugw...), model, IP, firmware, and whether the controller has an upgrade for it.</summary>
public sealed record UniFiDevice(string Name, string Type, string Model, string Ip, string Version, bool Upgradable)
{
    public static UniFiDevice? Parse(string json)
    {
        try { return JsonSerializer.Deserialize<UniFiDevice>(json); }
        catch (JsonException) { return null; }
    }

    public string Kind => Type switch { "uap" => "Access point", "usw" => "Switch", "ugw" or "udm" or "uxg" => "Gateway", _ => "Device" };
    public string Label => Name.Length > 0 ? Name : $"{Model} {Ip}";
}

/// <summary>What NetworkOps measured about the line out, from APP01 (through the firewall).</summary>
/// <param name="Pings">target -> (sent, received, average ms).</param>
public sealed record InternetCheck(string? PublicIp, string ExpectedPublicIp, bool DnsResolves, IReadOnlyDictionary<string, (int Sent, int Received, double AvgMs)> Pings);

/// <summary>
/// The internet line through the firewall. The headline rule: the office's traffic must leave by the Shaw
/// STATIC address -- if it leaves by any other, the firewall is routing out the wrong WAN (the old bonder) and the
/// VPN, port 25 and every allow-list keyed on the static IP break.
/// </summary>
public static class InternetRules
{
    public static RackResult Evaluate(InternetCheck c)
    {
        var b = new RackBuilder();
        b.Fact("wan.public-ip", c.PublicIp);
        if (c.PublicIp is null) b.Raise("net.public-ip", Severity.Critical, "Cannot reach the internet", "the public-IP lookup failed");
        else if (!string.Equals(c.PublicIp, c.ExpectedPublicIp, StringComparison.Ordinal))
            b.Raise("net.public-ip", Severity.Critical, "Office traffic is leaving by the wrong address", $"public IP {c.PublicIp}, expected the Shaw static {c.ExpectedPublicIp}: the firewall is using another WAN");
        if (!c.DnsResolves) b.Raise("net.dns", Severity.Critical, "Name resolution is failing", "could not resolve a public name");
        foreach (var (target, (sent, received, avg)) in c.Pings)
        {
            var loss = sent == 0 ? 100 : 100.0 * (sent - received) / sent;
            b.Metric("ping.loss.pct", Math.Round(loss, 1), target);
            if (received > 0) b.Metric("ping.ms", Math.Round(avg, 1), target);
            if (received == 0) b.Raise($"net.unreachable:{target}", Severity.Critical, $"{target} is not answering", $"{sent} pings, none answered");
            else if (loss >= 20) b.Raise($"net.loss:{target}", Severity.Warning, $"Packet loss to {target}", $"{loss:0}% of {sent} pings lost, {avg:0} ms average");
        }
        var worst = c.Pings.Count == 0 ? 0 : c.Pings.Max(p => p.Value.Sent == 0 ? 100 : 100.0 * (p.Value.Sent - p.Value.Received) / p.Value.Sent);
        return b.Done($"out via {c.PublicIp ?? "nothing"} · worst loss {worst:0}%");
    }
}

/// <summary>
/// The core switch (EdgeSwitch ES-16-XG) over SNMPv3 (SHA/DES -- all its firmware offers). Watches for a reboot
/// and for a port that was up going down (the ports that were up are kept as a fact, so the next read compares).
/// </summary>
public static class EdgeSwitchRules
{
    public const string SysDescr = "1.3.6.1.2.1.1.1.0";
    public const string SysUpTime = "1.3.6.1.2.1.1.3.0";
    // ifDescr, ifOperStatus, ifInErrors -- and (2026-10-02, Ian: "which IP / device name is attached to each port") the
    // MAC address table: BRIDGE-MIB dot1dBasePortIfIndex (bridge port -> ifIndex), dot1dTpFdbPort and Q-BRIDGE dot1qTpFdbPort
    // (MAC -> bridge port; the MAC is the last six numbers of the OID). Whichever of the two the firmware fills is used.
    public const string BasePortIfIndex = "1.3.6.1.2.1.17.1.4.1.2";
    public const string FdbPort = "1.3.6.1.2.1.17.4.3.1.2";
    public const string QFdbPort = "1.3.6.1.2.1.17.7.1.2.2.1.2";
    /// <summary>IF-MIB ifHighSpeed: each interface's speed now, in Mb/s (the core's panel in the Network window, 2026-10-02).</summary>
    public const string IfHighSpeed = "1.3.6.1.2.1.31.1.1.1.15";
    public static readonly string[] Tables = ["1.3.6.1.2.1.2.2.1.2", "1.3.6.1.2.1.2.2.1.8", "1.3.6.1.2.1.2.2.1.14", BasePortIfIndex, FdbPort, QFdbPort, IfHighSpeed];

    /// <summary>
    /// Each front-panel port as the switch reports it NOW: up or down, its speed, and every MAC it has learned there -- the
    /// core switch's own panel in the port map (it is an EdgeSwitch, not UniFi: the controller knows nothing of its ports).
    /// </summary>
    public static IReadOnlyList<Network.CoreSwitchPort> Ports(IReadOnlyDictionary<string, string> v)
    {
        var macs = MacsByIfIndex(v);
        return v.Where(kv => kv.Key.StartsWith("1.3.6.1.2.1.2.2.1.2.", StringComparison.Ordinal))
            .Select(kv => (Idx: kv.Key[20..], No: PortNumber(kv.Value)))
            .Where(x => x.No is not null)
            .Select(x => new Network.CoreSwitchPort(x.No!.Value,
                v.TryGetValue($"1.3.6.1.2.1.2.2.1.8.{x.Idx}", out var st) && st == "1",
                v.TryGetValue($"{IfHighSpeed}.{x.Idx}", out var sp) && int.TryParse(sp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mb) && mb > 0 ? mb : null,
                macs.TryGetValue(x.Idx, out var m) ? m : []))
            .OrderBy(p => p.Port).ToList();
    }

    /// <summary>A port's attached devices fit one fact (nvarchar(400)): this many labels, then "+N more".</summary>
    public const int AttachedShown = 6;

    /// <summary>
    /// The MACs the switch has learned on each ifIndex, from its MAC address table (Q-BRIDGE if filled, else BRIDGE).
    /// A MAC learned on several VLANs counts once per port.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> MacsByIfIndex(IReadOnlyDictionary<string, string> v)
    {
        var bridgeToIf = v.Where(kv => kv.Key.StartsWith(BasePortIfIndex + ".", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key[(BasePortIfIndex.Length + 1)..], kv => kv.Value);
        IEnumerable<(string Mac, string BridgePort)> Fdb(string table) => v
            .Where(kv => kv.Key.StartsWith(table + ".", StringComparison.Ordinal))
            .Select(kv => (Octets: kv.Key[(table.Length + 1)..].Split('.'), Port: kv.Value))
            .Where(x => x.Octets.Length >= 6 && x.Port != "0")
            .Select(x => (string.Join(":", x.Octets[^6..].Select(o => int.Parse(o, CultureInfo.InvariantCulture).ToString("x2", CultureInfo.InvariantCulture))), x.Port));
        var entries = Fdb(QFdbPort).ToList();
        if (entries.Count == 0) entries = Fdb(FdbPort).ToList();
        return entries
            .GroupBy(e => bridgeToIf.TryGetValue(e.BridgePort, out var ifIndex) ? ifIndex : e.BridgePort)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(e => e.Mac).Distinct().Order(StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// The maker of a MAC from its first three bytes, for the makers on KOR's network only (each checked against what the
    /// core switch had learned on 2026-10-02): a device that never talks to APP01 is not in its ARP table, so its port showed
    /// a bare MAC -- the NAS's and the hosts' storage ports. Null for anything else: no guessing beyond this list.
    /// </summary>
    public static string? MakerOf(string mac) => mac.Length >= 8 ? mac[..8].ToLowerInvariant() switch
    {
        "00:50:56" or "00:0c:29" or "00:05:69" => "VMware",
        "00:11:32" or "90:09:d0" => "Synology",
        "74:83:c2" or "74:ac:b9" or "f4:92:bf" or "24:5a:4c" or "78:8a:20" or "fc:ec:da" or "e0:63:da" => "Ubiquiti",
        "00:c0:b7" => "APC",
        _ => null,
    } : null;

    /// <summary>What a port's fact says: the devices behind it, named where the caller can, at most AttachedShown and "+N more".</summary>
    public static string AttachedText(IReadOnlyList<string> macs, Func<string, string?> label)
    {
        var named = macs.Select(m => label(m) ?? (MakerOf(m) is { } maker ? $"{maker} {m}" : m)).OrderBy(l => l.Contains(':') && l.Length == 17 ? 1 : 0).ThenBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
        var shown = string.Join("; ", named.Take(AttachedShown).Select(l => l.Length > 50 ? l[..50] : l));
        return named.Count > AttachedShown ? $"{shown}; +{named.Count - AttachedShown} more" : shown;
    }

    public static RackResult Evaluate(IReadOnlyDictionary<string, string> v, IReadOnlyDictionary<string, string> previousFacts, Func<string, string?>? labelOfMac = null)
    {
        var b = new RackBuilder();
        if (v.TryGetValue(SysDescr, out var descr))
        {
            var parts = descr.Trim('"').Split(',', StringSplitOptions.TrimEntries);
            b.Fact("hw.model", parts.ElementAtOrDefault(0)); b.Fact("fw.version", parts.ElementAtOrDefault(1));
        }
        if (v.TryGetValue(SysUpTime, out var ticks) && long.TryParse(ticks, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t))
        {
            var hours = t / 360000.0;
            b.Metric("uptime.hours", Math.Round(hours, 1));
            if (hours < 24) b.Raise("switch.rebooted", Severity.Info, "Core switch restarted recently", $"up {hours:0.0} h");
        }
        var names = v.Where(kv => kv.Key.StartsWith("1.3.6.1.2.1.2.2.1.2.", StringComparison.Ordinal)).ToDictionary(kv => kv.Key[20..], kv => kv.Value.Trim('"'));
        var up = new List<string>();
        var macs = MacsByIfIndex(v);
        foreach (var (idx, name) in names.OrderBy(kv => int.TryParse(kv.Key, out var n) ? n : 0))
        {
            var isUp = v.TryGetValue($"1.3.6.1.2.1.2.2.1.8.{idx}", out var st) && st == "1";
            if (isUp) up.Add(name);
            // Each physical port's state, kept for the port map (2026-10-02: only the count was kept). CPU / VLAN interfaces are not ports.
            if (PortNumber(name) is { } port)
            {
                b.Metric("port.up", isUp ? 1 : 0, name);
                // What is plugged in: how many devices the switch has learned there, and who they are (bounded to fit the fact).
                var here = macs.TryGetValue(idx, out var m) ? m : [];
                b.Metric("port.devices", here.Count, name);
                if (here.Count > 0) b.Fact($"port.attached:{port}", AttachedText(here, labelOfMac ?? (_ => null)));
            }
            if (v.TryGetValue($"1.3.6.1.2.1.2.2.1.14.{idx}", out var err) && double.TryParse(err, NumberStyles.Float, CultureInfo.InvariantCulture, out var e))
                b.Metric("port.in-errors", e, name);
        }
        b.Metric("ports.up", up.Count);
        b.Fact("ports.up", string.Join(",", up));
        if (previousFacts.TryGetValue("ports.up", out var before))
            foreach (var gone in before.Split(',', StringSplitOptions.RemoveEmptyEntries).Except(up, StringComparer.Ordinal))
                b.Raise($"switch.port-down:{gone}", Severity.Warning, $"Core switch port {gone} went down", "it was up on the previous read: a server, SAN or uplink may have lost its link");
        return b.Done($"{up.Count} ports up · up {(b.Metrics.FirstOrDefault(m => m.Metric == "uptime.hours")?.Value / 24 ?? 0):0.#} days");
    }

    /// <summary>The front-panel number of an EdgeSwitch interface ("Slot: 0 Port: 12 10G - Level" -> 12), or null for the
    /// CPU interface, a link aggregate or a VLAN. Anchored at the start: the CPU interface is named " CPU Interface for
    /// Slot: 5 Port: 1", and an unanchored match counted it as port 1 (the map read "10 of 17 up" on a 16-port switch).</summary>
    public static int? PortNumber(string ifName)
        => System.Text.RegularExpressions.Regex.Match(ifName.Trim().Trim('"'), @"^Slot:\s*\d+\s+Port:\s*(\d+)") is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
}

/// <summary>
/// The firewall (Netgate pfSense Plus) over SNMP v2c (bsnmpd -- v1/v2c only, no v3, checked 2026-10-05 against the live
/// fw01). Its assigned interfaces named by ifAlias (WAN / WAN2 / WAN3 / LAN), each one's negotiated speed and throughput,
/// and the box's own health: CPU (HOST-RESOURCES hrProcessorLoad), memory (UCD-SNMP, NOT hrStorageTable -- that table is
/// hundreds of FreeBSD UMA rows and walking it hangs), the PF state table (BEGEMOT-PF-MIB) and uptime. Throughput is a
/// rate between two reads, so the octet counters and the read time are kept as facts for the next sweep to difference.
/// </summary>
public static class FirewallRules
{
    // IF-MIB
    public const string IfDescr = "1.3.6.1.2.1.2.2.1.2";          // idx -> NIC name (igc0..igc3)
    public const string IfOperStatus = "1.3.6.1.2.1.2.2.1.8";     // 1 up, 2 down, 5 dormant (a WAN with no carrier)
    public const string IfInErrors = "1.3.6.1.2.1.2.2.1.14";
    public const string IfAlias = "1.3.6.1.2.1.31.1.1.1.18";      // idx -> pfSense role (WAN/LAN/...); empty for system ifaces
    public const string IfHighSpeed = "1.3.6.1.2.1.31.1.1.1.15";  // Mb/s
    public const string IfHCInOctets = "1.3.6.1.2.1.31.1.1.1.6";  // Counter64
    public const string IfHCOutOctets = "1.3.6.1.2.1.31.1.1.1.10";
    public const string HrProcessorLoad = "1.3.6.1.2.1.25.3.3.1.2"; // per-core %, a handful of rows
    public static readonly string[] Tables = [IfDescr, IfOperStatus, IfInErrors, IfAlias, IfHighSpeed, IfHCInOctets, IfHCOutOctets, HrProcessorLoad];

    // Scalars (one batched v2c GET)
    public const string SysDescr = "1.3.6.1.2.1.1.1.0";
    public const string SysName = "1.3.6.1.2.1.1.5.0";
    public const string SysUpTime = "1.3.6.1.2.1.1.3.0";          // TimeTicks (hundredths of a second)
    public const string MemTotalRealKb = "1.3.6.1.4.1.2021.4.5.0";
    public const string MemAvailRealKb = "1.3.6.1.4.1.2021.4.6.0";
    public const string PfRunning = "1.3.6.1.4.1.12325.1.200.1.1.1.0";
    public const string PfStateCount = "1.3.6.1.4.1.12325.1.200.1.3.1.0";
    public const string PfStateLimit = "1.3.6.1.4.1.12325.1.200.1.5.1.0";
    public static readonly string[] Scalars = [SysDescr, SysName, SysUpTime, MemTotalRealKb, MemAvailRealKb, PfRunning, PfStateCount, PfStateLimit];

    private static readonly System.Text.RegularExpressions.Regex Version =
        new(@"\d+\.\d+(\.\d+)?-RELEASE", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The assigned interfaces (ifAlias non-empty), with throughput differenced from the previous read's octet
    /// facts, and the raw current octets keyed by role so the caller can store them for next time.</summary>
    public static (IReadOnlyList<Network.FirewallInterface> Interfaces, IReadOnlyDictionary<string, (ulong In, ulong Out)> Octets)
        ReadInterfaces(IReadOnlyDictionary<string, string> v, IReadOnlyDictionary<string, string> previousFacts, DateTime nowUtc)
    {
        double? deltaSec = null;
        if (previousFacts.TryGetValue("fw.read.ticks", out var pt) && long.TryParse(pt, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
        {
            var secs = (nowUtc - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
            if (secs > 0) deltaSec = secs;
        }
        var ifaces = new List<Network.FirewallInterface>();
        var octets = new Dictionary<string, (ulong In, ulong Out)>(StringComparer.Ordinal);
        foreach (var (key, raw) in v.Where(kv => kv.Key.StartsWith(IfAlias + ".", StringComparison.Ordinal)))
        {
            var role = raw.Trim('"').Trim();
            if (role.Length == 0) continue;                       // a system interface (lo0, pflog0, ...): pfSense gives it no name
            var idx = key[(IfAlias.Length + 1)..];
            var nic = v.TryGetValue($"{IfDescr}.{idx}", out var n) ? n.Trim('"') : idx;
            var state = v.TryGetValue($"{IfOperStatus}.{idx}", out var os) ? os switch { "1" => "up", "5" => "dormant", _ => "down" } : "down";
            int? speed = v.TryGetValue($"{IfHighSpeed}.{idx}", out var sp) && int.TryParse(sp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mb) && mb > 0 ? mb : null;
            long errs = v.TryGetValue($"{IfInErrors}.{idx}", out var er) && long.TryParse(er, NumberStyles.Integer, CultureInfo.InvariantCulture, out var e) ? e : 0;
            ulong inOct = v.TryGetValue($"{IfHCInOctets}.{idx}", out var io) && ulong.TryParse(io, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv) ? iv : 0;
            ulong outOct = v.TryGetValue($"{IfHCOutOctets}.{idx}", out var oo) && ulong.TryParse(oo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ov) ? ov : 0;
            octets[role] = (inOct, outOct);
            double? inMbps = Rate(previousFacts, $"fw.octin:{role}", inOct, deltaSec);
            double? outMbps = Rate(previousFacts, $"fw.octout:{role}", outOct, deltaSec);
            ifaces.Add(new Network.FirewallInterface(role, nic, state, speed, inMbps, outMbps, errs));
        }
        // WAN/LAN first, then by role name, so the active WAN and the LAN head the panel.
        ifaces = ifaces.OrderByDescending(i => i.IsWan && i.Up).ThenByDescending(i => i.Up).ThenBy(i => i.Role, StringComparer.OrdinalIgnoreCase).ToList();
        return (ifaces, octets);
    }

    /// <summary>Megabits/second between the previous octet count and this one; null until there are two reads, and on a
    /// counter reset (the box rebooted) rather than a negative spike.</summary>
    private static double? Rate(IReadOnlyDictionary<string, string> previousFacts, string factKey, ulong now, double? deltaSec)
    {
        if (deltaSec is not { } ds || !previousFacts.TryGetValue(factKey, out var pv) || !ulong.TryParse(pv, NumberStyles.Integer, CultureInfo.InvariantCulture, out var prev) || now < prev)
            return null;
        return Math.Round((now - prev) * 8.0 / ds / 1_000_000.0, 1);
    }

    /// <summary>The whole read as the panel wants it (Service/RackCollector pushes this to the map), from the same parse.</summary>
    public static Network.FirewallRead Read(string name, string? ip, IReadOnlyDictionary<string, string> v,
        IReadOnlyDictionary<string, string> previousFacts, DateTime nowUtc)
    {
        var (ifaces, _) = ReadInterfaces(v, previousFacts, nowUtc);
        return new Network.FirewallRead(name, ip, Model(v), ifaces, Cpu(v), MemUsedPct(v), Int(v, PfStateCount), Int(v, PfStateLimit), UptimeHours(v), nowUtc);
    }

    public static RackResult Evaluate(IReadOnlyDictionary<string, string> v, IReadOnlyDictionary<string, string> previousFacts, DateTime nowUtc)
    {
        var b = new RackBuilder();
        if (Model(v) is { } model) b.Fact("fw.model", model);
        if (v.TryGetValue(SysName, out var host)) b.Fact("fw.hostname", host.Trim('"'));

        var (ifaces, octets) = ReadInterfaces(v, previousFacts, nowUtc);
        var upNow = new List<string>();
        foreach (var i in ifaces)
        {
            var speed = i.SpeedMbps is { } s ? $"{s} Mb/s" : "no link";
            var flow = i.InMbps is { } dn && i.OutMbps is { } upl ? $" · {dn:0.#}↓/{upl:0.#}↑ Mb/s" : "";
            b.Fact($"fw.if:{i.Role}", $"{i.Nic} {i.State} · {speed}{flow}");
            if (i.SpeedMbps is { } mb) b.Metric("fw.if.speed.mbps", mb, i.Role);
            b.Metric("fw.if.up", i.Up ? 1 : 0, i.Role);
            if (i.InMbps is { } din) b.Metric("fw.if.in.mbps", din, i.Role);
            if (i.OutMbps is { } dout) b.Metric("fw.if.out.mbps", dout, i.Role);
            if (i.InErrors > 0) b.Metric("fw.if.in-errors", i.InErrors, i.Role);
            if (i.Up) upNow.Add(i.Role);
        }
        // Keep the current octets and read time so the NEXT sweep can turn them into a rate.
        foreach (var (role, o) in octets) { b.Fact($"fw.octin:{role}", o.In.ToString(CultureInfo.InvariantCulture)); b.Fact($"fw.octout:{role}", o.Out.ToString(CultureInfo.InvariantCulture)); }
        b.Fact("fw.read.ticks", nowUtc.Ticks.ToString(CultureInfo.InvariantCulture));
        b.Fact("fw.up", string.Join(",", upNow));

        if (Cpu(v) is { } cpu) b.Metric("fw.cpu.pct", cpu);
        if (MemUsedPct(v) is { } mem) { b.Metric("fw.mem.pct", mem); if (mem >= 90) b.Raise("fw.memory", Severity.Warning, "Firewall memory is high", $"{mem}% of RAM in use"); }
        if (Int(v, PfStateCount) is { } states)
        {
            b.Metric("fw.states", states);
            if (Int(v, PfStateLimit) is { } limit && limit > 0)
            {
                b.Metric("fw.states.limit", limit);
                if (states >= limit * 0.8) b.Raise("fw.states", Severity.Warning, "Firewall state table is near its limit", $"{states:N0} of {limit:N0} states");
            }
        }
        if (UptimeHours(v) is var up && up > 0)
        {
            b.Metric("fw.uptime.hours", Math.Round(up, 1));
            if (up < 24) b.Raise("fw.rebooted", Severity.Info, "Firewall restarted recently", $"up {up:0.0} h");
        }
        if (v.TryGetValue(PfRunning, out var pf) && pf != "1")
            b.Raise("fw.pf-down", Severity.Critical, "Firewall packet filter is NOT running", "pf is disabled: traffic is not being filtered");

        // An interface that was up on the last read and is not now. A WAN may be a failover (Warning); the LAN going down
        // cuts the office off (Critical). Dormant standby WANs were never in the list, so they do not raise.
        if (previousFacts.TryGetValue("fw.up", out var before))
            foreach (var gone in before.Split(',', StringSplitOptions.RemoveEmptyEntries).Except(upNow, StringComparer.Ordinal))
                b.Raise($"fw.link-down:{gone}", gone.StartsWith("WAN", StringComparison.OrdinalIgnoreCase) ? Severity.Warning : Severity.Critical,
                    $"Firewall {gone} link went down", "it was up on the previous read");
        if (ifaces.Any(i => i.IsWan) && !ifaces.Any(i => i.IsWan && i.Up))
            b.Raise("fw.no-wan", Severity.Critical, "No WAN is up on the firewall", "every WAN-role interface is down or dormant");

        var wan = ifaces.FirstOrDefault(i => i.IsWan && i.Up);
        var lan = ifaces.FirstOrDefault(i => !i.IsWan && i.Up);
        var parts = new List<string>();
        if (wan is not null) parts.Add($"{wan.Role} {(wan.SpeedMbps is { } ws ? ws + "Mb" : "up")}");
        if (lan is not null) parts.Add($"{lan.Role} {(lan.SpeedMbps is { } ls ? ls + "Mb" : "up")}");
        if (Cpu(v) is { } c) parts.Add($"CPU {c}%");
        if (Int(v, PfStateCount) is { } st) parts.Add($"{st:N0} states");
        if (up > 0) parts.Add($"up {up / 24:0.#}d");
        return b.Done(parts.Count > 0 ? string.Join(" · ", parts) : "answered SNMP");
    }

    private static string? Model(IReadOnlyDictionary<string, string> v)
        => v.TryGetValue(SysDescr, out var d) && d.Contains("pfSense", StringComparison.OrdinalIgnoreCase)
            ? "pfSense" + (d.Contains("Plus", StringComparison.OrdinalIgnoreCase) ? " Plus" : "") + (Version.Match(d) is { Success: true } m ? " " + m.Value : "")
            : v.TryGetValue(SysDescr, out var d2) ? d2.Trim('"') : null;

    private static int? Cpu(IReadOnlyDictionary<string, string> v)
    {
        var cores = v.Where(kv => kv.Key.StartsWith(HrProcessorLoad + ".", StringComparison.Ordinal))
            .Select(kv => int.TryParse(kv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : -1).Where(p => p >= 0).ToList();
        return cores.Count > 0 ? (int)Math.Round(cores.Average()) : null;
    }

    private static int? MemUsedPct(IReadOnlyDictionary<string, string> v)
        => Long(v, MemTotalRealKb) is { } total && total > 0 && Long(v, MemAvailRealKb) is { } avail
            ? (int)Math.Round(100.0 * (total - avail) / total) : null;

    private static double UptimeHours(IReadOnlyDictionary<string, string> v)
        => v.TryGetValue(SysUpTime, out var t) && long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks) ? ticks / 360000.0 : 0;

    private static int? Int(IReadOnlyDictionary<string, string> v, string oid)
        => v.TryGetValue(oid, out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static long? Long(IReadOnlyDictionary<string, string> v, string oid)
        => v.TryGetValue(oid, out var s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}

/// <summary>A UPS as a rack device: the watcher's latest reading turned into findings (the shutdown decision itself is PowerPolicy).</summary>
public static class UpsRules
{
    public static RackResult Evaluate(UpsReading r)
    {
        var b = new RackBuilder();
        if (r.MinutesRemaining is { } m) b.Metric("runtime.min", m);
        if (r.ChargePercent is { } c) b.Metric("charge.pct", c);
        if (r.LoadPercent is { } l) b.Metric("load.pct", l);
        switch (r.Source)
        {
            case PowerSource.Battery: b.Raise("ups.on-battery", Severity.Critical, "UPS is ON BATTERY", $"{(r.SecondsOnBattery ?? 0) / 60} min on battery, {r.MinutesRemaining?.ToString() ?? "?"} min left"); break;
            case PowerSource.Off: b.Raise("ups.output-off", Severity.Critical, "UPS output is OFF", "everything on this UPS has lost this feed"); break;
            case PowerSource.Bypass: b.Raise("ups.bypass", Severity.Warning, "UPS is on bypass", "the load has mains but NO battery protection"); break;
        }
        if (r.BatteryLow) b.Raise("ups.battery-low", Severity.Critical, "UPS battery is LOW", "shutdown territory");
        if (r.ReplaceBattery) b.Raise("ups.replace-battery", Severity.Warning, "UPS asks for a battery replacement", "the battery failed its self-test or is past its life");
        if (r.Source == PowerSource.Mains && r.ChargePercent >= 95 && r.MinutesRemaining is < 15)
            b.Raise("ups.short-runtime", Severity.Warning, "UPS runtime is short on a full battery", $"{r.MinutesRemaining} min at {r.LoadPercent}% load: the battery is ageing or the load has grown");
        return b.Done($"{r.Source} · {r.MinutesRemaining?.ToString() ?? "?"} min · load {r.LoadPercent?.ToString() ?? "?"}%");
    }
}
