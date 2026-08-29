using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.Dxf;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// What a DXF actually contains, read with the SHIPPED reader — `DxfPlanReader`, the one the
/// DXF-to-ETABS intake uses.
///
/// Every test here was scanning the group-code stream by hand: find a line "8", take the next line
/// as a layer name. That is wrong twice over. It is wrong in fact — group code 8 means "layer" only
/// inside ENTITIES, and elsewhere the digit 8 turns up as a VALUE (grey is AutoCAD colour 8), so the
/// scanner invented layers called "6", "10" and "66" out of whatever group code followed. And it is
/// wrong in kind: reading our own output with our own ad-hoc parser proves the parser and the writer
/// agree, which is not the question. The question is whether the reader on the far side can use it.
///
/// So these read the file the way the consumer does. If `DxfPlanReader` cannot see a layer, that
/// layer does not exist as far as anything downstream is concerned, and a test that says otherwise
/// is lying in the direction of comfort.
/// </summary>
internal static class DxfFacts
{
    /// <summary>Every layer carrying geometry or text, as the ETABS-side reader sees them.</summary>
    public static IReadOnlyList<string> Layers(string dxfPath)
    {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in DxfPlanReader.ReadSegments(dxfPath)) found.Add(s.Layer);
        foreach (var t in DxfPlanReader.ReadPositionedTags(dxfPath)) found.Add(t.Layer);
        return found.ToList();
    }

    /// <summary>Segment count per layer — what is actually ON each one, not merely declared.</summary>
    public static IReadOnlyDictionary<string, int> SegmentsByLayer(string dxfPath) =>
        DxfPlanReader.ReadSegments(dxfPath)
            .GroupBy(s => s.Layer, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The words, with the layer each sits on.</summary>
    public static IReadOnlyList<(string Text, string Layer)> Tags(string dxfPath) =>
        DxfPlanReader.ReadPositionedTags(dxfPath).Select(t => (t.Text, t.Layer)).ToList();

    /// <summary>Inches per drawing unit, or null when the file declares no $INSUNITS — in which
    /// case DxfToEtabsService refuses it outright.</summary>
    public static double? UnitInInches(string dxfPath) => DxfPlanReader.UnitInInches(dxfPath);

    public static string Describe(string dxfPath)
    {
        var by = SegmentsByLayer(dxfPath);
        var tags = Tags(dxfPath);
        var text = tags.GroupBy(t => t.Layer, StringComparer.OrdinalIgnoreCase)
                       .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var all = new SortedSet<string>(by.Keys, StringComparer.OrdinalIgnoreCase);
        all.UnionWith(text.Keys);

        return string.Join("  ", all.Select(l =>
            $"{l}[{(by.TryGetValue(l, out int s) ? s : 0)} seg" +
            (text.TryGetValue(l, out int w) ? $", {w} txt]" : "]")));
    }
}
