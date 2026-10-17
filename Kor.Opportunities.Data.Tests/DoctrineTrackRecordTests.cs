using System.Text.Json;
using System.Data;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

/// <summary>
/// Covers per-move counts, distinct pursuit outcomes, the UTC date window, starting stages, exact flag thresholds and
/// old JSON. Does not cover SQL reads or rendered bindings; a wrong SQL join could pass every pure-function test here.
/// </summary>
public sealed class DoctrineTrackRecordTests
{
    private static readonly DateOnly Day = new(2026, 9, 1);
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static HourMove Move(int rank, long? pursuit = null) => new(rank, "Call", 15, new[] { "Due" }, null,
        Array.Empty<HourDraft>(), Array.Empty<string>(), pursuit is { } id ? new[] { new HourLink("Pursuit", "pursuit", id) } : Array.Empty<HourLink>())
        { Cards = new long[] { 7 } };
    private static DirectorHourContent Content(params HourMove[] moves) => new("2026-09-01", "BD", "Hour", null,
        Array.Empty<HourPlanLine>(), null, moves, Array.Empty<string>(), Array.Empty<string>(), null, null);
    private static DoctrineCardRecord Record(int count, string? result, bool pursuit = false,
        (long EngagementId, int Stage, DateTimeOffset EnteredAtUtc)[]? history = null) =>
        Assert.Single(DoctrineTrackRecord.Compute(
            new[] { (1L, Day, Content(Enumerable.Range(1, count).Select(n => Move(n, pursuit ? 10L : null)).ToArray())) },
            result is null ? Array.Empty<(long, int, string)>() : Enumerable.Range(1, count).Select(n => (1L, n, result!)).ToArray(),
            history ?? Array.Empty<(long, int, DateTimeOffset)>(), new long[] { 7 }));

    [Fact]
    public void ThreeSuggestions_Done_NotDoing_AndUndecided_AreCountedOnceEach()
    {
        var records = DoctrineTrackRecord.Compute(new[] { (1L, Day, Content(Move(1), Move(2), Move(3))) },
            new[] { (1L, 1, "Done"), (1L, 2, "NotDoing") }, Array.Empty<(long, int, DateTimeOffset)>(), new long[] { 7, 8 });
        var r = records[0];
        Assert.Equal((3, 1, 0, 1, 1), (r.Suggested, r.Done, r.NotToday, r.NotDoing, r.Undecided));
        Assert.Equal(Day, r.LastSuggested);
        Assert.Equal("Not suggested yet", records[1].Summary);
        Assert.Null(records[1].LastSuggested);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(10, 1)]
    [InlineData(30, 1)]
    [InlineData(31, 0)]
    [InlineData(40, 0)]
    public void Advancement_UsesTheNextThirtyCalendarDates(int days, int expected)
    {
        var r = Record(1, "Done", true, new[] { (10L, 1, Start.AddDays(-2)), (10L, 3, Start.AddDays(days)) });
        Assert.Equal(expected, r.PursuitsAdvanced);
    }

    [Fact]
    public void WinIsAnAdvance_LossAndArchiveAreSeparate_AndRepeatedLinksDoNotMultiplyPursuits()
    {
        var move = Move(1, 10) with { Links = new[] { new HourLink("a", "pursuit", 10), new HourLink("a again", "pursuit", 10),
            new HourLink("b", "pursuit", 11), new HourLink("c", "pursuit", 12), new HourLink("not a pursuit", "org", 13) } };
        var r = Assert.Single(DoctrineTrackRecord.Compute(new[] { (1L, Day, Content(move, move with { Rank = 2 })) },
            Array.Empty<(long, int, string)>(), new[] { (10L, 3, Start.AddDays(10)), (10L, 6, Start.AddDays(20)),
                (11L, 7, Start.AddDays(10)), (12L, 8, Start.AddDays(10)), (13L, 6, Start.AddDays(10)) }, new long[] { 7 }));
        Assert.Equal((1, 1, 1, 1), (r.PursuitsAdvanced, r.PursuitsWon, r.PursuitsLost, r.PursuitsArchived));
        Assert.Equal(2, r.Undecided);
    }

