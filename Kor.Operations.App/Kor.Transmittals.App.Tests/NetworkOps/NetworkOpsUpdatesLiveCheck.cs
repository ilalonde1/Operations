#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Kor.Operations.App.NetworkOps;
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;

namespace Kor.Operations.App.Tests.NetworkOps;

/// <summary>
/// The Updates view against the LIVE service, signed in as the person running it. Off unless KOR_NETWORKOPS_UPDATES_LIVE=1
/// (it signs in, and it asks every machine to search). It searches everything again, waits for the search, and writes
/// what each machine has to %TEMP%\kor-networkops-updates-live.txt.
///
/// It installs NOTHING unless KOR_NETWORKOPS_UPDATES_INSTALL names exactly one machine; then it installs on that one
/// machine without a restart, follows the run to the end, and records the result and the machine's row after.
///
/// WHAT IT COVERS: the routes, the sign-in, the scan job end to end, and (when asked) one real install and its re-scan.
/// WHAT IT DOES NOT: a restart, a batch, or the refusals (UpdateTests holds those). A SAME-CLASS FAULT IT WOULD NOT
/// CATCH: an install that reports success but left the update installed-pending-restart reads as "done" here too.
/// </summary>
[Trait("Speed", "Slow")]
public sealed class NetworkOpsUpdatesLiveCheck
{
    [Fact]
    public async System.Threading.Tasks.Task Live_search_and_optional_single_install()
    {
        if (Environment.GetEnvironmentVariable("KOR_NETWORKOPS_UPDATES_LIVE") != "1") return;
        // The test host has no App.config of its own: read the app's, from the repo.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kor.Operations.App", "App.config"))) dir = dir.Parent;
        var settings = System.Xml.Linq.XDocument.Load(Path.Combine(dir!.FullName, "Kor.Operations.App", "App.config"))
            .Descendants("appSettings").SelectMany(s => s.Elements("add")).ToDictionary(e => (string)e.Attribute("key")!, e => (string)e.Attribute("value")!);
        var client = new NetworkOpsClient(settings[NetworkOpsClient.BaseUrlKey], settings[NetworkOpsClient.ScopeKey], settings[NetworkOpsClient.PinKey],
            settings["Graph.TenantId"], settings["Graph.ClientId"]);
        var report = new StringBuilder();
        var ct = CancellationToken.None;

        var trigger = (await client.QueueUpdateScanAsync(ct));
        TriggerState? t = null;
        for (var i = 0; i < 180 && t?.CompletedUtc is null; i++)
        {
            await System.Threading.Tasks.Task.Delay(5000);
            t = (await client.GetTriggerAsync(trigger, ct));
        }
        report.AppendLine($"scan trigger {trigger}: {t?.Status} {t?.Result}");
        var rows = (await client.GetUpdatesAsync(ct));
        foreach (var r in rows)
            report.AppendLine($"{r.Name} | target={r.Target} guard={r.Guard} | scan={r.ScanStatus} | pending={r.Pending.Count} ({r.Pending.Count(p => p.Security)} security) | due={r.Due} | reboot={r.RebootPending} | {r.Why}");

        if (Environment.GetEnvironmentVariable("KOR_NETWORKOPS_UPDATES_INSTALL") is { Length: > 0 } name)
        {
            var row = rows.Single(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var outcome = (await client.InstallUpdatesAsync(new UpdateInstallRequest([row.DeviceId], "none", false), ct)).Single();
            report.AppendLine($"install on {name}: action {outcome.ActionId} refused={outcome.Refused} note={outcome.Note}");
            ActionRow? a = null;
            for (var i = 0; outcome.ActionId is { } id && i < 720 && a?.Status is not ("Done" or "Failed" or "Refused"); i++)
            {
                await System.Threading.Tasks.Task.Delay(10000);
                a = (await client.GetActionAsync(id, ct));
            }
            report.AppendLine($"install result: {a?.Status} | {a?.Detail}");
            report.AppendLine($"install output: {a?.Output}");
            var after = (await client.GetUpdatesAsync(ct)).Single(r => r.DeviceId == row.DeviceId);
            report.AppendLine($"after: scan={after.ScanStatus} at {after.ScannedUtc:u} | pending={after.Pending.Count} | due={after.Due} | reboot={after.RebootPending} | last={after.LastInstall}");
        }
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "kor-networkops-updates-live.txt"), report.ToString());
    }
}
