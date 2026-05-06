#nullable enable
using System;
using System.Globalization;

namespace Kor.Operations.Financials;

// Single source of truth for the two FX-conversion primitives used across loaders,
// view-models, and KPI builders:
//   * IsUsaOrg(org)            — whether a row's PR.Org/CFGBanks.Org bucket is USD-denominated
//   * ParseUsdToCadRate(raw)   — config-string parser that rejects 0/negative and falls back to 1.36
//
// Before consolidation, IsUsaOrg lived in 4 files and was inlined in 3 more; the rate
// parser was duplicated 4× with subtly different fallbacks. Centralizing here keeps
// every FX rollup in the app on identical semantics.
internal static class OrgFx
{
    public const double DefaultUsdToCadRate = 1.36;

    public static bool IsUsaOrg(string? org)
        => !string.IsNullOrWhiteSpace(org)
           && org!.Trim().Equals("USA", StringComparison.OrdinalIgnoreCase);

    public static double ParseUsdToCadRate(string? raw, double fallback = DefaultUsdToCadRate)
    {
        if (!string.IsNullOrWhiteSpace(raw))
        {
            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 0m)
                return (double)v;
            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out v) && v > 0m)
                return (double)v;
        }
        return fallback;
    }
}
