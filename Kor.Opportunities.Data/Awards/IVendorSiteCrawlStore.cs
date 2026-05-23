#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Awards;

public interface IVendorSiteCrawlStore
{
    Task<int> CountCrawledAsync(CancellationToken ct);

    /// <summary>
    /// Vendor-website URLs that have an AgentVendorWebsite on at least one OpportunityAwards row
    /// but no VendorSiteCrawl row yet, or whose last attempt failed and Attempts &lt; maxAttempts.
    /// </summary>
    Task<IReadOnlyList<string>> ListPendingWebsitesAsync(int batchSize, int maxAttempts, CancellationToken ct);

    Task RecordCaptureAsync(string website, RawSiteCapture capture, CancellationToken ct);

    Task RecordFailureAsync(string website, string status, string errorMessage, CancellationToken ct);
}
