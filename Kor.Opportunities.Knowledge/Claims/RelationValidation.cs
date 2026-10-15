namespace Kor.Opportunities.Knowledge.Claims;

public static class RelationValidation
{
    public static RelationValidationResult Validate(RelationFile file, IReadOnlySet<long> activeIds,
        IEnumerable<(long From, long To, string Kind)>? existing = null)
    {
        if (string.IsNullOrWhiteSpace(file.WrittenBy) || file.WrittenBy.Length > 100)
            throw new ArgumentException("writtenBy must contain 1–100 characters.");
        if (file.Relations is null) throw new ArgumentException("relations must be an array.");
        var seen = new HashSet<(long, long, string)>(existing ?? Array.Empty<(long, long, string)>());
        var accepted = new List<ClaimRelation>();
        var rejected = new List<RelationRejection>();
        var skipped = 0;
        for (var i = 0; i < file.Relations.Count; i++)
        {
            var row = file.Relations[i];
            string? reason = row is null ? "A relation cannot be null."
                : row.Kind is not ("Supports" or "Contradicts" or "Refines") ? "kind must be Supports, Contradicts or Refines."
                : row.From == row.To ? "from and to must be different claims."
                : !activeIds.Contains(row.From) || !activeIds.Contains(row.To) ? "Both claim ids must exist and not be retired."
                : row.Confidence is null or < 0 or > 1 ? "confidence must be a number from 0 to 1."
                : row.Note?.Length > 500 ? "note must be at most 500 characters." : null;
            if (reason is not null) { rejected.Add(new(i + 1, reason)); continue; }
            var valid = row!;
            if (!seen.Add((valid.From, valid.To, valid.Kind))) skipped++;
            else accepted.Add(valid);
        }
        file.Rejections = rejected;
        return new(accepted, rejected, skipped);
    }
}
