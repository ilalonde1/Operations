#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Kor.Operations.App.Opportunities;

/// <summary>
/// Classifies a dossier field into one of several render modes so the
/// dossier renderer can show layered presenters and only the matching one
/// stays visible.
///
/// Modes (label-driven first, then value-driven):
///   ChipList     - Label = "Sectors" or other comma-list field
///   Bullet       - Label looks like a person/partner row (People, Key People, Recurring Structural Partners)
///   PriorityChip - Label = "Kor Priority", "Status", "Structural Partner Status"
///   SingleUrl    - Value is a single http(s) URL with no whitespace
///   MultiUrl     - Value contains 2+ URLs separated by whitespace/comma/semicolon
///   Plain        - Everything else (fallback)
///
/// Binding can be either the full DossierField (so we see Label + Value) or
/// just the Value string for back-compat. ConverterParameter selects which
/// mode this binding matches.
/// </summary>
public sealed class DossierValueKindConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var requested = (parameter as string ?? string.Empty).Trim();

        string label = string.Empty;
        string val = string.Empty;
        if (value is DossierField field)
        {
            label = field.Label ?? string.Empty;
            val = field.Value ?? string.Empty;
        }
        else if (value is string s)
        {
            val = s;
        }

        var kind = Classify(label, val);
        return string.Equals(requested, kind, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static string Classify(string label, string rawValue)
    {
        var trimmed = (rawValue ?? string.Empty).Trim();
        var labelLower = (label ?? string.Empty).ToLowerInvariant();

        // Label-driven first: Sectors -> chip list, People rows -> bullet,
        // Priority/Status -> colored chip.
        if (labelLower == "sectors" && trimmed.Contains(','))
        {
            return "ChipList";
        }

        if (LooksLikePersonRow(labelLower))
        {
            return "Bullet";
        }

        if (LooksLikePriorityField(labelLower) && trimmed.Length > 0 && trimmed.Length <= 64)
        {
            return "PriorityChip";
        }

        // Value-driven: URL classification.
        if (string.IsNullOrEmpty(trimmed))
        {
            return "Plain";
        }

        var urlCount = CountUrls(trimmed);
        if (urlCount == 0)
        {
            return "Plain";
        }

        var noWhitespace = !trimmed.Any(char.IsWhiteSpace);
        if (urlCount == 1 && noWhitespace)
        {
            return "SingleUrl";
        }

        return "MultiUrl";
    }

    private static bool LooksLikePersonRow(string labelLower)
    {
        // Catches "Key People 1", "People 2", "Recurring Structural Partners 1",
        // "DecisionMakers 1", etc. Anything with "people" or "partner" + a number suffix.
        if (labelLower.StartsWith("key people"))
        {
            return true;
        }

        if (labelLower.StartsWith("people "))
        {
            return true;
        }

        if (labelLower.Contains("partners") && labelLower.Any(char.IsDigit))
        {
            return true;
        }

        if (labelLower.StartsWith("decisionmakers"))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikePriorityField(string labelLower)
        => labelLower == "kor priority"
            || labelLower == "kor relevance"
            || labelLower.EndsWith(" status")
            || labelLower == "status"
            || labelLower.EndsWith(" priority")
            || labelLower == "priority";

    private static int CountUrls(string s)
    {
        var count = 0;
        var idx = 0;
        while (idx < s.Length)
        {
            var hit = s.IndexOf("http", idx, StringComparison.OrdinalIgnoreCase);
            if (hit < 0)
            {
                break;
            }

            count++;
            idx = hit + 4;
        }

        return count;
    }
}

/// <summary>
/// Splits a multi-URL value into individual URL strings so each can be a
/// clickable Hyperlink. Splits on whitespace; keeps tokens that start with
/// <c>http</c>.
/// </summary>
public sealed class UrlSplitConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s))
        {
            return Array.Empty<string>();
        }

        return s
            .Split(new[] { ' ', '\t', '\n', '\r', ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// Splits a comma-separated value into individual trimmed strings for chip
/// rendering. Treats <c>,</c>, <c>;</c>, and <c>/</c> as separators since
/// research dossiers vary by provider.
/// </summary>
public sealed class CommaSplitConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s))
        {
            return Array.Empty<string>();
        }

        return s
            .Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToArray();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// Maps a priority/status value (e.g. "high", "rotating", "low", "active",
/// "lost", "won", "primary", "incumbent") to a brush so chips read semantically:
///   high / primary / incumbent / won / active   -> Risk.HighConfidence (green)
///   medium / rotating / pursuing                -> CorporateBlue
///   warning / at risk                           -> Risk.AtRisk (amber)
///   low / secondary / lost / blocked            -> Risk.Critical (red) or muted
///   default                                     -> Text.Secondary (slate)
/// Looks up the brush from <c>Application.Current.Resources</c> so KorTheme
/// owns the actual colors.
/// </summary>
public sealed class PriorityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var v = (value as string ?? string.Empty).Trim().ToLowerInvariant();
        var key = v switch
        {
            "high" or "primary" or "incumbent" or "won" or "active" or "preferred"
                => "Risk.HighConfidence.Foreground",
            "medium" or "rotating" or "pursuing" or "shortlist" or "shortlisted"
                => "CorporateBlue",
            "warning" or "at risk" or "atrisk" or "watch"
                => "Risk.AtRisk.Foreground",
            "low" or "secondary" or "lost" or "blocked" or "dormant"
                => "Risk.Critical.Foreground",
            _ => "Text.Secondary",
        };

        return Application.Current?.Resources[key] as Brush
            ?? new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
