#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.ResearchEnvelope;

/// <summary>
/// Validates that a JSON document conforms to the canonical envelope
/// shape. Used by tools/BdResearchImport BEFORE dispatching to a
/// kind-specific handler. Returns a structured result so the caller
/// can log "rejected" vs "legacy fallback" instead of silently
/// producing 0 rows.
/// </summary>
public static class ResearchEnvelopeValidator
{
    // Bump when the envelope shape itself changes. Handlers should
    // pin themselves to supported versions and reject newer ones
    // they don't yet understand.
    public const string CurrentSchemaVersion = "1.0";

    /// <summary>
    /// Parse + validate. Returns IsValid=true with the parsed
    /// envelope when the document is a well-formed envelope.
    /// IsValid=false + Reason when malformed; caller may then
    /// attempt legacy parsing.
    /// </summary>
    public static EnvelopeValidationResult Validate(
        JsonDocument doc,
        string expectedKind)
    {
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return new EnvelopeValidationResult(false,
                "root is not a JSON object (likely legacy flat-array shape)", null);
        }

        if (!doc.RootElement.TryGetProperty("schemaVersion", out var v)
            || v.ValueKind != JsonValueKind.String)
        {
            return new EnvelopeValidationResult(false,
                "missing 'schemaVersion' string property", null);
        }
        var schemaVersion = v.GetString() ?? "";
        if (schemaVersion != CurrentSchemaVersion)
        {
            return new EnvelopeValidationResult(false,
                $"unsupported schemaVersion '{schemaVersion}' (importer pinned to '{CurrentSchemaVersion}')",
                null);
        }

        if (!doc.RootElement.TryGetProperty("kind", out var k)
            || k.ValueKind != JsonValueKind.String)
        {
            return new EnvelopeValidationResult(false,
                "missing 'kind' string property", null);
        }
        var kind = k.GetString() ?? "";
        if (!string.Equals(kind, expectedKind, StringComparison.Ordinal))
        {
            return new EnvelopeValidationResult(false,
                $"kind mismatch: payload is '{kind}', importer expected '{expectedKind}'", null);
        }

        DateTimeOffset generatedAt = DateTimeOffset.MinValue;
        if (doc.RootElement.TryGetProperty("generatedAtUtc", out var g)
            && g.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(g.GetString(), out var parsed))
        {
            generatedAt = parsed;
        }

        string? notes = null;
        if (doc.RootElement.TryGetProperty("notes", out var n)
            && n.ValueKind == JsonValueKind.String)
        {
            notes = n.GetString();
        }

        if (!doc.RootElement.TryGetProperty("items", out var items))
        {
            return new EnvelopeValidationResult(false,
                "missing 'items' property", null);
        }

        return new EnvelopeValidationResult(true, null,
            new CanonicalResearchEnvelope(schemaVersion, kind, generatedAt, notes, items.Clone()));
    }
}
