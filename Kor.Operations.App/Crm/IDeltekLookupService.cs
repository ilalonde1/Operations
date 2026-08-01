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
    /// Substring search against Clendor.Name — matches the query anywhere
    /// in the client name. Use for interactive autocomplete UX where the
    /// user types any part of the name (e.g. "west" should find
    /// "Northwest Developments"). Returns up to <paramref name="max"/>
    /// rows ordered alphabetically. Empty input returns empty. SimilarityScore
    /// is set to 1.0 (not used for ranking — alphabetical order wins).
    /// When <paramref name="requireOpenAr"/> is true, only clients with at
    /// least one AR row whose outstanding balance is &gt; 0 are returned.
    /// </summary>
    Task<IReadOnlyList<DeltekClientCandidate>> SearchClientsAsync(
        string queryText, int max, CancellationToken ct, bool requireOpenAr = false);

    /// <summary>
    /// Resolve a single client by exact ClientID. Returns null if no row
    /// or the row is inactive. SimilarityScore on the candidate is set
    /// to 1.0 — there's no fuzzy matching here.
    /// </summary>
    Task<DeltekClientCandidate?> FindClientByIdAsync(string clientId, CancellationToken ct);

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
