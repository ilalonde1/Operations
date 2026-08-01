#nullable enable
using System;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Ingestion;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

[DisallowConcurrentExecution]
internal sealed class CaMajorProjectsInventoryJob : IJob
{
    public const string SourceName = "CA_MajorProjectsInventory";

    private static readonly string[] EndpointSourceNames =
    [
        "CA_SocrataSF",
        "CA_SocrataSanDiego",
        "CA_SanJoseCkan",
        "CA_CEQAnet",
    ];

    private readonly IIngestionDispatcher _dispatcher;
    private readonly ILogger<CaMajorProjectsInventoryJob> _logger;

    public CaMajorProjectsInventoryJob(
        IIngestionDispatcher dispatcher,
        ILogger<CaMajorProjectsInventoryJob> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var correlationId = $"sched:{context.FireInstanceId}";

        foreach (var endpointSourceName in EndpointSourceNames)
        {
            try
            {
                var dispatch = await _dispatcher.RunByNameAsync(endpointSourceName, correlationId, ct).ConfigureAwait(false);
                var r = dispatch.Result;
                _logger.LogInformation(
                    "Scheduled {Source} endpoint {EndpointSource} ingestion finished: success={Success} inserted={Inserted} duplicate={Duplicate} skipped={Skipped} failed={Failed}.",
                    SourceName, endpointSourceName, r.Success, r.Inserted, r.Duplicate, r.Skipped, r.Failed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled {Source} endpoint {EndpointSource} ingestion failed.", SourceName, endpointSourceName);
            }
        }
    }
}
