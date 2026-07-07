#nullable enable

using System;
using System.Data;
using System.Threading.Tasks;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Jobs;

/// <summary>
/// R81: nightly Intel* retirement job. Soft-deletes rows whose
/// LastSeenAtUtc is older than the configured threshold by setting
/// RetiredAtUtc. Read queries already filter RetiredAtUtc IS NULL, so
/// retired rows disappear from briefs and dossier but remain on disk
/// for audit / undo.
///
/// Schedule: daily at 02:00 UTC (before the catch-up job at 03:30 so
/// retired rows don't re-extract).
///
/// Default thresholds (override via configuration if needed):
///   IntelPerson / Affiliation / Signal / Work / Risk / Narrative
///     → not seen for 180 days
///   IntelAction
///     → Status in ('Done','Dismissed') AND StatusChangedAtUtc &lt; 30 days ago
///     → OR Status='Open' AND LastSeenAtUtc &lt; 180 days ago
///
/// Idempotent: rows already retired are skipped (filter on
/// RetiredAtUtc IS NULL).
/// </summary>
[DisallowConcurrentExecution]
public sealed class IntelRetirementJob : IJob
{
    private const int CommandTimeoutSeconds = 120;
    private const int StaleDays = 180;
    private const int ResolvedActionGraceDays = 30;

    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<IntelRetirementJob> _logger;

    public IntelRetirementJob(
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<IntelRetirementJob> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Fix F13 (CRM plan 2026-07-06): intel belonging to the buyer of a LIVE
    /// pursuit (CrmEngagements Stage 1/3) is exempt from pure-staleness
    /// retirement — a pursuit owner's Buyer-intel button must not go dark at
    /// day 180 mid-pursuit. Org-keyed tables exempt directly; people (and
    /// their affiliations) are exempt while affiliated with a live-pursuit
    /// buyer. Resolved (Done/Dismissed) actions still retire normally.
    /// </summary>
    private const string LivePursuitOrgExemption = @"
   AND NOT EXISTS (SELECT 1 FROM opportunities.CrmEngagements ce
                   WHERE ce.Stage IN (1, 3)
                     AND ce.BuyerCanonicalOrgId = {0}.CanonicalOrgId)";

    private const string LivePursuitPersonExemption = @"
   AND NOT EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation pa
                   JOIN opportunities.CrmEngagements ce
                     ON ce.BuyerCanonicalOrgId = pa.CanonicalOrgId
                    AND ce.Stage IN (1, 3)
                   WHERE pa.IntelPersonId = opportunities.IntelPerson.Id)";

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context?.CancellationToken ?? System.Threading.CancellationToken.None;

        await using var con = new SqlConnection(_options.Value.OpportunitiesDb);
        await con.OpenAsync(ct).ConfigureAwait(false);

        var person = await RetireStaleAsync(con, "opportunities.IntelPerson", "not-seen-180-days", StaleDays, ct, LivePursuitPersonExemption).ConfigureAwait(false);
        var affiliation = await RetireStaleAsync(con, "opportunities.IntelPersonAffiliation", "not-seen-180-days", StaleDays, ct, string.Format(LivePursuitOrgExemption, "opportunities.IntelPersonAffiliation")).ConfigureAwait(false);
        var signal = await RetireStaleAsync(con, "opportunities.IntelSignal", "not-seen-180-days", StaleDays, ct, string.Format(LivePursuitOrgExemption, "opportunities.IntelSignal")).ConfigureAwait(false);
        var work = await RetireStaleAsync(con, "opportunities.IntelWork", "not-seen-180-days", StaleDays, ct, string.Format(LivePursuitOrgExemption, "opportunities.IntelWork")).ConfigureAwait(false);
        var risk = await RetireStaleAsync(con, "opportunities.IntelRisk", "not-seen-180-days", StaleDays, ct, string.Format(LivePursuitOrgExemption, "opportunities.IntelRisk")).ConfigureAwait(false);
        var narrative = await RetireStaleAsync(con, "opportunities.IntelNarrative", "not-seen-180-days", StaleDays, ct, string.Format(LivePursuitOrgExemption, "opportunities.IntelNarrative")).ConfigureAwait(false);
        var actionStale = await RetireStaleAsync(con, "opportunities.IntelAction", "not-seen-180-days", StaleDays, ct, string.Format(LivePursuitOrgExemption, "opportunities.IntelAction")).ConfigureAwait(false);
        var actionResolved = await RetireResolvedActionsAsync(con, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "{Job}: completed. person={Person}; affiliation={Affiliation}; signal={Signal}; work={Work}; risk={Risk}; narrative={Narrative}; actionStale={ActionStale}; actionResolved={ActionResolved}.",
            nameof(IntelRetirementJob),
            person, affiliation, signal, work, risk, narrative, actionStale, actionResolved);
    }

    private static async Task<int> RetireStaleAsync(SqlConnection con, string table, string reason, int days, System.Threading.CancellationToken ct, string extraPredicate = "")
    {
        var sql = $@"
UPDATE {table}
   SET RetiredAtUtc = sysdatetimeoffset(),
       RetiredReason = @reason,
       UpdatedAtUtc = sysdatetimeoffset()
 WHERE RetiredAtUtc IS NULL
   AND LastSeenAtUtc < DATEADD(DAY, -@days, sysdatetimeoffset()){extraPredicate};";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@reason", SqlDbType.NVarChar, 200).Value = reason;
        cmd.Parameters.Add("@days", SqlDbType.Int).Value = days;
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<int> RetireResolvedActionsAsync(SqlConnection con, System.Threading.CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.IntelAction
   SET RetiredAtUtc = sysdatetimeoffset(),
       RetiredReason = N'resolved-' + LOWER(Status),
       UpdatedAtUtc = sysdatetimeoffset()
 WHERE RetiredAtUtc IS NULL
   AND Status IN (N'Done', N'Dismissed')
   AND StatusChangedAtUtc IS NOT NULL
   AND StatusChangedAtUtc < DATEADD(DAY, -@grace, sysdatetimeoffset());";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@grace", SqlDbType.Int).Value = ResolvedActionGraceDays;
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
