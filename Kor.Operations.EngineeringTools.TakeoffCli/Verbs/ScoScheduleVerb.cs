// The takeoff verb `sco-schedule`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// What a project's column design says, read from the S-Concrete files themselves — including where
// they disagree. Usage: takeoff sco-schedule <folder|file.SCO ...> <out.xlsx>
internal static class ScoScheduleVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("sco-schedule", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff sco-schedule <folder|file.SCO> [more...] <out.xlsx>"); return 1; }

        string scoOut = args[^1];
        var scoFiles = new List<string>();
        foreach (string a in args[1..^1])
        {
            if (Directory.Exists(a)) scoFiles.AddRange(Directory.EnumerateFiles(a, "*.SCO", SearchOption.AllDirectories));
            else if (File.Exists(a)) scoFiles.Add(a);
            else { Console.Error.WriteLine($"Not found '{a}'."); return 2; }
        }
        if (scoFiles.Count == 0) { Console.Error.WriteLine("No .SCO files found."); return 2; }

        var scoReport = ColumnDemandSchedule.Read(scoFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        if (scoReport.Columns.Count == 0)
        {
            Console.Error.WriteLine($"No column demands in {scoFiles.Count} file(s). Are they S-Concrete files?");
            return 3;
        }

        File.WriteAllBytes(scoOut, ColumnDemandSchedule.BuildXlsx(scoReport, Path.GetFileNameWithoutExtension(scoOut)));

        Console.WriteLine($"\n{scoReport.FilesRead} S-Concrete file(s) — {scoReport.DemandsRead} demands on {scoReport.Columns.Count} columns.");

        Console.WriteLine($"\n{"Storey",-8} {"Mark",-6} {"Section",-12} {"kl",8} {"Cases",6} {"Max comp",11} {"Max Mfy",10}");
        foreach (var c in scoReport.Columns.Take(12))
            Console.WriteLine($"{c.Storey,-8} {c.Mark,-6} {c.Section,-12} {(c.EffectiveLength is double k ? k.ToString("N3") : "—"),8} {c.Cases,6} {c.MaxCompression,11:N1} {c.MaxMfy,10:N1}");
        if (scoReport.Columns.Count > 12) Console.WriteLine($"   … and {scoReport.Columns.Count - 12} more.");

        if (scoReport.Truncated.Count > 0)
            Console.WriteLine($"\n{scoReport.Truncated.Count} demand(s) have NO effective length recorded — S-Concrete truncates the "
                + "comment at about sixty characters, and a long section name pushes kl off the end.");

        if (scoReport.FromFilenameOnly > 0)
            Console.WriteLine($"\n{scoReport.FromFilenameOnly} demand(s) carry no identity inside the file at all — the comment is "
                + "blank, so the member is taken from the FILENAME and the load case is its row number. "
                + "Rename a file on this job and its design becomes untraceable.");

        if (scoReport.MaterialConflicts > 0)
        {
            Console.WriteLine($"\n{scoReport.MaterialConflicts} column/case demand(s) are stated DIFFERENTLY in two files by more than 1%"
                + (scoReport.TrivialConflicts > 0 ? $" (and {scoReport.TrivialConflicts} more by less, which is rounding)" : "")
                + ". Which file is current is an engineering call — these are the pairs to settle:");

            foreach (var p in scoReport.ConflictingPairs.Take(6))
            {
                Console.WriteLine($"\n   {p.Demands,4} demands apart by up to {p.WorstPercent:N0}%");
                Console.WriteLine($"        {p.FileA}");
                Console.WriteLine($"        {p.FileB}");
            }
            if (scoReport.ConflictingPairs.Count > 6)
                Console.WriteLine($"\n   … and {scoReport.ConflictingPairs.Count - 6} more pairs.");
        }
        else Console.WriteLine("\nNo two files disagree about any column by more than rounding.");

        Console.WriteLine($"\n  ->  {scoOut}");
        return 0;
    }
}
