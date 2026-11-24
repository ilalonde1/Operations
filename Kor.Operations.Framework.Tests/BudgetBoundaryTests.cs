using System.Text.Json;
using Kor.Operations.Framework.Budget;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Enforce;
using Xunit;

namespace Kor.Operations.Framework.Tests;

public sealed class BudgetBoundaryTests
{
    [Theory]
    [InlineData("UPDATE TOP (1) oversight.Commitment SET Budget='2 runs' WHERE Id=2248")]
    [InlineData("UPDATE oversight.Commitment WITH (ROWLOCK) SET Budget='2 runs' WHERE Id=2248")]
    [InlineData("WITH c AS (SELECT * FROM oversight.Commitment WHERE Id=2248) UPDATE c SET Budget='2 runs'")]
    [InlineData("UPDATE oversight.Commitment SET @b = Budget = '2 runs' WHERE Id=2248")]
    [InlineData("UPDATE oversight.Commitment SET State=(SELECT State FROM x), Budget='2 runs' WHERE Id=2248")]
    public void LessSimpleUpdatesStillNeedApproval(string sql) =>
        Assert.NotNull(SqlWriteCutGuard.DecideBudget("Bash", "sqlcmd -Q \"" + sql + "\"", null, _ => false));

    [Theory]
    [InlineData("UPDATE c SET Budget='2 runs' FROM oversight.Commitment c JOIN x s ON 1=1 WHERE s.Id=2248")]
    [InlineData("UPDATE oversight.Commitment SET Budget='2 runs' FROM x s WHERE s.Id=2248")]
    [InlineData("MERGE oversight.Commitment AS c USING x s ON s.Id=2248 WHEN MATCHED THEN UPDATE SET Budget='2 runs';")]
    [InlineData("MERGE oversight.Commitment AS c USING x s ON c.Id=2248 WHEN NOT MATCHED BY SOURCE THEN UPDATE SET Budget='2 runs';")]
    [InlineData("UPDATE oversight.Commitment SET Budget='2 runs' WHERE Id=2143 UPDATE oversight.Commitment SET Budget='2 runs' WHERE Id=2248")]
    public void ApprovalForOneRowCannotAuthorizeAJoinSourceOrAnotherStatement(string sql) =>
        Assert.NotNull(SqlWriteCutGuard.DecideBudget("Bash", "sqlcmd -Q \"" + sql + "\"", null, row => row == 2248));

    [Theory]
    [InlineData("UPDATE c SET Budget='2 runs' FROM oversight.Commitment c WHERE c.Id=2248")]
    [InlineData("MERGE oversight.Commitment AS c USING x s ON c.Id=2248 WHEN MATCHED THEN UPDATE SET Budget='2 runs';")]
    public void PreciselyBoundedAliasAndMergeCanBeApproved(string sql) =>
        Assert.Null(SqlWriteCutGuard.DecideBudget("Bash", "sqlcmd -Q \"" + sql + "\"", null, row => row == 2248));

    [Fact]
    public void MissingCountStillReportsParsedHours()
    {
        var limit = BudgetGuard.Parse("two Codex passes; ~4 h");
        Assert.Equal(0, limit.Runs);
        Assert.Equal(TimeSpan.FromHours(4), limit.Duration);
        Assert.NotNull(limit.Error);
    }

    [Theory]
    [InlineData("one KOR-210 run or two more if needed")]
    [InlineData("one KOR-210 run; ~4 h or 8")]
    public void AlternativesAreNotAnAllowance(string text) => Assert.NotNull(BudgetGuard.Parse(text).Error);

    [Fact]
    public void NoUnknownSharedSessionApproval()
    {
        Assert.Null(BudgetApproval.Find(1, "IAN-APPROVED-OVER-BUDGET", "unknown", null, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void BudgetErrorsStayNonzeroEvenInHookMode()
    {
        var exit = Program.Run(["budget", "check", "--hook"], new StringWriter(), new StringWriter());
        Assert.NotEqual(0, exit);
        Assert.NotEqual(0, Program.Run(["--hook", "budget", "check"], new StringWriter(), new StringWriter()));
    }
}

public sealed class BudgetDatabaseFactAttribute : FactAttribute
{
    public BudgetDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("KOR_ENGINEERINGTOOLS_STANDARDSDB") == "budget-stop-offline")
            Skip = "Deliberate offline full-suite comparison; run separately in a rollback transaction.";
    }
}

public sealed class BudgetConcurrencyTests
{
    [BudgetDatabaseFact]
    public void AnotherTransactionCannotSpendWhileTheRowLockIsHeld()
    {
        // No fixture rows: take the same row application lock used by Check, prove exclusion, then roll both back.
        const int row = 2143;
        using var first = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        using var second = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        BudgetStore.Read(first, row, forSpend: true);
        using var attempt = second.Command("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'budget:row:2143', " +
            "@LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0; SELECT @r;");
        Assert.Equal(-1, (int)attempt.ExecuteScalar()!);
    }

    [BudgetDatabaseFact]
    public void SqlHookLogsBudgetRefusalAndRequiresBudgetTokenNotSpendToken()
    {
        var session = "budget-hook-" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(BudgetStopTests.Repo(), "obj/budget-stop", session);
        Directory.CreateDirectory(Path.Combine(root, "asked"));
        Directory.CreateDirectory(Path.Combine(root, "projects", "repo"));
        using var db = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        try
        {
            var hook = JsonSerializer.Serialize(new { session_id = session, tool_name = "Bash", tool_input = new { command = "sqlcmd -Q \"UPDATE oversight.Commitment SET Budget='2 runs' WHERE Id=2248\"" } });
            string Run()
            {
                var output = new StringWriter();
                Program.Run(["enforce", "--hook", "--module", "Framework", "--prompts-dir", Path.Combine(root, "asked"), "--projects-dir", Path.Combine(root, "projects")],
                    () => hook, output, new StringWriter(), Path.Combine(root, "hook.log"), db, db);
                return output.ToString();
            }
            void Prompt(string token)
            {
                File.WriteAllText(Path.Combine(root, "asked", session + ".jsonl"), JsonSerializer.Serialize(new { prompt = token }));
                File.WriteAllText(Path.Combine(root, "projects", "repo", session + ".jsonl"), JsonSerializer.Serialize(new
                {
                    type = "user", sessionId = session, timestamp = DateTimeOffset.UtcNow, entrypoint = "cli", promptSource = "typed", message = new { role = "user", content = token }
                }));
            }
            Prompt("IAN-APPROVED-OVER-BUDGET 2248");
            Assert.Contains("IAN-APPROVED-BUDGET 2248", Run());
            Prompt("IAN-APPROVED-BUDGET 2248");
            Assert.DoesNotContain("Budget change refused", Run());
            using var count = db.Command("SELECT COUNT(*) FROM oversight.GuardRefusal WHERE Guard=N'budget' AND Session=@s");
            count.Parameters.AddWithValue("@s", session);
            Assert.Equal(1, (int)count.ExecuteScalar()!);
        }
        finally { Directory.Delete(root, true); }
    }
}
