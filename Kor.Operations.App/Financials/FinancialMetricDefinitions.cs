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
            AddBilledPnLMetrics(d);
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

        // Sentinel emitted by EnsureFormula when neither Formula nor a HOW
        // section exists — skip it in AI snippets since "see business
        // definition" is noise, not methodology.
        private const string FormulaPlaceholder = "Calculation: see business definition.";

        /// <summary>
        /// Returns the HOW IT IS CALCULATED section + Formula for the given
        /// metric key, formatted for an AI context block. Returns null when
        /// the key is unknown or has no methodology text. Used by
        /// IAiContextProvider implementations so AI explanations cite KOR's
        /// actual methodology (predicates, exclusions, FX handling) instead
        /// of guessing at industry-standard formulas.
        /// </summary>
        internal static string? TryGetAiMethodology(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            var trimmedKey = key.Trim();
            if (!Definitions.TryGetValue(trimmedKey, out var def) || def == null) return null;
            var how = ExtractHowCalculated(def.Description);
            var formula = (def.Formula ?? string.Empty).Trim();
            bool hasFormula = formula.Length > 0
                && !string.Equals(formula, FormulaPlaceholder, StringComparison.Ordinal);
            if (string.IsNullOrEmpty(how) && !hasFormula) return null;

            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(how))
                sb.AppendLine($"    How: {how.Replace("\n", " ").Trim()}");
            if (hasFormula)
                sb.AppendLine($"    Formula: {formula}");

            // Cross-KPI relationships (Batch 68). If this KPI is derived from
            // or paired with other KPIs in the dictionary, name them so AI
            // can connect "Margin dropped" with "Net dropped while Revenue
            // held flat" without inventing the linkage. Only relationships
            // explicitly grounded in the Description text are encoded.
            if (KpiRelationships.TryGetValue(trimmedKey, out var rels) && rels.Length > 0)
            {
                sb.Append("    Related KPIs (often displayed together — read them alongside this one): ");
                for (int i = 0; i < rels.Length; i++)
                {
                    if (i > 0) sb.Append("; ");
                    var displayName = Definitions.TryGetValue(rels[i].OtherKey, out var od) && od != null
                        ? od.DisplayName
                        : rels[i].OtherKey;
                    sb.Append(displayName);
                    sb.Append(" (");
                    sb.Append(rels[i].OtherKey);
                    sb.Append(") — ");
                    sb.Append(rels[i].Note);
                }
                sb.AppendLine();
            }

            return sb.Length == 0 ? null : sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Reverse lookup DisplayName→Key (case-insensitive). Built once per
        /// process. Lets IAiContextProvider implementations resolve KPI
        /// titles (which is what their VMs already iterate) into dictionary
        /// keys without having to plumb a MetricKey field through every
        /// ExecutiveKpi / KpiCardVm record.
        /// </summary>
        private static readonly System.Lazy<Dictionary<string, string>> DisplayNameToKey =
            new(() =>
            {
                var m = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var kv in Definitions)
                    if (!string.IsNullOrWhiteSpace(kv.Value.DisplayName) && !m.ContainsKey(kv.Value.DisplayName))
                        m[kv.Value.DisplayName] = kv.Key;
                return m;
            });

        internal static string? TryResolveKeyFromDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return null;
            return DisplayNameToKey.Value.TryGetValue(displayName.Trim(), out var k) ? k : null;
        }

        /// <summary>
        /// Builds the "KPI methodology" block for a fixed set of dictionary keys
        /// (used by VMs whose headline numbers aren't a dynamic <c>Kpis</c>
        /// collection — Billed P&amp;L, GL P&amp;L, the active-projects view).
        /// Returns null when none of the keys produce methodology text. The
        /// caller is expected to prefix the result with the "KPI methodology
        /// (so you can explain how each number is calculated):" header
        /// recognized by the MCP system prompt (post-Batch 61).
        /// </summary>
        internal static string? BuildAiMethodologyBlock(IEnumerable<string> keys)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var key in keys)
            {
                if (!Definitions.TryGetValue(key, out var def) || def == null) continue;
                var block = TryGetAiMethodology(key);
                if (block == null) continue;
                sb.AppendLine($"  {def.DisplayName} ({key})");
                sb.AppendLine(block);
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        /// <summary>
        /// A directional pointer from one KPI to another that's part of the
        /// same conceptual story (numerator / denominator, dollar-vs-ratio
        /// twin, earned-vs-invoiced pair, pre-vs-post overhead). Note is a
        /// one-liner explaining HOW the two relate, so AI knows whether
        /// they move together, inversely, or as inputs to the same formula.
        /// </summary>
        internal sealed record KpiRelationship(string OtherKey, string Note);

        /// <summary>
        /// Map of each KPI key to other KPI keys it's directly tied to.
        /// Every entry below is grounded in explicit cross-reference text
        /// in the source FinancialMetricDefinition.Description — no
        /// guesswork. The map drives the "Related KPIs" line emitted by
        /// <see cref="TryGetAiMethodology"/>, which lets the AI panel
        /// answer "what's driving the change in X?" by pointing at the
        /// dictionary-confirmed inputs / pair, not industry-generic ones.
        /// </summary>
        internal static readonly Dictionary<string, KpiRelationship[]> KpiRelationships =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Exec_NetMultiplier"] = new[]
                {
                    new KpiRelationship("Exec_NetProfit", "dollar version of this same ratio (same NSR/DLC inputs, subtracted instead of divided)"),
                },
                ["Exec_NetProfit"] = new[]
                {
                    new KpiRelationship("Exec_NetMultiplier", "ratio version of this dollar amount (Labor Margin / DLC)"),
                    new KpiRelationship("Exec_GlNetIncome", "GL bottom-line — gap between the two = firm overhead burden over the trailing 12 months"),
                },
                ["Exec_GlNetIncome"] = new[]
                {
                    new KpiRelationship("Exec_NetProfit", "pre-overhead labor margin — gap to this number ≈ firm overhead burden"),
                },
                ["Exec_Revenue3090"] = new[]
                {
                    new KpiRelationship("Exec_Billed3090", "invoiced side of the same window — UnbilledGap = Earned (this) - Invoiced"),
                },
                ["Exec_Billed3090"] = new[]
                {
                    new KpiRelationship("Exec_Revenue3090", "earned side of the same window — UnbilledGap = Earned - Invoiced (this)"),
                },
                ["Billed_Net"] = new[]
                {
                    new KpiRelationship("Billed_Revenue", "numerator (Net = Revenue − Expenses)"),
                    new KpiRelationship("Billed_Expenses", "subtractor (Net = Revenue − Expenses)"),
                },
                ["Billed_Margin"] = new[]
                {
                    new KpiRelationship("Billed_Net", "numerator (Margin = Net / Revenue)"),
                    new KpiRelationship("Billed_Revenue", "denominator (Margin = Net / Revenue)"),
                },
                ["GlPnL_NetIncomePeriod"] = new[]
                {
                    new KpiRelationship("GlPnL_RevenuePeriod", "income side (Net = Revenue + Expenses, signed per GL convention)"),
                    new KpiRelationship("GlPnL_ExpensesPeriod", "expense side (Net = Revenue + Expenses, signed per GL convention)"),
                },
                ["GlPnL_NetMarginPeriod"] = new[]
                {
                    new KpiRelationship("GlPnL_NetIncomePeriod", "numerator"),
                    new KpiRelationship("GlPnL_RevenuePeriod", "denominator"),
                },
            };
    }
}
