#nullable enable
using System;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Awards;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

[DisallowConcurrentExecution]
internal sealed class AwardAgentEnrichmentJob : IJob
{
    private readonly AwardAgentEnrichmentService _service;
    private readonly IOpportunityAwardStore _awardStore;
    private readonly IOptions<Options.OpportunitiesWorkerOptions> _options;
    private readonly ILogger<AwardAgentEnrichmentJob> _logger;

    public AwardAgentEnrichmentJob(
        AwardAgentEnrichmentService service,
        IOpportunityAwardStore awardStore,
        IOptions<Options.OpportunitiesWorkerOptions> options,
        ILogger<AwardAgentEnrichmentJob> logger)
    {
        _service = service;
        _awardStore = awardStore;
        _options = options;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var opt = _options.Value;
        if (!opt.AwardAgentEnrichmentEnabled)
        {
            // Off by default — flip KOR_OPPORTUNITIES_AWARDAGENTENRICHMENTENABLED=true to enable.
            return;
        }

        // Hard ceiling on total enriched rows. Once hit, job no-ops until cap is raised.
        if (opt.AwardAgentEnrichmentTotalCap > 0)
        {
            var enrichedSoFar = await _awardStore.CountAgentEnrichedAsync(ct).ConfigureAwait(false);
            if (enrichedSoFar >= opt.AwardAgentEnrichmentTotalCap)
            {
                _logger.LogInformation(
                    "Award agent enrichment paused: cap reached ({Enriched} >= {Cap}). Raise AwardAgentEnrichmentTotalCap to continue.",
                    enrichedSoFar, opt.AwardAgentEnrichmentTotalCap);
                return;
            }
        }

        var batch = opt.AwardAgentEnrichmentBatchSize > 0 ? opt.AwardAgentEnrichmentBatchSize : 10;
        var maxAttempts = opt.AwardAgentEnrichmentMaxAttempts > 0 ? opt.AwardAgentEnrichmentMaxAttempts : 2;

        try
        {
            var result = await _service.EnrichBatchAsync(batch, maxAttempts, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Scheduled Award agent enrichment: attempted={A} enriched={E} failed={F}.",
                result.Attempted,
                result.Enriched,
                result.Failed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled Award agent enrichment failed.");
        }
    }
}
