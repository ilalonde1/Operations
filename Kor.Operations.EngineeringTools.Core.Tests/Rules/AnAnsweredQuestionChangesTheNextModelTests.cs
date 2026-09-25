#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Rules;

/// <summary>
/// The loop, end to end: the engineer answers a question, the answer is banked, and the NEXT model
/// of that job is different because of it.
/// </summary>
/// <remarks>
/// WHY THIS EXISTS. Ian, 2026-09-25: "the 'learning system' doesn't seem to be doing any learning.
/// Or anything really. Because you keep skipping them." Measured that hour, against the live store:
/// 88 rulings banked, 4 of them ever through the import path an answered workbook takes, all four on
/// 2026-08-15, and 20 of the 41 question codes carrying no setting key at all — so an answer to one
/// was written into a table and the next run behaved identically.
///
/// Nothing tested the loop. There were tests of the import, tests of the loader and tests of the
/// questionnaire, and each passed while the three of them together did nothing. This is the test
/// that would have failed on 2026-08-16 and every day since.
///
/// THE SHAPE IS A DIFFERENTIAL, per the repo's rule 11: the same drawings built twice, differing in
/// exactly one banked answer, with everything else held equal. It builds under a job number no
/// other test uses — the answer's key carries the job, so nothing else in the suite can see it even
/// while xUnit runs these classes in parallel.
///
/// WHAT THIS COVERS: that an answer banked as a ruling reaches the build; that it MOVES A DRAWING
/// to the storey she named; that it outranks the tool's own reading of the title; that the model
/// says so in words she can act on; and that with the answer withdrawn the model returns to exactly
/// what it was.
///
/// WHAT IT DOES NOT COVER:
/// <list type="bullet">
/// <item>The workbook end. That her typed cell becomes this row is
/// <c>RuleSettings.ImportQuestionAnswers</c>, tested separately. This banks the row directly, so a
/// break between the spreadsheet and the table passes here.</item>
/// <item>The other 13 questions that still have no landing place. This proves the mechanism on one,
/// and <see cref="EveryQuestionsAnswerHasSomewhereToLandTests"/> counts the rest.</item>
/// <item>Whether her answer is RIGHT. A wrong level is applied exactly as obediently as a right
/// one; that is the point of it being her answer.</item>
/// </list>
///
/// A same-class fault this would NOT catch: an answer that reaches the build and is then overwritten
/// further down the pipeline — the storey shift, the building cut, the stack merge all run after
/// this and each could undo it. This asserts the sheet's own level, not the final placement.
/// </remarks>
[Trait("Speed", "Slow")]
[Collection(SheetNamingVocabularyCollection.Name)]
public sealed class AnAnsweredQuestionChangesTheNextModelTests : IDisposable
{
    // A job nothing else builds. The answer's key carries it, so no other test in this suite —
    // including the six banked baselines, which build these same drawings — can see the row.
    private const string ThisJob = "99999-01";
    private const string Key = "dxf.sheet-levels";
    private const string Topic = "a-level-answered-by-its-own-test";

