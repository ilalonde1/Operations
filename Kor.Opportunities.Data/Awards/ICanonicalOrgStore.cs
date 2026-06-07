#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Awards;

public interface ICanonicalOrgStore
{
    Task<long> UpsertCanonicalOrgAsync(
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
    /// Find the first CanonicalOrg whose NormalizedName matches the given
    /// already-normalized value. Returns null if no match.
    /// </summary>
    Task<long?> FindByNormalizedNameAsync(string normalizedName, CancellationToken ct);

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
