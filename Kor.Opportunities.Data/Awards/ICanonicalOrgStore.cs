#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Awards;

public interface ICanonicalOrgStore
{
    /// <summary>
    /// Find-or-create a live canonical org. <c>Created</c> is true only when a new
    /// row was inserted — callers that post-process a fresh mint (e.g. born-archived
    /// stamping) MUST check it, or a race/fuzzy match can retire a pre-existing
    /// LIVE org that merely matched.
    /// </summary>
    Task<(long Id, bool Created)> UpsertCanonicalOrgAsync(
        string kind,
        string displayName,
        string? clendorClientId,
        string? website,
        string? notes,
        CancellationToken ct);

    Task<CanonicalOrgRow?> GetCanonicalOrgAsync(long id, CancellationToken ct);

    Task<IReadOnlyList<CanonicalOrgRow>> SearchCanonicalOrgsAsync(
        string? query,
        string? kind,
        int take,
        CancellationToken ct);

    /// <summary>
    /// Like <see cref="SearchCanonicalOrgsAsync(string?, string?, int, CancellationToken)"/>
    /// but filters to canonical orgs with at least one KOR-relevant
    /// relationship signal: linked Clendor client, research enrichment row,
    /// linked MPI project (as proponent or architect), linked Opportunity
    /// (as buyer), linked award (as awarder or winner), or linked KorPursuit.
    /// Default for the Relationships screen so the list is real organizations
    /// the firm actually deals with, not the full ~7k canonical-org universe.
    /// </summary>
    Task<IReadOnlyList<CanonicalOrgRow>> SearchCanonicalOrgsWithRelationshipsAsync(
        string? query,
        string? kind,
        int take,
        CancellationToken ct);

    Task<CanonicalOrgRow?> GetCanonicalOrgByClendorIdAsync(string clendorClientId, CancellationToken ct);

    Task RecordBcRegistrySnapshotAsync(long canonicalOrgId, BcRegistrySnapshot snapshot, CancellationToken ct);

    /// <summary>Load just the DisplayName + Kind so providers can decide what to search.</summary>
    Task<(string DisplayName, string Kind)?> GetNameAndKindAsync(long canonicalOrgId, CancellationToken ct);

    Task<bool> UnretireAsync(long canonicalOrgId, string reason, CancellationToken ct);

    /// <summary>
    /// The single retired CanonicalOrg that is eligible to be resurrected for the
    /// given strict normalized name, or null. Eligible = archived for inactivity
    /// (born-archived on intake, or low-value auto-archive) and NOT a dedup loser
    /// (an org merged into a survivor — resurrecting that would undo the merge).
    /// Returns null when there is no match OR the match is ambiguous (more than
    /// one eligible retired row), so the caller safely falls through to create.
    /// </summary>
    Task<long?> FindResurrectableRetiredAsync(string normalizedName, CancellationToken ct);

    Task MarkRetiredOnIntakeAsync(long canonicalOrgId, string reason, CancellationToken ct);

    /// <summary>
    /// Find the first CanonicalOrg whose NormalizedName matches the given
    /// already-normalized value. Returns null if no match.
    /// </summary>
    Task<long?> FindByNormalizedNameAsync(string normalizedName, CancellationToken ct);

    /// <summary>
    /// Find the deterministic live CanonicalOrg survivor for the supplied
    /// safe fuzzy-normalized name. Returns null for no match.
    /// </summary>
    Task<long?> FindByFuzzyNormalizedNameAsync(string fuzzyKey, CancellationToken ct);

    /// <summary>
    /// Find the deterministic live CanonicalOrg survivor for the supplied
    /// website domain. Generic/shared host domains are ignored.
    /// </summary>
    Task<long?> FindByWebsiteDomainAsync(string domain, CancellationToken ct);

    /// <summary>
    /// Follow the CanonicalOrgMerge ledger from a merged-away org id to the
    /// deepest live survivor. Returns null when the id has no live survivor.
    /// </summary>
    Task<long?> ResolveCanonicalOrgMergeAsync(long canonicalOrgId, CancellationToken ct);

    /// <summary>
    /// Resolve an alias whose CanonicalOrgId now points at a merged-away org.
    /// Returns the live merge survivor, or null when no such redirect exists.
    /// </summary>
    Task<long?> FindMergedSurvivorByAliasAsync(string rawName, string source, CancellationToken ct);

    /// <summary>
    /// Resolve a normalized name that now points at a merged-away org.
    /// Returns the live merge survivor, or null when no such redirect exists.
    /// </summary>
    Task<long?> FindMergedSurvivorByNormalizedNameAsync(string normalizedName, CancellationToken ct);

    /// <summary>
    /// Count retired orgs that would otherwise have matched this resolver input.
    /// Used only for resolver telemetry; callers must not attach to the retired ids.
    /// </summary>
    Task<int> CountRetiredMatchesAsync(
        string rawName,
        string source,
        string normalizedName,
        string fuzzyKey,
        CancellationToken ct);

    /// <summary>
    /// Promote incoming website/notes only into empty slots. Never overwrites.
    /// </summary>
    Task PromoteCanonicalOrgWebsiteNotesAsync(
        long canonicalOrgId,
        string? website,
        string? notes,
        CancellationToken ct);

    /// <summary>Insert an alias if not already present. Returns the alias Id.</summary>
    Task<long> UpsertAliasAsync(
        string rawName,
        string source,
        long? canonicalOrgId,
        int confidence,
        string? classifiedBy,
        string? notes,
        CancellationToken ct);

    Task<OrgAliasRow?> LookupAliasAsync(string rawName, string source, CancellationToken ct);

    Task<IReadOnlyList<OrgAliasRow>> ListUnclassifiedAsync(string? source, int batchSize, CancellationToken ct);

    Task<(int Total, int Classified, int Unclassified)> GetAliasCountsAsync(CancellationToken ct);
}
