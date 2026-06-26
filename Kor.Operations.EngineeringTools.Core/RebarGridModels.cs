#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.EngineeringTools.RebarChange
{
    /// <summary>How a continuous reinforcing grid is laid out - sets the steel-per-m² multiplier.</summary>
    public enum GridLayout
    {
        EachWayBottom,    // bottom mat, both directions  -> 2 x (1/spacing)
        EachWayTopBottom, // top + bottom, both directions -> 4 x (1/spacing)
        EachFace,         // one direction, both faces (walls) -> 2 x (1/spacing)
    }

    /// <summary>
    /// A continuous "field" reinforcing grid read off a sheet, e.g. "15M @ 350 EACH WAY BOT.".
    /// Unlike a spot call-out, a grid governs a known extent (a floor plate, a wall face), so a
    /// change in it can be priced precisely once an area is supplied.
    /// </summary>
    public sealed record GridSpec(int BarSize, int SpacingMm, GridLayout Layout, string Raw)
    {
        /// <summary>Steel intensity in kg/m² of plan/wall area for this grid (exact, no area needed).</summary>
        public double AsKgPerM2(IReadOnlyDictionary<int, double> barMass)
        {
            if (!barMass.TryGetValue(BarSize, out double m)) return 0;
            double perDir = (1000.0 / SpacingMm) * m;       // bars/m x kg/m = kg/m² per direction-layer
            return Layout switch
            {
                GridLayout.EachWayBottom => 2 * perDir,
                GridLayout.EachWayTopBottom => 4 * perDir,
                GridLayout.EachFace => 2 * perDir,
                _ => perDir,
            };
        }

        public string Display => $"{BarSize}M@{SpacingMm} {LayoutText}";
        public string LayoutText => Layout switch
        {
            GridLayout.EachWayBottom => "EW bot",
            GridLayout.EachWayTopBottom => "EW T&B",
            GridLayout.EachFace => "EF",
            _ => "",
        };
    }

    /// <summary>A change in a sheet's field grid between two issues, priced where an area is known.</summary>
    public sealed record GridChange(
        string Sheet,
        string Title,
        string Kind,                 // "Slab grid" | "Wall grid"
        GridSpec? Before,
        GridSpec? After,
        double DeltaAsKgPerM2,       // exact from the PDF; negative = steel saved
        double? AreaM2,              // supplied (Revit level/wall area, or manual); null if not yet known
        double? DeltaKg)             // = DeltaAsKgPerM2 x AreaM2, when area is known
    {
        public double? DeltaLb => DeltaKg.HasValue ? DeltaKg.Value * 2.20462 : (double?)null;
    }

    public sealed record RebarPricedResult(
        IReadOnlyList<GridChange> Changes,
        double TotalKnownDeltaKg,    // sum where area supplied
        int PricedCount,
        int UnpricedCount,
        string BeforeLabel,
        string AfterLabel);
}
