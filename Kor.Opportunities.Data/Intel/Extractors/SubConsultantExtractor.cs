#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

public sealed class SubConsultantExtractor : IIntelExtractor
{
    public string ProviderName => "SubConsultant";

    public ExtractedIntel Extract(IntelExtractionContext ctx)
    {
        try
        {
            var root = ctx.ResultJson.RootElement;
            var confidence = ParseConfidence(root);
            var works = new List<IntelWorkDraft>();
            var narratives = new List<IntelNarrativeDraft>();

            AddTeamsWith(root, ctx.CanonicalOrgId, confidence, narratives);
            AddWorks(root, ctx.CanonicalOrgId, confidence, works);
            AddNarrative(root, "discipline", "Summary", ctx.CanonicalOrgId, confidence, narratives, "Discipline: ");

            if (works.Count == 0 && narratives.Count == 0)
            {
                return ExtractedIntel.Empty;
            }

            return new ExtractedIntel(
                Array.Empty<IntelPersonDraft>(),
                Array.Empty<IntelPersonAffiliationDraft>(),
                Array.Empty<IntelSignalDraft>(),
                Array.Empty<IntelActionDraft>(),
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

    private static void AddTeamsWith(JsonElement root, long canonicalOrgId, IntelConfidence confidence, List<IntelNarrativeDraft> narratives)
    {
        if (!TryGetArray(root, "teamsWith", out var items))
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
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
                ParagraphText: $"Teams with: {firmName}",
                Confidence: confidence));
        }
    }

    private static void AddWorks(JsonElement root, long canonicalOrgId, IntelConfidence confidence, List<IntelWorkDraft> works)
    {
        if (!TryGetArray(root, "notableProjects", out var items))
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
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
                Role: GetStringPropertyAny(item, "role") ?? "Sub",
                YearApprox: GetStringPropertyAny(item, "yearApprox"),
                EstimatedValueCad: null,
                EstimatedValueText: null,
                Notes: GetStringPropertyAny(item, "notes"),
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
