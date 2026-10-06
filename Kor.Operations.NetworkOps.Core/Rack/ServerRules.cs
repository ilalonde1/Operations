#nullable enable
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Rack;

/// <summary>
/// A Windows server, from Probes/server.ps1 run on it through the one-shot SCM channel. The rules are the ones
/// this estate has needed: a VSS writer in error (FS01, 30 Sep 2026: three Veeam runs failed on stuck writers and
/// nothing said so directly), a disk filling, an automatic service that is not running (the MCP server and the
/// certificate renewer on APP01), storage errors in the System log, and patching that has stopped (every server
/// 71 days without an update on 30 Sep 2026).
///
/// Class 2 ("up is not the same as configured right"): exactly one ACTIVE antivirus (server.av-conflict / server.av-none)
/// and no NIC in the Public firewall profile (server.nic-public). COVERS Windows Defender (Normal mode + realtime) and
/// Webroot (WRSVC). DOES NOT COVER a third-party AV that is neither Defender nor Webroot (it reads as "no AV"), Defender's
/// exclusion list, or a firewall-profile rule other than the Public category. A SAME-CLASS FAULT IT WOULD MISS: two engines
/// where one is some other product (e.g. CrowdStrike) -- the count would see one and not flag the conflict.
/// </summary>
public static class ServerRules
{
    public static RackResult Evaluate(string json, int unpatchedDays = 45)
    {
        using var doc = JsonDocument.Parse(json);
        var s = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement[0] : doc.RootElement;
        var b = new RackBuilder();
        string Str(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        IEnumerable<JsonElement> Arr(string p) => s.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];

        b.Fact("os.caption", Str(s, "OsCaption")); b.Fact("os.build", Str(s, "OsBuild"));
        var uptime = s.TryGetProperty("UptimeHours", out var u) ? u.GetDouble() : 0;
        b.Metric("uptime.hours", uptime);

        foreach (var d in Arr("Disks"))
        {
            var drive = Str(d, "Drive"); var size = d.GetProperty("SizeGB").GetDouble(); var free = d.GetProperty("FreeGB").GetDouble();
            if (size <= 0) continue;
            var pct = 100 * free / size;
            b.Metric("disk.free.pct", Math.Round(pct, 1), drive);
            if (pct < 5) b.Raise($"server.disk-full:{drive}", Severity.Critical, $"Drive {drive} is almost full", $"{free:0.#} GB free of {size:0} GB ({pct:0.0}%)");
            else if (pct < 10) b.Raise($"server.disk-full:{drive}", Severity.Warning, $"Drive {drive} is filling up", $"{free:0.#} GB free of {size:0} GB ({pct:0.0}%)");
        }

        // A writer in error fails the next backup. "Waiting for completion" with no error is a snapshot in progress: fine.
        foreach (var w in Arr("VssWriters"))
        {
            var name = Str(w, "Name"); var state = Str(w, "State"); var error = Str(w, "LastError");
            if (!error.Equals("No error", StringComparison.OrdinalIgnoreCase) || state.Equals("Failed", StringComparison.OrdinalIgnoreCase))
                b.Raise($"server.vss-writer:{name}", Severity.Critical, $"VSS writer in error: {name}", $"state {state}, last error {error}: the next backup of this server will fail");
        }

        foreach (var svc in Arr("StoppedAutoServices"))
            b.Raise($"server.service-stopped:{Str(svc, "Name")}", Severity.Warning, $"{Str(svc, "Display")} is not running", $"service {Str(svc, "Name")} is set to start automatically but is stopped");

        foreach (var e in Arr("StorageErrors24h"))
        {
            var ev = Str(e, "Event");
            b.Raise($"server.storage-errors:{ev}", Severity.Warning, "Storage errors in the System log", $"{ev} x{e.GetProperty("Count").GetInt32()} in the last 24 h");
        }

        // Antivirus: exactly ONE active engine. Defender active = installed + Normal mode + realtime (Windows Server never
        // auto-passives Defender without MDE, so Normal here is really active); Webroot active = its service is Running.
        // Judged only when the probe actually sent the AV fields (ProbeVersion 2+), so an old probe does not read "no AV".
        if (s.TryGetProperty("WebrootStatus", out _))
        {
            var active = new List<string>();
            if ((s.TryGetProperty("DefenderInstalled", out var di) && di.ValueKind == JsonValueKind.True)
                && (s.TryGetProperty("DefenderRealtime", out var dr) && dr.ValueKind == JsonValueKind.True)
                && Str(s, "DefenderMode").Equals("Normal", StringComparison.OrdinalIgnoreCase)) active.Add("Windows Defender");
            if (Str(s, "WebrootStatus").Equals("Running", StringComparison.OrdinalIgnoreCase)) active.Add("Webroot");
            b.Fact("av.active", active.Count == 0 ? "none" : string.Join(", ", active));
            if (active.Count >= 2)
                b.Raise("server.av-conflict", Severity.Warning, "Two antivirus products are active at once",
                    $"{string.Join(" and ", active)} are both running -- they fight, slow the server, and can leave gaps; keep exactly one");
            else if (active.Count == 0)
                b.Raise("server.av-none", Severity.Critical, "No antivirus is active on this server",
                    "neither Windows Defender (Normal mode + realtime) nor Webroot is running -- the server is unprotected");
        }

        // A server NIC in the Public firewall profile gets the strict public rules; it should be Domain-authenticated (or Private).
        var publicNics = Arr("NicCategories").Count(n => n.ValueKind == JsonValueKind.String && (n.GetString() ?? "").Equals("Public", StringComparison.OrdinalIgnoreCase));
        if (publicNics > 0)
            b.Raise("server.nic-public", Severity.Warning, "A network connection is in the Public firewall profile",
                $"{publicNics} NIC(s) are Public -- a server's connections should be Domain-authenticated or Private, or the firewall applies its strict public rules");

        var updDays = s.TryGetProperty("LastUpdateDays", out var ud) ? ud.GetInt32() : -1;
        if (updDays >= 0) b.Metric("update.age.days", updDays);
        if (updDays > unpatchedDays) b.Raise("server.unpatched", Severity.Warning, "Server has not been patched", $"last update installed {updDays} days ago");
        var reboot = s.TryGetProperty("PendingReboot", out var pr) && pr.ValueKind == JsonValueKind.True;
        if (reboot && uptime > 24 * 7) b.Raise("server.reboot-pending", Severity.Info, "Server is waiting for a restart", $"a restart is pending and it has been up {uptime / 24:0} days");

        var disks = string.Join(", ", Arr("Disks").Select(d => $"{Str(d, "Drive")} {d.GetProperty("FreeGB").GetDouble():0} GB free"));
        return b.Done($"up {uptime / 24:0.#} days · {disks}");
    }
}
