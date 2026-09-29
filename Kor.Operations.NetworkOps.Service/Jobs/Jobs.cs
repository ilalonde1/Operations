#nullable enable
using System.Collections.Concurrent;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Probes;
using Kor.Operations.NetworkOps.Service.Alerting;
using Kor.Operations.NetworkOps.Service.Store;
using Kor.Operations.NetworkOps.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service.Jobs;

/// <summary>A scheduled unit of work. Every implementation must appear in <see cref="SchedulingCatalog"/> (a test enforces it).</summary>
internal interface INetworkOpsJob
{
    string Name { get; }
    /// <returns>A one-line summary recorded on the run.</returns>
    Task<string> RunAsync(CancellationToken ct);
}

// Hourly in business hours: who is in the directory, and which channel answers on each machine.
// Also owns "device-silent": a machine that has not answered for SilentAfterDays is itself a
// finding -- the class the 8-day backup failure and the 173-day dead PSU belonged to.
internal sealed class FleetCensusJob(NetworkOpsStore store, IDigestSender digest, IOptions<NetworkOpsOptions> options, ILogger<FleetCensusJob> log) : INetworkOpsJob
{
    public const string JobName = "FleetCensus";
    public const string SilentRule = "device-silent";
    public string Name => JobName;

    public async Task<string> RunAsync(CancellationToken ct)
    {
        var names = ActiveDirectoryFleet.Workstations();
        await store.SyncDirectoryAsync(names, ct);

        var rows = new ConcurrentBag<CensusRow>();
        await Parallel.ForEachAsync(names, new ParallelOptions { MaxDegreeOfParallelism = options.Value.ParallelProbes, CancellationToken = ct },
            async (h, c) => rows.Add(await ChannelCensus.ProbeAsync(h, c)));
        var now = DateTime.UtcNow;
        foreach (var r in rows) await store.RecordCensusAsync(r, now, ct);

        // Silence as a finding, owned here: raise for machines quiet too long, clear for the rest.
        var silentDays = options.Value.SilentAfterDays;
        var silent = (await store.SilentDevicesAsync(silentDays, now, ct)).ToDictionary(s => s.DeviceId);
        var notify = new List<(int DeviceId, DeviceChanges Changes)>();
        foreach (var (name, id) in await store.DirectoryDevicesAsync(ct))
        {
            var open = (await store.OpenFindingsAsync(id, null, ct)).Where(o => o.RuleKey == SilentRule).ToList();
            var raised = silent.TryGetValue(id, out var s)
                ? new List<Finding> { new(SilentRule, Severity.Warning, "Machine has gone quiet",
                    s.LastReachableUtc is { } last ? $"not reachable since {last.ToLocalTime():yyyy-MM-dd HH:mm} ({(now - last).TotalDays:0} days)" : $"never reachable in {silentDays}+ days") }
                : new List<Finding>();
            var changes = FindingDiff.Compute(open, raised);
            if (changes.Count == 0) continue;
            await store.ApplyChangesAsync(id, changes, now, ct);
            var mailable = changes.Where(FindingDiff.IsNotifiable).ToList();
            if (mailable.Count > 0) notify.Add((id, new DeviceChanges(name, mailable)));
        }
        if (notify.Count > 0 && await digest.SendAsync(notify.Select(n => n.Changes).ToList(), DateTime.Now, ct))
            foreach (var (id, dc) in notify)
                await store.MarkNotifiedAsync(id, dc.Changes.Where(c => c.Kind != ChangeKind.Cleared).Select(c => c.RuleKey), now, ct);

        var reachable = rows.Count(r => r.Reachable);
        log.LogInformation("Census: {Reachable} of {Total} reachable, {Silent} silent {Days}+ days", reachable, rows.Count, silent.Count, silentDays);
        return $"reachable {reachable} of {rows.Count}; run-on-target ready {rows.Count(r => r.AdminWrite && r.ServiceControl)}; silent {silent.Count}";
    }
}

// Twice a working day: the health probe ON every directory machine, the rules, the findings diff,
// and one digest of what is new, worse or cleared.
internal sealed class HealthSweepJob(NetworkOpsStore store, IDigestSender digest, IOptions<NetworkOpsOptions> options, ILogger<HealthSweepJob> log) : INetworkOpsJob
{
    public const string JobName = "HealthSweep";
    public string Name => JobName;

    public async Task<string> RunAsync(CancellationToken ct)
    {
        var o = options.Value;
        var devices = await store.DirectoryDevicesAsync(ct);
        var channel = new OnTargetChannel(TimeSpan.FromSeconds(o.ProbeTimeoutSeconds));
        var script = ProbeLibrary.Get(ProbeLibrary.Health);

        var runs = new ConcurrentBag<OnTargetRun>();
        await Parallel.ForEachAsync(devices.Keys, new ParallelOptions { MaxDegreeOfParallelism = o.ParallelProbes, CancellationToken = ct },
            async (h, c) => runs.Add(await channel.RunAsync(h, script, c)));

        var now = DateTime.UtcNow;
        var notify = new List<(int DeviceId, DeviceChanges Changes)>();
        int ok = 0, unreadable = 0, findings = 0;
        foreach (var r in runs.OrderBy(r => r.Computer, StringComparer.OrdinalIgnoreCase))
        {
            var id = devices[r.Computer];
            if (r.Status != OnTargetStatus.Ok)
            {
                // Offline/timeout: record it, but leave the machine's findings exactly as they were.
                // Not seeing a fault is not the fault being fixed.
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

            var raised = HealthRules.Evaluate(snap);
            findings += raised.Count;
            var open = await store.OpenFindingsAsync(id, FleetCensusJob.SilentRule, ct);
            var changes = FindingDiff.Compute(open, raised);
            await store.ApplyChangesAsync(id, changes, now, ct);
            var mailable = changes.Where(FindingDiff.IsNotifiable).ToList();
            if (mailable.Count > 0) notify.Add((id, new DeviceChanges(r.Computer, mailable)));
        }

        var sent = await digest.SendAsync(notify.Select(n => n.Changes).ToList(), DateTime.Now, ct);
        if (sent)
            foreach (var (id, dc) in notify)
                await store.MarkNotifiedAsync(id, dc.Changes.Where(c => c.Kind != ChangeKind.Cleared).Select(c => c.RuleKey), now, ct);

        var summary = $"probed {ok} of {devices.Count}, {findings} findings, {notify.Sum(n => n.Changes.Changes.Count)} notifiable changes on {notify.Count} machines" +
                      (unreadable > 0 ? $", {unreadable} unreadable" : "") + (sent ? ", digest mailed" : "");
        log.LogInformation("Health sweep: {Summary}", summary);
        return summary;
    }
}

// Nightly: keep the observation history bounded (SQL Express caps a database at 10 GB).
internal sealed class MaintenanceJob(NetworkOpsStore store, IOptions<NetworkOpsOptions> options) : INetworkOpsJob
{
    public const string JobName = "Maintenance";
    public string Name => JobName;

    public async Task<string> RunAsync(CancellationToken ct)
    {
        var n = await store.PurgeObservationsAsync(options.Value.ObservationRetentionDays, DateTime.UtcNow, ct);
        return $"purged {n} observations older than {options.Value.ObservationRetentionDays} days";
    }
}