    private readonly string _connection =
        Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable) ?? string.Empty;

    public AnAnsweredQuestionChangesTheNextModelTests() => Withdraw();

    public void Dispose() => Withdraw();

    [Fact]
    public void HerAnswerMovesTheDrawingAndWithdrawingItPutsItBack()
    {
        Assert.False(string.IsNullOrWhiteSpace(_connection),
            $"{RuleSettings.ConnectionEnvironmentVariable} is not set; the loop reads and writes KorStandards and never skips.");
        if (!LiveProjects.ShareReachable) return;

        var project = GeneratedModel.WestFirst;
        string drawings = DrawingCache.Local(project.DxfFolder);

        // THE DRAWING SHE WILL MOVE, chosen from the set rather than named here, so this does not
        // rot the day somebody renames a sheet.
        string? sheet = Directory.EnumerateFiles(drawings, "*.dxf")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(n => PlanSheetNaming.Parse(n!).Levels.Count == 1);

        Assert.NotNull(sheet);

        int wasAt = PlanSheetNaming.Parse(sheet!).Levels.Single();

        // A LEVEL ANOTHER DRAWING IN THIS SET ALREADY USES, so her answer MOVES the sheet onto a
        // storey that exists rather than off the ladder entirely. Sending it to level 47 also
        // proves the mechanism, but it proves it by making structure vanish, and "your answer
        // deleted a floor" is not the behaviour worth locking in.
        int sheAsks = Directory.EnumerateFiles(drawings, "*.dxf")
            .SelectMany(f => PlanSheetNaming.Parse(Path.GetFileNameWithoutExtension(f)).Levels)
            .Distinct()
            .Where(l => l != wasAt)
            .DefaultIfEmpty(wasAt + 40)
            .First();

        var before = Build(drawings, project.Reference);
        Assert.DoesNotContain(before.Report.Warnings, w => w.Contains("YOUR ANSWER APPLIED", StringComparison.Ordinal));

        Bank($"{sheet}={sheAsks}");
        var after = Build(drawings, project.Reference);

        string applied = Assert.Single(after.Report.Warnings, w => w.Contains("YOUR ANSWER APPLIED", StringComparison.Ordinal));
        Assert.Contains(sheet!, applied, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"level {sheAsks}", applied, StringComparison.Ordinal);

        // THE MODEL ITSELF, not a count of it. A count can stay the same while every member moves
        // storey, and the whole point of her answer is where things sit.
        Assert.False(before.Model == after.Model,
            "her answer reached the build and said so, but the .e2k that came out is byte-identical — "
            + "something downstream of the level pass is overwriting it.");

        // AND IT IS HERS, NOT OURS. Withdraw the ruling and the model must come back to EXACTLY
        // what it was, or the difference above was something else moving at the same time.
        Withdraw();
        var withdrawn = Build(drawings, project.Reference);
        Assert.DoesNotContain(withdrawn.Report.Warnings, w => w.Contains("YOUR ANSWER APPLIED", StringComparison.Ordinal));
        Assert.Equal(before.Model, withdrawn.Model);
    }

    private sealed record Run(DxfToEtabsReport Report, string Model);

    private static Run Build(string drawings, string reference)
    {
        string output = Path.Combine(Path.GetTempPath(), $"kor-loop-{Guid.NewGuid():N}.e2k");
        try
        {
            var report = DxfToEtabsService.Run(new DxfToEtabsRequest
            {
                RequireRuleSettings = true,
                DxfFolder = drawings,
                ReferenceE2k = reference,
                OutputE2k = output,
                Job = ThisJob,
            });
            return new Run(report, File.ReadAllText(output));
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    private void Bank(string answer)
    {
        using var connection = new SqlConnection(_connection);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO analysis.Ruling (Id, Engineer, Scope, Topic, Ruling, ActionType, Confidence, " +
            "                             RuledOn, CreatedBy, CreatedAtUtc, SettingKey, SettingValue, SettingUnits) " +
            "VALUES (NEWID(), 'andrean', @scope, @topic, @ruling, 'APPLY', 'engineer-confirmed', " +
            "        SYSUTCDATETIME(), 'end-to-end loop test', SYSUTCDATETIME(), @key, @value, @units)";
        command.Parameters.AddWithValue("@scope", RuleSettings.JobScope(ThisJob));
        command.Parameters.AddWithValue("@topic", Topic);
        command.Parameters.AddWithValue("@ruling", "That drawing is on the level named here.");
        command.Parameters.AddWithValue("@key", RuleSettings.KeyForJob(Key, ThisJob));
        command.Parameters.AddWithValue("@value", answer);
        command.Parameters.AddWithValue("@units", RuleSettings.TextUnits);
        command.ExecuteNonQuery();
    }

    private void Withdraw()
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
            // A cleanup that cannot reach the database must not become a second failure on top of
            // the one already being reported.
        }
    }
}
