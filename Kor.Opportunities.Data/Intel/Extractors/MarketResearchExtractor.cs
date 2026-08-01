#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

public sealed class MarketResearchExtractor : IIntelExtractor
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

    public MarketResearchExtractor(string providerName)
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
            AddNarrative(root, "korRelevance", "Summary", ctx.CanonicalOrgId, confidence, narratives, null);
            AddAction(root, "korRelevanceReason", "PursuitAngle", ctx.CanonicalOrgId, confidence, actions);
            AddNarrative(root, "researchNotes", "Current", ctx.CanonicalOrgId, confidence, narratives, null);

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

    private static void AddPeople(
        JsonElement root,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelPersonDraft> people,
        List<IntelPersonAffiliationDraft> affiliations,
        List<IntelSignalDraft> signals)
    {
        if (!TryGetProperty(root, out var peopleElement, "keyLeadership", "leadership", "keyPeople"))
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
                DisplayName: name,
                Email: GetStringPropertyAny(item, "email"),
                Phone: GetStringPropertyAny(item, "phone"),
                LinkedinUrl: GetStringPropertyAny(item, "linkedin", "linkedinUrl", "linkedin_url"),
                Notes: notes,
                Confidence: confidence));

            affiliations.Add(new IntelPersonAffiliationDraft(
                PersonDisplayName: name,
                CanonicalOrgId: canonicalOrgId,
                Title: title,
                Department: null,
                IsCurrent: !isDeparted,
                StartDateApprox: null,
                EndDateApprox: null,
                Notes: notes,
                Confidence: confidence));

            if (isDeparted)
            {
                signals.Add(new IntelSignalDraft(
                    CanonicalOrgId: canonicalOrgId,
                    SignalType: "LeadershipChange",
                    Subject: Truncate($"{title ?? "Role"} departure: {name}", 500),
                    Detail: notes,
                    OccurredAtApprox: null,
                    SourceUrl: null,
                    Confidence: confidence));
            }
        }
    }

    private static void AddWorks(JsonElement root, long canonicalOrgId, IntelConfidence confidence, List<IntelWorkDraft> works)
    {
        if (!TryGetProperty(root, out var projects, "signatureProjects", "notableProjects", "recentProjects"))
        {
            return;
        }

        foreach (var item in EnumerateArrayOnly(projects))
        {
            var projectName = item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : GetStringPropertyAny(item, "projectName", "name");
            if (string.IsNullOrWhiteSpace(projectName))
            {
                continue;
            }

            works.Add(new IntelWorkDraft(
                CanonicalOrgId: canonicalOrgId,
                ProjectName: projectName,
                Role: GetStringPropertyAny(item, "role"),
                YearApprox: GetStringPropertyAny(item, "year", "yearApprox"),
                EstimatedValueCad: null,
                EstimatedValueText: null,
                Notes: GetStringPropertyAny(item, "notes"),
                Confidence: confidence));
        }
    }

    private static void AddNarrative(JsonElement root, string propertyName, string narrativeType, long canonicalOrgId, IntelConfidence confidence, List<IntelNarrativeDraft> narratives, string? prefix)
    {
        var value = GetStringPropertyAny(root, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        narratives.Add(new IntelNarrativeDraft(canonicalOrgId, narrativeType, prefix is null ? value : prefix + value, confidence));
    }

    private static void AddAction(JsonElement root, string propertyName, string actionType, long canonicalOrgId, IntelConfidence confidence, List<IntelActionDraft> actions)
    {
        var value = GetStringPropertyAny(root, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        actions.Add(new IntelActionDraft(canonicalOrgId, actionType, value, null, null, confidence));
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
            yield return item;
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
