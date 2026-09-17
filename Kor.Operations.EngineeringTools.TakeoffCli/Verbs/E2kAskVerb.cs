// The takeoff verb `e2k-ask`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Ask a finished model about itself. The query surface an /ask tool will wrap later, driven from a
// terminal today. Usage: takeoff e2k-ask <model.e2k> [storeys|look|openings|sections|concrete] [storey]
internal static class E2kAskVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("e2k-ask", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: takeoff e2k-ask <model.e2k | folder> [storeys|look|openings|openings-with-columns|sections|concrete] [storey]"); return 1; }
        // A FOLDER OF MODELS ANSWERS AS A CORPUS (step 107b, 2026-09-16): the yardsticks folder holds 106 of the engineers'
        // own exports, and a question put to all of them judges a rule before it is banked - summed, then the sets that say otherwise
        if (Directory.Exists(args[1]) && args.Length >= 3 && args[2].Equals("openings-with-columns", StringComparison.OrdinalIgnoreCase))
        {
            int total = 0, with = 0, models = 0;
            var bySet = new List<(string Model, int Holes, int With)>();
            foreach (string e2k in Directory.EnumerateFiles(args[1], "*.e2k", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.Ordinal))
            {
                try
                {
                    var holes = E2kModelQuery.OpeningsWithColumnsInside(E2kDocument.Load(e2k));
                    if (holes.Count == 0) continue;
                    models++; total += holes.Count; int w = holes.Count(h => h.ColumnsInside > 0); with += w;
                    if (w > 0) bySet.Add((Path.GetFileNameWithoutExtension(e2k), holes.Count, w));
                }
                catch (Exception ex) { Console.Error.WriteLine($"  {Path.GetFileName(e2k)}: {ex.Message}"); }
            }
            Console.WriteLine($"{models} model(s) with openings a metre and more across: {total} opening(s), {with} hold a column of the storey inside ({(total == 0 ? 0 : 100.0 * with / total):F1}%)");
            foreach (var (m, h, w) in bySet.OrderByDescending(x => x.With)) Console.WriteLine($"   {m,-14} {w,3} of {h,4}");
            return 0;
        }
        // the shape census over a folder: how thin, how long, how small the holes the engineers cut are, counted by class,
        // so a reader rule about a shape ("a strip narrower than a metre is no hole") is judged before it is written
        if (Directory.Exists(args[1]) && args.Length >= 3 && args[2].Equals("openings-shapes", StringComparison.OrdinalIgnoreCase))
        {
            int models = 0, total = 0, zero = 0, subMetre = 0, strips = 0, stripsWide = 0, stripsLong = 0, shafts = 0, big = 0;
            var stripSets = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string e2k in Directory.EnumerateFiles(args[1], "*.e2k", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.Ordinal))
            {
                IReadOnlyList<(string Storey, string Object, double WidthMm, double HeightMm)> boxes;
                try { boxes = E2kModelQuery.OpeningBoxes(E2kDocument.Load(e2k)); } catch (Exception ex) { Console.Error.WriteLine($"  {Path.GetFileName(e2k)}: {ex.Message}"); continue; }
                if (boxes.Count == 0) continue;
                models++;
                foreach (var (_, _, w, h) in boxes)
                {
                    total++;
                    double lo = Math.Min(w, h), hi = Math.Max(w, h);
                    if (lo < 50) { zero++; continue; }                       // a slit of no area: an artefact
                    if (lo < 1000) subMetre++;                                 // sleeves, chases
                    if (hi >= 10 * lo && lo >= 50) { strips++; if (lo >= 1000) stripsWide++; if (hi > 10000) stripsLong++; stripSets[Path.GetFileNameWithoutExtension(e2k)] = stripSets.GetValueOrDefault(Path.GetFileNameWithoutExtension(e2k)) + 1; }
                    if (lo >= 1000 && hi <= 12000) shafts++;
                    if (hi > 12000) big++;
                }
            }
            Console.WriteLine($"{models} model(s), {total} opening(s): {zero} slits of no width (artefacts), {subMetre} under a metre on their short side, " +
                              $"{strips} strips ten times longer than wide (of which {stripsWide} a metre and wider, {stripsLong} longer than 10 m), {shafts} a metre and wider and under 12 m long (shafts, stairs), {big} over 12 m long (voids)");
            Console.WriteLine("   strips by set: " + string.Join(" ", stripSets.OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"{kv.Key}({kv.Value})")));
            return 0;
        }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"Model not found '{args[1]}'."); return 2; }

        var askDoc = E2kDocument.Load(args[1]);
        string question = args.Length >= 3 ? args[2].ToLowerInvariant() : "storeys";
        string? askStorey = args.Length >= 4 ? args[3] : null;

        Console.WriteLine($"\n{Path.GetFileName(args[1])}");

        switch (question)
        {
            case "storeys":
            case "storey":
            {
                var rows = E2kModelQuery.Storeys(askDoc)
                    .Where(s => askStorey is null || s.Name.Contains(askStorey, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Console.WriteLine($"\n{"Storey",-16} {"Rise",7} {"Walls",6} {"Cols",5} {"Slabs",6} {"Holes",6} {"Slab area",11} {"Concrete",10}  Thicknesses");
                foreach (var s in rows)
                {
                    string thick = string.Join("/", s.SlabThicknesses) + (s.WallThicknesses.Count > 0 ? "  walls " + string.Join("/", s.WallThicknesses) : "");
                    Console.WriteLine(
                        $"{s.Name,-16} {s.RiseInches / 12.0,6:N1}' {s.Walls,6} {s.Columns,5} {s.Slabs,6} {s.Openings,6} " +
                        $"{s.SlabAreaSqFt,10:N0}sf {s.ConcreteYd3,9:N1}y  {thick}");
                }
                var empty = rows.Where(s => s.Walls + s.Columns + s.Slabs == 0).Select(s => s.Name).ToList();
                if (empty.Count > 0) Console.WriteLine($"\n  Nothing stands on: {string.Join(", ", empty)}");
                break;
            }

            case "look":
            {
                var concerns = E2kModelQuery.WorthALook(askDoc);
                if (concerns.Count == 0) { Console.WriteLine("\nNothing on this model is worth a second look: every storey that carries structure carries a floor, and every floor has something under it."); break; }
                Console.WriteLine($"\n{concerns.Count} thing(s) worth a second look — legal in ETABS, and still worth a sentence:");
                foreach (var c in concerns) Console.WriteLine($"   {c.Storey,-16} {c.What}\n{"",19}{c.Why}");
                break;
            }

            case "openings":
            {
                var holes = E2kModelQuery.Openings(askDoc);
                var boxes = E2kModelQuery.OpeningBoxes(askDoc).ToDictionary(b => (b.Storey, b.Object), b => (b.WidthMm, b.HeightMm));
                Console.WriteLine($"\n{holes.Count} opening(s), biggest first (area, then the box in metres):");
                foreach (var (st, obj, area) in holes.Where(h => askStorey is null || h.Storey.Contains(askStorey, StringComparison.OrdinalIgnoreCase)))
                {
                    string box = boxes.TryGetValue((st, obj), out var b) ? $"{b.WidthMm / 1000:N1} x {b.HeightMm / 1000:N1} m" : "";
                    Console.WriteLine($"   {st,-16} {obj,-8} {area,9:N0} sq ft   {box}");
                }
                break;
            }

            case "openings-with-columns":
            {
                // step 107b's question: does the engineer cut a hole with a column in it? Ask one model, or every .e2k in a
                // folder (takeoff e2k-ask <folder> openings-with-columns) - the corpus of her models judges a rule before it is banked
                var holes = E2kModelQuery.OpeningsWithColumnsInside(askDoc);
                int with = holes.Count(h => h.ColumnsInside > 0);
                Console.WriteLine($"\n{holes.Count} opening(s) a metre and more across; {with} hold a column of the storey inside ({(holes.Count == 0 ? 0 : 100.0 * with / holes.Count):F1}%):");
                foreach (var (st, obj, area, n) in holes.Where(h => h.ColumnsInside > 0).Take(20))
                    Console.WriteLine($"   {st,-16} {obj,-8} {area,9:N0} sq ft  {n} column(s) inside");
                break;
            }

            case "sections":
            {
                Console.WriteLine($"\n{"Section",-22} {"Kind",-8} {"Size",-14} {"Used",5}  On");
                foreach (var (sec, kind, size, count, on) in E2kModelQuery.Sections(askDoc))
                    Console.WriteLine($"{sec,-22} {kind,-8} {size,-14} {count,5}  {(on.Length > 60 ? on[..60] + "…" : on)}");
                break;
            }

            case "concrete":
            {
                var t = E2kQuantityTakeoff.Read(askDoc);
                var rows = t.Inputs.Where(i => askStorey is null || i.Level.Contains(askStorey, StringComparison.OrdinalIgnoreCase)).ToList();
                Console.WriteLine($"\n{"Storey",-16} {"Element",-8} {"Grade",-8} {"Concrete",10} {"Formwork",11}");
                foreach (var i in rows)
                    Console.WriteLine($"{i.Level,-16} {i.Element,-8} {i.Grade,-8} {i.ConcreteVolume,9:N1}y {i.FormworkArea,10:N0}sf");
                Console.WriteLine($"{"TOTAL",-16} {"",-8} {"",-8} {rows.Sum(i => i.ConcreteVolume),9:N1}y {rows.Sum(i => i.FormworkArea),10:N0}sf");
                foreach (var fl in t.Flags) Console.WriteLine($"\n   [{fl.Code}] {fl.Note}");
                break;
            }

            default:
                Console.Error.WriteLine($"'{question}' is not a question this model can answer. Try: storeys, look, openings, sections, concrete.");
                return 1;
        }

        Console.WriteLine();
        return 0;
    }
}
