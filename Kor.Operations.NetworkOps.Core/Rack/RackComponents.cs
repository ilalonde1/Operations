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

        // The box itself: model, software version, its own temperature -- and the findings about the whole device.
        var version = new[] { F("esxi.version"), F("dsm.version"), F("os.caption"), F("fw.version").Length > 0 ? "firmware " + F("fw.version") : "" }.FirstOrDefault(v => v.Length > 0) ?? "";
        var model = F("hw.model");
        if (model.Length > 0 || version.Length > 0)
            tiles.Add(Make("system", "System", model.Length > 0 ? model : version, model.Length > 0 ? version : "", null, open,
                k => k == RackResult.UnreachableRule || k is "syno.update" or "syno.system" or "syno.power" or "syno.fan" or "syno.cpu-fan" or "syno.temp"
                     or "esxi.maintenance" or "esxi.sensors-blind" or "server.reboot-pending" or "unifi.firmware" || k.StartsWith("esxi.sensor:", StringComparison.Ordinal)));

        if (One("uptime.hours") is { } up)
            tiles.Add(Make("uptime", "Up", up >= 48 ? $"{up / 24:0.#} days" : $"{N(up)} hours",
                One("clock.offset.s") is { } off ? $"clock {(Math.Abs(off) < 1 ? "in step" : $"{off:+0;-0} s off")}" : One("temp.c") is { } t ? $"{N(t)} °C inside" : "",
                null, open, k => k is "esxi.clock" or "esxi.ntp" or "esxi.ptp" or "switch.rebooted"));

        if (One("cpu.pct") is { } cpu) tiles.Add(Make("cpu", "CPU", $"{N(cpu)}%", One("sensors.count") is { } s ? $"{N(s)} sensors read" : "", cpu, open, _ => false));
        if (One("mem.pct") is { } mem) tiles.Add(Make("memory", "Memory", $"{N(mem)}% used", "", mem, open, k => k == "esxi.memory"));
        if (One("vms.running") is { } vms)
            tiles.Add(Make("vms", "VMs", $"{N(vms)} running", "", null, open, k => k.StartsWith("esxi.vm-off:", StringComparison.Ordinal) || k.StartsWith("esxi.vm-tools:", StringComparison.Ordinal)));

        foreach (var d in Each("datastore.free.pct"))
            tiles.Add(Make("datastore", "Datastore", d.Subject, $"{N(d.Value)}% free", 100 - d.Value, open,
                k => k == $"esxi.datastore-full:{d.Subject}" || k == $"esxi.datastore-lost:{d.Subject}" || k == $"esxi.stale-mount:{d.Subject}"));
        foreach (var v in Each("volume.free.pct"))
            tiles.Add(Make("volume", "Volume", v.Subject, $"{N(v.Value)}% free", 100 - v.Value, open,
                k => k == $"syno.volume-full:{v.Subject}" || k == $"syno.raid:{v.Subject}"));
        // A NAS's disks as one tile: how many, the hottest, and every disk or RAID finding (one bad disk colours it).
        var temps = Each("disk.temp.c").ToList();
        if (One("disks.count") is { } disks || temps.Count > 0)
            tiles.Add(Make("disks", "Disks", $"{N(One("disks.count") ?? temps.Count)} disks", temps.Count > 0 ? $"hottest {N(temps.Max(x => x.Value))} °C" : "", null, open,
                k => k.StartsWith("syno.disk:", StringComparison.Ordinal) || (k.StartsWith("syno.raid:", StringComparison.Ordinal) && !Each("volume.free.pct").Any(v => k == $"syno.raid:{v.Subject}"))));
        foreach (var d in Each("disk.free.pct"))
            tiles.Add(Make("drive", $"{d.Subject.TrimEnd(':')}:", $"{N(100 - d.Value)}% used", $"{N(d.Value)}% free", 100 - d.Value, open,
                k => k == $"server.disk-full:{d.Subject}" || k == $"server.disk-full:{d.Subject.TrimEnd(':')}" || k.StartsWith("server.storage-errors", StringComparison.Ordinal)));
        if (One("update.age.days") is { } age)
            tiles.Add(Make("updates", "Updates", age < 0 ? "never seen" : $"{N(age)} days ago", "last update installed", null, open, k => k == "server.unpatched"));
        if (open.Any(f => f.RuleKey.StartsWith("server.service-stopped:", StringComparison.Ordinal) || f.RuleKey.StartsWith("server.vss-writer:", StringComparison.Ordinal)))
            tiles.Add(Make("services", "Services", "something stopped", "see the findings", null, open,
                k => k.StartsWith("server.service-stopped:", StringComparison.Ordinal) || k.StartsWith("server.vss-writer:", StringComparison.Ordinal)));

        if (One("ports.up") is { } ports)
        {
            var erring = Each("port.in-errors").Count(p => p.Value > 0);
            tiles.Add(Make("ports", "Ports", $"{N(ports)} up", erring > 0 ? $"{erring} with errors" : "no errors", null, open, k => k.StartsWith("switch.port-down:", StringComparison.Ordinal)));
        }
        if (One("devices.total") is { } total)
            tiles.Add(Make("devices", "Devices", $"{N(One("devices.online") ?? 0)} of {N(total)} online", F("unifi.site"), null, open,
                k => k.StartsWith("unifi.offline:", StringComparison.Ordinal) || k == "unifi.unnamed"));
        if (One("alarms.open") is { } alarms) tiles.Add(Make("alarms", "Alarms", alarms == 0 ? "none" : $"{N(alarms)} open", "", null, open, k => k == "unifi.alarms"));

        // Pinged per target (several destinations): the tile shows the WORST of them, latency and loss.
        var pings = Each("ping.ms").ToList();
        var losses = Each("ping.loss.pct").ToList();
        if (pings.Count > 0 || losses.Count > 0)
            tiles.Add(Make("internet", "Internet", pings.Count > 0 ? $"{N(pings.Max(p => p.Value))} ms" : "no reply",
                $"{N(losses.Count > 0 ? losses.Max(l => l.Value) : 0)}% loss{(F("wan.public-ip").Length > 0 ? " · " + F("wan.public-ip") : "")}", null, open,
                k => k.StartsWith("net.", StringComparison.Ordinal)));

        if (One("charge.pct") is { } charge) tiles.Add(Make("charge", "Battery", $"{N(charge)}% charged", "", charge, open, k => k is "ups.battery-low" or "ups.replace-battery" or "ups.on-battery"));
        if (One("runtime.min") is { } runtime) tiles.Add(Make("runtime", "Runtime", $"{N(runtime)} min", "on battery, at this load", null, open, k => k == "ups.short-runtime"));
        if (One("load.pct") is { } load) tiles.Add(Make("load", "Load", $"{N(load)}%", "", load, open, k => k is "ups.bypass" or "ups.output-off"));

        foreach (var j in Each("job.age.hours"))
            tiles.Add(Make("job", "Backup job", j.Subject, $"{F($"veeam.result:{j.Subject}") switch { "" => "last run", var r => r }} · {(j.Value >= 48 ? $"{j.Value / 24:0.#} d" : $"{N(j.Value)} h")} ago", null, open,
                k => k.EndsWith(":" + j.Subject, StringComparison.Ordinal) && k.StartsWith("veeam.", StringComparison.Ordinal) && !k.StartsWith("veeam.repo", StringComparison.Ordinal)));
        foreach (var r in Each("repo.free.pct"))
            tiles.Add(Make("repo", "Repository", r.Subject, $"{N(r.Value)}% free", 100 - r.Value, open,
                k => k == $"veeam.repo-full:{r.Subject}" || k == $"veeam.repo-offline:{r.Subject}"));
        return tiles;
    }

    private static PcComponent Make(string kind, string title, string line1, string line2, double? fill,
        IReadOnlyList<(string RuleKey, Severity Severity)> open, Func<string, bool> about)
    {
        var mine = open.Where(f => about(f.RuleKey)).OrderByDescending(f => f.Severity).ToList();
        return new PcComponent(kind, title, line1, line2, fill is { } x ? Math.Clamp(x, 0, 100) : null, mine.Count > 0 ? mine[0].Severity : null, mine.Select(f => f.RuleKey).ToList());
    }
}
