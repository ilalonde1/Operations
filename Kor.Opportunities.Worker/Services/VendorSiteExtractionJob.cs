#nullable enable
using System;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Awards;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

[DisallowConcurrentExecution]
internal sealed class VendorSiteExtractionJob : IJob
{
    private readonly VendorSiteExtractionService _service;
    private readonly IVendorSiteCrawlStore _store;
    private readonly IOptions<Options.OpportunitiesWorkerOptions> _options;
    private readonly ILogger<VendorSiteExtractionJob> _logger;

    public VendorSiteExtractionJob(
        VendorSiteExtractionService service,
        IVendorSiteCrawlStore store,
        IOptions<Options.OpportunitiesWorkerOptions> options,
        ILogger<VendorSiteExtractionJob> logger)
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
        if (!opt.VendorSiteExtractionEnabled) return;

        if (opt.VendorSiteExtractionTotalCap > 0)
        {
            var extractedSoFar = await _store.CountExtractedAsync(ct).ConfigureAwait(false);
            if (extractedSoFar >= opt.VendorSiteExtractionTotalCap)
            {
                _logger.LogInformation(
                    "VendorSiteExtraction paused: cap reached ({Extracted} >= {Cap}).",
                    extractedSoFar,
                    opt.VendorSiteExtractionTotalCap);
                return;
            }
        }

        var batch = opt.VendorSiteExtractionBatchSize > 0 ? opt.VendorSiteExtractionBatchSize : 5;
        var maxAttempts = opt.VendorSiteExtractionMaxAttempts > 0 ? opt.VendorSiteExtractionMaxAttempts : 3;

        try
        {
            var result = await _service.ExtractBatchAsync(batch, maxAttempts, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "VendorSiteExtraction batch: attempted={A} extracted={E} failed={F}.",
                result.Attempted,
                result.Extracted,
                result.Failed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VendorSiteExtraction job failed.");
        }
    }
}
