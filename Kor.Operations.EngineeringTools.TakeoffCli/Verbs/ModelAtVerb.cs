// WHAT THE MODEL HOLDS NEAR A POINT (2026-09-16): every object of an .e2k within a reach of a plan point, nearest
// first, with its kind, its ends and the storeys it rises through. The other half of the question dxf-inspect --near
// answers about the drawing; asked twice as a scratch script (31168's KW235, 31138's paired faces) before it was a verb.
// Usage: takeoff model-at <model.e2k> <x> <y> [reach]   (reach defaults to 60 in the model's unit)
internal static class ModelAtVerb
{
    public static bool Matches(string[] args) => args.Length >= 4 && args[0].Equals("model-at", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"Not found '{args[1]}'."); return 2; }
        if (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
            || !double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
        { Console.Error.WriteLine("Usage: takeoff model-at <model.e2k> <x> <y> [reach]"); return 1; }
        double reach = args.Length > 4 && double.TryParse(args[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : 60.0;

        var doc = E2kDocument.Load(args[1]);
        var near = E2kModelQuery.Near(doc, x, y, reach);
        Console.WriteLine($"{Path.GetFileName(args[1])}: {near.Count} object(s) within {reach} of ({x:0.#},{y:0.#}), by kind: " +
            string.Join(", ", near.GroupBy(o => o.Kind).Select(g => $"{g.Key} {g.Count()}")));
        foreach (var o in near)
        {
            // a PANEL names its two joints twice (A B B A): the distinct joints are the ends
            var pts = o.Points.Distinct().ToList();
            string ends = pts.Count == 1 ? $"at ({pts[0].X:0.#},{pts[0].Y:0.#})"
                : $"({pts[0].X:0.#},{pts[0].Y:0.#})-({pts[^1].X:0.#},{pts[^1].Y:0.#})" + (pts.Count > 2 ? $" {pts.Count} pts" : "");
            double len = pts.Count == 2 ? Math.Sqrt(Math.Pow(pts[0].X - pts[1].X, 2) + Math.Pow(pts[0].Y - pts[1].Y, 2)) : 0;
            Console.WriteLine($"  {o.Name,-10} {o.Kind,-7} d {o.Distance,6:0.#}  {ends}" + (pts.Count == 2 ? $" len {len:0.#}" : "") +
                $"  storeys {(o.Storeys.Count == 0 ? "(none)" : string.Join(",", o.Storeys))}");
        }
        return 0;
    }
}
