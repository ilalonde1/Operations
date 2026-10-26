namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// A FLOOR THE SET FRAMES IN WOOD CARRIES NO CONCRETE PLATE (intake step 184, 2026-10-26).
///
/// A wood-frame building is drawn as "LEVEL N SHOWING LEVEL N+1 FRAMING OVER": the plan of level N with the joists and
/// beams of the floor above it. Level N+1's floor IS the framing over the level N sheet. But the first link of that chain
/// frames concrete as often as wood: "LEVEL 1 SHOWING LEVEL 2 FRAMING OVER" is the ground floor carrying the podium slab
/// on 31064, 31066, 31082, 31162 and 31185. So a framing-over sheet's own floor is taken as wood only where the sheet that
/// frames it is ITSELF the plan of a framed floor - the second link and up. Whatever closed outline such a sheet carries on
/// a slab layer is not a concrete plate. A wood storey that also has a concrete plan takes its concrete from that plan.
///
/// Measured on the 57 sets of the corpus gate (2026-10-26): 21 carry framing-over sheets. Built on and off
/// (KOR_STEP184_OFF) over those 21 and the banked sets, the first version - every framed floor - moved 86 storeys: 80
/// right, 5 LOST an L2 the engineer plates (3,976-25,300 sq ft), 1 left. Narrowed to the second link (this version, scored
/// on the same builds): 65 storeys' plate removed where she plates nothing, 7 kept where she plates, 0 lost, 14 left as
/// read where she plates nothing - the chain's second floor where its bottom sheet is the podium top. No separator for
/// those 14 against the 5 is known: stud walls on the framing sheet, the plate's thickness and the sheet's level were each
/// measured and do not separate them. The plates removed were often not floors at all: 31162's wood storeys carried the
/// sheet's legend box, accepted because a sample wall stands in it.
///
/// WHAT IT COVERS: sheets whose title has a framing-over clause (<c>dxf.framing-over-words</c>), each storey read through
/// <see cref="PlanSheetNaming.Parse(string, DrawingVocabulary)"/> - the own part as every sheet's, the framed part the
/// same way - matched by building tag (a tagged sheet is framed only by sheets sharing a tag, an untagged one only by
/// untagged ones). WHAT IT DOES NOT: a wood plan titled without a framing-over clause (the stud-wall reading,
/// <c>WallTypeTagging.StudWallsOfAWoodPlan</c>, is not consulted); a floor framed in wood over only PART of its storey
/// (a podium storey half wood, half concrete) - the whole sheet's plate goes; a framed clause naming no storey; the first
/// wood floor above a podium (left as read: the 14). A same-class fault it would not catch: a podium two concrete storeys
/// deep, each drawn as the framing over the one below, loses its upper slab - the second link is concrete there.
/// </summary>
public static class WoodFramedFloors
{
    /// <summary>One sheet whose own floor its building frames in wood, and the storey that says so.</summary>
    public sealed record Framed(string File, string OwnStorey, string FramedBy);

    /// <summary>The sheets, of those given, whose own floor the set draws as the framing over the level below.</summary>
    public static IReadOnlyList<Framed> SheetsOf(IReadOnlyDictionary<string, PlanSheetInfo> infoByFile, DrawingVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(infoByFile);
        ArgumentNullException.ThrowIfNull(vocabulary);
        // every storey a sheet draws as the framing over, with the sheet that does and its buildings
        var framings = new List<(string Storey, string By, IReadOnlyList<string> Buildings)>();
        foreach (var (file, info) in infoByFile)
        {
            if (FramedPart(file, vocabulary) is not { } over) continue;
            foreach (string storey in Storeys(PlanSheetNaming.Parse(over + ".dxf", vocabulary)))
                framings.Add((storey, Path.GetFileNameWithoutExtension(file), info.BuildingTags));
        }
        if (framings.Count == 0) return Array.Empty<Framed>();

        var framed = new List<(Framed Sheet, IReadOnlyList<string> By)>();
        foreach (var (file, info) in infoByFile.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (FramedPart(file, vocabulary) is null) continue;              // not a framing-over sheet: a concrete plan keeps its floor
            var own = Storeys(info);
            if (own.Count == 0) continue;
            // every storey the sheet is the plan of must be framed by its own building - a sheet for L3 to L5 whose L3 is the podium keeps its plate
            var by = own.Select(s => framings.FirstOrDefault(f => f.Storey == s && SameBuilding(f.Buildings, info.BuildingTags))).ToList();
            if (by.Any(f => f.By is null)) continue;
            framed.Add((new Framed(file, string.Join("+", own), by[0].By), by.Select(f => f.By).ToList()));
        }
        // THE BOTTOM OF A CHAIN IS NOT WOOD (2026-10-26, the replay): "LEVEL 1 SHOWING LEVEL 2 FRAMING OVER" frames the CONCRETE
        // podium as often as a wood floor - 31064, 31066 building 2, 31082, 31162 and 31185 lost an L2 the engineer plates at
        // 3,976-25,300 sq ft. A floor is taken as wood only where the sheet framing it is itself the plan of a framed floor:
        // the second link of a chain and up. 0 of the replay's 86 moved storeys lost; 14 the engineer does not plate are left
        // as read, the chain's second floor where its bottom sheet is the podium top - no separator for those is known yet
        // (stud walls on the framing sheet, the plate's thickness and the sheet's level were each measured, and do not).
        var framedSheets = framed.Select(f => Path.GetFileNameWithoutExtension(f.Sheet.File)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return framed.Where(f => f.By.All(framedSheets.Contains)).Select(f => f.Sheet).ToList();
    }

    /// <summary>The part of a sheet's title after its framing-over word - "LEVEL 4 FRAMING OVER" - or null where it has none.</summary>
    internal static string? FramedPart(string file, DrawingVocabulary vocabulary)
    {
        string title = PlanSheetNaming.TitleOf(file);
        string own = vocabulary.OwnStoreyPart(title);
        if (own.Length >= title.Length) return null;
        string rest = title[own.Length..].TrimStart();
        foreach (string w in vocabulary.FramingOverWords)
            if (rest.StartsWith(w, StringComparison.OrdinalIgnoreCase)) { rest = rest[w.Length..]; break; }
        return rest.Trim().Length == 0 ? null : rest.Trim();
    }

    /// <summary>The storeys a parse names, as one key each: "L4", "P1", "ROOF".</summary>
    private static IReadOnlyList<string> Storeys(PlanSheetInfo info)
        => info.Levels.Select(l => "L" + l.ToString(System.Globalization.CultureInfo.InvariantCulture))
               .Concat(info.ParkadeLevels.Select(p => "P" + p.ToString(System.Globalization.CultureInfo.InvariantCulture)))
               .Concat(info.IsRoof ? new[] { "ROOF" } : Array.Empty<string>())
               .Distinct(StringComparer.Ordinal).ToList();

    private static bool SameBuilding(IReadOnlyList<string> a, IReadOnlyList<string> b)
        => a.Count == 0 && b.Count == 0 || a.Intersect(b, StringComparer.OrdinalIgnoreCase).Any();
}
