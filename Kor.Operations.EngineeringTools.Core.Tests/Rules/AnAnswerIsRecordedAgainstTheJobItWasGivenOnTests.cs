#nullable enable
using ClosedXML.Excel;
using Kor.Operations.EngineeringTools.Dxf;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Rules;

/// <summary>
/// An engineer answers a question on one building; the rule goes to the office, the record that she
/// settled it HERE goes to the job, and the next building is told who decided it and when.
/// </summary>
/// <remarks>
/// WHY THIS EXISTS. Ian, 2026-09-25, on what a learning system is: "It's 'Oh, I've seen that rule on
/// another project, but it's applicable to this project as well - apply' - not - 'Oh, let's create
/// the same rule for another specific project'. That's not a wise, learning system."
///
/// Until migration 101 there was nowhere to put the second half of that. The rule was one row and
/// always had been — <c>UX_Ruling_Setting</c> enforces one live rule per key, correctly — but
/// nothing recorded WHERE it had been found to apply, so a rule could not be offered to a new job
/// with its provenance, and a rejection on one job could not be remembered at all. Measured the same
/// hour: 4 of 88 rulings had ever come through this import path, all on 2026-08-15, and
/// <c>analysis.RulingEvidence.JobNumber</c> had been in the schema since the table was made without
/// a single row ever filling it.
///
/// WHAT THIS COVERS: that a real workbook written by <see cref="ModelQuestionnaire.Write"/> and read
/// by <see cref="RuleSettings.ImportQuestionAnswers"/> banks the rule, records the application
/// against the job named in the workbook, and stamps the evidence with that job; that another job is
/// then offered the rule with who/where/when attached; that a job's own confirmed rule is NOT
/// offered back to it as somebody else's precedent; and that a REJECTION on a job withholds the
/// setting on that job and stops it being offered again.
///
/// WHAT IT DOES NOT COVER:
/// <list type="bullet">
/// <item>That withholding reaches the geometry. <see cref="RuleSettings.WithheldFor"/> returns the
/// key; whether the build then drops it is the loader's job and is not asserted here.</item>
/// <item>Automatic carrying. A rule confirmed elsewhere is OFFERED; nothing yet applies it to a new
/// job unasked, which is deliberate until the workbook shows the offer.</item>
/// <item>Two engineers disagreeing on the same job. One standing decision per rule per job replaces
/// the earlier one, and which of them should win is not decided here.</item>
/// </list>
///
/// A same-class fault this would NOT catch: the workbook writing a job cell the importer reads by a
/// different name. Both ends use <see cref="ModelQuestionnaire.JobCellPrefix"/>, so they move
/// together — but a hand-built workbook in a test would have hidden exactly that, which is why this
/// uses the real writer rather than a sheet of its own making.
/// </remarks>
[Trait("Speed", "Slow")]
[Collection(SheetNamingVocabularyCollection.Name)]
public sealed class AnAnswerIsRecordedAgainstTheJobItWasGivenOnTests : IDisposable
{
    private const string HerJob = "99997-01";
    private const string AnotherJob = "99996-01";

    // The question she answers. C4 is a standing rule — how stocky a pier may be — with ONE setting
    // key the build reads, so this exercises a row that matters rather than a spare one.
    //
    // It was W5 first, which carries TWO keys (dxf.min-wall-thickness;dxf.unusual-wall-thickness),
    // and a single number against a two-key row is skipped — correctly. The verb prints the reason;
    // this test threw away RuleImportResult.Skipped and so reported only "no answer the importer
    // could read", which cost a round trip to find out why.
    private const string Code = "C4";

