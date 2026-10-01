#nullable enable
using System;
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
/// Brings ONE PC into NetworkOps through the LIVE service, signed in as the person running it, exactly as the device
/// window's buttons do: install the agent, install remote control, then search every machine's updates. Off unless
/// KOR_NETWORKOPS_SETUP_DEVICE names the PC. Writes what happened to %TEMP%\kor-networkops-setup-live.txt.
///
/// WHAT IT COVERS: the agent and Mesh install routes and their audited runs, end to end, on a real PC, and the PC
/// appearing in the Updates view. WHAT IT DOES NOT: anything the buttons do not (no restart, no fix). A SAME-CLASS FAULT
/// IT WOULD NOT CATCH: an agent that installs but never polls reads "Done" here; the fleet row's Agent column shows it.
/// </summary>
[Trait("Speed", "Slow")]
public sealed class NetworkOpsLiveDeviceSetup
{
    [Fact]
    public async Task Live_bring_one_pc_in()
    {
        if (Environment.GetEnvironmentVariable("KOR_NETWORKOPS_SETUP_DEVICE") is not { Length: > 0 } name) return;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kor.Operations.App", "App.config"))) dir = dir.Parent;
        var settings = System.Xml.Linq.XDocument.Load(Path.Combine(dir!.FullName, "Kor.Operations.App", "App.config"))
            .Descendants("appSettings").SelectMany(s => s.Elements("add")).ToDictionary(e => (string)e.Attribute("key")!, e => (string)e.Attribute("value")!);
        var client = new NetworkOpsClient(settings[NetworkOpsClient.BaseUrlKey], settings[NetworkOpsClient.ScopeKey], settings[NetworkOpsClient.PinKey],
            settings["Graph.TenantId"], settings["Graph.ClientId"]);
        var ct = CancellationToken.None;
        var report = new StringBuilder();

        var device = (await client.GetFleetAsync(ct)).Devices.Single(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        report.AppendLine($"{device.Name} (id {device.DeviceId}): agent {device.AgentVersion ?? "none"}, mesh {device.MeshNodeId ?? "none"}");

        async Task Follow(string what, long id)
        {
            ActionRow? a = null;
            for (var i = 0; i < 120 && a?.Status is not ("Done" or "Failed" or "Refused"); i++)
            {
                await Task.Delay(5000);
                a = await client.GetActionAsync(id, ct);
            }
            report.AppendLine($"{what} (action {id}): {a?.Status} | {a?.Detail}");
        }

        if (device.AgentVersion is null) await Follow("agent install", await client.RequestAgentAsync(device.DeviceId, "install", ct));
        if (device.MeshNodeId is null) await Follow("remote control install", await client.RequestMeshAsync(device.DeviceId, ct));

        var trigger = await client.QueueUpdateScanAsync(ct);
        TriggerState? t = null;
        for (var i = 0; i < 180 && t?.CompletedUtc is null; i++) { await Task.Delay(5000); t = await client.GetTriggerAsync(trigger, ct); }
        report.AppendLine($"update search: {t?.Result}");
        var row = (await client.GetUpdatesAsync(ct)).Single(r => r.DeviceId == device.DeviceId);
        report.AppendLine($"updates: scan={row.ScanStatus} | waiting {row.Pending.Count} ({row.Pending.Count(p => p.Security)} security) | due={row.Due} | {string.Join("; ", row.Pending.Select(p => p.Title))}");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "kor-networkops-setup-live.txt"), report.ToString());
    }
}
