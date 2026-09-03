#nullable enable
using System;
using System.Threading.Tasks;
using Kor.Operations.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Kor.Operations.App.Tests.PMTools;

/// <summary>
/// The workload meeting is only editable while it is the latest meeting. That rule used to
/// live solely in the view model, compared against a list loaded once when the window opened
/// and never refreshed — so a window left open while somebody else created the next meeting
/// kept writing into the previous one. Silently: the client-side check saw nothing wrong.
/// Unrecoverably: the carry-forward copy into the new meeting had already run.
///
/// These tests pin the guard where it cannot be bypassed — in the same statement as the write.
/// They run against a scratch database on LocalDB, created and dropped per class, so they never
/// touch KorTransmittals. Tagged Integration so the hermetic CI run skips them; if LocalDB is
/// absent the class reports that rather than failing, matching the pattern in
/// PursuitLifecycleIntegrationTests.
/// </summary>
[Trait("Category", "Integration")]
public sealed class WorkloadMeetingStoreGuardTests : IAsyncLifetime
{
    private const string MasterCs =
        @"Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=True;Connect Timeout=30;Pooling=False";

    private readonly string _dbName = "KorWorkloadGuardTest_" + Guid.NewGuid().ToString("N");
    private string _cs = string.Empty;
    private bool _available;

    private SqlWorkloadMeetingStore Store => new(_cs);

    public async Task InitializeAsync()
    {
        try
        {
            await ExecAsync(MasterCs, $"CREATE DATABASE [{_dbName}];").ConfigureAwait(false);
        }
        catch (SqlException)
        {
            _available = false;
            return;
        }
        catch (PlatformNotSupportedException)
        {
            _available = false;
            return;
        }

        _cs = $@"Server=(localdb)\MSSQLLocalDB;Database={_dbName};Integrated Security=True;Connect Timeout=30;Pooling=False";
        _available = true;
        await Store.EnsureTablesAsync().ConfigureAwait(false);
        await SweepAbandonedScratchDatabasesAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (!_available) return;
        await DropAsync(_dbName).ConfigureAwait(false);
    }

    /// <summary>
    /// A killed or crashed run leaves its scratch database behind, and those accumulate on the
    /// developer's LocalDB instance forever. xUnit runs the tests in one class sequentially, so
    /// by the time any test starts, every other database with this prefix belongs to a run that
    /// is already over and is safe to remove.
    /// </summary>
    private async Task SweepAbandonedScratchDatabasesAsync()
    {
        var abandoned = new System.Collections.Generic.List<string>();
        try
        {
            await using var cn = new SqlConnection(MasterCs);
            await cn.OpenAsync().ConfigureAwait(false);
            await using var cmd = new SqlCommand(
                "SELECT name FROM sys.databases WHERE name LIKE 'KorWorkloadGuardTest[_]%' AND name <> @Current;", cn);
            cmd.Parameters.AddWithValue("@Current", _dbName);
            await using var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await rd.ReadAsync().ConfigureAwait(false))
            {
                abandoned.Add(rd.GetString(0));
            }
        }
        catch (SqlException)
        {
            return;
        }

        foreach (var name in abandoned)
        {
            await DropAsync(name).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Best effort. A scratch database that will not drop must never fail the run it belongs
    /// to — the next run's sweep will collect it.
    /// </summary>
    private static async Task DropAsync(string dbName)
    {
        try
        {
            // SINGLE_USER first, or an open session keeps DROP waiting indefinitely.
            await ExecAsync(MasterCs,
                $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}];")
                .ConfigureAwait(false);
        }
        catch (SqlException)
        {
        }
    }

    [Fact]
    public async Task WriteToASupersededMeetingIsRefusedAndChangesNothing()
    {
        if (!_available) return;

        // The meeting a user had open, with a project already on it...
        var opened = await InsertMeetingAsync(new DateTime(2026, 8, 10), createdAt: new DateTime(2026, 8, 10, 16, 5, 0));
        Assert.True(await Store.UpsertProjectPriorityAsync(opened, "01809-01", 2, "original"));

        // ...superseded while that window sat open.
        await InsertMeetingAsync(new DateTime(2026, 8, 24), createdAt: new DateTime(2026, 8, 24, 16, 45, 0));

        Assert.False(await Store.UpsertProjectPriorityAsync(opened, "01809-01", 5, null));
        Assert.False(await Store.SaveProjectNotesAsync(opened, "01809-01", "typed after the fact"));
        Assert.False(await Store.SaveMeetingNotesAsync(opened, "meeting notes after the fact"));

        // Refused means refused: the superseded meeting is untouched, not partially written.
        var rows = await Store.GetProjectsForMeetingAsync(opened);
        var row = Assert.Single(rows);
        Assert.Equal(2, row.Priority);
        Assert.Equal("original", row.Notes);

        var meetings = await Store.GetAllMeetingsAsync();
        Assert.Null(Assert.Single(meetings, m => m.Id == opened).Notes);
    }

