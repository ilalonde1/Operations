using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// A WALL IS WHAT ITS TAG SAYS IT IS (intake step 33).
///
/// On a set with an assembly schedule (<see cref="AssemblySchedule"/>), the plans tag their walls
/// with the schedule's codes — on 31170, 887 tags on the enlarged 1/4" plans, 635 of them steel
/// stud. A tag is a word on the plan equal to a code; a wall takes the nearest tag within reach
/// of its axis; the code's material comes from the card. A wall whose type is a partition is not
/// structure: it goes to the DXF's KOR_PARTITION layer, which the model does not read, and the
/// report counts it. A tag names its whole run, so a pier on the same line as a typed neighbour
/// takes its type. And on a plan that tags its walls (<see cref="TaggingSheetMinTags"/> or more),
/// a wall with no tag is not a wall — millwork, a tub, a rail, pairs of lines the two-face reader
/// cannot tell from a wall — and goes to the same layer, counted separately. On a plan that does
/// not tag (the 1/8" key plan), an untagged wall keeps being modelled as drawn.
///
/// WHAT IT COVERS: a tag beside a wall; the nearest of two tags; a tag out of reach leaving the
/// wall untagged; a partition's code and a concrete code from the same schedule. WHAT IT DOES NOT:
/// a tag on another sheet (the 1/8" floor plan's walls are untagged; the enlargement tags them, and
/// DXF carries no way to pass that across sheets — the case for the composer reading the intake's
/// record directly); a tag with a leader pointing further than the reach; a wall drawn as two face
/// lines whose tag sits between them (the axis is between the faces, so that one is within reach).
/// </summary>
public static class WallTypeTagging
{
    /// <summary>How far from a wall's axis its tag may stand, in millimetres. On 31170's 1/4" plans the tags sit within about 600 mm; a bay is 3 m or more.</summary>
    public const double ReachMm = 1200;

    public static void Apply(ExtractedGeometry geometry, VectorPageReader.PageContent content,
        SheetFurniture.Set furniture, IReadOnlyList<AssemblySchedule.Assembly> assemblies, double minWallThicknessMm = PdfIntakeOptions.DefaultMinWallThicknessMm,
        double unfilledWallMinThicknessMm = DefaultUnfilledWallMinThicknessMm)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(assemblies);

        geometry.WallTypeCodes.Clear();
        geometry.WallIsPartition.Clear();
        geometry.WallTypeTags.Clear();

