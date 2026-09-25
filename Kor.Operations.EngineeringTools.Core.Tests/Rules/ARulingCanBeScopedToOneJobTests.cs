#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Rules;

/// <summary>
/// An answer about ONE BUILDING is banked against that building, wins over the office convention on
/// that job only, and is offered as evidence — never silently applied — on every other.
/// </summary>
/// <remarks>
/// WHY THIS EXISTS. Ian, 2026-09-25: "the 'learning system' doesn't seem to be doing any learning."
/// Measured that hour: 88 rulings banked, 0 scoped to a job, and 4 of the 88 ever arrived through
/// the import path that an answered workbook takes — all four on 2026-08-15. The questions that
/// would close the corpus gap are the ones that can only be answered per job, and they had been
/// given no setting key precisely because the store could not hold a per-job answer.
///
/// This is the round trip, against the live KorStandards, with its own row cleaned up afterwards.
/// It is the only test that proves the loop rather than one of its halves.
///
/// WHAT THIS COVERS: that a job-scoped ruling is written by the same code the import uses; that
/// <see cref="RuleSettings.LoadForJob"/> returns it for its own job and NOT for another; that it
/// beats a convention carrying the same key; that its attribution — who, which job, what date —
/// survives into the setting the build reads; and that
/// <see cref="RuleSettings.PriorAnswers"/> surfaces it to a different job as evidence.
///
/// WHAT IT DOES NOT COVER:
/// <list type="bullet">
/// <item>That any geometry changes. The setting reaches the build; whether the build then draws a
/// different floor is a differential on a real set, not this.</item>
/// <item>The workbook end — that the engineer's typed answer becomes this row. That is
/// <c>RuleSettings.ImportQuestionAnswers</c> and its own tests.</item>
/// <item>Retirement and supersession. A second answer on the same job and key wins by date here;
/// nothing tests what should happen when she changes her mind twice in one day.</item>
/// </list>
///
/// A same-class fault this would NOT catch: a question whose RuleScope is left at the office
/// default, so her per-job answer banks globally and quietly changes every other job. That is what
/// <see cref="EveryQuestionsAnswerHasSomewhereToLandTests"/> and the questionnaire's own scope test
/// are for.
/// </remarks>
[Trait("Speed", "Slow")]
public sealed class ARulingCanBeScopedToOneJobTests : IDisposable
{
    // A job number no drawing set has. Five digits keeps it the shape of a real one, and 99999
    // is outside the range KOR has issued, so a stray row cannot reach a real build.
    private const string ThisJob = "99999-01";
    private const string AnotherJob = "99998-01";
    private const string Key = "dxf.min-wall-length";
    private const string Topic = "a-job-scope-round-trip-written-by-its-own-test";

