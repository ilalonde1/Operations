// The takeoff verb `verify-e2k`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// INSPECT one plan: what does this drawing actually give the model? Lists the layers, the
// outlines that closed, and every member read out of them with its dimensions.
// Usage: takeoff dxf-inspect <plan.dxf> [--walls]
// The gate. Every fault it refuses actually shipped, or came within one publish of shipping, on
// 31168 -- and every one passed the counts in the report. The report FLAGS, which needs a reader;
// this REFUSES, which does not.
internal static class VerifyE2kVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("verify-e2k", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: takeoff verify-e2k <model.e2k> [--joint-tolerance <in>] [--dropped <a,b,c>] [--reference <ref.e2k>] [--report report.txt] [--questions questions.xlsx]"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"Not found '{args[1]}'."); return 2; }

        double jointTolerance = 0.05;
        var droppedNames = new List<string>();
        string? referencePath = null;
        string? reportPath = null;
        string? questionsPath = null;
        for (int i = 2; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--reference", StringComparison.OrdinalIgnoreCase))
                referencePath = args[i + 1];
            if (args[i].Equals("--report", StringComparison.OrdinalIgnoreCase))
                reportPath = args[i + 1];
            if (args[i].Equals("--questions", StringComparison.OrdinalIgnoreCase))
                questionsPath = args[i + 1];
            if (args[i].Equals("--joint-tolerance", StringComparison.OrdinalIgnoreCase))
                double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out jointTolerance);
            if (args[i].Equals("--dropped", StringComparison.OrdinalIgnoreCase))
                droppedNames.AddRange(args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length > 0));
        }

        if (referencePath is not null && !File.Exists(referencePath))
        { Console.Error.WriteLine($"Reference not found '{referencePath}'."); return 2; }
        if (reportPath is not null && !File.Exists(reportPath))
        { Console.Error.WriteLine($"Report not found '{reportPath}'."); return 2; }
        if (questionsPath is not null && !File.Exists(questionsPath))
        { Console.Error.WriteLine($"Questions workbook not found '{questionsPath}'."); return 2; }

        var breaches = ShippedModelInvariants.Check(
            File.ReadLines(args[1]), jointTolerance, droppedNames,
            referencePath is null ? null : File.ReadLines(referencePath),
            reportLines: reportPath is null ? null : File.ReadLines(reportPath),
            workbookText: questionsPath is null ? null : ModelQuestionnaire.ClaimLines(questionsPath));
        var blockers = breaches.Where(b => b.BlocksPublishing).ToList();
        var advisory = breaches.Where(b => !b.BlocksPublishing).ToList();
        if (blockers.Count == 0)
        {
            if (advisory.Count == 0)
            {
                Console.WriteLine($"verify-e2k: {Path.GetFileName(args[1])} passes every publish-blocking invariant.");
                return 0;
            }

            Console.WriteLine($"verify-e2k: {Path.GetFileName(args[1])} passes every publish-blocking invariant; reports {advisory.Count} advisory check(s).");
            foreach (var group in advisory.GroupBy(b => b.Rule).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"  [advisory:{group.Key}] {group.Count()}");
                foreach (var b in group.Take(6)) Console.WriteLine($"      {b.What}   ({b.Where})");
                if (group.Count() > 6) Console.WriteLine($"      ... and {group.Count() - 6} more");
            }
            return 0;
        }

        Console.Error.WriteLine($"verify-e2k: {Path.GetFileName(args[1])} FAILS {blockers.Count} publish-blocking check(s).");
        foreach (var group in blockers.GroupBy(b => b.Rule).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Console.Error.WriteLine($"  [{group.Key}] {group.Count()}");
            foreach (var b in group.Take(6)) Console.Error.WriteLine($"      {b.What}   ({b.Where})");
            if (group.Count() > 6) Console.Error.WriteLine($"      ... and {group.Count() - 6} more");
        }
        if (advisory.Count > 0)
        {
            Console.Error.WriteLine($"  advisory check(s) also reported: {advisory.Count}");
            foreach (var group in advisory.GroupBy(b => b.Rule).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                Console.Error.WriteLine($"  [advisory:{group.Key}] {group.Count()}");
                foreach (var b in group.Take(6)) Console.Error.WriteLine($"      {b.What}   ({b.Where})");
                if (group.Count() > 6) Console.Error.WriteLine($"      ... and {group.Count() - 6} more");
            }
        }
        return 3;
    }
}
