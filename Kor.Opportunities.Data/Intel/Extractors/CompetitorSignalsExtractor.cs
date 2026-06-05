#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

public sealed class CompetitorSignalsExtractor : IIntelExtractor
{
    public string ProviderName => "CompetitorSignals";

    public ExtractedIntel Extract(IntelExtractionContext ctx)
    {
        try
        {
            var root = ctx.ResultJson.RootElement;
            var rowConfidence = ParseConfidence(root);
            var signals = new List<IntelSignalDraft>();
            var works = new List<IntelWorkDraft>();
            var risks = new List<IntelRiskDraft>();
            var narratives = new List<IntelNarrativeDraft>();

            AddSignalArray(root, "hiringSignals", "HiringSurge", ctx.CanonicalOrgId, rowConfidence, signals);
            AddSignalArray(root, "leadershipMoves", "LeadershipChange", ctx.CanonicalOrgId, rowConfidence, signals);
            AddSignalArray(root, "officeChanges", "OfficeMove", ctx.CanonicalOrgId, rowConfidence, signals);
            AddSignalArray(root, "ownershipMnA", "OwnershipMnA", ctx.CanonicalOrgId, rowConfidence, signals);
            AddRecentWins(root, ctx.CanonicalOrgId, rowConfidence, signals, works);
            AddCapacityRead(root, ctx.CanonicalOrgId, rowConfidence, signals, risks);
            AddNotes(root, ctx.CanonicalOrgId, rowConfidence, narratives);

            if (signals.Count == 0 && works.Count == 0 && risks.Count == 0 && narratives.Count == 0)
            {
                return ExtractedIntel.Empty;
            }

            return new ExtractedIntel(
                Array.Empty<IntelPersonDraft>(),
                Array.Empty<IntelPersonAffiliationDraft>(),
                signals,
                Array.Empty<IntelActionDraft>(),
                works,
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

    private static void AddSignalArray(
        JsonElement root,
        string propertyName,
        string signalType,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelSignalDraft> signals)
    {
        if (!TryGetArray(root, propertyName, out var items))
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
        {
            var subject = GetSubject(item);
            if (string.IsNullOrWhiteSpace(subject))
            {
                continue;
            }

            signals.Add(new IntelSignalDraft(
                CanonicalOrgId: canonicalOrgId,
                SignalType: signalType,
                Subject: Truncate(subject, 500),
                Detail: null,
                OccurredAtApprox: null,
                SourceUrl: null,
                Confidence: confidence));
        }
    }

    private static void AddRecentWins(
        JsonElement root,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelSignalDraft> signals,
        List<IntelWorkDraft> works)
    {
        if (!TryGetArray(root, "recentWins", out var items))
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var subject = item.GetString();
                if (!string.IsNullOrWhiteSpace(subject))
                {
                    signals.Add(new IntelSignalDraft(
                        CanonicalOrgId: canonicalOrgId,
                        SignalType: "RecentWin",
                        Subject: Truncate(subject, 500),
                        Detail: null,
                        OccurredAtApprox: null,
                        SourceUrl: null,
                        Confidence: confidence));
                }

                continue;
            }

            var projectName = GetStringPropertyAny(item, "projectName");
            if (string.IsNullOrWhiteSpace(projectName))
            {
                continue;
            }

            works.Add(new IntelWorkDraft(
                CanonicalOrgId: canonicalOrgId,
                ProjectName: projectName,
                Role: GetStringPropertyAny(item, "role") ?? "Structural",
                YearApprox: GetStringPropertyAny(item, "yearApprox"),
                EstimatedValueCad: null,
                EstimatedValueText: null,
                Notes: GetStringPropertyAny(item, "notes"),
                Confidence: confidence));
        }
    }

    private static void AddCapacityRead(
        JsonElement root,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelSignalDraft> signals,
        List<IntelRiskDraft> risks)
    {
        var value = GetStringPropertyAny(root, "capacityRead");
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        risks.Add(new IntelRiskDraft(
            CanonicalOrgId: canonicalOrgId,
            RiskType: "CapacityStrain",
            Description: value,
            MitigationNotes: null,
            Confidence: confidence));

        signals.Add(new IntelSignalDraft(
            CanonicalOrgId: canonicalOrgId,
            SignalType: "CapacityStrain",
            Subject: Truncate(value, 500),
            Detail: null,
            OccurredAtApprox: null,
            SourceUrl: null,
            Confidence: confidence));
    }

    private static void AddNotes(
        JsonElement root,
        long canonicalOrgId,
        IntelConfidence confidence,
        List<IntelNarrativeDraft> narratives)
    {
        var notes = GetStringPropertyAny(root, "notes");
        if (string.IsNullOrWhiteSpace(notes))
        {
            return;
        }

        narratives.Add(new IntelNarrativeDraft(
            CanonicalOrgId: canonicalOrgId,
            NarrativeType: "Summary",
            ParagraphText: notes,
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

    private static string? GetSubject(JsonElement item)
    {
        return item.ValueKind == JsonValueKind.String
            ? item.GetString()
            : GetStringPropertyAny(item, "subject", "text", "description");
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

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
