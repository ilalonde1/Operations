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

    Task<CanonicalOrgRow?> GetCanonicalOrgByClendorIdAsync(string clendorClientId, CancellationToken ct);

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
