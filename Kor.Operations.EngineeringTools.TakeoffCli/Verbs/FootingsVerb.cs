// The takeoff verb `footings`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// DIAGNOSTIC: takeoff footings <pdf> [first] [last] — run the deterministic footing-schedule takeoff
// standalone: schedule rows, per-mark plan placements, priced spread-footing volumes.
internal static class FootingsVerb
{
    public static bool Matches(string[] args) => args.Length >= 2 && args[0].Equals("footings", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        int ff = args.Length >= 3 && int.TryParse(args[2], out var fa) ? fa : 1;
        int fl = args.Length >= 4 && int.TryParse(args[3], out var fb) ? fb : ff + 199;
        double total = 0;
        for (int pg = ff; pg <= fl; pg++)
        {
            VectorPageReader.PageContent pc; try { pc = VectorPageReader.ReadPage(args[1], pg); } catch { break; }
            if (pc.Words.Count == 0) continue;
            string ds = string.Concat(string.Join(" ", pc.Words.Select(w => w.Text)).ToUpperInvariant().Where(c => !char.IsWhiteSpace(c)));
            // a sheet that schedules a RAFT, a MAT or PILES has said what its foundation is, and a
            // total of 0 spread footings should say so rather than say nothing (31202 is a raft)
            if (!ds.Contains("FOUNDATIONSCHEDULE") && !ds.Contains("FOOTINGSCHEDULE")
                && !ds.Contains("RAFTSLAB") && !ds.Contains("MATFOUNDATION") && !ds.Contains("MATSLAB") && !ds.Contains("PILESCHEDULE")) continue;
            var (ftypes, box) = FootingScheduleReader.ReadSchedule(pc);
            if (ftypes.Count == 0) { Console.WriteLine($"p{pg}: no footing rows — {FootingScheduleReader.WhyNoRows(pc)}"); continue; }
            var counts = FootingScheduleReader.CountPlacements(pc, ftypes, box, SheetFurniture.On(pc, PlanAgreesWithItsSchedule.DefaultToleranceMm));
            string lvl = SheetTitleReader.FromPage(pc)?.Display ?? "?";
            Console.WriteLine($"p{pg} ({lvl}): {ftypes.Count} schedule row(s)");
            foreach (var ft in ftypes)
            {
                int n = counts.GetValueOrDefault(ft.Mark);
                if (ft.IsSpread)
                {
                    double cy = n * ft.VolumeCuYdEach;
                    total += cy;
                    Console.WriteLine($"    {ft.Mark,-4} {ft.LengthMm,5:0}x{ft.WidthMm,4:0}x{ft.DepthMm,4:0} DEEP  x{n,3}  = {cy,7:N1} cy");
                }
                else Console.WriteLine($"    {ft.Mark,-4} {ft.WidthMm,5:0}x{ft.DepthMm,4:0} DEEP (STRIP) x{n,3}  — length on plan, residual");
            }
        }
        Console.WriteLine($"TOTAL spread footings: {total:N0} cy");
        return 0;
    }
}
