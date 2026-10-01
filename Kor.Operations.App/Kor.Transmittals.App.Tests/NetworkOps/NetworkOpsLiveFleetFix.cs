#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.NetworkOps;
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;

namespace Kor.Operations.App.Tests.NetworkOps;

/// <summary>
/// Runs ONE catalog fix on every PC that is on now, through the LIVE service and its audit, signed in as the person
/// running it (KOR_NETWORKOPS_FLEET_FIX=&lt;fix id&gt;). Optionally then proves Wake-on-LAN end to end on one PC nobody is
/// using (KOR_NETWORKOPS_WAKE_TEST=&lt;pc&gt;): shuts it down, waits until it stops answering, presses Wake, waits for it to
/// answer. Writes everything to %TEMP%\kor-networkops-fleetfix-live.txt.
///
/// WHAT IT COVERS: the fix on the real fleet (each run's result), and one real shutdown-and-wake. WHAT IT DOES NOT: the
/// PCs that are off now (they get the fix when they are next on: the finding flags them), or a PC whose BIOS blocks wake
/// other than the one tested. A SAME-CLASS FAULT IT WOULD NOT CATCH: the wake test PC waking does not prove an ASUS or
/// Gigabyte board will -- their BIOS cannot be read remotely; only waking each one proves it.
/// </summary>
[Trait("Speed", "Slow")]
public sealed class NetworkOpsLiveFleetFix
{
    [Fact]
    public async Task Live_fix_the_fleet_and_optionally_prove_wake()
    {
        var fixId = Environment.GetEnvironmentVariable("KOR_NETWORKOPS_FLEET_FIX");
        var wakeTest = Environment.GetEnvironmentVariable("KOR_NETWORKOPS_WAKE_TEST");
        var wakeOnly = Environment.GetEnvironmentVariable("KOR_NETWORKOPS_WAKE_ONLY");   // press Wake on one PC, nothing else
        if (string.IsNullOrEmpty(fixId) && string.IsNullOrEmpty(wakeTest) && string.IsNullOrEmpty(wakeOnly)) return;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kor.Operations.App", "App.config"))) dir = dir.Parent;
        var settings = System.Xml.Linq.XDocument.Load(Path.Combine(dir!.FullName, "Kor.Operations.App", "App.config"))
            .Descendants("appSettings").SelectMany(s => s.Elements("add")).ToDictionary(e => (string)e.Attribute("key")!, e => (string)e.Attribute("value")!);
        var client = new NetworkOpsClient(settings[NetworkOpsClient.BaseUrlKey], settings[NetworkOpsClient.ScopeKey], settings[NetworkOpsClient.PinKey],
            settings["Graph.TenantId"], settings["Graph.ClientId"]);
        var ct = CancellationToken.None;
        var report = new StringBuilder();
        var path = Path.Combine(Path.GetTempPath(), "kor-networkops-fleetfix-live.txt");

        var fleet = await client.GetFleetAsync(ct);
        if (!string.IsNullOrEmpty(wakeOnly))
        {
            var target = fleet.Devices.Single(d => d.Name.Equals(wakeOnly, StringComparison.OrdinalIgnoreCase));
            var started = DateTime.UtcNow;
            var id = await client.WakeAsync(target.DeviceId, ct);
            ActionRow? w = null;
            for (var i = 0; i < 90 && w?.Status is not ("Done" or "Failed" or "Refused"); i++) { await Task.Delay(5000); w = await client.GetActionAsync(id, ct); }
            File.WriteAllText(path, $"wake {target.Name} (action {id}): {w?.Status} | {w?.Detail} | {(int)(DateTime.UtcNow - started).TotalSeconds} s{Environment.NewLine}");
            return;
        }
        var now = DateTime.UtcNow;
        var on = fleet.Devices.Where(d => d.AgentConnected || d.LastReachableUtc is { } r && now - r < TimeSpan.FromHours(2)).OrderBy(d => d.Name).ToList();
        if (!string.IsNullOrEmpty(fixId)) await FixFleetAsync(client, fixId, on, fleet.Devices.Count, report, path, ct);
        foreach (var name in (wakeTest ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            await WakeTestAsync(client, on.Single(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)), report, path, ct);
    }

    private static async Task FixFleetAsync(NetworkOpsClient client, string fixId, List<DeviceRow> on, int total, StringBuilder report, string path, CancellationToken ct)
    {
        report.AppendLine($"{fixId} on {on.Count} of {total} PCs (on now)");
        var runs = new Dictionary<long, DeviceRow>();
        foreach (var d in on)
        {
            var (id, refused, _) = await client.RequestFixAsync(d.DeviceId, new FixRequest(fixId, null, null, "fleet run", false), ct);
            if (id is { } x) runs[x] = d; else report.AppendLine($"{d.Name}: refused: {refused}");
        }
        var results = new Dictionary<long, ActionRow?>();
        for (var i = 0; i < 90 && results.Count(r => r.Value?.Status is "Done" or "Failed" or "Refused") < runs.Count; i++)
        {
            await Task.Delay(5000);
            foreach (var id in runs.Keys.Where(k => results.GetValueOrDefault(k)?.Status is not ("Done" or "Failed" or "Refused")))
                results[id] = await client.GetActionAsync(id, ct);
        }
        foreach (var (id, d) in runs.OrderBy(r => r.Value.Name))
            report.AppendLine($"{d.Name}: {results.GetValueOrDefault(id)?.Status} | {results.GetValueOrDefault(id)?.Detail}");
        File.WriteAllText(path, report.ToString());
    }

    private static async Task WakeTestAsync(NetworkOpsClient client, DeviceRow pc, StringBuilder report, string path, CancellationToken ct)
    {
        report.AppendLine($"WAKE TEST on {pc.Name} ({pc.Presence})");
        if (pc.PresenceState is "Active" or "Locked" or "RemoteOnly")
        {
            report.AppendLine("NOT shut down: somebody is signed in to it. Pick a PC nobody is using.");
            File.WriteAllText(path, report.ToString());
            return;
        }
        var (stop, why, _) = await client.RequestFixAsync(pc.DeviceId,
            new FixRequest(Kor.Operations.NetworkOps.Core.Actions.FixCatalog.RunCommand, "& shutdown.exe /s /t 20 /d p:4:1 /c 'KOR IT: Wake-on-LAN test, back in a few minutes'; if ($LASTEXITCODE -eq 0) { 'shutdown in 20 s' } else { \"shutdown.exe refused: exit $LASTEXITCODE\" }", null, "Wake-on-LAN test", false), ct);
        report.AppendLine($"shutdown: action {stop} {why}");
        var off = DateTime.UtcNow;
        var gone = false;
        for (var i = 0; i < 40 && !gone; i++)
        {
            await Task.Delay(15000);
            gone = (await client.GetFleetAsync(ct)).Devices.Single(d => d.DeviceId == pc.DeviceId) is { AgentConnected: false };
        }
        report.AppendLine($"stopped answering: {gone} after {(int)(DateTime.UtcNow - off).TotalSeconds} s");
        await Task.Delay(TimeSpan.FromSeconds(60));   // let it reach S5, not catch it on the way down
        var wake = await client.WakeAsync(pc.DeviceId, ct);
        ActionRow? w = null;
        for (var i = 0; i < 90 && w?.Status is not ("Done" or "Failed" or "Refused"); i++) { await Task.Delay(5000); w = await client.GetActionAsync(wake, ct); }
        report.AppendLine($"wake (action {wake}): {w?.Status} | {w?.Detail}");
        File.WriteAllText(path, report.ToString());
    }
}
