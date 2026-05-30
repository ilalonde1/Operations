#nullable enable
using System.Collections.Generic;
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
        return input.Trim().ToLowerInvariant()
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace(".", "", StringComparison.Ordinal)
            .Replace(",", "", StringComparison.Ordinal)
            .Replace("'", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("&", "", StringComparison.Ordinal)
            .Replace("/", "", StringComparison.Ordinal)
            .Replace("(", "", StringComparison.Ordinal)
            .Replace(")", "", StringComparison.Ordinal)
            .Replace("+", "", StringComparison.Ordinal);
    }

    // Pre-compiled patterns for the fuzzy normalizer.
    private static readonly System.Text.RegularExpressions.Regex SchoolDistrictNumberRegex = new(
        @"\b(?:school\s+district|sd)\s*(?:no\.?\s*)?#?\s*(\d+)\b",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex CityOfSuffixRegex = new(
        @"^(.+?)\s*\(city of\)$",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex CityOfPrefixRegex = new(
        @"^city of\s+(.+)$",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly string[] CorporateSuffixes =
    {
        " incorporated", " corporation", " limited",
        " inc", " inc.", " ltd", " ltd.", " llp", " llp.",
        " corp", " corp.", " co", " co.",
    };

    /// <summary>
    /// More aggressive normalization for FUZZY duplicate detection. Use this
    /// in dedup tooling and audit reports — NEVER in the resolver's fast-path
    /// lookup, because the persisted <c>CanonicalOrg.NormalizedName</c>
    /// computed column (schema migration 22) uses the strict <see cref="NormalizeName"/>
    /// formula and these two functions intentionally diverge.
    ///
    /// Beyond strict normalization, this fuzzy variant:
    /// - normalizes <c>&amp;</c> / <c>and</c> to the same token
    /// - canonicalizes school-district forms: <c>SD68</c>, <c>SD #68</c>,
    ///   <c>School District 68</c>, <c>School District No. 68</c> all
    ///   collapse to <c>schooldistrict68</c>
    /// - canonicalizes civic forms: <c>City of Vancouver</c> and
    ///   <c>Vancouver (City of)</c> both collapse to <c>cityofvancouver</c>
    /// - strips corporate suffixes (Ltd, Inc, LLP, Corp, Co, Corporation,
    ///   Incorporated, Limited) so <c>Acme Ltd.</c> and <c>Acme</c> match
    /// </summary>
    public static string NormalizeForFuzzyMatch(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var s = input.Trim().ToLowerInvariant();

        // & / and equivalence — preserve word boundaries via surrounding spaces
        // so "AT&T" doesn't become "ATandT" (it'd be "at&t" lowercased, then this
        // would only fire if there was a space).
        s = s.Replace(" & ", " and ", StringComparison.Ordinal);

        // Civic forms
        var cityPrefix = CityOfPrefixRegex.Match(s);
        if (cityPrefix.Success)
        {
            s = "city of " + cityPrefix.Groups[1].Value;
        }
        else
        {
            var citySuffix = CityOfSuffixRegex.Match(s);
            if (citySuffix.Success)
            {
                s = "city of " + citySuffix.Groups[1].Value;
            }
        }

        // School district forms — replace any matched form with the canonical
        // "school district NNN" so downstream punctuation strip collapses to
        // "schooldistrictNNN".
        s = SchoolDistrictNumberRegex.Replace(s, "school district $1");

        // Corporate suffixes — strip trailing forms. Iterate so e.g. "Inc. Ltd."
        // (rare but possible from data sources) collapses fully.
        bool changed;
        do
        {
            changed = false;
            foreach (var suffix in CorporateSuffixes)
            {
                if (s.EndsWith(suffix, StringComparison.Ordinal))
                {
                    s = s[..^suffix.Length].TrimEnd(' ', ',', '.');
                    changed = true;
                    break;
                }
            }
        }
        while (changed);

        // Final pass: strict-normalize for punctuation/space removal so the
        // result is directly comparable to NormalizedName-style values.
        return NormalizeName(s);
    }
}
