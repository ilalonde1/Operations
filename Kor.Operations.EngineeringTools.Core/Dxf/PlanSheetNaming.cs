using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>What a plan's filename says about where it belongs in the building.</summary>
public sealed record PlanSheetInfo(
    string FileName,
    string? BuildingTag,
    IReadOnlyList<int> Levels,
    bool IsRoof,
    string Label)
{
    /// <summary>Parkade levels the sheet covers, numbered in their own sequence (P1, P2 …).</summary>
    public IReadOnlyList<int> ParkadeLevels { get; init; } = Array.Empty<int>();

    /// <summary>Every building the sheet serves; a plan titled "BLDG A&amp;B" serves both.</summary>
    public IReadOnlyList<string> BuildingTags { get; init; } = Array.Empty<string>();

    /// <summary>A foundation plan: the lowest slab, named by its job rather than by a level.</summary>
    public bool IsFoundation { get; init; }

    public bool HasPlacement => Levels.Count > 0 || ParkadeLevels.Count > 0 || IsRoof || IsFoundation;
}

/// <summary>
/// Reads the level coverage out of drafting's sheet names.
///
/// A single plan usually serves a run of identical floors — "LEVEL 29 PLAN (L29-35)"
/// is drawn once and applies to seven storeys — so the range in the title is what
/// lets one drawing populate the whole tower.
/// </summary>
public static partial class PlanSheetNaming
{
    /// <summary>One sheet can serve several buildings — "BLDG A&amp;B", "BLDG A &amp; B".</summary>
    [GeneratedRegex(@"BLDG\s*([A-Z](?:\s*&\s*[A-Z])*)", RegexOptions.IgnoreCase)]
    private static partial Regex BuildingRegex();

    /// <summary>Storey-prefixed sheet names such as "A-LEVEL 28" name their building directly.</summary>
    [GeneratedRegex(@"(?<![A-Z0-9])([A-Z])-LEVEL\s*\d", RegexOptions.IgnoreCase)]
    private static partial Regex PrefixBuildingRegex();

