using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// THE CLASSIFIER ON ONE VIEW'S DXF, PRINTED (2026-09-18 13:20). What the composer was handed for a view and what it
/// made of it: every slab and opening with its plan box, and every flag - the instrument for "the DXF holds the loop
/// and the model has no opening there" (31065's elevator shaft, X'd on the plan, on the slab-edge layer of the DXF,
/// absent from L10). Silent unless <c>KOR_CLASSIFY_DXF</c> names a DXF written by the PDF route (millimetres, KOR
/// layers); the rules come from KorStandards as the composer takes them.
/// <code>KOR_CLASSIFY_DXF="...\S2.10.1_2_LEVEL 6 PLAN.dxf" dotnet test --filter FullyQualifiedName~ClassifyProbe</code>
/// The listing goes to TestResults/classify-probe/&lt;dxf name&gt;.txt.
/// </summary>
public sealed class ClassifyProbe
{
    [Fact]
    public void ClassifyOneViewsDxfAndPrintWhatItMade()
    {
        string? dxf = Environment.GetEnvironmentVariable("KOR_CLASSIFY_DXF");
        if (string.IsNullOrWhiteSpace(dxf)) return;
        Assert.True(File.Exists(dxf), dxf);
        string? conn = Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(conn), $"{RuleSettings.ConnectionEnvironmentVariable} is not set");

        var banked = RuleSettings.LoadRequired(conn!, DxfToEtabsService.RequiredRuleKeys);
        // as the composer takes them for a PDF-route view: the reader paired the faces, the model is in millimetres
        var options = DxfToEtabsService.ApplyRules(new PlanClassificationOptions { PairOpenFaces = false }, banked).InUnitOf(1.0 / 25.4);
        var segments = DxfPlanReader.ReadSegments(dxf);
        var tags = DxfPlanReader.ReadPositionedTags(dxf);
        var set = StructuralPlanClassifier.Classify(segments, options, sheet: null, tags: tags);

