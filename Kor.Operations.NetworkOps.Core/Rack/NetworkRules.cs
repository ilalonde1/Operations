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
    public static readonly string[] Tables = ["1.3.6.1.2.1.2.2.1.2", "1.3.6.1.2.1.2.2.1.8", "1.3.6.1.2.1.2.2.1.14", BasePortIfIndex, FdbPort, QFdbPort];

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
