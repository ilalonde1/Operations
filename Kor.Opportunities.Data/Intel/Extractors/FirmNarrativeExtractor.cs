#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

public sealed class FirmNarrativeExtractor : IIntelExtractor
{
    public string ProviderName => "FirmNarrative";

    public ExtractedIntel Extract(IntelExtractionContext ctx)
    {
        try
        {
            var root = ctx.ResultJson.RootElement;
            var rowConfidence = ParseConfidence(root);
            var actions = new List<IntelActionDraft>();
            var narratives = new List<IntelNarrativeDraft>();

            AddNarrative(root, "paragraphCurrent", "Current", ctx.CanonicalOrgId, rowConfidence, narratives);
            AddNarrative(root, "paragraphHistory", "History", ctx.CanonicalOrgId, rowConfidence, narratives);

            var paragraphAction = GetStringProperty(root, "paragraphAction");
            if (!string.IsNullOrWhiteSpace(paragraphAction))
            {
                narratives.Add(new IntelNarrativeDraft(
                    CanonicalOrgId: ctx.CanonicalOrgId,
                    NarrativeType: "Action",
                    ParagraphText: paragraphAction,
                    Confidence: rowConfidence));

                actions.Add(new IntelActionDraft(
                    CanonicalOrgId: ctx.CanonicalOrgId,
                    ActionType: "PursuitAngle",
                    Recommendation: paragraphAction,
                    TargetPersonName: null,
                    TimingNotes: null,
                    Confidence: rowConfidence));
            }

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
        List<IntelNarrativeDraft> narratives)
    {
        var text = GetStringProperty(root, propertyName);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        narratives.Add(new IntelNarrativeDraft(
            CanonicalOrgId: canonicalOrgId,
            NarrativeType: narrativeType,
            ParagraphText: text,
            Confidence: confidence));
    }

    private static IntelConfidence ParseConfidence(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("overallConfidence", out var overallConfidence)
            && overallConfidence.ValueKind == JsonValueKind.Number
            && overallConfidence.TryGetDouble(out var confidence)
            && confidence < 0.6)
        {
            return IntelConfidence.Low;
        }

        if (HasDataDependencies(root))
        {
            return IntelConfidence.Low;
        }

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("overallConfidence", out overallConfidence)
            && overallConfidence.ValueKind == JsonValueKind.Number
            && overallConfidence.TryGetDouble(out confidence)
            && confidence >= 0.85)
        {
            return IntelConfidence.High;
        }

        return IntelConfidence.Medium;
    }

    private static bool HasDataDependencies(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("dataDependencies", out var dataDependencies)
            || dataDependencies.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in dataDependencies.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(item.GetString()))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }
}
