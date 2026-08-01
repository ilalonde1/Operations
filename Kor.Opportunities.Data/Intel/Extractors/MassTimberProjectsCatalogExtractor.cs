#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

public sealed class MassTimberProjectsCatalogExtractor : IIntelExtractor
{
    public string ProviderName => "MassTimberProjectsCatalog";

    public ExtractedIntel Extract(IntelExtractionContext ctx)
    {
        try
        {
            var root = ctx.ResultJson.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("projects", out var projects)
                || projects.ValueKind != JsonValueKind.Array)
            {
                return ExtractedIntel.Empty;
            }

            var works = new List<IntelWorkDraft>();
            foreach (var project in projects.EnumerateArray())
            {
                var projectName = GetStringProperty(project, "projectName");
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    continue;
                }

                works.Add(new IntelWorkDraft(
                    CanonicalOrgId: ctx.CanonicalOrgId,
                    ProjectName: projectName,
                    Role: "Catalog",
                    YearApprox: GetNumberPropertyText(project, "completionYear"),
                    EstimatedValueCad: GetDecimalProperty(project, "estCostCad"),
                    EstimatedValueText: null,
                    Notes: BuildNotes(project),
                    Confidence: IntelConfidence.Medium));
            }

            if (works.Count == 0)
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

    private static string? BuildNotes(JsonElement project)
    {
        var parts = new List<string>();
        AddPart(parts, "Architect", GetStringProperty(project, "architect"));
        AddPart(parts, "SE", GetStringProperty(project, "structuralEngineer"));
        AddPart(parts, "Owner", GetStringProperty(project, "owner"));
        AddPart(parts, "City", GetStringProperty(project, "city"));
        AddPart(parts, "Year", GetNumberPropertyText(project, "completionYear"));
        AddPart(parts, "Status", GetStringProperty(project, "status"));
        AddPart(parts, "Fabricator", GetStringProperty(project, "fabricator"));
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static void AddPart(List<string> parts, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label}: {value}.");
        }
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static string? GetNumberPropertyText(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
                ? property.GetRawText()
                : null;
    }

    private static decimal? GetDecimalProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDecimal(out var value)
                ? value
                : null;
    }
}
