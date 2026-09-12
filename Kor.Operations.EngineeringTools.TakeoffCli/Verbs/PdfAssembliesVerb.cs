// The takeoff verb `pdf-assemblies`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// THE SET'S ASSEMBLY SCHEDULES, READ WHOLE (intake step 32). Usage: takeoff pdf-assemblies <set.pdf> [assemblies.csv]
// An architect's set states its wall and floor types as cards on schedule sheets — code, name,
// every layer of the build-up, the fire and sound ratings, the references, the remarks. The plans
// tag walls with the codes. Everything on the card is read; the material and thickness the
// structural model takes are derived from the words by a vocabulary.
internal static class PdfAssembliesVerb
{
    public static bool Matches(string[] args) => args.Length >= 2 && args[0].Equals("pdf-assemblies", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"Not found: {args[1]}"); return 1; }
        if (args.Any(a => a.Equals("--trace", StringComparison.OrdinalIgnoreCase))) AssemblySchedule.Trace = Console.WriteLine;
        var (paOptions, paRulesSource) = PdfIntakeOptions.For(args.SkipWhile(a => !a.Equals("--rules-db", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() ?? Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable));
        var cards = AssemblySchedule.ReadSet(args[1], paOptions.AssemblyStructuralWords, paOptions.AssemblyPartitionWords);   // the vocabulary rows reach this verb too (Codex audit 2026-09-11, F20)
        Console.WriteLine($"{cards.Count} assembly card(s) on {cards.Select(c => c.Page).Distinct().Count()} schedule sheet(s)");
        Console.WriteLine($"{"kind",-7} {"code",-8} {"material",-9} {"mm",5}  {"F.R.R.",-6} {"S.T.C.",-6} layers  name");
        foreach (var c in cards)
            Console.WriteLine($"{c.Kind,-7} {c.Code,-8} {c.Material,-9} {(c.ThicknessMm is double t ? t.ToString("0") : "-"),5}  " +
                              $"{(c.Ratings.TryGetValue("F.R.R.", out var frr) ? frr : "-"),-6} {(c.Ratings.TryGetValue("S.T.C.", out var stc) ? stc : "-"),-6} {c.Layers.Count,6}  {c.Name}");
        var byKind = cards.GroupBy(c => c.Kind).Select(g => $"{g.Key}: {g.Count()} ({g.Count(c => c.IsStructural)} structural, {g.Count(c => c.Material == AssemblySchedule.Material.Stud)} stud, {g.Count(c => c.Material == AssemblySchedule.Material.Unknown)} unknown)");
        Console.WriteLine(string.Join("; ", byKind));
        if (args.Length >= 3)
        {
            var csv = new List<string> { "kind,code,name,material,structural,thickness_mm,frr,stc,layers,references,remarks,page" };
            static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
            foreach (var c in cards)
                csv.Add(string.Join(",", c.Kind, c.Code, Q(c.Name), c.Material, c.IsStructural ? "1" : "0",
                    c.ThicknessMm is double tm ? tm.ToString("0") : "", Q(c.Ratings.TryGetValue("F.R.R.", out var f) ? f : ""), Q(c.Ratings.TryGetValue("S.T.C.", out var s) ? s : ""),
                    Q(string.Join(" | ", c.Layers)), Q(string.Join(" | ", c.References)), Q(string.Join(" | ", c.Remarks)), c.Page.ToString()));
            File.WriteAllLines(args[2], csv);
            Console.WriteLine($"→ {args[2]}");
        }
        return 0;
    }
}
