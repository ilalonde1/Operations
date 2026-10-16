namespace Kor.Opportunities.Data.Crm;

public sealed record DirectorCitation(long ClaimId, long PassageId, string Statement, string Source);
public sealed record DirectorBriefRow(long Id, DateOnly WeekOf, string Section, string Headline, string Body,
    string? Recommendation, IReadOnlyList<DirectorCitation> Citations, string WrittenBy,
    DateTimeOffset ImportedAtUtc, DateTimeOffset? AcknowledgedAtUtc, string? AcknowledgedBy, long? DecisionId = null);
public sealed record PriorCall(DateOnly WeekOf, string Headline, string Recommendation, string Expected,
    string Outcome, string? OutcomeNote, DateTimeOffset ReviewAtUtc);

// Shared input contracts live in Data to keep the dependency Knowledge -> Data one-way.
// JSON parsing and export-specific validation live in Knowledge/Brief.
public sealed record DirectorBriefFile(DateOnly WeekOf, string WrittenBy, IReadOnlyList<DirectorBriefSection> Sections)
{
    public static IReadOnlyList<string> SectionNames { get; } = Array.AsReadOnly(new[] { "ThisWeek", "Pursuits", "Accounts", "Market", "Asks" });

    public IReadOnlyList<string> ValidateShape()
    {
        var errors = new List<string>();
        if (WeekOf == default) errors.Add("weekOf is required.");
        if (string.IsNullOrWhiteSpace(WrittenBy) || WrittenBy.Length > 100)
            errors.Add("writtenBy must contain 1–100 characters.");
        if (Sections is null) { errors.Add("sections must contain exactly the five sections."); return errors; }
        if (Sections.Count != 5) errors.Add("sections must contain exactly five entries.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in Sections)
        {
            if (section is null) { errors.Add("A section cannot be null."); continue; }
            if (!SectionNames.Contains(section.Section)) errors.Add($"Unknown section: {section.Section}.");
            if (!seen.Add(section.Section)) errors.Add($"Duplicate section: {section.Section}.");
            if (string.IsNullOrWhiteSpace(section.Headline) || section.Headline.Length > 512)
                errors.Add($"{section.Section}: headline must contain 1–512 characters.");
            if (section.Headline?.IndexOfAny(new[] { '\r', '\n', '\u0085', '\u2028', '\u2029' }) >= 0)
                errors.Add($"{section.Section}: headline must not contain a line break.");
            if (string.IsNullOrWhiteSpace(section.Body) || section.Body.Length > 20000)
                errors.Add($"{section.Section}: body must contain 1–20,000 characters.");
            if (section.Citations is null) errors.Add($"{section.Section}: citations must be an array (empty when none).");
            if (!string.IsNullOrWhiteSpace(section.Recommendation)
                && (string.IsNullOrWhiteSpace(section.Expected) || section.Expected.Length > 500))
                errors.Add($"{section.Section}: expected must contain 1–500 characters when a recommendation is present.");
            if (!string.IsNullOrWhiteSpace(section.Recommendation)
                && section.Expected?.IndexOfAny(new[] { '\r', '\n', '\u0085', '\u2028', '\u2029' }) >= 0)
                errors.Add($"{section.Section}: expected must be one line.");
            if (section.ReviewAfterDays is < 7 or > 90)
                errors.Add($"{section.Section}: reviewAfterDays must be between 7 and 90.");
        }
        foreach (var name in SectionNames.Where(name => !seen.Contains(name))) errors.Add($"Missing section: {name}.");
        return errors;
    }

    // A missing key means the claim is gone; a non-null value means it is retired.
    // Called after duplicate detection and before any rows are written.
    public IReadOnlyList<string> ValidateCurrentCitations(IReadOnlyDictionary<long, DateTimeOffset?> retiredAt)
    {
        var errors = new List<string>();
        foreach (var section in Sections)
        foreach (var id in section.Citations.Distinct())
        {
            if (!retiredAt.TryGetValue(id, out var retired)) errors.Add($"{section.Section}: citation {id} no longer exists.");
            else if (retired is not null) errors.Add($"{section.Section}: citation {id} is retired.");
        }
        return errors;
    }
}
public sealed record DirectorBriefSection(string Section, string Headline, string Body,
    string? Recommendation, IReadOnlyList<long> Citations, string? Expected = null, int? ReviewAfterDays = null)
{
    public DateTimeOffset ReviewAtUtc(DateOnly weekOf)
    {
        var local = weekOf.AddDays(ReviewAfterDays ?? 28).ToDateTime(new TimeOnly(16, 0), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time")));
    }
}

public interface IDirectorBriefStore
{
    Task<IReadOnlyList<DirectorBriefRow>> GetLatestAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DirectorBriefRow>> GetHistoryAsync(int weeks, CancellationToken ct = default);
    // The four-week window is [since, since + 28 days), excluding the export's week.
    Task<IReadOnlyList<PriorCall>> ListPriorCallsAsync(string section, DateOnly since, CancellationToken ct = default);
    Task AcknowledgeAsync(long id, string by, CancellationToken ct = default);
    // Caller validates against the original export. The store rechecks active citations and
    // snapshots them under transaction locks; (0, []) means the file hash was already imported.
    Task<(int SectionsWritten, IReadOnlyList<string> Rejected)> ImportAsync(DirectorBriefFile file,
        string exportSha256, string fileName, string fileSha256, string by, bool force, CancellationToken ct = default);
}
