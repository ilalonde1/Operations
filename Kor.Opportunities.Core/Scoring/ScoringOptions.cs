#nullable enable
using System;
using System.Collections.Generic;

namespace Kor.Opportunities.Core.Scoring;

/// <summary>
/// Tunables for the rules-based opportunity scorer. Persisted as JSON in
/// <c>opportunities.ScoringProfile.ValueJson</c>; cached in memory for ~10 s
/// by <see cref="IScoringOptionsAccessor"/>.
///
/// Single source of truth for hard-reject lists, term weights, and tier
/// thresholds — Contract Radar duplicated these across the scorer and the
/// admin controller and they drifted (plan §16 risk #3). Don't replicate.
/// </summary>
public sealed class ScoringOptions
{
    public Dictionary<string, decimal> PositiveTermWeights { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, decimal> NegativeTermWeights { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Region-name weights (positive = in-footprint, negative = out-of-scope).
    /// Combined into the same scoring sum as <see cref="PositiveTermWeights"/>.</summary>
    public Dictionary<string, decimal> RegionTermWeights { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Substrings that, when present anywhere in the scored text, short-circuit
    /// to <see cref="HardRejectScore"/>. Used for "country outside Canada/USA".</summary>
    public string[] HardRejectCountryTerms { get; init; } = Array.Empty<string>();

    public decimal MinScore { get; init; } = -100m;
    public decimal MaxScore { get; init; } = 100m;
    public decimal HardRejectScore { get; init; } = -100m;

    public decimal RepeatDeveloperBonus { get; init; } = 5m;

    /// <summary>Bonus when the submission deadline is at least
    /// <see cref="DeadlineWarningWindowDays"/> away. 0 disables the positive signal.</summary>
    public decimal DeadlineFeasibleBonus { get; init; } = 0m;

    /// <summary>Penalty when the deadline is closer than <see cref="DeadlineWarningWindowDays"/>.</summary>
    public decimal DeadlineCrunchPenalty { get; init; } = 5m;

    public int DeadlineWarningWindowDays { get; init; } = 14;

    /// <summary>If &gt; 0, opportunities with EstimatedValue strictly below this
    /// CAD value are hard-rejected. 0 disables the threshold.</summary>
    public decimal MinimumValueThresholdCad { get; init; } = 0m;

    // Tier bucketing thresholds (inclusive lower bounds).
    public decimal TierHighThreshold { get; init; } = 30m;
    public decimal TierMediumThreshold { get; init; } = 15m;
    public decimal TierLowThreshold { get; init; } = 0m;

    /// <summary>
    /// Returns a fresh <see cref="ScoringOptions"/> seeded with KOR's ratified
    /// defaults from the architecture plan §6. Used as the bootstrap value when
    /// <c>opportunities.ScoringProfile</c> has no row yet.
    /// </summary>
    public static ScoringOptions KorDefaults() => new()
    {
        PositiveTermWeights = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["structural"] = 12m,
            ["structural engineer"] = 5m,
            ["structural engineering"] = 5m,
            ["engineer of record"] = 8m,
            ["EOR"] = 8m,
            ["schedule B"] = 10m,
            ["schedule C-B"] = 10m,
            ["letter of assurance"] = 10m,
            ["field review"] = 6m,
            ["seismic"] = 7m,
            ["seismic upgrade"] = 7m,
            ["seismic retrofit"] = 7m,
            ["mass timber"] = 6m,
            ["CLT"] = 6m,
            ["GLT"] = 6m,
            ["wood-frame"] = 5m,
            ["wood frame"] = 5m,
            ["concrete"] = 4m,
            ["post-tensioned"] = 4m,
            ["cast-in-place"] = 4m,
            ["mid-rise"] = 5m,
            ["high-rise"] = 5m,
            ["multi-family residential"] = 5m,
            ["multifamily"] = 5m,
            ["building inspection"] = 6m,
            ["building inspector"] = 6m,
            ["tenant improvement"] = 2m,
            ["mixed-use"] = 3m,
        },

        NegativeTermWeights = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["electrical only"] = 15m,
            ["MEP only"] = 15m,
            ["mechanical only"] = 15m,
            ["HVAC design"] = 15m,
            ["architectural only"] = 12m,
            ["architecture only"] = 12m,
            ["interior design only"] = 12m,
            ["geotechnical only"] = 10m,
            ["surveying only"] = 12m,
            ["land surveyor"] = 12m,
            ["landscape architecture"] = 10m,
            ["environmental remediation"] = 15m,
            ["marine engineering"] = 12m,
            ["coastal engineering"] = 12m,
            ["bridge design"] = 8m,
            ["water treatment"] = 12m,
            ["wastewater treatment"] = 12m,
            ["transportation engineering"] = 12m,
            ["traffic engineering"] = 12m,
            ["hydroelectric"] = 15m,
            ["dam engineering"] = 15m,
        },

        RegionTermWeights = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            // BC + Lower Mainland (HQ)
            ["British Columbia"] = 8m,
            ["BC"] = 8m,
            ["B.C."] = 8m,
            ["Vancouver"] = 10m,
            ["Surrey"] = 10m,
            ["Burnaby"] = 10m,
            ["Coquitlam"] = 10m,
            ["Richmond"] = 10m,
            ["Langley"] = 10m,
            ["Delta"] = 10m,
            ["North Vancouver"] = 10m,
            ["West Vancouver"] = 10m,
            ["New Westminster"] = 10m,
            ["Maple Ridge"] = 10m,
            ["Pitt Meadows"] = 10m,
            ["Port Moody"] = 10m,
            ["Port Coquitlam"] = 10m,
            ["White Rock"] = 10m,
            // Vancouver Island
            ["Victoria"] = 8m,
            ["Saanich"] = 8m,
            ["Nanaimo"] = 8m,
            ["Duncan"] = 8m,
            ["Cowichan"] = 8m,
            // Sea-to-Sky
            ["Whistler"] = 8m,
            ["Squamish"] = 8m,
            ["Pemberton"] = 8m,
            ["Sea to Sky"] = 8m,
            // Okanagan
            ["Kelowna"] = 6m,
            ["West Kelowna"] = 6m,
            ["Vernon"] = 6m,
            ["Kamloops"] = 6m,
            ["Penticton"] = 6m,
            ["Lake Country"] = 6m,
            ["Okanagan"] = 6m,
            // Alberta (Edmonton focus)
            ["Alberta"] = 6m,
            ["Edmonton"] = 10m,
            ["Calgary"] = 3m,
            // SoCal — LA basin
            ["California"] = 6m,
            ["Los Angeles"] = 10m,
            ["Long Beach"] = 10m,
            ["Pasadena"] = 10m,
            ["Burbank"] = 10m,
            ["Glendale"] = 10m,
            ["Santa Monica"] = 10m,
            ["Culver City"] = 10m,
            ["West Hollywood"] = 10m,
            // Orange County
            ["Orange County"] = 10m,
            ["Anaheim"] = 10m,
            ["Santa Ana"] = 10m,
            ["Irvine"] = 10m,
            ["Huntington Beach"] = 10m,
            ["Newport Beach"] = 10m,
            ["Costa Mesa"] = 10m,
            ["Fullerton"] = 10m,
            // San Diego
            ["San Diego"] = 10m,
            ["Chula Vista"] = 10m,
            ["Carlsbad"] = 10m,
            ["Oceanside"] = 10m,
            ["Escondido"] = 10m,
            // Inland Empire
            ["Riverside"] = 6m,
            ["San Bernardino"] = 6m,
            ["Ontario CA"] = 6m,
            ["Rancho Cucamonga"] = 6m,
            ["Fontana"] = 6m,
            ["Moreno Valley"] = 6m,
            ["Corona"] = 6m,
            // Out-of-scope provinces (negative weights)
            ["Saskatchewan"] = -15m,
            ["Manitoba"] = -15m,
            ["Quebec"] = -15m,
            ["New Brunswick"] = -15m,
            ["Nova Scotia"] = -15m,
            ["Newfoundland"] = -15m,
            // Out-of-scope US states
            ["Nevada"] = -10m,
            ["Arizona"] = -10m,
            ["Texas"] = -10m,
            ["Washington state"] = -10m,
            ["Oregon"] = -10m,
            // Northern California - penalize but don't hard-reject
            ["Northern California"] = -8m,
            ["San Francisco"] = -8m,
            ["Bay Area"] = -8m,
            ["Sacramento"] = -8m,
            ["Oakland"] = -8m,
        },

        HardRejectCountryTerms = new[]
        {
            // Country names well outside Canada/USA. Match is substring + case-insensitive,
            // so include unambiguous forms only. "USA" / "Canada" / "United States" are NOT
            // here on purpose.
            "Mexico",
            "Brazil",
            "Argentina",
            "European Union",
            "United Kingdom",
            "Germany",
            "France",
            "Spain",
            "Italy",
            "Netherlands",
            "Australia",
            "New Zealand",
            "Japan",
            "China",
            "India",
            "Singapore",
            "Saudi Arabia",
            "United Arab Emirates",
            "South Africa",
        },
    };
}
