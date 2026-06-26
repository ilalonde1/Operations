#nullable enable

using System.Collections.Generic;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// Reinforcing density keyed by (element, variant) for the absolute Structural Quantity Takeoff.
    /// "variant" distinguishes element sub-kinds that carry different reinforcing ratios — slab type
    /// (Parking / Residential / Roof …), wall kind (Shear / Other), or mat-foundation thickness.
    /// A null/unknown variant falls back to the element-level value.
    ///
    /// This is a NEW type. It deliberately does NOT touch <see cref="RebarDensityTable"/> or
    /// <c>RebarWeightEstimator.DefaultDensities</c>, whose values are pinned by existing tests.
    ///
    /// The table carries its own <see cref="UnitSystem"/>; densities and volumes must agree
    /// (kg/m³ × m³ = kg, or lb/yd³ × yd³ = lb). Convert with <see cref="TakeoffUnits.Density"/>.
    /// </summary>
    public sealed class StructuralDensityTable
    {
        private readonly Dictionary<(TakeoffElementType Element, string? Variant), double> _densities;

        public UnitSystem Unit { get; }

        public StructuralDensityTable(
            UnitSystem unit,
            IReadOnlyDictionary<(TakeoffElementType Element, string? Variant), double> densities)
        {
            Unit = unit;
            _densities = new Dictionary<(TakeoffElementType, string?), double>();
            foreach (var kv in densities)
                _densities[(kv.Key.Element, Normalize(kv.Key.Variant))] = kv.Value;
        }

        /// <summary>Density for an element/variant, in this table's <see cref="Unit"/>.
        /// Variant-specific value if present; otherwise the element default; otherwise 0.</summary>
        public double For(TakeoffElementType element, string? variant = null)
        {
            var v = Normalize(variant);
            if (v != null && _densities.TryGetValue((element, v), out var exact))
                return exact;
            if (_densities.TryGetValue((element, null), out var fallback))
                return fallback;
            return 0;
        }

        /// <summary>This table re-expressed in the other unit system (densities converted).</summary>
        public StructuralDensityTable In(UnitSystem unit)
        {
            if (unit == Unit) return this;
            var converted = new Dictionary<(TakeoffElementType, string?), double>();
            foreach (var kv in _densities)
                converted[kv.Key] = TakeoffUnits.Density(kv.Value, Unit, unit);
            return new StructuralDensityTable(unit, converted);
        }

        private static string? Normalize(string? variant)
            => string.IsNullOrWhiteSpace(variant) ? null : variant.Trim().ToLowerInvariant();

        /// <summary>
        /// KOR standard reinforcing ratios, IMPERIAL (lb/yd³), seeded from the verified Lindley
        /// takeoff. Editable/calibratable per project — these are the firm starting points.
        /// </summary>
        public static StructuralDensityTable KorImperialDefault => new(
            UnitSystem.Imperial,
            new Dictionary<(TakeoffElementType, string?), double>
            {
                [(TakeoffElementType.Column, null)] = 700,
                [(TakeoffElementType.Wall, "shear")] = 500,
                [(TakeoffElementType.Wall, "other")] = 285,
                [(TakeoffElementType.Wall, null)] = 285,
                [(TakeoffElementType.Slab, "roof")] = 200,
                [(TakeoffElementType.Slab, "residential")] = 110,
                [(TakeoffElementType.Slab, "parking")] = 170,
                [(TakeoffElementType.Slab, "podium")] = 200,
                [(TakeoffElementType.Slab, "ground")] = 250,
                [(TakeoffElementType.Slab, null)] = 170,
                [(TakeoffElementType.Foundation, "132in")] = 1100,
                [(TakeoffElementType.Foundation, "96in")] = 1100,
                [(TakeoffElementType.Foundation, "84in")] = 480,
                [(TakeoffElementType.Foundation, "60in")] = 360,
                [(TakeoffElementType.Foundation, "36in")] = 300,
                [(TakeoffElementType.Foundation, "24in")] = 300,
                [(TakeoffElementType.Foundation, null)] = 480,
            });

        /// <summary>KOR standard ratios in METRIC (kg/m³), derived from the imperial defaults.</summary>
        public static StructuralDensityTable KorMetricDefault => KorImperialDefault.In(UnitSystem.Metric);
    }
}
