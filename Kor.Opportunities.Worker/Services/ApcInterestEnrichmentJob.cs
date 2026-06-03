#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// Continuous APC InterestedFirms enrichment. After the AlbertaPurchasingScraper
/// discovers new APC postings on its hourly cadence, this job walks any
/// APC opportunity that lacks OpportunityInterestedFirms rows and populates
/// them via the shared ApcInterestExtractor.
///
/// Resume-friendly: source query auto-skips postings already enriched.
/// One-canonical-at-a-time with per-firm try/catch; one bad row never kills
/// the batch.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class ApcInterestEnrichmentJob : IJob
{
    private const string PostingUrlTemplate = "https://purchasing.alberta.ca/posting/{0}";
    private const string SourcePortal = "APC";

    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<ApcInterestEnrichmentJob> _logger;
    private readonly ApcInterestExtractor _extractor;
    private readonly PlaywrightBrowserPool _browserPool;
    private readonly CanonicalOrgResolver _resolver;
    private readonly IOpportunityInterestedFirmStore _interestStore;

    public ApcInterestEnrichmentJob(
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<ApcInterestEnrichmentJob> logger,
        ApcInterestExtractor extractor,
        PlaywrightBrowserPool browserPool,
        CanonicalOrgResolver resolver,
        IOpportunityInterestedFirmStore interestStore)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _browserPool = browserPool ?? throw new ArgumentNullException(nameof(browserPool));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _interestStore = interestStore ?? throw new ArgumentNullException(nameof(interestStore));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.ApcInterestEnrichmentEnabled)
        {
            _logger.LogInformation("{Job}: disabled by configuration", nameof(ApcInterestEnrichmentJob));
            return;
        }

        var ct = context.CancellationToken;
        var batchSize = Math.Max(1, opt.ApcInterestEnrichmentBatchSize);

        var targets = await LoadTargetsAsync(opt.OpportunitiesDb!, batchSize, ct).ConfigureAwait(false);
        if (targets.Count == 0)
        {
            _logger.LogInformation("{Job}: no unenriched APC opportunities; nothing to do", nameof(ApcInterestEnrichmentJob));
            context.Result = "No unenriched APC opps";
            return;
        }
        _logger.LogInformation("{Job}: processing {Count} APC opportunities", nameof(ApcInterestEnrichmentJob), targets.Count);

        await using var ctxRef = await _browserPool.AcquireContextAsync(ct).ConfigureAwait(false);
        var page = await ctxRef.NewPageAsync().ConfigureAwait(false);

        int processed = 0;
        int suppliersWritten = 0;
        int canonResolved = 0;
        int postingsWithSuppliers = 0;
        int failures = 0;

        foreach (var t in targets)
        {
            ct.ThrowIfCancellationRequested();
            processed++;
            var url = string.Format(CultureInfo.InvariantCulture, PostingUrlTemplate, t.ExternalReference);
            try
            {
                var suppliers = await _extractor.ExtractAsync(page, url, ct).ConfigureAwait(false);
                if (suppliers.Count == 0)
                {
                    _logger.LogDebug("{Key}: no suppliers on detail page", t.OpportunityKey);
                    continue;
                }
                postingsWithSuppliers++;

                foreach (var supplier in suppliers)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(supplier.Name)) continue;
                        var guessKind = GuessKindFromDescriptor(supplier.Name, supplier.Descriptor);
                        var resolvedId = await _resolver.ResolveAsync(
                            supplier.Name,
                            guessKind,
                            OrgAliasSources.Manual + ":APC.InterestedFirms",
                            ct,
                            allowCreate: true,
                            minConfidenceForCreate: 70).ConfigureAwait(false);
                        if (resolvedId.HasValue) canonResolved++;

                        await _interestStore.UpsertAsync(
                            opportunityId: t.OpportunityId,
                            rawFirmName: supplier.Name,
                            resolvedCanonicalOrgId: resolvedId,
                            resolvedKind: resolvedId.HasValue ? guessKind : null,
                            sourcePortal: SourcePortal,
                            sourcePostingUrl: url,
                            expressedAtUtc: null,
                            notes: supplier.Descriptor,
                            rawJson: supplier.RawText,
                            ct: ct).ConfigureAwait(false);
                        suppliersWritten++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "{Key}: skip supplier '{Name}'", t.OpportunityKey, supplier.Name);
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                failures++;
                _logger.LogWarning(ex, "{Key}: posting-level failure; continuing", t.OpportunityKey);
            }
        }

        var summary = $"processed={processed}; withSuppliers={postingsWithSuppliers}; suppliers={suppliersWritten}; resolved={canonResolved}; failures={failures}";
        _logger.LogInformation("{Job}: {Summary}", nameof(ApcInterestEnrichmentJob), summary);
        context.Result = summary;
    }

    private static async Task<IReadOnlyList<TargetOpp>> LoadTargetsAsync(string connStr, int batchSize, CancellationToken ct)
    {
        // Resume-friendly: only opps that have NO APC interest rows yet.
        // Ordered by OpportunityKey DESC so freshest postings get enriched first.
        const string sql = @"
SELECT TOP (@batch) o.Id, o.OpportunityKey, REPLACE(o.OpportunityKey, 'APCALLBU-', '') AS ExternalReference
FROM   opportunities.Opportunities o
WHERE  o.OpportunityKey LIKE 'APCALLBU-%'
  AND  NOT EXISTS (
    SELECT 1 FROM opportunities.OpportunityInterestedFirms f
    WHERE f.OpportunityId = o.Id AND f.SourcePortal = 'APC'
  )
ORDER BY o.OpportunityKey DESC;";

        await using var con = new SqlConnection(connStr);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@batch", SqlDbType.Int).Value = batchSize;

        var list = new List<TargetOpp>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new TargetOpp(r.GetInt64(0), r.GetString(1), r.GetString(2)));
        }
        return list;
    }

    private static string GuessKindFromDescriptor(string name, string? descriptor)
    {
        var blob = (name + " " + (descriptor ?? "")).ToLowerInvariant();
        if (blob.Contains("structural")) return OrgKinds.Competitor;
        if (blob.Contains("architect")) return OrgKinds.Architect;
        if (blob.Contains("interior design") || blob.Contains("urban planning")) return OrgKinds.Architect;
        if (blob.Contains("general contract") || blob.Contains("construction services")) return OrgKinds.GeneralContractor;
        if (blob.Contains("engineering")) return OrgKinds.Competitor;
        return OrgKinds.Unknown;
    }

    private sealed record TargetOpp(long OpportunityId, string OpportunityKey, string ExternalReference);
}
