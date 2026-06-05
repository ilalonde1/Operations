#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

public sealed class PersonListExtractor : IIntelExtractor
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

    public PersonListExtractor(string providerName)
    {
        ProviderName = providerName;
    }

    public string ProviderName { get; }

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

            if (!TryGetProperty(root, out var peopleElement, "people", "decisionMakers", "keyPeople"))
            {
                return ExtractedIntel.Empty;
            }

            foreach (var item in EnumerateObjectOrArray(peopleElement))
            {
                var name = GetStringPropertyAny(item, "name", "fullName", "full_name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var notes = GetStringPropertyAny(item, "notes");
                var firmName = GetStringPropertyAny(item, "firmName");
                var affNotes = string.IsNullOrWhiteSpace(firmName)
                    ? notes
                    : string.IsNullOrWhiteSpace(notes) ? $"Firm: {firmName}." : $"Firm: {firmName}. {notes}";
                var title = GetStringPropertyAny(item, "title", "role", "decisionRole");
                var isDeparted = IsDeparted(notes);

                people.Add(new IntelPersonDraft(
                    DisplayName: name,
                    Email: GetStringPropertyAny(item, "email"),
                    Phone: GetStringPropertyAny(item, "phone"),
                    LinkedinUrl: GetStringPropertyAny(item, "linkedin", "linkedinUrl", "linkedin_url"),
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
                    Notes: affNotes,
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

                var korConnection = GetStringPropertyAny(item, "korConnection", "korRelationship");
                if (!string.IsNullOrWhiteSpace(korConnection))
                {
                    actions.Add(new IntelActionDraft(
                        CanonicalOrgId: ctx.CanonicalOrgId,
                        ActionType: "ContactStrategy",
                        Recommendation: korConnection,
                        TargetPersonName: name,
                        TimingNotes: null,
                        Confidence: rowConfidence));
                }
            }

            if (people.Count == 0 && affiliations.Count == 0 && signals.Count == 0 && actions.Count == 0)
            {
                return ExtractedIntel.Empty;
            }

            return new ExtractedIntel(
                people,
                affiliations,
                signals,
                actions,
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
