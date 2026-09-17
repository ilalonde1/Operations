using System.Globalization;
using System.Text.RegularExpressions;


// THE ENGINEERS' REVIEW, FROM THE CORPUS (2026-09-16, plan WP6a item 8 re-planned on Ian's challenge: "with our
// extensive corpus we should be able to synthesize ALL of the info we have"). Fifty-odd sets carry the engineer's
// own ETABS model beside the one this route builds from the drawings, and the analyzer already writes each
// comparison to <job>/yardstick.txt - per storey, per section, per column. Nobody had read them across the
// corpus. This reads all of them and says, ranked, where the two models disagree and how often: the storeys one
// names and the other does not, the storeys whose columns sit a consistent distance apart (a frame or a reading),
// the sections we place that she does not (a wall she models as a pier, an anchor we read as a column) and the
// sections she places that we miss. Each class with its count over the corpus and the sets to open first. That
// list is the backlog; it needs no meeting.
//   takeoff corpus-disagreements [--ledger <dir>] [--top N]
internal static class CorpusDisagreementsVerb
{
    private sealed record StoreyRow(string Job, string Ours, string Theirs, int OursJudged, int TheirsCount, double MedianMm, int OursWithin, int TheirsWithin, double RigidX, double RigidY, double AfterMm, int AfterWithin, int Beyond);

