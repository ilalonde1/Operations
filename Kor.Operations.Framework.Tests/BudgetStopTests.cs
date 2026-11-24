using System.Reflection;
using System.Text.Json;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Enforce;
using Xunit;

namespace Kor.Operations.Framework.Tests;

// Reflection keeps the cases-only commit executable against the old assembly: a missing budget contract is a
// failed assertion, not a compiler error. No production implementation or substitute policy lives in this harness.
public sealed class BudgetStopTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-23T18:00:00Z");
    private readonly string _root = Path.Combine(Repo(), "obj", "budget-stop", "cases", Guid.NewGuid().ToString("N"));
    private const string Row2248 = "At most one KOR-210 SAFE run and two Codex passes; ~4 h; reads the 2143 bundle, its four Codex responses and the done-list; Ian picks the model if not B1 L02 - asked in this session's closing message 20 Nov.";
    private const string Row2143 = "One SAFE run on KOR-210 for one building model plus one Codex review; sized before starting.";

    internal static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Path.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("not in checkout");
    }

    private static Type Contract(string name)
    {
        var t = typeof(SqlWriteCutGuard).Assembly.GetType("Kor.Operations.Framework.Budget." + name);
        Assert.True(t is not null, "Missing budget-stop contract: " + name);
        return t!;
    }

    private static object? Call(string name, string method, params object?[] args)
    {
        var m = Contract(name).GetMethod(method, BindingFlags.Public | BindingFlags.Static);
        Assert.True(m is not null, "Missing method: " + method);
        return m!.Invoke(null, args);
    }

    private static T Prop<T>(object value, string name) => (T)value.GetType().GetProperty(name)!.GetValue(value)!;
    private static object Parse(string? budget) => Call("BudgetGuard", "Parse", budget)!;
    private static object Spend(int row, string outcome, DateTimeOffset at, string session = "earlier-session") =>
        Activator.CreateInstance(Contract("BudgetSpend"), row, "kor210-run", "tag", session, outcome, at)!;
    private static object Decide(int row, string? budget, object[] spends, DateTimeOffset? approval = null, string step = "kor210-run")
    {
        var array = Array.CreateInstance(Contract("BudgetSpend"), spends.Length);
        for (var i = 0; i < spends.Length; i++) array.SetValue(spends[i], i);
        return Call("BudgetGuard", "Decide", row, budget, array, step, Now, approval)!;
    }

    [Theory]
    [InlineData("one KOR-210 run", 1)]
    [InlineData("a KOR-210 SAFE run", 1)]
    [InlineData("at most one KOR-210 ETABS run", 1)]
    [InlineData("3 KOR-210 runs", 3)]
    [InlineData("2 KOR-210 SAFE runs", 2)]
    [InlineData("4 KOR-210 ETABS runs", 4)]
    [InlineData("One SAFE run on KOR-210", 1)]
    [InlineData("a ETABS run on KOR-210", 1)]
    [InlineData("2 runs on KOR-210", 2)]
    [InlineData("0 KOR-210 runs", 0)]
    public void RunWordings(string text, int count)
    {
        var parsed = Parse(text);
        Assert.Null(Prop<string?>(parsed, "Error"));
        Assert.Equal(count, Prop<int>(parsed, "Runs"));
    }

    [Theory]
    [InlineData("~4 h", 14400)]
    [InlineData("half a day", 43200)]
    [InlineData("20 min", 1200)]
    [InlineData("20 mins", 1200)]
    [InlineData("20 minute", 1200)]
    [InlineData("20 minutes", 1200)]
    [InlineData("1.5 h", 5400)]
    [InlineData("1 hr", 3600)]
    [InlineData("1 hrs", 3600)]
    [InlineData("1 hour", 3600)]
    [InlineData("1 hours", 3600)]
    [InlineData("1 day", 86400)]
    [InlineData("1.5 days", 129600)]
    public void DurationWordings(string duration, int seconds)
    {
        var parsed = Parse(duration + "; one KOR-210 run");
        Assert.Null(Prop<string?>(parsed, "Error"));
        Assert.Equal(TimeSpan.FromSeconds(seconds), Prop<TimeSpan?>(parsed, "Duration"));
    }

    [Theory]
    [InlineData(2248, Row2248)]
    [InlineData(2143, Row2143)]
    public void HistoricalRowsRefuseTheirSecondSpendAcrossSessions(int row, string budget)
    {
        Assert.Equal("allowed", Prop<string>(Decide(row, budget, []), "Outcome"));
        var spends = new[] { Spend(row, "allowed", Now.AddMinutes(-5)), Spend(row, "refused", Now.AddMinutes(-4)), Spend(999, "allowed", Now.AddMinutes(-3)) };
        var second = Decide(row, budget, spends);
        Assert.Equal("refused", Prop<string>(second, "Outcome"));
        var says = Prop<string>(second, "Message");
        Assert.Contains(row.ToString(), says);
        Assert.Contains(budget, says);
        Assert.Contains("spent 1 of 1", says);
        Assert.Contains("IAN-APPROVED-OVER-BUDGET " + row, says);
        if (row == 2248) Assert.Equal(TimeSpan.FromHours(4), Prop<TimeSpan?>(Parse(budget), "Duration"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("two Codex passes; ~4 h")]
    [InlineData("one or two KOR-210 runs")]
    [InlineData("1-2 KOR-210 runs")]
    [InlineData("1.5 KOR-210 runs")]
    [InlineData("-1 KOR-210 run")]
    [InlineData("one KOR-210 run or 3 KOR-210 runs")]
    [InlineData("one KOR-210 run; another KOR-210 run if needed")]
    [InlineData("no more than 2 KOR-210 runs")]
    [InlineData("~4-6 h; one KOR-210 run")]
    [InlineData("one KOR-210 run; ~4 h or ~8 h")]
    [InlineData("one KOR-210 run; ~many h")]
    [InlineData("999999999999999999999 KOR-210 runs")]
    public void MissingOrAmbiguousBudgetNeverGuesses(string? text)
    {
        var decision = Decide(2248, text, []);
        Assert.Equal("refused", Prop<string>(decision, "Outcome"));
        if (text is "two Codex passes; ~4 h") Assert.Contains("state the run count in the row's Budget", Prop<string>(decision, "Message"));
    }

    [Fact]
    public void ClockStartsAtFirstAllowedSpendAndLiftedSpendsCount()
    {
        const string budget = "3 KOR-210 runs; ~4 h";
        Assert.Equal("allowed", Prop<string>(Decide(1, budget, [Spend(1, "refused", Now.AddDays(-4))]), "Outcome"));
        Assert.Equal("refused", Prop<string>(Decide(1, budget, [Spend(1, "allowed", Now.AddHours(-4))]), "Outcome"));
        Assert.Equal("allowed", Prop<string>(Decide(1, budget, [Spend(1, "allowed", Now.AddHours(-3.9))]), "Outcome"));
        Assert.Equal("refused", Prop<string>(Decide(1, "one KOR-210 run", [Spend(1, "lifted", Now.AddHours(-1))]), "Outcome"));
        Assert.Equal("refused", Prop<string>(Decide(1, "one KOR-210 run", [], step: "codex-pass"), "Outcome"));
    }

    [Fact]
    public void LiftExpiresAndDoesNotEraseSpending()
    {
        var spends = new[] { Spend(1, "allowed", Now.AddDays(-1)) };
        Assert.Equal("lifted", Prop<string>(Decide(1, "one KOR-210 run; ~4 h", spends, Now.AddHours(-11)), "Outcome"));
        Assert.Equal("refused", Prop<string>(Decide(1, "one KOR-210 run", spends, Now.AddHours(-12)), "Outcome"));
        Assert.Equal("refused", Prop<string>(Decide(1, "one KOR-210 run", spends, Now.AddMinutes(1)), "Outcome"));
    }

    private DateTimeOffset? Approval(string prompt, string transcript, string session = "s1")
    {
        Directory.CreateDirectory(Path.Combine(_root, "asked"));
        Directory.CreateDirectory(Path.Combine(_root, "projects", "repo"));
        File.WriteAllText(Path.Combine(_root, "asked", "s1.jsonl"), JsonSerializer.Serialize(new { prompt }));
        File.WriteAllText(Path.Combine(_root, "projects", "repo", "s1.jsonl"), transcript);
        return (DateTimeOffset?)Call("BudgetApproval", "Find", 2248, "IAN-APPROVED-OVER-BUDGET", session,
            Path.Combine(_root, "asked"), Path.Combine(_root, "projects"), Now);
    }

    private static string User(string text, int hours = 1, string type = "user", string source = "typed", bool meta = false, object? content = null) =>
        JsonSerializer.Serialize(new { type, sessionId = "s1", timestamp = Now.AddHours(-hours), entrypoint = "cli", promptSource = source, isMeta = meta,
            message = new { role = type, content = content ?? text } });

    [Theory]
    [InlineData("IAN-APPROVED-OVER-BUDGET 2248", "user", "typed", false, 1, "s1", true)]
    [InlineData("IAN-APPROVED-OVER-BUDGET 2248", "user", "queued", false, 11, "s1", true)]
    [InlineData("IAN-APPROVED-OVER-BUDGET 2248", "user", "typed", false, 12, "s1", false)]
    [InlineData("IAN-APPROVED-OVER-BUDGET 2248", "user", "typed", false, -1, "s1", false)]
    [InlineData("IAN-APPROVED-OVER-BUDGET 2143", "user", "typed", false, 1, "s1", false)]
    [InlineData("IAN-APPROVED-OVER-BUDGET 22480", "user", "typed", false, 1, "s1", false)]
    [InlineData("IAN-APPROVED-OVER-BUDGET 2248", "assistant", "typed", false, 1, "s1", false)]
    [InlineData("IAN-APPROVED-OVER-BUDGET 2248", "user", "sdk", false, 1, "s1", false)]
    [InlineData("IAN-APPROVED-OVER-BUDGET 2248", "user", "system", true, 1, "s1", false)]
    [InlineData("IAN-APPROVED-OVER-BUDGET 2248", "user", "typed", false, 1, "other", false)]
    [InlineData("quoted: `IAN-APPROVED-OVER-BUDGET 2248`", "user", "typed", false, 1, "s1", false)]
    public void OnlyTypedSessionRowTokenWithinTwelveHoursLifts(string prompt, string type, string source, bool meta, int hours, string session, bool expected) =>
        Assert.Equal(expected, Approval(prompt, User(prompt, hours, type, source, meta), session) is not null);

    [Fact]
    public void ToolFileAndUnreadableRecordsDoNotLift()
    {
        const string token = "IAN-APPROVED-OVER-BUDGET 2248";
        Assert.Null(Approval(token, User(token, content: new[] { new { type = "tool_result", content = token } })));
        Assert.Null(Approval("Read file with approval", User(token)));
        Assert.Null(Approval(token, "{invalid json"));
        Assert.Null(Approval(token, ""));
    }

    private static object? Sql(string sql, Func<int, bool>? approved = null, string tool = "Bash", string? cwd = null)
    {
        var method = typeof(SqlWriteCutGuard).GetMethod("DecideBudget");
        Assert.True(method is not null, "Missing SqlWriteCutGuard budget protection");
        return method!.Invoke(null, [tool, sql, cwd, approved ?? (_ => false)]);
    }

    [Theory]
    [InlineData("UPDATE oversight.Commitment SET Budget='2 KOR-210 runs' WHERE Id=2248")]
    [InlineData("UPDATE [KorStandards].[oversight].[Commitment] SET [Budget]=N'2 runs' WHERE [Id] = 2248")]
    [InlineData("UPDATE c SET c.Budget='2 runs' FROM oversight.Commitment c WHERE c.Id=2248")]
    [InlineData("MERGE INTO oversight.Commitment AS c USING (SELECT 2248 AS Id) AS s ON c.Id=s.Id WHEN MATCHED THEN UPDATE SET Budget='2 runs';")]
    [InlineData("UPDATE oversight.Commitment SET State='open', Budget='2 runs' WHERE Id=2248")]
    public void ExistingRowBudgetWritesRefuse(string sql) => Assert.NotNull(Sql("sqlcmd -Q \"" + sql + "\""));

    [Theory]
    [InlineData("INSERT INTO oversight.Commitment(Budget) VALUES('one KOR-210 run')")]
    [InlineData("UPDATE oversight.Commitment SET State='open' WHERE Id=2248")]
    [InlineData("SELECT Budget FROM oversight.Commitment")]
    [InlineData("UPDATE other.TableName SET Budget='x' WHERE Id=2248")]
    [InlineData("UPDATE oversight.Commitment SET Evidence='Budget = increased' WHERE Id=2248")]
    [InlineData("-- UPDATE oversight.Commitment SET Budget='x' WHERE Id=2248\nSELECT 1")]
    public void NewRowsAndOtherWritesStillPass(string sql) => Assert.Null(Sql("sqlcmd -Q \"" + sql + "\""));

    [Fact]
    public void SqlLiftIsForExactlyTheAffectedRowAndCannotHideAdditionalTargets()
    {
        const string prefix = "sqlcmd -Q \"UPDATE oversight.Commitment SET Budget='2 runs' WHERE ";
        Assert.Null(Sql(prefix + "Id=2248\"", row => row == 2248));
        Assert.NotNull(Sql(prefix + "Id=2248\"", row => row == 2143));
        Assert.NotNull(Sql(prefix + "Id=2248 OR Id=2143\"", row => row == 2248));
        Assert.NotNull(Sql(prefix + "Id=@row\"", _ => true));
        Assert.NotNull(Sql(prefix + "Id=2248; UPDATE oversight.Commitment SET Budget='x' WHERE Id=2143\"", row => row == 2248));
        Assert.Null(Sql("echo \"UPDATE oversight.Commitment SET Budget='x' WHERE Id=2248\""));
    }

    [Fact]
    public void SqlInputFileAndPowerShellQueryAreInspected()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "raise.sql"), "UPDATE oversight.Commitment SET Budget='2 runs' WHERE Id=2248;");
        Assert.NotNull(Sql("sqlcmd -i raise.sql", cwd: _root));
        Assert.NotNull(Sql("Invoke-Sqlcmd -Query \"UPDATE oversight.Commitment SET Budget='2 runs' WHERE Id=2248\"", tool: "PowerShell"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