        var byCode = new Dictionary<string, AssemblySchedule.Assembly>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in assemblies)
            if (!byCode.ContainsKey(a.Code)) byCode[a.Code] = a;
        if (byCode.Count == 0 || geometry.ScaleDenominator <= 0)
        {
            for (int i = 0; i < geometry.Walls.Count; i++) { geometry.WallTypeCodes.Add(null); geometry.WallIsPartition.Add(false); }
            StudWallsOfAWoodPlan(geometry, minWallThicknessMm, unfilledWallMinThicknessMm);
            return;
        }

        double scale = geometry.ScaleDenominator * PdfToSafeConstants.PointsToMm;
        foreach (var w in content.Words)
        {
            string t = w.Text.Trim();
            if (!byCode.ContainsKey(t)) continue;
            if (furniture.IsFurniture(w.Cx, w.Cy)) continue;
            geometry.WallTypeTags.Add((byCode[t].Code, w.Cx * scale, w.Cy * scale));
        }

        var codes = new string?[geometry.Walls.Count];
        // a dimension string (step 35) is no wall: it takes no tag, and it passes none along a run —
        // it took the stud tag beside it and handed it to the real wall it abutted (Codex audit 2026-09-11, F10)
        bool NotAWall(int i) => i < geometry.WallIsDimensionString.Count && geometry.WallIsDimensionString[i];
        for (int i = 0; i < geometry.Walls.Count; i++)
        {
            if (NotAWall(i)) continue;
            var wall = geometry.Walls[i];
            string? best = null;
            double bestD = double.MaxValue;
            foreach (var tag in geometry.WallTypeTags)
            {
                double d = DistanceToSegment(tag.X, tag.Y, wall.Start, wall.End);
                if (d < bestD) { bestD = d; best = tag.Code; }
            }
            if (best is not null && bestD <= ReachMm) codes[i] = best;
        }

        // A TAG NAMES ITS WHOLE RUN. A wall is drawn as piers where doorways cut it and as panels
        // where the two-face reader found it, and the drafter tags the run once; a pier that runs
        // the same way as a typed neighbour, on the same line, and abuts it, is the same wall.
        bool spread = true;
        while (spread)
        {
            spread = false;
            for (int i = 0; i < codes.Length; i++)
            {
                if (codes[i] is not null || NotAWall(i)) continue;
                for (int j = 0; j < codes.Length; j++)
                {
                    if (codes[j] is null || NotAWall(j) || !ContinuesRun(geometry.Walls[i], geometry.Walls[j])) continue;
                    codes[i] = codes[j]; spread = true; break;
                }
            }
        }

        // ON A PLAN THAT TAGS ITS WALLS, A WALL WITH NO TAG IS NOT A WALL. An architect's enlarged
        // plan tags every wall assembly; the pairs of parallel lines left over are millwork, tubs,
        // counters and balcony rails, which the two-face reader cannot tell from a wall and the
        // drafter never called one. A sheet with fewer tags than this has not tagged its walls, and
        // its untagged walls keep being modelled as drawn (the 1/8" key plan, 24 tags on 16 sheets).
        bool taggingSheet = geometry.WallTypeTags.Count >= TaggingSheetMinTags;

        for (int i = 0; i < geometry.Walls.Count; i++)
        {
            string? code = codes[i];
            if (i < geometry.WallIsDimensionString.Count && geometry.WallIsDimensionString[i]) { geometry.WallTypeCodes.Add(null); geometry.WallIsPartition.Add(false); continue; }   // a dimension string is not a wall (step 35): neither typed nor a footprint
            geometry.WallTypeCodes.Add(code);
            bool partition = code is not null
                ? byCode[code].Material == AssemblySchedule.Material.Stud
                : taggingSheet;                                   // untagged on a tagging sheet: not a wall, kept as a footprint
            geometry.WallIsPartition.Add(partition);
        }
        StudWallsOfAWoodPlan(geometry, minWallThicknessMm, unfilledWallMinThicknessMm);
    }

    /// <summary>
    /// A WALL THE DRAFTER DID NOT FILL IS A WALL ONLY AT A RETAINING WALL'S THICKNESS (step 67, 2026-09-14).
    /// Thickness cannot tell a 2x6 stud wall (139.7 mm) from a six-inch wall drawn thin (31065: 142-150 mm) -
    /// s72 said so - but the DRAWING can: a concrete wall is a filled band, and the one concrete wall drawn as
    /// its two faces is a retaining wall, 12-16 in; a stud wall is two lines with nothing between. Measured on
    /// seven plans (2026-09-14): wood plans read 81% and 79% of their walls from unfilled line pairs (31066 L2
    /// 117 of 145, L5 77 of 98); concrete plans 0-35% (31138 0 of 15, 31168 5 of 29, 31065 10 of 51, 31202 15
    /// of 43; the architect's 31170 4 of 38). So a sheet MOST of whose walls are unfilled pairs is a wood plan,
    /// and on it every unfilled pair thinner than <see cref="UnfilledWallMinThicknessMm"/> is a stud wall: a
    /// partition, kept as a footprint on the layer the model does not read (step 33), never a wall - and never
    /// two lines the composer rebuilds into one. A concrete plan's unfilled pairs (its retaining walls) are
    /// untouched: they are the minority there, and thicker than the floor anyway.
    /// WHAT THIS DOES NOT: a wood plan whose stud walls are drawn filled (a hatch is a fill); a wood plan with
    /// more filled concrete than stud walls (a podium level); the composer's own reading of a KOR DXF.
    /// </summary>
    internal static void StudWallsOfAWoodPlan(ExtractedGeometry geometry, double minWallThicknessMm, double unfilledWallMinThicknessMm = DefaultUnfilledWallMinThicknessMm)
    {
        // a dimension string stood down as a wall (step 35) is not a wall here either - neither evidence that the
        // sheet is a wood plan nor a partition (step 72, the audit's finding 4: twenty stood-down strings beside one
        // filled 140 mm wall made the sheet "wood" and the wall a partition)
        bool NotAWall(int i) => i < geometry.WallIsDimensionString.Count && geometry.WallIsDimensionString[i];
        var fromFaces = geometry.WallFaceLines.Values.Where(i => !NotAWall(i)).ToHashSet();
        int walls = Enumerable.Range(0, geometry.Walls.Count).Count(i => !NotAWall(i));
        // a wood plan by the measurement: two thirds or more of its walls unfilled pairs, and twenty of them - a
        // concrete sheet with three walls, two of them a retaining wall's faces, is not a wood plan
        if (walls == 0 || fromFaces.Count < WoodPlanMinUnfilledPairs || fromFaces.Count * 3 < walls * 2) return;
        for (int i = 0; i < geometry.Walls.Count && i < geometry.WallIsPartition.Count; i++)
        {
            if (NotAWall(i)) continue;
            double t = geometry.Walls[i].ThicknessMm;
            // an unfilled pair under a retaining wall's thickness; and a FILLED band under the wall floor with no
            // slack - the half inch is for concrete walls drawn thin (31065's six-inch walls at 142-150 mm), and on
            // a wood plan a filled 140 mm band is a poche'd 2x6 (31066 L2: 22 of its 28 filled bands), not a thin six
            if ((fromFaces.Contains(i) && t < unfilledWallMinThicknessMm) || (!fromFaces.Contains(i) && t < minWallThicknessMm))
                geometry.WallIsPartition[i] = true;
        }
    }

    /// <summary>The thinnest unfilled line pair that is a concrete wall on a wood plan: a retaining wall, 8 in - the compiled default under the row dxf.pdf.unfilled-wall-min-thickness-mm (migration 091, read by PdfIntakeOptions since step 72).</summary>
    public const double DefaultUnfilledWallMinThicknessMm = 8 * 25.4;

    /// <summary>A wood plan has at least this many unfilled pairs (31066's plans: 117 and 77; the concrete plans measured: 15 at most).</summary>
    public const int WoodPlanMinUnfilledPairs = 20;

    /// <summary>A sheet with at least this many tags has tagged its walls, and what it left untagged is not a wall.</summary>
    public const int TaggingSheetMinTags = 10;

    /// <summary>Whether wall a continues the run of wall b: the same way within ten degrees, on the same line within a wall's thickness, and abutting or overlapping within a hand's width.</summary>
    internal static bool ContinuesRun(WallPanel a, WallPanel b)
    {
        double ax = a.End.X - a.Start.X, ay = a.End.Y - a.Start.Y, al = Math.Sqrt(ax * ax + ay * ay);
        double bx = b.End.X - b.Start.X, by = b.End.Y - b.Start.Y, bl = Math.Sqrt(bx * bx + by * by);
        if (al <= 0 || bl <= 0) return false;
        if (Math.Abs((ax * bx + ay * by) / (al * bl)) < 0.985) return false;
        // lateral offset of a's midpoint from b's line, and a's extent along b
        double ux = bx / bl, uy = by / bl;
        double mx = (a.Start.X + a.End.X) / 2 - b.Start.X, my = (a.Start.Y + a.End.Y) / 2 - b.Start.Y;
        double lateral = Math.Abs(-uy * mx + ux * my);
        if (lateral > Math.Max(a.ThicknessMm, b.ThicknessMm) + 25) return false;
        double t0 = (a.Start.X - b.Start.X) * ux + (a.Start.Y - b.Start.Y) * uy;
        double t1 = (a.End.X - b.Start.X) * ux + (a.End.Y - b.Start.Y) * uy;
        double lo = Math.Min(t0, t1), hi = Math.Max(t0, t1);
        const double hand = 6 * 25.4;
        return hi >= -hand && lo <= bl + hand;
    }

    private static double DistanceToSegment(double px, double py, (double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        double t = len2 <= 0 ? 0 : Math.Clamp(((px - a.X) * dx + (py - a.Y) * dy) / len2, 0, 1);
        double cx = a.X + t * dx, cy = a.Y + t * dy;
        return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
    }
}
