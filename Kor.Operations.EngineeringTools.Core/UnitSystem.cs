#nullable enable

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>Unit system a takeoff is expressed in. Metric = m³ / kg / m² / kg·m⁻³;
    /// Imperial = yd³ / lb / ft² / lb·yd⁻³ (the US structural convention, e.g. Lindley).</summary>
    public enum UnitSystem
    {
        Metric,
        Imperial
    }

    /// <summary>
    /// Explicit, both-direction unit conversions for takeoff quantities. Every factor is named by
    /// its direction so the caller can never apply it backwards (the density factor in particular
    /// was a documented foot-gun: 0.593276 is lb/yd³→kg/m³, NOT kg/m³→lb/yd³).
    /// </summary>
    public static class TakeoffUnits
    {
        // Mass
        public const double KgToLb = 2.2046226218487757;
        public const double LbToKg = 0.45359237;

        // Volume
        public const double M3ToYd3 = 1.3079506193143922;
        public const double Yd3ToM3 = 0.764554857984;

        // Area
        public const double M2ToFt2 = 10.763910416709722;
        public const double Ft2ToM2 = 0.09290304;

        // Density (mass per unit volume): kg/m³ ↔ lb/yd³.
        // kg/m³ → lb/yd³ multiplies by (kg→lb) and by (m³ per yd³ = yd³→m³).
        public const double KgPerM3ToLbPerYd3 = KgToLb * Yd3ToM3;   // ≈ 1.685555
        public const double LbPerYd3ToKgPerM3 = LbToKg * M3ToYd3;   // ≈ 0.593276

        public static double Mass(double value, UnitSystem from, UnitSystem to)
            => Pick(value, from, to, KgToLb, LbToKg);

        public static double Volume(double value, UnitSystem from, UnitSystem to)
            => Pick(value, from, to, M3ToYd3, Yd3ToM3);

        public static double Area(double value, UnitSystem from, UnitSystem to)
            => Pick(value, from, to, M2ToFt2, Ft2ToM2);

        public static double Density(double value, UnitSystem from, UnitSystem to)
            => Pick(value, from, to, KgPerM3ToLbPerYd3, LbPerYd3ToKgPerM3);

        private static double Pick(double value, UnitSystem from, UnitSystem to, double metricToImperial, double imperialToMetric)
        {
            if (from == to) return value;
            return from == UnitSystem.Metric ? value * metricToImperial : value * imperialToMetric;
        }
    }
}
