#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

public sealed class PublicSectorResearchExtractor : IIntelExtractor
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

    public string ProviderName => "PublicSectorResearch";

    public ExtractedIntel Extract(IntelExtractionContext ctx)
    {
        try
        {
            var root = ctx.ResultJson.RootElement;
            var rowConfidence = ParseConfidence(root);
            var people = new List<IntelPersonDraft>();
            var affiliations = new List<IntelPersonAffiliationDraft>();
            var signals = new List<IntelSignalDraft>();

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("decisionMakers", out var decisionMakers))
            {
                foreach (var item in EnumerateObjectOrArray(decisionMakers))
                {
                    var name = GetStringProperty(item, "name") ?? GetStringProperty(item, "fullName");
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
                        LinkedinUrl: null,
                        Notes: notes,
                        Confidence: rowConfidence));

                    affiliations.Add(new IntelPersonAffiliationDraft(
                        PersonDisplayName: name,
                        CanonicalOrgId: ctx.CanonicalOrgId,
                        Title: title,
                        Department: null,
                        IsCurrent: !isDeparted,
                        StartDateApprox: null,
                        EndDateApprox: null,
                        Notes: notes,
                        Confidence: rowConfidence));

                    if (isDeparted)
                    {
                        signals.Add(new IntelSignalDraft(
                            CanonicalOrgId: ctx.CanonicalOrgId,
                            SignalType: "LeadershipChange",
                            Subject: Truncate($"{title ?? "Role"} departure: {name}", 500),
                            Detail: notes,
                            OccurredAtApprox: null,
                            SourceUrl: null,
                            Confidence: rowConfidence));
                    }
                }
            }

            if (people.Count == 0 && affiliations.Count == 0 && signals.Count == 0)
            {
                return ExtractedIntel.Empty;
            }

            return new ExtractedIntel(
                people,
                affiliations,
                signals,
                Array.Empty<IntelActionDraft>(),
                Array.Empty<IntelWorkDraft>(),
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
