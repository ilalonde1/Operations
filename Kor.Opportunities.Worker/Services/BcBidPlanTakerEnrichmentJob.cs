#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// Continuous BC Bid Plan Takers enrichment. Walks BC Bid opportunities that
/// have no BCBID OpportunityInterestedFirms rows yet and populates plan takers.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class BcBidPlanTakerEnrichmentJob : IJob
{
    private const string SourcePortal = "BCBID";

    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<BcBidPlanTakerEnrichmentJob> _logger;
    private readonly BcBidPlanTakerExtractor _extractor;
    private readonly BcBidCredentials _credentials;
    private readonly PlaywrightBrowserPool _browserPool;
    private readonly CanonicalOrgResolver _resolver;
    private readonly IOpportunityInterestedFirmStore _interestStore;

    public BcBidPlanTakerEnrichmentJob(
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<BcBidPlanTakerEnrichmentJob> logger,
        BcBidPlanTakerExtractor extractor,
        BcBidCredentials credentials,
        PlaywrightBrowserPool browserPool,
        CanonicalOrgResolver resolver,
        IOpportunityInterestedFirmStore interestStore)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _browserPool = browserPool ?? throw new ArgumentNullException(nameof(browserPool));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _interestStore = interestStore ?? throw new ArgumentNullException(nameof(interestStore));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.BcBidPlanTakerEnrichmentEnabled)
        {
            _logger.LogInformation("{Job}: disabled by configuration", nameof(BcBidPlanTakerEnrichmentJob));
            return;
        }

        if (!_credentials.IsConfigured)
        {
            _logger.LogInformation("{Job}: BC Bid credentials not configured; skipping plan taker enrichment", nameof(BcBidPlanTakerEnrichmentJob));
            return;
        }

        var ct = context.CancellationToken;
        var batchSize = Math.Max(1, opt.BcBidPlanTakerEnrichmentBatchSize);

        var targets = await LoadTargetsAsync(opt.OpportunitiesDb!, batchSize, ct).ConfigureAwait(false);
        if (targets.Count == 0)
        {
            _logger.LogInformation("{Job}: no unenriched BC Bid opportunities; nothing to do", nameof(BcBidPlanTakerEnrichmentJob));
            context.Result = "No unenriched BCBID opps";
            return;
        }
        _logger.LogInformation("{Job}: processing {Count} BC Bid opportunities", nameof(BcBidPlanTakerEnrichmentJob), targets.Count);

        await using var ctxRef = await _browserPool.AcquireContextAsync(ct).ConfigureAwait(false);
        var page = await ctxRef.NewPageAsync().ConfigureAwait(false);
        await _extractor.LoginAsync(page, ct).ConfigureAwait(false);

        int processed = 0;
        int suppliersWritten = 0;
        int canonResolved = 0;
        int postingsWithSuppliers = 0;
        int failures = 0;

        foreach (var t in targets)
        {
            ct.ThrowIfCancellationRequested();
            processed++;
            try
            {
                var suppliers = await _extractor.ExtractAsync(page, t.DetailUrl, ct).ConfigureAwait(false);
                if (suppliers.Count == 0)
                {
                    _logger.LogDebug("{Key}: no plan takers on detail page", t.OpportunityKey);
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
                            OrgAliasSources.Manual + ":BCBID.PlanTakers",
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
                            sourcePostingUrl: t.DetailUrl,
                            expressedAtUtc: null,
                            notes: supplier.Descriptor,
                            rawJson: supplier.RawText,
                            ct: ct).ConfigureAwait(false);
                        suppliersWritten++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "{Key}: skip plan taker '{Name}'", t.OpportunityKey, supplier.Name);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures++;
                _logger.LogWarning(ex, "{Key}: BC Bid plan taker failure; continuing", t.OpportunityKey);
            }
        }

        var summary = $"processed={processed}; withPlanTakers={postingsWithSuppliers}; suppliers={suppliersWritten}; resolved={canonResolved}; failures={failures}";
        _logger.LogInformation("{Job}: {Summary}", nameof(BcBidPlanTakerEnrichmentJob), summary);
        context.Result = summary;
    }

    private static async Task<IReadOnlyList<TargetOpp>> LoadTargetsAsync(string connStr, int batchSize, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@batch) o.Id, o.OpportunityKey, o.Url
FROM opportunities.Opportunities o
WHERE o.OpportunityKey LIKE 'BCBID%'
  AND o.Url IS NOT NULL
  AND NOT EXISTS (
    SELECT 1 FROM opportunities.OpportunityInterestedFirms f
    WHERE f.OpportunityId = o.Id AND f.SourcePortal = 'BCBID'
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

    private sealed record TargetOpp(long OpportunityId, string OpportunityKey, string DetailUrl);
}
