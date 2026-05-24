#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Ingestion;
using Kor.Opportunities.Data.Sources;
using Kor.Opportunities.Worker.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// Drains <c>opportunities.IngestionTriggers</c> on a short cadence (default
/// 30s). Each Pending row is claimed atomically — two pollers cannot grab the
/// same row — then dispatched through the same <see cref="IIngestionDispatcher"/>
/// the Quartz job uses, then marked Completed/Failed.
///
/// This is the bridge between the WPF "Run Now" button and the actual ingestion
/// pipeline: the WPF process inserts a trigger row and waits; the Worker picks
/// it up within one poll cycle.
/// </summary>
internal sealed class IngestionTriggerPollerBackgroundService : BackgroundService
{
    private const int MinPeriodSeconds = 5;
    private const int DefaultPeriodSeconds = 30;

    private readonly IIngestionTriggerStore _triggerStore;
    private readonly IIngestionDispatcher _dispatcher;
    private readonly AwardIngestionService _awardService;
    private readonly IOpportunitySourceStore _sourceStore;
    private readonly OpportunitiesWorkerOptions _options;
    private readonly ILogger<IngestionTriggerPollerBackgroundService> _logger;

    public IngestionTriggerPollerBackgroundService(
        IIngestionTriggerStore triggerStore,
        IIngestionDispatcher dispatcher,
        AwardIngestionService awardService,
        IOpportunitySourceStore sourceStore,
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<IngestionTriggerPollerBackgroundService> logger)
    {
        _triggerStore = triggerStore;
        _dispatcher = dispatcher;
        _awardService = awardService;
        _sourceStore = sourceStore;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var periodSeconds = Math.Max(MinPeriodSeconds, _options.IngestionTriggerPollSeconds <= 0
            ? DefaultPeriodSeconds
            : _options.IngestionTriggerPollSeconds);
        var period = TimeSpan.FromSeconds(periodSeconds);

        var claimer = $"{Environment.MachineName}/{Environment.ProcessId}";

        _logger.LogInformation(
            "IngestionTriggerPoller started. Period={Seconds}s ClaimedBy={Claimer}.",
            periodSeconds, claimer);

        // Drain anything already pending before starting the timer so a Run-Now
        // request that landed seconds before service start fires immediately.
        await DrainAsync(claimer, stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(period);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await DrainAsync(claimer, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }

        _logger.LogInformation("IngestionTriggerPoller stopping.");
    }

    private async Task DrainAsync(string claimer, CancellationToken ct)
    {
        // Drain in a loop — if multiple Run-Now requests were submitted between
        // ticks, handle them all in one wake instead of waiting another period.
        var maxPerWake = _options.IngestionTriggerMaxPerWake <= 0 ? 25 : _options.IngestionTriggerMaxPerWake;
        var processedThisWake = 0;
        while (!ct.IsCancellationRequested)
        {
            if (processedThisWake >= maxPerWake)
            {
                _logger.LogInformation(
                    "IngestionTriggerPoller reached max-per-wake cap of {MaxPerWake}; remaining triggers will run on the next tick.",
                    maxPerWake);
                return;
            }

            IngestionTrigger? trigger;
            try
            {
                trigger = await _triggerStore.ClaimNextPendingAsync(claimer, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to claim next pending IngestionTrigger.");
                return;
            }

            if (trigger is null)
            {
                return;
            }

            await ProcessAsync(trigger, ct).ConfigureAwait(false);
            processedThisWake++;
        }
    }

    private async Task ProcessAsync(IngestionTrigger trigger, CancellationToken ct)
    {
        var correlationId = $"trigger:{trigger.Id}";
        try
        {
            var sourceType = await GetSourceTypeAsync(trigger.OpportunitySourceId, ct).ConfigureAwait(false);
            if (sourceType is not null && _awardService.CanHandle(sourceType.Value))
            {
                var (success, inserted, updated, error, runId) = await _awardService
                    .IngestAsync(trigger.OpportunitySourceId, correlationId, ct).ConfigureAwait(false);
                await _triggerStore.CompleteAsync(
                    trigger.Id,
                    success ? IngestionTriggerStatus.Completed : IngestionTriggerStatus.Failed,
                    ingestionRunId: runId,
                    errorSummary: error,
                    ct).ConfigureAwait(false);

                _logger.LogInformation(
                    "Award IngestionTrigger {Trigger} for {SourceType} completed: success={Success} inserted={Inserted} updated={Updated}.",
                    trigger.Id, sourceType, success, inserted, updated);
                return;
            }

            var dispatch = await _dispatcher.RunByIdAsync(trigger.OpportunitySourceId, correlationId, ct).ConfigureAwait(false);
            await _triggerStore.CompleteAsync(
                trigger.Id,
                dispatch.Result.Success ? IngestionTriggerStatus.Completed : IngestionTriggerStatus.Failed,
                ingestionRunId: dispatch.Result.RunId,
                errorSummary: dispatch.Result.ErrorSummary,
                ct).ConfigureAwait(false);

            _logger.LogInformation(
                "IngestionTrigger {Trigger} for {Source} ({Type}) completed: success={Success} inserted={Inserted} duplicate={Duplicate}.",
                trigger.Id, dispatch.Source.Name, dispatch.Source.SourceType,
                dispatch.Result.Success, dispatch.Result.Inserted, dispatch.Result.Duplicate);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // On shutdown, leave the row InProgress. A restarted worker will
            // automatically reclaim stale rows after the configured window.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IngestionTrigger {Trigger} dispatch failed.", trigger.Id);
            try
            {
                await _triggerStore.CompleteAsync(
                    trigger.Id,
                    IngestionTriggerStatus.Failed,
                    ingestionRunId: null,
                    errorSummary: Truncate(ex.Message, 2000),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception completeEx)
            {
                _logger.LogError(completeEx, "Failed to mark IngestionTrigger {Trigger} as Failed.", trigger.Id);
            }
        }
    }

    private async Task<OpportunitySourceType?> GetSourceTypeAsync(Guid sourceId, CancellationToken ct)
    {
        var sources = await _sourceStore.ListEnabledAsync(ct).ConfigureAwait(false);
        foreach (var source in sources)
        {
            if (source.Id == sourceId)
            {
                return source.SourceType;
            }
        }

        return null;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max);
}
