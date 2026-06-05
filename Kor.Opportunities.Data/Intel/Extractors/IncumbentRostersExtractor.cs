#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

public sealed class IncumbentRostersExtractor : IIntelExtractor
{
    public string ProviderName => "IncumbentRosters";

    public ExtractedIntel Extract(IntelExtractionContext ctx)
    {
        try
        {
            var root = ctx.ResultJson.RootElement;
            var confidence = ParseConfidence(root);
            var actions = new List<IntelActionDraft>();
            var narratives = new List<IntelNarrativeDraft>();

            AddIncumbents(root, ctx.CanonicalOrgId, confidence, narratives);
            AddAction(root, "howToGetOnRoster", "HowToGetOnRoster", ctx.CanonicalOrgId, confidence, actions, null);
            AddRosterStatus(root, ctx.CanonicalOrgId, confidence, narratives);
            AddAction(root, "opportunityTiming", "TimingWindow", ctx.CanonicalOrgId, confidence, actions, null, useValueAsTimingNotes: true);
            AddAction(root, "expiryOrRenewalWindow", "TimingWindow", ctx.CanonicalOrgId, confidence, actions, "Renewal window: ");
            AddNarrative(root, "scope", "Current", ctx.CanonicalOrgId, confidence, narratives, null);
            AddNarrative(root, "estValueOrCeiling", "Summary", ctx.CanonicalOrgId, confidence, narratives, "Roster value/ceiling: ");

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

    private static void AddIncumbents(JsonElement root, long canonicalOrgId, IntelConfidence confidence, List<IntelNarrativeDraft> narratives)
    {
        if (!TryGetArray(root, "incumbentFirms", out var firms))
        {
            return;
        }

        var discipline = GetStringPropertyAny(root, "discipline") ?? "discipline";
        foreach (var item in firms.EnumerateArray())
        {
            var firmName = item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : GetStringPropertyAny(item, "name");
            if (string.IsNullOrWhiteSpace(firmName))
            {
                continue;
            }

            narratives.Add(new IntelNarrativeDraft(
                CanonicalOrgId: canonicalOrgId,
                NarrativeType: "Summary",
                ParagraphText: $"Incumbent ({discipline}): {firmName}",
                Confidence: confidence));
        }
    }

    private static void AddRosterStatus(JsonElement root, long canonicalOrgId, IntelConfidence confidence, List<IntelNarrativeDraft> narratives)
    {
        var value = GetStringPropertyAny(root, "korStatus");
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        narratives.Add(new IntelNarrativeDraft(
            CanonicalOrgId: canonicalOrgId,
            NarrativeType: "Summary",
            ParagraphText: $"KOR roster status: {value}",
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

    private static void AddAction(
        JsonElement root,
        string propertyName,
        string actionType,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelActionDraft> actions,
        string? prefix,
        bool useValueAsTimingNotes = false)
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
            TimingNotes: useValueAsTimingNotes ? value : null,
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
}
