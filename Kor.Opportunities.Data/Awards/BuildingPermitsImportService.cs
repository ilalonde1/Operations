#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Awards;

public sealed class BuildingPermitsImportService
{
    private readonly IBuildingPermitStore _store;
    private readonly VancouverOpenDataPermitAdapter _vancouverAdapter;
    private readonly ILogger<BuildingPermitsImportService> _logger;

    public BuildingPermitsImportService(
        IBuildingPermitStore store,
        VancouverOpenDataPermitAdapter vancouverAdapter,
        ILogger<BuildingPermitsImportService> logger)
    {
        _store = store;
        _vancouverAdapter = vancouverAdapter;
        _logger = logger;
    }

    public sealed record ImportResult(
        int SourcesAttempted,
        int TotalPulled,
        int TotalUpserted,
        int TotalCanonicals,
        int TotalFailed);

    public async Task<ImportResult> ImportAllAsync(int maxRowsPerSource, CancellationToken ct)
    {
        var sources = await _store.ListActiveSourcesAsync(ct).ConfigureAwait(false);
        var pulled = 0;
        var upserted = 0;
        var canonicals = 0;
        var failed = 0;

        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var r = source.Adapter switch
                {
                    VancouverOpenDataPermitAdapter.AdapterName => await _vancouverAdapter.ImportAsync(source, maxRowsPerSource, ct)
                        .ConfigureAwait(false),
                    _ => throw new InvalidOperationException($"Unknown permit adapter '{source.Adapter}'."),
                };

                pulled += r.Pulled;
                upserted += r.Upserted;
                canonicals += r.CanonicalsResolved;
                failed += r.Failed;

                _logger.LogInformation(
                    "Permit source '{Name}' adapter={Adapter}: pulled={Pulled} upserted={Upserted} canonicals={Canonicals} failed={Failed}.",
                    source.Name,
                    source.Adapter,
                    r.Pulled,
                    r.Upserted,
                    r.CanonicalsResolved,
                    r.Failed);

                await _store.UpdateSourceHeartbeatAsync(source.Id, null, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Permit source '{Name}' failed.", source.Name);
                try
                {
                    await _store.UpdateSourceHeartbeatAsync(source.Id, ex.Message, ct).ConfigureAwait(false);
                }
                catch
                {
                }

                failed++;
            }
        }

        return new ImportResult(sources.Count, pulled, upserted, canonicals, failed);
    }
}