        var lines = new List<string>
        {
            $"{Path.GetFileName(dxf)}: {segments.Count} segments, {tags.Count} tags; slabs {set.Slabs.Count}, openings {set.Openings.Count}, walls {set.Walls.Count}, columns {set.Columns.Count}",
            $"options: MinSlabArea {options.MinSlabArea:N0} (unit {options.UnitInInches:0.####} in), MinOpeningSpan {options.MinOpeningSpan:N0}, slab layers {string.Join(",", options.SlabLayerPatterns)}",
            "slabs (sq ft, plan box mm, points):",
        };
        foreach (var s in set.Slabs.OrderByDescending(s => s.Area))
            lines.Add($"  slab     {s.Area / 92903.04,10:N0}  ({s.Points.Min(p => p.X):N0}..{s.Points.Max(p => p.X):N0}, {s.Points.Min(p => p.Y):N0}..{s.Points.Max(p => p.Y):N0})  {s.Points.Count} pts  {s.Layer}{(s.ThicknessInchesFromTag is { } t ? $"  {t}\"" : "")}");
        lines.Add("openings:");
        foreach (var o in set.Openings.OrderByDescending(o => o.Area))
            lines.Add($"  opening  {o.Area / 92903.04,10:N0}  ({o.Points.Min(p => p.X):N0}..{o.Points.Max(p => p.X):N0}, {o.Points.Min(p => p.Y):N0}..{o.Points.Max(p => p.Y):N0})  {o.Points.Count} pts  {o.Layer}");
        lines.Add("flags:");
        lines.AddRange(set.Flags.Select(f => "  " + f));

        // the slab layer alone through the classifier: what the other layers change
        var slabSegments = segments.Where(s => PlanClassificationOptions.Matches(s.Layer, options.SlabLayerPatterns)).ToList();
        var alone = StructuralPlanClassifier.Classify(slabSegments, options, sheet: null, tags: tags);
        lines.Add($"the slab layer alone ({slabSegments.Count} segments): slabs {alone.Slabs.Count}, openings {alone.Openings.Count}: {string.Join(" | ", alone.Openings.Select(o => $"{o.Area / 92903.04:N0} sq ft {o.Points.Count} pts"))}; flags: {string.Join(" | ", alone.Flags.Where(f => f.Contains("close", StringComparison.Ordinal)))}");
        var byDefaults = StructuralPlanClassifier.Classify(slabSegments, new PlanClassificationOptions { PairOpenFaces = false }.InUnitOf(1.0 / 25.4), sheet: null, tags: tags);
        lines.Add($"the slab layer alone under the compiled defaults: openings {byDefaults.Openings.Count}: {string.Join(" | ", byDefaults.Openings.Select(o => $"{o.Area / 92903.04:N0} sq ft {o.Points.Count} pts"))}");
        lines.Add("slab-layer segments as read, in order: " + string.Join(" ", slabSegments.Select(s => $"({s.Start.X:0.#},{s.Start.Y:0.#})-({s.End.X:0.#},{s.End.Y:0.#})")));
        var bare = slabSegments.Select(s => new DxfSegment(s.Layer, s.Start, s.End)).ToList();
        var byBare = StructuralPlanClassifier.Classify(bare, new PlanClassificationOptions { PairOpenFaces = false }.InUnitOf(1.0 / 25.4), sheet: null, tags: null);
        lines.Add($"the same segments rebuilt bare (layer, start, end), no tags, defaults: openings {byBare.Openings.Count}: {string.Join(" | ", byBare.Openings.Select(o => $"{o.Area / 92903.04:N0} sq ft {o.Points.Count} pts"))}; FromCurve set on {slabSegments.Count(s => s.FromCurve)} of the read segments");
        // the slab layer's loops as the builder makes them, before the classifier decides what each is
        var built = new PlanLoopBuilder(options.JoinTolerance, options.BridgeTolerance, options.ExtendLimit).Build(slabSegments);
        lines.Add($"slab-layer loops built from {slabSegments.Count} segments: {built.Loops.Count} closed, {built.OpenChains.Count} open (sq ft, plan box mm, points; MinSlabArea {options.MinSlabArea / 92903.04:N0} sq ft):");
        foreach (var l in built.Loops.OrderByDescending(l => l.Area))
        {
            lines.Add($"  loop     {l.Area / 92903.04,10:N0}  ({l.Points.Min(p => p.X):N0}..{l.Points.Max(p => p.X):N0}, {l.Points.Min(p => p.Y):N0}..{l.Points.Max(p => p.Y):N0})  {l.Points.Count} pts{(l.Area < options.MinSlabArea ? "  UNDER MinSlabArea" : "")}");
            // what the self-touch split makes of it, at the classifier's tolerance
            var rings = LoopGeometry.SplitSelfCrossings(l.Points, options.OutlineSelfTouchTolerance);
            if (rings.Count > 1 || l.Points.Count > 6)
            {
                lines.Add($"    points: {string.Join(" ", l.Points.Select(p => $"({p.X:N0},{p.Y:N0})"))}");
                foreach (var r in rings)
                {
                    var pl = new PlanLoop(l.Layer, r, closedExactly: true);
                    lines.Add($"    ring   {pl.Area / 92903.04,10:N0}  ({r.Min(p => p.X):N0}..{r.Max(p => p.X):N0}, {r.Min(p => p.Y):N0}..{r.Max(p => p.Y):N0})  {r.Count} pts");
                }
            }
        }
        foreach (var c in built.OpenChains)
            lines.Add($"  open chain {c.Count} pts  ({c.Min(p => p.X):N0}..{c.Max(p => p.X):N0}, {c.Min(p => p.Y):N0}..{c.Max(p => p.Y):N0})");

        string dir = Path.Combine(AppContext.BaseDirectory, "TestResults", "classify-probe");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, Path.GetFileNameWithoutExtension(dxf) + ".txt"), lines);
    }
}
