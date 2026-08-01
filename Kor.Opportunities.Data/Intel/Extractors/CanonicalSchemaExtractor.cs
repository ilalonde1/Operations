#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

public sealed class CanonicalSchemaExtractor : IIntelExtractor
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

    public CanonicalSchemaExtractor(string providerName)
    {
        ProviderName = providerName;
    }

    public string ProviderName { get; }

    public ExtractedIntel Extract(IntelExtractionContext ctx)
    {
        // Top-level try/catch handles only fundamental failures (root not
        // parseable, confidence read fails). Each per-section call below
        // owns its own try/catch via TrySection so a single bad block
        // (e.g. malformed signals array) doesn't lose every other valid
        // section. R94: caught 2026-06-07 when Bird Design-Build Construction
        // silently produced zero Intel rows despite having decisionMakers,
        // signals, actions, narratives, works — one bad type somewhere
        // killed the whole extract under the old method-wide try/catch.
        JsonElement root;
        IntelConfidence rowConfidence;
        try
        {
            root = ctx.ResultJson.RootElement;
            rowConfidence = ParseConfidence(root);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                "Intel extractor top-level read failed for provider {0}, canonical org id {1}: {2}",
                ctx.ProviderName, ctx.CanonicalOrgId, ex.Message);
            return ExtractedIntel.Empty;
        }

        var people = new List<IntelPersonDraft>();
        var affiliations = new List<IntelPersonAffiliationDraft>();
        var signals = new List<IntelSignalDraft>();
        var actions = new List<IntelActionDraft>();
        var works = new List<IntelWorkDraft>();
        var risks = new List<IntelRiskDraft>();
        var narratives = new List<IntelNarrativeDraft>();

        TrySection(ctx, "decisionMakers",
            () => AddDecisionMakers(root, ctx.CanonicalOrgId, rowConfidence, people, affiliations, signals));
        TrySection(ctx, "signals",
            () => AddSignals(root, ctx.CanonicalOrgId, rowConfidence, signals));
        TrySection(ctx, "actions",
            () => AddActions(root, ctx.CanonicalOrgId, rowConfidence, actions));
        TrySection(ctx, "works",
            () => AddWorks(root, ctx.CanonicalOrgId, rowConfidence, works));
        TrySection(ctx, "risks",
            () => AddRisks(root, ctx.CanonicalOrgId, rowConfidence, risks));
        TrySection(ctx, "narratives",
            () => AddNarratives(root, ctx.CanonicalOrgId, rowConfidence, narratives));

        if (people.Count == 0
            && affiliations.Count == 0
            && signals.Count == 0
            && actions.Count == 0
            && works.Count == 0
            && risks.Count == 0
            && narratives.Count == 0)
        {
            return ExtractedIntel.Empty;
        }

        return new ExtractedIntel(people, affiliations, signals, actions, works, risks, narratives);
    }

    private static void TrySection(IntelExtractionContext ctx, string section, Action body)
    {
        try
        {
            body();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                "Intel extractor section '{0}' failed for provider {1}, canonical org id {2}: {3}. Other sections continue.",
                section, ctx.ProviderName, ctx.CanonicalOrgId, ex.Message);
        }
    }

    private static void AddDecisionMakers(
        JsonElement root,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelPersonDraft> people,
        List<IntelPersonAffiliationDraft> affiliations,
        List<IntelSignalDraft> signals)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("decisionMakers", out var decisionMakers))
        {
            return;
        }

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

    private static void AddSignals(
        JsonElement root,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelSignalDraft> signals)
    {
        if (!TryGetArray(root, "signals", out var items))
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
        {
            var subject = GetStringProperty(item, "subject");
            if (string.IsNullOrWhiteSpace(subject))
            {
                continue;
            }

            signals.Add(new IntelSignalDraft(
                CanonicalOrgId: canonicalOrgId,
                SignalType: NonEmptyOrDefault(GetStringProperty(item, "signalType"), "Other"),
                Subject: Truncate(subject, 500),
                Detail: GetStringProperty(item, "detail"),
                OccurredAtApprox: GetStringProperty(item, "occurredAtApprox"),
                SourceUrl: GetStringProperty(item, "sourceUrl"),
                Confidence: confidence));
        }
    }

    private static void AddActions(
        JsonElement root,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelActionDraft> actions)
    {
        if (!TryGetArray(root, "actions", out var items))
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
        {
            var recommendation = GetStringProperty(item, "recommendation");
            if (string.IsNullOrWhiteSpace(recommendation))
            {
                continue;
            }

            actions.Add(new IntelActionDraft(
                CanonicalOrgId: canonicalOrgId,
                ActionType: NonEmptyOrDefault(GetStringProperty(item, "actionType"), "Other"),
                Recommendation: recommendation,
                TargetPersonName: GetStringProperty(item, "targetPersonName"),
                TimingNotes: GetStringProperty(item, "timingNotes"),
                Confidence: confidence));
        }
    }

    private static void AddWorks(
        JsonElement root,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelWorkDraft> works)
    {
        if (!TryGetArray(root, "works", out var items))
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
                Role: GetStringProperty(item, "role"),
                YearApprox: GetStringProperty(item, "yearApprox"),
                EstimatedValueCad: GetDecimalProperty(item, "estimatedValueCad"),
                EstimatedValueText: GetStringProperty(item, "estimatedValueText"),
                Notes: GetStringProperty(item, "notes"),
                Confidence: confidence));
        }
    }

    private static void AddRisks(
        JsonElement root,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelRiskDraft> risks)
    {
        if (!TryGetArray(root, "risks", out var items))
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
        {
            var description = GetStringProperty(item, "description");
            if (string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            risks.Add(new IntelRiskDraft(
                CanonicalOrgId: canonicalOrgId,
                RiskType: NonEmptyOrDefault(GetStringProperty(item, "riskType"), "Other"),
                Description: description,
                MitigationNotes: GetStringProperty(item, "mitigationNotes"),
                Confidence: confidence));
        }
    }

    private static void AddNarratives(
        JsonElement root,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelNarrativeDraft> narratives)
    {
        if (!TryGetArray(root, "narratives", out var items))
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
        {
            var paragraphText = GetStringProperty(item, "paragraphText");
            if (string.IsNullOrWhiteSpace(paragraphText))
            {
                continue;
            }

            narratives.Add(new IntelNarrativeDraft(
                CanonicalOrgId: canonicalOrgId,
                NarrativeType: NonEmptyOrDefault(GetStringProperty(item, "narrativeType"), "Summary"),
                ParagraphText: paragraphText,
                Confidence: confidence));
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

    private static decimal? GetDecimalProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDecimal(out var value)
                ? value
                : null;
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
