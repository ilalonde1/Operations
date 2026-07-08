#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Jobs;

/// <summary>
/// THE nightly CRM enrichment job (plan 3.1, 2026-07-07). One job, sections —
/// never sibling jobs (plan anti-feature 15: job sprawl is developer-facing
/// bloat). Sections:
///
///   1. Email warmth: per live-pursuit buyer org with a website domain,
///      aggregate the filed-email corpus (KorEmailIndex.dbo.Emails, ~368k
///      rows) into opportunities.CrmBuyerEmailWarmth — last touch, last
///      inbound, 90-day and all-time counts, top correspondent. LIKE-scans
///      over delimited recipient lists are nightly-tier cost (~4s/domain,
///      measured 2026-07-07); they must never run live.
///
/// Privacy invariant: org-level aggregates only — counts, dates, ONE
/// correspondent address. Never subjects or bodies.
///
/// The email index needs its own connection (EmailIndexDb option — the
/// opportunities login has no rights there by design, D7): section skips
/// with a warning when unset so the Worker never hard-fails on config.
/// </summary>
[DisallowConcurrentExecution]
public sealed class CrmEnrichmentJob : IJob
{
    private const int CommandTimeoutSeconds = 120;

    /// <summary>Freemail/ISP domains that would aggregate strangers' mail into
    /// a buyer's warmth. A buyer org with only a generic domain gets no rollup.</summary>
    private static readonly HashSet<string> GenericDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "outlook.com", "hotmail.com", "live.com", "yahoo.com", "yahoo.ca",
        "icloud.com", "me.com", "shaw.ca", "telus.net", "aol.com", "msn.com", "protonmail.com",
    };

    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<CrmEnrichmentJob> _logger;

    public CrmEnrichmentJob(
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<CrmEnrichmentJob> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context?.CancellationToken ?? CancellationToken.None;
        var opt = _options.Value;

        var (updated, skipped) = await RunEmailWarmthSectionAsync(opt, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "{Job}: completed. emailWarmth updated={Updated} skippedGenericOrNoDomain={Skipped}.",
            nameof(CrmEnrichmentJob), updated, skipped);
    }

    private async Task<(int Updated, int Skipped)> RunEmailWarmthSectionAsync(
        OpportunitiesWorkerOptions opt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opt.EmailIndexDb))
        {
            _logger.LogWarning(
                "{Job}: email-warmth section skipped — EmailIndexDb connection not configured "
                + "(KOR_OPPORTUNITIES_EMAILINDEXDB env var / appsettings 'EmailIndexDb').",
                nameof(CrmEnrichmentJob));
            return (0, 0);
        }

        // 1. Live-pursuit buyer orgs with a usable website domain (opportunities conn).
        var targets = new List<(long OrgId, string Domain)>();
        await using (var opsCon = new SqlConnection(opt.OpportunitiesDb))
        {
            await opsCon.OpenAsync(ct).ConfigureAwait(false);
            const string sql = @"
SELECT DISTINCT o.Id, o.WebsiteDomain
FROM opportunities.CrmEngagements e
JOIN opportunities.CanonicalOrg o
  ON o.Id = e.BuyerCanonicalOrgId
 AND o.WebsiteDomain IS NOT NULL
 AND LTRIM(RTRIM(o.WebsiteDomain)) <> N''
WHERE e.Stage IN (1, 3);";
            await using var cmd = new SqlCommand(sql, opsCon) { CommandTimeout = CommandTimeoutSeconds };
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                targets.Add((reader.GetInt64(0), reader.GetString(1).Trim()));
            }
        }

        var skipped = targets.Count(t => GenericDomains.Contains(t.Domain));
        var usable = targets.Where(t => !GenericDomains.Contains(t.Domain)).ToList();
        if (usable.Count == 0)
        {
            return (0, skipped);
        }

        // 2. Per-domain aggregate over the filed-email corpus (email-index conn).
        //    One domain at a time — nightly-tier LIKE scans, sequential on purpose.
        const string warmthSql = @"
