using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Kor.Operations.Framework.Budget;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Enforce;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Budget checks must survive one SQL 1205, and command options must not turn harmless SQL into a Budget refusal.
/// Covers injected retry/exhaustion, paired harmless/protected queries and files, and missing approval evidence.
/// Does not force a real server deadlock or interpret arbitrary Python/SQL. A custom client raising Budget still escapes.
/// Reflection lets the cases-only revision fail assertions against the original CLI instead of failing compilation.
/// </summary>
public sealed class BudgetRoundTwoTests : IDisposable
{
    private readonly string _root = Path.Combine(BudgetStopTests.Repo(), "obj", "budget-stop-2", "cases", Guid.NewGuid().ToString("N"));

    private static int RunCheck(Func<BudgetDecision> attempt, TextWriter output, bool retry, Action<TimeSpan> sleep)
    {
        var method = typeof(Program).GetMethod("RunBudgetCheck", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(method is not null, "Missing budget check retry/refusal boundary");
        try { return (int)method!.Invoke(null, [2248, attempt, output, retry, sleep])!; }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void First1205RetriesWholeAttemptAndOnlyPrintsItsFinalDecision(bool exhausted)
    {
        var calls = 0;
        var pauses = new List<TimeSpan>();
        var output = new StringWriter();
        var decision = BudgetGuard.Decide(2248, exhausted ? "0 KOR-210 runs" : "one KOR-210 run", [],
            BudgetGuard.Step, DateTimeOffset.UtcNow, null);
        var exit = RunCheck(() =>
        {
            Assert.Equal("", output.ToString());
            if (++calls == 1) throw DeadlockRetryTests.SqlError(1205);
            return decision;
        }, output, true, pauses.Add);
        Assert.Equal(2, calls);
        Assert.InRange(Assert.Single(pauses).TotalMilliseconds, 200, 600);
        Assert.Equal(exhausted ? 1 : 0, exit);
        Assert.Equal(decision.Message + Environment.NewLine, output.ToString());
    }

    [Fact]
    public void Second1205RefusesInOneLineNamingTheRetryWithoutRawException()
    {
        var calls = 0;
        var pauses = new List<TimeSpan>();
        var output = new StringWriter();
        var exit = RunCheck(() => { calls++; throw DeadlockRetryTests.SqlError(1205, "RAW server detail\nsecond line"); },
            output, true, pauses.Add);
        Assert.Equal(1, exit);
        Assert.Equal(2, calls);
        Assert.Single(pauses);
        Assert.Equal("refused: row 2248, budget check deadlocked (SQL 1205) after one retry; no spend recorded." + Environment.NewLine,
            output.ToString());
    }

    [Fact]
    public void CallerOwnedTransactionIsNeverReusedAfter1205()
    {
        var calls = 0;
        var output = new StringWriter();
        Assert.Equal(1, RunCheck(() => { calls++; throw DeadlockRetryTests.SqlError(1205); }, output, false,
            _ => Assert.Fail("A caller-owned transaction cannot be retried")));
        Assert.Equal(1, calls);
        Assert.Equal("refused: row 2248, budget check deadlocked (SQL 1205) in the caller's transaction; retry the whole transaction; no spend recorded." + Environment.NewLine,
            output.ToString());
    }

    [Fact]
    public void OtherSqlErrorsAreNotRetriedOrPrintedAsPermission()
    {
        var calls = 0;
        var output = new StringWriter();
        var error = Assert.Throws<SqlException>(() => RunCheck(() => { calls++; throw DeadlockRetryTests.SqlError(547); },
            output, true, _ => Assert.Fail("Only SQL 1205 is retried")));
        Assert.Equal(547, error.Number);
        Assert.Equal(1, calls);
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public void BudgetDatabaseClassesShareTheExistingNonparallelCollection()
    {
        foreach (var type in new[] { typeof(BudgetLedgerTests), typeof(BudgetConcurrencyTests) })
            Assert.Equal(LiveDb.Name, type.GetCustomAttributesData().SingleOrDefault(a => a.AttributeType == typeof(CollectionAttribute))?.ConstructorArguments[0].Value);
    }

    [Theory]
    [InlineData("Bash", "sqlcmd -I -Q")]
    [InlineData("PowerShell", "sqlcmd -I -q")]
    [InlineData("Bash", "python -I tools/SqlQuery/oq.py -q")]
    [InlineData("PowerShell", "python -I tools/SqlQuery/oq.py -q")]
    [InlineData("Bash", "python -I -X utf8 -u tools/SqlQuery/oq.py -q")]
    [InlineData("Bash", "python -X utf8 -I tools/SqlQuery/oq.py -q")]
    [InlineData("Bash", "python3 -W ignore -I -u tools/SqlQuery/oq.py -q")]
    [InlineData("Bash", "py -3 -I tools/SqlQuery/oq.py -q")]
    [InlineData("Bash", "python -I -- tools/SqlQuery/oq.py -q")]
    [InlineData("Bash", "python -I tools/SqlQuery/oq.py -I -Q")]
    [InlineData("PowerShell", "Invoke-Sqlcmd -QUERY")]
    public void InterpreterAndClientFlagsPreserveSqlDecisions(string tool, string command)
    {
        // No oq.py file here: recognising its SQL arguments must not depend on reading the script as a SQL input file.
        Directory.CreateDirectory(_root);
        SqlWriteCutGuard.BudgetRefusal? Decide(string sql, bool approved = false) =>
            SqlWriteCutGuard.DecideBudget(tool, command + " \"" + sql + "\"", _root, row => approved && row == 2248);
        Assert.Null(Decide("UPDATE KorStandards.oversight.Commitment SET State='open' WHERE Id=2248"));
        const string update = "UPDATE KorStandards.oversight.Commitment SET Budget='2 KOR-210 runs' WHERE Id=2248";
        var refusal = Assert.IsType<SqlWriteCutGuard.BudgetRefusal>(Decide(update));
        Assert.Equal(2248, refusal.Row);
        Assert.Contains("has not typed approval", refusal.Reason);
        Assert.Null(Decide(update, approved: true));
    }

    [Theory]
    [InlineData("Bash", "sqlcmd -I -i")]
    [InlineData("Bash", "python -I -X utf8 tools/SqlQuery/oq.py -i")]
    [InlineData("PowerShell", "Invoke-Sqlcmd -INPUTFILE")]
    public void RealInputFilesAreStillInspectedWithInterpreterOrClientFlags(string tool, string command)
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "input.sql");
        File.WriteAllText(file, "SELECT Budget FROM KorStandards.oversight.Commitment");
        Assert.Null(SqlWriteCutGuard.DecideBudget(tool, command + " input.sql", _root, _ => false));
        File.WriteAllText(file, "UPDATE KorStandards.oversight.Commitment SET Budget='2 runs' WHERE Id=2248");
        Assert.Equal(2248, SqlWriteCutGuard.DecideBudget(tool, command + " input.sql", _root, _ => false)?.Row);
    }

    [Theory]
    [InlineData("IAN-APPROVED-OVER-BUDGET", false)]
    [InlineData("IAN-APPROVED-OVER-BUDGET", true)]
    [InlineData("IAN-APPROVED-BUDGET", false)]
    [InlineData("IAN-APPROVED-BUDGET", true)]
    public void ValidSessionNeedsBothPromptRecordAndCorroboratingTranscript(string token, bool promptOnly)
    {
        const string session = "valid-session";
        var asked = Path.Combine(_root, "asked");
        var projects = Path.Combine(_root, "projects");
        Directory.CreateDirectory(asked);
        Directory.CreateDirectory(Path.Combine(projects, "repo"));
        var prompt = token + " 2248";
        var record = Path.Combine(asked, session + ".jsonl");
        var transcript = Path.Combine(projects, "repo", session + ".jsonl");
        var now = DateTimeOffset.UtcNow;
        var user = JsonSerializer.Serialize(new { type = "user", sessionId = session, timestamp = now.AddMinutes(-1),
            entrypoint = "cli", promptSource = "typed", message = new { role = "user", content = prompt } });
        if (promptOnly) File.WriteAllText(record, JsonSerializer.Serialize(new { prompt }));
        else File.WriteAllText(transcript, user);
        Assert.Null(BudgetApproval.Find(2248, token, session, asked, projects, now));
        // The same valid session lifts once the missing half is supplied.
        File.WriteAllText(record, JsonSerializer.Serialize(new { prompt }));
        File.WriteAllText(transcript, user);
        Assert.NotNull(BudgetApproval.Find(2248, token, session, asked, projects, now));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