    [Fact]
    public void ReenteringTheStartingStageOrAnEarlierStageDoesNotAdvance()
    {
        Assert.Equal(0, Record(1, "Done", true, new[] { (10L, 3, Start.AddDays(-1)), (10L, 1, Start.AddDays(2)),
            (10L, 3, Start.AddDays(5)) }).PursuitsAdvanced);
        Assert.Equal(1, Record(1, null, true, new[] { (10L, 3, Start.AddDays(10)) }).PursuitsAdvanced);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void Skipped_FiresAtExactlyThree(int count, bool expected) =>
        Assert.Equal(expected, Record(count, "NotDoing").Flags.Contains("Skipped"));

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public void Idle_FiresAtExactlyFive(int count, bool expected) =>
        Assert.Equal(expected, Record(count, "NotToday").Flags.Contains("Idle"));

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public void NoEffect_FiresAtExactlyFourDonePursuitMoves(int count, bool expected) =>
        Assert.Equal(expected, Record(count, "Done", true).Flags.Contains("NoEffect"));

    [Fact]
    public void NoEffect_RequiresDonePursuitMoves_AndAnyAdvanceOnOneOfThemClearsIt()
    {
        Assert.DoesNotContain("NoEffect", Record(4, "Done").Flags);
        Assert.DoesNotContain("NoEffect", Record(4, "NotDoing", true).Flags);
        Assert.DoesNotContain("NoEffect", Record(4, "Done", true, new[] { (10L, 3, Start.AddDays(10)) }).Flags);
        Assert.Contains("NoEffect", Record(4, "Done", true, new[] { (10L, 7, Start.AddDays(10)) }).Flags);
        Assert.Contains("NoEffect", Record(4, "Done", true, new[] { (10L, 8, Start.AddDays(10)) }).Flags);
    }

    [Fact]
    public void DoneClearsIdle_AndAnAdvanceOnAnUndecidedMoveDoesNotClearNoEffect()
    {
        var content = Content(Move(1, 10), Move(2, 10), Move(3, 10), Move(4, 10), Move(5, 11));
        var results = Enumerable.Range(1, 4).Select(rank => (1L, rank, "Done")).ToArray();
        var r = Assert.Single(DoctrineTrackRecord.Compute(new[] { (1L, Day, content) }, results,
            new[] { (11L, 3, Start.AddDays(10)) }, new long[] { 7 }));
        Assert.DoesNotContain("Idle", r.Flags);
        Assert.Contains("NoEffect", r.Flags);
        Assert.Equal(1, r.PursuitsAdvanced);
    }

    [Fact]
    public void LatestThreeSuggestions_IncludeUndecided_AndUseDateBeforeHourId()
    {
        var hours = new[] { (99L, Day.AddDays(-1), Content(Move(1))), (1L, Day, Content(Move(1), Move(2), Move(3))) };
        var results = new[] { (99L, 1, "Done"), (1L, 1, "NotDoing"), (1L, 2, "NotDoing"), (1L, 3, "NotDoing") };
        var r = Assert.Single(DoctrineTrackRecord.Compute(hours, results, Array.Empty<(long, int, DateTimeOffset)>(), new long[] { 7 }));
        Assert.Contains("Skipped", r.Flags);
        Assert.DoesNotContain("Idle", r.Flags);
        r = Assert.Single(DoctrineTrackRecord.Compute(hours, results.Take(3).ToArray(), Array.Empty<(long, int, DateTimeOffset)>(), new long[] { 7 }));
        Assert.DoesNotContain("Skipped", r.Flags);
    }

    [Fact]
    public void OldJsonWithoutCards_StillParses_WithAnEmptyList()
    {
        const string json = """
        { "forDate":"2026-09-01", "kind":"BD", "title":"Old hour", "plan":[],
          "moves":[{"rank":1,"title":"Call","minutes":15,"whyNow":["Due"]}] }
        """;
        var (content, errors) = DirectorHourFile.Parse(json);
        Assert.Empty(errors);
        Assert.Empty(Assert.Single(content!.Moves).Cards);
        Assert.Empty(new HourMove(1, "Call", 15, new[] { "Due" }, null, Array.Empty<HourDraft>(), Array.Empty<string>(), Array.Empty<HourLink>()).Cards);
    }

    [Theory]
    [InlineData(0, 7)]
    [InlineData(-1, 7)]
    [InlineData(7, 7)]
    public void CardIds_MustBePositiveAndUnique(long first, long second)
    {
        var json = JsonSerializer.Serialize(Content(Move(1) with { Cards = new[] { first, second } }), DirectorHourFile.JsonOptions);
        var (content, errors) = DirectorHourFile.Parse(json);
        Assert.Null(content);
        Assert.Contains(errors, e => e.Contains("move 1: cards must contain positive, unique card ids.", StringComparison.Ordinal));
    }
}

/// <summary>
/// Opt-in store gates against a migrated disposable database supplied explicitly in KOR_DOCTRINE_TEST_DB.
/// Never falls back to the app connection. Covers retirement's status guard and archive/reopen persistence.
/// Does not cover concurrent UI edits; a missing required-reason dialog would not be caught by these store tests.
/// </summary>
public sealed class DoctrineTrackRecordStoreTests
{
    [DoctrineDatabaseFact]
    public async Task Retire_AcceptsOnlyAcceptedCards_AndRecordsIan()
    {
        var cs = Environment.GetEnvironmentVariable("KOR_DOCTRINE_TEST_DB")!;
        var store = new SqlBdDoctrineStore(cs);
        var (token, sha) = BdDoctrine.NewToken();
        var run = await store.StartRunAsync("synthesis", "BD", "Doctrine store gate", null, "doctrine-test", new byte[32], sha, CancellationToken.None);
        try
        {
            DoctrineCardDraft Draft(string title) => new(title, title, "any", "Principle", "Recognise", "Play", null, "Fixture", null, null);
            Assert.True(await store.ReportAsync(run, token, new BdPromptReport("answered", "Fixture", new[]
                { Draft("Accept"), Draft("Propose"), Draft("Reject") }), CancellationToken.None));
            var cards = (await store.ListCardsAsync("BD", CancellationToken.None)).Where(c => c.SourceRunId == run).ToDictionary(c => c.Title);
            Assert.True(await store.DecideCardAsync(cards["Accept"].Id, true, "fixture", CancellationToken.None));
            Assert.True(await store.DecideCardAsync(cards["Reject"].Id, false, "fixture", CancellationToken.None));
            Assert.True(await store.RetireCardAsync(cards["Accept"].Id, "Ian", CancellationToken.None));
            Assert.False(await store.RetireCardAsync(cards["Accept"].Id, "Ian", CancellationToken.None));
            Assert.False(await store.RetireCardAsync(cards["Propose"].Id, "Ian", CancellationToken.None));
            Assert.False(await store.RetireCardAsync(cards["Reject"].Id, "Ian", CancellationToken.None));
            cards = (await store.ListCardsAsync("BD", CancellationToken.None)).Where(c => c.SourceRunId == run).ToDictionary(c => c.Title);
            Assert.Equal("retired", cards["Accept"].Status);
            Assert.Equal("Ian", cards["Accept"].DecidedBy);
            Assert.NotNull(cards["Accept"].DecidedAtUtc);
            Assert.Equal("proposed", cards["Propose"].Status);
            Assert.Null(cards["Propose"].DecidedBy);
            Assert.Equal("rejected", cards["Reject"].Status);
            Assert.Equal("fixture", cards["Reject"].DecidedBy);
        }
        finally
        {
            await Cleanup(cs, "DELETE FROM opportunities.DoctrineCard WHERE SourceRunId = @id; DELETE FROM opportunities.BdPromptRun WHERE Id = @id;", run);
        }
    }

