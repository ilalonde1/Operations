#nullable enable
using System;
using System.Threading.Tasks;
using Kor.Opportunities.Data.IndustryEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// Feeds <c>opportunities.IndustryEvents</c> from the association calendars
/// registered in <c>opportunities.IndustryEventSource</c>.
///
/// Counterpart to <see cref="DataRetirementJob"/>, which retires events whose
/// date has passed. Until this job existed the table only ever shrank.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class IndustryEventsIngestJob : IJob
{
    private readonly IndustryEventIngestService _service;
    private readonly IOptions<Options.OpportunitiesWorkerOptions> _options;
    private readonly ILogger<IndustryEventsIngestJob> _logger;

    public IndustryEventsIngestJob(
        IndustryEventIngestService service,
        IOptions<Options.OpportunitiesWorkerOptions> options,
        ILogger<IndustryEventsIngestJob> logger)
    {
        _service = service;
        _options = options;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var opt = _options.Value;
        if (!opt.IndustryEventsIngestEnabled)
        {
            _logger.LogDebug(
                "{Job} skipped: feature disabled via {Flag}.",
                nameof(IndustryEventsIngestJob),
                nameof(opt.IndustryEventsIngestEnabled));
            return;
        }

        try
        {
            var r = await _service.IngestAllAsync(ct).ConfigureAwait(false);
            context.Result =
                $"IndustryEventsIngest: polled={r.SourcesPolled}, skipped={r.SourcesSkipped}, "
                + $"parsed={r.EventsParsed}, upserted={r.Upserted}, failed={r.Failed}";
            _logger.LogInformation(
                "IndustryEventsIngest: polled={P} skipped={S} parsed={E} upserted={U} failed={X}.",
                r.SourcesPolled,
                r.SourcesSkipped,
                r.EventsParsed,
                r.Upserted,
                r.Failed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.Result = $"IndustryEventsIngest failed: {ex.GetType().Name}: {ex.Message}";
            _logger.LogError(ex, "IndustryEventsIngest job failed.");
        }
    }
}
