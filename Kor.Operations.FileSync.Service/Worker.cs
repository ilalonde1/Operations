#nullable enable
using System.Reflection;
using Kor.Operations.FileSync.Service.ControlPlane;
using Kor.Operations.FileSync.Service.Jobs;
using Kor.Operations.FileSync.Service.Jobs.Watcher;
using Kor.Operations.FileSync.Service.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Operations.FileSync.Service;

internal sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IOptionsMonitor<FileSyncOptions> _options;
    private readonly IControlPlaneStore _store;
    private readonly JobRunnerRegistry _runners;
    private readonly IWatcherState _watcherState;

    public Worker(
        ILogger<Worker> logger,
        IOptionsMonitor<FileSyncOptions> options,
        IControlPlaneStore store,
        JobRunnerRegistry runners,
        IWatcherState watcherState)
    {
        _logger = logger;
        _options = options;
        _store = store;
        _runners = runners;
        _watcherState = watcherState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.CurrentValue;
        var hostName = Environment.MachineName;
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        var startedAt = DateTimeOffset.Now;

        _logger.LogInformation(
            "Kor.Operations.FileSync.Service started. Host={Host} Mode={Mode} Version={Version}.",
            hostName,
            opts.Mode,
            version);

        try
        {
            var ok = await _store.PingAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("Control-plane ping: {Result}.", ok ? "ok" : "unexpected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Control-plane ping failed.");
        }

        await TryWriteHeartbeatAsync(hostName, startedAt, version, stoppingToken).ConfigureAwait(false);

        var period = TimeSpan.FromSeconds(Math.Max(5, opts.HeartbeatSeconds));
        using var timer = new PeriodicTimer(period);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TryWriteHeartbeatAsync(hostName, startedAt, version, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }

        _logger.LogInformation("Kor.Operations.FileSync.Service stopping.");
    }

    private async Task TryWriteHeartbeatAsync(string hostName, DateTimeOffset startedAt, string version, CancellationToken ct)
    {
        try
        {
            var current = _options.CurrentValue;
            // Pull live values from shared state so this 60s heartbeat doesn't
            // overwrite the WatcherHostedService's gen with null between its
            // own (slower) heartbeat ticks.
            var gen = _watcherState.IsAttached ? _watcherState.Generation : (int?)null;
            await _store.WriteHeartbeatAsync(
                hostName: hostName,
                startedAt: startedAt,
                mode: current.Mode.ToString(),
                version: version,
                jobsRegistered: _runners.RegisteredCount,
                watcherGen: gen,
                ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heartbeat write failed.");
        }
    }
}
