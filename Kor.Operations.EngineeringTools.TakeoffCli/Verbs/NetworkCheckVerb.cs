// THE WALL NETWORK'S INVARIANT OVER EVERY SHEET OF A SET OR A CORPUS (2026-10-27, Mistakes 64-65, board 54). WallNetworkInvariant
// holds each sheet's network against the walls it was given: no wall tilted (TILT), no header off its jambs' axis (HEADER), no
// short wall joined only to a near-copy of itself (SELF). 0f3e4ee4 broke all three on the metric gate sets and was committed on a
// check of one building's core; this is the check of every sheet at once.
//
// Usage: takeoff network-check <dir> [<dir> ...] [--jobs a,b] [--model-unit mm|in|ft] [--examples N]
//   <dir>        a corpus-gate --work dir (<job>\dxf\*.dxf), or a folder of DXFs (one set, named by the folder)
//   --model-unit the unit the service would classify in (default: each drawing's own); Halifax is built in ft
// Exit 1 when any sheet breaks it, 0 when none does, 2 on bad arguments.
//
// ⚠ IT CLASSIFIES WITH DEFAULT PlanClassificationOptions - no KorStandards rules, no sheet info, no tags - so it reads nothing
// over the network and runs when the rules DB is down; a rule that changes which elements are walls (layers, minimum length) is
// not applied here. The network's moves do not depend on those rules, the population it moves does.
internal static class NetworkCheckVerb
{
    public static bool Matches(string[] args) => args.Length >= 2 && args[0].Equals("network-check", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        var dirs = new List<string>(); HashSet<string>? jobs = null; double? modelUnit = null; int examples = 3;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].Equals("--jobs", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                jobs = new HashSet<string>(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
            else if (args[i].Equals("--model-unit", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                modelUnit = args[++i].ToLowerInvariant() switch { "mm" => 1.0 / 25.4, "in" => 1.0, "ft" => 12.0, _ => double.NaN };
                if (double.IsNaN(modelUnit.Value)) { Console.Error.WriteLine("--model-unit wants mm, in or ft"); return 2; }
            }
            else if (args[i].Equals("--examples", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) examples = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else dirs.Add(args[i]);
        }
        if (dirs.Count == 0) { Console.Error.WriteLine("network-check <dir> [<dir> ...] [--jobs a,b] [--model-unit mm|in|ft]"); return 2; }

        var sets = new List<(string Job, string Folder)>();
        foreach (string dir in dirs)
        {
            if (!Directory.Exists(dir)) { Console.Error.WriteLine($"no such folder: {dir}"); return 2; }
            if (Directory.EnumerateFiles(dir, "*.dxf").Any()) sets.Add((Path.GetFileName(Path.GetFullPath(dir).TrimEnd('\\', '/')), dir));
            else
                foreach (string job in Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.Ordinal))
                    if (Directory.Exists(Path.Combine(job, "dxf")) && (jobs is null || jobs.Contains(Path.GetFileName(job))))
                        sets.Add((Path.GetFileName(job), Path.Combine(job, "dxf")));
        }

        int sheets = 0, broken = 0; var total = new Dictionary<string, int> { ["TILT"] = 0, ["HEADER"] = 0, ["SELF"] = 0 };
        int setsBroken = 0;
        foreach (var (job, folder) in sets)
        {
            var mine = new Dictionary<string, int> { ["TILT"] = 0, ["HEADER"] = 0, ["SELF"] = 0 };
            var shown = new List<string>();
            foreach (string file in Directory.EnumerateFiles(folder, "*.dxf").OrderBy(f => f, StringComparer.Ordinal))
            {
                double drawing = DxfPlanReader.UnitInInches(file) ?? 1.0;
                double model = modelUnit ?? drawing;
                double scale = drawing / model;
                var segments = DxfPlanReader.ReadSegments(file)
                    .Select(s => Math.Abs(scale - 1.0) < 1e-12 ? s : new DxfSegment(s.Layer, new DxfPoint(s.Start.X * scale, s.Start.Y * scale), new DxfPoint(s.End.X * scale, s.End.Y * scale)))
                    .ToList();
                var options = Math.Abs(model - 1.0) < 1e-12 ? new PlanClassificationOptions() : new PlanClassificationOptions().InUnitOf(model);
                var geometry = StructuralPlanClassifier.Classify(segments, options, null, null);
                var found = WallNetworkInvariant.Check(geometry, options.MinWallLength, 1.0 / options.UnitInInches);
                sheets++;
                if (found.Count > 0) broken++;
                foreach (var v in found)
                {
                    mine[v.Kind]++;
                    if (shown.Count(s => s.StartsWith(v.Kind, StringComparison.Ordinal)) < examples)
                        shown.Add($"{v.Kind,-6} {Path.GetFileName(file)}: ({v.Start.X * model * 25.4:0}, {v.Start.Y * model * 25.4:0})-({v.End.X * model * 25.4:0}, {v.End.Y * model * 25.4:0}) mm  {v.Why}");
                }
            }
            int n = mine.Values.Sum();
            if (n > 0) setsBroken++;
            foreach (var k in mine.Keys) total[k] += mine[k];
            Console.WriteLine($"{job}: {(n == 0 ? "holds" : $"TILT {mine["TILT"]}, HEADER {mine["HEADER"]}, SELF {mine["SELF"]}")}");
            foreach (string s in shown) Console.WriteLine("    " + s);
        }
        Console.WriteLine();
        Console.WriteLine($"{sets.Count} set(s), {sheets} sheet(s): {setsBroken} set(s) and {broken} sheet(s) break the network invariant - TILT {total["TILT"]}, HEADER {total["HEADER"]}, SELF {total["SELF"]}");
        return total.Values.Sum() == 0 ? 0 : 1;
    }
}
