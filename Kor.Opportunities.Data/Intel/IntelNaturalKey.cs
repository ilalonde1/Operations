#nullable enable
using Kor.Opportunities.Data.Awards;

namespace Kor.Opportunities.Data.Intel;

public static class IntelNaturalKey
{
    public static string Compute(params string?[] parts)
    {
        var joined = string.Join("|", parts.Select(p => (p ?? string.Empty).Trim().ToLowerInvariant()));
        using var sha = System.Security.Cryptography.SHA1.Create();
        return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(joined)));
    }

    public static string Normalize(string? s) =>
        string.IsNullOrWhiteSpace(s) ? string.Empty : CanonicalOrgResolver.NormalizeName(s);
}
