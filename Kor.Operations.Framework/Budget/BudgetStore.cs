using System.Data;
using System.Text.Json;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Stall;

namespace Kor.Operations.Framework.Budget;

public sealed record BudgetSnapshot(int Row, string? Text, bool Exists, IReadOnlyList<BudgetSpend> Spends, DateTimeOffset Now);

/// <summary>
/// Uses the Board login for oversight reads, brain.Event inserts and LogGuardRefusal in ONE transaction. A transaction
/// application lock serialises callers by row (including an empty ledger); an update lock holds the Budget stable.
/// Failure to read, lock, record or commit never grants permission. Caller-owned transactions are never committed here.
/// No schema migration, cache, local ledger or session-based allowance. Events are append-only via BrainStore.
/// </summary>
public static class BudgetStore
{
    private static string Key(int row) => $"budget:row:{row}";

    public static BudgetSnapshot Read(BrainDb db, int row, bool forSpend = false)
    {
        if (forSpend)
        {
            using var acquire = db.Command("DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource=@key, @LockMode='Exclusive', " +
                "@LockOwner='Transaction', @LockTimeout=5000; IF @r < 0 THROW 51000, 'budget row lock unavailable; no spend recorded', 1;", 7);
            acquire.Parameters.Add("@key", SqlDbType.NVarChar, 255).Value = Key(row);
            acquire.ExecuteNonQuery();
        }
        string? budget = null;
        bool exists;
        using (var command = db.Command("SELECT Budget FROM oversight.Commitment" + (forSpend ? " WITH (UPDLOCK, HOLDLOCK)" : "") + " WHERE Id=@row", 7))
        {
            command.Parameters.Add("@row", SqlDbType.Int).Value = row;
            using var reader = command.ExecuteReader();
            exists = reader.Read();
            if (exists && !reader.IsDBNull(0)) budget = reader.GetString(0);
        }
        var spends = new List<BudgetSpend>();
        using (var command = db.Command("SELECT AtUtc, Outcome, Detail, Session FROM brain.Event " +
            "WHERE Module=N'Framework' AND Kind=N'gate' AND Actor=N'kor budget' AND Ref=@key ORDER BY Id", 7))
        {
            command.Parameters.Add("@key", SqlDbType.NVarChar, 400).Value = Key(row);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                using var detail = JsonDocument.Parse(reader.GetString(2));
                var root = detail.RootElement;
                // Corrupt records must not silently disappear from the spend count.
                if (root.GetProperty("row").GetInt32() != row) throw new InvalidOperationException("budget ledger row mismatch; refusing to guess");
                spends.Add(new(row, root.GetProperty("step").GetString()!, root.GetProperty("ref").GetString(), reader.GetString(3), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(0)));
            }
        }
        using var clock = db.Command("SELECT SYSDATETIMEOFFSET()", 3);
        return new(row, budget, exists, spends, (DateTimeOffset)clock.ExecuteScalar()!);
    }

    public static BudgetDecision Check(BrainDb db, int row, string step, string? reference, string session, string? promptsDir, string? projectsDir)
    {
        var snapshot = Read(db, row, forSpend: true);
        var approval = snapshot.Exists ? BudgetApproval.Find(row, "IAN-APPROVED-OVER-BUDGET", session, promptsDir, projectsDir, snapshot.Now) : null;
        var decision = BudgetGuard.Decide(row, snapshot.Text, snapshot.Spends, step, snapshot.Now, approval);
        if (!snapshot.Exists) decision = decision with { Outcome = "refused", Message = $"refused: row {row} does not exist. " + decision.Message };
        var detail = JsonSerializer.Serialize(new
        {
            row, step, @ref = reference, session, outcome = decision.Outcome, budget = snapshot.Text,
            spent = decision.Spent, limit = decision.Limit.Runs, hours = decision.Limit.Duration?.TotalHours,
            approvalAtUtc = decision.Outcome == "lifted" ? approval : null
        });
        BrainStore.Insert(new("Framework", "gate", null, null, Key(row), decision.Outcome, detail, "kor budget", session), db);
        if (!decision.Allowed) StallStore.LogGuardRefusal(db, "budget", $"row {row} {step}", session, null, decision.Message);
        return decision;
    }
}
