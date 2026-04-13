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
}
