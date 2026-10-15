using System.Text.RegularExpressions;

namespace Kor.Opportunities.Knowledge.Claims;

public sealed record StoredClaimPassage(long Id, long SourceId, int Ordinal, DateTimeOffset? ClaimsExtractedAtUtc);
public sealed record AcceptedClaim(long PassageId, ClaimFileClaim Claim);
public sealed record ClaimRejection(long PassageId, int ClaimNumber, string Reason);
public sealed record ClaimValidationResult(IReadOnlyList<AcceptedClaim> Accepted,
    IReadOnlyList<ClaimRejection> Rejected, IReadOnlyList<string> FileErrors);
public sealed record ClaimImportPlan(ClaimValidationResult Validation, bool SupersedeUnreviewedModelClaims);

/// <summary>
/// Mechanical gates for one standalone, actionable or testable sentence supported by its passage.
/// A writer must omit front matter, praise, contents, exercises and anecdotes without an assertion.
/// Passage support and sentence meaning require the writer/reviewer: punctuation and abbreviations
/// are not guessed here. Length and line breaks are enforced; no model is involved.
/// </summary>
public static class ClaimValidation
{
    public static string NormalizeSlug(string? value) => Regex.Replace(
        (value ?? "").Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    public static ClaimValidationResult Validate(ClaimFile file, IReadOnlyList<StoredClaimPassage> stored)
    {
        var accepted = new List<AcceptedClaim>();
        var rejected = new List<ClaimRejection>();
        var errors = new List<string>();
        if (file.SourceId <= 0) errors.Add("sourceId must be positive.");
        if (string.IsNullOrWhiteSpace(file.WrittenBy) || file.WrittenBy.Length > 100)
            errors.Add("writtenBy is required and must be 1–100 characters.");
        if (file.Passages is null || file.Passages.Count == 0)
        {
            errors.Add("List at least one passage, with an explicit claims array (empty when none).");
            return new(accepted, rejected, errors);
        }
        var byId = stored.ToDictionary(p => p.Id);
        var seen = new HashSet<long>();
        foreach (var passage in file.Passages)
        {
            if (passage is null) { errors.Add("A passage cannot be null."); continue; }
            string? passageError = null;
            if (!seen.Add(passage.Id)) passageError = $"Passage {passage.Id} is listed more than once.";
            else if (passage.Id <= 0 || !byId.TryGetValue(passage.Id, out var actual) || actual.SourceId != file.SourceId)
                passageError = $"Passage {passage.Id} does not belong to source {file.SourceId}.";
            else if (passage.Ordinal <= 0 || actual.Ordinal != passage.Ordinal)
                passageError = $"Passage {passage.Id} ordinal does not match the store.";
            if (passageError is not null) errors.Add(passageError);
            if (passage.Claims is null)
            {
                errors.Add($"Passage {passage.Id} needs an explicit claims array (empty when none).");
                continue;
            }
            for (var i = 0; i < passage.Claims.Count; i++)
            {
                var claim = passage.Claims[i];
                var reason = passageError ?? ValidateClaim(claim);
                if (reason is not null) rejected.Add(new(passage.Id, i + 1, reason));
                else accepted.Add(new(passage.Id, claim with
                {
                    Concepts = claim.Concepts.Select(c => new ClaimFileConcept(NormalizeSlug(c.Slug), c.Name.Trim())).ToArray(),
                }));
            }
        }
        return new(accepted, rejected, errors);
    }

    public static ClaimImportPlan PrepareImport(ClaimFile file, IReadOnlyList<StoredClaimPassage> stored, bool force)
    {
        var validation = Validate(file, stored);
        file.Rejections = validation.Rejected;
        if (validation.FileErrors.Count != 0) throw new InvalidDataException(string.Join(" ", validation.FileErrors));
        var ids = file.Passages.Select(p => p.Id).ToHashSet();
        if (!force && stored.Any(p => ids.Contains(p.Id) && p.ClaimsExtractedAtUtc is not null))
            throw new InvalidOperationException("A listed passage is already stamped; use --force to supersede unreviewed model claims.");
        return new(validation, force);
    }

    private static string? ValidateClaim(ClaimFileClaim? claim)
    {
        if (claim is null) return "A claim cannot be null.";
        if (claim.Kind is not ("Rule" or "Fact" or "Definition" or "Heuristic"))
            return "kind must be Rule, Fact, Definition or Heuristic.";
        if (string.IsNullOrWhiteSpace(claim.Statement) || claim.Statement.Length > 1000)
            return "statement must contain 1–1,000 characters.";
        if (claim.Statement.IndexOfAny(new[] { '\r', '\n', '\u0085', '\u2028', '\u2029' }) >= 0)
            return "statement must be one sentence without a line break.";
        if (claim.Confidence is null or < 0 or > 1) return "confidence must be a number from 0 to 1.";
        if (claim.Concepts is null || claim.Concepts.Count is < 1 or > 3) return "A claim needs 1–3 concepts.";
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var concept in claim.Concepts)
        {
            if (concept is null || !Regex.IsMatch(NormalizeSlug(concept.Slug), "^[a-z0-9-]{3,80}$"))
                return "A concept slug must normalize to 3–80 lower-case letters, digits or hyphens.";
            if (string.IsNullOrWhiteSpace(concept.Name) || concept.Name.Length > 150)
                return "A concept name must contain 1–150 characters.";
            if (!slugs.Add(NormalizeSlug(concept.Slug))) return "A claim must not repeat a concept slug.";
        }
        return null;
    }
}
