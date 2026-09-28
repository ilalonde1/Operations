#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kor.Operations.StandardDetails;

/// <summary>
/// The one place that knows how to read a KOR.Drafter bridge reply, and the one place that knows
/// what a KOR-D number looks like.
///
/// WHY THIS EXISTS. MasterPublisher and SheetComposer each grew a private copy of this whole set —
/// the prefix pattern, TryReadViewPrefix, EnumerateResultItems, TryGetProperty/String/Int64/Double —
/// and the copies had already drifted: MasterPublisher read a "parameters" payload whether the
/// bridge sent an object or an array, SheetComposer gave up unless it was an array. So the same
/// reply could be a detail to one reader and not a detail to the other, and nothing would say so.
/// A third copy was about to be written for the detail intake; instead there is now one.
///
/// The union of both copies lives here, always taking the more tolerant behaviour, because the
/// bridge's payload shape is the bridge's business and every reader should survive all of it.
/// </summary>
internal static class BridgeJson
{
    /// <summary>
    /// A detail number and nothing else. The View Prefix parameter on a Revit view is what binds a
    /// drawing to the catalogue, so this pattern decides what counts as catalogued — in publishing,
    /// in composing, and in intake alike.
    /// </summary>
    internal static readonly Regex DetailNumberPattern =
        new(@"^KOR-D-\d{5}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static bool IsDetailNumber(string? value)
        => !string.IsNullOrWhiteSpace(value) && DetailNumberPattern.IsMatch(value.Trim());

    internal static string NormalizeDetailNumber(string? detailNumber)
        => (detailNumber ?? "").Trim().ToUpperInvariant();

    /// <summary>
    /// Reads the "View Prefix" parameter off one element of a getparams reply. Returns false when
    /// the element carries no such parameter at all — which is a legitimate non-detail (schedules
    /// and sheets have no View Prefix; plan and 3D views carry it empty), never an error.
    /// </summary>
    internal static bool TryReadViewPrefix(JsonElement item, out string prefix)
    {
        prefix = "";

        if (!TryGetProperty(item, "parameters", out var parameters))
        {
            return false;
        }

        if (parameters.ValueKind == JsonValueKind.Array)
        {
            foreach (var parameter in parameters.EnumerateArray())
            {
                if (!string.Equals(TryGetString(parameter, "name"), "View Prefix", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                prefix = ReadScalarOrDisplayValue(parameter).Trim();
                return true;
            }

            return false;
        }

        if (parameters.ValueKind == JsonValueKind.Object && TryGetProperty(parameters, "View Prefix", out var value))
        {
            prefix = ReadScalarOrDisplayValue(value).Trim();
            return true;
        }

        return false;
    }

    /// <summary>
    /// The bridge reports a parameter value as a bare string, or as an object carrying some mix of
    /// displayValue / display / value / stringValue. Both shapes are read here so no caller has to
    /// guess which one it got.
    /// </summary>
    internal static string ReadScalarOrDisplayValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? "";
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            return TryGetString(value, "displayValue")
                ?? TryGetString(value, "display")
                ?? TryGetString(value, "value")
                ?? TryGetString(value, "stringValue")
                ?? "";
        }

        return "";
    }

    /// <summary>
    /// Yields the items of a reply, whether the result is the array itself or an object holding it
    /// under one of the given property names. The bridge names that array differently per verb
    /// ("views", "sheets", "elements", "items", "results"), so callers pass the ones they expect.
    /// </summary>
    internal static IEnumerable<JsonElement> EnumerateResultItems(JsonElement result, params string[] arrayPropertyNames)
    {
        if (result.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in result.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        if (result.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var propertyName in arrayPropertyNames)
        {
            if (!TryGetProperty(result, propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in array.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }
    }

    internal static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    internal static string? TryGetString(JsonElement element, string name)
        => TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static bool TryGetInt64(JsonElement element, string name, out long value)
    {
        value = 0;
        if (!TryGetProperty(element, name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value))
        {
            return true;
        }

        return property.ValueKind == JsonValueKind.String
               && long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    internal static bool TryGetInt32(JsonElement element, string name, out int value)
    {
        value = 0;
        if (!TryGetProperty(element, name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value))
        {
            return true;
        }

        return property.ValueKind == JsonValueKind.String
               && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    internal static bool TryGetDouble(JsonElement element, string name, out double value)
    {
        value = 0;
        if (!TryGetProperty(element, name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out value))
        {
            return true;
        }

        return property.ValueKind == JsonValueKind.String
               && double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    internal static bool TryGetBool(JsonElement element, string name, out bool value)
    {
        value = false;
        if (!TryGetProperty(element, name, out var property))
        {
            return false;
        }

        switch (property.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                return true;
            case JsonValueKind.False:
                value = false;
                return true;
            case JsonValueKind.String:
                return bool.TryParse(property.GetString(), out value);
            default:
                return false;
        }
    }
}
