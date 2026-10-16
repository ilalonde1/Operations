using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Knowledge;

namespace Kor.Opportunities.Knowledge.Brief;

/// <summary>Mechanical gates only; the session writer must judge whether the cited claims
/// support an answer. A recommendation does not require a citation.</summary>
public static class DirectorBriefValidation
{
    public static IReadOnlyList<string> Validate(DirectorBriefFile file, DirectorBriefExportFile export)
    {
        // Shared shape validation also requires a one-line expected outcome (1–500)
        // for each recommendation and bounds reviewAfterDays to 7–90. The Data store
        // uses the same gate before starting its import transaction.
        var errors = file.ValidateShape().ToList();
        if (file.WeekOf != export.WeekOf) errors.Add("weekOf must equal the export's weekOf.");
        if (export.Sections is null || export.Sections.Count != 5 || export.Sections.Any(s => s is null)
            || !export.Sections.Select(s => s.Section).Order(StringComparer.Ordinal)
                .SequenceEqual(DirectorBriefFile.SectionNames.Order(StringComparer.Ordinal)))
        {
            errors.Add("Export must offer exactly the five sections, each once.");
            return errors;
        }
        var offered = export.Sections.ToDictionary(s => s.Section, StringComparer.Ordinal);
        foreach (var section in export.Sections)
        {
            if (string.IsNullOrWhiteSpace(section.Question) || string.IsNullOrWhiteSpace(section.ClaimsQuery)
                || section.Facts is null || section.Facts.Length > 6000)
                errors.Add($"Export {section.Section}: question, claimsQuery and bounded facts are required.");
            if (section.Claims is null || section.Claims.Count > 12 || section.Claims.Any(c => c is null || c.ClaimId <= 0))
                errors.Add($"Export {section.Section}: claims must be an array of at most twelve offered claims.");
        }
        if (file.Sections is null) return errors;
        foreach (var section in file.Sections)
        {
            if (section?.Citations is null || section.Section is null || !offered.TryGetValue(section.Section, out var evidence) || evidence.Claims is null) continue;
            var ids = evidence.Claims.Where(c => c is not null).Select(c => c.ClaimId).ToHashSet();
            foreach (var id in section.Citations.Distinct())
                if (!ids.Contains(id)) errors.Add($"{section.Section}: citation {id} was not offered for this section.");
        }
        return errors;
    }

    public static IReadOnlyList<string> ValidateCurrentCitations(DirectorBriefFile file, IReadOnlyDictionary<long, KnowledgeClaimRow?> current)
        => file.ValidateCurrentCitations(current.Where(c => c.Value is not null).ToDictionary(c => c.Key, c => c.Value!.RetiredAtUtc));
}
