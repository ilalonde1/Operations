// Where on the SHEET is this model point? Maps e2k model coordinates back to a sheet's DXF frame by the
// grid: the model's GRID lines carry each axis's model coordinate, the sheet's DXF carries the same axis
// names at sheet coordinates (GridAlignment.NamedAxes), so the shift between the two frames is the median
// difference over the shared names — the alignment model-diff uses between two models. Prints the shift,
// how many names agreed, every name that disagrees by more than 50 mm (a sheet the model placed by a
// different reference plan gets the WRONG shift, and this is how that shows), and each point in the
// sheet's frame; `takeoff pdf-overlay --mark x y` then draws it on the page. Built 2026-09-11 to look at
// the walls the audit fixes returned to 31138's L1/L2 (four 1,414 mm diagonals in a row): the model says
// where they are, the sheet says what they are. Ported from docs/etabs-handoff/model_to_page.py (WP2);
// the script's second join through an overlay wall census is not carried — --mark takes sheet mm directly.
//   takeoff model-to-page <model.e2k> <sheet.dxf> <x> <y> [<x> <y> ...]     (model units; the DXF is mm)
internal static class ModelToPageVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("model-to-page", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 5 || (args.Length - 3) % 2 != 0) { Console.Error.WriteLine("Usage: takeoff model-to-page <model.e2k> <sheet.dxf> <x> <y> [<x> <y> ...]"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"Model not found '{args[1]}'."); return 2; }
        if (!File.Exists(args[2])) { Console.Error.WriteLine($"Sheet not found '{args[2]}'."); return 2; }
        var doc = E2kDocument.Load(args[1]);
        double unitMm = (doc.LengthUnitInInches() ?? 1.0) * 25.4;
        var grids = doc.ReadGrids();
        var axes = GridAlignment.NamedAxes(DxfPlanReader.ReadSegments(args[2]), DxfPlanReader.ReadPositionedTags(args[2]));
        var points = new List<(double X, double Y)>();
        for (int i = 3; i + 1 < args.Length; i += 2)
            points.Add((double.Parse(args[i], NumberStyles.Float, CultureInfo.InvariantCulture), double.Parse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture)));

        // an X-direction axis in the model is a vertical line on the sheet: its constant is an x
        var dx = new List<(string Label, double D)>(); var dy = new List<(string Label, double D)>();
        foreach (var ((_, label, dir), coord) in grids)
        {
            bool vertical = dir == "X";
            var axis = axes.FirstOrDefault(a => a.Vertical == vertical && a.Name.Equals(label, StringComparison.OrdinalIgnoreCase));
            if (axis is null) continue;
            (vertical ? dx : dy).Add((label, coord * unitMm - axis.At));
        }
        if (dx.Count == 0 || dy.Count == 0)
        {
            Console.WriteLine($"no shared names on both axes: the model has {grids.Count} axes, the sheet names {axes.Count} ({string.Join(" ", axes.Select(a => a.Name).Distinct().Take(12))}); X shared {dx.Count}, Y shared {dy.Count}");
            return 1;
        }
        double sx = Median(dx.Select(d => d.D)), sy = Median(dy.Select(d => d.D));
        Console.WriteLine($"model = sheet + ({sx:0}, {sy:0}) mm, from {dx.Count} X and {dy.Count} Y names (model unit {unitMm:0.##} mm)");
        foreach (var (label, d) in dx) if (Math.Abs(d - sx) > 50) Console.WriteLine($"   X axis {label} disagrees by {d - sx:0} mm");
        foreach (var (label, d) in dy) if (Math.Abs(d - sy) > 50) Console.WriteLine($"   Y axis {label} disagrees by {d - sy:0} mm");
        // the scratch DXF banks the page origin in its own frame ($INSBASE), so the page's millimetres —
        // what pdf-overlay --mark draws at — follow without a second alignment
        var origin = DxfPlanReader.PageOriginInDrawing(args[2]);
        Console.WriteLine(origin is { } o
            ? $"the sheet states its page origin at ({o.X:0}, {o.Y:0}); page = sheet - that"
            : "the sheet does not state its page origin (no $INSBASE): page millimetres not available");
        foreach (var (x, y) in points)
        {
            double sxMm = x * unitMm - sx, syMm = y * unitMm - sy;
            Console.WriteLine($"model ({x:0}, {y:0}) -> sheet ({sxMm:0}, {syMm:0}) mm" + (origin is { } og ? $" -> page ({sxMm - og.X:0}, {syMm - og.Y:0}) mm" : ""));
        }
        return 0;

        static double Median(IEnumerable<double> values)
        {
            var v = values.OrderBy(d => d).ToList();
            return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
        }
    }
}
