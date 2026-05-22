#nullable enable
using System;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// Scheduled BC Bid historical-archive enrichment. Runs on cron; each tick
/// drains up to <c>BcBidHistoricalEnrichmentBatchSize</c> pending rows by
/// delegating to <see cref="BcBidHistoricalEnrichmentService"/>.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class BcBidHistoricalEnrichmentJob : IJob
{
    private readonly BcBidHistoricalEnrichmentService _service;
    private readonly IOptions<Options.OpportunitiesWorkerOptions> _options;
    private readonly ILogger<BcBidHistoricalEnrichmentJob> _logger;

    public BcBidHistoricalEnrichmentJob(
        BcBidHistoricalEnrichmentService service,
        IOptions<Options.OpportunitiesWorkerOptions> options,
        ILogger<BcBidHistoricalEnrichmentJob> logger)
    {
        _service = service;
        _options = options;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var batchSize = _options.Value.BcBidHistoricalEnrichmentBatchSize;
        if (batchSize <= 0) batchSize = 25;

        try
        {
            var result = await _service.EnrichBatchAsync(batchSize, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Scheduled BcBidHistorical enrichment: attempted={A} enriched={E} failed={F}.",
                result.Attempted,
                result.Enriched,
                result.Failed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled BcBidHistorical enrichment failed.");
            // Don't rethrow  keep the trigger alive.
        }
    }
}
