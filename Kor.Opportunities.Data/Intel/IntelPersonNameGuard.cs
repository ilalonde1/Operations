#nullable enable
using System.Text.RegularExpressions;

namespace Kor.Opportunities.Data.Intel;

public static class IntelPersonNameGuard
{
    private static readonly Regex WholeStringBracketed = new(@"^\[.+\]$", RegexOptions.Compiled);
    private static readonly Regex InternalWhitespace = new(@"\s", RegexOptions.Compiled);

    public static bool IsValid(string? displayName) =>
        IsValid(displayName, out _);

    public static bool IsValid(string? displayName, out string? rejectReason)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            rejectReason = "null-or-whitespace";
            return false;
        }

        string trimmed = displayName.Trim();
        if (trimmed.Length < 4)
        {
            rejectReason = "too-short";
            return false;
        }

        if (trimmed.Contains('<') || trimmed.Contains('>'))
        {
            rejectReason = "angle-bracket-placeholder";
            return false;
        }

        if (WholeStringBracketed.IsMatch(trimmed))
        {
            rejectReason = "square-bracket-placeholder";
            return false;
        }

        string lower = trimmed.ToLowerInvariant();
        if (lower.StartsWith("unknown", StringComparison.Ordinal))
        {
            rejectReason = "unknown-placeholder";
            return false;
        }

        if (lower is "tbd" or "tba" or "n/a" or "na" or "redacted")
        {
            rejectReason = "placeholder-keyword";
            return false;
        }

        if (lower.Contains(" tbd", StringComparison.Ordinal)
            || lower.Contains("tbd ", StringComparison.Ordinal)
            || lower.Contains(" tba ", StringComparison.Ordinal))
        {
            rejectReason = "placeholder-keyword";
            return false;
        }

        if (lower.Contains("various", StringComparison.Ordinal)
            || lower.Contains("not yet", StringComparison.Ordinal)
            || lower.Contains("redacted", StringComparison.Ordinal)
            || lower.Contains("public servant", StringComparison.Ordinal)
            || lower.Contains("official", StringComparison.Ordinal)
            || lower.Contains("see notes", StringComparison.Ordinal))
        {
            rejectReason = "generic-role-label";
            return false;
        }

        if (!InternalWhitespace.IsMatch(trimmed))
        {
            rejectReason = "single-word";
            return false;
        }

        rejectReason = null;
        return true;
    }
}
