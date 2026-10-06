#nullable enable
using System.Globalization;
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Rack;

/// <summary>
/// An ESXi host, from Rack/esxi-health.py (run on the host through hostd with a local ticket). Each rule is a
/// fault this rack has actually had or nearly had:
///   clock off / NTP not running / PTP on   -- host .10 ran 35 min slow on 2026-09-29 (PTP on, no PTP master)
///   no hardware sensors                    -- host .16 reported 0 sensors (CIM off); a PSU died unseen for 173 days
///   a sensor not green                     -- that PSU
///   datastore low / inaccessible           -- every VM lives on the UC3200 datastore
///   a production VM off, or Tools not running on it (the shutdown chain needs Tools)
///   maintenance mode                       -- a cleanly-shut host comes back IN maintenance mode (2026-09-25)
///
/// Class 2 ("configured right, not just up"): every powered-on VM on VMXNET3 (esxi.vm-nic-legacy); no iSCSI path that keeps
/// dropping (esxi.iscsi-flapping, from the host's own vmkernel.log); and every ACTIVE path to the SAN running over the
/// dedicated 192.168.200.x storage network, not the 1G management subnet (esxi.iscsi-wrong-path). DOES NOT COVER a NIC on a
/// powered-OFF VM, a legacy SCSI controller, a STANDBY (State=off) path's subnet, or the storage MTU directly. A SAME-CLASS
/// FAULT IT WOULD MISS: a VM on VMXNET3 but an old SCSI controller; flapping that rotated out of the current log; and an
/// active path on a third subnet that is not the management one (only the 192.168.200.x-or-flag test is applied).
/// </summary>
public static class EsxiRules
{
    /// <summary>iscsivmk_StopConnection events in the current vmkernel.log above this = a flapping path (Class 2 f).</summary>
    public const int IscsiFlapThreshold = 8;

    /// <summary>The dedicated storage network for the SAN (MTU 9000). An ACTIVE iSCSI path whose connection is NOT on it is
    /// on the 1G management subnet instead -- the wrong-path fault (Class 2 e).</summary>
    public const string StorageSubnetPrefix = "192.168.200.";

    public static string Script
    {
        get
        {
            using var s = typeof(EsxiRules).Assembly.GetManifestResourceStream("Rack.esxi-health.py")
                ?? throw new InvalidOperationException("the embedded ESXi health script is missing");
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }
    }

