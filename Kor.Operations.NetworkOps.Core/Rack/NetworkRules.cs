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
    public static readonly string[] Tables = ["1.3.6.1.2.1.2.2.1.2", "1.3.6.1.2.1.2.2.1.8", "1.3.6.1.2.1.2.2.1.14"];

    public static RackResult Evaluate(IReadOnlyDictionary<string, string> v, IReadOnlyDictionary<string, string> previousFacts)
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
        foreach (var (idx, name) in names.OrderBy(kv => int.TryParse(kv.Key, out var n) ? n : 0))
        {
            if (v.TryGetValue($"1.3.6.1.2.1.2.2.1.8.{idx}", out var st) && st == "1") up.Add(name);
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