    private readonly string _connection =
        Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable) ?? string.Empty;

    private readonly List<string> _temporary = [];

    public AnAnswerIsRecordedAgainstTheJobItWasGivenOnTests() => Clean();

    public void Dispose()
    {
        Clean();
        // KOR_KEEP_ANSWER_WORKBOOKS=1 leaves them on disk. A workbook this test wrote is the only
        // evidence of what the importer was actually handed, and deleting it is why the first
        // failure here had to be guessed at.
        if (Environment.GetEnvironmentVariable("KOR_KEEP_ANSWER_WORKBOOKS") == "1") return;
        foreach (string f in _temporary) if (File.Exists(f)) File.Delete(f);
    }

    [Fact]
    public void TheRuleGoesToTheOfficeAndTheRecordOfWhoSettledItGoesToTheJob()
    {
        Assert.False(string.IsNullOrWhiteSpace(_connection),
            $"{RuleSettings.ConnectionEnvironmentVariable} is not set; the loop reads and writes KorStandards and never skips.");

        var import = RuleSettings.ImportQuestionAnswers(AnsweredWorkbookFor(HerJob), "andrean", _connection);

        Assert.Equal(HerJob, import.Job);
        Assert.True(import.AnswersFound >= 1,
            "the workbook this test wrote carried no answer the importer could read. It said: "
            + (import.Skipped.Count > 0 ? string.Join("; ", import.Skipped) : "nothing was skipped either."));
        Assert.True(import.ApplicationsWritten >= 1, "the answer banked, but nothing recorded which building it was settled on.");

        // The evidence carries the building. The column has existed since the table was made and
        // every row banked before today left it null.
        Assert.Equal(HerJob, ScalarString(
            "SELECT TOP 1 e.JobNumber FROM analysis.RulingEvidence e " +
            "JOIN analysis.Ruling r ON r.Id = e.RulingId WHERE r.Topic = @topic ORDER BY e.ObservedAtUtc DESC"));

        Assert.Equal("confirmed", ScalarString(
            "SELECT TOP 1 a.Decision FROM analysis.RuleApplication a " +
            "JOIN analysis.Ruling r ON r.Id = a.RulingId WHERE r.Topic = @topic AND a.Job = '" + HerJob + "'"));
    }

    [Fact]
    public void AnotherBuildingIsOfferedItWithWhoSettledItAndWhen()
    {
        Assert.False(string.IsNullOrWhiteSpace(_connection),
            $"{RuleSettings.ConnectionEnvironmentVariable} is not set; the loop reads and writes KorStandards and never skips.");

        RuleSettings.ImportQuestionAnswers(AnsweredWorkbookFor(HerJob), "andrean", _connection);

        var carried = RuleSettings.CarriedRules(_connection, AnotherJob);
        var mine = Assert.Single(carried, c => c.Topic == TopicOfTheAnsweredRule());

        Assert.Equal(HerJob, mine.Job);
        Assert.Equal("andrean", mine.Engineer);
        Assert.Contains(HerJob, mine.Attribution, StringComparison.Ordinal);
        Assert.Contains("andrean", mine.Attribution, StringComparison.Ordinal);
        Assert.Contains(DateTime.UtcNow.ToString("yyyy-MM"), mine.Attribution, StringComparison.Ordinal);

        // A job is not shown its own decision as somebody else's precedent. It has stopped being a
        // question there.
        Assert.DoesNotContain(RuleSettings.CarriedRules(_connection, HerJob), c => c.Topic == TopicOfTheAnsweredRule());
    }

    [Fact]
    public void ARejectionIsRememberedSoSheIsNotAskedToGiveItAgain()
    {
        Assert.False(string.IsNullOrWhiteSpace(_connection),
            $"{RuleSettings.ConnectionEnvironmentVariable} is not set; the loop reads and writes KorStandards and never skips.");

        RuleSettings.ImportQuestionAnswers(AnsweredWorkbookFor(HerJob), "andrean", _connection);

        string? key = ScalarString("SELECT TOP 1 SettingKey FROM analysis.Ruling WHERE Topic = @topic");
        Assert.False(string.IsNullOrWhiteSpace(key), "the answered rule carries no setting key, so nothing could be withheld.");

        Assert.DoesNotContain(key!, RuleSettings.WithheldFor(_connection, AnotherJob));

        Reject(AnotherJob);

        Assert.Contains(key!, RuleSettings.WithheldFor(_connection, AnotherJob));

        // And it is no longer offered there. A rule she has refused must not come back next run
        // wearing the same provenance it came with the first time.
        Assert.DoesNotContain(RuleSettings.CarriedRules(_connection, AnotherJob), c => c.Topic == TopicOfTheAnsweredRule());

        // The job where she confirmed it is untouched.
        Assert.DoesNotContain(key!, RuleSettings.WithheldFor(_connection, HerJob));
    }

    /// <summary>
    /// A real questions workbook for <paramref name="job"/> with one cell filled in, written by the
    /// same code that writes the one an engineer opens.
    /// </summary>
    private string AnsweredWorkbookFor(string job)
    {
        string path = Path.Combine(Path.GetTempPath(), $"kor-answers-{Guid.NewGuid():N}.xlsx");
        _temporary.Add(path);

        var report = new DxfToEtabsReport(
            path, 1, 1, 1,
            new ComposeSummary(1, 1, 1, 4, 1, Array.Empty<string>(), Array.Empty<string>()),
            (0, 0),
            Array.Empty<SheetOutcome>(),
            Array.Empty<string>(),
            new PlanClassificationOptions(),
            new ComposeOptions { SpandrelDepthFloor = 18, SpandrelDepthCeiling = 60 });

        ModelQuestionnaire.Write(path, report, report.ClassificationUsed, report.ComposeUsed, job);

        using (var workbook = new XLWorkbook(path))
        {
            // C4 is a DECIDED row, so since 2026-09-25 it lives on ‘Settled questions’ rather than
            // on the page she opens. Answering it there MUST still import — that tab exists so a
            // decision the tool took can be overruled, and a tab whose answers are silently
            // discarded is worse than no tab.
            var sheet = workbook.Worksheets
                .FirstOrDefault(w => w.Name.Equals(ModelQuestionnaire.SettledSheetName, StringComparison.OrdinalIgnoreCase))
                ?? workbook.Worksheet("Questions");

            int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 4;

            int row = Enumerable.Range(5, lastRow - 4)
                .FirstOrDefault(r => sheet.Cell(r, 1).GetString().Trim().Equals(Code, StringComparison.OrdinalIgnoreCase));

            Assert.True(row >= 5,
                $"question {Code} is on neither '{ModelQuestionnaire.SettledSheetName}' nor 'Questions'; pick another.");
            Assert.Equal(ModelQuestionnaire.SettledSheetName, sheet.Name);

            // Her answer, and a topic nothing else in the store uses so the cleanup below cannot
            // reach a real ruling.
            sheet.Cell(row, 5).Value = "50";
            sheet.Cell(row, 8).Value = TopicOfTheAnsweredRule();
            workbook.Save();
        }

        return path;
    }

    private static string TopicOfTheAnsweredRule() => "a-thickness-answered-by-its-own-test";

    private void Reject(string job)
    {
        using var connection = new SqlConnection(_connection);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO analysis.RuleApplication (RulingId, Job, Decision, DecidedBy, Because) " +
            "SELECT Id, @job, 'rejected', 'andrean', 'Not on this building.' " +
            "FROM analysis.Ruling WHERE Topic = @topic";
        command.Parameters.AddWithValue("@job", job);
        command.Parameters.AddWithValue("@topic", TopicOfTheAnsweredRule());
        command.ExecuteNonQuery();
    }

    private string? ScalarString(string sql)
    {
        using var connection = new SqlConnection(_connection);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@topic", TopicOfTheAnsweredRule());
        object? value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    /// <summary>
    /// Removes this test's own rows, children first, AND PUTS BACK THE OFFICE CONVENTION IT
    /// DISPLACED. The topic is unique to this file, so nothing a person banked can match it.
    /// </summary>
    /// <remarks>
    /// ⚠ THE SECOND HALF IS NOT OPTIONAL, and leaving it out did real damage on 2026-09-25.
    /// <c>UpsertRuling</c> retires the FormatConvention carrying the same setting key — correctly,
    /// because an engineer's ruling supersedes a mined convention. Deleting only the ruling
    /// afterwards leaves the key with NO live row in either table: the store went from 99 settings
    /// to 98, <c>dxf.max-pier-thickness</c> vanished, and four tests that had nothing to do with
    /// this one went red, including both projects' pier-label check.
    ///
    /// Any test that imports an answer takes on this obligation. The ruling is one row and easy to
    /// see; the convention it silently stood down is the one that gets left behind.
    /// </remarks>
    private void Clean()
    {
        if (string.IsNullOrWhiteSpace(_connection)) return;
        try
        {
            using var connection = new SqlConnection(_connection);
            connection.Open();

            // The keys FIRST: after the delete there is nothing left to read them from.
            var keys = new List<string>();
            using (var read = connection.CreateCommand())
            {
                read.CommandText = "SELECT SettingKey FROM analysis.Ruling WHERE Topic = @topic AND SettingKey IS NOT NULL";
                read.Parameters.AddWithValue("@topic", TopicOfTheAnsweredRule());
                using var reader = read.ExecuteReader();
                while (reader.Read()) keys.Add(reader.GetString(0));
            }

            foreach (string sql in new[]
                     {
                         "DELETE a FROM analysis.RuleApplication a JOIN analysis.Ruling r ON r.Id = a.RulingId WHERE r.Topic = @topic",
                         "DELETE e FROM analysis.RulingEvidence e JOIN analysis.Ruling r ON r.Id = e.RulingId WHERE r.Topic = @topic",
                         "DELETE h FROM analysis.RulingHistory h JOIN analysis.Ruling r ON r.Id = h.RulingId WHERE r.Topic = @topic",
                         "DELETE FROM analysis.Ruling WHERE Topic = @topic",
                     })
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("@topic", TopicOfTheAnsweredRule());
                command.ExecuteNonQuery();
            }

            foreach (string key in keys)
            {
                using var restore = connection.CreateCommand();
                restore.CommandText =
                    "UPDATE analysis.FormatConvention " +
                    "   SET RetiredAtUtc = NULL, RetiredReason = NULL, UpdatedAtUtc = SYSUTCDATETIME() " +
                    " WHERE SettingKey = @key AND RetiredAtUtc IS NOT NULL " +
                    "   AND RetiredReason LIKE 'Superseded by an engineer questionnaire ruling%'";
                restore.Parameters.AddWithValue("@key", key);
                restore.ExecuteNonQuery();
            }
        }
        catch
        {
            // A cleanup that cannot reach the database must not become a second failure on top of
            // the one already being reported.
        }
    }
}
