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
internal sealed class RackSweepJob(NetworkOpsStore store, RackCollector collector, IDigestSender digest, IOptions<NetworkOpsOptions> options, ILogger<RackSweepJob> log) : INetworkOpsJob
{
    public const string JobName = "RackSweep";
    public string Name => JobName;

    public Task<string> RunAsync(CancellationToken ct) => SweepAsync(null, ct);

    public async Task<string> SweepAsync(string? only, CancellationToken ct)
    {
        var o = options.Value;
        var devices = o.Rack.Where(d => only is null || d.Name.Equals(only, StringComparison.OrdinalIgnoreCase)).ToList();
        if (devices.Count == 0) return only is null ? "no rack devices configured" : $"no rack device named {only}";
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

        var sent = notify.Count > 0 && await digest.SendAsync(notify.Select(n => n.Changes).ToList(), [], DateTime.Now, ct);
        if (sent)
            foreach (var (id, dc) in notify)
                await store.MarkNotifiedAsync(id, dc.Changes.Where(c => c.Kind != ChangeKind.Cleared).Select(c => c.RuleKey), now, ct);

        var summary = $"read {ok} of {devices.Count} rack devices, {raised} findings, {notify.Sum(n => n.Changes.Changes.Count)} notifiable" +
                      (ok < devices.Count ? $"; not answering: {string.Join(", ", results.Values.Where(x => !x.Result.Reachable).Select(x => $"{x.Device.Name} ({x.Result.Error})"))}" : "");
        log.LogInformation("Rack sweep: {Summary}", summary);
        return summary;
    }
}
