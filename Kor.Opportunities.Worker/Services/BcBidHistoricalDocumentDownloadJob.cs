#nullable enable
using System;
using System.Threading.Tasks;
using Kor.Opportunities.Data.HistoricalOpportunities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

[DisallowConcurrentExecution]
internal sealed class BcBidHistoricalDocumentDownloadJob : IJob
{
    private readonly BcBidHistoricalDocumentDownloadService _service;
    private readonly IOptions<Options.OpportunitiesWorkerOptions> _options;
    private readonly ILogger<BcBidHistoricalDocumentDownloadJob> _logger;

    public BcBidHistoricalDocumentDownloadJob(
        BcBidHistoricalDocumentDownloadService service,
        IOptions<Options.OpportunitiesWorkerOptions> options,
        ILogger<BcBidHistoricalDocumentDownloadJob> logger)
    {
        _service = service;
        _options = options;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var opt = _options.Value;
        var batch = opt.BcBidHistoricalDocumentBatchSize > 0 ? opt.BcBidHistoricalDocumentBatchSize : 20;
        var maxAttempts = opt.BcBidHistoricalDocumentMaxAttempts > 0 ? opt.BcBidHistoricalDocumentMaxAttempts : 3;
        var root = string.IsNullOrWhiteSpace(opt.BcBidHistoricalDocumentArchiveRoot)
            ? @"C:\OpsArchive\Opportunities"
            : opt.BcBidHistoricalDocumentArchiveRoot;

        try
        {
            var result = await _service.DownloadBatchAsync(batch, maxAttempts, root, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Scheduled BcBidHistorical document download: attempted={A} downloaded={D} failed={F}.",
                result.Attempted,
                result.Downloaded,
                result.Failed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled BcBidHistorical document download failed.");
        }
    }
}
