using System.Data;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Opportunities;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

/// <summary>
/// Integration tests for the pursuit-lifecycle WRITE path (audit 2026-07-01 M5 —
/// grab/reassign shipped with zero automated tests). They run against the DB in
/// KOR_OPPORTUNITIES_OPPORTUNITIESDB, creating their own AUDITTEST- opportunity
/// and deleting everything they created in a finally (the opportunity delete
/// cascades the engagement and its stage history; assignment-log rows are
/// deleted explicitly because that table has no FKs by design).
/// When the env var is absent (CI without DB access) each test no-ops.
/// </summary>
public sealed class PursuitLifecycleIntegrationTests
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB");

    [Fact]
    public async Task Grab_ClaimsOpportunity_CreatesOwnedEngagement_LogsAndRecordsStage()
    {
        var cs = ConnectionString;
        if (string.IsNullOrWhiteSpace(cs)) return; // no DB in this environment

        var actor = "audittest-grab@korstructural.com";
        var oppStore = new SqlOpportunityStore(cs);
        var grabStore = new SqlPursuitGrabStore(cs);
        long oppId = 0;
        try
        {
            var opp = await oppStore.InsertAsync(NewTestOpportunity(), actor, CancellationToken.None);
            oppId = opp.Id;

            var result = await grabStore.GrabAsync(oppId, actor, CancellationToken.None);

            Assert.Equal(GrabOutcome.Grabbed, result.Outcome);
            Assert.True(result.EngagementId > 0);

            await using var con = new SqlConnection(cs);
            await con.OpenAsync();
            Assert.Equal(4, await ScalarAsync<int>(con, "SELECT Status FROM opportunities.Opportunities WHERE Id=@p0", oppId));
            Assert.Equal(actor, await ScalarAsync<string>(con, "SELECT OwnerStaffId FROM opportunities.Opportunities WHERE Id=@p0", oppId));
            Assert.Equal(1, await ScalarAsync<int>(con, "SELECT Stage FROM opportunities.CrmEngagements WHERE Id=@p0", result.EngagementId));
            Assert.Equal(actor, await ScalarAsync<string>(con, "SELECT OwnerStaffId FROM opportunities.CrmEngagements WHERE Id=@p0", result.EngagementId));
            Assert.Equal(1, await ScalarAsync<int>(con, "SELECT COUNT(*) FROM opportunities.OpportunityAssignmentLog WHERE EngagementId=@p0 AND Action=N'Grab'", result.EngagementId));
            Assert.Equal(1, await ScalarAsync<int>(con, "SELECT COUNT(*) FROM opportunities.CrmEngagementStageHistory WHERE EngagementId=@p0 AND Stage=1", result.EngagementId));

            // Race guard: a second grab must lose without minting a duplicate.
            var second = await grabStore.GrabAsync(oppId, "audittest-loser@korstructural.com", CancellationToken.None);
            Assert.Equal(GrabOutcome.AlreadyTaken, second.Outcome);
            Assert.Equal(1, await ScalarAsync<int>(con, "SELECT COUNT(*) FROM opportunities.CrmEngagements WHERE OpportunityId=@p0", oppId));
        }
        finally
        {
            await CleanupAsync(cs, oppId);
        }
    }

    [Fact]
    public async Task Reassign_MovesOwner_GuardsStaleFrom_AndLogs()
    {
        var cs = ConnectionString;
        if (string.IsNullOrWhiteSpace(cs)) return; // no DB in this environment

        var actor = "audittest-reassign@korstructural.com";
        var target = "audittest-target@korstructural.com";
        var oppStore = new SqlOpportunityStore(cs);
        var grabStore = new SqlPursuitGrabStore(cs);
        var overwatch = new SqlPursuitOverwatchStore(cs);
        long oppId = 0;
        try
        {
            var opp = await oppStore.InsertAsync(NewTestOpportunity(), actor, CancellationToken.None);
            oppId = opp.Id;
            var grab = await grabStore.GrabAsync(oppId, actor, CancellationToken.None);
            Assert.Equal(GrabOutcome.Grabbed, grab.Outcome);

            var outcome = await overwatch.ReassignAsync(
                grab.EngagementId, oppId, fromOwner: actor, toOwner: target, byStaff: actor, reason: "audit test", CancellationToken.None);
            Assert.Equal(ReassignOutcome.Reassigned, outcome);

            await using var con = new SqlConnection(cs);
            await con.OpenAsync();
            Assert.Equal(target, await ScalarAsync<string>(con, "SELECT OwnerStaffId FROM opportunities.CrmEngagements WHERE Id=@p0", grab.EngagementId));
            Assert.Equal(target, await ScalarAsync<string>(con, "SELECT OwnerStaffId FROM opportunities.Opportunities WHERE Id=@p0", oppId));
            Assert.Equal(1, await ScalarAsync<int>(con, "SELECT COUNT(*) FROM opportunities.OpportunityAssignmentLog WHERE EngagementId=@p0 AND Action=N'Reassign'", grab.EngagementId));

            // Stale-from guard: reassigning with the OLD owner must not clobber.
            var stale = await overwatch.ReassignAsync(
                grab.EngagementId, oppId, fromOwner: actor, toOwner: "audittest-clobber@korstructural.com", byStaff: actor, reason: null, CancellationToken.None);
            Assert.Equal(ReassignOutcome.OwnerChanged, stale);
            Assert.Equal(target, await ScalarAsync<string>(con, "SELECT OwnerStaffId FROM opportunities.CrmEngagements WHERE Id=@p0", grab.EngagementId));
        }
        finally
        {
            await CleanupAsync(cs, oppId);
        }
    }

    private static Opportunity NewTestOpportunity() => new()
    {
        OpportunityKey = $"AUDITTEST-{Guid.NewGuid():N}",
        Name = "AUDITTEST pursuit-lifecycle integration row",
        BuyerName = "AUDITTEST Buyer",
        Status = OpportunityStatus.New,
    };

    private static async Task CleanupAsync(string cs, long oppId)
    {
        if (oppId <= 0) return;
        await using var con = new SqlConnection(cs);
        await con.OpenAsync();
        await ExecAsync(con, "DELETE l FROM opportunities.OpportunityAssignmentLog l JOIN opportunities.CrmEngagements e ON e.Id=l.EngagementId WHERE e.OpportunityId=@p0", oppId);
        // Opportunity delete cascades the engagement + its stage history.
        await ExecAsync(con, "DELETE FROM opportunities.Opportunities WHERE Id=@p0", oppId);
    }

    private static async Task<T> ScalarAsync<T>(SqlConnection con, string sql, long p0)
    {
        await using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.Add("@p0", SqlDbType.BigInt).Value = p0;
        var value = await cmd.ExecuteScalarAsync();
        return (T)Convert.ChangeType(value!, typeof(T));
    }

    private static async Task ExecAsync(SqlConnection con, string sql, long p0)
    {
        await using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.Add("@p0", SqlDbType.BigInt).Value = p0;
        await cmd.ExecuteNonQueryAsync();
    }
}
