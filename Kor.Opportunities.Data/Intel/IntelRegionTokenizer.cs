#nullable enable
using System;
using System.Collections.Generic;

namespace Kor.Opportunities.Data.Intel;

/// <summary>
/// Region-alias-aware city tokenizer shared between the Region Brief layer
/// (SqlBriefDataStore.TokenizeCity) and the Intel region read queries
/// (IntelReadService.GetRegion*Async). Before this lived only in
/// SqlBriefDataStore, IntelReadService used a naive LIKE '%@city%' which
/// silently returned 0 rows for inputs like "GVRD" / "Lower Mainland" —
/// the two sections rendered side by side on the brief diverged.
///
/// Adding a new region alias (e.g. Calgary Region, Greater Toronto Area)
/// requires adding it here only — both Brief and Intel queries respect it.
/// </summary>
public static class IntelRegionTokenizer
{
    private static readonly char[] CitySeparators = { '/', ',', ';', '&' };

    public static readonly IReadOnlyList<string> MetroVancouverMunicipalities = new[]
    {
        "Vancouver", "Burnaby", "Surrey", "Richmond", "Coquitlam", "Port Coquitlam",
        "Port Moody", "New Westminster", "Delta", "Langley", "Maple Ridge",
        "Pitt Meadows", "North Vancouver", "West Vancouver", "White Rock",
        "Bowen Island", "Anmore", "Belcarra", "Lions Bay",
        "Metro Vancouver", "Greater Vancouver",
    };

    private static readonly Dictionary<string, IReadOnlyList<string>> RegionAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "greater vancouver",                     MetroVancouverMunicipalities },
            { "greater vancouver regional district",   MetroVancouverMunicipalities },
            { "gvrd",                                  MetroVancouverMunicipalities },
            { "metro vancouver",                       MetroVancouverMunicipalities },
            { "metro vancouver regional district",     MetroVancouverMunicipalities },
            { "metro van",                             MetroVancouverMunicipalities },
            { "lower mainland",                        MetroVancouverMunicipalities },
        };

    /// <summary>
    /// Split free-text city input on separators, deduplicate, and expand
    /// region aliases (GVRD → 21 munis). Empty input → empty list.
    /// </summary>
    public static IReadOnlyList<string> Tokenize(string? city)
    {
        if (string.IsNullOrWhiteSpace(city)) return Array.Empty<string>();

        var parts = city.Split(CitySeparators, StringSplitOptions.RemoveEmptyEntries);
        var list = new List<string>(parts.Length);
        foreach (var p in parts)
        {
            var t = p.Trim();
            if (t.Length == 0) continue;

            if (RegionAliases.TryGetValue(t, out var expanded))
            {
                foreach (var member in expanded)
                {
                    if (!list.Exists(x => string.Equals(x, member, StringComparison.OrdinalIgnoreCase)))
                    {
                        list.Add(member);
                    }
                }
                continue;
            }

            if (!list.Exists(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(t);
            }
        }

        return list;
    }
}