    public static int Run(string[] args)
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "kor-drawings", "corpus");
        int top = 12;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].Equals("--ledger", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) root = args[++i];
            else if (args[i].Equals("--top", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) top = int.Parse(args[++i], CultureInfo.InvariantCulture);
        }
        var files = Directory.EnumerateDirectories(root).Select(d => Path.Combine(d, "yardstick.txt")).Where(File.Exists).OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (files.Count == 0) { Console.Error.WriteLine($"no yardstick.txt under {root}"); return 1; }

        var storeys = new List<StoreyRow>();
        var onlyOurs = new List<(string Job, string Name)>();
        var onlyTheirs = new List<(string Job, string Name)>();
        var oursNotTheirs = new List<(string Job, string Section, int Count)>();
        var theirsNotOurs = new List<(string Job, string Section, int Count)>();
        var pierToHer = new List<(string Job, int Count)>();
        var beyondFootprint = new List<(string Job, int Count)>();
        var frames = new List<(string Job, bool Grids, int Within, int Of)>();
        var olderBy = new Dictionary<string, int>();   // her model's age against the drawing's issue, in days, where the yardstick says
        // openings, ours against hers (step 104's yardstick line): judged, hers, matched each way; the unmatched by plan size
        var openings = new List<(string Job, int Ours, int Theirs, int OursMatched, int TheirsMatched)>();
        var oursOpeningsNotHers = new List<(string Job, string Size, int Count)>();
        var hersOpeningsNotOurs = new List<(string Job, string Size, int Count)>();
        var openingSize = new Regex(@"(\S+x\S+ m) (\d+)");
        var storeyLine = new Regex(@"^\s+(\S+)\s+(\S+)\s+(\d+)\s+(\d+)\s+([\d,]+) mm\s+(\d+)%\s+(\d+)%\s+rigid \(\s*(-?[\d,]+),\s*(-?[\d,]+)\)\s*->\s*([\d,]+) mm\s+(\d+)%\s+beyond (\d+)");
        var sectionCount = new Regex(@"(\S+?) (\d+)(?:,|\s*\.\.\.|$)");
        foreach (var f in files)
        {
            string job = Path.GetFileName(Path.GetDirectoryName(f)!);
            foreach (var raw in File.ReadLines(f))
            {
                string line = raw.TrimEnd();
                var m = storeyLine.Match(line);
                if (m.Success)
                {
                    storeys.Add(new StoreyRow(job, m.Groups[1].Value, m.Groups[2].Value, N(m.Groups[3].Value), N(m.Groups[4].Value), D(m.Groups[5].Value), N(m.Groups[6].Value), N(m.Groups[7].Value),
                        D(m.Groups[8].Value), D(m.Groups[9].Value), D(m.Groups[10].Value), N(m.Groups[11].Value), N(m.Groups[12].Value)));
                    continue;
                }
                if (line.StartsWith("storeys:", StringComparison.Ordinal))
                {
                    var mo = Regex.Match(line, @"only ours: ([^;]*)"); if (mo.Success) onlyOurs.AddRange(mo.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(n => (job, n)));
                    var mt = Regex.Match(line, @"only theirs: (.*)$"); if (mt.Success) onlyTheirs.AddRange(mt.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(n => (job, n)));
                    continue;
                }
                if (line.StartsWith("ours with none of theirs within 300 mm, by section:", StringComparison.Ordinal))
                { foreach (Match s in sectionCount.Matches(line["ours with none of theirs within 300 mm, by section:".Length..])) oursNotTheirs.Add((job, s.Groups[1].Value, N(s.Groups[2].Value))); continue; }
                if (line.StartsWith("theirs with none of ours within 300 mm, by their section:", StringComparison.Ordinal))
                { foreach (Match s in sectionCount.Matches(line["theirs with none of ours within 300 mm, by their section:".Length..])) theirsNotOurs.Add((job, s.Groups[1].Value, N(s.Groups[2].Value))); continue; }
                var mp = Regex.Match(line, @"of which (\d+) stand on a wall she modelled");
                if (mp.Success) { pierToHer.Add((job, N(mp.Groups[1].Value))); continue; }
                var mb = Regex.Match(line, @"note: (\d+) of our columns stand beyond her model's footprint");
                if (mb.Success) { beyondFootprint.Add((job, N(mb.Groups[1].Value))); continue; }
                var ma = Regex.Match(line, @"^her model: .*?written \d{4}-\d{2}-\d{2}, (-?\d+) days before the drawing's issue");
                if (ma.Success) { olderBy[job] = N(ma.Groups[1].Value); continue; }
                var mf = Regex.Match(line, @"^frames matched by (column registration|grid labels).*?(\d+) of (\d+) of our columns within 100 mm");
                if (mf.Success) { frames.Add((job, mf.Groups[1].Value.StartsWith("grid", StringComparison.Ordinal), N(mf.Groups[2].Value), N(mf.Groups[3].Value))); continue; }
                var mop = Regex.Match(line, @"^openings on the shared storeys: ours (\d+) inside her footprint.*?, hers (\d+); ours with one of hers within [\d.]+ m (\d+) \(\d+%\); hers with one of ours (\d+)");
                if (mop.Success) { openings.Add((job, N(mop.Groups[1].Value), N(mop.Groups[2].Value), N(mop.Groups[3].Value), N(mop.Groups[4].Value))); continue; }
                if (line.StartsWith("  ours she has not, by size:", StringComparison.Ordinal))
                { foreach (Match s in openingSize.Matches(line)) oursOpeningsNotHers.Add((job, s.Groups[1].Value, N(s.Groups[2].Value))); continue; }
                if (line.StartsWith("  hers we have not, by size:", StringComparison.Ordinal))
                { foreach (Match s in openingSize.Matches(line)) hersOpeningsNotOurs.Add((job, s.Groups[1].Value, N(s.Groups[2].Value))); continue; }
            }
        }

        Console.WriteLine($"the engineers' review from the corpus: {files.Count} sets carry her model; {storeys.Select(s => s.Job).Distinct().Count()} share storeys with columns ({storeys.Count} storey pairs)");
        Console.WriteLine();

        // 1. the frame: sets where the two models' columns sit far apart on most shared storeys
        Console.WriteLine("1. WHERE THE COLUMNS SIT, storey by storey (median distance from each of ours to the nearest of hers):");
        var bins = new[] { ("within 100 mm - the same reading", 0.0, 100.0), ("100-300 mm - a centroid against a grid point", 100.0, 300.0), ("300 mm-1 m - a reading apart", 300.0, 1000.0), ("over 1 m - another frame or another floor", 1000.0, double.MaxValue) };
        foreach (var (name, lo, hi) in bins)
        {
            var inBin = storeys.Where(s => s.MedianMm >= lo && s.MedianMm < hi).ToList();
            Console.WriteLine($"   {inBin.Count,4} storey pairs {name}; sets: {string.Join(" ", inBin.GroupBy(s => s.Job).OrderByDescending(g => g.Count()).Take(top).Select(g => $"{g.Key}({g.Count()}{(olderBy.TryGetValue(g.Key, out int d) && d > 180 ? $", her model {d} d older" : "")})"))}");
        }
        var rigid = storeys.Where(s => Math.Abs(s.RigidX) + Math.Abs(s.RigidY) >= 150 && s.AfterMm < s.MedianMm / 2).ToList();
        Console.WriteLine($"   of these, {rigid.Count} storey pairs are a RIGID SHIFT of 150 mm or more that halves the error once removed (a frame, not a reading): {string.Join(" ", rigid.GroupBy(s => s.Job).Take(top).Select(g => $"{g.Key}({g.Count()}: {g.First().RigidX:0},{g.First().RigidY:0})"))}");
        Console.WriteLine();

        // 2. storeys one names and the other does not
        Console.WriteLine("2. STOREYS ONE MODEL NAMES AND THE OTHER DOES NOT:");
        Console.WriteLine($"   only ours ({onlyOurs.Count} over {onlyOurs.Select(o => o.Job).Distinct().Count()} sets), by name shape: {Shapes(onlyOurs.Select(o => o.Name), top)}");
        Console.WriteLine($"   only hers ({onlyTheirs.Count} over {onlyTheirs.Select(o => o.Job).Distinct().Count()} sets), by name shape: {Shapes(onlyTheirs.Select(o => o.Name), top)}");
        Console.WriteLine();

        // 3. what we place that she does not, by our section
        Console.WriteLine("3. OURS WITH NONE OF HERS WITHIN 300 mm, by our section (a wall she models as a pier, an anchor read as a column, a column she left out):");
        foreach (var g in oursNotTheirs.GroupBy(o => o.Section).OrderByDescending(g => g.Sum(x => x.Count)).Take(top))
            Console.WriteLine($"   {g.Sum(x => x.Count),6}  {g.Key,-22} on {g.Select(x => x.Job).Distinct().Count()} sets: {string.Join(" ", g.OrderByDescending(x => x.Count).Take(5).Select(x => $"{x.Job}({x.Count})"))}");
        Console.WriteLine($"   of all of ours she has no partner for, {pierToHer.Sum(p => p.Count)} stand on a wall she modelled (a column to us, a pier to her) over {pierToHer.Count} sets: {string.Join(" ", pierToHer.OrderByDescending(p => p.Count).Take(top).Select(p => $"{p.Job}({p.Count})"))}");
        Console.WriteLine($"   and {beyondFootprint.Sum(b => b.Count)} of ours stand beyond her model's footprint on their storey over {beyondFootprint.Count} sets (another building on the sheet, or her model is one building of several): {string.Join(" ", beyondFootprint.OrderByDescending(b => b.Count).Take(top).Select(b => $"{b.Job}({b.Count})"))}");
        Console.WriteLine();

        // 4. what she places that we miss, by her section
        Console.WriteLine("4. HERS WITH NONE OF OURS WITHIN 300 mm, by her section (what the drawing reader misses or misplaces):");
        foreach (var g in theirsNotOurs.GroupBy(o => o.Section).OrderByDescending(g => g.Sum(x => x.Count)).Take(top))
            Console.WriteLine($"   {g.Sum(x => x.Count),6}  {g.Key,-22} on {g.Select(x => x.Job).Distinct().Count()} sets: {string.Join(" ", g.OrderByDescending(x => x.Count).Take(5).Select(x => $"{x.Job}({x.Count})"))}");
        Console.WriteLine();

        // 5. the sets to open first: most of hers unmatched
        Console.WriteLine("5. THE SETS TO OPEN FIRST (her columns we have no partner for, most first):");
        foreach (var g in theirsNotOurs.GroupBy(o => o.Job).OrderByDescending(g => g.Sum(x => x.Count)).Take(top))
        {
            var fr = frames.FirstOrDefault(x => x.Job == g.Key);
            Console.WriteLine($"   {g.Key,-10} {g.Sum(x => x.Count),5} of hers unmatched; frame {(fr.Job is null ? "-" : fr.Grids ? "grids" : "columns")}, ours within 100 mm {(fr.Of > 0 ? $"{fr.Within} of {fr.Of}" : "-")}; her top sections: {string.Join(" ", g.OrderByDescending(x => x.Count).Take(3).Select(x => $"{x.Section}({x.Count})"))}");
        }

        // 6. openings, ours against hers (step 104): the X rule's precision where she modelled, and what she cuts that we do not
        if (openings.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("6. OPENINGS, OURS AGAINST HERS on the storeys both models name (an opening is hers when the centre of one of hers lies within 1.5 m of ours):");
            int ours = openings.Sum(o => o.Ours), hers = openings.Sum(o => o.Theirs), om = openings.Sum(o => o.OursMatched), tm = openings.Sum(o => o.TheirsMatched);
            Console.WriteLine($"   over {openings.Count} sets: ours judged {ours}, hers {hers}; ours she has {om} ({(ours == 0 ? 0 : 100.0 * om / ours):F0}%); hers we have {tm} ({(hers == 0 ? 0 : 100.0 * tm / hers):F0}%)");
            Console.WriteLine($"   ours she has not, by plan size: {string.Join(", ", oursOpeningsNotHers.GroupBy(o => o.Size).OrderByDescending(g => g.Sum(x => x.Count)).Take(top).Select(g => $"{g.Key} {g.Sum(x => x.Count)} on {g.Select(x => x.Job).Distinct().Count()} sets"))}");
            Console.WriteLine($"   hers we have not, by plan size: {string.Join(", ", hersOpeningsNotOurs.GroupBy(o => o.Size).OrderByDescending(g => g.Sum(x => x.Count)).Take(top).Select(g => $"{g.Key} {g.Sum(x => x.Count)} on {g.Select(x => x.Job).Distinct().Count()} sets"))}");
            Console.WriteLine("   sets to open first (ours she has not, most first): " + string.Join(" ", openings.Where(o => o.Ours - o.OursMatched > 0).OrderByDescending(o => o.Ours - o.OursMatched).Take(top).Select(o => $"{o.Job}({o.Ours - o.OursMatched} of {o.Ours})")));
            Console.WriteLine("   sets to open first (hers we have not, a metre and more across, most first): " + string.Join(" ", hersOpeningsNotOurs.Where(h => !h.Size.StartsWith("0x", StringComparison.Ordinal) && !h.Size.StartsWith("0.5x", StringComparison.Ordinal)).GroupBy(h => h.Job).OrderByDescending(g => g.Sum(x => x.Count)).Take(top).Select(g => $"{g.Key}({g.Sum(x => x.Count)})")));
        }
        return 0;

        static int N(string s) => int.Parse(s.Replace(",", ""), CultureInfo.InvariantCulture);
        static double D(string s) => double.Parse(s.Replace(",", ""), CultureInfo.InvariantCulture);
        static string Shapes(IEnumerable<string> names, int top) =>
            string.Join(" ", names.Select(n => Regex.Replace(n.ToUpperInvariant(), @"\d+", "#")).GroupBy(n => n).OrderByDescending(g => g.Count()).Take(top).Select(g => $"{g.Key}({g.Count()})"));
    }
}
