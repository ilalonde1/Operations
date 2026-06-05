#nullable enable
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Worker.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Opportunities.Worker.Services.Research;

/// <summary>
/// Drains <c>opportunities.BdResearchTriggers</c> on a short cadence. Each
/// Pending row is claimed atomically, dispatched through
/// <see cref="BdResearchExecutorService"/>, then marked Completed/Failed.
/// </summary>
internal sealed class BdResearchTriggerPollerBackgroundService : BackgroundService
{
    private const int MinPeriodSeconds = 5;
    private const int DefaultPeriodSeconds = 30;

    private readonly IBdResearchTriggerStore _triggerStore;
    private readonly BdResearchExecutorService _executorService;
    private readonly OpportunitiesWorkerOptions _options;
    private readonly ILogger<BdResearchTriggerPollerBackgroundService> _logger;

    public BdResearchTriggerPollerBackgroundService(
        IBdResearchTriggerStore triggerStore,
        BdResearchExecutorService executorService,
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<BdResearchTriggerPollerBackgroundService> logger)
    {
        _triggerStore = triggerStore;
        _executorService = executorService;
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
            "BdResearchTriggerPoller started. Period={Seconds}s ClaimedBy={Claimer}.",
            periodSeconds,
            claimer);

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

        _logger.LogInformation("BdResearchTriggerPoller stopping.");
    }

    private async Task DrainAsync(string claimer, CancellationToken ct)
    {
        var maxPerWake = _options.IngestionTriggerMaxPerWake <= 0 ? 25 : _options.IngestionTriggerMaxPerWake;
        var processedThisWake = 0;
        while (!ct.IsCancellationRequested)
        {
            if (processedThisWake >= maxPerWake)
            {
                _logger.LogInformation(
                    "BdResearchTriggerPoller reached max-per-wake cap of {MaxPerWake}; remaining triggers will run on the next tick.",
                    maxPerWake);
                return;
            }

            BdResearchTrigger? trigger;
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
                _logger.LogError(ex, "Failed to claim next pending BdResearchTrigger.");
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

    private async Task ProcessAsync(BdResearchTrigger trigger, CancellationToken ct)
    {
        if (trigger.ClaimToken is not { } claimToken)
        {
            _logger.LogError("BdResearchTrigger {Trigger} was claimed without a claim token.", trigger.Id);
            return;
        }

        try
        {
            var result = await _executorService
                .ExecuteOneAsync(trigger.CanonicalOrgId, trigger.ProviderName, ct)
                .ConfigureAwait(false);
            await _triggerStore.CompleteAsync(
                trigger.Id,
                claimToken,
                result is null ? BdResearchTriggerStatus.Failed : BdResearchTriggerStatus.Completed,
                inputTokens: result?.InputTokens,
                outputTokens: result?.OutputTokens,
                errorSummary: result is null
                    ? "Executor returned null (key missing, prompt missing, or HTTP failure  see Worker log)"
                    : null,
                ct).ConfigureAwait(false);

            _logger.LogInformation(
                "BdResearchTrigger {Trigger} for org {CanonicalOrgId}/{ProviderName} completed: success={Success}.",
                trigger.Id,
                trigger.CanonicalOrgId,
                trigger.ProviderName,
                result is not null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // On shutdown, leave the row InProgress. A restarted worker will
            // automatically reclaim stale rows after the configured window.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BdResearchTrigger {Trigger} dispatch failed.", trigger.Id);
            try
            {
                await _triggerStore.CompleteAsync(
                    trigger.Id,
                    claimToken,
                    BdResearchTriggerStatus.Failed,
                    inputTokens: null,
                    outputTokens: null,
                    errorSummary: Truncate(ex.Message, 2000),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception completeEx)
            {
                _logger.LogError(completeEx, "Failed to mark BdResearchTrigger {Trigger} as Failed.", trigger.Id);
            }
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max);
}
