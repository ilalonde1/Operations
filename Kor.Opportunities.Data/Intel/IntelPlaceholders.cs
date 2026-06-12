#nullable enable
using System;

namespace Kor.Opportunities.Data.Intel;

/// <summary>
/// Front-door guard for drain-model placeholder text. Research models
/// sometimes emit literal "None" / "N/A" / "Unknown" where they have nothing
/// to say; persisting those creates junk intel rows that surface in dossiers
/// and Priority Actions (Ian, 2026-06-12: a CONTACT STRATEGY action whose
/// whole recommendation was "None"). Guarding at the persistence choke point
/// covers every extractor, present and future — never band-aid the display.
/// </summary>
public static class IntelPlaceholders
{
    private static readonly string[] Placeholders =
    {
        "none", "n/a", "na", "n.a.", "nil", "null", "unknown", "tbd",
        "tba", "not applicable", "not available", "-", "—", "–",
    };

    public static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim().TrimEnd('.');
        foreach (var placeholder in Placeholders)
        {
            if (trimmed.Equals(placeholder, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Normalizes placeholder text to null so optional fields stay genuinely optional.</summary>
    public static string? NullIfPlaceholder(string? value)
        => IsPlaceholder(value) ? null : value;
}
