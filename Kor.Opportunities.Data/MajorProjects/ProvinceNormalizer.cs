#nullable enable

namespace Kor.Opportunities.Data.MajorProjects;

public static class ProvinceNormalizer
{
    public static readonly IReadOnlyList<string> CanonicalProvinceCodes =
    [
        "AB",
        "BC",
        "MB",
        "NB",
        "NL",
        "NS",
        "NT",
        "NU",
        "ON",
        "PE",
        "QC",
        "SK",
        "YT",
    ];

    private static readonly HashSet<string> CanonicalCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AB",
        "BC",
        "MB",
        "NB",
        "NL",
        "NS",
        "NT",
        "NU",
        "ON",
        "PE",
        "QC",
        "SK",
        "YT",
        "WA",
        "OR",
        "CA",
        "NV",
        "AZ",
        "TX",
        "NY",
        "FL",
        "IL",
        "MA",
        "CO",
        "GA",
    };

    private static readonly Dictionary<string, string> TokenRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["britishcolumbia"] = "BC",
        ["vancouver"] = "BC",
        ["lowermainland"] = "BC",
        ["okanagan"] = "BC",
        ["kootenay"] = "BC",
        ["vancouverisland"] = "BC",
        ["alberta"] = "AB",
        ["calgary"] = "AB",
        ["edmonton"] = "AB",
        ["ontario"] = "ON",
        ["toronto"] = "ON",
        ["ottawa"] = "ON",
        ["quebec"] = "QC",
        ["montreal"] = "QC",
        ["manitoba"] = "MB",
        ["winnipeg"] = "MB",
        ["saskatchewan"] = "SK",
        ["regina"] = "SK",
        ["saskatoon"] = "SK",
        ["novascotia"] = "NS",
        ["halifax"] = "NS",
        ["newbrunswick"] = "NB",
        ["fredericton"] = "NB",
        ["newfoundland"] = "NL",
        ["princeedwardisland"] = "PE",
        ["pei"] = "PE",
        ["washington"] = "WA",
        ["seattle"] = "WA",
        ["pacificnorthwest"] = "WA",
        ["pacnw"] = "WA",
        ["oregon"] = "OR",
        ["portland"] = "OR",
        ["california"] = "CA",
        ["losangeles"] = "CA",
        ["sandiego"] = "CA",
        ["sanfrancisco"] = "CA",
    };

    public static string Normalize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        string trimmed = value.Trim();
        string upper = trimmed.ToUpperInvariant();
        if (upper.Length == 2 && upper.All(char.IsLetter) && CanonicalCodes.Contains(upper))
        {
            return upper;
        }

        string token = NormalizeToken(trimmed);
        foreach (KeyValuePair<string, string> route in TokenRoutes)
        {
            if (token.Contains(route.Key, StringComparison.Ordinal))
            {
                return route.Value;
            }
        }

        return fallback;
    }

    private static string NormalizeToken(string value)
    {
        char[] chars = value.ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();
        return new string(chars);
    }
}
