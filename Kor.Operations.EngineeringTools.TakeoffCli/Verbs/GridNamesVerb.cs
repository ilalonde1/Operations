// The axis names a sheet's DXF carries and the names a model's GRIDS carry, side by side — the first
// question when a sheet "could NOT be set on the grid by name". Prints the model's labels per grid
// system, then for each sheet its named axes (GridAlignment.NamedAxes, the composer's own reader), which
// of them the model names, and which it does not. Built 2026-09-11 when 31168's 2026-09-10 reissue placed
// 1 of 3 parkade sheets where the 08-25 issue placed 3 of 3 and the report said only "their axes name
// nothing the model names" — true, and not a reason. Ported from docs/etabs-handoff/grid_names.py (WP2).
//   takeoff grid-names <model.e2k> <sheet.dxf> [<sheet.dxf> ...]
internal static class GridNamesVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("grid-names", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff grid-names <model.e2k> <sheet.dxf> [<sheet.dxf> ...]"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"Model not found '{args[1]}'."); return 2; }
        var grids = E2kDocument.Load(args[1]).ReadGrids();
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var system in grids.GroupBy(g => g.Key.System).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var xs = system.Where(g => g.Key.Dir == "X").OrderBy(g => g.Value).Select(g => g.Key.Label);
            var ys = system.Where(g => g.Key.Dir == "Y").OrderBy(g => g.Value).Select(g => g.Key.Label);
            Console.WriteLine($"model grid system \"{system.Key}\": X {string.Join(" ", xs)} | Y {string.Join(" ", ys)}");
            foreach (var g in system) named.Add(g.Key.Label);
        }
        if (grids.Count == 0) Console.WriteLine($"{Path.GetFileName(args[1])} carries no GRID lines");

        int unplaceable = 0;
        foreach (string sheet in args.Skip(2))
        {
            if (!File.Exists(sheet)) { Console.Error.WriteLine($"Sheet not found '{sheet}'."); return 2; }
            var axes = GridAlignment.NamedAxes(DxfPlanReader.ReadSegments(sheet), DxfPlanReader.ReadPositionedTags(sheet));
            var have = axes.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n.Length).ThenBy(n => n, StringComparer.Ordinal).ToList();
            var yes = have.Where(named.Contains).ToList();
            var no = have.Where(n => !named.Contains(n)).ToList();
            int gridLines = DxfPlanReader.ReadSegments(sheet).Count(s => GridAlignment.LooksLikeAGridLayer(s.Layer));
            Console.WriteLine();
            Console.WriteLine($"{Path.GetFileName(sheet)}: {have.Count} named axes ({axes.Count(a => a.Vertical)} vertical, {axes.Count(a => !a.Vertical)} horizontal), {gridLines} grid-layer lines");
            Console.WriteLine($"   named by the model ({yes.Count}): {string.Join(" ", yes)}");
            Console.WriteLine($"   not named        ({no.Count}): {string.Join(" ", no)}");
            if (yes.Count < GridAlignment.LeastConvincingByName) unplaceable++;
        }
        Console.WriteLine();
        Console.WriteLine($"{args.Length - 2} sheet(s); {unplaceable} with fewer than {GridAlignment.LeastConvincingByName} axes the model names (the fewest a fit by name takes)");
        return 0;
    }
}
