#nullable enable
using System;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Ingestion;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

[DisallowConcurrentExecution]
internal sealed class BcMajorProjectsInventoryJob : IJob
{
    public const string SourceName = "BC_MajorProjectsInventory";

    private readonly IIngestionDispatcher _dispatcher;
    private readonly ILogger<BcMajorProjectsInventoryJob> _logger;

    public BcMajorProjectsInventoryJob(
        IIngestionDispatcher dispatcher,
        ILogger<BcMajorProjectsInventoryJob> logger)
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
