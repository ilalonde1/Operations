using Kor.Operations.EngineeringTools;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;

// WHAT HAPPENED TO THE INK HERE. Every path and word of a page within a radius of a point, each with the
// fate the intake gave it - the ledger pdf-inventory sums, opened at one spot. Built 2026-09-12 when 11 of
// 55 tendon anchors on 31202's L7-12 plan were still read as columns and the census said only "nearest
// tendon end 1,696 mm": the page draws a 16 m tendon from that anchor (vector-lines), so the question was
// which fate the intake gave that line, and nothing answered it at a point. The point is in the frame
// pdf-overlay's --mark and model-to-page use: page millimetres at the sheet's scale, y up.
//   takeoff pdf-at <pdf> <page> <x> <y> --scale N [--radius mm] [--rules-db <conn>] [--points]
internal static class PdfAtVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("pdf-at", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 5) { Console.Error.WriteLine("Usage: takeoff pdf-at <pdf> <page> <x> <y> --scale N [--radius mm] [--rules-db <conn>]"); return 1; }
        string pdf = args[1];
        if (!File.Exists(pdf)) { Console.Error.WriteLine($"PDF not found '{pdf}'."); return 2; }
        if (!int.TryParse(args[2], out int page) || page < 1) { Console.Error.WriteLine("Page must be a positive integer."); return 2; }
        if (!double.TryParse(args[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double atX)
            || !double.TryParse(args[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double atY))
        { Console.Error.WriteLine("x and y are page millimetres (the frame of pdf-overlay --mark)."); return 2; }
        int scale = 0; double radius = 600; string? rules = null;
        bool points = false;   // --points: each path's vertices, in drawing millimetres (what a 4-point path that reads as three corners actually is)
        for (int i = 5; i < args.Length; i++)
        {
            if (args[i].Equals("--scale", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) int.TryParse(args[++i], out scale);
            else if (args[i].Equals("--radius", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) double.TryParse(args[++i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out radius);
            else if (args[i].Equals("--rules-db", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) rules = args[++i];
            else if (args[i].Equals("--points", StringComparison.OrdinalIgnoreCase)) points = true;
        }
        if (scale <= 0) { Console.Error.WriteLine("--scale <denominator> is required (1/8\" = 1'-0\" is 96)."); return 2; }

        var (options, rulesSource) = PdfIntakeOptions.For(rules);
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var facts = DocumentFacts.From(doc);
        var record = DrawingIntake.ReadSheet(doc, page, new IntakeRequest(scale, options), facts);
        double mmPerPt = scale * 25.4 / 72.0;
        var fateOf = record.PathFates.ToDictionary(f => f.PathIndex, f => f);
        var wordFateOf = record.WordFates.ToDictionary(f => f.WordIndex, f => f);
        double ax = atX / mmPerPt, ay = atY / mmPerPt, r = radius / mmPerPt;

        Console.WriteLine($"{Path.GetFileName(pdf)} p{page} 1:{scale} rules: {rulesSource}   at ({atX:0}, {atY:0}) mm = ({ax:0.0}, {ay:0.0}) pt, within {radius:0} mm");
        static double ToBox(double x, double y, double x0, double y0, double x1, double y1)
        {
            double dx = Math.Max(Math.Max(x0 - x, 0), x - x1), dy = Math.Max(Math.Max(y0 - y, 0), y - y1);
            return Math.Sqrt(dx * dx + dy * dy);
        }
        var paths = record.Content.Paths;
        int shown = 0;
        for (int i = 0; i < paths.Count; i++)
        {
            var p = paths[i];
            double d = ToBox(ax, ay, p.MinX, p.MinY, p.MaxX, p.MaxY);
            if (d > r) continue;
            shown++;
            string fate = fateOf.TryGetValue(i, out var f) ? $"{f.Disposition} {f.Reason}{(f.ObjectIndex is int oi ? $" #{oi}" : "")}" : "(no fate: annotation, clip or no ink)";
            string ink = p.IsClipping ? "clip" : (p.IsFilled ? "filled" : "") + (p.IsFilled && p.IsStroked ? "+" : "") + (p.IsStroked ? $"stroked w{p.LineWidth:0.##}pt" : "") + (p.IsAnnotation ? " annotation" : "");
            Console.WriteLine($"  path #{i,-6} {fate,-42} {ink,-28} {(p.IsClosed ? "closed" : "open"),-6} {p.Points.Count,4} pts  box {p.Width * mmPerPt:0}x{p.Height * mmPerPt:0} mm at ({p.MinX * mmPerPt:0},{p.MinY * mmPerPt:0})  {d * mmPerPt:0} mm away  #{p.Color.R:X2}{p.Color.G:X2}{p.Color.B:X2}");
            if (points) Console.WriteLine("           " + string.Join(" ", p.Points.Select(q => $"({q.X * mmPerPt:0.#},{q.Y * mmPerPt:0.#})")));
        }
        Console.WriteLine($"  {shown} of {paths.Count} paths within {radius:0} mm");
        int words = 0;
        for (int i = 0; i < record.Content.Words.Count; i++)
        {
            var w = record.Content.Words[i];
            double d = ToBox(ax, ay, w.MinX, w.MinY, w.MaxX, w.MaxY);
            if (d > r) continue;
            words++;
            string fate = wordFateOf.TryGetValue(i, out var wf) ? $"{wf.Disposition} {wf.Kind}: {wf.Reason}" : "(no fate)";
            Console.WriteLine($"  word \"{w.Text}\" h{w.Height * mmPerPt / scale:0.#}mm at ({w.Cx * mmPerPt:0},{w.Cy * mmPerPt:0})  {d * mmPerPt:0} mm away  {fate}");
        }
        Console.WriteLine($"  {words} of {record.Content.Words.Count} words within {radius:0} mm");
        return 0;
    }
}
