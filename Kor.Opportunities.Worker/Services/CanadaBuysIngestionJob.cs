#nullable enable
using System;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Ingestion;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// Scheduled CanadaBuys CSV pull. Quartz fires this on the configured cron;
/// it just hands off to <see cref="IIngestionDispatcher"/> by source name so
/// the heavy lifting stays in <c>Kor.Opportunities.Data</c> and is shared
/// with the manual "Run Now" path.
/// </summary>
[DisallowConcurrentExecution] // a slow pull mustn't queue up overlapping runs
internal sealed class CanadaBuysIngestionJob : IJob
{
    public const string SourceName = "CanadaBuys";

    private readonly IIngestionDispatcher _dispatcher;
    private readonly ILogger<CanadaBuysIngestionJob> _logger;

    public CanadaBuysIngestionJob(IIngestionDispatcher dispatcher, ILogger<CanadaBuysIngestionJob> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var correlationId = $"sched:{context.FireInstanceId}";

        try
        {
            var dispatch = await _dispatcher.RunByNameAsync(SourceName, correlationId, ct).ConfigureAwait(false);
            var r = dispatch.Result;
            _logger.LogInformation(
                "Scheduled {Source} ingestion finished: success={Success} inserted={Inserted} duplicate={Duplicate} skipped={Skipped} failed={Failed}.",
                SourceName, r.Success, r.Inserted, r.Duplicate, r.Skipped, r.Failed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Service is shutting down — nothing to log beyond Quartz's own teardown.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled {Source} ingestion failed.", SourceName);
            // Don't rethrow — a transient pull failure is recorded on the IngestionRun row;
            // we don't want Quartz to flag the trigger as broken and pause it.
        }
    }
}
