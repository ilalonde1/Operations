#nullable enable
using System.Globalization;
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Rack;

/// <summary>One reading a rack device's last read stored (NetworkOps.Metrics): metric, subject (a datastore, a volume, a
/// disk, a port, a Veeam job...), value, when.</summary>
public sealed record DeviceReading(string Metric, string Subject, double Value, DateTime AtUtc);

/// <summary>
/// A rack device's parts as tiles -- the "This PC" strip for the rest of the network (Ian, 2026-10-02: "the whole point of
/// this is that it collates and manages EVERYTHING on a network. I have individual management interfaces for all of these
/// components but not consolidated"). Built from what the device REPORTS, not from its kind: a switch has port tiles because
/// it reports ports, a NAS volume tiles because it reports volumes -- so a new collector's readings show without a change
/// here. Each tile is coloured by the open findings about that part (the collectors' own rule keys, per datastore / volume /
/// disk / port / job / repository), exactly as PcComponents does for a PC.
/// </summary>
public static class RackComponents
{
    public static IReadOnlyList<PcComponent> Of(IReadOnlyDictionary<string, string> facts, IReadOnlyList<DeviceReading> readings,
        IReadOnlyList<(string RuleKey, Severity Severity)> open)
    {
        var tiles = new List<PcComponent>();
        double? One(string metric) => readings.Where(r => r.Metric == metric).OrderByDescending(r => r.AtUtc).Select(r => (double?)r.Value).FirstOrDefault();
        IEnumerable<DeviceReading> Each(string metric) => readings.Where(r => r.Metric == metric).GroupBy(r => r.Subject).Select(g => g.OrderByDescending(r => r.AtUtc).First()).OrderBy(r => r.Subject, StringComparer.OrdinalIgnoreCase);
        string F(string key) => facts.TryGetValue(key, out var v) ? v : "";
        static string N(double v) => v.ToString("0", CultureInfo.InvariantCulture);
        // The device-wide readings named, as rows.
        IReadOnlyList<PartInfo> Pick(params string[] metrics)
            => metrics.SelectMany(m => readings.Where(r => r.Metric == m && r.Subject.Length == 0).OrderByDescending(r => r.AtUtc).Take(1))
                .Select(r => new PartInfo(LabelOf(r.Metric), Value(r))).ToList();

        // The box itself: model, software version, its own temperature -- and the findings about the whole device.
        var version = new[] { F("esxi.version"), F("dsm.version"), F("os.caption"), F("fw.version").Length > 0 ? "firmware " + F("fw.version") : "" }.FirstOrDefault(v => v.Length > 0) ?? "";
        var model = F("hw.model");
        if (model.Length > 0 || version.Length > 0)
            tiles.Add(Make("system", "System", model.Length > 0 ? model : version, model.Length > 0 ? version : "", null, open,
                k => k == RackResult.UnreachableRule || k is "syno.update" or "syno.system" or "syno.power" or "syno.fan" or "syno.cpu-fan" or "syno.temp"
                     or "esxi.maintenance" or "esxi.sensors-blind" or "server.reboot-pending" or "unifi.firmware" || k.StartsWith("esxi.sensor:", StringComparison.Ordinal)
                     || (k.StartsWith("printer.", StringComparison.Ordinal) && !k.StartsWith("printer.supply:", StringComparison.Ordinal)))   // jam, door, paper, offline
                with { Info = DeviceInfo(facts, readings) });

        if (One("uptime.hours") is { } up)
            tiles.Add(Make("uptime", "Up", up >= 48 ? $"{up / 24:0.#} days" : $"{N(up)} hours",
                One("clock.offset.s") is { } off ? $"clock {(Math.Abs(off) < 1 ? "in step" : $"{off:+0;-0} s off")}" : One("temp.c") is { } t ? $"{N(t)} °C inside" : "",
                null, open, k => k is "esxi.clock" or "esxi.ntp" or "esxi.ptp" or "switch.rebooted")
                with { Info = Pick("uptime.hours", "clock.offset.s", "temp.c") });

        if (One("cpu.pct") is { } cpu) tiles.Add(Make("cpu", "CPU", $"{N(cpu)}%", One("sensors.count") is { } s ? $"{N(s)} sensors read" : "", cpu, open, _ => false)
            with { Info = Pick("cpu.pct", "sensors.count") });
        if (One("mem.pct") is { } mem) tiles.Add(Make("memory", "Memory", $"{N(mem)}% used", "", mem, open, k => k == "esxi.memory") with { Info = Pick("mem.pct") });
        if (One("vms.running") is { } vms)
            tiles.Add(Make("vms", "VMs", $"{N(vms)} running", "", null, open, k => k.StartsWith("esxi.vm-off:", StringComparison.Ordinal) || k.StartsWith("esxi.vm-tools:", StringComparison.Ordinal))
                with { Info = Pick("vms.running") });

        foreach (var d in Each("datastore.free.pct"))
            tiles.Add(Make("datastore", "Datastore", d.Subject, $"{N(d.Value)}% free", 100 - d.Value, open,
                k => k == $"esxi.datastore-full:{d.Subject}" || k == $"esxi.datastore-lost:{d.Subject}" || k == $"esxi.stale-mount:{d.Subject}")
                with { Info = SubjectInfo(d.Subject, facts, readings) });
        foreach (var v in Each("volume.free.pct"))
            tiles.Add(Make("volume", "Volume", v.Subject, $"{N(v.Value)}% free", 100 - v.Value, open,
                k => k == $"syno.volume-full:{v.Subject}" || k == $"syno.raid:{v.Subject}") with { Info = SubjectInfo(v.Subject, facts, readings) });
        // A NAS's disks as one tile: how many, the hottest, and every disk or RAID finding (one bad disk colours it).
        var temps = Each("disk.temp.c").ToList();
        if (One("disks.count") is { } disks || temps.Count > 0)
            tiles.Add(Make("disks", "Disks", $"{N(One("disks.count") ?? temps.Count)} disks", temps.Count > 0 ? $"hottest {N(temps.Max(x => x.Value))} °C" : "", null, open,
                k => k.StartsWith("syno.disk:", StringComparison.Ordinal) || (k.StartsWith("syno.raid:", StringComparison.Ordinal) && !Each("volume.free.pct").Any(v => k == $"syno.raid:{v.Subject}")))
                with { Info = [.. Pick("disks.count"), .. EachInfo(readings, "disk.temp.c")] });
        foreach (var d in Each("disk.free.pct"))
            tiles.Add(Make("drive", $"{d.Subject.TrimEnd(':')}:", $"{N(100 - d.Value)}% used", $"{N(d.Value)}% free", 100 - d.Value, open,
                k => k == $"server.disk-full:{d.Subject}" || k == $"server.disk-full:{d.Subject.TrimEnd(':')}" || k.StartsWith("server.storage-errors", StringComparison.Ordinal))
                with { Info = SubjectInfo(d.Subject, facts, readings) });
        if (One("update.age.days") is { } age)
            tiles.Add(Make("updates", "Updates", age < 0 ? "never seen" : $"{N(age)} days ago", "last update installed", null, open, k => k == "server.unpatched")
                with { Info = [.. Pick("update.age.days"), .. PcComponents.Rows(("Windows", F("os.caption")), ("Build", F("os.build")))] });
        if (open.Any(f => f.RuleKey.StartsWith("server.service-stopped:", StringComparison.Ordinal) || f.RuleKey.StartsWith("server.vss-writer:", StringComparison.Ordinal)))
            tiles.Add(Make("services", "Services", "something stopped", "see the findings", null, open,
                k => k.StartsWith("server.service-stopped:", StringComparison.Ordinal) || k.StartsWith("server.vss-writer:", StringComparison.Ordinal))
                with { Info = open.Where(f => f.RuleKey.StartsWith("server.service-stopped:", StringComparison.Ordinal) || f.RuleKey.StartsWith("server.vss-writer:", StringComparison.Ordinal))
                    .Select(f => new PartInfo(f.RuleKey.StartsWith("server.vss", StringComparison.Ordinal) ? "Backup writer failed" : "Stopped", f.RuleKey[(f.RuleKey.IndexOf(':') + 1)..])).ToList() });

        if (One("ports.up") is { } ports)
        {
            var errors = Each("port.in-errors").ToDictionary(p => p.Subject, p => p.Value);
            var erring = errors.Count(p => p.Value > 0);
            // ONE rule for what a port is, for the lights, the count, the rows and the port tiles: an interface EdgeSwitchRules
            // can number. The CPU interface has no front-panel number; read before 2026-10-02 it was drawn as "Port 0".
            var physical = Each("port.up").Select(p => (No: EdgeSwitchRules.PortNumber(p.Subject), Reading: p)).Where(p => p.No is not null)
                .Select(p => (No: p.No!.Value, p.Reading)).OrderBy(p => p.No).ToList();
            // The port map: each physical port, front-panel order, as a light (needs the per-port state kept since 2026-10-02).
            var lights = physical.Select(p => p.Reading.Value >= 1 ? (errors.GetValueOrDefault(p.Reading.Subject) > 0 ? 2 : 1) : 0).ToList();
            // "N of M up" from the lights (physical ports only): ports.up counts every interface up, the always-up CPU one too.
            var tile = Make("ports", "Ports", lights.Count > 0 ? $"{lights.Count(l => l >= 1)} of {lights.Count} up" : $"{N(ports)} up", erring > 0 ? $"{erring} with errors" : "no errors", null, open,
                k => k.StartsWith("switch.port-down:", StringComparison.Ordinal));
            tile = tile with
            {
                Info = physical.Select(x => new PartInfo($"Port {x.No}", (x.Reading.Value >= 1 ? "up" : "down")
                        + (errors.GetValueOrDefault(x.Reading.Subject) is > 0 and var e ? $" · {N(e)} errors" : "")
                        + (x.Reading.Value >= 1 && F($"port.attached:{x.No}") is { Length: > 0 } a ? $" · {a.Split(';')[0].Trim()}" : ""))).ToList(),
            };
            // What is on each port is NOT drawn here: the core is a panel in the Network window, every port with its device,
            // built from this same read (Core/Network rule 7). One view of the data, this tile the way in (Ian, 2026-10-02:
            // "I want duplicate ways to get into the same data. No data duplication").
            tiles.Add((lights.Count > 0 ? tile with { Lights = lights } : tile) with { Opens = "network:core" });
        }
        if (One("devices.total") is { } total)
            tiles.Add(Make("devices", "Devices", $"{N(One("devices.online") ?? 0)} of {N(total)} online", F("unifi.site"), null, open,
                k => k.StartsWith("unifi.offline:", StringComparison.Ordinal) || k == "unifi.unnamed")
                with { Info = [.. PcComponents.Rows(("Site", F("unifi.site"))), .. Pick("devices.total", "devices.online")], Opens = "network:" });
        if (One("alarms.open") is { } alarms) tiles.Add(Make("alarms", "Alarms", alarms == 0 ? "none" : $"{N(alarms)} open", "", null, open, k => k == "unifi.alarms")
            with { Info = Pick("alarms.open") });
        // Each UniFi device (access point, switch, gateway): checked in or not, firmware, an upgrade waiting -- offline first.
        var unifi = Each("unifi.seen.min")
            .Select(s => (Seen: s, Dev: facts.TryGetValue($"unifi.device:{s.Subject}", out var j) ? UniFiDevice.Parse(j) : null))
            .Where(x => x.Dev is not null)
            .OrderBy(x => x.Seen.Value is >= 0 and <= 5 ? 1 : 0).ThenBy(x => x.Dev!.Kind, StringComparer.Ordinal).ThenBy(x => x.Dev!.Label, StringComparer.OrdinalIgnoreCase);
        foreach (var (seen, dev) in unifi)
        {
            var mac = seen.Subject;
            var state = seen.Value < 0 ? "never checked in" : seen.Value <= 5 ? "online" : $"offline {(seen.Value >= 2880 ? $"{seen.Value / 1440:0} days" : seen.Value >= 120 ? $"{seen.Value / 60:0} h" : $"{seen.Value:0} min")}";
            tiles.Add(Make("unifi-device", dev!.Kind, dev.Label, $"{state}{(dev.Upgradable ? " · update waiting" : "")} · {dev.Model}", null, open,
                k => k == $"unifi.offline:{mac}") with { Info = SubjectInfo(mac, facts, readings), Opens = $"network:{mac}" });   // its ports / wireless live in the Network window
        }

        // Pinged per target (several destinations): the tile shows the WORST of them, latency and loss.
        var pings = Each("ping.ms").ToList();
        var losses = Each("ping.loss.pct").ToList();
        if (pings.Count > 0 || losses.Count > 0)
            tiles.Add(Make("internet", "Internet", pings.Count > 0 ? $"{N(pings.Max(p => p.Value))} ms" : "no reply",
                $"{N(losses.Count > 0 ? losses.Max(l => l.Value) : 0)}% loss{(F("wan.public-ip").Length > 0 ? " · " + F("wan.public-ip") : "")}", null, open,
                k => k.StartsWith("net.", StringComparison.Ordinal))
                with { Info = [.. PcComponents.Rows(("Public address", F("wan.public-ip"))), .. EachInfo(readings, "ping.ms", "ping.loss.pct")] });

        var ups = Pick("charge.pct", "runtime.min", "load.pct");
        if (One("charge.pct") is { } charge) tiles.Add(Make("charge", "Battery", $"{N(charge)}% charged", "", charge, open, k => k is "ups.battery-low" or "ups.replace-battery" or "ups.on-battery") with { Info = ups });
        if (One("runtime.min") is { } runtime) tiles.Add(Make("runtime", "Runtime", $"{N(runtime)} min", "on battery, at this load", null, open, k => k == "ups.short-runtime") with { Info = ups });
        if (One("load.pct") is { } load) tiles.Add(Make("load", "Load", $"{N(load)}%", "", load, open, k => k is "ups.bypass" or "ups.output-off") with { Info = ups });

        // A printer: each supply (ink, toner, drum, waste) with what is left; the page count (Rack/PrinterRules).
        foreach (var s in Each("supply.pct"))
            tiles.Add(Make("supply", "Supply", s.Subject, $"{s.Value.ToString("0", CultureInfo.InvariantCulture)}% left", 100 - s.Value, open,
                k => k == $"printer.supply:{s.Subject}") with { Info = SubjectInfo(s.Subject, facts, readings) });
        if (One("pages.total") is { } pages)
            tiles.Add(Make("pages", "Pages", $"{pages.ToString("#,0", CultureInfo.InvariantCulture)} printed", F("fw.version") is { Length: > 0 } fwv ? $"firmware {fwv}" : "", null, open, _ => false)
                with { Info = [.. Pick("pages.total"), .. PcComponents.Rows(("Model", F("hw.model")), ("Firmware", F("fw.version")))] });

        foreach (var j in Each("job.age.hours"))
            tiles.Add(Make("job", "Backup job", j.Subject, $"{F($"veeam.result:{j.Subject}") switch { "" => "last run", var r => r }} · {(j.Value >= 48 ? $"{j.Value / 24:0.#} d" : $"{N(j.Value)} h")} ago", null, open,
                k => k.EndsWith(":" + j.Subject, StringComparison.Ordinal) && k.StartsWith("veeam.", StringComparison.Ordinal) && !k.StartsWith("veeam.repo", StringComparison.Ordinal))
                with { Info = SubjectInfo(j.Subject, facts, readings) });
        foreach (var r in Each("repo.free.pct"))
            tiles.Add(Make("repo", "Repository", r.Subject, $"{N(r.Value)}% free", 100 - r.Value, open,
                k => k == $"veeam.repo-full:{r.Subject}" || k == $"veeam.repo-offline:{r.Subject}") with { Info = SubjectInfo(r.Subject, facts, readings) });
        return tiles;
    }

    private static PcComponent Make(string kind, string title, string line1, string line2, double? fill,
        IReadOnlyList<(string RuleKey, Severity Severity)> open, Func<string, bool> about)
    {
        var mine = open.Where(f => about(f.RuleKey)).OrderByDescending(f => f.Severity).ToList();
        return new PcComponent(kind, title, line1, line2, fill is { } x ? Math.Clamp(x, 0, 100) : null, mine.Count > 0 ? mine[0].Severity : null, mine.Select(f => f.RuleKey).ToList());
    }

    // What clicking a tile shows. ONE rule, not one per kind, so a new collector's readings and facts show without a change
    // here: a tile about one subject (a datastore, a port, a job, a UniFi device) lists every reading of that subject and the
    // facts kept about it; the System tile lists the device's own facts and its device-wide readings. A metric or fact with
    // no label below shows under its own key -- never dropped.
    private static readonly Dictionary<string, string> FactLabels = new(StringComparer.Ordinal)
    {
        ["hw.model"] = "Model", ["hw.serial"] = "Serial", ["esxi.version"] = "ESXi", ["esxi.hostname"] = "Host name", ["ntp.servers"] = "Time servers",
        ["dsm.version"] = "DSM", ["os.caption"] = "Windows", ["os.build"] = "Build", ["fw.version"] = "Firmware", ["wan.public-ip"] = "Public address",
        ["unifi.site"] = "Site", ["unifi.devices"] = "Devices adopted", ["ports.up"] = "Interfaces up",
    };

    private static readonly Dictionary<string, (string Label, string Unit)> MetricLabels = new(StringComparer.Ordinal)
    {
        ["uptime.hours"] = ("Up", " hours"), ["clock.offset.s"] = ("Clock offset", " s"), ["temp.c"] = ("Temperature", " °C"), ["cpu.pct"] = ("CPU", "%"),
        ["mem.pct"] = ("Memory used", "%"), ["vms.running"] = ("VMs running", ""), ["sensors.count"] = ("Sensors read", ""),
        ["datastore.free.pct"] = ("Free", "%"), ["volume.free.pct"] = ("Free", "%"), ["disk.free.pct"] = ("Free", "%"), ["repo.free.pct"] = ("Free", "%"),
        ["disk.temp.c"] = ("Temperature", " °C"), ["disks.count"] = ("Disks", ""), ["update.age.days"] = ("Last update installed", " days ago"),
        ["ports.up"] = ("Interfaces up", ""), ["port.up"] = ("Link", ""), ["port.devices"] = ("Devices behind it", ""), ["port.in-errors"] = ("Errors in", ""),
        ["devices.total"] = ("Devices", ""), ["devices.online"] = ("Online", ""), ["alarms.open"] = ("Alarms open", ""), ["unifi.seen.min"] = ("Last checked in", " min ago"),
        ["ping.ms"] = ("Round trip", " ms"), ["ping.loss.pct"] = ("Loss", "%"), ["charge.pct"] = ("Charge", "%"), ["runtime.min"] = ("Runtime", " min"),
        ["load.pct"] = ("Load", "%"), ["job.age.hours"] = ("Last run", " hours ago"),
        ["supply.pct"] = ("Left", "%"), ["pages.total"] = ("Pages printed", ""),
    };

    private static string Value(DeviceReading r)
    {
        if (r.Metric == "port.up") return r.Value >= 1 ? "up" : "down";
        if (r.Metric == "uptime.hours") return r.Value >= 48 ? $"{(r.Value / 24).ToString("0.#", CultureInfo.InvariantCulture)} days" : $"{r.Value:0} hours";
        if (r.Metric == "unifi.seen.min" && r.Value < 0) return "never";
        var unit = MetricLabels.TryGetValue(r.Metric, out var l) ? l.Unit : "";
        return r.Value.ToString(Math.Abs(r.Value) < 10 && r.Value % 1 != 0 ? "0.#" : "#,0", CultureInfo.InvariantCulture) + unit;
    }

    private static string LabelOf(string metric) => MetricLabels.TryGetValue(metric, out var l) ? l.Label : metric;

    /// <summary>Every reading of one subject, and the facts keyed to it ("veeam.result:{job}", "port.attached:{n}" one row per
    /// device, a UniFi device's fields).</summary>
    internal static IReadOnlyList<PartInfo> SubjectInfo(string subject, IReadOnlyDictionary<string, string> facts, IReadOnlyList<DeviceReading> readings, string? factSuffix = null)
    {
        var rows = new List<PartInfo>();
        if (facts.TryGetValue($"unifi.device:{subject}", out var json) && UniFiDevice.Parse(json) is { } dev)
            rows.AddRange(PcComponents.Rows(("Name", dev.Name), ("Model", dev.Model), ("Address", dev.Ip), ("MAC", subject), ("Firmware", dev.Version),
                ("Update", dev.Upgradable ? "waiting -- the controller has a newer firmware" : "none waiting")));
        foreach (var r in readings.Where(r => r.Subject == subject).GroupBy(r => r.Metric).Select(g => g.OrderByDescending(r => r.AtUtc).First()).OrderBy(r => r.Metric, StringComparer.Ordinal))
            rows.Add(new PartInfo(LabelOf(r.Metric), Value(r)));
        foreach (var (key, value) in facts.Where(f => f.Key.EndsWith(":" + (factSuffix ?? subject), StringComparison.Ordinal) && !f.Key.StartsWith("unifi.device:", StringComparison.Ordinal))
                     .OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            if (key.StartsWith("port.attached:", StringComparison.Ordinal))
                rows.AddRange(value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(a => new PartInfo("Attached", a)));
            else rows.Add(new PartInfo(key.StartsWith("veeam.result:", StringComparison.Ordinal) ? "Last result" : key[..key.IndexOf(':')], value));
        }
        var at = readings.Where(r => r.Subject == subject).Select(r => (DateTime?)r.AtUtc).Max();
        if (at is { } t) rows.Add(new PartInfo("Read", $"{t.ToLocalTime():yyyy-MM-dd HH:mm}"));
        return rows;
    }

    /// <summary>The device itself: its facts (not those keyed to one part) and its device-wide readings.</summary>
    internal static IReadOnlyList<PartInfo> DeviceInfo(IReadOnlyDictionary<string, string> facts, IReadOnlyList<DeviceReading> readings)
    {
        var rows = facts.Where(f => !f.Key.Contains(':')).OrderBy(f => FactLabels.ContainsKey(f.Key) ? 0 : 1).ThenBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => new PartInfo(FactLabels.TryGetValue(f.Key, out var l) ? l : f.Key, f.Value)).ToList();
        rows.AddRange(readings.Where(r => r.Subject.Length == 0).GroupBy(r => r.Metric).Select(g => g.OrderByDescending(r => r.AtUtc).First())
            .Where(r => !rows.Any(x => x.Label == LabelOf(r.Metric)))
            .OrderBy(r => r.Metric, StringComparer.Ordinal).Select(r => new PartInfo(LabelOf(r.Metric), Value(r))));
        return rows;
    }

    /// <summary>Every subject of one metric as a row: the NAS's disks by temperature, the internet's targets by round trip.</summary>
    private static IReadOnlyList<PartInfo> EachInfo(IReadOnlyList<DeviceReading> readings, params string[] metrics)
        => readings.Where(r => metrics.Contains(r.Metric) && r.Subject.Length > 0).GroupBy(r => r.Subject)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PartInfo(g.Key, string.Join(" · ", metrics.Select(m => g.Where(r => r.Metric == m).OrderByDescending(r => r.AtUtc).FirstOrDefault())
                .Where(r => r is not null).Select(r => $"{LabelOf(r!.Metric).ToLowerInvariant()} {Value(r)}")))).ToList();
}
