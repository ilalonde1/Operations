#nullable enable
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Awards;

/// <summary>
/// Resolves raw organization name strings to canonical CanonicalOrg ids.
/// Strategy: alias hit, normalized-name match, create new.
/// </summary>
public sealed class CanonicalOrgResolver
{
    private static readonly Regex NonAlnum = new("[^a-z0-9]", RegexOptions.Compiled);
    private static readonly HashSet<string> GenericNameDenylist = new(StringComparer.OrdinalIgnoreCase)
    {
        "officials",
        "thecity",
        "acontractor",
        "thedeveloper",
        "theteam",
        "thecompany",
        "thefirm",
        "theowner",
        "theapplicant",
        "na",
        "tba",
        "tbd",
        "unknown",
        "none",
        "theproject",
        "theproponent",
        "government",
        "ministry",
        "department",
        "thebuyer",
        "thevendor",
        "thecontractor",
        "various",
        "multiple",
        "etal",
        "etc",
        "sole",
        "partner",
        "director",
        "manager",
        "employee",
        "staff",
        "partnership",
        "generalpartnership",
        "gp",
    };

    private readonly ICanonicalOrgStore _store;
    private readonly ILogger<CanonicalOrgResolver> _logger;

    public CanonicalOrgResolver(ICanonicalOrgStore store, ILogger<CanonicalOrgResolver> logger)
    {
        _store = store;
        _logger = logger;
    }

    public Task<long?> ResolveBuyerAsync(string? rawName, CancellationToken ct)
        => ResolveAsync(rawName, OrgKinds.Buyer, OrgAliasSources.OpportunityAwardsAwarding, ct);

    public Task<long?> ResolveVendorAsync(string? rawName, CancellationToken ct)
        => ResolveAsync(rawName, OrgKinds.Vendor, OrgAliasSources.OpportunityAwardsAwardedTo, ct);

    public Task<long?> ResolveOpportunityBuyerAsync(string? rawName, CancellationToken ct)
        => ResolveAsync(rawName, OrgKinds.Buyer, OrgAliasSources.OpportunitiesBuyer, ct);

    public async Task<long?> ResolveAsync(
        string? rawName,
        string kind,
        string source,
        CancellationToken ct,
        bool allowCreate = true,
        int minConfidenceForCreate = 50)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return null;
        var trimmed = rawName.Trim();
        var normalized = NormalizeName(trimmed);

        if (GenericNameDenylist.Contains(normalized))
        {
            await RecordUnclassifiedAliasAsync(trimmed, source, 5, "denylist", "Generic organization name denylist", ct)
                .ConfigureAwait(false);
            return null;
        }

        if (normalized.Length < 3)
        {
            await RecordUnclassifiedAliasAsync(trimmed, source, 5, "too-short", "Normalized organization name shorter than 3 characters", ct)
                .ConfigureAwait(false);
            return null;
        }

        var alias = await _store.LookupAliasAsync(trimmed, source, ct).ConfigureAwait(false);
        if (alias is not null && alias.CanonicalOrgId.HasValue)
        {
            return alias.CanonicalOrgId.Value;
        }

        long? canonicalId = null;
        if (!string.IsNullOrEmpty(normalized))
        {
            canonicalId = await _store.FindByNormalizedNameAsync(normalized, ct).ConfigureAwait(false);
        }

        if (canonicalId is null)
        {
            if (!allowCreate)
            {
                await RecordUnclassifiedAliasAsync(trimmed, source, 10, "auto-unresolved", "Creation disabled for this source", ct)
                    .ConfigureAwait(false);
                _logger.LogDebug("Org '{RawName}' ({Source}) was not resolved; creation disabled.", trimmed, source);
                return null;
            }

            canonicalId = await _store.UpsertCanonicalOrgAsync(
                kind: kind,
                displayName: trimmed,
                clendorClientId: null,
                website: null,
                notes: null,
                ct: ct).ConfigureAwait(false);

            await _store.UpsertAliasAsync(
                rawName: trimmed,
                source: source,
                canonicalOrgId: canonicalId,
                confidence: minConfidenceForCreate,
                classifiedBy: "auto-new",
                notes: "Created by CanonicalOrgResolver (no normalized-name match)",
                ct: ct).ConfigureAwait(false);
        }
        else
        {
            await _store.UpsertAliasAsync(
                rawName: trimmed,
                source: source,
                canonicalOrgId: canonicalId,
                confidence: 80,
                classifiedBy: "auto-normalized",
                notes: "Matched by normalized name",
                ct: ct).ConfigureAwait(false);
        }

        _logger.LogDebug("Resolved org '{RawName}' ({Source}) to CanonicalOrgId {CanonicalOrgId}.", trimmed, source, canonicalId);
        return canonicalId;
    }

    private Task RecordUnclassifiedAliasAsync(
        string rawName,
        string source,
        int confidence,
        string classifiedBy,
        string notes,
        CancellationToken ct)
        => _store.UpsertAliasAsync(
            rawName: rawName,
            source: source,
            canonicalOrgId: null,
            confidence: confidence,
            classifiedBy: classifiedBy,
            notes: notes,
            ct: ct);

    /// <summary>
    /// Public so one-shot backfill scripts can use the same normalization
    /// the resolver uses, ensuring matches against the persisted computed column.
    /// </summary>
    public static string NormalizeName(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        return NonAlnum.Replace(input.ToLowerInvariant(), "");
    }
}
