#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>What a sheet is, as read from its title block.</summary>
    public enum SheetKind
    {
        Framing,     // suspended floor / concrete-outline plan
        Foundation,  // mat / footing / parkade slab-on-grade plan
        Schedule,    // column / wall / beam schedule
        Detail,      // sections and details
        Other
    }

    /// <summary>
    /// One concrete-outline plate the vision layer located on a sheet: which level(s) it represents,
    /// the element kind, the read thickness, and a NORMALISED (0..1) bounding box around just that
    /// plate. The box is deliberately coarse — the deterministic geometry then measures the largest
    /// enclosed plate-cluster inside it, so the vision layer never has to be pixel-accurate (and a
    /// box that clips a neighbouring plate is rejected by the clustering, not double-counted).
    /// </summary>
    public sealed record PlateReading(
        string Level,
        int Count,
        TakeoffElementType Element,
        string? Variant,
        double NormX0,
        double NormY0,
        double NormX1,
        double NormY1,
        double? ThicknessIn,
        double Confidence);

    /// <summary>The vision layer's reading of one sheet.</summary>
    public sealed record SheetReading(
        SheetKind Kind,
        string? ScaleNote,
        IReadOnlyList<PlateReading> Plates,
        string? Notes);

    /// <summary>
    /// Parses the structured JSON the vision tool returns into a <see cref="SheetReading"/>. Pure and
    /// defensive: unknown enums degrade safely, boxes are clamped to [0,1] and ordered, counts and
    /// confidences are sanitised. Kept separate from the HTTP call so the whole vision contract is
    /// unit-testable against canned JSON without spending a request.
    /// </summary>
    public static class PlanVisionParser
    {
        /// <summary>An empty, safe reading — returned when the JSON root is not the expected object.</summary>
        private static readonly SheetReading Empty =
            new(SheetKind.Other, null, Array.Empty<PlateReading>(), null);

        public static SheetReading Parse(string json)
        {
            ArgumentNullException.ThrowIfNull(json);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // The tool contract is a JSON object; anything else (array, scalar, garbage that still
            // parses) yields no plates rather than throwing — one odd response can't abort a batch.
            if (root.ValueKind != JsonValueKind.Object) return Empty;

            var kind = ParseKind(GetString(root, "kind"));
            string? scaleNote = GetString(root, "scaleNote");
            string? notes = GetString(root, "notes");

            var plates = new List<PlateReading>();
            if (root.TryGetProperty("plates", out var platesEl) && platesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in platesEl.EnumerateArray())
                {
                    if (p.ValueKind != JsonValueKind.Object) continue;
                    plates.Add(ParsePlate(p));
                }
            }
            return new SheetReading(kind, NullIfBlank(scaleNote), plates, NullIfBlank(notes));
        }

        private static PlateReading ParsePlate(JsonElement p)
        {
            string level = GetString(p, "level") ?? "";
            int count = Math.Max(1, GetInt(p, "count", 1));
            var element = ParseElement(GetString(p, "element"));
            string? variant = NullIfBlank(GetString(p, "variant"));
            double? thickness = GetNullableDouble(p, "thicknessIn");
            if (thickness is <= 0) thickness = null;
            double confidence = Math.Clamp(GetDouble(p, "confidence", 0.0), 0.0, 1.0);

            // Default to a DEGENERATE box (zero-area), not the whole sheet: a plate the vision layer
            // failed to locate must be skipped by the caller, never measured over the entire page
            // (which would silently swallow every plate and stray region on it). A real box overwrites.
            double x0 = 0, y0 = 0, x1 = 0, y1 = 0;
            if (p.TryGetProperty("box", out var box) && box.ValueKind == JsonValueKind.Array)
            {
                var v = new List<double>(4);
                foreach (var e in box.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.Number) v.Add(e.GetDouble());
                if (v.Count >= 4) { x0 = v[0]; y0 = v[1]; x1 = v[2]; y1 = v[3]; }
            }
            // clamp to [0,1] and order so x0<x1, y0<y1
            x0 = Math.Clamp(x0, 0, 1); y0 = Math.Clamp(y0, 0, 1);
            x1 = Math.Clamp(x1, 0, 1); y1 = Math.Clamp(y1, 0, 1);
            if (x1 < x0) (x0, x1) = (x1, x0);
            if (y1 < y0) (y0, y1) = (y1, y0);

            return new PlateReading(level, count, element, variant, x0, y0, x1, y1, thickness, confidence);
        }

        private static SheetKind ParseKind(string? raw) => (raw ?? "").Trim().ToLowerInvariant() switch
        {
            "framing" or "floor" or "slab" => SheetKind.Framing,
            "foundation" or "mat" or "parkade" or "footing" => SheetKind.Foundation,
            "schedule" => SheetKind.Schedule,
            "detail" or "section" => SheetKind.Detail,
            _ => SheetKind.Other,
        };

        private static TakeoffElementType ParseElement(string? raw) => (raw ?? "").Trim().ToLowerInvariant() switch
        {
            "wall" => TakeoffElementType.Wall,
            "beam" or "framing" => TakeoffElementType.Beam,
            "column" => TakeoffElementType.Column,
            "foundation" or "footing" or "mat" => TakeoffElementType.Foundation,
            "droppanel" or "drop" => TakeoffElementType.DropPanel,
            _ => TakeoffElementType.Slab,
        };

        private static string? GetString(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static int GetInt(JsonElement e, string name, int fallback)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : fallback;

        private static double GetDouble(JsonElement e, string name, double fallback)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;

        private static double? GetNullableDouble(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

        private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
