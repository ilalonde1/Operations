#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Sources;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// Runs once at startup. Calls <see cref="IOpportunitySourceStore.EnsureAsync"/>
/// for every source the Worker is expected to ingest from, so a fresh database
/// (just-migrated, no manual seed) gets the canonical rows automatically.
///
/// Idempotent — <c>EnsureAsync</c> is a guarded INSERT and never overwrites
/// hand-tweaked URLs/cadences on existing rows.
/// </summary>
internal sealed class SourceBootstrapHostedService : IHostedService
{
    private readonly IOpportunitySourceStore _sourceStore;
    private readonly ILogger<SourceBootstrapHostedService> _logger;

    public SourceBootstrapHostedService(
        IOpportunitySourceStore sourceStore,
        ILogger<SourceBootstrapHostedService> logger)
    {
        _sourceStore = sourceStore;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            // CanadaBuys "currently open tenders" CSV - the Open Government open-data
            // export. We use the open-only feed (not the all-time history) so the DB
            // tracks current pursuit candidates rather than years of closed notices.
            // Procurement category + region filter is applied client-side by
            // GenericCsvOpportunityProvider.
            var canadaBuys = await _sourceStore.EnsureAsync(
                new OpportunitySource
                {
                    Name = CanadaBuysIngestionJob.SourceName,
                    SourceType = OpportunitySourceType.GenericCsv,
                    BaseUrl = "https://canadabuys.canada.ca/opendata/pub/openTenderNotice-ouvertAvisAppelOffres.csv",
                    IsEnabled = true,
                    CrawlDelaySeconds = 7200,
                    RequestTimeoutSeconds = 90,
                },
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Source bootstrap: {Source} = {Id} (enabled={Enabled}, url={Url}).",
                canadaBuys.Name, canadaBuys.Id, canadaBuys.IsEnabled, canadaBuys.BaseUrl);

            var samGov = await _sourceStore.EnsureAsync(
                new OpportunitySource
                {
                    Name = SamGovIngestionJob.SourceName,
                    SourceType = OpportunitySourceType.SamGov,
                    BaseUrl = "https://api.sam.gov/prod/opportunities/v2/search",
                    IsEnabled = true,
                    CrawlDelaySeconds = 86400,
                    RequestTimeoutSeconds = 120,
                },
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Source bootstrap: {Source} = {Id} (enabled={Enabled}, url={Url}).",
                samGov.Name, samGov.Id, samGov.IsEnabled, samGov.BaseUrl);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A bootstrap failure must NOT crash the host — the operator may need to
            // start the service to inspect logs and fix the schema. The Quartz job
            // and trigger poller will surface the missing row when they run.
            _logger.LogError(ex, "Source bootstrap failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
