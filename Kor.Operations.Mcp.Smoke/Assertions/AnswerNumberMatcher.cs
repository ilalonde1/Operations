#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;

namespace Kor.Operations.Mcp.Smoke.Assertions;

internal static partial class AnswerNumberMatcher
{
    public static IReadOnlyList<decimal> ExtractNumbers(string answer)
    {
        var values = new List<decimal>();
        foreach (Match match in NumberRegex().Matches(answer ?? ""))
        {
            var raw = match.Groups["num"].Value.Replace(",", "", StringComparison.Ordinal);
            if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                continue;

            var suffix = match.Groups["suffix"].Value.ToUpperInvariant();
            value = suffix switch
            {
                "K" => value * 1_000m,
                "M" => value * 1_000_000m,
                "B" => value * 1_000_000_000m,
                _ => value,
            };
            if (match.Groups["percent"].Success)
                value /= 100m;
            values.Add(value);
        }
        return values;
    }

    public static bool ContainsMatch(string answer, decimal expected, out decimal matched)
    {
        foreach (var value in ExtractNumbers(answer))
        {
            if (Tolerance.Matches(expected, value))
            {
                matched = value;
                return true;
            }
        }
        matched = 0m;
        return false;
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9])\$?\s*(?<num>-?\d{1,3}(?:,\d{3})*(?:\.\d+)?|-?\d+(?:\.\d+)?)(?<suffix>[KkMmBb])?(?<percent>\s*%)?", RegexOptions.Compiled)]
    private static partial Regex NumberRegex();
}
