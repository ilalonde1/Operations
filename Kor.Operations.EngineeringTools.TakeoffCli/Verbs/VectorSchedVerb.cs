// The takeoff verb `vector-sched`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Schedule grid reconstruction probe: from the native vector tokens, recover the level ladder and the
// thickness cells (each resolved to its level row). Usage: takeoff vector-sched <pdf> <page>
internal static class VectorSchedVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("vector-sched", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff vector-sched <pdf> <page>"); return 1; }
        // The COLUMN schedule alongside the shear-wall one, read the same deterministic way. The only
        // column-schedule reader before this was `sched-read column`, which is an AI read of a
        // downscaled PNG of text this project can read exactly, for free, and the same way twice.
        try
        {
            var colPage = VectorPageReader.ReadPage(args[1], int.Parse(args[2], CultureInfo.InvariantCulture));
            var colRows = ColumnScheduleReader.ReadSchedule(colPage);
            Console.WriteLine($"Column schedule ({colRows.Count} mark(s)):");
            foreach (var r in colRows)
                Console.WriteLine($"  {r.Mark,-6} {r.WidthMm / PrintedLength.MmPerInch,5:0.#}\" x " +
                                  $"{r.DepthMm / PrintedLength.MmPerInch,5:0.#}\"" +
                                  $"{(r.StrengthMPa is double mpa ? $"  {mpa:0} MPa" : "")}" +
                                  $"{(r.Reinforcing is null ? "" : "  " + r.Reinforcing)}" +
                                  $"{(r.Ties is null ? "" : "  | " + r.Ties)}" +
                                  $"  [{r.Route}]");
            Console.WriteLine();
        }
        catch (Exception ex) { Console.WriteLine($"Column schedule: unreadable — {ex.GetType().Name}"); }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
        if (!int.TryParse(args[2], out int schPage) || schPage < 1) { Console.Error.WriteLine("Page must be a positive integer."); return 2; }

        var page = VectorPageReader.ReadPage(args[1], schPage);
        var ladder = ScheduleGridReader.ReadLevelLadder(page);
        var cells  = ScheduleGridReader.ReadThicknessCells(page);

        Console.WriteLine($"Level ladder ({ladder.Count} rows, top->bottom):");
        Console.WriteLine("  " + string.Join("  ", ladder.Select(r => r.Normalized)));
        Console.WriteLine($"Thickness cells ({cells.Count}):");
        foreach (var c in cells)
            Console.WriteLine($"    {c.ThicknessIn,2:F0}\" WALL  @ {c.Level,-6} (x={c.X:F0})");

        var bands = ScheduleGridReader.ReadWallBands(page);
        Console.WriteLine($"Wall bands ({bands.Count}) — mark: top..bottom = thickness:");
        foreach (var b in bands)
            Console.WriteLine($"    {b.Mark,-4} {b.LevelTop,-4}..{b.LevelBottom,-4} = {b.ThicknessIn:F0}\"");

        var flatRows = ScheduleGridReader.ReadFlatWallRows(page);
        Console.WriteLine($"Flat wall rows ({flatRows.Count}) — mark: thickness, strength:");
        foreach (var r in flatRows)
            Console.WriteLine($"    {r.Mark,-4} {r.ThicknessIn:F0}\"{(r.StrengthMPa is double mpa ? $"  {mpa:F0} MPa" : "")}  [{r.Route}]");
        return 0;
    }
}
