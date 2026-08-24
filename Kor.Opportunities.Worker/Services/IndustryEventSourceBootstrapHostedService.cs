#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.IndustryEvents;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// Runs once at startup and registers the association calendars listed in
/// <see cref="IndustryEventSourceSeeds.Default"/>, so a fresh database
/// self-populates.
///
/// Mirrors <see cref="SourceBootstrapHostedService"/>: <c>EnsureAsync</c> is a
/// guarded INSERT keyed on CalendarUrl and never overwrites an operator's
/// edits, so disabling or retuning a source in the database sticks.
/// </summary>
internal sealed class IndustryEventSourceBootstrapHostedService : IHostedService
{
    private readonly IIndustryEventSourceStore _sourceStore;
    private readonly ILogger<IndustryEventSourceBootstrapHostedService> _logger;

    public IndustryEventSourceBootstrapHostedService(
        IIndustryEventSourceStore sourceStore,
        ILogger<IndustryEventSourceBootstrapHostedService> logger)
    {
        _sourceStore = sourceStore;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var seed in IndustryEventSourceSeeds.Default)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var row = await _sourceStore.EnsureAsync(seed, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "Industry event source: {Source} = {Id} (active={Active}, parser={Parser}, url={Url}).",
                    row.Name,
                    row.Id,
                    row.IsActive,
                    row.ParserKey,
                    row.CalendarUrl);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A bootstrap failure must NOT crash the host — the operator may
            // need the service up to read the logs. The ingest job will report
            // the missing rows on its next tick.
            _logger.LogError(ex, "Industry event source bootstrap failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
