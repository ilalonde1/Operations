#nullable enable
using System;
using System.Collections.Generic;
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
public static class OrgFx
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

    public static IReadOnlyDictionary<int, (double Rate, bool IsProvisional)> ParseUsdToCadRateTable(string? raw)
    {
        var result = new Dictionary<int, (double Rate, bool IsProvisional)>();
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        foreach (var entry in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
                continue;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
                continue;

            var rateRaw = parts[1].Trim();
            var isProvisional = rateRaw.StartsWith("~", StringComparison.Ordinal);
            if (isProvisional)
                rateRaw = rateRaw.Substring(1).Trim();

            if (decimal.TryParse(rateRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate) && rate > 0m)
            {
                result[year] = ((double)rate, isProvisional);
                continue;
            }

            if (decimal.TryParse(rateRaw, NumberStyles.Number, CultureInfo.CurrentCulture, out rate) && rate > 0m)
                result[year] = ((double)rate, isProvisional);
        }

        return result;
    }

    public static (double Rate, bool IsProvisional) ResolveUsdToCadRate(
        IReadOnlyDictionary<int, (double Rate, bool IsProvisional)> table,
        int year,
        double fallback)
    {
        if (table != null && table.TryGetValue(year, out var value))
            return value;

        return (fallback, true);
    }
}
