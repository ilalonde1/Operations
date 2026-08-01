#nullable enable
using System;
using System.Globalization;
using System.Text.Json;

namespace Kor.Opportunities.Data.BdReports;

/// <summary>Honing fields hydrated from a ProjectBriefHoning ResultJson payload.</summary>
public sealed record HoningResult(
    string? Verdict,
    string? KorAngle,
    string? Status,
    string? Description,
    double? OverallConfidence)
{
    public static readonly HoningResult Empty = new(null, null, null, null, null);
}

/// <summary>
/// Reads the honing fields out of MajorProjectEnrichment.ResultJson, handling
/// the contract shape AND the three legacy shapes per
/// tools/BdQueueDrainPrompts/HONING-OUTPUT-CONTRACT.md: for each field
/// independently, the honingPass object wins when it carries the field,
/// falling back to the item root otherwise (ProjectBriefHoningExtractor
/// semantics). Used instead of SQL JSON_VALUE for korAngle/status/description
/// because JSON_VALUE silently truncates/NULLs values beyond 4000 chars.
/// </summary>
public static class HoningResultParser
{
    public static HoningResult Parse(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            return HoningResult.Empty;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(resultJson);
        }
        catch (JsonException)
        {
            return HoningResult.Empty;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return HoningResult.Empty;
            }

            JsonElement? honingPass = null;
            if (root.TryGetProperty("honingPass", out var hp) && hp.ValueKind == JsonValueKind.Object)
            {
                honingPass = hp;
            }

            return new HoningResult(
                ReadString(honingPass, root, "verdict"),
                ReadString(honingPass, root, "korAngle"),
                ReadString(honingPass, root, "status"),
                ReadString(honingPass, root, "description"),
                ReadDouble(honingPass, root, "overallConfidence"));
        }
    }

    private static string? ReadString(JsonElement? honingPass, JsonElement root, string name)
    {
        return StringOf(honingPass, name) ?? StringOf(root, name);
    }

    private static double? ReadDouble(JsonElement? honingPass, JsonElement root, string name)
    {
        return DoubleOf(honingPass, name) ?? DoubleOf(root, name);
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

    private static double? DoubleOf(JsonElement? obj, string name)
    {
        if (obj is not { } o || !o.TryGetProperty(name, out var p))
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.Number => p.GetDouble(),
            JsonValueKind.String when double.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }
}
