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

    /// <summary>
    /// Whether the title carries a sheet number, and so is a drawing the office issued rather than
    /// a view the drafter kept. See <see cref="DrawingVocabulary.SheetNumberPattern"/>.
    /// </summary>
    public bool IsIssuedSheet { get; init; }

    /// <summary>A foundation plan: the lowest slab, named by its job rather than by a level.</summary>
    public bool IsFoundation { get; init; }

    /// <summary>
    /// The roof over the lift overrun, which stands above the main roof. A model with two roof
    /// storeys gets two roof sheets, and without telling them apart both land on the same one.
    /// </summary>
    public bool IsElevatorRoof { get; init; }

    /// <summary>
    /// A mezzanine plan. A mezzanine reads as the level it sits above — "LEVEL 1 PLAN MEZZ" gives
    /// level 1 — so without this it is indistinguishable from the floor below it.
    /// </summary>
    public bool IsMezzanine { get; init; }

    /// <summary>
    /// The levels this sheet calls a mezzanine, where it does not call all of them that.
    ///
    /// IsMezzanine is one flag for a whole sheet, and a title can be both: 31104 has
    /// "LEVEL 1 MEZZ AND LEVEL2 CANOPY", a level-1 mezzanine and a level-2 canopy on one drawing.
    /// One flag cannot express it, so the sheet was mezzanine-only, level 1 and level 2 are not
    /// mezzanine storeys, and it matched nothing at all -- 19 walls and 30 columns read and placed
    /// nowhere. It will happen again on any sheet that spans a mezzanine and an ordinary level.
    ///
    /// Empty where every level on the sheet is the same kind, which is the ordinary case and
    /// behaves exactly as before.
    /// </summary>
    public IReadOnlyList<int> MezzanineLevels { get; init; } = Array.Empty<int>();

    /// <summary>Whether this sheet has anything to say about a storey of this kind.</summary>
    public bool Serves(bool storeyIsMezzanine)
        => storeyIsMezzanine
            ? IsMezzanine
            : !IsMezzanine || MezzanineLevels.Count < Levels.Count;

    /// <summary>Whether this sheet calls THIS level a mezzanine.</summary>
    public bool CallsLevelMezzanine(int level)
        => IsMezzanine && (MezzanineLevels.Count == 0 || MezzanineLevels.Contains(level));

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

    /// <summary>
    /// The words this office uses on its drawings. Set once per run from KorStandards; KOR's own
    /// vocabulary until something says otherwise, so a job that says nothing reads as it always did.
    ///
    /// Static because sheet naming is asked about from five places and threading a vocabulary
    /// through every one of them would be a larger change than the one this is worth. A run sets
    /// it before reading any sheet.
    /// </summary>
    public static DrawingVocabulary Vocabulary { get; set; } = DrawingVocabulary.Default;

    public static PlanSheetInfo Parse(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        var buildings = new List<string>();
        if (Vocabulary.Building.Match(name) is { Success: true } b)
        {
            foreach (char c in b.Groups[1].Value.ToUpperInvariant())
                if (char.IsLetter(c)) buildings.Add(c.ToString());
        }
        else if (Vocabulary.PrefixBuilding.Match(name) is { Success: true } p)
        {
            buildings.Add(p.Groups[1].Value.ToUpperInvariant());
        }

        string? building = buildings.Count > 0 ? buildings[0] : null;

        bool isRoof = Vocabulary.IsRoofName(name);

        var parkade = Vocabulary.ParkadeLevel.Matches(name)
            .Select(m => int.Parse(m.Groups[1].Value))
            .Distinct()
            .OrderBy(v => v)
            .ToList();

        var levels = new List<int>();

        foreach (Match m in Vocabulary.Range.Matches(name))
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

            // A listed title first — "LEVEL 8, 9" is two floors, and reading only the 8 loses a
            // whole storey silently.
            foreach (Match m in Vocabulary.LevelList.Matches(title))
            {
                levels.Add(int.Parse(m.Groups[1].Value));
                foreach (Match more in Regex.Matches(m.Groups[2].Value, @"\d+"))
                    levels.Add(int.Parse(more.Value));
            }

            if (levels.Count == 0)
                foreach (Match m in Vocabulary.SingleLevel.Matches(title))
                    levels.Add(int.Parse(m.Groups[1].Value));
        }

        return new PlanSheetInfo(
            Path.GetFileName(fileName),
            building,
            levels.Distinct().OrderBy(v => v).ToList(),
            isRoof,
            CleanLabel(name))
        {
            IsFoundation = Vocabulary.IsFoundationName(name),
            IsElevatorRoof = Vocabulary.IsElevatorRoofName(name),
            IsMezzanine = IsMezzanineName(name),
            MezzanineLevels = MezzanineLevelsIn(StripSheetNumber(name)),
            ParkadeLevels = parkade,
            BuildingTags = buildings,
            IsIssuedSheet = Vocabulary.IsIssuedSheetName(name),
        };
    }

    /// <summary>
    /// Whether a sheet title or a storey name is a mezzanine. Drafting writes MEZZ; a model may
    /// write Mezz or MEZZANINE, and 31138's own storey list uses "Mezz".
    /// </summary>
    /// <summary>
    /// Which levels a title calls a mezzanine, when it calls some of them that and not others.
    ///
    /// Split on the word AND, never on "&" -- "BLDG A&B" is a building tag and splitting there
    /// would take a plan for two towers apart. Returns empty unless the title genuinely mixes the
    /// two kinds, so a sheet that is wholly a mezzanine is untouched.
    /// </summary>
    private static IReadOnlyList<int> MezzanineLevelsIn(string title)
    {
        var parts = Regex.Split(title, @"\s+AND\s+", RegexOptions.IgnoreCase);
        if (parts.Length < 2) return Array.Empty<int>();

        var mezzanine = new List<int>();
        bool anyPlain = false;

        foreach (string part in parts)
        {
            var levels = Vocabulary.SingleLevel.Matches(part).Select(m => int.Parse(m.Groups[1].Value)).ToList();
            if (levels.Count == 0) continue;

            if (IsMezzanineName(part)) mezzanine.AddRange(levels);
            else anyPlain = true;
        }

        // Only interesting where the title really does mix them.
        return mezzanine.Count > 0 && anyPlain
            ? mezzanine.Distinct().OrderBy(v => v).ToList()
            : Array.Empty<int>();
    }

    private static bool IsMezzanineName(string text) => Vocabulary.IsMezzanineName(text);

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
            // A mezzanine is a storey in its own right, not the unprefixed form of the level below
            // it. 31168 has "LEVEL 1 MEZZ" above "A-LEVEL 1" and "B-LEVEL 1", and both the level 1
            // sheet and the level 1 mezzanine sheet read as level 1. The rule that an untagged
            // sheet belongs to the unprefixed storey then handed the mezzanine both sheets and left
            // the ground floor of both towers empty — 45 walls and 67 columns modelled a storey up.
            bool storeyIsMezzanine = IsMezzanineName(story);
            if (!sheet.Serves(storeyIsMezzanine)) continue;

            if (sheet.BuildingTags.Count > 0 &&
                !sheet.BuildingTags.Any(tag => StoryBelongsToBuilding(story, tag))) continue;

            var parkadeInStory = Vocabulary.ParkadeStory.Match(story);
            if (parkadeInStory.Success)
            {
                if (sheet.ParkadeLevels.Contains(int.Parse(parkadeInStory.Groups[1].Value))) matches.Add(story);
                continue;
            }

            if (sheet.ParkadeLevels.Count > 0 && sheet.Levels.Count == 0) continue;

            var levelInStory = Vocabulary.SingleLevel.Match(story);
            if (!levelInStory.Success) continue;

            int storyLevel = int.Parse(levelInStory.Groups[1].Value);
            if (!sheet.Levels.Contains(storyLevel)) continue;

            // A mezzanine storey takes only the levels this sheet calls a mezzanine, and an
            // ordinary storey takes only the ones it does not.
            if (storeyIsMezzanine != sheet.CallsLevelMezzanine(storyLevel)) continue;

            matches.Add(story);
        }

        // A model may name its mezzanine storey just "Mezz", with no level number — 31138 does.
        // A mezzanine sheet then matches nothing by number, and its geometry was landing on level 1
        // instead: 11 walls and 18 columns of a mezzanine part plan built into the floor below.
        if (matches.Count == 0 && sheet.IsMezzanine)
        {
            var unnumbered = stories
                .Where(s => IsMezzanineName(s) && !Vocabulary.SingleLevel.IsMatch(s))
                .Where(s => sheet.BuildingTags.Count == 0 || sheet.BuildingTags.Any(tag => StoryBelongsToBuilding(s, tag)))
                .ToList();
            if (unnumbered.Count > 0) return unnumbered;
        }

        // A building tag only narrows the choice where the model actually separates buildings.
        // Lower storeys are often shared and unprefixed, so a sheet titled "BLDG A&B" covering
        // levels 15-26 must still land on plain "LEVEL 15" and up.
        //
        // AND THE PARKADE IS THE MOST SHARED STOREY THERE IS. This is where a tagged sheet gets its
        // second chance, and it looked only at NUMBERED levels — so every per-building parkade
        // sheet in the set landed nowhere and was silently not placed:
        //
        //     S2.05.1_1_LEVEL P1 PLAN - CONCRETE OUTLINE - BLDG C     26 walls  66 columns
        //     S2.06.1_1_LEVEL P1 PLAN - CONCRETE OUTLINE - WEST       42 walls  67 columns
        //     S2.03/S2.04 the same at P2, S2.01/S2.02 the same at P3
        //
        // "levels P1 match no storey in the model — not placed", seven times, in a report nobody
        // read that far down. Only the undivided "LEVEL P1 PLAN - CONCRETE OUTLINE" was placed, so
        // the parkade arrived as one site-wide slab and 108 columns with nothing saying whose they
        // were — and a model of building C alone came out standing on the whole site's parkade.
        if (matches.Count == 0 && sheet.BuildingTags.Count > 0)
        {
            foreach (string story in storyNames)
            {
                if (IsMezzanineName(story) != sheet.IsMezzanine) continue;

                var parkade = Vocabulary.ParkadeStory.Match(story);
                if (parkade.Success)
                {
                    if (sheet.ParkadeLevels.Contains(int.Parse(parkade.Groups[1].Value))) matches.Add(story);
                    continue;
                }

                if (sheet.ParkadeLevels.Count > 0 && sheet.Levels.Count == 0) continue;

                var level = Vocabulary.SingleLevel.Match(story);
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

        // A level with no number in its name.
        //
        // Everything above matches on the level NUMBER carried in the sheet title, which is a fact
        // about how one office names levels, not about buildings. Run against Autodesk's own
        // structural sample, four of its nine plans landed nowhere -- Parking, Top of Footing, R2,
        // Parapet 2 -- because none of those names contains a number this could read. The building
        // came through with its floors missing and nothing said which.
        //
        // The level's own NAME is the better key and it is right there: drafting titles a plan
        // after the level it cuts, and the bridge writes the level name into the filename when it
        // exports. So once matching by number has failed outright, the sheet's title is compared
        // to the storey names themselves, punctuation and case set aside.
        //
        // Numbers still win: this runs only where they found nothing, so no job that names its
        // levels numerically is touched by it.
        if (matches.Count == 0)
        {
            var byName = stories
                .Where(story => IsMezzanineName(story) == sheet.IsMezzanine)
                .Where(story => sheet.BuildingTags.Count == 0
                             || sheet.BuildingTags.Any(tag => StoryBelongsToBuilding(story, tag)))
                .Where(story => SameName(story, sheet.Label) || SameName(story, sheet.FileName))
                .ToList();

            if (byName.Count > 0) return byName;
        }

        // A numbered level that matches no storey falls back to what the sheet SAYS it is.
        //
        // Drafting numbers the top of a building; the model names it. 31065's drawings carry
        // "ROOF LEVEL (L20)" and "ELEVATOR ROOF (L21)" while the model's top three storeys are
        // L19, Roof and ELV — so both sheets matched nothing and were dropped whole, taking the
        // roof of the building with them. The roof rule above could have placed them, but it only
        // runs for a sheet with NO level number at all, and these have one.
        //
        // Numbers first, always: this runs only once matching by number has failed outright, so a
        // sheet whose level exists in the model is untouched by it.
        if (matches.Count == 0 && (sheet.IsRoof || sheet.IsFoundation))
        {
            var eligible = stories
                .Where(s => sheet.BuildingTags.Count == 0 || sheet.BuildingTags.Any(tag => StoryBelongsToBuilding(s, tag)))
                .ToList();
            if (eligible.Count == 0) eligible = stories;
            if (eligible.Count == 0) return matches;

            if (sheet.IsFoundation) return eligible.TakeLast(1).ToList();

            // The named roofs, in the order the model lists them — top down — so an elevator roof
            // above a main roof takes the higher one and they do not both land on the same storey.
            var roofs = eligible.Where(s => s.Contains("ROOF", StringComparison.OrdinalIgnoreCase)).ToList();
            if (roofs.Count == 0) return eligible.Take(1).ToList();
            if (roofs.Count == 1) return roofs;

            // Two roof storeys and two roof sheets: the higher-numbered sheet is the higher one.
            int highest = sheet.Levels.Count > 0 ? sheet.Levels.Max() : 0;
            bool topmost = sheet.IsElevatorRoof
                || (highest > 0 && stories.Count > 0 && highest >= LevelCeiling(stories));
            return topmost ? roofs.Take(1).ToList() : roofs.Skip(1).Take(1).ToList();
        }

        return matches;
    }

    /// <summary>The largest level number any storey in the model names.</summary>
    private static int LevelCeiling(IEnumerable<string> stories)
    {
        int top = 0;
        foreach (string s in stories)
        {
            var m = Vocabulary.SingleLevel.Match(s);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int n) && n > top) top = n;
        }
        return top;
    }

    /// <summary>
    /// Whether a sheet title names this storey, with the decoration drafting adds set aside:
    /// case, underscores, hyphens and runs of spaces. "Top of Footing" matches "TOP_OF_FOOTING",
    /// and a title that merely CONTAINS the storey name matches it too, because a sheet is
    /// commonly called "Parking Plan" for a level called "Parking".
    ///
    /// Whole words only. Without that, a storey called "L1" would claim every sheet with an L1 in
    /// it — "L1_43_High" among them — and a name match that is looser than the number match it
    /// stands in for would be worse than no match at all.
    /// </summary>
    private static bool SameName(string storyName, string sheetTitle)
    {
        string story = Normalise(storyName);
        string sheet = Normalise(sheetTitle);
        if (story.Length == 0 || sheet.Length == 0) return false;
        if (story == sheet) return true;

        int at = sheet.IndexOf(story, StringComparison.Ordinal);
        while (at >= 0)
        {
            bool startsClean = at == 0 || sheet[at - 1] == ' ';
            bool endsClean = at + story.Length == sheet.Length || sheet[at + story.Length] == ' ';
            if (startsClean && endsClean) return true;
            at = sheet.IndexOf(story, at + 1, StringComparison.Ordinal);
        }

        return false;
    }

    private static string Normalise(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
            else if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
        }
        return sb.ToString().Trim();
    }

    private static bool StoryBelongsToBuilding(string storyName, string buildingTag)
    {
        string trimmed = storyName.TrimStart();
        return trimmed.StartsWith(buildingTag + "-", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(buildingTag + " ", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains($"BLDG {buildingTag}", StringComparison.OrdinalIgnoreCase);
    }
}
