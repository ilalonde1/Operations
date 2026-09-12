// The takeoff verb `e2k-compare`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
internal static class E2kCompareVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("e2k-compare", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("Usage: takeoff e2k-compare <reference.e2k> <candidate.e2k> <story> [<story> ...]"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"Not found '{args[1]}'."); return 2; }
        if (!File.Exists(args[2])) { Console.Error.WriteLine($"Not found '{args[2]}'."); return 2; }

        var refGeom = E2kGeometryReader.Read(E2kDocument.Load(args[1]));
        var candGeom = E2kGeometryReader.Read(E2kDocument.Load(args[2]));

        var alignment = E2kGeometryAligner.Solve(refGeom, candGeom, "K");
        Console.WriteLine($"best-fit offset   : {alignment.OffsetX:0.#}, {alignment.OffsetY:0.#} in   " +
                          $"({alignment.Inliers}/{alignment.ReferencePoints} reference columns within 6in, " +
                          $"median residual {alignment.MedianResidual:0.0} in)");
        Console.WriteLine();

        for (int i = 3; i < args.Length; i++)
        {
            // "K" is the prefix the generator stamps on everything it creates; without it the
            // reference's own objects, carried through into the output, would match themselves.
            var agree = E2kGeometryComparer.Compare(refGeom, candGeom, args[i], "K");
            Console.WriteLine($"storey            : {agree.Story}");
            Console.WriteLine($"  walls           : reference {agree.ReferenceWalls}, generated {agree.CandidateWalls}");
            Console.WriteLine($"  columns         : reference {agree.ReferenceColumns}, generated {agree.CandidateColumns}");
            Console.WriteLine($"  ref extents     : X {agree.ReferenceExtents.MinX:0}..{agree.ReferenceExtents.MaxX:0}   Y {agree.ReferenceExtents.MinY:0}..{agree.ReferenceExtents.MaxY:0}");
            Console.WriteLine($"  gen extents     : X {agree.CandidateExtents.MinX:0}..{agree.CandidateExtents.MaxX:0}   Y {agree.CandidateExtents.MinY:0}..{agree.CandidateExtents.MaxY:0}");
            Console.WriteLine($"  nearest match   : median {agree.MedianWallDistance:0.0} in, worst {agree.MaxWallDistance:0.0} in");
            Console.WriteLine($"  agreement       : {agree.WallsWithin12in}/{agree.ReferenceWalls} within 12in, {agree.WallsWithin36in}/{agree.ReferenceWalls} within 36in");
            Console.WriteLine($"  columns match   : median {agree.MedianColumnDistance:0.0} in, {agree.ColumnsWithin6in}/{agree.ReferenceColumns} within 6in");
            Console.WriteLine();
        }
        return 0;
    }
}