    /// <param name="productionVms">VMs that must be running (the shutdown plan's waves and controller).</param>
    /// <param name="measuredAtUtc">When NetworkOps received the snapshot: the clock check compares the host's time with it.</param>
    public static RackResult Evaluate(string json, IReadOnlyCollection<string> productionVms, DateTime measuredAtUtc)
    {
        using var doc = JsonDocument.Parse(json);
        var h = doc.RootElement;
        var b = new RackBuilder();
        string? S(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        b.Fact("esxi.version", S(h, "version"));
        b.Fact("hw.model", $"{S(h, "vendor")} {S(h, "model")}".Trim());
        b.Fact("hw.serial", S(h, "serial"));
        b.Fact("esxi.hostname", S(h, "name"));
        b.Fact("ntp.servers", h.TryGetProperty("ntpServers", out var ns) ? string.Join(", ", ns.EnumerateArray().Select(x => x.GetString())) : null);

        // Clock: the host's own time against when we received it (seconds of transit are noise; minutes are not).
        if (S(h, "hostTimeUtc") is { } ht && DateTime.TryParse(ht, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var hostTime))
        {
            var offset = (hostTime - measuredAtUtc).TotalSeconds;
            b.Metric("clock.offset.s", Math.Round(offset, 1));
            if (Math.Abs(offset) > 300) b.Raise("esxi.clock", Severity.Critical, "Host clock is wrong", $"host time is {RackBuilder.N(offset / 60, 1)} min off (domain time and Kerberos break past 5 min)");
            else if (Math.Abs(offset) > 60) b.Raise("esxi.clock", Severity.Warning, "Host clock is drifting", $"host time is {RackBuilder.N(offset)} s off");
        }
        bool Running(string svc) => h.TryGetProperty(svc, out var s) && s.ValueKind == JsonValueKind.Object && s.GetProperty("running").GetBoolean();
        string Policy(string svc) => h.TryGetProperty(svc, out var s) && s.ValueKind == JsonValueKind.Object ? s.GetProperty("policy").GetString() ?? "" : "";
        if (!Running("ntp") || Policy("ntp") != "on")
            b.Raise("esxi.ntp", Severity.Warning, "Host time sync (NTP) is not on", $"ntpd running={Running("ntp")}, start policy={Policy("ntp")}");
        if (Running("ptp"))
            b.Raise("esxi.ptp", Severity.Warning, "Host is on PTP, not NTP", "PTP needs a PTP master on the network and KOR has none: the clock free-runs (host .10 drifted 35 min this way)");

        if (h.GetProperty("maintenanceMode").GetBoolean())
            b.Raise("esxi.maintenance", Severity.Warning, "Host is in maintenance mode", "no VM can be powered on until it exits (vim-cmd hostsvc/maintenance_mode_exit)");
        if (S(h, "bootTime") is { } bt && DateTime.TryParse(bt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var boot))
            b.Metric("uptime.hours", Math.Round((measuredAtUtc - boot).TotalHours, 1));

        var sensors = h.GetProperty("sensors").EnumerateArray().ToList();
        b.Metric("sensors.count", sensors.Count);
        if (sensors.Count == 0)
            b.Raise("esxi.sensors-blind", Severity.Warning, "Host reports no hardware sensors",
                "power supplies, fans and temperatures are invisible: enable CIM (esxcli system wbem set -e true) -- a PSU died unseen for 173 days this way");
        foreach (var s in sensors)
        {
            var state = (s.GetProperty("state").GetString() ?? "").ToLowerInvariant();
            if (state is "green" or "unknown" or "") continue;
            var name = s.GetProperty("name").GetString() ?? "?";
            b.Raise($"esxi.sensor:{name}", state == "red" ? Severity.Critical : Severity.Warning, $"Hardware sensor not healthy: {name}", $"{name} is {state}");
        }

        foreach (var d in h.GetProperty("datastores").EnumerateArray())
        {
            var name = d.GetProperty("name").GetString() ?? "?";
            // Veeam's instant-recovery NFS mounts (VeeamBackup_*) are left behind inaccessible after a restore: noise, not a fault.
            var veeamMount = name.StartsWith("VeeamBackup_", StringComparison.OrdinalIgnoreCase);
            if (!d.GetProperty("accessible").GetBoolean())
            {
                if (veeamMount) b.Raise($"esxi.stale-mount:{name}", Severity.Info, "Leftover Veeam datastore", $"{name} is mounted but inaccessible (an old instant-recovery mount); it can be unmounted");
                else b.Raise($"esxi.datastore-lost:{name}", Severity.Critical, $"Datastore {name} is not accessible", "VMs on it cannot run");
                continue;
            }
            var cap = d.GetProperty("capacityGb").GetDouble(); var free = d.GetProperty("freeGb").GetDouble();
            if (cap <= 0) continue;
            var pct = 100 * free / cap;
            b.Metric("datastore.free.pct", Math.Round(pct, 1), name);
            if (pct < 5) b.Raise($"esxi.datastore-full:{name}", Severity.Critical, $"Datastore {name} is almost full", $"{RackBuilder.N(free)} GB free of {RackBuilder.N(cap)} GB ({RackBuilder.N(pct, 1)}%)");
            else if (pct < 15) b.Raise($"esxi.datastore-full:{name}", Severity.Warning, $"Datastore {name} is filling up", $"{RackBuilder.N(free)} GB free of {RackBuilder.N(cap)} GB ({RackBuilder.N(pct, 1)}%)");
        }

        var vms = h.GetProperty("vms").EnumerateArray().Select(v => (Name: v.GetProperty("name").GetString() ?? "", Power: v.GetProperty("power").GetString() ?? "",
            Tools: S(v, "tools") ?? "", Nics: v.TryGetProperty("nics", out var vn) ? vn.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [])).ToList();
        var on = vms.Count(v => v.Power == "poweredOn");
        b.Metric("vms.running", on);
        foreach (var v in vms.Where(v => productionVms.Contains(v.Name, StringComparer.OrdinalIgnoreCase)))
        {
            if (v.Power != "poweredOn") b.Raise($"esxi.vm-off:{v.Name}", Severity.Critical, $"{v.Name} is not running", $"power state {v.Power}");
            else if (v.Tools != "guestToolsRunning") b.Raise($"esxi.vm-tools:{v.Name}", Severity.Warning, $"VMware Tools not running in {v.Name}",
                $"tools {v.Tools}: the UPS shutdown chain cannot shut it down cleanly without them");
        }

        // Class 2 (d): every POWERED-ON VM should use VMXNET3. E1000/E1000e are legacy and throttle -- Kor-BK01 ran E1000e
        // and capped backups near 1G before 6 Oct 2026. A powered-off VM (Kor-Lab01_proxy) is not judged.
        foreach (var v in vms.Where(v => v.Power == "poweredOn" && v.Nics.Any(t => t.Length > 0 && !t.Equals("VirtualVmxnet3", StringComparison.OrdinalIgnoreCase))))
        {
            var legacy = string.Join(", ", v.Nics.Where(t => t.Length > 0 && !t.Equals("VirtualVmxnet3", StringComparison.OrdinalIgnoreCase)).Select(t => t.Replace("Virtual", "")));
            b.Raise($"esxi.vm-nic-legacy:{v.Name}", Severity.Warning, $"{v.Name} has a legacy virtual NIC", $"{legacy}: switch it to VMXNET3 for full throughput (an E1000e caps Veeam near 1G)");
        }

        // Class 2 (f): a path to the SAN that keeps dropping -- iscsivmk_StopConnection in the current (recent) vmkernel.log.
        if (h.TryGetProperty("iscsiDrops", out var idr) && idr.TryGetInt32(out var drops) && drops >= 0)
        {
            b.Metric("iscsi.drops", drops);
            if (drops >= IscsiFlapThreshold) b.Raise("esxi.iscsi-flapping", Severity.Warning, "An iSCSI connection keeps dropping",
                $"{drops} iscsivmk_StopConnection events in the current vmkernel.log -- a path is flapping (a bad cable or NIC, a standby portal answering, or an MTU / port-binding mismatch)");
        }

        // Class 2 (e): an ACTIVE path to the SAN LUN must run over the dedicated storage network (192.168.200.x / MTU 9000),
        // not the 1G management subnet. .16 ran its active path over 192.168.1.x before 6 Oct and backups ran at half speed.
        if (h.TryGetProperty("iscsiPaths", out var ips) && ips.ValueKind == JsonValueKind.Array)
        {
            var active = 0;
            foreach (var p in ips.EnumerateArray())
            {
                if ((S(p, "state") ?? "") != "active") continue;   // a disabled/standby path on the management net is fine
                active++;
                var local = S(p, "local") ?? ""; var remote = S(p, "remote") ?? "";
                var offStorage = (local.Length > 0 && !local.StartsWith(StorageSubnetPrefix, StringComparison.Ordinal))
                              || (remote.Length > 0 && !remote.StartsWith(StorageSubnetPrefix, StringComparison.Ordinal));
                if (offStorage)
                    b.Raise($"esxi.iscsi-wrong-path:{S(p, "runtime")}", Severity.Warning, "SAN traffic is on the wrong network",
                        $"active path {S(p, "runtime")} runs {(local.Length > 0 ? local : "?")} -> {(remote.Length > 0 ? remote : "?")}, not the {StorageSubnetPrefix}x storage network (MTU 9000): it is using the 1G management NIC and will be slow");
            }
            b.Metric("iscsi.active-paths", active);   // also proves the esxcli read ran (0 = it did not, or no SAN paths)
        }

        var cpu = h.GetProperty("cpuMhzTotal").GetDouble() is var t && t > 0 ? 100 * h.GetProperty("cpuMhzUsed").GetDouble() / t : 0;
        var mem = 100.0 * h.GetProperty("memMbUsed").GetDouble() / Math.Max(1, h.GetProperty("memMbTotal").GetDouble());
        b.Metric("cpu.pct", Math.Round(cpu, 1)); b.Metric("mem.pct", Math.Round(mem, 1));
        if (mem > 95) b.Raise("esxi.memory", Severity.Warning, "Host memory is nearly exhausted", $"{RackBuilder.N(mem, 1)}% of RAM in use");

        return b.Done($"{on} of {vms.Count} VMs running · {sensors.Count} sensors · CPU {RackBuilder.N(cpu)}% · RAM {RackBuilder.N(mem)}%");
    }
}
