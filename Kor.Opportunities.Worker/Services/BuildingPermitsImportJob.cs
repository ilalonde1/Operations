#nullable enable
using System;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Awards;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

[DisallowConcurrentExecution]
internal sealed class BuildingPermitsImportJob : IJob
{
    private readonly BuildingPermitsImportService _service;
    private readonly IOptions<Options.OpportunitiesWorkerOptions> _options;
    private readonly ILogger<BuildingPermitsImportJob> _logger;

    public BuildingPermitsImportJob(
        BuildingPermitsImportService service,
        IOptions<Options.OpportunitiesWorkerOptions> options,
        ILogger<BuildingPermitsImportJob> logger)
    {
        _service = service;
        _options = options;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        if (!_options.Value.BuildingPermitsImportEnabled)
        {
            return;
        }

        try
        {
            var r = await _service.ImportAllAsync(ct).ConfigureAwait(false);
            _logger.LogInformation(
                "BuildingPermitsImport: sources={S} pulled={P} upserted={U} canonicals={C} failed={F}.",
                r.SourcesAttempted,
                r.TotalPulled,
                r.TotalUpserted,
                r.TotalCanonicals,
                r.TotalFailed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BuildingPermitsImport job failed.");
        }
    }
}
