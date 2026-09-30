#nullable enable
using System.Globalization;
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Rack;

/// <summary>
/// A Synology (DSM or DSM UC) over SNMPv3: SYNOLOGY-SYSTEM-MIB, -DISK-MIB and -RAID-MIB. The UC3200's PSU2
/// dropped out repeatedly from 2023 with nobody told; its alerts now mail, and this watches it too.
///
/// Volume free space is checked only where it means something: a box that holds THICK iSCSI LUNs (the Veeam
/// repositories) shows its volume nearly full by design -- the LUN file fills it -- and the real free space
/// is inside the LUN, which the Veeam rules watch. Hence VolumeFreeWarnPct = 0 for those.
/// </summary>
public static class SynologyRules
{
    public const string Oid = "1.3.6.1.4.1.6574";
    /// <summary>The subtrees a walk must cover: system, disks, RAID/volumes.</summary>
    public static readonly string[] Tables = [Oid + ".1", Oid + ".2.1.1", Oid + ".3.1.1"];

    /// <param name="values">Walk results: numeric OID (no leading dot) -> value as text.</param>
    public static RackResult Evaluate(IReadOnlyDictionary<string, string> values, int volumeFreeWarnPct)
    {
        var b = new RackBuilder();
        string? V(string suffix) => values.TryGetValue($"{Oid}.{suffix}", out var s) ? s.Trim('"') : null;
        int? I(string suffix) => int.TryParse(V(suffix), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
        double? D(string suffix) => double.TryParse(V(suffix), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;

        b.Fact("hw.model", V("1.5.1.0")); b.Fact("hw.serial", V("1.5.2.0")); b.Fact("dsm.version", V("1.5.3.0"));
        if (I("1.1.0") == 2) b.Raise("syno.system", Severity.Critical, "Synology reports a system failure", "systemStatus = failed");
        if (I("1.3.0") == 2) b.Raise("syno.power", Severity.Critical, "A power supply has failed", "powerStatus = failed (the UC3200's PSU2 dropped out repeatedly 2023-2026)");
        if (I("1.4.1.0") == 2) b.Raise("syno.fan", Severity.Critical, "A system fan has failed", "systemFanStatus = failed");
        if (I("1.4.2.0") == 2) b.Raise("syno.cpu-fan", Severity.Critical, "The CPU fan has failed", "cpuFanStatus = failed");
        if (D("1.2.0") is { } temp)
        {
            b.Metric("temp.c", temp);
            if (temp >= 70) b.Raise("syno.temp", Severity.Critical, "Synology is overheating", $"{temp:0} C");
            else if (temp >= 60) b.Raise("syno.temp", Severity.Warning, "Synology is running hot", $"{temp:0} C");
        }
        if (I("1.5.4.0") == 1) b.Raise("syno.update", Severity.Info, "A DSM update is available", $"running {V("1.5.3.0")}; install it in a maintenance window, not live");

        // Disks: status (1 normal ... 5 crashed) and, on DSM 7, the health verdict (1 normal, 2 warning, 3 critical, 4 failing).
        var diskIdx = values.Keys.Where(k => k.StartsWith(Oid + ".2.1.1.2.", StringComparison.Ordinal)).Select(k => k[(Oid.Length + 9)..]).ToList();
        foreach (var i in diskIdx)
        {
            var name = V($"2.1.1.2.{i}") ?? $"disk {i}";
            var status = I($"2.1.1.5.{i}");
            var health = I($"2.1.1.13.{i}");
            if (D($"2.1.1.6.{i}") is { } t) b.Metric("disk.temp.c", t, name);
            if (status is 4 or 5) b.Raise($"syno.disk:{name}", Severity.Critical, $"{name} has failed", status == 5 ? "disk status = crashed" : "system partition failed");
            else if (health is 3 or 4) b.Raise($"syno.disk:{name}", Severity.Critical, $"{name} is failing", $"DSM disk health = {(health == 4 ? "failing" : "critical")}");
            else if (health == 2) b.Raise($"syno.disk:{name}", Severity.Warning, $"{name} has a health warning", "DSM disk health = warning (bad sectors or SMART attributes): plan a replacement");
            else if (status is 3) b.Raise($"syno.disk:{name}", Severity.Warning, $"{name} is not initialized", "disk status = not initialized");
        }
        b.Metric("disks.count", diskIdx.Count);

        // RAID groups / volumes: 1 normal; 11 degraded; 12 crashed; others are work in progress.
        var raidIdx = values.Keys.Where(k => k.StartsWith(Oid + ".3.1.1.2.", StringComparison.Ordinal)).Select(k => k[(Oid.Length + 9)..]).ToList();
        foreach (var i in raidIdx)
        {
            var name = V($"3.1.1.2.{i}") ?? $"raid {i}";
            switch (I($"3.1.1.3.{i}"))
            {
                case 12: b.Raise($"syno.raid:{name}", Severity.Critical, $"{name} has CRASHED", "RAID status = crashed: data at risk"); break;
                case 11: b.Raise($"syno.raid:{name}", Severity.Critical, $"{name} is degraded", "RAID status = degraded: one more disk failure loses it"); break;
                case 2 or 7: b.Raise($"syno.raid:{name}", Severity.Warning, $"{name} is rebuilding", "RAID is repairing / syncing"); break;
            }
            if (D($"3.1.1.4.{i}") is { } free && D($"3.1.1.5.{i}") is { } total && total > 0 && name.StartsWith("Volume", StringComparison.OrdinalIgnoreCase))
            {
                var pct = 100 * free / total;
                b.Metric("volume.free.pct", Math.Round(pct, 1), name);
                if (volumeFreeWarnPct > 0 && pct < volumeFreeWarnPct)
                    b.Raise($"syno.volume-full:{name}", pct < volumeFreeWarnPct / 2.0 ? Severity.Critical : Severity.Warning, $"{name} is filling up",
                        $"{free / 1e12:0.0} TB free of {total / 1e12:0.0} TB ({pct:0.0}%)");
            }
        }
        var unhealthy = b.Findings.Count(f => f.Severity >= Severity.Warning);
        return b.Done($"{V("1.5.1.0")} · {diskIdx.Count} disks · {(unhealthy == 0 ? "all healthy" : $"{unhealthy} problem(s)")}");
    }
}
