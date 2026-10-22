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

    /// <summary>
    /// The forward cycle (Ian, 21 Oct 2026): grabbing a forward project opens a pursuit the Director's board shows; the
    /// manager sees it under New grabs until kept; the RFP for it, when it posts, is matched and joins that pursuit, owned by
    /// the forward owner whoever pressed Grab; letting the project go then leaves the pursuit alone because it has a tender.
    /// Does NOT cover the app's join prompt or the Worker's document fetch the join asks for.
    /// </summary>
    [Fact]
    public async Task ForwardGrab_OpensAPursuit_TheTenderJoinsIt_AndKeepClearsNewGrabs()
    {
        var cs = ConnectionString;
        if (string.IsNullOrWhiteSpace(cs)) return; // no DB in this environment

        var owner = "audittest-forward@korstructural.com";
        var other = "audittest-other@korstructural.com";
        var manager = "audittest-manager@korstructural.com";
        var tag = Guid.NewGuid().ToString("N")[..8];
        var lifecycle = new Kor.Opportunities.Data.MajorProjects.SqlPursuitLifecycleStore(cs);
        var grabStore = new SqlPursuitGrabStore(cs);
        var reviews = new SqlPursuitReviewStore(cs);
        long mpiId = 0, oppId = 0, engId = 0;
        await using var con = new SqlConnection(cs);
        await con.OpenAsync();
        try
        {
            await using (var insert = new SqlCommand(
                "INSERT INTO opportunities.MajorProjectsInventory (Province, SourceKey, ProjectName) OUTPUT inserted.Id VALUES (N'AB', @k, @n);", con))
            {
                insert.Parameters.AddWithValue("@k", $"AUDITTEST-{tag}");
                insert.Parameters.AddWithValue("@n", $"Auditville {tag} Aquatic Centre Replacement");
                mpiId = (long)(await insert.ExecuteScalarAsync())!;
            }

            Assert.Equal(Kor.Opportunities.Data.MajorProjects.LifecycleOutcome.Applied, await lifecycle.GrabProjectAsync(mpiId, owner, CancellationToken.None));
            engId = await ScalarAsync<long>(con, "SELECT EngagementId FROM opportunities.CrmEngagementProjectLink WHERE MajorProjectsInventoryId=@p0 AND MatchedBy=N'Grab'", mpiId);
            Assert.Equal(owner, await ScalarAsync<string>(con, "SELECT OwnerStaffId FROM opportunities.CrmEngagements WHERE Id=@p0", engId));
            var board = await reviews.ListBoardAsync(CancellationToken.None);
            Assert.Contains(board, r => r.EngagementId == engId && r.IsForward && r.Project.Contains(tag, StringComparison.Ordinal));

            var grabs = await grabStore.RecentGrabsAsync(manager, CancellationToken.None);
            Assert.Contains(grabs, g => g.EngagementId == engId && g.IsForward && g.OwnerMailbox == owner);

            // The RFP posts; someone else goes to grab it and is offered the forward pursuit instead.
            var opp = await new SqlOpportunityStore(cs).InsertAsync(new Opportunity
            {
                OpportunityKey = $"AUDITTEST-{Guid.NewGuid():N}",
                Name = $"Request for Proposals - Auditville {tag} Aquatic Centre Replacement - Prime Consultant Services",
                BuyerName = "AUDITTEST Town of Auditville", ProjectProvince = "AB", Status = OpportunityStatus.New,
            }, other, CancellationToken.None);
            oppId = opp.Id;
            var joined = await grabStore.GrabAsync(oppId, other, CancellationToken.None);
            Assert.True(joined.Outcome == GrabOutcome.Grabbed && joined.EngagementId == engId && joined.JoinedOwner is not null,
                $"EXPECTED the tender to join pursuit {engId}; ACTUAL {joined}");
            Assert.Equal(oppId, await ScalarAsync<long>(con, "SELECT OpportunityId FROM opportunities.CrmEngagements WHERE Id=@p0", engId));
            Assert.Equal(owner, await ScalarAsync<string>(con, "SELECT OwnerStaffId FROM opportunities.Opportunities WHERE Id=@p0", oppId));
            Assert.Equal(1, await ScalarAsync<int>(con, "SELECT COUNT(*) FROM opportunities.CrmEngagements WHERE OpportunityId=@p0", oppId));
            Assert.Equal(GrabOutcome.AlreadyTaken, (await grabStore.GrabAsync(oppId, other, CancellationToken.None)).Outcome);

            await grabStore.KeepAsync(engId, manager, CancellationToken.None);
            Assert.DoesNotContain(await grabStore.RecentGrabsAsync(manager, CancellationToken.None), g => g.EngagementId == engId);

            // Letting the project go now leaves the pursuit live: it has its tender.
            Assert.Equal(Kor.Opportunities.Data.MajorProjects.LifecycleOutcome.Applied, await lifecycle.ReleaseProjectAsync(mpiId, owner, CancellationToken.None));
            Assert.Equal(1, await ScalarAsync<int>(con, "SELECT Stage FROM opportunities.CrmEngagements WHERE Id=@p0", engId));
        }
        finally
        {
            if (engId > 0)
            {
                await ExecAsync(con, "DELETE FROM opportunities.OpportunityAssignmentLog WHERE EngagementId=@p0", engId);
                await ExecAsync(con, "DELETE FROM opportunities.CrmEngagementStageHistory WHERE EngagementId=@p0", engId);
                await ExecAsync(con, "DELETE FROM opportunities.CrmEngagementProjectLink WHERE EngagementId=@p0", engId);
                await ExecAsync(con, "DELETE FROM opportunities.CrmEngagements WHERE Id=@p0", engId);
            }
            if (mpiId > 0)
            {
                await ExecAsync(con, "DELETE FROM opportunities.OpportunityAssignmentLog WHERE MpiId=@p0", mpiId);
                await ExecAsync(con, "DELETE FROM opportunities.MajorProjectsInventory WHERE Id=@p0", mpiId);
            }
            await CleanupAsync(cs, oppId);
        }
    }

    /// <summary>A forward project with nothing joined, let go: its pursuit is archived (kept, out of live counts), not deleted.</summary>
    [Fact]
    public async Task ForwardRelease_ArchivesThePursuitItsGrabOpened()
    {
        var cs = ConnectionString;
        if (string.IsNullOrWhiteSpace(cs)) return; // no DB in this environment

        var owner = "audittest-forward@korstructural.com";
        var lifecycle = new Kor.Opportunities.Data.MajorProjects.SqlPursuitLifecycleStore(cs);
        long mpiId = 0, engId = 0;
        await using var con = new SqlConnection(cs);
        await con.OpenAsync();
        try
        {
            await using (var insert = new SqlCommand(
                "INSERT INTO opportunities.MajorProjectsInventory (Province, SourceKey, ProjectName) OUTPUT inserted.Id VALUES (N'BC', @k, N'AUDITTEST forward release');", con))
            {
                insert.Parameters.AddWithValue("@k", $"AUDITTEST-{Guid.NewGuid():N}");
                mpiId = (long)(await insert.ExecuteScalarAsync())!;
            }
            await lifecycle.GrabProjectAsync(mpiId, owner, CancellationToken.None);
            engId = await ScalarAsync<long>(con, "SELECT EngagementId FROM opportunities.CrmEngagementProjectLink WHERE MajorProjectsInventoryId=@p0 AND MatchedBy=N'Grab'", mpiId);

            Assert.Equal(Kor.Opportunities.Data.MajorProjects.LifecycleOutcome.Applied, await lifecycle.ReleaseProjectAsync(mpiId, owner, CancellationToken.None));
            Assert.Equal(8, await ScalarAsync<int>(con, "SELECT Stage FROM opportunities.CrmEngagements WHERE Id=@p0", engId));
            Assert.Equal(1, await ScalarAsync<int>(con, "SELECT COUNT(*) FROM opportunities.CrmEngagementStageHistory WHERE EngagementId=@p0 AND Stage=8", engId));
        }
        finally
        {
            if (engId > 0)
            {
                await ExecAsync(con, "DELETE FROM opportunities.OpportunityAssignmentLog WHERE EngagementId=@p0", engId);
                await ExecAsync(con, "DELETE FROM opportunities.CrmEngagementStageHistory WHERE EngagementId=@p0", engId);
                await ExecAsync(con, "DELETE FROM opportunities.CrmEngagementProjectLink WHERE EngagementId=@p0", engId);
                await ExecAsync(con, "DELETE FROM opportunities.CrmEngagements WHERE Id=@p0", engId);
            }
            if (mpiId > 0)
            {
                await ExecAsync(con, "DELETE FROM opportunities.OpportunityAssignmentLog WHERE MpiId=@p0", mpiId);
                await ExecAsync(con, "DELETE FROM opportunities.MajorProjectsInventory WHERE Id=@p0", mpiId);
            }
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