    private readonly string _connection =
        Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable) ?? string.Empty;

    public ARulingCanBeScopedToOneJobTests() => RemoveTestRulings();

    public void Dispose() => RemoveTestRulings();

    [Fact]
    public void AJobsOwnAnswerBeatsTheOfficeConventionOnThatJobAndNoOther()
    {
        Assert.False(string.IsNullOrWhiteSpace(_connection),
            $"{RuleSettings.ConnectionEnvironmentVariable} is not set; the loop reads and writes KorStandards and never skips.");

        var office = RuleSettings.Load(_connection);
        Assert.True(office.TryGetValue(Key, out var banked),
            $"{Key} is not banked as a convention, so this test cannot show a job answer overriding one.");

        // A value no convention would carry, so "the job won" cannot be confused with "the default
        // happened to match".
        double hers = banked!.Value + 7;
        Bank(ThisJob, hers, $"On {ThisJob} the short returns are real walls, so read down to {hers} in.");

        var forThisJob = RuleSettings.LoadForJob(_connection, ThisJob);
        Assert.Equal(hers, forThisJob[Key].Value);

        var forAnother = RuleSettings.LoadForJob(_connection, AnotherJob);
        Assert.Equal(banked.Value, forAnother[Key].Value);

        var unscoped = RuleSettings.Load(_connection);
        Assert.Equal(banked.Value, unscoped[Key].Value);
    }

    [Fact]
    public void TheAnswerCarriesWhoSettledItAndWhen()
    {
        Assert.False(string.IsNullOrWhiteSpace(_connection),
            $"{RuleSettings.ConnectionEnvironmentVariable} is not set; the loop reads and writes KorStandards and never skips.");

        Bank(ThisJob, 42, "The parkade is this building's share only.");

        var setting = RuleSettings.LoadForJob(_connection, ThisJob)[Key];

        // Ian, 2026-09-24: "Could even attribute it in the design (built on the assumption made by
        // x on xyz date)." Because is where every existing reader of a setting already looks.
        Assert.Contains("andrean", setting.Authority, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ThisJob, setting.Because, StringComparison.Ordinal);
        Assert.Contains(DateTime.UtcNow.ToString("yyyy-MM"), setting.Because, StringComparison.Ordinal);
        Assert.Contains("parkade", setting.Because, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnotherJobSeesItAsEvidenceRatherThanAsARule()
    {
        Assert.False(string.IsNullOrWhiteSpace(_connection),
            $"{RuleSettings.ConnectionEnvironmentVariable} is not set; the loop reads and writes KorStandards and never skips.");

        Bank(ThisJob, 42, "The parkade is this building's share only.");

        var prior = RuleSettings.PriorAnswers(_connection, exceptJob: AnotherJob);
        var mine = prior.Where(p => p.Topic == Topic).ToList();

        Assert.True(mine.Count == 1, $"expected the one banked answer to show as prior art on another job; got {mine.Count}.");
        Assert.Equal(ThisJob, mine[0].Job);
        Assert.Contains(ThisJob, mine[0].Attribution, StringComparison.Ordinal);
        Assert.Contains("parkade", mine[0].Attribution, StringComparison.OrdinalIgnoreCase);

        // Its own job must not be shown its own answer as somebody else's precedent.
        var onItsOwnJob = RuleSettings.PriorAnswers(_connection, exceptJob: ThisJob);
        Assert.DoesNotContain(onItsOwnJob, p => p.Topic == Topic);
    }

    private void Bank(string job, double value, string ruling)
    {
        using var connection = new SqlConnection(_connection);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO analysis.Ruling (Id, Engineer, Scope, Topic, Ruling, ActionType, Confidence, " +
            "                             RuledOn, CreatedBy, CreatedAtUtc, SettingKey, SettingValue, SettingUnits) " +
            "VALUES (NEWID(), 'andrean', @scope, @topic, @ruling, 'APPLY', 'engineer-confirmed', " +
            "        SYSUTCDATETIME(), 'job-scope round trip test', SYSUTCDATETIME(), @key, @value, 'in')";
        command.Parameters.AddWithValue("@scope", RuleSettings.JobScope(job));
        command.Parameters.AddWithValue("@topic", Topic);
        command.Parameters.AddWithValue("@ruling", ruling);
        command.Parameters.AddWithValue("@key", Key);
        command.Parameters.AddWithValue("@value", value);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Deletes rather than retires: a retired row still sits in the table a coverage audit counts,
    /// and a test's own scaffolding has no business being audited. The topic is unique to this
    /// file, so nothing else can match.
    /// </summary>
    private void RemoveTestRulings()
    {
        if (string.IsNullOrWhiteSpace(_connection)) return;

        try
        {
            using var connection = new SqlConnection(_connection);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM analysis.Ruling WHERE Topic = @topic";
            command.Parameters.AddWithValue("@topic", Topic);
            command.ExecuteNonQuery();
        }
        catch
        {
            // A cleanup that cannot reach the database must not turn into a second failure on top
            // of the one that is already going to be reported.
        }
    }
}
