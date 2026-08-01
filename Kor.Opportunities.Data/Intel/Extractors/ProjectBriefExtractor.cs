#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

public sealed class ProjectBriefExtractor : IProjectIntelExtractor
{
    public string ProviderName => "ProjectBrief";

    public ExtractedProjectIntel Extract(ProjectIntelExtractionContext ctx)
    {
        try
        {
            var root = ctx.ResultJson.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                TraceSkip(ctx, "root JSON is not an object");
                return ExtractedProjectIntel.Empty;
            }

            var confidence = ParseConfidence(root);
            var projects = new List<IntelProjectDraft>();
            var signals = new List<IntelProjectSignalDraft>();
            var actions = new List<IntelProjectActionDraft>();
            var risks = new List<IntelProjectRiskDraft>();
            var keyPeople = new List<IntelProjectKeyPersonDraft>();

            var description = GetStringProperty(root, "description");
            var schedule = GetStringProperty(root, "schedule");
            var status = GetStringProperty(root, "status");
            var korAngle = GetStringProperty(root, "korAngle");
            if (!string.IsNullOrWhiteSpace(description)
                || !string.IsNullOrWhiteSpace(schedule)
                || !string.IsNullOrWhiteSpace(status)
                || !string.IsNullOrWhiteSpace(korAngle))
            {
                projects.Add(new IntelProjectDraft(
                    ctx.MajorProjectsInventoryId,
                    description,
                    schedule,
                    status,
                    korAngle,
                    confidence));
            }

            AddSignals(root, ctx.MajorProjectsInventoryId, confidence, signals);
            AddActions(root, ctx.MajorProjectsInventoryId, confidence, actions);
            AddRisks(root, ctx.MajorProjectsInventoryId, confidence, risks);
            AddKeyPeople(root, ctx.MajorProjectsInventoryId, confidence, keyPeople);

            if (projects.Count + signals.Count + actions.Count + risks.Count + keyPeople.Count == 0)
            {
                return ExtractedProjectIntel.Empty;
            }

            return new ExtractedProjectIntel(projects, signals, actions, risks, keyPeople);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                "Project intel extractor failed for provider {0}, MPI id {1}: {2}",
                ctx.ProviderName,
                ctx.MajorProjectsInventoryId,
                ex.Message);
            return ExtractedProjectIntel.Empty;
        }
    }

    private static void AddSignals(
        JsonElement root,
        long mpiId,
        IntelConfidence confidence,
        List<IntelProjectSignalDraft> signals)
    {
        if (!TryGetArray(root, "signals", out var arr))
        {
            return;
        }

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var subject = GetStringProperty(item, "subject");
            if (string.IsNullOrWhiteSpace(subject))
            {
                continue;
            }

            signals.Add(new IntelProjectSignalDraft(
                mpiId,
                NonBlank(GetStringProperty(item, "type"), "Other"),
                subject,
                GetStringProperty(item, "detail"),
                GetStringProperty(item, "occurredAt"),
                GetStringProperty(item, "sourceUrl"),
                confidence));
        }
    }

    private static void AddActions(
        JsonElement root,
        long mpiId,
        IntelConfidence confidence,
        List<IntelProjectActionDraft> actions)
    {
        if (!TryGetArray(root, "actions", out var arr))
        {
            return;
        }

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var recommendation = GetStringProperty(item, "recommendation");
            if (string.IsNullOrWhiteSpace(recommendation))
            {
                continue;
            }

            actions.Add(new IntelProjectActionDraft(
                mpiId,
                NonBlank(GetStringProperty(item, "type"), "Other"),
                recommendation,
                GetStringProperty(item, "targetPerson"),
                null,
                GetStringProperty(item, "timingNotes"),
                confidence));
        }
    }

    private static void AddRisks(
        JsonElement root,
        long mpiId,
        IntelConfidence confidence,
        List<IntelProjectRiskDraft> risks)
    {
        if (!TryGetArray(root, "risks", out var arr))
        {
            return;
        }

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var description = GetStringProperty(item, "description");
            if (string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            risks.Add(new IntelProjectRiskDraft(
                mpiId,
                NonBlank(GetStringProperty(item, "type"), "Other"),
                description,
                GetStringProperty(item, "mitigation"),
                confidence));
        }
    }

    private static void AddKeyPeople(
        JsonElement root,
        long mpiId,
        IntelConfidence confidence,
        List<IntelProjectKeyPersonDraft> keyPeople)
    {
        if (!TryGetArray(root, "keyPeople", out var arr))
        {
            return;
        }

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = GetStringProperty(item, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            keyPeople.Add(new IntelProjectKeyPersonDraft(
                mpiId,
                name,
                GetStringProperty(item, "title"),
                NonBlank(GetStringProperty(item, "side"), "Other"),
                null,
                confidence));
        }
    }

    private static bool TryGetArray(JsonElement root, string propertyName, out JsonElement array)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(propertyName, out array)
            && array.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        array = default;
        return false;
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

        return IntelConfidence.Medium;
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static string NonBlank(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static void TraceSkip(ProjectIntelExtractionContext ctx, string reason)
    {
        System.Diagnostics.Trace.TraceWarning(
            "Project intel extractor skipped provider {0}, MPI id {1}: {2}.",
            ctx.ProviderName,
            ctx.MajorProjectsInventoryId,
            reason);
    }
}
