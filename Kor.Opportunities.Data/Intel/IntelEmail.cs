#nullable enable
using System.Net.Mail;

namespace Kor.Opportunities.Data.Intel;

public static class IntelEmail
{
    public static string? Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var candidate = raw.Trim();
        var cutAt = FirstCutIndex(candidate);
        if (cutAt >= 0)
        {
            candidate = candidate.Substring(0, cutAt);
        }

        candidate = candidate.TrimEnd('.', ',', ';');
        if (candidate.Contains('*', StringComparison.Ordinal) || !candidate.Contains('@', StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var addr = new MailAddress(candidate);
            return addr.Address.Equals(candidate, StringComparison.OrdinalIgnoreCase) ? candidate : null;
        }
        catch
        {
            return null;
        }
    }

    private static int FirstCutIndex(string value)
    {
        var cutAt = -1;
        foreach (var delimiter in new[] { ' ', '\t', '\u2014', '(' })
        {
            var index = value.IndexOf(delimiter);
            if (index >= 0 && (cutAt < 0 || index < cutAt))
            {
                cutAt = index;
            }
        }

        return cutAt;
    }
}
