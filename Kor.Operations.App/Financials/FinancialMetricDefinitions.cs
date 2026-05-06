#nullable enable
#pragma warning disable SA1649
using System;
using System.Collections.Generic;

namespace Kor.Operations.Financials
{
    public sealed class FinancialMetricDefinition
    {
        public string Key { get; init; } = "";
        public string Category { get; init; } = "Financial";
        public string DisplayName { get; init; } = "";
        public string Description { get; init; } = "";
        public string Formula { get; init; } = "";
    }

    /// <summary>
    /// Catalog of every metric the Financials UI surfaces — descriptions,
    /// formulas, and category labels feeding tooltips and the metric
    /// dictionary window. Definitions are split per-section across partial
    /// files in MetricDefinitions/ so each module's metrics live next to
    /// each other and the master file stays under 100 lines.
    /// </summary>
    internal static partial class FinancialMetricDefinitions
    {
        internal static readonly Dictionary<string, FinancialMetricDefinition> Definitions = BuildDefinitions();

        private static Dictionary<string, FinancialMetricDefinition> BuildDefinitions()
        {
            var d = new Dictionary<string, FinancialMetricDefinition>(StringComparer.OrdinalIgnoreCase);
            AddCoreMetrics(d);
            AddExecutiveMetrics(d);
            AddAlertMetrics(d);
            AddPortfolioMetrics(d);
            AddGlPnLMetrics(d);
            AddPmToolsMetrics(d);
            AddStaffUtilizationMetrics(d);
            AddBillingManagerMetrics(d);
            AddHistoricalMetrics(d);
            return NormalizeDefinitions(d);
        }

        private static Dictionary<string, FinancialMetricDefinition> NormalizeDefinitions(
            Dictionary<string, FinancialMetricDefinition> source)
        {
            var normalized = new Dictionary<string, FinancialMetricDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in source)
            {
                var def = kv.Value ?? new FinancialMetricDefinition { Key = kv.Key, DisplayName = kv.Key };
                normalized[kv.Key] = new FinancialMetricDefinition
                {
                    Key = string.IsNullOrWhiteSpace(def.Key) ? kv.Key : def.Key,
                    Category = string.IsNullOrWhiteSpace(def.Category) ? "Financial" : def.Category,
                    DisplayName = string.IsNullOrWhiteSpace(def.DisplayName) ? kv.Key : def.DisplayName,
                    Description = def.Description ?? string.Empty,
                    Formula = EnsureFormula(def.Description, def.Formula)
                };
            }
            return normalized;
        }

        private static string EnsureFormula(string? description, string? formula)
        {
            if (!string.IsNullOrWhiteSpace(formula))
                return formula.Trim();

            var how = ExtractHowCalculated(description);
            if (!string.IsNullOrWhiteSpace(how))
                return how;

            return "Calculation: see business definition.";
        }

        private static string ExtractHowCalculated(string? description)
        {
            var text = (description ?? string.Empty).Trim();
            if (text.Length == 0) return string.Empty;

            const string marker = "HOW IT IS CALCULATED:";
            var start = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return string.Empty;

            var after = text.Substring(start + marker.Length).Trim();
            if (after.Length == 0) return string.Empty;

            var nextSection = after.IndexOf("\n\n", StringComparison.Ordinal);
            if (nextSection >= 0)
                after = after.Substring(0, nextSection).Trim();

            return after;
        }

        internal static string? TryGetTooltipText(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            if (!Definitions.TryGetValue(key.Trim(), out var def) || def == null) return null;
            if (string.IsNullOrWhiteSpace(def.Description) && string.IsNullOrWhiteSpace(def.Formula)) return null;

            var desc = (def.Description ?? string.Empty).Trim();
            var formula = (def.Formula ?? string.Empty).Trim();
            if (desc.Length == 0 && formula.Length == 0) return null;
            if (formula.Length == 0) return desc.Length == 0 ? null : desc;
            if (desc.Length == 0) return $"Formula: {formula}";
            return $"{desc}\nFormula: {formula}";
        }
    }
}
