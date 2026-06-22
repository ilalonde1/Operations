#nullable enable
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Worker.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Opportunities.Worker.Services.Research;

/// <summary>Drains durable ad-hoc person-research requests.</summary>
internal sealed class BdPersonResearchTriggerPollerBackgroundService : BackgroundService
{
    private const int MinPeriodSeconds = 5;
    private const int DefaultPeriodSeconds = 30;

    private readonly IBdPersonResearchTriggerStore _triggerStore;
    private readonly BdPersonResearchExecutorService _executorService;
    private readonly OpportunitiesWorkerOptions _workerOptions;
    private readonly BdPersonResearchExecutorOptions _personOptions;
    private readonly ILogger<BdPersonResearchTriggerPollerBackgroundService> _logger;

    public BdPersonResearchTriggerPollerBackgroundService(
        IBdPersonResearchTriggerStore triggerStore,
        BdPersonResearchExecutorService executorService,
        IOptions<OpportunitiesWorkerOptions> workerOptions,
        IOptions<BdPersonResearchExecutorOptions> personOptions,
        ILogger<BdPersonResearchTriggerPollerBackgroundService> logger)
    {
        _triggerStore = triggerStore;
        _executorService = executorService;
        _workerOptions = workerOptions.Value;
        _personOptions = personOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_personOptions.Enabled)
        {
            _logger.LogInformation("BdPersonResearchTriggerPoller disabled with the person research executor.");
            return;
        }

        var periodSeconds = Math.Max(MinPeriodSeconds, _workerOptions.IngestionTriggerPollSeconds <= 0
            ? DefaultPeriodSeconds
            : _workerOptions.IngestionTriggerPollSeconds);
        var period = TimeSpan.FromSeconds(periodSeconds);
        var claimer = $"{Environment.MachineName}/{Environment.ProcessId}";

        _logger.LogInformation(
            "BdPersonResearchTriggerPoller started. Period={Seconds}s ClaimedBy={Claimer}.",
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
            // Expected on shutdown.
        }

        _logger.LogInformation("BdPersonResearchTriggerPoller stopping.");
    }

    private async Task DrainAsync(string claimer, CancellationToken ct)
    {
        var maxPerWake = _workerOptions.IngestionTriggerMaxPerWake <= 0
            ? 25
            : _workerOptions.IngestionTriggerMaxPerWake;
        var processedThisWake = 0;

        while (!ct.IsCancellationRequested)
        {
            if (processedThisWake >= maxPerWake)
            {
                _logger.LogInformation(
                    "BdPersonResearchTriggerPoller reached max-per-wake cap of {MaxPerWake}.",
                    maxPerWake);
                return;
            }

            BdPersonResearchTrigger? trigger;
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
                _logger.LogError(ex, "Failed to claim next pending BdPersonResearchTrigger.");
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

    private async Task ProcessAsync(BdPersonResearchTrigger trigger, CancellationToken ct)
    {
        if (trigger.ClaimToken is not { } claimToken)
        {
            _logger.LogError("BdPersonResearchTrigger {Trigger} was claimed without a claim token.", trigger.Id);
            return;
        }

        try
        {
            var result = await _executorService
                .ExecuteOneAsync(trigger.IntelPersonId, ct)
                .ConfigureAwait(false);
            await _triggerStore.CompleteAsync(
                trigger.Id,
                claimToken,
                result is null ? BdResearchTriggerStatus.Failed : BdResearchTriggerStatus.Completed,
                inputTokens: result?.InputTokens,
                outputTokens: result?.OutputTokens,
                errorSummary: result is null
                    ? "Executor returned null (person missing, prompt missing, or research failure; see Worker log)"
                    : null,
                ct).ConfigureAwait(false);

            _logger.LogInformation(
                "BdPersonResearchTrigger {Trigger} for IntelPerson {IntelPersonId}/{ProviderName} completed: success={Success}.",
                trigger.Id,
                trigger.IntelPersonId,
                trigger.ProviderName,
                result is not null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BdPersonResearchTrigger {Trigger} dispatch failed.", trigger.Id);
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
                _logger.LogError(
                    completeEx,
                    "Failed to mark BdPersonResearchTrigger {Trigger} as Failed.",
                    trigger.Id);
            }
        }
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
}
