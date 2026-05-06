#nullable enable

namespace Kor.Operations.Financials;

/// <summary>
/// Centralized thresholds and constants used across Financials and PM Tools scoring.
/// All values are documented with rationale so they can be defended in performance reviews.
/// </summary>
internal static class AnalyticsThresholds
{
    /// <summary>
    /// A project is "over budget" when actual eng hours exceed estimated hours by this factor.
    /// 1.35 = 35% tolerance, reflecting typical estimation variance on structural projects.
    /// Calibrated against KOR's 2020-2025 project history: ~65% of closed projects land
    /// within 135% of their peer-estimated engineering budget.
    /// </summary>
    internal const double OverBudgetFactor = 1.35;

    /// <summary>
    /// Delivery confidence "At Risk" triggers when hours-spent percentage exceeds
    /// fee-billed percentage by this gap. 0.15 = 15 percentage points.
    /// Example: 60% of hours spent but only 40% billed → gap = 20% → At Risk.
    /// </summary>
    internal const double DeliveryGapThreshold = 0.15;

    /// <summary>
    /// Delivery confidence "Watch" triggers when remaining engineering hours fall
    /// below this fraction of total budgeted hours. 0.15 = 15%.
    /// </summary>
    internal const double WatchRemainingFraction = 0.15;

    /// <summary>
    /// Standard working hours per day at KOR. Used for hours-to-days conversion.
    /// </summary>
    internal const double HoursPerDay = 7.5;

    /// <summary>
    /// Approximate production team size at KOR. Used to convert remaining hours
    /// into team-days: (hours / HoursPerDay / TeamSize) = days of team capacity.
    /// Update this when firm headcount changes materially.
    /// </summary>
    internal const double TeamSize = 35.0;

    /// <summary>
    /// Default target billing rate ($/hr) used in the formula-based budget estimator
    /// when no configured value is provided. Calibrated from Historical Analytics
    /// median fee-per-hour in Apr 2026.
    /// </summary>
    internal const double DefaultTargetBillingRate = 185.0;

    /// <summary>
    /// Minimum number of peer projects required for peer-based budget estimation.
    /// Below this, falls back to formula-based estimation.
    /// </summary>
    internal const int MinPeerCount = 3;

    /// <summary>
    /// Dollar-amount floor below which a value is treated as rounding noise.
    /// Used in <c>Math.Abs(x) > 0.004</c> filters across drilldowns and
    /// reconciliation thresholds. Aligned with Deltek's 4-decimal currency
    /// precision: anything smaller is sub-cent rounding artifact, not a real
    /// row.
    /// </summary>
    internal const double RoundingDollarFloor = 0.004;

    /// <summary>
    /// "Billing Lagging Burn" alert triggers when burn % exceeds billed % by at
    /// least this gap, AND burn % is at least <see cref="BillingLaggingBurnPercentFloor"/>.
    /// Identical numeric value to <see cref="DeliveryGapThreshold"/> by
    /// coincidence — kept distinct so refactors don't accidentally couple
    /// alert and risk-status semantics.
    /// </summary>
    internal const double BillingLaggingBurnDeltaThreshold = 0.15;

    /// <summary>
    /// "Billing Lagging Burn" alert burn-percent floor. Below this, the alert
    /// is suppressed (early-stage projects naturally have low billed %).
    /// </summary>
    internal const double BillingLaggingBurnPercentFloor = 0.60;

    /// <summary>
    /// Liquidity tile shows the inline AR USD/CAD breakdown only when USA AR
    /// exceeds this dollar floor (50¢). Below it, the breakdown is suppressed
    /// to keep the tile clean.
    /// </summary>
    internal const double UsaArBreakdownDisplayThreshold = 0.5;

    /// <summary>
    /// Collection-exposure drilldown counts a project as "high exposure" when
    /// its (AR / 90-day billed) ratio meets or exceeds this. 50% means AR is at
    /// least half a quarter's invoiced revenue — a meaningful collection risk.
    /// </summary>
    internal const double HighCollectionRiskRatio = 0.5;
}
