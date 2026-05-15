#nullable enable
using System;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Ingestion;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// Scheduled pull of the CanadaBuys "newTenderNotice" delta feed — the same
/// platform as <see cref="CanadaBuysIngestionJob"/>, but a different URL that
/// refreshes every 2 hours instead of daily. Acts as a low-latency companion
/// to the daily "open" snapshot, which remains as a durability backstop.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class CanadaBuysNewIngestionJob : IJob
{
    public const string SourceName = "CanadaBuysNew";

    private readonly IIngestionDispatcher _dispatcher;
    private readonly ILogger<CanadaBuysNewIngestionJob> _logger;

    public CanadaBuysNewIngestionJob(IIngestionDispatcher dispatcher, ILogger<CanadaBuysNewIngestionJob> logger)
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
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled {Source} ingestion failed.", SourceName);
        }
    }
}
