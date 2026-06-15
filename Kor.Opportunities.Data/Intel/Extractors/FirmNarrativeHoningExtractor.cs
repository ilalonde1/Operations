#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

/// <summary>
/// Extracts structured intel from FirmNarrativeHoning enrichment results.
/// Handles two output shapes produced by honing drains:
///   - Standard honing (vip-*-deep): paragraphCurrent/History/Action, decisionMakers,
///     signals[], activePipeline[], seAllegiances[], korDisplacementStrategy
///   - SE allegiance (van/ab-se-allegiance): narratives[{section,content}], signals[],
///     seAllegiances[], primarySe, displacementStrategy, openSeProjects[]
/// Supersedes PersonListExtractor("FirmNarrativeHoning") which only read people arrays.
/// </summary>
public sealed class FirmNarrativeHoningExtractor : IIntelExtractor
{
    private static readonly string[] DepartureTerms =
    [
        "departed", "stepping down", "former", "outgoing", "left", "resigned", "no longer",
    ];

    public string ProviderName => "FirmNarrativeHoning";

    public ExtractedIntel Extract(IntelExtractionContext ctx)
    {
        try
        {
            var root = ctx.ResultJson.RootElement;
            var conf = ParseConfidence(root);
            var people = new List<IntelPersonDraft>();
            var affiliations = new List<IntelPersonAffiliationDraft>();
            var signals = new List<IntelSignalDraft>();
            var actions = new List<IntelActionDraft>();
            var works = new List<IntelWorkDraft>();
            var narratives = new List<IntelNarrativeDraft>();

            AddPeople(root, ctx.CanonicalOrgId, conf, people, affiliations, actions);
            AddNarrativeString(root, "paragraphCurrent", "Current", ctx.CanonicalOrgId, conf, narratives);
            AddNarrativeString(root, "paragraphHistory", "History", ctx.CanonicalOrgId, conf, narratives);
            AddParagraphAction(root, ctx.CanonicalOrgId, conf, narratives, actions);
            AddNarrativesArray(root, ctx.CanonicalOrgId, conf, narratives);
            AddSignalsArray(root, ctx.CanonicalOrgId, conf, signals);
            AddSeAllegiances(root, ctx.CanonicalOrgId, conf, signals, narratives);
            AddDisplacementStrategy(root, ctx.CanonicalOrgId, conf, actions);
            AddActivePipeline(root, ctx.CanonicalOrgId, conf, works);
            AddOpenSeProjects(root, ctx.CanonicalOrgId, conf, narratives);

            if (people.Count + affiliations.Count + signals.Count + actions.Count
                + works.Count + narratives.Count == 0)
            {
                return ExtractedIntel.Empty;
            }

            return new ExtractedIntel(
                people,
                affiliations,
                signals,
                actions,
                works,
                Array.Empty<IntelRiskDraft>(),
                narratives);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                "Intel extractor failed for provider {0}, canonical org id {1}: {2}",
                ctx.ProviderName, ctx.CanonicalOrgId, ex.Message);
            return ExtractedIntel.Empty;
        }
    }

    // ── People ────────────────────────────────────────────────────────────────

    private static void AddPeople(
        JsonElement root, long orgId, IntelConfidence conf,
        List<IntelPersonDraft> people,
        List<IntelPersonAffiliationDraft> affiliations,
        List<IntelActionDraft> actions)
    {
        JsonElement arr;
        if (!TryGetArray(root, out arr, "decisionMakers", "keyPeople", "people"))
        {
            return;
        }

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var name = GetStr(item, "name", "fullName", "personName");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var notes = GetStr(item, "notes");
            var title = GetStr(item, "title", "role", "decisionRole");
            var isDeparted = IsDeparted(notes);

            people.Add(new IntelPersonDraft(
                DisplayName: name,
                Email: GetStr(item, "email"),
                Phone: GetStr(item, "phone"),
                LinkedinUrl: GetStr(item, "linkedinUrl", "linkedin", "linkedInUrl"),
                Notes: notes,
                Confidence: conf));

            affiliations.Add(new IntelPersonAffiliationDraft(
                PersonDisplayName: name,
                CanonicalOrgId: orgId,
                Title: title,
                Department: null,
                IsCurrent: !isDeparted,
                StartDateApprox: null,
                EndDateApprox: null,
                Notes: notes,
                Confidence: conf));

            var korConn = GetStr(item, "korConnection", "korRelationship");
            if (!string.IsNullOrWhiteSpace(korConn))
            {
                actions.Add(new IntelActionDraft(
                    CanonicalOrgId: orgId,
                    ActionType: "ContactStrategy",
                    Recommendation: korConn,
                    TargetPersonName: name,
                    TimingNotes: null,
                    Confidence: conf));
            }
        }
    }

    // ── Narrative strings ─────────────────────────────────────────────────────

    private static void AddNarrativeString(
        JsonElement root, string prop, string narrativeType,
        long orgId, IntelConfidence conf, List<IntelNarrativeDraft> narratives)
    {
        var text = GetStr(root, prop);
        if (string.IsNullOrWhiteSpace(text)) return;
        narratives.Add(new IntelNarrativeDraft(orgId, narrativeType, text, conf));
    }

    private static void AddParagraphAction(
        JsonElement root, long orgId, IntelConfidence conf,
        List<IntelNarrativeDraft> narratives, List<IntelActionDraft> actions)
    {
        var text = GetStr(root, "paragraphAction");
        if (string.IsNullOrWhiteSpace(text)) return;
        narratives.Add(new IntelNarrativeDraft(orgId, "Action", text, conf));
        actions.Add(new IntelActionDraft(orgId, "PursuitAngle", text, null, null, conf));
    }

    // ── Narratives array [{section, content}] (SE allegiance shape) ──────────

    private static void AddNarrativesArray(
        JsonElement root, long orgId, IntelConfidence conf,
        List<IntelNarrativeDraft> narratives)
    {
        if (!TryGetArray(root, out var arr, "narratives")) return;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var content = GetStr(item, "content", "paragraphText");
            if (string.IsNullOrWhiteSpace(content)) continue;
            narratives.Add(new IntelNarrativeDraft(orgId, "Summary", content, conf));
        }
    }

    // ── Signals array ─────────────────────────────────────────────────────────

    private static void AddSignalsArray(
        JsonElement root, long orgId, IntelConfidence conf,
        List<IntelSignalDraft> signals)
    {
        if (!TryGetArray(root, out var arr, "signals")) return;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var subject = GetStr(item, "subject");
            if (string.IsNullOrWhiteSpace(subject)) continue;
            var signalType = GetStr(item, "signalType") ?? "Other";
            signals.Add(new IntelSignalDraft(
                CanonicalOrgId: orgId,
                SignalType: Truncate(signalType, 100),
                Subject: Truncate(subject, 500),
                Detail: GetStr(item, "detail"),
                OccurredAtApprox: GetStr(item, "occurredAtApprox"),
                SourceUrl: GetStr(item, "sourceUrl"),
                Confidence: conf));
        }
    }

    // ── SE allegiances → signals + primary SE narrative ───────────────────────

    private static void AddSeAllegiances(
        JsonElement root, long orgId, IntelConfidence conf,
        List<IntelSignalDraft> signals, List<IntelNarrativeDraft> narratives)
    {
        if (TryGetArray(root, out var arr, "seAllegiances"))
        {
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var seFirm = GetStr(item, "seFirm");
                if (string.IsNullOrWhiteSpace(seFirm)) continue;
                var lock_ = GetStr(item, "lockStrength") ?? "OPEN";
                var count = 0;
                if (item.TryGetProperty("projectCount", out var pc) && pc.ValueKind == JsonValueKind.Number)
                    pc.TryGetInt32(out count);
                var evidence = GetStr(item, "evidence");
                var subject = $"{seFirm} — {lock_}" + (count > 0 ? $" ({count} projects)" : "");
                signals.Add(new IntelSignalDraft(
                    CanonicalOrgId: orgId,
                    SignalType: lock_,
                    Subject: Truncate(subject, 500),
                    Detail: evidence,
                    OccurredAtApprox: null,
                    SourceUrl: null,
                    Confidence: conf));
            }
        }

        var primarySe = GetStr(root, "primarySe");
        if (!string.IsNullOrWhiteSpace(primarySe))
        {
            narratives.Add(new IntelNarrativeDraft(orgId, "Summary", $"Primary SE: {primarySe}", conf));
        }
    }

    // ── Displacement / KOR strategy → action ─────────────────────────────────

    private static void AddDisplacementStrategy(
        JsonElement root, long orgId, IntelConfidence conf,
        List<IntelActionDraft> actions)
    {
        var text = GetStr(root, "korDisplacementStrategy", "displacementStrategy", "korDisplacementRead");
        if (string.IsNullOrWhiteSpace(text)) return;
        actions.Add(new IntelActionDraft(orgId, "KorDisplacementRead", text, null, null, conf));
    }

    // ── Active pipeline → works ───────────────────────────────────────────────

    private static void AddActivePipeline(
        JsonElement root, long orgId, IntelConfidence conf,
        List<IntelWorkDraft> works)
    {
        if (!TryGetArray(root, out var arr, "activePipeline")) return;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var name = GetStr(item, "projectName", "name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            long? valueCad = null;
            if (item.TryGetProperty("estimatedValueCadM", out var vm)
                && vm.ValueKind == JsonValueKind.Number
                && vm.TryGetInt64(out var vml))
            {
                valueCad = vml * 1_000_000;
            }

            var urgency = GetStr(item, "korUrgency");
            var seStatus = GetStr(item, "seStatus");
            var notes = string.Join(" | ", new[] { seStatus, urgency, GetStr(item, "architect") }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!));

            works.Add(new IntelWorkDraft(
                CanonicalOrgId: orgId,
                ProjectName: Truncate(name, 300),
                Role: GetStr(item, "stage"),
                YearApprox: null,
                EstimatedValueCad: valueCad,
                EstimatedValueText: null,
                Notes: string.IsNullOrWhiteSpace(notes) ? null : notes,
                Confidence: conf));
        }
    }

    // ── Open SE projects → narratives ─────────────────────────────────────────

    private static void AddOpenSeProjects(
        JsonElement root, long orgId, IntelConfidence conf,
        List<IntelNarrativeDraft> narratives)
    {
        if (!TryGetArray(root, out var arr, "openSeProjects")) return;
        foreach (var item in arr.EnumerateArray())
        {
            var text = item.ValueKind == JsonValueKind.String ? item.GetString() : GetStr(item, "name");
            if (string.IsNullOrWhiteSpace(text)) continue;
            narratives.Add(new IntelNarrativeDraft(orgId, "Action", $"Open SE seat: {text}", conf));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IntelConfidence ParseConfidence(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return IntelConfidence.Medium;

        // Numeric _confidence (0.0-1.0)
        if (root.TryGetProperty("_confidence", out var cp))
        {
            if (cp.ValueKind == JsonValueKind.Number && cp.TryGetDouble(out var d))
                return d >= 0.8 ? IntelConfidence.High : d < 0.5 ? IntelConfidence.Low : IntelConfidence.Medium;
            if (cp.ValueKind == JsonValueKind.String)
            {
                var s = cp.GetString()?.Trim().ToLowerInvariant();
                if (s == "high") return IntelConfidence.High;
                if (s == "low") return IntelConfidence.Low;
                if (double.TryParse(s, out var d2))
                    return d2 >= 0.8 ? IntelConfidence.High : d2 < 0.5 ? IntelConfidence.Low : IntelConfidence.Medium;
            }
        }

        // overallConfidence fallback
        if (root.TryGetProperty("overallConfidence", out var oc)
            && oc.ValueKind == JsonValueKind.Number && oc.TryGetDouble(out var od))
        {
            return od >= 0.8 ? IntelConfidence.High : od < 0.5 ? IntelConfidence.Low : IntelConfidence.Medium;
        }

        return IntelConfidence.Medium;
    }

    private static string? GetStr(JsonElement el, params string[] names)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        foreach (var n in names)
        {
            if (el.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String)
            {
                var v = p.GetString();
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
        }
        return null;
    }

    private static bool TryGetArray(JsonElement root, out JsonElement value, params string[] names)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var n in names)
            {
                if (root.TryGetProperty(n, out value) && value.ValueKind == JsonValueKind.Array)
                    return true;
            }
        }
        value = default;
        return false;
    }

    private static bool IsDeparted(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return false;
        var low = notes.ToLowerInvariant();
        return DepartureTerms.Any(t => low.Contains(t, StringComparison.Ordinal));
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
