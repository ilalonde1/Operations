using System.Globalization;
using Kor.Operations.Framework.Recall;

namespace Kor.Operations.Framework.Cli;

/// <summary>Board row 1893: <c>kor recall --module M "task" [--budget tokens]</c>. Read-only; a pilot, wired to no hook (<see cref="MemoryRecall"/>).</summary>
public static partial class Program
{
    private static int RecallVerb(Options o, TextWriter output, BrainDb? db)
    {
        var module = o.Get("--module") ?? throw new ArgumentException("recall needs --module <lane>");
        var task = o.Positional.ElementAtOrDefault(1);
        if (string.IsNullOrWhiteSpace(task)) throw new ArgumentException("recall needs the task: kor recall --module M \"<task>\"");
        if (task.Length > MemoryRecall.MostTaskChars) throw new ArgumentException($"the task is {task.Length} chars; recall searches at most {MemoryRecall.MostTaskChars}, and will not search part of it");
        var budget = o.Get("--budget") is null ? MemoryRecall.DefaultBudgetTokens
            : o.Int("--budget") ?? throw new ArgumentException($"--budget takes a whole number, not '{o.Get("--budget")}'");
        if (budget < 1_000) throw new ArgumentException("--budget under 1,000 tokens leaves no room for the head and one section");
        var found = db is not null ? MemoryRecall.Search(db, module, task)
            : BrainDb.Read(KorStandardsConnection.Login.Brain, d => MemoryRecall.Search(d, module, task));
        if (found.ModuleRecords == 0) throw new ArgumentException($"module '{module}' holds no live record: nothing was searched");
        output.Write(MemoryRecall.Render(module, task, found, budget, DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
        return 0;
    }
}
