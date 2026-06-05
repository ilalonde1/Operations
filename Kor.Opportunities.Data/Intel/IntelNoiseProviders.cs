#nullable enable
using System.Collections.Generic;

namespace Kor.Opportunities.Data.Intel;

/// <summary>
/// Single source of truth for "do-not-extract" provider names. These rows
/// exist in opportunities.CanonicalOrgEnrichment as legal/registry metadata
/// (BcRegistry) or internal-snapshot data (KorDeltekHistory) and are NOT
/// actionable intel. They are deliberately skipped by every layer:
/// extraction, the catch-up Quartz job, the backfill CLI, and the MCP
/// SystemPrompt docs.
///
/// To add a new noise provider, add the name here only — every consumer
/// references this list.
/// </summary>
public static class IntelNoiseProviders
{
    public static readonly IReadOnlyList<string> Names = new[]
    {
        "BcRegistry",
        "Registry",
        "KorDeltekHistory",
        "KorCapability",
    };

    /// <summary>
    /// SQL fragment for use in WHERE clauses: "ProviderName NOT IN (N'...', N'...', ...)".
    /// Wraps every name in N'' literals — safe because the list is a fixed compile-time set.
    /// </summary>
    public static string SqlNotInClause(string columnExpression = "ProviderName")
    {
        var literals = new List<string>(Names.Count);
        foreach (var n in Names) literals.Add($"N'{n.Replace("'", "''")}'");
        return $"{columnExpression} NOT IN ({string.Join(", ", literals)})";
    }

    public static bool IsNoise(string? providerName)
    {
        if (string.IsNullOrEmpty(providerName)) return false;
        foreach (var n in Names)
        {
            if (string.Equals(n, providerName, System.StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
