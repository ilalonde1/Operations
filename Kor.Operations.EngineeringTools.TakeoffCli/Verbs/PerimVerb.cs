// The takeoff verb `perim`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Core-wall concrete the estimator's way: cross-reference the CORE WALL KEY PLAN (each mark's length)
// with the SHEAR WALL SCHEDULE (each mark's thickness per level band), priced over a level list.
// Diagnostic: run a vision schedule/keyplan reader on a rendered sheet and print the extracted JSON, to
// PROVE the dense wall/column schedules are machine-readable before wiring them into the takeoff.
// DIAGNOSTIC: takeoff perim <png> [scale] [dpi] — plate contour length off a rendered page (largest
// component, hairlines opened away) — validates the below-grade perimeter-wall measurement.
internal static class PerimVerb
{
    public static bool Matches(string[] args) => args.Length >= 2 && args[0].Equals("perim", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        string pscale = args.Length >= 3 ? args[2] : "1/8\"=1'-0\"";
        double pdpi = args.Length >= 4 && double.TryParse(args[3], out var pd) ? pd : 110;
        double? pmpp = PlanGeometry.MetresPerPixel(pscale, pdpi);
        if (pmpp is null) { Console.Error.WriteLine("bad scale"); return 2; }
        var pcrop = PlanRaster.LoadCrop(args[1], 0, 0, int.MaxValue / 2, int.MaxValue / 2);
        double m = PlanGeometry.BoundaryMetres(pcrop.Lum, pcrop.Width, pcrop.Height, pmpp.Value);
        Console.WriteLine($"{pcrop.Width}x{pcrop.Height}px  contour = {m * 3.2808399:N0} ft");
        return 0;
    }
}
