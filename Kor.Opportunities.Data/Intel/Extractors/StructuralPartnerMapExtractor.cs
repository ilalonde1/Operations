#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

public sealed class StructuralPartnerMapExtractor : IIntelExtractor
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

    public string ProviderName => "StructuralPartnerMap";

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

            AddKeyPeople(root, ctx.CanonicalOrgId, confidence, people, affiliations, signals);
            AddRecentProjects(root, ctx.CanonicalOrgId, confidence, works);
            AddRecurringPartners(root, ctx.CanonicalOrgId, confidence, narratives);
            AddSignalArray(root, "leadershipChanges", "LeadershipChange", ctx.CanonicalOrgId, confidence, signals);
            AddSignalArray(root, "newsHeadlines", "Other", ctx.CanonicalOrgId, confidence, signals);
            AddNarrative(root, "structuralPartnerStatus", "Summary", ctx.CanonicalOrgId, confidence, narratives, null);
            AddNarrative(root, "korEngagementHistory", "History", ctx.CanonicalOrgId, confidence, narratives, null);
            AddNarrative(root, "korPriority", "Summary", ctx.CanonicalOrgId, confidence, narratives, "KOR priority: ");
            AddAction(root, "korDisplacementRead", "KorDisplacementRead", ctx.CanonicalOrgId, confidence, actions);

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

    private static void AddKeyPeople(
        JsonElement root,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelPersonDraft> people,
        List<IntelPersonAffiliationDraft> affiliations,
        List<IntelSignalDraft> signals)
    {
        if (!TryGetProperty(root, out var keyPeople, "keyPeople"))
        {
            return;
        }

        foreach (var item in EnumerateObjectOrArray(keyPeople))
        {
            var name = GetStringPropertyAny(item, "name", "fullName", "full_name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var title = GetStringPropertyAny(item, "title");
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

    private static void AddRecentProjects(JsonElement root, long canonicalOrgId, IntelConfidence confidence, List<IntelWorkDraft> works)
    {
        if (!TryGetArray(root, "recentProjects", out var projects))
        {
            return;
        }

        foreach (var item in projects.EnumerateArray())
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
                YearApprox: GetStringPropertyAny(item, "yearApprox", "year"),
                EstimatedValueCad: null,
                EstimatedValueText: null,
                Notes: GetStringPropertyAny(item, "notes"),
                Confidence: confidence));
        }
    }

    private static void AddRecurringPartners(JsonElement root, long canonicalOrgId, IntelConfidence confidence, List<IntelNarrativeDraft> narratives)
    {
        if (!TryGetArray(root, "recurringStructuralPartners", out var partners))
        {
            return;
        }

        foreach (var item in partners.EnumerateArray())
        {
            var name = item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : GetStringPropertyAny(item, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            narratives.Add(new IntelNarrativeDraft(
                CanonicalOrgId: canonicalOrgId,
                NarrativeType: "Summary",
                ParagraphText: $"Recurring structural partner: {name}",
                Confidence: confidence));
        }
    }

    private static void AddSignalArray(
        JsonElement root,
        string propertyName,
        string signalType,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelSignalDraft> signals)
    {
        if (!TryGetArray(root, propertyName, out var items))
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
        {
            var subject = item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : GetStringPropertyAny(item, "subject", "text", "description");
            if (string.IsNullOrWhiteSpace(subject))
            {
                continue;
            }

            signals.Add(new IntelSignalDraft(
                CanonicalOrgId: canonicalOrgId,
                SignalType: signalType,
                Subject: Truncate(subject, 500),
                Detail: null,
                OccurredAtApprox: null,
                SourceUrl: null,
                Confidence: confidence));
        }
    }

    private static void AddNarrative(
        JsonElement root,
        string propertyName,
        string narrativeType,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelNarrativeDraft> narratives,
        string? prefix)
    {
        var value = GetStringPropertyAny(root, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        narratives.Add(new IntelNarrativeDraft(
            CanonicalOrgId: canonicalOrgId,
            NarrativeType: narrativeType,
            ParagraphText: prefix is null ? value : prefix + value,
            Confidence: confidence));
    }

    private static void AddAction(
        JsonElement root,
        string propertyName,
        string actionType,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelActionDraft> actions)
    {
        var value = GetStringPropertyAny(root, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        actions.Add(new IntelActionDraft(
            CanonicalOrgId: canonicalOrgId,
            ActionType: actionType,
            Recommendation: value,
            TargetPersonName: null,
            TimingNotes: null,
            Confidence: confidence));
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

    private static bool TryGetArray(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(propertyName, out value)
            && value.ValueKind == JsonValueKind.Array)
        {
            return true;
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

        if (element.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                yield return item;
            }
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
