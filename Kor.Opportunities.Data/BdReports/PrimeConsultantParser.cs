#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.BdReports;

/// <summary>Fields hydrated from a PrimeConsultantResearch ResultJson payload.</summary>
public sealed record PrimeConsultantResult(
    string? FirmName,
    string? PrincipalInCharge,
    string? ContactEmail,
    bool KnownToKor,
    string? KorRelationshipNotes,
    string? KorAngle,
    double? Confidence)
{
    public static readonly PrimeConsultantResult Empty = new(null, null, null, false, null, null, null);
}

/// <summary>
/// Reads the prime-consultant fields out of a PrimeConsultantResearch
/// enrichment payload: { primeConsultant: { firmName, principalInCharge,
/// contactEmail, knownToKor, korRelationshipNotes, confidence }, korAngle }.
/// Empty strings are treated as missing; malformed JSON never throws.
/// </summary>
public static class PrimeConsultantParser
{
    public static PrimeConsultantResult Parse(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            return PrimeConsultantResult.Empty;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(resultJson);
        }
        catch (JsonException)
        {
            return PrimeConsultantResult.Empty;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return PrimeConsultantResult.Empty;
            }

            JsonElement? prime = null;
            if (root.TryGetProperty("primeConsultant", out var pc) && pc.ValueKind == JsonValueKind.Object)
            {
                prime = pc;
            }

            return new PrimeConsultantResult(
                StringOf(prime, "firmName"),
                StringOf(prime, "principalInCharge"),
                StringOf(prime, "contactEmail"),
                BoolOf(prime, "knownToKor"),
                StringOf(prime, "korRelationshipNotes"),
                StringOf(root, "korAngle"),
                DoubleOf(prime, "confidence"));
        }
    }

    private static string? StringOf(JsonElement? obj, string name)
    {
        if (obj is not { } o || !o.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var s = p.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static bool BoolOf(JsonElement? obj, string name)
        => obj is { } o && o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static double? DoubleOf(JsonElement? obj, string name)
    {
        if (obj is not { } o || !o.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var d = p.GetDouble();
        return d == 0 ? null : d; // the drain writes confidence 0 for "not assessed"
    }

    private static string? StringOf(JsonElement obj, string name) => StringOf((JsonElement?)obj, name);
}
