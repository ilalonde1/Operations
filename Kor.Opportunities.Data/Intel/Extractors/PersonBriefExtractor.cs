#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

/// <summary>
/// Decomposes a person-research JSON payload into <see cref="ExtractedIntel"/>
/// drafts. The PersonBrief refresh path produces drafts keyed to the person's
/// CURRENT EMPLOYER canonical org id — that org acts as the carrier for the
/// IntelSignal / IntelAction rows. The IntelPerson + IntelPersonAffiliation
/// rows describe the person and their tie to the employer.
///
/// Expected JSON shape (matches the system prompt for PersonBrief):
///   {
///     "overallConfidence": 0.0-1.0,
///     "person": { "displayName":"...", "email":"...", "phone":"...",
///                 "linkedinUrl":"...", "notes":"..." },
///     "currentAffiliation": { "title":"...", "department":"...",
///                             "startDateApprox":"YYYY-MM",
///                             "confirmed": true|false },
///     "formerAffiliations": [
///         { "orgName":"...", "title":"...", "endDateApprox":"YYYY-MM" }
///     ],
///     "recentSignals": [
///         { "type":"LeadershipChange|HiringSurge|...",
///           "subject":"...", "detail":"...",
///           "occurredAt":"YYYY-MM", "sourceUrl":"..." }
///     ],
///     "korActions": [
///         { "type":"ContactStrategy|PursuitAngle|...",
///           "recommendation":"...", "timingNotes":"..." }
///     ]
///   }
/// </summary>
public sealed class PersonBriefExtractor
{
    /// <summary>
    /// Decompose the person-research JSON. The <paramref name="ctx"/> carries
    /// the canonical org id of the person's current employer — all
    /// CanonicalOrgId-keyed drafts (signals, actions, affiliations) use it.
    /// </summary>
    public ExtractedIntel Extract(PersonBriefExtractionContext ctx)
    {
        try
        {
            var root = ctx.ResultJson.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                TraceSkip(ctx, "root JSON is not an object");
                return ExtractedIntel.Empty;
            }

            var confidence = ParseConfidence(root);

            var people = new List<IntelPersonDraft>();
            var affiliations = new List<IntelPersonAffiliationDraft>();
            var signals = new List<IntelSignalDraft>();
            var actions = new List<IntelActionDraft>();

            AddPerson(root, ctx.PersonDisplayName, confidence, people);
            AddCurrentAffiliation(root, ctx, confidence, affiliations);
            AddFormerAffiliations(root, ctx.PersonDisplayName, confidence, affiliations);
            AddSignals(root, ctx, confidence, signals);
            AddActions(root, ctx, confidence, actions);

            if (people.Count + affiliations.Count + signals.Count + actions.Count == 0)
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
                "Person brief extractor failed for person {0} (id {1}): {2}",
                ctx.PersonDisplayName,
                ctx.IntelPersonId,
                ex.Message);
            return ExtractedIntel.Empty;
        }
    }

    private static void AddPerson(
        JsonElement root,
        string personDisplayName,
        IntelConfidence confidence,
        List<IntelPersonDraft> people)
    {
        if (!TryGetObject(root, "person", out var person))
        {
            return;
        }

        people.Add(new IntelPersonDraft(
            // Always use the IntelPerson DisplayName the executor was tasked
            // to refresh — never the model's spelling, so the MERGE NaturalKey
            // resolves the existing row.
            personDisplayName,
            GetStringProperty(person, "email"),
            GetStringProperty(person, "phone"),
            GetStringProperty(person, "linkedinUrl"),
            GetStringProperty(person, "notes"),
            confidence));
    }

    private static void AddCurrentAffiliation(
        JsonElement root,
        PersonBriefExtractionContext ctx,
        IntelConfidence confidence,
        List<IntelPersonAffiliationDraft> affiliations)
    {
        if (!TryGetObject(root, "currentAffiliation", out var current))
        {
            return;
        }

        // "confirmed" defaults to true. If the model emits confirmed=false,
        // we treat it as "person no longer here" and don't write a new
        // current affiliation. The retire-superseded pass on the existing
        // current row handles flipping it.
        if (current.TryGetProperty("confirmed", out var confirmedProp)
            && confirmedProp.ValueKind == JsonValueKind.False)
        {
            return;
        }

        affiliations.Add(new IntelPersonAffiliationDraft(
            ctx.PersonDisplayName,
            ctx.CurrentEmployerCanonicalOrgId,
            GetStringProperty(current, "title"),
            GetStringProperty(current, "department"),
            IsCurrent: true,
            GetStringProperty(current, "startDateApprox"),
            EndDateApprox: null,
            Notes: null,
            confidence));
    }

    private static void AddFormerAffiliations(
        JsonElement root,
        string personDisplayName,
        IntelConfidence confidence,
        List<IntelPersonAffiliationDraft> affiliations)
    {
        // Former affiliations require a canonical org resolution that the
        // extractor does not do — the persistence layer does it via the
        // CanonicalOrgResolver. For lean R91c we store ONLY former
        // affiliations whose orgName already maps to a known canonical id;
        // we tag them with CanonicalOrgId = -1 to mean "needs resolution"
        // and the chokepoint resolves them. The simplest path is to skip
        // former affiliations here entirely and let separate org-side
        // refresh capture team moves. (Re-enable when CanonicalOrgResolver
        // is wired into the chokepoint.)
        _ = root;
        _ = personDisplayName;
        _ = confidence;
        _ = affiliations;
    }

    private static void AddSignals(
        JsonElement root,
        PersonBriefExtractionContext ctx,
        IntelConfidence confidence,
        List<IntelSignalDraft> signals)
    {
        if (!TryGetArray(root, "recentSignals", out var arr))
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

            // Prefix the subject with the person's name so the org-side
            // brief surfaces these signals as "about this person" rather
            // than "about the org generally". The Person Brief's data path
            // matches on LIKE %DisplayName% which catches both.
            var subjectPrefixed = subject!.Contains(ctx.PersonDisplayName, StringComparison.OrdinalIgnoreCase)
                ? subject
                : ctx.PersonDisplayName + ": " + subject;

            signals.Add(new IntelSignalDraft(
                ctx.CurrentEmployerCanonicalOrgId,
                NonBlank(GetStringProperty(item, "type"), "Other"),
                subjectPrefixed,
                GetStringProperty(item, "detail"),
                GetStringProperty(item, "occurredAt"),
                GetStringProperty(item, "sourceUrl"),
                confidence));
        }
    }

    private static void AddActions(
        JsonElement root,
        PersonBriefExtractionContext ctx,
        IntelConfidence confidence,
        List<IntelActionDraft> actions)
    {
        if (!TryGetArray(root, "korActions", out var arr))
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

            actions.Add(new IntelActionDraft(
                ctx.CurrentEmployerCanonicalOrgId,
                NonBlank(GetStringProperty(item, "type"), "Other"),
                recommendation,
                ctx.PersonDisplayName, // TargetPersonName always = the refreshed person
                GetStringProperty(item, "timingNotes"),
                confidence));
        }
    }

    private static bool TryGetObject(JsonElement root, string propertyName, out JsonElement obj)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(propertyName, out obj)
            && obj.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        obj = default;
        return false;
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

    private static void TraceSkip(PersonBriefExtractionContext ctx, string reason)
    {
        System.Diagnostics.Trace.TraceWarning(
            "Person brief extractor skipped person {0} (id {1}): {2}.",
            ctx.PersonDisplayName,
            ctx.IntelPersonId,
            reason);
    }
}

public sealed record PersonBriefExtractionContext(
    long IntelPersonId,
    string PersonDisplayName,
    long CurrentEmployerCanonicalOrgId,
    long SourceEnrichmentId,
    string ProviderName,
    JsonDocument ResultJson,
    DateTimeOffset RefreshedAtUtc);
