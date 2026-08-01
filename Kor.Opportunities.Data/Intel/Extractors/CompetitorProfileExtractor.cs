#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

public sealed class CompetitorProfileExtractor : IIntelExtractor
{
    public CompetitorProfileExtractor()
        : this("CompetitorProfile")
    {
    }

    public CompetitorProfileExtractor(string providerName)
    {
        ProviderName = providerName;
    }

    public string ProviderName { get; }

    public ExtractedIntel Extract(IntelExtractionContext ctx)
    {
        try
        {
            var root = ctx.ResultJson.RootElement;
            var rowConfidence = ParseConfidence(root);
            var people = new List<IntelPersonDraft>();
            var affiliations = new List<IntelPersonAffiliationDraft>();
            var signals = new List<IntelSignalDraft>();
            var actions = new List<IntelActionDraft>();
            var works = new List<IntelWorkDraft>();
            var risks = new List<IntelRiskDraft>();
            var narratives = new List<IntelNarrativeDraft>();

            if (TryGetProperty(root, out var keyPeople, "keyPeople", "leadership", "key_people", "key_contacts"))
            {
                foreach (var item in EnumerateObjectOrArray(keyPeople))
                {
                    var name = GetStringProperty(item, "name") ?? GetStringProperty(item, "fullName");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    people.Add(new IntelPersonDraft(
                        DisplayName: name,
                        Email: GetStringProperty(item, "email"),
                        Phone: GetStringProperty(item, "phone"),
                        LinkedinUrl: GetStringProperty(item, "linkedinUrl"),
                        Notes: GetStringProperty(item, "notes"),
                        Confidence: rowConfidence));

                    affiliations.Add(new IntelPersonAffiliationDraft(
                        PersonDisplayName: name,
                        CanonicalOrgId: ctx.CanonicalOrgId,
                        Title: GetStringProperty(item, "title"),
                        Department: null,
                        IsCurrent: true,
                        StartDateApprox: null,
                        EndDateApprox: null,
                        Notes: GetStringProperty(item, "notes"),
                        Confidence: rowConfidence));
                }
            }

            AddWorks(root, ctx.CanonicalOrgId, rowConfidence, works, "notableProjects", "notable_projects", "areas_of_expertise");
            AddWorks(root, ctx.CanonicalOrgId, rowConfidence, works, "recentWinsKnown", "recent_wins_known");
            AddSignals(root, ctx.CanonicalOrgId, rowConfidence, signals, "recentSignals", "recent_signals");
            AddSignals(root, ctx.CanonicalOrgId, rowConfidence, signals, "signalsLast12Mo", "signals_last_12mo");

            var exploitableWeakness = GetStringPropertyAny(root, "exploitableWeakness", "exploitable_weakness");
            if (!string.IsNullOrWhiteSpace(exploitableWeakness))
            {
                risks.Add(new IntelRiskDraft(
                    CanonicalOrgId: ctx.CanonicalOrgId,
                    RiskType: "ExploitableWeakness",
                    Description: exploitableWeakness,
                    MitigationNotes: null,
                    Confidence: rowConfidence));
            }

            var korOverlap = GetStringPropertyAny(root, "korOverlap", "kor_overlap");
            if (!string.IsNullOrWhiteSpace(korOverlap))
            {
                narratives.Add(new IntelNarrativeDraft(
                    CanonicalOrgId: ctx.CanonicalOrgId,
                    NarrativeType: "Summary",
                    ParagraphText: korOverlap,
                    Confidence: rowConfidence));
            }

            var korRelevanceNotes = GetStringPropertyAny(root, "korRelevanceNotes", "kor_relevance_notes");
            if (!string.IsNullOrWhiteSpace(korRelevanceNotes))
            {
                actions.Add(new IntelActionDraft(
                    CanonicalOrgId: ctx.CanonicalOrgId,
                    ActionType: "PursuitAngle",
                    Recommendation: korRelevanceNotes,
                    TargetPersonName: null,
                    TimingNotes: null,
                    Confidence: rowConfidence));
            }

            if (people.Count == 0
                && affiliations.Count == 0
                && signals.Count == 0
                && actions.Count == 0
                && works.Count == 0
                && risks.Count == 0
                && narratives.Count == 0)
            {
                return ExtractedIntel.Empty;
            }

            return new ExtractedIntel(people, affiliations, signals, actions, works, risks, narratives);
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

    private static void AddWorks(
        JsonElement root,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelWorkDraft> works,
        params string[] propertyNames)
    {
        if (!TryGetArray(root, out var projects, propertyNames))
        {
            return;
        }

        foreach (var item in projects.EnumerateArray())
        {
            var name = GetStringValue(item) ?? GetStringProperty(item, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            works.Add(new IntelWorkDraft(
                CanonicalOrgId: canonicalOrgId,
                ProjectName: name,
                Role: GetStringProperty(item, "role"),
                YearApprox: GetStringProperty(item, "year"),
                EstimatedValueCad: null,
                EstimatedValueText: null,
                Notes: null,
                Confidence: confidence));
        }
    }

    private static void AddSignals(
        JsonElement root,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelSignalDraft> signals,
        params string[] propertyNames)
    {
        if (!TryGetArray(root, out var signalItems, propertyNames))
        {
            return;
        }

        foreach (var item in signalItems.EnumerateArray())
        {
            var subject = GetStringValue(item) ?? GetStringProperty(item, "subject") ?? GetStringProperty(item, "text");
            if (string.IsNullOrWhiteSpace(subject))
            {
                continue;
            }

            signals.Add(new IntelSignalDraft(
                CanonicalOrgId: canonicalOrgId,
                SignalType: ClassifySignal(subject),
                Subject: Truncate(subject, 500),
                Detail: null,
                OccurredAtApprox: null,
                SourceUrl: null,
                Confidence: confidence));
        }
    }

    private static IntelConfidence ParseConfidence(JsonElement root)
    {
        var value = GetStringProperty(root, "_confidence");
        if (string.IsNullOrWhiteSpace(value)
            && root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("_meta", out var meta))
        {
            value = GetStringProperty(meta, "confidence");
        }

        return value?.Trim().ToLowerInvariant() switch
        {
            "high" => IntelConfidence.High,
            "low" => IntelConfidence.Low,
            _ => IntelConfidence.Medium,
        };
    }

    private static string ClassifySignal(string value)
    {
        var lowered = value.ToLowerInvariant();
        if (lowered.Contains("hired", StringComparison.Ordinal) || lowered.Contains("hiring", StringComparison.Ordinal))
        {
            return "HiringSurge";
        }

        if (lowered.Contains("won", StringComparison.Ordinal) || lowered.Contains("awarded", StringComparison.Ordinal))
        {
            return "RecentWin";
        }

        if (lowered.Contains("acquired", StringComparison.Ordinal)
            || lowered.Contains("acquisition", StringComparison.Ordinal)
            || lowered.Contains("merger", StringComparison.Ordinal))
        {
            return "OwnershipMnA";
        }

        if (lowered.Contains("office", StringComparison.Ordinal)
            || lowered.Contains("moved", StringComparison.Ordinal)
            || lowered.Contains("relocat", StringComparison.Ordinal))
        {
            return "OfficeMove";
        }

        if (lowered.Contains("capacity", StringComparison.Ordinal)
            || lowered.Contains("overload", StringComparison.Ordinal)
            || lowered.Contains("overcommit", StringComparison.Ordinal))
        {
            return "CapacityStrain";
        }

        if (lowered.Contains("departed", StringComparison.Ordinal)
            || lowered.Contains("stepping down", StringComparison.Ordinal)
            || lowered.Contains("resigned", StringComparison.Ordinal))
        {
            return "LeadershipChange";
        }

        return "Other";
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static string? GetStringPropertyAny(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var property)
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

    private static bool TryGetArray(JsonElement root, out JsonElement value, params string[] propertyNames)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in propertyNames)
            {
                if (root.TryGetProperty(propertyName, out value)
                    && value.ValueKind == JsonValueKind.Array)
                {
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? GetStringValue(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() : null;

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

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
