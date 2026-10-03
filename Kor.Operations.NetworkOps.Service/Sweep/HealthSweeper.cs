#nullable enable
using System.Collections.Concurrent;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Core.Probes;
using Kor.Operations.NetworkOps.Service.Alerting;
using Kor.Operations.NetworkOps.Service.Jobs;
using Kor.Operations.NetworkOps.Service.Store;
using Kor.Operations.NetworkOps.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service.Sweep;

// One health sweep, for the whole fleet or named PCs (the Command Center's "check now"). Per PC,
// in this order, because each step feeds the next:
//   1. probe it (on the machine, through its agent or the network route; one result back)
//   2. facts: what it is now vs what was on record -> history + change tracking
//   3. metrics: this sweep's numbers -> the trend lines
//   4. findings: the rules on the snapshot + the predictions on the trend lines
//   5. diff against what is open; for everything that CLEARED, record what changed meanwhile
//      (facts, restart, actions) -- the evidence fix learning ranks
// Then, across the fleet: what the affected PCs share (insights), and one digest of the news.
//
// A PC that could not be probed keeps its findings exactly as they were: not seeing a fault is
// not the fault being fixed.
internal sealed class HealthSweeper(NetworkOpsStore store, IDigestSender digest, Agents.MachineRunner runner, Agents.AgentHub agents,
    Mesh.MeshState mesh, Updates.UpdateRescans rescans, Bios.LenovoBiosCatalog bios, IOptions<NetworkOpsOptions> options, ILogger<HealthSweeper> log)
{
    public const int HistoryDays = 90;

    public async Task<string> SweepAsync(IReadOnlyCollection<string>? only, CancellationToken ct)
    {
        var o = options.Value;
        var all = await store.DirectoryDevicesAsync(ct);
        var devices = only is null
            ? all
            : all.Where(kv => only.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        if (only is not null && devices.Count == 0) return $"no directory device named {string.Join(", ", only)}";

        var timeout = TimeSpan.FromSeconds(o.ProbeTimeoutSeconds);
        var script = ProbeLibrary.Get(ProbeLibrary.Health);
        var runs = new ConcurrentBag<OnTargetRun>();
        await Parallel.ForEachAsync(devices.Keys, new ParallelOptions { MaxDegreeOfParallelism = o.ParallelProbes, CancellationToken = ct },
            async (h, c) => runs.Add(await runner.RunAsync(h, script, timeout, wantsIdle: true, c)));

        var now = DateTime.UtcNow;
        // Which PCs have an agent, and the version APP01 ships: the agent's own health is judged with each check.
        var installed = await store.AgentRecordsAsync(ct);
        var shipped = Agents.AgentInstaller.PackageVersionOrNull();
        var notify = new List<(int DeviceId, DeviceChanges Changes)>();
        int ok = 0, unreadable = 0, findings = 0, factChanges = 0, resolutions = 0;
        var booted = new List<(int DeviceId, string Name, DateTime? BootUtc)>();
        foreach (var r in runs.OrderBy(r => r.Computer, StringComparer.OrdinalIgnoreCase))
        {
            var id = devices[r.Computer];
            if (r.Status != OnTargetStatus.Ok)
            {
                await store.RecordObservationAsync(id, ProbeLibrary.Health, null, now, r.Status.ToString(), null, r.Error, ct);
                continue;
            }
            HealthSnapshot snap;
            try { snap = HealthSnapshot.Parse(r.OutputJson!); }
            catch (System.Text.Json.JsonException ex)
            {
                unreadable++;
                await store.RecordObservationAsync(id, ProbeLibrary.Health, null, now, "Unreadable", r.OutputJson, ex.Message, ct);
                continue;
            }
            ok++;
            await store.RecordObservationAsync(id, ProbeLibrary.Health, snap.ProbeVersion, now, "Ok", r.OutputJson, null, ct);
            booted.Add((id, r.Computer, Core.Updates.UpdateRules.BootFromLocal(snap.Os?.LastBoot, now, TimeZoneInfo.Local)));

            // 2. facts
            var observed = Facts.Extract(snap);
            var factDiff = FactDiff.Compute(await store.CurrentFactsAsync(id, ct), observed);
            factChanges += factDiff.Count;
            await store.ApplyFactsAsync(id, factDiff, observed is not null, now, ct);

            // 3. metrics, then the history they extend
            await store.InsertMetricsAsync(id, Metrics.Extract(snap), now, ct);
            var history = await store.MetricHistoryAsync(id, now.AddDays(-HistoryDays), ct);

            // 4. findings: rules on the snapshot, predictions on the trends, and the BIOS against Lenovo's current package
            //    (null = Lenovo's catalog could not be read: the BIOS is then not judged at all, below).
            var machineType = Core.Bios.LenovoCatalog.MachineTypeOf(snap.Inventory?.Manufacturer, snap.Inventory?.MachineType);
            var biosPackages = machineType is null ? [] : await bios.PackagesAsync(machineType, ct);
            var raised = HealthRules.Evaluate(snap).Concat(Predictions.Evaluate(snap with { CollectedAt = now }, history))
                .Concat(AgentRules.Evaluate(AgentStateOf(r.Computer, installed), answeredOverNetwork: r.Stages?.StartsWith("agent:", StringComparison.Ordinal) != true, shipped, now))
                .Concat(MeshRules.Evaluate(o.MeshEnabled ? mesh.PresenceOf(id) : null, answered: true))
                .Concat(biosPackages is null ? [] : Core.Bios.BiosRules.Evaluate(snap.Inventory, (snap.Os?.Build ?? 0) >= 22000, biosPackages))
                .GroupBy(f => f.RuleKey).Select(g => g.OrderByDescending(f => f.Severity).First()).ToList();
            findings += raised.Count;

            // 5. diff; learn from what cleared
            // The update scan owns "updates-due" (Updates/UpdateScanner): this sweep never raises it, so must never clear it.
            // An unread Lenovo catalog is not a current BIOS: the BIOS finding then stands exactly as it was.
            var open = (await store.OpenFindingsAsync(id, FleetCensusJob.SilentRule, ct))
                .Where(f => !Core.Updates.UpdateRules.Owns(f.RuleKey) && !Core.Network.NetworkFindings.Owns(f.RuleKey)
                            && (biosPackages is not null || f.RuleKey != Core.Bios.BiosRules.Rule)).ToList();
            var changes = FindingDiff.Compute(open, raised);
            await store.ApplyChangesAsync(id, changes, now, ct);
            foreach (var cleared in changes.Where(c => c.Kind == ChangeKind.Cleared && c.Previous is not null))
            {
                var from = cleared.Previous!.LastSeenUtc ?? cleared.Previous.FirstSeenUtc;
                var rebooted = snap.Os is { } os && os.UptimeHours < (now - from).TotalHours;
                var res = new Resolution(cleared.RuleKey, now, rebooted,
                    await store.FactChangesBetweenAsync(id, from, now, ct), await store.ActionsBetweenAsync(id, from, now, ct));
                await store.InsertResolutionAsync(cleared.Previous.FindingId, id, res, ct);
                resolutions++;
            }
            var mailable = changes.Where(FindingDiff.IsNotifiable).ToList();
            if (mailable.Count > 0) notify.Add((id, new DeviceChanges(r.Computer, mailable)));
        }

        // A PC restarted since its last Windows Update search is searched again now, not at 08:00 (Updates/UpdateRescans).
        var researched = await rescans.ConsiderAsync(booted, ct);

        // Across the fleet: what the affected PCs share. Re-run on every sweep, including a
        // single-PC check, so a pattern appears the moment the evidence for it does.
        var insights = FleetCorrelation.Find(await store.FleetMembersAsync(ct));
        var insightChanges = InsightDiff.Compute(await store.ActiveInsightKeysAsync(ct), insights);
        await store.ApplyInsightsAsync(insightChanges, now, ct);
        var newPatterns = insightChanges.Where(x => x.Kind == InsightChange.New).Select(x => x.Current!).ToList();

        var sent = (notify.Count > 0 || newPatterns.Count > 0)
                   && await digest.SendAsync(notify.Select(n => n.Changes).ToList(), newPatterns, DateTime.Now, ct);
        if (sent)
            foreach (var (id, dc) in notify)
                await store.MarkNotifiedAsync(id, dc.Changes.Where(c => c.Kind != ChangeKind.Cleared).Select(c => c.RuleKey), now, ct);

        var summary = $"probed {ok} of {devices.Count}, {findings} findings, {notify.Sum(n => n.Changes.Changes.Count)} notifiable on {notify.Count} PCs, " +
                      $"{factChanges} fact changes, {resolutions} resolutions, {insights.Count} fleet patterns ({newPatterns.Count} new)" +
                      (unreadable > 0 ? $", {unreadable} unreadable" : "") + (sent ? ", digest mailed" : "") +
                      (researched > 0 ? $", {researched} restarted since their last update search (searching again)" : "");
        log.LogInformation("Health sweep: {Summary}", summary);
        return summary;
    }

    /// <summary>The agent as the rules see it: installed (SQL), heard right now and which version (the hub, which is live).</summary>
    private AgentState? AgentStateOf(string device, IReadOnlyDictionary<string, NetworkOpsStore.AgentRecord> installed)
    {
        if (!installed.TryGetValue(device, out var rec)) return null;
        var live = agents.Status(device);
        var lastPoll = live is { LastPollUtc: var p } && p != default ? p : (DateTime?)null;
        return new AgentState(rec.Version, live?.Version is { Length: > 0 } v ? v : rec.LastVersion, live?.Connected == true,
            new[] { lastPoll, rec.LastContactUtc }.Max(), rec.InstalledUtc);
    }
}
