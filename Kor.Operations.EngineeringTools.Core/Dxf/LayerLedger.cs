namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>One drawing layer, how much geometry it carries, and what the tool made of it.</summary>
public sealed record LayerEntry(string Layer, int Segments, string? Role)
{
    public bool Claimed => Role is not null;
}

/// <summary>
/// Every layer in a drawing set, with the role each was given.
///
/// The tool decides what a piece of linework IS from the layer it sits on, and the patterns it
/// matches on — WALL, _COL, SLABEDG — are KOR's own drafting convention. A drafter who names
/// columns anything else does not get an error: the columns are simply never seen, the model is
/// built without them, and every count agrees with itself because nothing was ever read.
///
/// That is the failure this closes. A role that ends up with nothing while unclaimed layers sit
/// there carrying thousands of segments is a naming mismatch, not a building without columns, and
/// the tool must say so with the candidate layer names rather than quietly produce half a model.
/// </summary>
public static class LayerLedger
{
    /// <summary>Counts the segments on every layer of a drawing set and assigns each layer its role.</summary>
    public static IReadOnlyList<LayerEntry> Build(
        IEnumerable<IReadOnlyList<DxfSegment>> sheets, PlanClassificationOptions options)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in sheets)
            foreach (var segment in sheet)
                counts[segment.Layer] = counts.GetValueOrDefault(segment.Layer) + 1;

        return counts
            .Select(kv => new LayerEntry(kv.Key, kv.Value, RoleOf(kv.Key, options)))
            .OrderByDescending(e => e.Segments)
            .ToList();
    }

    private static string? RoleOf(string layer, PlanClassificationOptions options)
        => options.RoleOf(layer);

    /// <summary>
    /// The roles that got nothing while unclaimed layers carry real geometry — the signature of a
    /// layer-naming mismatch rather than a building genuinely without that member.
    ///
    /// A role with nothing and no unclaimed geometry to explain it is left alone: a drawing set
    /// really can have no columns, and refusing that would be a different kind of wrong.
    /// </summary>
    public static IReadOnlyList<string> RolesMissingWithGeometryUnclaimed(
        IReadOnlyList<LayerEntry> ledger, int unclaimedSegmentsThatMatter = 200)
    {
        int unclaimed = ledger.Where(e => !e.Claimed).Sum(e => e.Segments);
        if (unclaimed < unclaimedSegmentsThatMatter) return Array.Empty<string>();

        return new[] { "walls", "columns", "slab edges" }
            .Where(role => !ledger.Any(e => e.Role == role))
            .ToList();
    }

    /// <summary>The ledger as report lines: what each layer was taken for, biggest first.</summary>
    public static IEnumerable<string> Describe(IReadOnlyList<LayerEntry> ledger, int topUnclaimed = int.MaxValue)
    {
        foreach (var entry in ledger.Where(e => e.Claimed))
            yield return $"{entry.Layer} -> {entry.Role} ({entry.Segments:N0} segments)";

        // A layer that names a member this tool does not model is not the same as a layer of
        // dimensions or hatching, and burying it in a list of forty-three ignored names hides it.
        //
        // Beams are the case in hand. They are deliberately out of scope: the concrete-outline
        // plans this reads do not carry framing — 31138's JBP_S_BEAM holds one entity per sheet,
        // 25 across 25 drawings — and the 27 beams in that engineer's own model were drawn by her.
        // Inventing them would mean inventing a depth as well as a position. But a job whose
        // drawings DO carry framing must be told it was skipped, rather than reading a clean report
        // and assuming the model is whole.
        var structural = ledger
            .Where(e => !e.Claimed && e.Segments >= 20)
            .Where(e => new[] { "BEAM", "JOIST", "BRACE", "TRUSS" }
                .Any(w => e.Layer.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var entry in structural)
            yield return $"{entry.Layer} carries {entry.Segments:N0} segments and names a member this tool does " +
                         "not model. Nothing from it is in the model — those members are yours.";

        var ignored = ledger.Where(e => !e.Claimed).Take(topUnclaimed).ToList();
        if (ignored.Count == 0) yield break;

        int total = ledger.Where(e => !e.Claimed).Sum(e => e.Segments);
        yield return $"read and ignored, on no structural layer: {total:N0} segments across " +
                     $"{ledger.Count(e => !e.Claimed)} layer(s) — " +
                     string.Join(", ", ignored.Select(e => $"{e.Layer} ({e.Segments:N0})"));
    }
}
