// The takeoff verb `col-text`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// DIAGNOSTIC: takeoff col-text <pdf> <page> — the DETERMINISTIC column-schedule read (text grid, no vision):
// bands, key-plan counts, and the priced result at 10.5ft storeys.
internal static class ColTextVerb
{
    public static bool Matches(string[] args) => args.Length >= 3 && args[0].Equals("col-text", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        var cpage = VectorPageReader.ReadPage(args[1], int.Parse(args[2]));
        var cladder = ScheduleGridReader.ReadLevelLadder(cpage).OrderByDescending(r => r.Y).Select(r => r.RawLabel).ToList();
        var cbands = ScheduleGridReader.ReadColumnBands(cpage);
        var cmarks = cbands.Select(b => b.Mark).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var ccounts = ScheduleGridReader.CountColumnMarks(cpage, cmarks);
        Console.WriteLine($"ladder {cladder.Count} levels; {cbands.Count} band-rows over {cmarks.Count} mark(s)");
        foreach (var mk in cmarks.OrderBy(m => m))
        {
            var mb = cbands.Where(b => b.Mark.Equals(mk, StringComparison.OrdinalIgnoreCase)).ToList();
            Console.WriteLine($"  {mk,-5} x{ccounts.GetValueOrDefault(mk.ToUpperInvariant(), 1),2}  {mb.Count} level(s), sizes {string.Join("|", mb.Select(b => $"{b.WidthIn * 25.4:0}x{b.DepthIn * 25.4:0}").Distinct())}");
        }
        // Price with counts (distinct-mark replication) at typical storeys, for comparison with vision.
        var expanded = new List<ScheduleTakeoff.ColumnBand>();
        foreach (var b in cbands)
        {
            int n = Math.Max(1, ccounts.GetValueOrDefault(b.Mark.ToUpperInvariant(), 1));
            for (int k = 1; k <= n; k++)
                expanded.Add(n == 1 ? b : b with { Mark = $"{b.Mark}#{k}" });
        }
        var norm = cladder.Select(ScheduleTakeoff.NormalizeLevel).ToList();
        var cres = ScheduleTakeoff.ComputeColumn(norm, norm.Select(_ => 126.0).ToList(), expanded);
        Console.WriteLine($"priced: {cres.MarksPriced} columns -> {cres.TotalCuYd:N0} cy (10.5ft storeys)");
        return 0;
    }
}
