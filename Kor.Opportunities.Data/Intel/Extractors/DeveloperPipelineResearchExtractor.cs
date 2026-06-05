#nullable enable
using System.Globalization;
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

public sealed class DeveloperPipelineResearchExtractor : IIntelExtractor
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

    public string ProviderName => "DeveloperPipelineResearch";

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

            AddContacts(root, ctx.CanonicalOrgId, rowConfidence, people, affiliations, signals);
            AddWorks(root, ctx.CanonicalOrgId, rowConfidence, works, "pipeline_projects", "pipelineProjects", "active_pipeline_projects", "activePipeline", "activePursuits");
            AddWorks(root, ctx.CanonicalOrgId, rowConfidence, works, "recent_completions", "recentCompletions", "notable_recent_completions", "recentDeliveries", "recentBuiltWork", "notableProjects");
            AddKorRelationshipAction(root, ctx.CanonicalOrgId, rowConfidence, actions);

            if (people.Count == 0
                && affiliations.Count == 0
                && signals.Count == 0
                && actions.Count == 0
                && works.Count == 0)
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
                Array.Empty<IntelNarrativeDraft>());
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

    private static void AddContacts(
        JsonElement root,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelPersonDraft> people,
        List<IntelPersonAffiliationDraft> affiliations,
        List<IntelSignalDraft> signals)
    {
        if (!TryGetProperty(root, out var contacts, "key_contacts", "keyContacts", "key_people", "keyPeople"))
        {
            return;
        }

        foreach (var item in EnumerateObjectOrArray(contacts))
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
                LinkedinUrl: GetStringPropertyAny(item, "linkedin_url", "linkedinUrl"),
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

    private static void AddWorks(
        JsonElement root,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelWorkDraft> works,
        params string[] propertyNames)
    {
        if (!TryGetArray(root, out var items, propertyNames))
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
        {
            var projectName = GetStringPropertyAny(item, "project_name", "projectName", "name");
            if (string.IsNullOrWhiteSpace(projectName))
            {
                continue;
            }

            var stage = GetStringPropertyAny(item, "stage");
            var notes = GetStringPropertyAny(item, "notes");

            works.Add(new IntelWorkDraft(
                CanonicalOrgId: canonicalOrgId,
                ProjectName: projectName,
                Role: GetStringPropertyAny(item, "role") ?? "Owner",
                YearApprox: GetStringPropertyAny(item, "year", "yearApprox", "expected_year", "expectedYear"),
                EstimatedValueCad: null,
                EstimatedValueText: GetEstimatedValueText(item),
                Notes: CombineStageAndNotes(stage, notes),
                Confidence: confidence));
        }
    }

    private static void AddKorRelationshipAction(
        JsonElement root,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelActionDraft> actions)
    {
        var signal = GetStringPropertyAny(root, "kor_relationship_signal", "korRelationshipSignal", "kor_relationship", "korRelationshipNotes")
            ?? GetKorRelationshipObjectText(root);
        if (string.IsNullOrWhiteSpace(signal))
        {
            return;
        }

        actions.Add(new IntelActionDraft(
            CanonicalOrgId: canonicalOrgId,
            ActionType: "PursuitAngle",
            Recommendation: signal,
            TargetPersonName: null,
            TimingNotes: null,
            Confidence: confidence));
    }

    private static string? GetKorRelationshipObjectText(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in new[] { "kor_relationship_signal", "korRelationshipSignal", "kor_relationship", "korRelationshipNotes" })
        {
            if (root.TryGetProperty(name, out var property)
                && property.ValueKind == JsonValueKind.Object)
            {
                var text = GetStringPropertyAny(property, "rationale", "notes", "recommendation");
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
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

    private static string? GetEstimatedValueText(JsonElement item)
    {
        var text = GetStringPropertyAny(item, "estimated_value_text", "estimatedValueText");
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        if (item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty("value_usd_est", out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out var amount))
        {
            return amount >= 1_000_000m
                ? string.Format(CultureInfo.InvariantCulture, "${0:0.#}M", amount / 1_000_000m)
                : string.Format(CultureInfo.InvariantCulture, "${0:0}", amount);
        }

        return null;
    }

    private static string? CombineStageAndNotes(string? stage, string? notes)
    {
        if (string.IsNullOrWhiteSpace(stage))
        {
            return notes;
        }

        var stageText = $"Stage: {stage}.";
        return string.IsNullOrWhiteSpace(notes) ? stageText : $"{stageText} {notes}";
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
