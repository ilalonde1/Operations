#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.Shared;

// Shared PM email map. Names must match Deltek's PM column exactly (post-
// normalize). Verbatim from Send-Weekly-PM-Deadlines.ps1 §22-35 and
// File-ConcreteTestReports.ps1 §37-50 (which agree).
//
// Aliases handle nickname/last-name-first variants seen in CTR's mapping
// sheet ("Jim DesRoches" -> "James DesRoches"). They are applied AFTER
// NormalizePmName so the lookup is case- and whitespace-insensitive.
internal static class PmDirectory
{
    public static IReadOnlyDictionary<string, string> ByDisplayName { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Andrea Neuviale"] = "andrean@korstructural.com",
            ["Conor Murtagh"] = "cmurtagh@korstructural.com",
            ["Griffin Dow"] = "gdow@korstructural.com",
            ["James DesRoches"] = "jdesroches@korstructural.com",
            ["Jason Stuart"] = "jstuart@korstructural.com",
            ["Jeremy Atkinson"] = "jatkinson@korstructural.com",
            ["John Bryson"] = "jbryson@korstructural.com",
            ["John Markulin"] = "jmarkulin@korstructural.com",
            ["Katherine Reid"] = "kreid@korstructural.com",
            ["Kevin Wurmlinger"] = "kevinw@korstructural.com",
            ["Omar Alcazar Pastrana"] = "omara@korstructural.com",
            ["Rory Beirne"] = "rbeirne@korstructural.com",
        };

    public static IReadOnlyDictionary<string, string> Aliases { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Jim DesRoches"] = "James DesRoches",
        };

    public static string? TryGetEmail(string pmName)
    {
        var key = NormalizePmName(pmName);
        if (Aliases.TryGetValue(key, out var canonical)) key = canonical;
        return ByDisplayName.TryGetValue(key, out var email) ? email : null;
    }

    public static string NormalizePmName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        // Strip diacritics ('Andréa' -> 'Andrea')
        var d = raw.Trim().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(d.Length);
        foreach (var ch in d)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        var collapsed = sb.ToString().Normalize(System.Text.NormalizationForm.FormC);

        // "Last, First" -> "First Last" (CTR mapping sheet uses both forms)
        var commaIdx = collapsed.IndexOf(',');
        if (commaIdx > 0 && commaIdx < collapsed.Length - 1)
        {
            var last = collapsed[..commaIdx].Trim();
            var first = collapsed[(commaIdx + 1)..].Trim();
            if (!string.IsNullOrEmpty(last) && !string.IsNullOrEmpty(first))
                collapsed = $"{first} {last}";
        }

        return System.Text.RegularExpressions.Regex.Replace(collapsed, @"\s+", " ").Trim();
    }
}
