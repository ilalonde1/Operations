#nullable enable
using System.Collections.Concurrent;
using System.Threading.Channels;
using Kor.Operations.NetworkOps.Core.Updates;
using Kor.Operations.NetworkOps.Service.Store;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.NetworkOps.Service.Updates;

// A machine that has restarted since Windows Update was last searched on it is searched again, on its own, within
// minutes -- not at the next 08:00/13:00 search (Ian, 2026-10-02: DC01, FS01 and RDS01 patched by hand and restarted,
// still "security updates are due" 20 minutes later). Updates finish at a restart, so a restart is when the answer changes.
//
// Who notices the restart: the rack sweep (servers, every 5 minutes, day and night, from the uptime server.ps1 reads)
// and the health sweep (PCs, from the probe's LastBoot) -- which a PC's agent reconnecting after a restart triggers within
// seconds (Agents/AgentApi). Each calls Consider() with the boot time it read; the rule is UpdateRules.RestartedSinceSearch.
//
// Its own queue, off the sweeps: a search takes up to 6 minutes, and neither the 5-minute rack sweep nor a "Check now"
// may wait for one. One search per machine at a time; a machine already queued is not queued twice.
internal sealed class UpdateRescans(NetworkOpsStore store, UpdateScanner scanner, ILogger<UpdateRescans> log) : BackgroundService
{
    private readonly Channel<(int DeviceId, string Name, string Why)> _queue = Channel.CreateUnbounded<(int, string, string)>();
    private readonly ConcurrentDictionary<int, byte> _queued = new();

    /// <summary>Queues a search of each machine read in a sweep that has restarted since its last one. Returns how many.</summary>
    public async Task<int> ConsiderAsync(IReadOnlyList<(int DeviceId, string Name, DateTime? BootUtc)> read, CancellationToken ct)
    {
        if (read.Count == 0) return 0;
        var scans = await store.LatestUpdateScansAsync(ct).ConfigureAwait(false);
        var n = 0;
        foreach (var (id, name, boot) in read)
        {
            var last = scans.GetValueOrDefault(id);
            if (!_queued.ContainsKey(id) && UpdateRules.RestartedSinceSearch(boot, last?.CollectedUtc)
                && Enqueue(id, name, $"restarted {boot:yyyy-MM-dd HH:mm} UTC, last searched {(last is null ? "never" : $"{last.CollectedUtc:yyyy-MM-dd HH:mm} UTC")}"))
                n++;
        }
        return n;
    }

    private bool Enqueue(int deviceId, string name, string why)
    {
        if (!_queued.TryAdd(deviceId, 0)) return false;
        _queue.Writer.TryWrite((deviceId, name, why));
        log.LogInformation("Update search queued for {Device}: {Why}", name, why);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Two at a time: a burst (every PC back after a power cut) drains in minutes without loading APP01.
        await Parallel.ForEachAsync(_queue.Reader.ReadAllAsync(ct), new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = ct }, async (item, c) =>
        {
            try
            {
                var summary = await scanner.ScanAsync([item.DeviceId], patchTuesdayNotice: false, c).ConfigureAwait(false);
                log.LogInformation("Update search after restart, {Device}: {Summary}", item.Name, summary);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Update search after restart failed on {Device}", item.Name); }
            finally { _queued.TryRemove(item.DeviceId, out _); }
        }).ConfigureAwait(false);
    }
}
