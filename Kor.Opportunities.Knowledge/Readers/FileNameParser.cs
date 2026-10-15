using System.Globalization;
using System.Text.RegularExpressions;

namespace Kor.Opportunities.Knowledge.Readers;

public sealed record FileNameMetadata(string Title, string? Author, short? Year);

/// <summary>
/// Split the stem on " -- ": title, then author (remove only a trailing bracket block),
/// then the first standalone 19xx/20xx year in subsequent parts. In titles, "_" becomes
/// ":" only after a word character and before a space. In authors it becomes "." only
/// after a single capital initial, including adjacent initials (A_G_ / William D_).
/// No spelling, punctuation or other filename corrections are inferred.
/// </summary>
public static partial class FileNameParser
{
    public static FileNameMetadata Parse(string path) => ParseStem(Path.GetFileNameWithoutExtension(path));

    public static FileNameMetadata ParseStem(string stem)
    {
        var parts = stem.Split(" -- ", StringSplitOptions.None);
        var title = TitleColon().Replace(parts[0].Trim(), ":");
        var author = parts.Length > 1 ? TrailingSortedName().Replace(parts[1].Trim(), "").Trim() : null;
        if (author is not null) author = Initial().Replace(author, "$1.");
        if (string.IsNullOrWhiteSpace(author)) author = null;
        var match = YearToken().Match(string.Join(" -- ", parts.Skip(2)));
        short? year = match.Success ? short.Parse(match.Value, CultureInfo.InvariantCulture) : null;
        return new FileNameMetadata(title, author, year);
    }

    [GeneratedRegex(@"(?<=\w)_(?= )")]
    private static partial Regex TitleColon();
    [GeneratedRegex(@"\s+\[[^\[\]]*\]\s*$")]
    private static partial Regex TrailingSortedName();
    [GeneratedRegex(@"(?<![\p{L}\p{N}])([A-Z])_(?=[A-Z]_|[\s.,;]|$)")]
    private static partial Regex Initial();
    [GeneratedRegex(@"\b(?:19|20)\d{2}\b")]
    private static partial Regex YearToken();
}
