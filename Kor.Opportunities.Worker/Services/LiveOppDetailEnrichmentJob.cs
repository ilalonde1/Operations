#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// Phase-2 live-opportunity detail enricher. Walks live BC Bid opportunities that
/// have not yet had their detail page read (DetailEnrichedAtUtc IS NULL), opens
/// the authenticated detail page via <see cref="BcBidLiveDetailExtractor"/>, and
/// FILL-ONLY persists the recovered Discipline (from the commodity list), buyer
/// contact, and RFx document references. Every attempted opp is stamped
/// DetailEnrichedAtUtc so it is processed exactly once — never re-queued (the fix
/// for the plan-taker starvation class of bug).
/// </summary>
[DisallowConcurrentExecution]
internal sealed class LiveOppDetailEnrichmentJob : IJob
{
    private const string SourcePortal = "BCBID";

    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<LiveOppDetailEnrichmentJob> _logger;
    private readonly BcBidLiveDetailExtractor _extractor;
    private readonly BcBidCredentials _credentials;
    private readonly PlaywrightBrowserPool _browserPool;

    public LiveOppDetailEnrichmentJob(
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<LiveOppDetailEnrichmentJob> logger,
        BcBidLiveDetailExtractor extractor,
        BcBidCredentials credentials,
        PlaywrightBrowserPool browserPool)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _browserPool = browserPool ?? throw new ArgumentNullException(nameof(browserPool));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.LiveOppDetailEnrichmentEnabled)
        {
            _logger.LogInformation("{Job}: disabled by configuration", nameof(LiveOppDetailEnrichmentJob));
            return;
        }
        if (!_credentials.IsConfigured)
        {
            _logger.LogInformation("{Job}: BC Bid credentials not configured; skipping", nameof(LiveOppDetailEnrichmentJob));
            return;
        }

        var ct = context.CancellationToken;
        var batchSize = Math.Max(1, opt.LiveOppDetailEnrichmentBatchSize);
        var db = opt.OpportunitiesDb!;

        var targets = await LoadTargetsAsync(db, batchSize, ct).ConfigureAwait(false);
        if (targets.Count == 0)
        {
            _logger.LogInformation("{Job}: no un-enriched BC Bid opportunities", nameof(LiveOppDetailEnrichmentJob));
            context.Result = "No un-enriched BCBID opps";
            return;
        }
        _logger.LogInformation("{Job}: processing {Count} BC Bid opportunities", nameof(LiveOppDetailEnrichmentJob), targets.Count);

        await using var ctxRef = await _browserPool.AcquireContextAsync(ct).ConfigureAwait(false);
        var page = await ctxRef.NewPageAsync().ConfigureAwait(false);
        await _extractor.LoginAsync(page, ct).ConfigureAwait(false);

        int processed = 0, disciplineSet = 0, contactSet = 0, docsWritten = 0, failures = 0;

        foreach (var t in targets)
        {
            ct.ThrowIfCancellationRequested();
            processed++;
            LiveDetailResult? result = null;
            try
            {
                result = await _extractor.ExtractAsync(page, t.Url, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures++;
                _logger.LogWarning(ex, "{Key}: BC Bid detail extract failed; marking attempted", t.OpportunityKey);
            }

            try
            {
                var (d, c, docs) = await PersistAsync(db, t, result, ct).ConfigureAwait(false);
                disciplineSet += d; contactSet += c; docsWritten += docs;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures++;
                _logger.LogWarning(ex, "{Key}: BC Bid detail persist failed", t.OpportunityKey);
            }
        }

        var summary = $"processed={processed}; disciplineSet={disciplineSet}; contactSet={contactSet}; docs={docsWritten}; failures={failures}";
        _logger.LogInformation("{Job}: {Summary}", nameof(LiveOppDetailEnrichmentJob), summary);
        context.Result = summary;
    }

    private static async Task<IReadOnlyList<Target>> LoadTargetsAsync(string db, int batch, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@batch) o.Id, o.OpportunityKey, o.Name, o.Url
