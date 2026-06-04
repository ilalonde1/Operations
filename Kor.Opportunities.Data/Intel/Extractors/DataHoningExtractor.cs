#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

public sealed class DataHoningExtractor : IIntelExtractor
{
    public string ProviderName => "DataHoning";

    public ExtractedIntel Extract(IntelExtractionContext ctx)
    {
        try
        {
            var root = ctx.ResultJson.RootElement;
            var rowConfidence = ParseConfidence(root);
            var people = new List<IntelPersonDraft>();
            var affiliations = new List<IntelPersonAffiliationDraft>();
            var works = new List<IntelWorkDraft>();

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("keyPeople", out var keyPeople))
            {
                foreach (var item in EnumerateObjectOrArray(keyPeople))
                {
                    var name = GetStringProperty(item, "name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    people.Add(new IntelPersonDraft(
                        name,
                        Email: null,
                        Phone: null,
                        LinkedinUrl: null,
                        Notes: null,
                        Confidence: rowConfidence));

                    affiliations.Add(new IntelPersonAffiliationDraft(
                        PersonDisplayName: name,
                        CanonicalOrgId: ctx.CanonicalOrgId,
                        Title: GetStringProperty(item, "title"),
                        Department: null,
                        IsCurrent: true,
                        StartDateApprox: null,
                        EndDateApprox: null,
                        Notes: null,
                        Confidence: rowConfidence));
                }
            }

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("notableProjects", out var notableProjects)
                && notableProjects.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in notableProjects.EnumerateArray())
                {
                    var name = GetProjectName(item);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    works.Add(new IntelWorkDraft(
                        CanonicalOrgId: ctx.CanonicalOrgId,
                        ProjectName: name,
                        Role: GetStringProperty(item, "role"),
                        YearApprox: GetStringProperty(item, "year"),
                        EstimatedValueCad: null,
                        EstimatedValueText: null,
                        Notes: null,
                        Confidence: rowConfidence));
                }
            }

            if (people.Count == 0 && affiliations.Count == 0 && works.Count == 0)
            {
                return ExtractedIntel.Empty;
            }

            return new ExtractedIntel(
                people,
                affiliations,
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

    private static IntelConfidence ParseConfidence(JsonElement root)
    {
        var value = GetStringProperty(root, "_confidence");
        return value?.Trim().ToLowerInvariant() switch
        {
            "high" => IntelConfidence.High,
            "low" => IntelConfidence.Low,
            _ => IntelConfidence.Medium,
        };
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
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

    private static string? GetProjectName(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : GetStringProperty(element, "name");
    }
}