    [GeneratedRegex(@"L(?:EVEL)?\s*(\d+)\s*(?:-|TO|THRU|THROUGH)\s*L?(?:EVEL)?\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex RangeRegex();

    [GeneratedRegex(@"L(?:EVEL)?\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SingleLevelRegex();

    /// <summary>Parkade levels are numbered separately: "LEVEL P2" is not level 2.</summary>
    [GeneratedRegex(@"L(?:EVEL)?\s*P\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ParkadeLevelRegex();

    /// <summary>
    /// A parkade storey as a model names one. Drafting titles a sheet "LEVEL P2", but a model may
    /// call the storey just "P2" — 31138 does, and because the sheet pattern demanded the word
    /// LEVEL, every below-grade sheet in that project matched no storey and was dropped. The whole
    /// parkade went missing for want of a prefix.
    /// </summary>
    [GeneratedRegex(@"^\s*(?:L(?:EVEL)?\s*)?P\s*(\d+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ParkadeStoryRegex();

    public static PlanSheetInfo Parse(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        var buildings = new List<string>();
        if (BuildingRegex().Match(name) is { Success: true } b)
        {
            foreach (char c in b.Groups[1].Value.ToUpperInvariant())
                if (char.IsLetter(c)) buildings.Add(c.ToString());
        }
        else if (PrefixBuildingRegex().Match(name) is { Success: true } p)
        {
            buildings.Add(p.Groups[1].Value.ToUpperInvariant());
        }

        string? building = buildings.Count > 0 ? buildings[0] : null;

        bool isRoof = name.Contains("ROOF", StringComparison.OrdinalIgnoreCase);

        var parkade = ParkadeLevelRegex().Matches(name)
            .Select(m => int.Parse(m.Groups[1].Value))
            .Distinct()
            .OrderBy(v => v)
            .ToList();

        var levels = new List<int>();

        foreach (Match m in RangeRegex().Matches(name))
        {
            int from = int.Parse(m.Groups[1].Value);
            int to = int.Parse(m.Groups[2].Value);
            if (to < from) (from, to) = (to, from);

            // A "range" spanning the whole building is a sheet-number artefact, not a level range.
            if (to - from > 60) continue;
            for (int lvl = from; lvl <= to; lvl++) levels.Add(lvl);
        }

        if (levels.Count == 0)
        {
            // Sheet identifiers such as "S2-32-1_2" precede the title; strip them so
            // their digits are not mistaken for level numbers.
            string title = StripSheetNumber(name);
            foreach (Match m in SingleLevelRegex().Matches(title))
                levels.Add(int.Parse(m.Groups[1].Value));
        }

        return new PlanSheetInfo(
            Path.GetFileName(fileName),
            building,
            levels.Distinct().OrderBy(v => v).ToList(),
            isRoof,
            CleanLabel(name))
        {
            IsFoundation = name.Contains("FOUNDATION", StringComparison.OrdinalIgnoreCase),
            ParkadeLevels = parkade,
            BuildingTags = buildings,
        };
    }

    private static string StripSheetNumber(string name)
    {
        int marker = name.IndexOf("LEVEL", StringComparison.OrdinalIgnoreCase);
        if (marker > 0) return name[marker..];

        marker = name.IndexOf("ROOF", StringComparison.OrdinalIgnoreCase);
        return marker > 0 ? name[marker..] : name;
    }

    private static string CleanLabel(string name)
    {
        string label = StripSheetNumber(name)
            .Replace("CONCRETE OUTLINE", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Copy 1", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim(' ', '-', '_');
        return string.IsNullOrWhiteSpace(label) ? name : label;
    }

    /// <summary>
    /// Picks the model storeys a plan applies to. Storey names are matched on their
    /// level number, and on the building letter when the sheet names one — so a
    /// "BLDG B" plan never lands on Tower A's storeys.
    /// </summary>
    /// <param name="storyNames">The model's storeys, highest first.</param>
    public static IReadOnlyList<string> MatchStories(PlanSheetInfo sheet, IEnumerable<string> storyNames)
    {
        var stories = storyNames.ToList();
        var matches = new List<string>();

        // A roof or foundation plan carries no level number, so it matched nothing and was dropped
        // — the roof and the lowest slab of a building, missing because of how the sheet is titled.
        // Both name their place in the building instead: the roof is the storey called one, or
        // failing that the topmost; the foundation is the lowest.
        if (sheet.Levels.Count == 0 && sheet.ParkadeLevels.Count == 0)
        {
            var eligible = stories
                .Where(s => sheet.BuildingTags.Count == 0 || sheet.BuildingTags.Any(tag => StoryBelongsToBuilding(s, tag)))
                .ToList();
            if (eligible.Count == 0) eligible = stories;

            if (sheet.IsRoof)
            {
                var named = eligible.Where(s => s.Contains("ROOF", StringComparison.OrdinalIgnoreCase)).ToList();
                return named.Count > 0 ? named : eligible.Take(1).ToList();
            }

            if (sheet.IsFoundation) return eligible.Count > 0 ? eligible.TakeLast(1).ToList() : matches;
        }

        foreach (string story in stories)
        {
            if (sheet.BuildingTags.Count > 0 &&
                !sheet.BuildingTags.Any(tag => StoryBelongsToBuilding(story, tag))) continue;

            var parkadeInStory = ParkadeStoryRegex().Match(story);
            if (parkadeInStory.Success)
            {
                if (sheet.ParkadeLevels.Contains(int.Parse(parkadeInStory.Groups[1].Value))) matches.Add(story);
                continue;
            }

            if (sheet.ParkadeLevels.Count > 0 && sheet.Levels.Count == 0) continue;

            var levelInStory = SingleLevelRegex().Match(story);
            if (!levelInStory.Success) continue;

            int storyLevel = int.Parse(levelInStory.Groups[1].Value);
            if (sheet.Levels.Contains(storyLevel)) matches.Add(story);
        }

        // A building tag only narrows the choice where the model actually separates buildings.
        // Lower storeys are often shared and unprefixed, so a sheet titled "BLDG A&B" covering
        // levels 15-26 must still land on plain "LEVEL 15" and up.
        if (matches.Count == 0 && sheet.BuildingTags.Count > 0)
        {
            foreach (string story in storyNames)
            {
                var level = SingleLevelRegex().Match(story);
                if (level.Success && sheet.Levels.Contains(int.Parse(level.Groups[1].Value)))
                    matches.Add(story);
            }

            var shared = matches
                .Where(m => m.TrimStart().StartsWith("LEVEL", StringComparison.OrdinalIgnoreCase))
                .ToList();
            return shared.Count > 0 ? shared : matches;
        }

        // An untagged sheet in a multi-building model belongs to the unprefixed storeys
        // if any exist — otherwise it would be copied onto every tower at that level.
        if (sheet.BuildingTags.Count == 0)
        {
            var unprefixed = matches
                .Where(m => m.TrimStart().StartsWith("LEVEL", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (unprefixed.Count > 0) return unprefixed;
        }

        return matches;
    }

    private static bool StoryBelongsToBuilding(string storyName, string buildingTag)
    {
        string trimmed = storyName.TrimStart();
        return trimmed.StartsWith(buildingTag + "-", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(buildingTag + " ", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains($"BLDG {buildingTag}", StringComparison.OrdinalIgnoreCase);
    }
}
