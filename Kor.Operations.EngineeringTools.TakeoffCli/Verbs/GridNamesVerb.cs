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

        // the model's members, for the registration a sheet with no shared name falls back to (step 55): its
        // column joints and its wall panels' two plan ends (a panel's four joints are two plan points, low and
        // high), in the model's unit; a sheet is read in its own unit and scaled to the model's
        var modelDoc = E2kDocument.Load(args[1]);
        double modelUnit = modelDoc.LengthUnitInInches() ?? 1.0;
        var modelPoints = modelDoc.PlanPointsOfObjects();
        var modelMembers = new List<DxfPoint>();
        foreach (string raw in modelDoc.LinesOf("LINE CONNECTIVITIES"))
        {
            var m = System.Text.RegularExpressions.Regex.Match(raw.TrimStart(), @"^LINE\s+""([^""]+)""\s+COLUMN\b");
            if (m.Success && modelPoints.TryGetValue(m.Groups[1].Value, out var cp) && cp.Count > 0) modelMembers.Add(new DxfPoint(cp[0].X, cp[0].Y));
        }
        foreach (string raw in modelDoc.LinesOf("AREA CONNECTIVITIES"))
        {
            var m = System.Text.RegularExpressions.Regex.Match(raw.TrimStart(), @"^AREA\s+""([^""]+)""\s+PANEL\b");
            if (m.Success && modelPoints.TryGetValue(m.Groups[1].Value, out var wp))
                modelMembers.AddRange(wp.Select(q => new DxfPoint(q.X, q.Y)).DistinctBy(q => (Math.Round(q.X, 3), Math.Round(q.Y, 3))));
        }

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

            // and where its members would stand over the model's (step 55): the second way a sheet is placed
            // the standing rules are stated in inches; the sheet is read in its own unit
            double sheetUnit = DxfPlanReader.UnitInInches(sheet) ?? 1.0;
            var members = StructuralPlanClassifier.MemberPoints(DxfPlanReader.ReadSegments(sheet), new PlanClassificationOptions().InUnitOf(sheetUnit));
            double toModel = sheetUnit / modelUnit;
            var byMembers = GridAlignment.SolveByColumns(members, modelMembers, toModel, out string why);
            Console.WriteLine($"   members: {members.Count} (column centres and wall axis ends); the model has {modelMembers.Count}: " +
                (byMembers is null ? why : $"{byMembers.Note} at ({byMembers.Frame.OffsetX:0}, {byMembers.Frame.OffsetY:0}) model units"));
            // a sheet the composer left in its own frame is in the model at (0, 0) already and registers on itself;
            // what the composer would do is the fit against the model WITHOUT what stands where this sheet sits
            if (byMembers is { } self && Math.Abs(self.Frame.OffsetX) <= GridAlignment.ColumnRegistrationMm * toModel && Math.Abs(self.Frame.OffsetY) <= GridAlignment.ColumnRegistrationMm * toModel)
            {
                double bin = GridAlignment.ColumnRegistrationMm * toModel;
                var others = modelMembers.Where(q => !members.Any(p => Math.Abs(q.X - p.X * toModel) <= bin && Math.Abs(q.Y - p.Y * toModel) <= bin)).ToList();
                var elsewhere = GridAlignment.SolveByColumns(members, others, toModel, out string whyNot);
                // audit F21 (step 61): a fit at (0, 0) MAY be the sheet standing on itself where the composer left it, or a
                // sheet rightly placed over members that stand exactly under it; the members are taken out by position,
                // not by which sheet drew them, so the second fit is a question, not a diagnosis
                Console.WriteLine($"   ... a fit at (0, 0) is either this sheet standing on itself where the composer left it, or a sheet rightly placed over what stands under it - the model does not say which sheet drew a member; without the {modelMembers.Count - others.Count} members at its own positions, against the other {others.Count}: " +
                    (elsewhere is null ? whyNot : $"{elsewhere.Note} at ({elsewhere.Frame.OffsetX:0}, {elsewhere.Frame.OffsetY:0}) model units"));
            }
        }
        Console.WriteLine();
        Console.WriteLine($"{args.Length - 2} sheet(s); {unplaceable} with fewer than {GridAlignment.LeastConvincingByName} axes the model names (the fewest a fit by name takes)");
        return 0;
    }
}
