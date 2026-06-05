#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

public sealed class ArchitectPipelineResearchExtractor : IIntelExtractor
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

    public string ProviderName => "ArchitectPipelineResearch";

    public ExtractedIntel Extract(IntelExtractionContext ctx)
    {
        try
        {
            var outer = ctx.ResultJson.RootElement;
            if (outer.ValueKind == JsonValueKind.Object
                && outer.TryGetProperty("skipped", out var skipped)
                && skipped.ValueKind == JsonValueKind.True)
            {
                return ExtractedIntel.Empty;
            }

            var rowConfidence = ParseConfidence(outer);
            JsonDocument? innerDoc = null;
            var inner = outer;
            var resultJson = GetStringProperty(outer, "resultJson");
            if (!string.IsNullOrWhiteSpace(resultJson))
            {
                innerDoc = ParseInnerResultJson(resultJson, ctx);
                if (innerDoc is null)
                {
                    return ExtractedIntel.Empty;
                }

                inner = innerDoc.RootElement;
            }

            using (innerDoc)
            {
                var people = new List<IntelPersonDraft>();
                var affiliations = new List<IntelPersonAffiliationDraft>();
                var signals = new List<IntelSignalDraft>();
                var actions = new List<IntelActionDraft>();
                var works = new List<IntelWorkDraft>();
                var narratives = new List<IntelNarrativeDraft>();

                AddLeadership(inner, ctx.CanonicalOrgId, rowConfidence, people, affiliations, signals);
                AddStructuralPartners(inner, ctx.CanonicalOrgId, rowConfidence, narratives);
                AddWorks(inner, "activePursuits", ctx.CanonicalOrgId, rowConfidence, works);
                AddWorks(inner, "recentBuiltWork", ctx.CanonicalOrgId, rowConfidence, works);
                AddKorAngle(inner, ctx.CanonicalOrgId, rowConfidence, actions);

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

    private static JsonDocument? ParseInnerResultJson(string resultJson, IntelExtractionContext ctx)
    {
        try
        {
            return JsonDocument.Parse(resultJson);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            System.Diagnostics.Trace.TraceWarning(
                "ArchitectPipelineResearch inner resultJson parse failed for canonical org id {0}: {1}",
                ctx.CanonicalOrgId,
                ex.Message);
            return null;
        }
    }

    private static void AddLeadership(
        JsonElement inner,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelPersonDraft> people,
        List<IntelPersonAffiliationDraft> affiliations,
        List<IntelSignalDraft> signals)
    {
        if (inner.ValueKind != JsonValueKind.Object
            || !inner.TryGetProperty("leadership", out var leadership))
        {
            return;
        }

        foreach (var item in EnumerateObjectOrArray(leadership))
        {
            var name = GetStringProperty(item, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var title = GetStringProperty(item, "title");
            var notes = GetStringProperty(item, "notes");
            var isDeparted = IsDeparted(notes);

            people.Add(new IntelPersonDraft(
                DisplayName: name,
                Email: GetStringProperty(item, "email"),
                Phone: GetStringProperty(item, "phone"),
                LinkedinUrl: GetStringProperty(item, "linkedinUrl"),
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

    private static void AddStructuralPartners(
        JsonElement inner,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelNarrativeDraft> narratives)
    {
        if (!TryGetArray(
            inner,
            out var partners,
            "structuralPartners",
            "structural_partners_observed",
            "structural_partners"))
        {
            return;
        }

        foreach (var item in partners.EnumerateArray())
        {
            var partnerName = item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : GetStringProperty(item, "name");
            if (string.IsNullOrWhiteSpace(partnerName))
            {
                continue;
            }

            narratives.Add(new IntelNarrativeDraft(
                CanonicalOrgId: canonicalOrgId,
                NarrativeType: "Summary",
                ParagraphText: $"Recurring structural partner: {partnerName}",
                Confidence: confidence));
        }
    }

    private static void AddWorks(
        JsonElement inner,
        string propertyName,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelWorkDraft> works)
    {
        if (!TryGetArray(inner, out var items, GetWorkPropertyAliases(propertyName)))
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
        {
            var projectName = GetStringProperty(item, "projectName");
            if (string.IsNullOrWhiteSpace(projectName))
            {
                continue;
            }

            works.Add(new IntelWorkDraft(
                CanonicalOrgId: canonicalOrgId,
                ProjectName: projectName,
                Role: NonEmptyOrDefault(GetStringProperty(item, "role"), "Architect"),
                YearApprox: GetStringProperty(item, "yearApprox"),
                EstimatedValueCad: null,
                EstimatedValueText: GetStringProperty(item, "estimatedValueText"),
                Notes: GetStringProperty(item, "notes"),
                Confidence: confidence));
        }
    }

    private static void AddKorAngle(
        JsonElement inner,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelActionDraft> actions)
    {
        if (!TryGetObject(inner, out var korAngle, "korAngle", "kor_relationship", "kor_angle"))
        {
            return;
        }

        var theAsk = GetStringProperty(korAngle, "theAsk");
        if (!string.IsNullOrWhiteSpace(theAsk))
        {
            actions.Add(new IntelActionDraft(
                CanonicalOrgId: canonicalOrgId,
                ActionType: "PursuitAngle",
                Recommendation: theAsk,
                TargetPersonName: null,
                TimingNotes: GetStringProperty(korAngle, "timingNotes"),
                Confidence: confidence));
        }

        var displacementRead = GetStringProperty(korAngle, "displacementRead");
        if (!string.IsNullOrWhiteSpace(displacementRead))
        {
            actions.Add(new IntelActionDraft(
                CanonicalOrgId: canonicalOrgId,
                ActionType: "KorDisplacementRead",
                Recommendation: displacementRead,
                TargetPersonName: null,
                TimingNotes: null,
                Confidence: confidence));
        }
    }

    private static IntelConfidence ParseConfidence(JsonElement outer)
    {
        if (outer.ValueKind == JsonValueKind.Object
            && outer.TryGetProperty("confidence", out var confidence)
            && confidence.ValueKind == JsonValueKind.Number
            && confidence.TryGetDouble(out var value))
        {
            if (value < 0.6)
            {
                return IntelConfidence.Low;
            }

            if (value >= 0.85)
            {
                return IntelConfidence.High;
            }
        }

        return IntelConfidence.Medium;
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
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

    private static bool TryGetObject(JsonElement root, out JsonElement value, params string[] propertyNames)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in propertyNames)
            {
                if (root.TryGetProperty(propertyName, out value)
                    && value.ValueKind == JsonValueKind.Object)
                {
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string[] GetWorkPropertyAliases(string propertyName)
    {
        return propertyName switch
        {
            "activePursuits" => ["activePursuits", "active_pipeline_projects", "activePipeline"],
            "recentBuiltWork" => ["recentBuiltWork", "notable_recent_completions", "recentDeliveries", "recent_built_work"],
            _ => [propertyName],
        };
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

    private static string NonEmptyOrDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
