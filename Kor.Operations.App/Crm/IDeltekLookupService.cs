#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations.App.Crm;

public sealed record DeltekClientCandidate(
    string ClientId,
    string Name,
    string? Type,
    double SimilarityScore);

public sealed record DeltekContactCandidate(
    string ContactId,
    string ClientId,
    string FullName,
    string? Email,
    string? Title,
    double SimilarityScore);

public interface IDeltekLookupService
{
    /// <summary>
    /// Fuzzy-match a free-text company name against active rows in
    /// Clendor (ClientInd='Y' AND Status NOT 'I'). Returns up to <paramref name="max"/>
    /// candidates ordered by similarity desc. Empty input returns empty.
    /// Caller decides whether to auto-apply (typically score >= 1.0) or
    /// surface to a human (typically score >= 0.85).
    /// </summary>
    Task<IReadOnlyList<DeltekClientCandidate>> FindClientByNameAsync(
        string companyName, int max, CancellationToken ct);

    /// <summary>
    /// Find a Deltek Contact by best-effort: exact email match wins;
    /// otherwise fuzzy-match (FirstName + ' ' + LastName) optionally
    /// scoped to a known clientId. Returns up to <paramref name="max"/>
    /// candidates ordered by similarity desc.
    /// </summary>
    Task<IReadOnlyList<DeltekContactCandidate>> FindContactAsync(
        string? fullName,
        string? email,
        string? clientIdScope,
        int max,
        CancellationToken ct);
}