FROM opportunities.Opportunities o
WHERE o.OpportunityKey LIKE 'BCBID%'
  AND o.Url IS NOT NULL
  AND o.Status IN (0,1)
  AND o.DetailEnrichedAtUtc IS NULL
ORDER BY o.SubmissionDeadlineUtc ASC;";   // soonest-closing first

        await using var con = new SqlConnection(db);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@batch", SqlDbType.Int).Value = batch;
        var list = new List<Target>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new Target(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        }
        return list;
    }

    /// <summary>Fill-only persist + idempotent docs + always-mark-attempted.</summary>
    private static async Task<(int discipline, int contact, int docs)> PersistAsync(
        string db, Target t, LiveDetailResult? result, CancellationToken ct)
    {
        int discSet = 0, contactSet = 0, docsWritten = 0;
        await using var con = new SqlConnection(db);
        await con.OpenAsync(ct).ConfigureAwait(false);

        if (result is not null)
        {
            var discipline = DisciplineClassifier.Classify(result.CommodityCodes, t.Name, null);
            if (discipline != OpportunityDiscipline.Unknown)
            {
                discSet += await ExecAsync(con,
                    "UPDATE opportunities.Opportunities SET Discipline=@v WHERE Id=@id AND Discipline=0",
                    ("@v", SqlDbType.Int, (int)discipline), ("@id", SqlDbType.BigInt, t.Id));
            }
            if (!string.IsNullOrWhiteSpace(result.ContactEmail))
            {
                contactSet += await ExecAsync(con,
                    "UPDATE opportunities.Opportunities SET BuyerContactEmail=@v WHERE Id=@id AND BuyerContactEmail IS NULL",
                    ("@v", SqlDbType.NVarChar, Trunc(result.ContactEmail, 255)), ("@id", SqlDbType.BigInt, t.Id));
            }
            if (!string.IsNullOrWhiteSpace(result.ContactName))
            {
                await ExecAsync(con,
                    "UPDATE opportunities.Opportunities SET BuyerContactName=@v WHERE Id=@id AND BuyerContactName IS NULL",
                    ("@v", SqlDbType.NVarChar, Trunc(result.ContactName, 120)), ("@id", SqlDbType.BigInt, t.Id));
            }
            if (!string.IsNullOrWhiteSpace(result.ContactPhone))
            {
                await ExecAsync(con,
                    "UPDATE opportunities.Opportunities SET BuyerContactPhone=@v WHERE Id=@id AND BuyerContactPhone IS NULL",
                    ("@v", SqlDbType.NVarChar, Trunc(result.ContactPhone, 40)), ("@id", SqlDbType.BigInt, t.Id));
            }
            foreach (var doc in result.Documents)
            {
                docsWritten += await ExecAsync(con, @"
INSERT INTO opportunities.OpportunityDocuments (OpportunityId, DocumentName, DocumentUrl, SourcePortal)
SELECT @id, @name, @url, @portal
WHERE NOT EXISTS (SELECT 1 FROM opportunities.OpportunityDocuments
                  WHERE OpportunityId=@id AND DocumentUrl=@url)",
                    ("@id", SqlDbType.BigInt, t.Id),
                    ("@name", SqlDbType.NVarChar, Trunc(doc.Name, 400)),
                    ("@url", SqlDbType.NVarChar, Trunc(doc.Url, 1000)),
                    ("@portal", SqlDbType.NVarChar, SourcePortal));
            }
        }

        // Always mark attempted — success, no-data, or extract failure — so no
        // opp is ever re-queued forever.
        await ExecAsync(con,
            "UPDATE opportunities.Opportunities SET DetailEnrichedAtUtc=sysdatetimeoffset() WHERE Id=@id AND DetailEnrichedAtUtc IS NULL",
            ("@id", SqlDbType.BigInt, t.Id));

        return (discSet, contactSet, docsWritten);
    }

    private static async Task<int> ExecAsync(SqlConnection con, string sql, params (string, SqlDbType, object)[] ps)
    {
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        foreach (var (n, ty, v) in ps) cmd.Parameters.Add(n, ty).Value = v;
        return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s.Substring(0, n);

    private sealed record Target(long Id, string OpportunityKey, string Name, string Url);
}
