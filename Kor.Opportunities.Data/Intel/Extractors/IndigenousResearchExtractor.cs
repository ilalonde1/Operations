#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

public sealed class IndigenousResearchExtractor : IIntelExtractor
{
    private static readonly string[] DepartureTerms =
    [
        "departed",
        "stepping down",
        "former",
        "outgoing",
        "left",
        "resigned",
        "no longer",
    ];

    public IndigenousResearchExtractor(string providerName)
    {
        ProviderName = providerName;
    }

    public string ProviderName { get; }

    public ExtractedIntel Extract(IntelExtractionContext ctx)
    {
        try
        {
            var root = ctx.ResultJson.RootElement;
            var confidence = ParseConfidence(root);
            var people = new List<IntelPersonDraft>();
            var affiliations = new List<IntelPersonAffiliationDraft>();
            var signals = new List<IntelSignalDraft>();
            var actions = new List<IntelActionDraft>();
            var works = new List<IntelWorkDraft>();
            var narratives = new List<IntelNarrativeDraft>();

            AddPeople(root, ctx.CanonicalOrgId, confidence, people, affiliations, signals);
            AddWorks(root, ctx.CanonicalOrgId, confidence, works);
            AddNationNarratives(root, ctx.CanonicalOrgId, confidence, narratives);
            AddNarrative(root, "org_type", "Summary", ctx.CanonicalOrgId, confidence, narratives, "Org type: ");
            AddNarrative(root, "activity_level", "Current", ctx.CanonicalOrgId, confidence, narratives, "Activity level: ");
            AddNarrative(root, "region", "Summary", ctx.CanonicalOrgId, confidence, narratives, "Region: ");
            AddAction(root, ctx.CanonicalOrgId, confidence, actions, "kor_signal", "korWarmth", "fitNotes");
            AddNarrative(root, "notes", "Summary", ctx.CanonicalOrgId, confidence, narratives, null);

            if (people.Count == 0
                && affiliations.Count == 0
                && signals.Count == 0
                && actions.Count == 0
                && works.Count == 0
                && narratives.Count == 0)
            {
                return ExtractedIntel.Empty;
            }

            return new ExtractedIntel(
                people,
                affiliations,
                signals,
                actions,
                works,
                Array.Empty<IntelRiskDraft>(),
                narratives);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                "Intel extractor failed for provider {0}, canonical org id {1}: {2}",
                ctx.ProviderName,
                ctx.CanonicalOrgId,
                ex.Message);

            return ExtractedIntel.Empty;
        }
    }

    private static void AddPeople(JsonElement root, long canonicalOrgId, IntelConfidence confidence, List<IntelPersonDraft> people, List<IntelPersonAffiliationDraft> affiliations, List<IntelSignalDraft> signals)
    {
        if (!TryGetProperty(root, out var peopleElement, "bdContacts", "keyPeople", "leadership", "people"))
        {
            return;
        }

        foreach (var item in EnumerateObjectOrArray(peopleElement))
        {
            var name = GetStringPropertyAny(item, "name", "fullName", "full_name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var title = GetStringPropertyAny(item, "title", "role");
            var notes = GetStringPropertyAny(item, "notes");
            var isDeparted = IsDeparted(notes);

            people.Add(new IntelPersonDraft(
                name,
                GetStringPropertyAny(item, "email"),
                GetStringPropertyAny(item, "phone"),
                GetStringPropertyAny(item, "linkedin", "linkedinUrl", "linkedin_url"),
                notes,
                confidence));

            affiliations.Add(new IntelPersonAffiliationDraft(name, canonicalOrgId, title, null, !isDeparted, null, null, notes, confidence));

            if (isDeparted)
            {
                signals.Add(new IntelSignalDraft(canonicalOrgId, "LeadershipChange", Truncate($"{title ?? "Role"} departure: {name}", 500), notes, null, null, confidence));
            }
        }
    }

    private static void AddWorks(JsonElement root, long canonicalOrgId, IntelConfidence confidence, List<IntelWorkDraft> works)
    {
        if (!TryGetProperty(root, out var projects, "projects", "recentInstitutionalWins", "notableProjects"))
        {
            return;
        }

        foreach (var item in EnumerateArrayOnly(projects))
        {
            var projectName = item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : GetStringPropertyAny(item, "projectName", "project_name", "name");
            if (string.IsNullOrWhiteSpace(projectName))
            {
                continue;
            }

            works.Add(new IntelWorkDraft(
                canonicalOrgId,
                projectName,
                GetStringPropertyAny(item, "role"),
                GetStringPropertyAny(item, "year", "yearApprox", "year_approx"),
                null,
                GetStringPropertyAny(item, "estimatedValueText", "estimated_value_text"),
                GetStringPropertyAny(item, "notes"),
                confidence));
        }
    }

    private static void AddNationNarratives(JsonElement root, long canonicalOrgId, IntelConfidence confidence, List<IntelNarrativeDraft> narratives)
    {
        var nation = GetStringPropertyAny(root, "nation");
        if (!string.IsNullOrWhiteSpace(nation))
        {
            narratives.Add(new IntelNarrativeDraft(canonicalOrgId, "Summary", $"Nation: {nation}", confidence));
        }

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("nations", out var nations)
            && nations.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in nations.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                {
                    narratives.Add(new IntelNarrativeDraft(canonicalOrgId, "Summary", $"Nation: {item.GetString()}", confidence));
                }
            }
        }
    }

    private static void AddNarrative(JsonElement root, string propertyName, string narrativeType, long canonicalOrgId, IntelConfidence confidence, List<IntelNarrativeDraft> narratives, string? prefix)
    {
        var value = GetStringPropertyAny(root, propertyName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            narratives.Add(new IntelNarrativeDraft(canonicalOrgId, narrativeType, prefix is null ? value : prefix + value, confidence));
        }
    }

    private static void AddAction(JsonElement root, long canonicalOrgId, IntelConfidence confidence, List<IntelActionDraft> actions, params string[] propertyNames)
    {
        var value = GetStringPropertyAny(root, propertyNames);
        if (!string.IsNullOrWhiteSpace(value))
        {
            actions.Add(new IntelActionDraft(canonicalOrgId, "PursuitAngle", value, null, null, confidence));
        }
    }

    private static IntelConfidence ParseConfidence(JsonElement root)
    {
        var value = GetStringPropertyAny(root, "_confidence");
        if (string.IsNullOrWhiteSpace(value)
            && root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("_meta", out var meta))
        {
            value = GetStringPropertyAny(meta, "confidence");
        }

        return value?.Trim().ToLowerInvariant() switch
        {
            "high" => IntelConfidence.High,
            "low" => IntelConfidence.Low,
            _ => IntelConfidence.Medium,
        };
    }

    private static string? GetStringPropertyAny(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property)
                && property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement root, out JsonElement value, params string[] propertyNames)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in propertyNames)
            {
                if (root.TryGetProperty(propertyName, out value))
                {
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static IEnumerable<JsonElement> EnumerateObjectOrArray(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
            yield break;
        }

        foreach (var item in EnumerateArrayOnly(element))
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                yield return item;
            }
        }
    }

    private static IEnumerable<JsonElement> EnumerateArrayOnly(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in element.EnumerateArray())
        {
            yield return item;
        }
    }

    private static bool IsDeparted(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return false;
        }

        var lowered = notes.ToLowerInvariant();
        return DepartureTerms.Any(term => lowered.Contains(term, StringComparison.Ordinal));
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
