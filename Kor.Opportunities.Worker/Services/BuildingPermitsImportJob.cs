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
    private readonly IBuildingPermitStore _store;
    private readonly IOptions<Options.OpportunitiesWorkerOptions> _options;
    private readonly ILogger<BuildingPermitsImportJob> _logger;

    public BuildingPermitsImportJob(
        BuildingPermitsImportService service,
        IBuildingPermitStore store,
        IOptions<Options.OpportunitiesWorkerOptions> options,
        ILogger<BuildingPermitsImportJob> logger)
    {
        _service = service;
        _store = store;
        _options = options;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var opt = _options.Value;
        if (!opt.BuildingPermitsImportEnabled)
        {
            _logger.LogDebug(
                "{Job} skipped: feature disabled via {Flag}.",
                nameof(BuildingPermitsImportJob),
                nameof(opt.BuildingPermitsImportEnabled));
            return;
        }

        var totalCap = opt.BuildingPermitsTotalCap;
        var importedSoFar = 0;
        if (totalCap > 0)
        {
            importedSoFar = await _store.CountAsync(ct).ConfigureAwait(false);
            if (importedSoFar >= totalCap)
            {
                _logger.LogInformation(
                    "BuildingPermitsImport paused: cap reached ({Imported} >= {Cap}).",
                    importedSoFar,
                    totalCap);
                return;
            }
        }

        var maxRowsPerSource = opt.BuildingPermitsMaxRowsPerSource > 0
            ? opt.BuildingPermitsMaxRowsPerSource
            : 5000;
        if (totalCap > 0)
        {
            maxRowsPerSource = Math.Min(maxRowsPerSource, totalCap - importedSoFar);
        }
        if (maxRowsPerSource <= 0) return;

        try
        {
            var r = await _service.ImportAllAsync(maxRowsPerSource, ct).ConfigureAwait(false);
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
