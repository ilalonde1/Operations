#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

public sealed class InstitutionalOwnerResearchExtractor : IIntelExtractor
{
    public string ProviderName => "InstitutionalOwnerResearch";

    public ExtractedIntel Extract(IntelExtractionContext ctx)
    {
        try
        {
            var root = ctx.ResultJson.RootElement;
            var confidence = ParseConfidence(root);
            var actions = new List<IntelActionDraft>();
            var narratives = new List<IntelNarrativeDraft>();

            AddNarrative(root, "structuralRelevance", "Summary", ctx.CanonicalOrgId, confidence, narratives, null);
            AddAction(root, "korRelevanceReason", "PursuitAngle", ctx.CanonicalOrgId, confidence, actions, null, null);
            AddNarrative(root, "procurementVehicle", "Current", ctx.CanonicalOrgId, confidence, narratives, "Procurement vehicle: ");
            AddNarrative(root, "annualOrProgramCapitalBudget", "Current", ctx.CanonicalOrgId, confidence, narratives, "Capital budget: ");
            AddNarrative(root, "typicalPrimeDiscipline", "Summary", ctx.CanonicalOrgId, confidence, narratives, "Typical prime discipline: ");

            if (actions.Count == 0 && narratives.Count == 0)
            {
                return ExtractedIntel.Empty;
            }

            return new ExtractedIntel(
                Array.Empty<IntelPersonDraft>(),
                Array.Empty<IntelPersonAffiliationDraft>(),
                Array.Empty<IntelSignalDraft>(),
                actions,
                Array.Empty<IntelWorkDraft>(),
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
        List<IntelActionDraft> actions,
        string? prefix,
        string? timingNotes)
    {
        var value = GetStringPropertyAny(root, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        actions.Add(new IntelActionDraft(
            CanonicalOrgId: canonicalOrgId,
            ActionType: actionType,
            Recommendation: prefix is null ? value : prefix + value,
            TargetPersonName: null,
            TimingNotes: timingNotes,
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
}
