#nullable enable
using System.Collections.Concurrent;
using System.Text;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Probes;
using Kor.Operations.NetworkOps.Core.Updates;
using Kor.Operations.NetworkOps.Service.Alerting;
using Kor.Operations.NetworkOps.Service.Store;
using Kor.Operations.NetworkOps.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service.Updates;

/// <summary>A machine updates can be searched and installed on -- or listed with why not, never silently left out.</summary>
/// <param name="Host">What MachineRunner reaches it by: a PC's name (agent or network), a server's Address.</param>
/// <param name="Guard">"Alone" | "NoRestart" | null (UpdateAloneHosts / UpdateNoRestartHosts).</param>
internal sealed record UpdateTarget(int DeviceId, string Name, string Kind, bool IsServer, string? Host, string? Why, string? Guard);

// What Windows Update has waiting, on every PC and Windows server: Probes/updates.ps1 run ON each machine (its agent, or
// the one-shot SCM route), recorded as an observation, judged by Core/Updates/UpdateRules into the one "updates-due"
// finding this scanner owns -- the health and rack sweeps leave that finding alone. Searched, never installed: installs
// happen only when someone asks (Api: /api/updates/install). The morning after Patch Tuesday it also mails what arrived.
internal sealed class UpdateScanner(NetworkOpsStore store, Agents.MachineRunner runner, IDigestSender digest, IOptions<NetworkOpsOptions> options,
    ILogger<UpdateScanner> log)
{
    public static readonly TimeSpan ScanTimeout = TimeSpan.FromMinutes(6);

    public async Task<IReadOnlyList<UpdateTarget>> TargetsAsync(CancellationToken ct)
    {
        var o = options.Value;
        var list = new List<UpdateTarget>();
        foreach (var (name, id) in await store.DirectoryDevicesAsync(ct))
            list.Add(new UpdateTarget(id, name, "Workstation", false, name, null, GuardOf(name)));

        var rackIds = (await store.FleetSnapshotAsync(ct, rack: true)).Devices.ToDictionary(d => d.Name, d => d.DeviceId, StringComparer.OrdinalIgnoreCase);
        foreach (var d in o.Rack)
        {
            if (!rackIds.TryGetValue(d.Name, out var id)) continue;
            if (d.AppCanRunOn)
                list.Add(new UpdateTarget(id, d.Name, d.Kind, true, d.Address, null, GuardOf(d.Address)));
            else if (d.Kind == "Backup")
                list.Add(new UpdateTarget(id, d.Name, d.Kind, true, null,
                    "APP01 cannot run anything on it (SMB/445 is closed to it): patch it by hand, through Connect, outside the backup windows", null));
        }
        return list.OrderBy(t => t.IsServer).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private string? GuardOf(string host)
    {
        var o = options.Value;
        return o.UpdateAloneHosts.Contains(host, StringComparer.OrdinalIgnoreCase) ? "Alone"
             : o.UpdateNoRestartHosts.Contains(host, StringComparer.OrdinalIgnoreCase) ? "NoRestart"
             : null;
    }

    /// <summary>Searches every target (or the ones named), records each result, and updates the "updates-due" findings.</summary>
    public async Task<string> ScanAsync(IReadOnlyCollection<int>? only, bool patchTuesdayNotice, CancellationToken ct)
    {
        var targets = (await TargetsAsync(ct)).Where(t => t.Host is not null && (only is null || only.Contains(t.DeviceId))).ToList();
        var script = ProbeLibrary.Get(NetworkOpsStore.UpdatesProbe);
        var runs = new ConcurrentBag<(UpdateTarget Target, OnTargetRun Run)>();
        await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Value.UpdateScanParallel), CancellationToken = ct },
            async (t, c) => runs.Add((t, await runner.RunAsync(t.Host!, script, ScanTimeout, wantsIdle: false, c))));

        var now = DateTime.UtcNow;
        var notify = new List<(int Id, DeviceChanges Changes)>();
        var due = new List<(UpdateTarget Target, UpdateScan Scan, Finding? Finding)>();
        int ok = 0, failed = 0;
        foreach (var (t, run) in runs.OrderBy(x => x.Target.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (run.Status != OnTargetStatus.Ok)
            {
                failed++;
                await store.RecordObservationAsync(t.DeviceId, NetworkOpsStore.UpdatesProbe, null, now, run.Status.ToString(), null, run.Error, ct);
                continue;   // not seeing updates is not the updates being installed: its finding stands
            }
            UpdateScan scan;
            try { scan = UpdateScan.Parse(run.OutputJson!); }
            catch (System.Text.Json.JsonException ex)
            {
                failed++;
                await store.RecordObservationAsync(t.DeviceId, NetworkOpsStore.UpdatesProbe, null, now, "Unreadable", run.OutputJson, ex.Message, ct);
                continue;
            }
            ok++;
            await store.RecordObservationAsync(t.DeviceId, NetworkOpsStore.UpdatesProbe, scan.ScanVersion, now, "Ok", run.OutputJson, null, ct);

            var raised = UpdateRules.Evaluate(scan, DateTime.Now);
            var open = (await store.OpenFindingsAsync(t.DeviceId, null, ct)).Where(f => UpdateRules.Owns(f.RuleKey)).ToList();
            var changes = FindingDiff.Compute(open, raised);
            if (changes.Count > 0) await store.ApplyChangesAsync(t.DeviceId, changes, now, ct);
            var mailable = changes.Where(FindingDiff.IsNotifiable).ToList();
            if (mailable.Count > 0) notify.Add((t.DeviceId, new DeviceChanges(t.Name, mailable)));
            if (scan.Updates.Count > 0) due.Add((t, scan, raised.FirstOrDefault()));
        }

        var sent = notify.Count > 0 && await digest.SendAsync(notify.Select(n => n.Changes).ToList(), [], DateTime.Now, ct);
        if (sent)
            foreach (var (id, dc) in notify)
                await store.MarkNotifiedAsync(id, dc.Changes.Where(c => c.Kind != ChangeKind.Cleared).Select(c => c.RuleKey), now, ct);

        if (patchTuesdayNotice && due.Count > 0)
            await digest.SendAlertAsync($"[NetworkOps] Patch Tuesday: updates are waiting on {due.Count} machines", PatchTuesdayBody(due), ct);

        var summary = $"searched {ok} of {targets.Count} machines, {due.Count} with updates waiting " +
                      $"({due.Count(d => d.Scan.Updates.Any(u => u.Security))} with security updates), {failed} could not be searched" +
                      (notify.Count > 0 ? $", {notify.Count} notifiable{(sent ? " (mailed)" : "")}" : "") +
                      (patchTuesdayNotice && due.Count > 0 ? ", Patch Tuesday notice sent" : "");
        log.LogInformation("Update scan: {Summary}", summary);
        return summary;
    }

    internal static string PatchTuesdayBody(IReadOnlyList<(UpdateTarget Target, UpdateScan Scan, Finding? Finding)> due)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Microsoft's monthly updates are out. Nothing installs by itself: open the Command Center -> Updates, tick the machines, and install.");
        sb.AppendLine("Security updates are held 3 days (in case one is pulled); after that they show as due.");
        sb.AppendLine();
        foreach (var (t, s, _) in due.OrderBy(d => d.Target.IsServer).ThenBy(d => d.Target.Name, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"{t.Name}: {s.Updates.Count(u => u.Security)} security, {s.Updates.Count(u => !u.Security)} other" +
                          (s.RebootPending ? "; a restart is already pending" : "") + (t.Guard == "Alone" ? "; patch it on its own" : ""));
        return sb.ToString();
    }
}

/// <summary>Twice a working day: what is waiting, everywhere. The morning run after Patch Tuesday also mails the month's arrivals.</summary>
internal sealed class UpdateScanJob(UpdateScanner scanner) : Jobs.INetworkOpsJob
{
    public const string JobName = "UpdateScan";
    public string Name => JobName;

    public Task<string> RunAsync(CancellationToken ct)
    {
        var local = DateTime.Now;
        return scanner.ScanAsync(null, patchTuesdayNotice: UpdateRules.IsDayAfterPatchTuesday(local) && local.Hour < 12, ct);
    }
}
