namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// What to build for a job, decided before anything is built.
///
/// These decisions used to live in a PowerShell script: which folder is the job, which model in it
/// is the engineer's rather than ours, how many buildings the drawings describe and which storeys
/// belong to each. That is the product — the part nobody else has — and it sat in 818 lines that
/// the test suite could not reach, that shipped as text beside the binary, and that had to be run
/// against the live project share to be exercised at all.
///
/// It belongs here: decided in one place, covered by tests, compiled into the tool.
/// </summary>
public static class PublishPlan
{
    /// <summary>One model: what to call it, and how to cut the storey list down to it.</summary>
    /// <param name="Building">The building tag, or empty where a job has only one building.</param>
    /// <param name="Tower">
    /// Passed as --tower. Cuts by NAME: drops storeys belonging to other buildings and keeps the
    /// shared base BELOW this one, merging storeys that are one level drafted twice. 31168's ground
    /// floor is A-LEVEL 1 and B-LEVEL 1 1.7 in apart, and building C stands on it — without this
    /// the YMCA comes out with no ground floor at all.
    /// </param>
    /// <param name="DropStoreys">
    /// Passed as --drop-storeys. Reaches what a name cannot: 31168's LEVEL 3 to LEVEL 26 are tower
    /// floors called nothing in particular, and only where their structure stands says they are not
    /// the YMCA's.
    /// </param>
    public sealed record Model(string Building, string Tower, IReadOnlyList<string> DropStoreys);

    /// <summary>
    /// The storey extents this needs: where the structure read from each storey's sheets stands, in
    /// plan. Supplied rather than read here so this stays testable without a drawing on a share.
    /// </summary>
    public sealed record StoreyReach(string Storey, double MinX, double MinY, double MaxX, double MaxY);

    /// <summary>
    /// One model per building, worked out from the storey names and where their structure stands.
    ///
    /// A storey NAMED for a building belongs to it. A storey named for nobody belongs to whichever
    /// building's footprint its structure stands inside — and to all of them when it stands under
    /// all of them, which is what a shared podium or parkade is.
    ///
    /// The engineer asked for this directly — "let's do one model per building", and "it's best if
    /// a file only has the elevations relevant to the building modelled" — and the second half is a
    /// defect report: storeys from another building corrupt every storey-to-storey check she makes,
    /// because the stack she is checking is not the stack that exists.
    /// </summary>
    public static IReadOnlyList<Model> ForBuildings(
        IEnumerable<string> storeysTopToBottom,
        IEnumerable<StoreyReach> reach)
    {
        var storeys = storeysTopToBottom
            .Where(s => !s.Equals("Base", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var where = reach.ToDictionary(x => x.Storey, StringComparer.OrdinalIgnoreCase);

        var tags = storeys
            .Select(E2kDocument.BuildingTagOf)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // One building: nothing to cut, and no tower filter to apply.
        if (tags.Count == 0)
            return new[] { new Model(string.Empty, string.Empty, Array.Empty<string>()) };

        // A building's footprint is where its own named storeys stand.
        var footprint = new Dictionary<string, StoreyReach>(StringComparer.OrdinalIgnoreCase);
        foreach (string tag in tags)
        foreach (string storey in storeys.Where(s => E2kDocument.BuildingTagOf(s).Equals(tag, StringComparison.OrdinalIgnoreCase)))
        {
            if (!where.TryGetValue(storey, out var box)) continue;
            footprint[tag] = footprint.TryGetValue(tag, out var had)
                ? new StoreyReach(tag, Math.Min(had.MinX, box.MinX), Math.Min(had.MinY, box.MinY),
                                       Math.Max(had.MaxX, box.MaxX), Math.Max(had.MaxY, box.MaxY))
                : box with { Storey = tag };
        }

        var plans = new List<Model>();
        foreach (string tag in tags)
        {
            var drop = new List<string>();
            foreach (string storey in storeys)
            {
                // Storeys named for a building are the tower filter's business, not this one's:
                // it keeps the shared base below, which a name-blind drop would throw away.
                if (E2kDocument.BuildingTagOf(storey).Length > 0) continue;

                // Nothing read from it — keeping it costs an empty storey, dropping it could lose
                // real structure, and the first is the smaller mistake.
                if (!where.TryGetValue(storey, out var box)) continue;
                if (!footprint.TryGetValue(tag, out var mine)) continue;

                bool here = Covers(box, mine) >= 0.5;
                bool underEverything = tags.All(t => footprint.TryGetValue(t, out var f) && Covers(f, box) >= 0.5);
                if (!here && !underEverything) drop.Add(storey);
            }

            plans.Add(new Model(tag, tag, drop));
        }

        return plans;
    }

    /// <summary>How much of <paramref name="a"/> lies inside <paramref name="b"/>, 0 to 1.</summary>
    private static double Covers(StoreyReach a, StoreyReach b)
    {
        double w = Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX);
        double h = Math.Min(a.MaxY, b.MaxY) - Math.Max(a.MinY, b.MinY);
        double own = (a.MaxX - a.MinX) * (a.MaxY - a.MinY);
        return w > 0 && h > 0 && own > 0 ? w * h / own : 0;
    }

    /// <summary>
    /// The engineer's own model in a folder, never one of ours.
    ///
    /// Tool output carries KOR-prefixed object names, and a file round-tripped through ETABS keeps
    /// them — which is exactly how a generated model once got mistaken for an engineer's own work
    /// and rebuilt from itself. Choosing between two candidates is not this tool's call either:
    /// 31168's folder holds a site reference and a tower-B rebuild within 66 bytes of each other,
    /// and the larger is not the one meant.
    /// </summary>
    /// <returns>The chosen file, or null with <paramref name="why"/> saying what to do instead.</returns>
    public static string? ChooseReference(
        IEnumerable<(string Name, Func<string> Head)> candidates, out string why)
    {
        var ours = new List<string>();
        var theirs = new List<string>();

        foreach (var (name, head) in candidates)
        {
            if (name.Contains("FROM-DRAWINGS", StringComparison.OrdinalIgnoreCase)) { ours.Add(name); continue; }
            if (ContainsGeneratedObjectName(head())) { ours.Add(name); continue; }
            theirs.Add(name);
        }

        if (theirs.Count == 0)
        {
            why = ours.Count > 0
                ? "every model in this folder was generated by this tool; a model rebuilt from its own output is not a rebuild."
                : "no engineer-built model in this folder to build from.";
            return null;
        }

        var preferred = theirs.Where(n => n.Contains("reference", StringComparison.OrdinalIgnoreCase)).ToList();
        if (preferred.Count == 1) { why = string.Empty; return preferred[0]; }
        if (theirs.Count == 1) { why = string.Empty; return theirs[0]; }

        why = "more than one model here could be the reference, and choosing between them is not " +
              "this tool's call: " + string.Join(", ", theirs) + ".";
        return null;
    }

    private static bool ContainsGeneratedObjectName(string text)
    {
        for (int i = 0; i + 3 < text.Length; i++)
        {
            if (text[i] != '"') continue;
            if (text[i + 1] != 'K') continue;
            if ("WCPFSO".IndexOf(text[i + 2], StringComparison.Ordinal) < 0) continue;
            if (!char.IsDigit(text[i + 3])) continue;

            int j = i + 4;
            while (j < text.Length && char.IsDigit(text[j])) j++;
            if (j < text.Length && text[j] == '"') return true;
        }

        return false;
    }
}
