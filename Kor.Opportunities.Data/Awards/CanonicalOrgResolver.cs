#nullable enable
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Text;
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

    private static readonly Meter ResolverMeter = new("Kor.Opportunities.Data.CanonicalOrgResolver");
    private static readonly Counter<int> MatchedByFuzzySurvivorCounter = ResolverMeter.CreateCounter<int>("matchedByFuzzySurvivor");
    private static readonly Counter<int> CreatedNewCounter = ResolverMeter.CreateCounter<int>("createdNew");
    private static readonly Counter<int> RedirectedFromMergedCounter = ResolverMeter.CreateCounter<int>("redirectedFromMerged");
    private static readonly Counter<int> SkippedRetiredCounter = ResolverMeter.CreateCounter<int>("skippedRetired");

    private readonly ICanonicalOrgStore _store;
    private readonly ILogger<CanonicalOrgResolver> _logger;
    private readonly bool? _resolverFuzzySurvivorAttachOverride;

    public CanonicalOrgResolver(ICanonicalOrgStore store, ILogger<CanonicalOrgResolver> logger)
        : this(store, logger, resolverFuzzySurvivorAttach: null)
    {
    }

    public CanonicalOrgResolver(
        ICanonicalOrgStore store,
        ILogger<CanonicalOrgResolver> logger,
        bool resolverFuzzySurvivorAttach)
        : this(store, logger, (bool?)resolverFuzzySurvivorAttach)
    {
    }

    private CanonicalOrgResolver(
        ICanonicalOrgStore store,
        ILogger<CanonicalOrgResolver> logger,
        bool? resolverFuzzySurvivorAttach)
    {
        _store = store;
        _logger = logger;
        _resolverFuzzySurvivorAttachOverride = resolverFuzzySurvivorAttach;
    }

    public static bool IsGenericNameDenied(string normalizedName)
        => GenericNameDenylist.Contains(normalizedName);

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
        int minConfidenceForCreate = 50,
        bool createArchived = false,
        string? website = null,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return null;
        var trimmed = rawName.Trim();
        var cleaned = StripIntakeNoise(trimmed);
        var normalized = NormalizeName(cleaned);

        if (IsGenericNameDenied(normalized))
        {
            await RecordUnclassifiedAliasAsync(cleaned, source, 5, "denylist", "Generic organization name denylist", ct)
                .ConfigureAwait(false);
            return null;
        }

        if (normalized.Length < 3)
        {
            await RecordUnclassifiedAliasAsync(cleaned, source, 5, "too-short", "Normalized organization name shorter than 3 characters", ct)
                .ConfigureAwait(false);
            return null;
        }

        // Several real organizations concatenated into one name (e.g. "Fraser Health
        // AuthorityVancouver Coastal Health Authority..." — the #794 class). This
        // happens when a source lists multiple parties in one proponent/owner string.
        // Do NOT create a canonical from it (that's an un-mergeable junk entity that
        // surfaces in dossiers); record it as an unclassified alias for review so the
        // raw text is kept and nothing is lost.
        if (IsConcatenatedMultiEntity(cleaned))
        {
            await RecordUnclassifiedAliasAsync(cleaned, source, 5, "concatenated-multi-entity",
                "Name looks like multiple organizations concatenated; not auto-created", ct)
                .ConfigureAwait(false);
            _logger.LogWarning("Skipped concatenated multi-entity org name '{RawName}' from source '{Source}'.", cleaned, source);
            return null;
        }

        var fuzzyKey = NormalizeForFuzzyMatch(cleaned);
        long? canonicalId = null;
        var matchedByAlias = false;
        var matchedByFuzzySurvivor = false;
        var redirectedFromMerged = false;
        var createdNew = false;
        var skippedRetired = 0;
        var classifiedBy = "auto-normalized";
        var aliasNotes = "Matched by normalized name";

        var alias = await _store.LookupAliasAsync(trimmed, source, ct).ConfigureAwait(false);
        if (alias is not null && alias.CanonicalOrgId.HasValue)
        {
            var redirected = await ResolveMergeOrSameAsync(alias.CanonicalOrgId.Value, ct).ConfigureAwait(false);
            canonicalId = redirected.Id;
            redirectedFromMerged = redirected.Redirected;
            matchedByAlias = true;
            classifiedBy = redirected.Redirected ? "auto-merge-redirect" : "auto-alias";
            aliasNotes = redirected.Redirected
                ? "Alias pointed at a merged org; redirected to live survivor"
                : "Matched by existing alias";
        }

        if (canonicalId is null)
        {
            var mergedAliasSurvivor = await _store.FindMergedSurvivorByAliasAsync(trimmed, source, ct).ConfigureAwait(false);
            if (mergedAliasSurvivor.HasValue)
            {
                canonicalId = mergedAliasSurvivor.Value;
                redirectedFromMerged = true;
                classifiedBy = "auto-merge-redirect";
                aliasNotes = "Alias pointed at a merged org; redirected to live survivor";
            }
        }

        if (canonicalId is null && !string.IsNullOrEmpty(normalized))
        {
            canonicalId = await _store.FindByNormalizedNameAsync(normalized, ct).ConfigureAwait(false);
        }

        if (canonicalId is null && !string.IsNullOrEmpty(normalized))
        {
            var mergedNameSurvivor = await _store.FindMergedSurvivorByNormalizedNameAsync(normalized, ct).ConfigureAwait(false);
            if (mergedNameSurvivor.HasValue)
            {
                canonicalId = mergedNameSurvivor.Value;
                redirectedFromMerged = true;
                classifiedBy = "auto-merge-redirect";
                aliasNotes = "Normalized name pointed at a merged org; redirected to live survivor";
            }
        }

        if (canonicalId is null)
        {
            if (ResolverFuzzySurvivorAttachEnabled && fuzzyKey.Length >= 8 && !IsGenericNameDenied(fuzzyKey))
            {
                canonicalId = await _store.FindByFuzzyNormalizedNameAsync(fuzzyKey, ct).ConfigureAwait(false);
                matchedByFuzzySurvivor = canonicalId.HasValue;
                if (matchedByFuzzySurvivor)
                {
                    classifiedBy = "auto-fuzzy-survivor";
                    aliasNotes = "Matched by deterministic fuzzy-normalized survivor";
                }
            }
            else
            {
                _logger.LogDebug(
                    "Skipped fuzzy survivor attach for org '{RawName}' from source '{Source}': enabled={Enabled}, fuzzyKeyLength={FuzzyKeyLength}, denylisted={Denylisted}.",
                    cleaned,
                    source,
                    ResolverFuzzySurvivorAttachEnabled,
                    fuzzyKey.Length,
                    IsGenericNameDenied(fuzzyKey));
            }
        }

        if (canonicalId is null)
        {
            skippedRetired = await _store.CountRetiredMatchesAsync(trimmed, source, normalized, fuzzyKey, ct).ConfigureAwait(false);
            if (skippedRetired > 0)
            {
                _logger.LogInformation(
                    "Skipped {SkippedRetired} retired CanonicalOrg candidate(s) for '{RawName}' from source '{Source}'.",
                    skippedRetired,
                    cleaned,
                    source);
            }
        }

        if (canonicalId is null)
        {
            if (!allowCreate)
            {
                await RecordUnclassifiedAliasAsync(cleaned, source, 10, "auto-unresolved", "Creation disabled for this source", ct)
                    .ConfigureAwait(false);
                _logger.LogDebug("Org '{RawName}' ({Source}) was not resolved; creation disabled.", cleaned, source);
                EmitResolverStats(matchedByFuzzySurvivor, createdNew, redirectedFromMerged, skippedRetired);
                return null;
            }

            canonicalId = await _store.UpsertCanonicalOrgAsync(
                kind: kind,
                displayName: cleaned,
                clendorClientId: null,
                website: website,
                notes: notes,
                ct: ct).ConfigureAwait(false);
            createdNew = true;
            classifiedBy = "auto-new";
            aliasNotes = "Created by CanonicalOrgResolver (no live normalized/fuzzy-name match)";

            if (createArchived)
            {
                await _store.MarkRetiredOnIntakeAsync(
                    canonicalId.Value,
                    "Born-archived on intake: orphan procurement vendor; resurrects on any future reference",
                    ct).ConfigureAwait(false);
            }

            await _store.UpsertAliasAsync(
                rawName: trimmed,
                source: source,
                canonicalOrgId: canonicalId,
                confidence: minConfidenceForCreate,
                classifiedBy: classifiedBy,
                notes: aliasNotes,
                ct: ct).ConfigureAwait(false);
        }
        else if (!matchedByAlias || redirectedFromMerged)
        {
            await _store.UpsertAliasAsync(
                rawName: trimmed,
                source: source,
                canonicalOrgId: canonicalId,
                confidence: redirectedFromMerged ? 90 : 80,
                classifiedBy: classifiedBy,
                notes: aliasNotes,
                ct: ct).ConfigureAwait(false);
        }

        if (canonicalId.HasValue && (!string.IsNullOrWhiteSpace(website) || !string.IsNullOrWhiteSpace(notes)))
        {
            await _store.PromoteCanonicalOrgWebsiteNotesAsync(canonicalId.Value, website, notes, ct)
                .ConfigureAwait(false);
        }

        EmitResolverStats(matchedByFuzzySurvivor, createdNew, redirectedFromMerged, skippedRetired);
        _logger.LogDebug("Resolved org '{RawName}' ({Source}) to CanonicalOrgId {CanonicalOrgId}.", cleaned, source, canonicalId);
        return canonicalId;
    }

    private async Task<(long Id, bool Redirected)> ResolveMergeOrSameAsync(long canonicalOrgId, CancellationToken ct)
    {
        var survivorId = await _store.ResolveCanonicalOrgMergeAsync(canonicalOrgId, ct).ConfigureAwait(false);
        if (survivorId.HasValue && survivorId.Value != canonicalOrgId)
        {
            return (survivorId.Value, true);
        }

        return (canonicalOrgId, false);
    }

    private bool ResolverFuzzySurvivorAttachEnabled
        => _resolverFuzzySurvivorAttachOverride ?? ReadResolverFuzzySurvivorAttachFlag();

    private static bool ReadResolverFuzzySurvivorAttachFlag()
    {
        var configured = System.Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_RESOLVERFUZZYSURVIVORATTACH")
            ?? System.Environment.GetEnvironmentVariable("ResolverFuzzySurvivorAttach");
        if (string.IsNullOrWhiteSpace(configured)) return true;

        return !(configured.Equals("false", System.StringComparison.OrdinalIgnoreCase)
            || configured.Equals("0", System.StringComparison.OrdinalIgnoreCase)
            || configured.Equals("no", System.StringComparison.OrdinalIgnoreCase)
            || configured.Equals("off", System.StringComparison.OrdinalIgnoreCase));
    }

    private static void EmitResolverStats(
        bool matchedByFuzzySurvivor,
        bool createdNew,
        bool redirectedFromMerged,
        int skippedRetired)
    {
        if (matchedByFuzzySurvivor) MatchedByFuzzySurvivorCounter.Add(1);
        if (createdNew) CreatedNewCounter.Add(1);
        if (redirectedFromMerged) RedirectedFromMergedCounter.Add(1);
        if (skippedRetired > 0) SkippedRetiredCounter.Add(skippedRetired);
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

    private static readonly Regex ConcatGlue = new(@"(Authority|Corporation)[A-Z][a-z]", RegexOptions.Compiled);
    private static readonly Regex AuthorityToken = new(@"\bAuthority\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CorporationToken = new(@"\bCorporation\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FirstNationToken = new(@"\bFirst Nation\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Detects a name that is several real organizations concatenated into one — the
    /// #794 class (e.g. "Fraser Health AuthorityVancouver Coastal Health Authority").
    /// Two signals: an entity word glued straight into the next Capitalized word with
    /// no space ("AuthorityVancouver"), or two-plus full entity-suffix tokens (a single
    /// real org carries at most one "Authority"/"Corporation"/"First Nation"). Public
    /// so a one-shot audit can reuse the exact same rule.
    ///
    /// Conservative: when in doubt it does NOT match, and even a false match is cheap —
    /// the caller only DEFERS that one name to the unclassified-alias review queue
    /// (the raw text is preserved; nothing is created and nothing is lost).
    /// </summary>
    public static bool IsConcatenatedMultiEntity(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();
        if (ConcatGlue.IsMatch(n)) return true;
        if (AuthorityToken.Matches(n).Count >= 2) return true;
        if (CorporationToken.Matches(n).Count >= 2) return true;
        if (FirstNationToken.Matches(n).Count >= 2) return true;
        return n.IndexOf("Health Care", System.StringComparison.OrdinalIgnoreCase) >= 0
            && n.IndexOf("Health Services", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

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

    /// <summary>
    /// Aggressive normalization that strips trailing corporate/role suffixes
    /// iteratively after stripping all non-alphanumeric chars. Single source
    /// of truth shared with BdCanonicalDedup so the resolver collapses
    /// "ACI Architecture" / "ACI Architects Inc." / "ACI Architecture Inc."
    /// to the same key preventing the dup-creation cycle where the
    /// resolver creates a new canonical that BdCanonicalDedup then has to
    /// merge away.
    ///
    /// Mirrors NormalizeKey in tools/BdCanonicalDedup/Program.cs (extracted
    /// here so both call the same code).
    /// </summary>
    public static string NormalizeAggressiveKey(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        }

        var normalized = sb.ToString();
        bool stripped;
        do
        {
            stripped = false;
            foreach (var token in AggressiveStripTokens)
            {
                if (normalized.Length >= token.Length && normalized.EndsWith(token, StringComparison.Ordinal))
                {
                    // Guard short tokens (e.g. "co", "lp") to avoid collapsing unrelated names.
                    if (token.Length <= 2 && normalized.Length - token.Length < 4) continue;
                    normalized = normalized[..^token.Length];
                    stripped = true;
                    break;
                }
            }
        }
        while (stripped);

        return normalized;
    }

    private static readonly string[] AggressiveStripTokens = new[]
    {
        "inc", "incorporated", "ltd", "limited", "llp", "llc", "lp",
        "corp", "corporation", "co", "company",
        "architects", "architect", "architecture",
        "partnership", "partners", "group",
    };

    /// <summary>
    /// Strips intake-time noise from a raw organization name so the resolver
    /// lookup and (when creating) the DisplayName both reflect the firm's
    /// actual brand. Applied BEFORE NormalizeName for lookup, and used as
    /// the stored DisplayName when creating a new canonical.
    ///
    /// Patterns stripped:
    /// - "PersonName DBA: BrandName" -> "BrandName"
    /// - "1234567 BC Ltd o/a Brand" -> "Brand"
    /// - Trailing procurement-status noise (Pre-Qualified, Bid Received,
    ///   Successful Proponent, Contract Awarded, Weighted Evaluation,
    ///   "is the successful", "Awarded" as trailing word, etc.)
    ///
    /// Returns the cleaned name. If the result is empty or under 3 chars,
    /// returns the original trimmed input (caller's length check still applies).
    /// Idempotent: StripIntakeNoise(StripIntakeNoise(x)) == StripIntakeNoise(x).
    /// </summary>
    public static string StripIntakeNoise(string input)
    {
        var original = input?.Trim() ?? string.Empty;
        if (original.Length == 0)
        {
            return original;
        }

        var cleaned = original;

        var dba = DbaPrefixRegex.Match(cleaned);
        if (dba.Success)
        {
            cleaned = TrimLeadingIntakePunctuation(dba.Groups[1].Value);
        }

        var numberedWrapper = NumberedCorpOaRegex.Match(cleaned);
        if (numberedWrapper.Success)
        {
            cleaned = numberedWrapper.Groups[1].Value.TrimStart();
        }

        var oaMatches = GenericOaRegex.Matches(cleaned);
        if (oaMatches.Count > 0)
        {
            var last = oaMatches[^1];
            cleaned = cleaned[(last.Index + last.Length)..].TrimStart();
        }

        cleaned = ProcurementNoiseRegex.Replace(cleaned, string.Empty);
        cleaned = TrailingAwardedRegex.Replace(cleaned, string.Empty);
        cleaned = TidyIntakeName(cleaned);

        return VisibleCharCount(cleaned) < 3
            ? original
            : cleaned;
    }

    private static int VisibleCharCount(string value)
    {
        var count = 0;
        foreach (var c in value)
        {
            if (!char.IsWhiteSpace(c))
            {
                count++;
            }
        }
        return count;
    }

    private static string TrimLeadingIntakePunctuation(string value)
        => value.TrimStart(' ', '\t', '\r', '\n', '.', ',', ';', ':', '-');

    private static string TidyIntakeName(string value)
        => value
            .TrimEnd(' ', '\t', '\r', '\n', ',', ';', ':', '-')
            .TrimStart(' ', '\t', '\r', '\n', ',', ':');

    private static readonly System.Text.RegularExpressions.Regex DbaPrefixRegex = new(
        @"\bdba\s*:\s*(.+)$",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex NumberedCorpOaRegex = new(
        @"^\s*[\d()\s]+\s+(?:BC|Alberta|Ontario|Manitoba|Canada|Quebec|British\s+Columbia)\s+(?:Ltd|Inc|Corp|Limited|Incorporated|Corporation)\.?\s+o/a\s+(.+)$",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex GenericOaRegex = new(
        @"\s+o/a\s+",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex ProcurementNoiseRegex = new(
        @"\s+(?:(?:successfully\s+|successful\s+)?pre-?qualified|bid\s+received|bid\s+has\s+been\s+received|bid\s+has\s+not\s+been|bids?\s+have\s+not\s+been|received\s*[-/]?\s*not\s+reviewed|proposal\s+received\s+not\s+reviewed|proposal\s+have\s+not\s+been\s+reviewed|successful\s+(?:proponent|vendor|proposal|prequalified|bidder|tenderer|respondent|bid)|is\s+the\s+successful|contract\s+awarded|tender\s+awarded|weighted\s+evaluation|evaluation\s+is\s+under\s+review|the\s+short\s+listed\s+firms|highest\s+scoring|top\s+(?:ranked|scoring)|selected\s+(?:proponent|vendor|firm)|selected\s+as\s+(?:the\s+)?(?:successful|winning)|1\s+bid\s+received)\b.*$",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex TrailingAwardedRegex = new(
        @"\s+awarded\b\s*$",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

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
        " incorporated", " corporation", " limited", " company",
        " inc", " inc.", " ltd", " ltd.", " llp", " llp.", " llc", " llc.",
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
