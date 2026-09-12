// The takeoff verb `model-render`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// EVERY STOREY OF A MODEL ON ONE SHEET, so a person can LOOK (ModelRender, ported 2026-09-11 from
// plan_sheet.py and render_storeys.sh). SVG always; PNG through Edge when it is there.
// Usage: takeoff model-render <model.e2k> <out.png|out.svg> ["title"] [--columns N] [--cell px] [--no-png]
internal static class ModelRenderVerb
{
    public static bool Matches(string[] args) => args.Length >= 3 && args[0].Equals("model-render", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"no such model: {args[1]}"); return 1; }
        string mrTitle = args.Length >= 4 && !args[3].StartsWith("--", StringComparison.Ordinal) ? args[3] : Path.GetFileName(args[1]);
        int mrColumns = 3, mrCell = 600; bool mrPng = true;
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i].Equals("--columns", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) mrColumns = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i].Equals("--cell", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) mrCell = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i].Equals("--no-png", StringComparison.OrdinalIgnoreCase)) mrPng = false;
        }
        var (mrSvg, mrPngPath, mrDrawn) = ModelRender.Write(args[1], args[2], mrTitle, mrColumns, mrCell, mrPng);
        Console.WriteLine($"{mrSvg}  ({mrDrawn} storeys drawn)");
        if (mrPng) Console.WriteLine(mrPngPath ?? "Edge wrote no PNG (is Edge installed?); the SVG stands");
        return mrPng && mrPngPath is null ? 2 : 0;
    }
}
