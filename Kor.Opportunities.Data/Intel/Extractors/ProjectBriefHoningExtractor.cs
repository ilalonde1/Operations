#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

/// <summary>
/// Extractor for honing-pass enrichment rows (BD-Audit-2026-06-09 M3).
/// Mirrors <see cref="ProjectBriefExtractor"/>'s field mappings and
/// confidence conventions exactly, but tolerates the three legacy JSON
/// shapes found under ProviderName = "ProjectBriefHoning":
///   (a) first-pass-like: top-level { overallConfidence, description,
///       keyPeople, actions, signals, risks, korAngle, schedule, status };
///   (b) top-level verdict: { verdict, ... } with intel arrays at root;
///   (c) nested: { ..., honingPass: { verdict, overallConfidence?,
///       keyPeople?, actions?, signals?, risks?, korAngle?, ... } }.
/// When a root "honingPass" object exists, fields/arrays are read from it
/// first, falling back to the root for anything honingPass lacks. The
/// honing verdict (PURSUE / PURSUE_URGENT / MONITOR / DEAD / DISCOVER /
/// DUPLICATE) is surfaced through the existing Status field only when the
/// payload carries no explicit "status" — IntelProjectDraft is not extended.
/// </summary>
public sealed class ProjectBriefHoningExtractor : IProjectIntelExtractor
{
    public string ProviderName => "ProjectBriefHoning";

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

            JsonElement? honingPass = null;
            if (root.TryGetProperty("honingPass", out var hp) && hp.ValueKind == JsonValueKind.Object)
            {
                honingPass = hp;
            }

            var confidence = ParseConfidence(honingPass, root);
            var projects = new List<IntelProjectDraft>();
            var signals = new List<IntelProjectSignalDraft>();
            var actions = new List<IntelProjectActionDraft>();
            var risks = new List<IntelProjectRiskDraft>();
            var keyPeople = new List<IntelProjectKeyPersonDraft>();

            var description = GetScalar(honingPass, root, "description");
            var schedule = GetScalar(honingPass, root, "schedule");
            var status = GetScalar(honingPass, root, "status");
            var korAngle = GetScalar(honingPass, root, "korAngle");

            // Verdict-only payloads (shapes b/c) often carry no "status";
            // surface the honing verdict there so it isn't write-only.
            if (string.IsNullOrWhiteSpace(status))
            {
                status = GetScalar(honingPass, root, "verdict");
            }

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

            AddSignals(ArrayHost(honingPass, root, "signals"), ctx.MajorProjectsInventoryId, confidence, signals);
            AddActions(ArrayHost(honingPass, root, "actions"), ctx.MajorProjectsInventoryId, confidence, actions);
            AddRisks(ArrayHost(honingPass, root, "risks"), ctx.MajorProjectsInventoryId, confidence, risks);
            AddKeyPeople(ArrayHost(honingPass, root, "keyPeople"), ctx.MajorProjectsInventoryId, confidence, keyPeople);

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

    /// <summary>
    /// Picks the element that actually carries the named intel array:
    /// honingPass when present AND it has the array, otherwise the root.
    /// </summary>
    private static JsonElement ArrayHost(JsonElement? honingPass, JsonElement root, string propertyName)
    {
        if (honingPass is { } hp && TryGetArray(hp, propertyName, out _))
        {
            return hp;
        }

        return root;
    }

    private static string? GetScalar(JsonElement? honingPass, JsonElement root, string propertyName)
    {
        if (honingPass is { } hp)
        {
            var fromHoning = GetStringProperty(hp, propertyName);
            if (!string.IsNullOrWhiteSpace(fromHoning))
            {
                return fromHoning;
            }
        }

        return GetStringProperty(root, propertyName);
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

    private static IntelConfidence ParseConfidence(JsonElement? honingPass, JsonElement root)
    {
        if (honingPass is { } hp && TryParseConfidence(hp, out var fromHoning))
        {
            return fromHoning;
        }

        if (TryParseConfidence(root, out var fromRoot))
        {
            return fromRoot;
        }

        return IntelConfidence.Medium;
    }

    private static bool TryParseConfidence(JsonElement element, out IntelConfidence confidence)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("overallConfidence", out var overallConfidence)
            && overallConfidence.ValueKind == JsonValueKind.Number
            && overallConfidence.TryGetDouble(out var value))
        {
            // Same convention as ProjectBriefExtractor.ParseConfidence:
            // < 0.6 => Low, otherwise Medium.
            confidence = value < 0.6 ? IntelConfidence.Low : IntelConfidence.Medium;
            return true;
        }

        confidence = IntelConfidence.Medium;
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