    [DoctrineDatabaseFact]
    public async Task Archived_KeepsReasonAndCloseDate_AndReopeningClearsBoth()
    {
        var cs = Environment.GetEnvironmentVariable("KOR_DOCTRINE_TEST_DB")!;
        var store = new SqlCrmEngagementStore(cs);
        var record = await store.InsertAsync(new CrmEngagement { Notes = "Doctrine store gate", OwnerStaffId = "doctrine-test" }, "doctrine-test", CancellationToken.None);
        try
        {
            record = await store.UpdateAsync(record with { Stage = CrmEngagementStage.Archived, OutcomeReason = "Buyer cancelled the scope" }, "Ian", CancellationToken.None);
            var read = await store.GetByIdAsync(record.Id, CancellationToken.None);
            Assert.Equal("Buyer cancelled the scope", read!.OutcomeReason);
            Assert.NotNull(read.ClosedAtUtc);
            var closedAt = read.ClosedAtUtc;
            record = await store.UpdateAsync(read with { Notes = "Still archived" }, "Ian", CancellationToken.None);
            Assert.Equal(closedAt, record.ClosedAtUtc);
            var closed = await new SqlBdDoctrineStore(cs).ListClosedPursuitsAsync(DateTimeOffset.UtcNow.AddDays(-90), DateTimeOffset.UtcNow, CancellationToken.None);
            Assert.Contains(closed, p => p.EngagementId == record.Id && p.Stage == 8 && p.Reason == "Buyer cancelled the scope");
            record = await store.UpdateAsync(record with { Stage = CrmEngagementStage.Drafting }, "Ian", CancellationToken.None);
            Assert.Null(record.OutcomeReason);
            Assert.Null(record.ClosedAtUtc);
        }
        finally
        {
            await Cleanup(cs, "DELETE FROM opportunities.CrmEngagementStageHistory WHERE EngagementId = @id; DELETE FROM opportunities.CrmEngagements WHERE Id = @id;", record.Id);
        }
    }

    private static async Task Cleanup(string cs, string sql, long id)
    {
        await using var con = new SqlConnection(cs);
        await con.OpenAsync();
        await using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        await cmd.ExecuteNonQueryAsync();
    }
}

public sealed class DoctrineDatabaseFactAttribute : FactAttribute
{
    public DoctrineDatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOR_DOCTRINE_TEST_DB")))
            Skip = "Set KOR_DOCTRINE_TEST_DB to a migrated disposable test database to run this store gate.";
    }
}
