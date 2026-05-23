#nullable enable
using System;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Awards;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

[DisallowConcurrentExecution]
internal sealed class EnrichmentDispatchJob : IJob
{
    private readonly EnrichmentDispatcher _dispatcher;
    private readonly IOptions<Options.OpportunitiesWorkerOptions> _options;
    private readonly ILogger<EnrichmentDispatchJob> _logger;

    public EnrichmentDispatchJob(
        EnrichmentDispatcher dispatcher,
        IOptions<Options.OpportunitiesWorkerOptions> options,
        ILogger<EnrichmentDispatchJob> logger)
    {
        _dispatcher = dispatcher;
        _options = options;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var opt = _options.Value;
        if (!opt.EnrichmentDispatchEnabled) return;

        var batch = opt.EnrichmentDispatchBatchSize > 0 ? opt.EnrichmentDispatchBatchSize : 5;
        try
        {
            var r = await _dispatcher.DispatchBatchAsync(batch, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "EnrichmentDispatch: attempted={A} ok={O} failed={F} blocked={B}.",
                r.Attempted,
                r.Ok,
                r.Failed,
                r.Blocked);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EnrichmentDispatch job failed.");
        }
    }
}
