#nullable enable
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Heartbeat;
using Kor.Opportunities.Worker.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// BackgroundService that pings the database at start-up then writes a row to
/// <c>opportunities.ServiceHeartbeat</c> every <see cref="OpportunitiesWorkerOptions.HeartbeatSeconds"/>.
/// Heartbeat-write failures are logged and swallowed: a transient SQL hiccup must not
/// crash the host, the next tick will retry naturally.
/// </summary>
internal sealed class HeartbeatBackgroundService : BackgroundService
{
    // Floor on the configured cadence. Anything below 5s would flood the log on a SQL
    // outage without buying us additional liveness fidelity.
    private const int MinPeriodSeconds = 5;

    private const string ServiceName = "Kor.Opportunities.Worker";

    private readonly ILogger<HeartbeatBackgroundService> _logger;
    private readonly IHeartbeatStore _store;
    private readonly OpportunitiesWorkerOptions _options;

    public HeartbeatBackgroundService(
        ILogger<HeartbeatBackgroundService> logger,
        IHeartbeatStore store,
        IOptions<OpportunitiesWorkerOptions> options)
    {
        _logger = logger;
        _store = store;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hostName = Environment.MachineName;
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

        _logger.LogInformation(
            "Kor.Opportunities.Worker started. Host={Host} Version={Version} HeartbeatSeconds={Seconds}.",
            hostName,
            version,
            _options.HeartbeatSeconds);

        try
        {
            var ok = await _store.PingAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("Opportunities DB ping: {Result}.", ok ? "ok" : "unexpected");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Opportunities DB ping failed.");
        }

        await TryWriteHeartbeatAsync(hostName, version, stoppingToken).ConfigureAwait(false);

        var period = TimeSpan.FromSeconds(Math.Max(MinPeriodSeconds, _options.HeartbeatSeconds));
        using var timer = new PeriodicTimer(period);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TryWriteHeartbeatAsync(hostName, version, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }

        _logger.LogInformation("Kor.Opportunities.Worker stopping.");
    }

    private async Task TryWriteHeartbeatAsync(string hostName, string version, CancellationToken ct)
    {
        try
        {
            await _store.WriteHeartbeatAsync(
                serviceName: ServiceName,
                machineName: hostName,
                version: version,
                ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heartbeat write failed.");
        }
    }
}
