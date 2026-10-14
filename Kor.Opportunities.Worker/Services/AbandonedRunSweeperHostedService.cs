#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Ingestion;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// Runs once at startup. Any <c>opportunities.IngestionRuns</c> row from THIS host that
/// still has no <c>EndedAtUtc</c> was abandoned: the previous Worker process ended (a
/// deploy, a crash, a reboot) before the run could finalise its row. Nothing else ever
/// closes such a row, and Admin renders a null-ended run as "running", so without this
/// sweep an abandoned run reads as in flight forever. Two sat open from 2026-10-01 and
/// 2026-10-06 when this was written, both left by Worker restarts.
///
/// Only this host's rows are touched. A run opened by another host may genuinely be in
/// flight, and at startup nothing from this host can be. The run is closed as FAILED with
/// a reason that names the restart, so the Admin grid and the morning report say what
/// happened instead of showing an eternal "running".
///
/// <c>tools/BdIntegrityCheck</c> carries <c>ingestion_run_open_over_24h</c> as the gate on
/// the data itself, whichever binary is deployed.
/// </summary>
internal sealed class AbandonedRunSweeperHostedService : IHostedService
{
    private readonly IIngestionRunStore _runs;
    private readonly ILogger<AbandonedRunSweeperHostedService> _logger;

    public AbandonedRunSweeperHostedService(
        IIngestionRunStore runs,
        ILogger<AbandonedRunSweeperHostedService> logger)
    {
        _runs = runs;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var host = Environment.MachineName;
        try
        {
            var closed = await _runs.CloseAbandonedAsync(
                host,
                $"Abandoned: the worker on {host} restarted before this run ended (closed at startup {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC).",
                cancellationToken).ConfigureAwait(false);
            if (closed > 0)
            {
                _logger.LogWarning(
                    "Closed {Count} ingestion run(s) abandoned by a previous worker process on {Host}.",
                    closed, host);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Startup must not hinge on this; the integrity check reports open runs independently.
            _logger.LogError(ex, "Abandoned ingestion-run sweep failed on {Host}.", host);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
