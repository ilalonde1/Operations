#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.ResearchEnvelope;

/// <summary>
/// Canonical envelope every KOR-* Sonnet session SHOULD emit. The
/// envelope is provider-agnostic  the <c>Kind</c> tag tells the
/// importer which handler decodes <c>Items</c>. Adding a new
/// research stream means picking a new Kind string, writing a
/// per-kind deserializer, and reusing this envelope + the validator
/// instead of inventing a new wire format.
///
/// Legacy un-enveloped JSON is still accepted by importer handlers
/// during the transition; new prompts MUST emit the envelope so
/// drift gets caught at ingest time instead of producing 0 rows
/// silently.
/// </summary>
public sealed record CanonicalResearchEnvelope(
    string SchemaVersion,
    string Kind,
    DateTimeOffset GeneratedAtUtc,
    string? Notes,
    JsonElement Items);

public sealed record EnvelopeValidationResult(
    bool IsValid,
    string? Reason,
    CanonicalResearchEnvelope? Envelope);
