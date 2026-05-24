#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Ingestion;
using Kor.Opportunities.Data.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

[DisallowConcurrentExecution]
internal sealed class GraphEmailIngestionJob : IJob
{
    private readonly IOpportunitySourceStore _sourceStore;
    private readonly IIngestionDispatcher _dispatcher;
    private readonly IOptions<Options.OpportunitiesWorkerOptions> _options;
    private readonly ILogger<GraphEmailIngestionJob> _logger;

    public GraphEmailIngestionJob(
        IOpportunitySourceStore sourceStore,
        IIngestionDispatcher dispatcher,
        IOptions<Options.OpportunitiesWorkerOptions> options,
        ILogger<GraphEmailIngestionJob> logger)
    {
        _sourceStore = sourceStore;
        _dispatcher = dispatcher;
        _options = options;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var correlationId = $"sched:{context.FireInstanceId}";
        var opt = _options.Value;
        if (string.IsNullOrWhiteSpace(opt.GraphEmailTenantId)
            || string.IsNullOrWhiteSpace(opt.GraphEmailClientId)
            || string.IsNullOrWhiteSpace(opt.GraphEmailClientSecret)
            || string.IsNullOrWhiteSpace(opt.GraphEmailUserEmail))
        {
            _logger.LogDebug("{Job} skipped: Graph email credentials are not configured.", nameof(GraphEmailIngestionJob));
            return;
        }

        try
        {
            var sources = await _sourceStore.ListEnabledAsync(ct).ConfigureAwait(false);
            var graphEmailSources = sources
                .Where(s => s.SourceType == OpportunitySourceType.GraphEmail)
                .ToList();

            if (graphEmailSources.Count == 0)
            {
                _logger.LogDebug("No enabled GraphEmail sources to ingest.");
                return;
            }

            foreach (var source in graphEmailSources)
            {
                try
                {
                    var dispatch = await _dispatcher.RunByNameAsync(source.Name, correlationId, ct).ConfigureAwait(false);
                    var r = dispatch.Result;
                    _logger.LogInformation(
                        "Scheduled GraphEmail ingestion {Source} finished: success={Success} inserted={Inserted} duplicate={Duplicate} skipped={Skipped} failed={Failed}.",
                        source.Name, r.Success, r.Inserted, r.Duplicate, r.Skipped, r.Failed);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Scheduled GraphEmail ingestion for source {Source} failed.", source.Name);
                    // Continue to the next source; don't let one bad source kill the others.
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GraphEmail ingestion job failed.");
        }
    }
}
