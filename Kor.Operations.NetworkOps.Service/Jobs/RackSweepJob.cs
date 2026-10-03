#nullable enable
using System.Collections.Concurrent;
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Core.Rack;
using Kor.Operations.NetworkOps.Service.Alerting;
using Kor.Operations.NetworkOps.Service.Rack;
using Kor.Operations.NetworkOps.Service.Store;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service.Jobs;

// Every 5 minutes, day and night: read every rack device (Rack/RackCollector), then per device the same path a
// PC's health sweep takes -- observation, facts, metrics, findings diff, one digest. A device that cannot be read
// raises `rack.unreachable` and keeps every other finding exactly as it was: not seeing a fault is not the fault
// being fixed. Also runnable for one device from the Command Center ("check now").
internal sealed class RackSweepJob(NetworkOpsStore store, RackCollector collector, IDigestSender digest, Updates.UpdateRescans rescans,
    Mesh.MeshState mesh, IOptions<NetworkOpsOptions> options, ILogger<RackSweepJob> log, Network.NetworkMapService? map = null) : INetworkOpsJob
{
    public const string JobName = "RackSweep";
    public string Name => JobName;

    public Task<string> RunAsync(CancellationToken ct) => SweepAsync(null, ct);

    public async Task<string> SweepAsync(string? only, CancellationToken ct)
    {
        var o = options.Value;
        var named = o.Rack.Where(d => only is null || d.Name.Equals(only, StringComparison.OrdinalIgnoreCase)).ToList();
        if (named.Count == 0) return only is null ? "no rack devices configured" : $"no rack device named {only}";
        // Just after a service start MeshCentral has not been read yet: a device judged from it is left exactly as it was,
        // not called "not answering" (it was, every restart, until the first Mesh sweep -- a false alarm on KOR-MESH01).
        List<RackDevice> waiting = mesh.Attempted ? [] : named.Where(d => d.JudgedFromMesh).ToList();
        var devices = named.Except(waiting).ToList();
        if (devices.Count == 0) return $"{string.Join(", ", waiting.Select(d => d.Name))}: waiting for the first MeshCentral read since the service started";
        var now = DateTime.UtcNow;
        if (only is null) await store.RetireRackDevicesExceptAsync(o.Rack.Select(d => d.Name).ToList(), now, ct);

        // Read everything first, in parallel (one slow device must not hold up the rest).
        var results = new ConcurrentDictionary<string, (RackDevice Device, int Id, RackResult Result)>(StringComparer.OrdinalIgnoreCase);
        await Parallel.ForEachAsync(devices, new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = ct }, async (d, c) =>
        {
            var id = await store.UpsertRackDeviceAsync(d.Name, d.Kind, reachable: false, now, c);
            var previous = await store.CurrentFactsAsync(id, c);
            var r = await collector.CollectAsync(d, previous, c);
            results[d.Name] = (d, id, r);
        });

        var notify = new List<(int Id, DeviceChanges Changes)>();
        int ok = 0, raised = 0;
        foreach (var (d, id, r) in results.Values.OrderBy(x => x.Device.Name))
        {
            await store.UpsertRackDeviceAsync(d.Name, d.Kind, r.Reachable, now, ct);
            var payload = JsonSerializer.Serialize(new { summary = r.Summary, error = r.Error, findings = r.Findings.Count });
            await store.RecordObservationAsync(id, "rack", 1, now, r.Reachable ? "Ok" : "Offline", payload, r.Error, ct);

            // The update scan owns "updates-due" (Updates/UpdateScanner): this sweep never raises it, so must never clear it.
            var open = (await store.OpenFindingsAsync(id, null, ct)).Where(f => !Core.Updates.UpdateRules.Owns(f.RuleKey)).ToList();
            IReadOnlyList<FindingChange> changes;
            if (r.Reachable)
            {
                ok++;
                var observed = r.Facts.Count > 0 ? r.Facts : null;
                await store.ApplyFactsAsync(id, FactDiff.Compute(await store.CurrentFactsAsync(id, ct), observed), observed is not null, now, ct);
                await store.InsertMetricsAsync(id, r.Metrics, now, ct);
                raised += r.Findings.Count;
                changes = FindingDiff.Compute(open, r.Findings);   // also clears rack.unreachable
            }
            else
            {
                // Only the unreachable finding moves; everything else stands.
                var severity = d.Kind is RackKinds.Host or RackKinds.Storage or RackKinds.Internet or RackKinds.Ups ? Severity.Critical : Severity.Warning;
                changes = FindingDiff.Compute(open.Where(f => f.RuleKey == RackResult.UnreachableRule).ToList(),
                    [new Finding(RackResult.UnreachableRule, severity, $"{d.Name} is not answering", r.Error ?? "no answer")]);
            }
            if (changes.Count > 0) await store.ApplyChangesAsync(id, changes, now, ct);
            var mailable = changes.Where(FindingDiff.IsNotifiable).ToList();
            if (mailable.Count > 0) notify.Add((id, new DeviceChanges(d.Name, mailable)));
        }

        // A server restarted since its last Windows Update search is searched again now, not at 08:00 (Updates/UpdateRescans).
        var booted = results.Values.Where(x => x.Result.Reachable && x.Device.AppCanRunOn)
            .Select(x => (x.Id, x.Device.Name, Core.Updates.UpdateRules.BootFromUptime(now, x.Result.Metrics.FirstOrDefault(m => m.Metric == Metrics.UptimeHours)?.Value)))
            .ToList();
        var researched = await rescans.ConsiderAsync(booted, ct);

        var sent = notify.Count > 0 && await digest.SendAsync(notify.Select(n => n.Changes).ToList(), [], DateTime.Now, ct);
        if (sent)
            foreach (var (id, dc) in notify)
                await store.MarkNotifiedAsync(id, dc.Changes.Where(c => c.Kind != ChangeKind.Cleared).Select(c => c.RuleKey), now, ct);

        // The port map, from this sweep's UniFi read (a failure to build it is said, and touches nothing above).
        var mapped = "";
        if (map is not null && results.Values.Any(x => x.Device.Collector == "UniFi" && x.Result.Reachable))
        {
            try { mapped = "; " + await map.RefreshAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Port map not built"); mapped = "; port map not built: " + ex.Message; }
        }

        var summary = $"read {ok} of {devices.Count} rack devices, {raised} findings, {notify.Sum(n => n.Changes.Changes.Count)} notifiable" + mapped +
                      (researched > 0 ? $", {researched} restarted since their last update search (searching again)" : "") +
                      (waiting.Count > 0 ? $"; not judged until MeshCentral is first read: {string.Join(", ", waiting.Select(d => d.Name))}" : "") +
                      (ok < devices.Count ? $"; not answering: {string.Join(", ", results.Values.Where(x => !x.Result.Reachable).Select(x => $"{x.Device.Name} ({x.Result.Error})"))}" : "");
        log.LogInformation("Rack sweep: {Summary}", summary);
        return summary;
    }
}
