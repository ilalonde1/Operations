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
/// Pursuit-lifecycle accountability (migration 284): owning a prime-target
/// play (MajorProjectsInventory.OwnerStaffId) takes it off the shared boards
/// and the weekly attack sheet — so an owned-but-unworked play is invisible
/// inventory. This job releases ownership after the configured window
/// (default 14 days) so nothing is parked silently.
///
/// The release is never a surprise: the per-owner morning digest starts
/// warning at day 10 ("N days left"). Every release writes an MpiReap row to
/// the shared audit stream (OpportunityAssignmentLog) with the previous
/// owner, so admin can see exactly what came back and from whom.
///
/// Since 21 Oct 2026 a grab opens the project's pursuit at once (CrmEngagementProjectLink, MatchedBy 'Grab'), so
/// "worked" is detectable and the guard is activity, not age: a grab is released only when its pursuit has had no
/// tender join it, has no next step still to come, and has not been touched for the window. The release archives that
/// pursuit (stage 8, kept) in the same transaction. A grab from before then, with no pursuit, is released on age alone.
/// </summary>
[DisallowConcurrentExecution]
public sealed class MpiOwnershipReaperJob : IJob
{
    private const int CommandTimeoutSeconds = 60;

    // OUTPUT ... INTO a table variable first: the assignment log sits in FK
    // relationships, which SQL Server rejects as a direct OUTPUT INTO target.
    private const string ReapSql = @"
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

DECLARE @reaped TABLE (Id bigint NOT NULL, PrevOwner nvarchar(300) NULL);

DECLARE @cutoff datetimeoffset = DATEADD(DAY, -@days, SYSDATETIMEOFFSET());

UPDATE m
   SET OwnerStaffId = NULL,
       OwnedAtUtc   = NULL
OUTPUT deleted.Id, deleted.OwnerStaffId INTO @reaped (Id, PrevOwner)
FROM opportunities.MajorProjectsInventory m
WHERE m.OwnerStaffId IS NOT NULL
  AND m.OwnedAtUtc < @cutoff
  -- Its pursuit, if it has one, has gone quiet: no tender, no next step to come, untouched for the window.
  AND NOT EXISTS (SELECT 1
                  FROM opportunities.CrmEngagementProjectLink l
                  JOIN opportunities.CrmEngagements e ON e.Id = l.EngagementId
                  WHERE l.MajorProjectsInventoryId = m.Id AND l.MatchedBy = N'Grab' AND e.Stage IN (1, 3)
                    AND (e.OpportunityId IS NOT NULL OR e.NextActionDueUtc >= SYSDATETIMEOFFSET() OR e.UpdatedAtUtc >= @cutoff));

DECLARE @closed TABLE (Id bigint);
UPDATE e
   SET Stage = 8, ClosedAtUtc = SYSDATETIMEOFFSET(), UpdatedAtUtc = SYSDATETIMEOFFSET(), UpdatedBy = N'system:reaper'
OUTPUT inserted.Id INTO @closed
  FROM opportunities.CrmEngagements e
  JOIN opportunities.CrmEngagementProjectLink l ON l.EngagementId = e.Id AND l.MatchedBy = N'Grab'
  JOIN @reaped r ON r.Id = l.MajorProjectsInventoryId
 WHERE e.Stage = 1 AND e.OpportunityId IS NULL
   -- The same guard again, on the row being archived: a next step set between the two statements keeps the pursuit
   -- (audit C4, 2026-10-21).
   AND NOT (e.NextActionDueUtc >= SYSDATETIMEOFFSET() OR e.UpdatedAtUtc >= @cutoff);
INSERT INTO opportunities.CrmEngagementStageHistory (EngagementId, Stage, ByStaffId) SELECT Id, 8, N'system:reaper' FROM @closed;

INSERT INTO opportunities.OpportunityAssignmentLog (MpiId, Action, FromStaffId, ByStaffId, Reason)
SELECT Id, N'MpiReap', PrevOwner, N'system:reaper',
       N'Auto-released after ' + CAST(@days AS nvarchar(10)) + N' days with no next step and nothing done'
FROM @reaped;

COMMIT TRANSACTION;
SELECT COUNT(*) FROM @reaped;";

    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<MpiOwnershipReaperJob> _logger;

    public MpiOwnershipReaperJob(
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<MpiOwnershipReaperJob> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.MpiOwnershipReaperEnabled)
        {
            _logger.LogInformation("{Job}: disabled by configuration", nameof(MpiOwnershipReaperJob));
            return;
        }

        var days = Math.Clamp(opt.MpiOwnershipReapDays, 3, 90);
        var ct = context.CancellationToken;

        await using var con = new SqlConnection(opt.OpportunitiesDb);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(ReapSql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@days", SqlDbType.Int).Value = days;

        var reaped = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));

        var summary = $"released {reaped} owned play(s) older than {days}d back to the pool";
        _logger.LogInformation("{Job}: {Summary}", nameof(MpiOwnershipReaperJob), summary);
        context.Result = summary;
    }
}
