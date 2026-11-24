using System.Text.Json;
using Kor.Operations.Framework.Cli;
using Xunit;

namespace Kor.Operations.Framework.Tests;

// Every database command, including CLI writes, uses this one uncommittable transaction. Disposal rolls it all back.
// Full offline suites report this case as a DB-disabled failure, never a fake pass; run it separately with the DB.
public sealed class BudgetLedgerTests
{
    [Fact]
    public void CheckAndShowUseOneLedgerAndRefusalLogThenRollbackEverything()
    {
        // Fail on the absent verb before opening a transaction in the cases-only revision.
        Assert.NotNull(typeof(Enforce.SqlWriteCutGuard).Assembly.GetType("Kor.Operations.Framework.Budget.BudgetGuard"));
        var session = "budget-test-" + Guid.NewGuid().ToString("N");
        int row;
        using (var db = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board))
        {
            using (var add = db.Command("INSERT INTO oversight.Commitment (Owner, What, DoneWhen, State, RecordedBy, Budget) " +
                "VALUES (N'Framework', N'budget stop rollback test', N'test only', N'open', @session, N'one KOR-210 run; ~4 h'); SELECT CAST(SCOPE_IDENTITY() AS int)"))
            {
                add.Parameters.AddWithValue("@session", session);
                row = (int)add.ExecuteScalar()!;
            }
            (int Exit, string Text) Run(params string[] args)
            {
                var output = new StringWriter();
                var error = new StringWriter();
                var exit = Program.Run(["budget", ..args, "--row", row.ToString(), "--session", session], () => null,
                    output, error, Path.Combine(BudgetStopTests.Repo(), "obj/budget-stop/test-hook.log"), db, db);
                return (exit, output + error.ToString());
            }
            var first = Run("check", "--step", "kor210-run", "--ref", "first-tag");
            Assert.Equal(0, first.Exit);
            var second = Run("check", "--step", "kor210-run", "--ref", "second-tag");
            Assert.NotEqual(0, second.Exit);
            Assert.Contains("spent 1 of 1", second.Text);
            var prompts = Path.Combine(BudgetStopTests.Repo(), "obj/budget-stop", session);
            Directory.CreateDirectory(Path.Combine(prompts, "asked"));
            Directory.CreateDirectory(Path.Combine(prompts, "projects", "repo"));
            var token = "IAN-APPROVED-OVER-BUDGET " + row;
            File.WriteAllText(Path.Combine(prompts, "asked", session + ".jsonl"), JsonSerializer.Serialize(new { prompt = token }));
            File.WriteAllText(Path.Combine(prompts, "projects", "repo", session + ".jsonl"), JsonSerializer.Serialize(new
            {
                type = "user", sessionId = session, timestamp = DateTimeOffset.UtcNow.AddMinutes(-1), entrypoint = "cli", promptSource = "typed",
                message = new { role = "user", content = token }
            }));
            try
            {
                var lifted = Run("check", "--step", "kor210-run", "--ref", "lifted-tag", "--prompts-dir", Path.Combine(prompts, "asked"), "--projects-dir", Path.Combine(prompts, "projects"));
                Assert.Equal(0, lifted.Exit);
                Assert.Contains("lifted", lifted.Text);
            }
            finally { Directory.Delete(prompts, true); }
            var show = Run("show");
            Assert.Equal(0, show.Exit);
            Assert.Contains("one KOR-210 run; ~4 h", show.Text);
            Assert.Contains("first-tag", show.Text);
            Assert.Contains("second-tag", show.Text);
            Assert.Contains("0 runs left", show.Text);
            using (var events = db.Command("SELECT Outcome, Detail FROM brain.Event WHERE Session=@session ORDER BY Id"))
            {
                events.Parameters.AddWithValue("@session", session);
                using var reader = events.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal("allowed", reader.GetString(0));
                using var detail = JsonDocument.Parse(reader.GetString(1));
                Assert.Equal(row, detail.RootElement.GetProperty("row").GetInt32());
                Assert.Equal("kor210-run", detail.RootElement.GetProperty("step").GetString());
                Assert.Equal("first-tag", detail.RootElement.GetProperty("ref").GetString());
                Assert.Equal(session, detail.RootElement.GetProperty("session").GetString());
                Assert.True(reader.Read());
                Assert.Equal("refused", reader.GetString(0));
                Assert.True(reader.Read());
                Assert.Equal("lifted", reader.GetString(0));
                Assert.False(reader.Read());
            }
            using var refusals = db.Command("SELECT COUNT(*) FROM oversight.GuardRefusal WHERE Guard=N'budget' AND Session=@session");
            refusals.Parameters.AddWithValue("@session", session);
            Assert.Equal(1, (int)refusals.ExecuteScalar()!);
        }
        using var verify = BrainDb.OpenRead(KorStandardsConnection.Login.Board, snapshot: false);
        using var leftovers = verify.Command("SELECT (SELECT COUNT(*) FROM oversight.Commitment WHERE Id=@row) + " +
            "(SELECT COUNT(*) FROM brain.Event WHERE Session=@session) + (SELECT COUNT(*) FROM oversight.GuardRefusal WHERE Session=@session)");
        leftovers.Parameters.AddWithValue("@row", row);
        leftovers.Parameters.AddWithValue("@session", session);
        Assert.Equal(0, (int)leftovers.ExecuteScalar()!);
    }
}