    [Fact]
    public async Task WriteToTheLatestMeetingIsAccepted()
    {
        if (!_available) return;

        await InsertMeetingAsync(new DateTime(2026, 8, 10), createdAt: new DateTime(2026, 8, 10, 16, 5, 0));
        var latest = await InsertMeetingAsync(new DateTime(2026, 8, 24), createdAt: new DateTime(2026, 8, 24, 16, 45, 0));

        Assert.True(await Store.UpsertProjectPriorityAsync(latest, "01809-01", 3, "note"));
        Assert.True(await Store.SaveProjectNotesAsync(latest, "01809-01", "revised"));
        Assert.True(await Store.SaveMeetingNotesAsync(latest, "agenda"));

        var row = Assert.Single(await Store.GetProjectsForMeetingAsync(latest));
        Assert.Equal(3, row.Priority);
        Assert.Equal("revised", row.Notes);
        Assert.Equal("agenda", Assert.Single(
            await Store.GetAllMeetingsAsync(), m => m.Id == latest).Notes);
    }

    [Fact]
    public async Task LatestIsBrokenByCreatedAtWhenTwoMeetingsShareADate()
    {
        if (!_available) return;

        // Not hypothetical: 2026-07-27 carries two meetings in production, ten minutes apart.
        // The store must break the tie exactly as the UI's ORDER BY does, or the two disagree
        // about which meeting is current and legitimate edits get refused.
        var sameDate = new DateTime(2026, 7, 27);
        var earlier = await InsertMeetingAsync(sameDate, createdAt: new DateTime(2026, 7, 27, 15, 44, 0));
        var later = await InsertMeetingAsync(sameDate, createdAt: new DateTime(2026, 7, 27, 15, 54, 0));

        Assert.False(await Store.UpsertProjectPriorityAsync(earlier, "01809-01", 1, null));
        Assert.True(await Store.UpsertProjectPriorityAsync(later, "01809-01", 1, null));
    }

    [Fact]
    public async Task ClearingAPriorityOnASupersededMeetingIsAlsoRefused()
    {
        if (!_available) return;

        // Priority 0 takes the DELETE branch, which is a different statement from the MERGE
        // and therefore needs its own guard — a deletion is as destructive as a write.
        var opened = await InsertMeetingAsync(new DateTime(2026, 8, 10), createdAt: new DateTime(2026, 8, 10, 16, 5, 0));
        Assert.True(await Store.UpsertProjectPriorityAsync(opened, "01809-01", 4, "keep me"));

        await InsertMeetingAsync(new DateTime(2026, 8, 24), createdAt: new DateTime(2026, 8, 24, 16, 45, 0));

        Assert.False(await Store.UpsertProjectPriorityAsync(opened, "01809-01", 0, null));
        Assert.Single(await Store.GetProjectsForMeetingAsync(opened));
    }

    [Fact]
    public async Task CarryForwardIsExemptSoASecondCreatorCannotLeaveAnEmptyMeeting()
    {
        if (!_available) return;

        // CreateMeetingAsync and CarryForwardProjectsAsync are two calls. If somebody creates a
        // third meeting in the gap, guarding the copy would leave the new meeting empty. The
        // copy is therefore exempt, and this pins that: seeding a meeting that is NOT the latest
        // still works.
        var source = await InsertMeetingAsync(new DateTime(2026, 8, 10), createdAt: new DateTime(2026, 8, 10, 16, 5, 0));
        Assert.True(await Store.UpsertProjectPriorityAsync(source, "01809-01", 2, "carried"));

        var target = await InsertMeetingAsync(new DateTime(2026, 8, 24), createdAt: new DateTime(2026, 8, 24, 16, 45, 0));

        // The interloper: created after our target, so target is no longer latest.
        await InsertMeetingAsync(new DateTime(2026, 8, 25), createdAt: new DateTime(2026, 8, 25, 16, 0, 0));

        await Store.CarryForwardProjectsAsync(source, target);

        var carried = Assert.Single(await Store.GetProjectsForMeetingAsync(target));
        Assert.Equal(2, carried.Priority);
        Assert.Equal("carried", carried.Notes);
    }

    /// <summary>
    /// Inserts a meeting with an explicit CreatedAt. CreateMeetingAsync stamps UtcNow, which is
    /// too coarse to order two meetings created microseconds apart in a test.
    /// </summary>
    private async Task<Guid> InsertMeetingAsync(DateTime meetingDate, DateTime createdAt)
    {
        var id = Guid.NewGuid();
        await using var cn = new SqlConnection(_cs);
        await cn.OpenAsync().ConfigureAwait(false);
        await using var cmd = new SqlCommand(
            @"INSERT INTO dbo.WorkloadMeetings (Id, MeetingDate, Notes, CreatedAt, CreatedBy)
              VALUES (@Id, @MeetingDate, NULL, @CreatedAt, 'test');", cn);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@MeetingDate", meetingDate);
        cmd.Parameters.AddWithValue("@CreatedAt", createdAt);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        return id;
    }

    private static async Task ExecAsync(string connectionString, string sql)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync().ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, cn);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
