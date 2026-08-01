#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

public sealed class PrimeTargetingExtractor : IIntelExtractor
{
    public string ProviderName => "PrimeTargeting";

    public ExtractedIntel Extract(IntelExtractionContext ctx)
    {
        try
        {
            var root = ctx.ResultJson.RootElement;
            var confidence = ParseConfidence(root);
            var actions = new List<IntelActionDraft>();
            var risks = new List<IntelRiskDraft>();
            var narratives = new List<IntelNarrativeDraft>();

            AddRationale(root, ctx.CanonicalOrgId, confidence, actions, narratives);
            AddKorRelationship(root, ctx.CanonicalOrgId, confidence, actions);
            AddNarrative(root, "priorityRank", "Summary", ctx.CanonicalOrgId, confidence, narratives, "KOR pursuit priority: ");
            AddStandingPartnerRisk(root, ctx.CanonicalOrgId, confidence, risks);
            AddTimingAction(root, ctx.CanonicalOrgId, confidence, actions);
            AddNarrative(root, "publicPrimeVolume", "Current", ctx.CanonicalOrgId, confidence, narratives, "Public prime volume: ");

            if (actions.Count == 0 && risks.Count == 0 && narratives.Count == 0)
            {
                return ExtractedIntel.Empty;
            }

            return new ExtractedIntel(
                Array.Empty<IntelPersonDraft>(),
                Array.Empty<IntelPersonAffiliationDraft>(),
                Array.Empty<IntelSignalDraft>(),
                actions,
                Array.Empty<IntelWorkDraft>(),
                risks,
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

    private static void AddRationale(
        JsonElement root,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelActionDraft> actions,
        List<IntelNarrativeDraft> narratives)
    {
        var value = GetStringPropertyAny(root, "rationale");
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        narratives.Add(new IntelNarrativeDraft(
            CanonicalOrgId: canonicalOrgId,
            NarrativeType: "Action",
            ParagraphText: value,
            Confidence: confidence));

        actions.Add(new IntelActionDraft(
            CanonicalOrgId: canonicalOrgId,
            ActionType: "PursuitAngle",
            Recommendation: value,
            TargetPersonName: null,
            TimingNotes: null,
            Confidence: confidence));
    }

    private static void AddKorRelationship(
        JsonElement root,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelActionDraft> actions)
    {
        var value = GetStringPropertyAny(root, "korRelationship") ?? GetObjectStringProperty(root, "korRelationship", "rationale", "notes", "recommendation");
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        actions.Add(new IntelActionDraft(
            CanonicalOrgId: canonicalOrgId,
            ActionType: "ContactStrategy",
            Recommendation: value,
            TargetPersonName: null,
            TimingNotes: null,
            Confidence: confidence));
    }

    private static void AddStandingPartnerRisk(JsonElement root, long canonicalOrgId, IntelConfidence confidence, List<IntelRiskDraft> risks)
    {
        var value = GetStringPropertyAny(root, "hasStandingStructuralPartner");
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var lowered = value.ToLowerInvariant();
        if (!lowered.Contains("yes", StringComparison.Ordinal) && !lowered.Contains("true", StringComparison.Ordinal))
        {
            return;
        }

        risks.Add(new IntelRiskDraft(
            CanonicalOrgId: canonicalOrgId,
            RiskType: "KeyPersonDependency",
            Description: $"Firm has standing structural partner: {value}",
            MitigationNotes: null,
            Confidence: confidence));
    }

    private static void AddTimingAction(JsonElement root, long canonicalOrgId, IntelConfidence confidence, List<IntelActionDraft> actions)
    {
        var value = GetStringPropertyAny(root, "calgaryTripNotes");
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        actions.Add(new IntelActionDraft(
            CanonicalOrgId: canonicalOrgId,
            ActionType: "TimingWindow",
            Recommendation: value,
            TargetPersonName: null,
            TimingNotes: value,
            Confidence: confidence));
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

    private static string? GetObjectStringProperty(JsonElement root, string objectPropertyName, params string[] subfieldNames)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(objectPropertyName, out var obj)
            || obj.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetStringPropertyAny(obj, subfieldNames);
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
}
