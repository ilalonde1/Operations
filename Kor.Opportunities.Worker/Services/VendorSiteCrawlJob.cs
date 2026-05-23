#nullable enable
using System;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Awards;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

[DisallowConcurrentExecution]
internal sealed class VendorSiteCrawlJob : IJob
{
    private readonly VendorSiteCrawlService _service;
    private readonly IVendorSiteCrawlStore _store;
    private readonly IOptions<Options.OpportunitiesWorkerOptions> _options;
    private readonly ILogger<VendorSiteCrawlJob> _logger;

    public VendorSiteCrawlJob(
        VendorSiteCrawlService service,
        IVendorSiteCrawlStore store,
        IOptions<Options.OpportunitiesWorkerOptions> options,
        ILogger<VendorSiteCrawlJob> logger)
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
        if (!opt.VendorSiteCrawlEnabled) return;

        if (opt.VendorSiteCrawlTotalCap > 0)
        {
            var crawledSoFar = await _store.CountCrawledAsync(ct).ConfigureAwait(false);
            if (crawledSoFar >= opt.VendorSiteCrawlTotalCap)
            {
                _logger.LogInformation(
                    "VendorSiteCrawl paused: cap reached ({Crawled} >= {Cap}).",
                    crawledSoFar,
                    opt.VendorSiteCrawlTotalCap);
                return;
            }
        }

        var batch = opt.VendorSiteCrawlBatchSize > 0 ? opt.VendorSiteCrawlBatchSize : 2;
        var maxAttempts = opt.VendorSiteCrawlMaxAttempts > 0 ? opt.VendorSiteCrawlMaxAttempts : 2;

        try
        {
            var result = await _service.CrawlBatchAsync(batch, maxAttempts, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "VendorSiteCrawl batch: attempted={A} ok={O} failed={F} blocked={B}.",
                result.Attempted,
                result.Ok,
                result.Failed,
                result.Blocked);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VendorSiteCrawl job failed.");
        }
    }
}
