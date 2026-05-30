#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace Kor.Operations.App.Opportunities;

/// <summary>
/// Classifies a dossier-field value as Plain / SingleUrl / MultiUrl so the
/// dossier renderer can show three layered TextBlocks (only the matching one
/// stays visible). Treats whitespace-free <c>http(s)://...</c> strings as
/// SingleUrl, multi-URL whitespace-separated values as MultiUrl, everything
/// else as Plain. ConverterParameter selects which mode this binding matches.
/// </summary>
public sealed class DossierValueKindConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var requested = (parameter as string ?? string.Empty).Trim();
        var kind = Classify(value as string ?? string.Empty);
        return string.Equals(requested, kind, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static string Classify(string raw)
    {
        var trimmed = raw.Trim();
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

        if (urlCount >= 1)
        {
            return "MultiUrl";
        }

        return "Plain";
    }

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