SELECT MAX(m.SentOnUtc)                                                        AS LastTouchUtc,
       MAX(CASE WHEN m.FromEmail LIKE N'%@' + @domain THEN m.SentOnUtc END)    AS LastInboundUtc,
       SUM(CASE WHEN m.SentOnUtc >= DATEADD(DAY, -90, SYSDATETIMEOFFSET()) THEN 1 ELSE 0 END) AS Emails90d,
       COUNT_BIG(*)                                                            AS EmailsAllTime
FROM dbo.Emails m
WHERE m.FromEmail LIKE N'%@' + @domain
   OR m.ToList  LIKE N'%@' + @domain + N'%'
   OR m.CcList  LIKE N'%@' + @domain + N'%';

SELECT TOP 1 m.FromEmail
FROM dbo.Emails m
WHERE m.FromEmail LIKE N'%@' + @domain
GROUP BY m.FromEmail
ORDER BY COUNT_BIG(*) DESC;";

        const string upsertSql = @"
MERGE opportunities.CrmBuyerEmailWarmth AS t
USING (SELECT @orgId AS CanonicalOrgId) AS s
   ON t.CanonicalOrgId = s.CanonicalOrgId
WHEN MATCHED THEN UPDATE SET
    Domain = @domain, LastTouchUtc = @lastTouch, LastInboundUtc = @lastInbound,
    Emails90d = @e90, EmailsAllTime = @eAll, TopCorrespondent = @top,
    ComputedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN INSERT
    (CanonicalOrgId, Domain, LastTouchUtc, LastInboundUtc, Emails90d, EmailsAllTime, TopCorrespondent)
    VALUES (@orgId, @domain, @lastTouch, @lastInbound, @e90, @eAll, @top);";

        var updated = 0;
        await using var mailCon = new SqlConnection(opt.EmailIndexDb);
        await mailCon.OpenAsync(ct).ConfigureAwait(false);
        await using var writeCon = new SqlConnection(opt.OpportunitiesDb);
        await writeCon.OpenAsync(ct).ConfigureAwait(false);

        foreach (var (orgId, domain) in usable)
        {
            ct.ThrowIfCancellationRequested();

            DateTimeOffset? lastTouch = null, lastInbound = null;
            var e90 = 0;
            long eAll = 0;
            string? top = null;

            try
            {
                await using var cmd = new SqlCommand(warmthSql, mailCon) { CommandTimeout = CommandTimeoutSeconds };
                cmd.Parameters.Add("@domain", SqlDbType.NVarChar, 200).Value = domain;
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    // dbo.Emails.SentOnUtc is datetime2 (UTC by contract) —
                    // GetDateTimeOffset would throw InvalidCastException.
                    lastTouch = r.IsDBNull(0) ? null : new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(0), DateTimeKind.Utc));
                    lastInbound = r.IsDBNull(1) ? null : new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc));
                    e90 = r.IsDBNull(2) ? 0 : r.GetInt32(2);
                    eAll = r.IsDBNull(3) ? 0 : r.GetInt64(3);
                }

                if (await r.NextResultAsync(ct).ConfigureAwait(false)
                    && await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    top = r.IsDBNull(0) ? null : r.GetString(0);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad domain (weird chars, timeout) must not kill the sweep.
                _logger.LogWarning(ex, "{Job}: warmth aggregate failed for domain {Domain}; org {OrgId} skipped.",
                    nameof(CrmEnrichmentJob), domain, orgId);
                continue;
            }

            await using (var up = new SqlCommand(upsertSql, writeCon) { CommandTimeout = CommandTimeoutSeconds })
            {
                up.Parameters.Add("@orgId", SqlDbType.BigInt).Value = orgId;
                up.Parameters.Add("@domain", SqlDbType.NVarChar, 200).Value = domain;
                up.Parameters.Add("@lastTouch", SqlDbType.DateTimeOffset).Value = (object?)lastTouch ?? DBNull.Value;
                up.Parameters.Add("@lastInbound", SqlDbType.DateTimeOffset).Value = (object?)lastInbound ?? DBNull.Value;
                up.Parameters.Add("@e90", SqlDbType.Int).Value = e90;
                up.Parameters.Add("@eAll", SqlDbType.BigInt).Value = eAll;
                up.Parameters.Add("@top", SqlDbType.NVarChar, 320).Value = (object?)top ?? DBNull.Value;
                await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            updated++;
        }

        return (updated, skipped);
    }
}
