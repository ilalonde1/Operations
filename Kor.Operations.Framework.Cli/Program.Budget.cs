using Kor.Operations.Framework.Budget;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Cli;

public static partial class Program
{
    private static int BudgetVerb(Options o, TextWriter output, BrainDb? board)
    {
        var action = o.Positional.ElementAtOrDefault(1);
        var row = o.Int("--row") ?? throw new ArgumentException("budget check|show needs --row <positive board row>");
        if (row <= 0) throw new ArgumentException("budget --row must be positive");
        if (action == "show")
        {
            var snapshot = board is not null ? BudgetStore.Read(board, row)
                : BrainDb.Read(KorStandardsConnection.Login.Board, db => BudgetStore.Read(db, row));
            if (!snapshot.Exists) { output.WriteLine($"refused: row {row} does not exist"); return 1; }
            var parsed = BudgetGuard.Parse(snapshot.Text);
            var spends = snapshot.Spends.Where(s => s.Outcome is "allowed" or "lifted" && s.Step == BudgetGuard.Step).ToList();
            output.WriteLine($"row {row} Budget: {snapshot.Text ?? "(missing)"}");
            output.WriteLine($"parsed: {parsed.Runs} KOR-210 runs; time: {(parsed.Duration is { } time ? time.TotalHours.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + " h" : "not stated")}; {parsed.Error ?? "valid"}");
            output.WriteLine($"spent {spends.Count} of {parsed.Runs}; {Math.Max(0, parsed.Runs - spends.Count)} runs left");
            if (parsed.Duration is { } duration)
                output.WriteLine(spends.Count == 0 ? "clock not started (first allowed spend starts it)"
                    : $"clock started {spends.Min(s => s.AtUtc):O}; {Math.Max(0, (duration - (snapshot.Now - spends.Min(s => s.AtUtc))).TotalSeconds):0} seconds left");
            foreach (var spend in snapshot.Spends)
                output.WriteLine($"{spend.AtUtc:O} {spend.Outcome} {spend.Step} ref={spend.Ref ?? "-"} session={spend.Session}");
            return 0;
        }
        if (action != "check") throw new ArgumentException("budget needs check|show --row N; check also needs --step kor210-run [--ref tag]");
        var step = o.Get("--step") ?? throw new ArgumentException("budget check needs --step kor210-run");
        var session = o.Get("--session") ?? BrainStore.CurrentSession() ?? "unknown";
        if (session.Length > 64 || session.Length == 0) throw new ArgumentException("budget session must be 1-64 characters");
        var reference = o.Get("--ref");
        if (reference?.Length > 400 || step.Length > 80) throw new ArgumentException("budget ref must be <=400 characters and step <=80");
        BudgetDecision decision;
        if (board is not null)
            decision = BudgetStore.Check(board, row, step, reference, session, o.Get("--prompts-dir"), o.Get("--projects-dir"));
        else
        {
            // Print permission only AFTER the ledger and refusal transaction commits successfully.
            using var db = BrainDb.Open(KorStandardsConnection.Login.Board, 3);
            decision = BudgetStore.Check(db, row, step, reference, session, o.Get("--prompts-dir"), o.Get("--projects-dir"));
            db.Commit();
        }
        output.WriteLine(decision.Message);
        return decision.Allowed ? 0 : 1;
    }
}
