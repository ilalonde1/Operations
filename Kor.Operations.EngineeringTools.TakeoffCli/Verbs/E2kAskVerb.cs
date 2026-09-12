// The takeoff verb `e2k-ask`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Ask a finished model about itself. The query surface an /ask tool will wrap later, driven from a
// terminal today. Usage: takeoff e2k-ask <model.e2k> [storeys|look|openings|sections|concrete] [storey]
internal static class E2kAskVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("e2k-ask", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: takeoff e2k-ask <model.e2k> [storeys|look|openings|sections|concrete] [storey]"); return 1; }
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
                Console.WriteLine($"\n{holes.Count} opening(s), biggest first:");
                foreach (var (st, obj, area) in holes.Where(h => askStorey is null || h.Storey.Contains(askStorey, StringComparison.OrdinalIgnoreCase)))
                    Console.WriteLine($"   {st,-16} {obj,-8} {area,9:N0} sq ft");
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
