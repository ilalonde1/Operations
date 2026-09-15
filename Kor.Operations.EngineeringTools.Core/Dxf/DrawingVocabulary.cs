using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// The WORDS one office uses on its drawings, as data rather than as compiled patterns.
///
/// Everything about reading a structural plan was already a rule — which layers carry walls, how
/// thin is too thin, how wide an interruption to bridge. What a drawing is CALLED was not. Seven
/// regexes in PlanSheetNaming encoded one firm's titles, and the same words were repeated as
/// string literals in four other files: "LEVEL", "BLDG", "MEZZ", "ROOF", "FOUNDATION".
///
/// That is the part a firm actually differs on. A practice that writes "FLOOR 3" or
/// "BUILDING C" or numbers its below-grade storeys "B1" gets sheets matched to no storey, and a
/// whole building disappears without a word. PlanSheetNaming's own comment records it happening:
/// "the whole parkade went missing for want of a prefix."
///
/// So the GRAMMAR stays in code — a range is still two numbers with a separator between them,
/// wherever you are — and the VOCABULARY becomes rules. A firm says "we call them floors" by
/// changing one row, not by waiting for a build.
///
/// Every default here is KOR's, so a job that says nothing reads exactly as it did before.
/// </summary>
public sealed record DrawingVocabulary
{
    /// <summary>What this office calls a storey. `dxf.level-words`.</summary>
    public IReadOnlyList<string> LevelWords { get; init; } = new[] { "LEVEL", "L" };

    /// <summary>What it calls a building on a sheet title. `dxf.building-words`.</summary>
    public IReadOnlyList<string> BuildingWords { get; init; } = new[] { "BLDG", "BUILDING" };

    /// <summary>How a below-grade storey is numbered — P1, P2. `dxf.parkade-words`.</summary>
    public IReadOnlyList<string> ParkadeWords { get; init; } = new[] { "P" };

    /// <summary>What sits between the two ends of a level range. `dxf.range-words`.</summary>
    public IReadOnlyList<string> RangeWords { get; init; } = new[] { "-", "TO", "THRU", "THROUGH" };

    /// <summary>`dxf.roof-words`.</summary>
    public IReadOnlyList<string> RoofWords { get; init; } = new[] { "ROOF" };

    /// <summary>`dxf.mezzanine-words`.</summary>
    public IReadOnlyList<string> MezzanineWords { get; init; } = new[] { "MEZZ" };

    /// <summary>
    /// How this office numbers an issued sheet, as a regular expression matched against a drawing
    /// title. `dxf.sheet-number-pattern`.
    ///
    /// A Revit export offers every view in the model, and only some of them are drawings. KOR's
    /// issued sheets carry their number in the title — S2.22.1_1_LEVEL 33 PLAN … — and the rest
    /// are working views the drafter kept: LEVEL 26, B-LEVEL 33, uncropped, showing every building
    /// standing at that elevation.
    ///
    /// That is why 31168's B-LEVEL 33 held 73 columns where the building has 24. The view is named
    /// for one tower and draws them all, so both towers' structure went up tower B's stack and
    /// tower A's storeys came out with a floor plate and nothing under them.
    /// </summary>
    public string SheetNumberPattern { get; init; } = @"\b[A-Z]{1,3}\d+(?:\.\d+)*_\d+_";

    /// <summary>A slab on grade is not a suspended floor. `dxf.foundation-words`.</summary>
    public IReadOnlyList<string> FoundationWords { get; init; } = new[] { "FOUNDATION" };

    /// <summary>
    /// A roof over the lift overrun, which is not the building's roof. `dxf.elevator-roof-words`.
    /// </summary>
    public IReadOnlyList<string> ElevatorRoofWords { get; init; } =
        new[] { "ELEVATOR ROOF", "ELEV ROOF" };

    /// <summary>
    /// A STOREY MAY BE NAMED BY A WORD (intake step 47, 2026-09-13). KOR's small jobs name their plans
    /// "MAIN FLOOR PLAN SHOWING 2ND FLOOR FRAMING OVER", "UPPER FLOOR PLAN", "BASEMENT FLOOR PLAN",
    /// "LOFT PLAN" — no LEVEL, no number — and 68 of the corpus's 86 sets without a model had plans
    /// named exactly so (383 plans). Each entry is WORD=LEVEL: the level number the word names.
    /// <c>dxf.floor-words</c>.
    /// </summary>
    public IReadOnlyList<string> FloorWords { get; init; } = new[] { "MAIN=1", "GROUND=1", "UPPER=2" };

    /// <summary>A word that names the storey below the main floor — a parkade level by another name. <c>dxf.basement-words</c>.</summary>
    public IReadOnlyList<string> BasementWords { get; init; } = new[] { "BASEMENT", "LOWER", "CELLAR" };

    /// <summary>A word that names the storey above the highest numbered plan, under the roof. <c>dxf.top-floor-words</c>.</summary>
    public IReadOnlyList<string> TopFloorWords { get; init; } = new[] { "LOFT", "ATTIC" };

    /// <summary>The nouns a floor word or an ordinal is followed by: "2ND FLOOR", "MAIN LEVEL". <c>dxf.floor-nouns</c>.</summary>
    public IReadOnlyList<string> FloorNouns { get; init; } = new[] { "FLOOR", "LEVEL", "STOREY", "STORY" };

    /// <summary>
    /// The word after which a title names the framing OVER the plan, not the plan's own storey: "MAIN FLOOR
    /// PLAN SHOWING 2ND FLOOR FRAMING OVER" is the main floor's plan. <c>dxf.framing-over-words</c>.
    /// </summary>
    public IReadOnlyList<string> FramingOverWords { get; init; } = new[] { "SHOWING" };

    private static readonly string[] OrdinalWords =
        { "FIRST", "SECOND", "THIRD", "FOURTH", "FIFTH", "SIXTH", "SEVENTH", "EIGHTH", "NINTH", "TENTH", "ELEVENTH", "TWELFTH" };

    /// <summary>The level a word names, or null: "MAIN" is 1, "SECOND" and "2ND" are 2.</summary>
    public int? LevelOfWord(string word)
    {
        string w = word.Trim().ToUpperInvariant();
        foreach (string entry in FloorWords)
        {
            int eq = entry.IndexOf('=');
            if (eq > 0 && entry[..eq].Trim().Equals(w, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(entry[(eq + 1)..].Trim(), out int level)) return level;
        }
        int ordinal = Array.IndexOf(OrdinalWords, w);
        if (ordinal >= 0) return ordinal + 1;
        var m = Regex.Match(w, @"^(\d{1,2})(?:ST|ND|RD|TH)$");
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    /// <summary>
    /// THE SET'S OWN ORDER OF ITS FLOOR WORDS (step 60, 2026-09-13). The row says MAIN and GROUND are both level
    /// 1 - true of a set that uses one of them - but 31089-01's townhouses draw "GROUND FLOOR SHOWING MAIN FLOOR
    /// FRAMING OVER", "MAIN FLOOR SHOWING UPPER FLOOR FRAMING OVER", "UPPER FLOOR SHOWING ROOF FRAMING OVER":
    /// three floors, and the framing-over clauses say their order. Where the titles chain floor words that way,
    /// the chain ranks them from 1 upward (a word that shows a numbered floor over it sits one below that
    /// number) and those levels replace the row's; a word the chain never names keeps the row's level.
    /// </summary>
    public DrawingVocabulary WithFloorWordsRankedBy(IEnumerable<string> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);
        var words = FloorWords.Select(e => e.Split('=')[0].Trim().ToUpperInvariant()).Where(w => w.Length > 0).ToHashSet(StringComparer.Ordinal);
        // ONE CHAIN PER BUILDING (the second audit's B4): "GROUND ... SHOWING MAIN ... BLDG A" and "MAIN ... SHOWING
        // UPPER ... BLDG B" are two chains, not one; the buildings' chains must agree on every word they share
        var overByBuilding = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (string title in titles)
        {
            string t = title.ToUpperInvariant();
            string own = OwnStoreyPart(t);
            if (own.Length == t.Length) continue;                              // no framing-over clause: nothing said about order
            string ownWord = FloorWordIn(own), overWord = FloorWordIn(t[own.Length..]);
            if (!words.Contains(ownWord) || overWord.Length == 0) continue;
            if (ownWord == overWord) return this;                             // a floor shown over itself: two stories, the row stands (B5)
            string building = Building.Match(t) is { Success: true } b ? b.Groups[1].Value.ToUpperInvariant() : PrefixBuilding.Match(t) is { Success: true } pb ? pb.Groups[1].Value.ToUpperInvariant() : "";
            if (!overByBuilding.TryGetValue(building, out var over)) overByBuilding[building] = over = new Dictionary<string, string>(StringComparer.Ordinal);
            if (over.TryGetValue(ownWord, out string? had) && had != overWord) return this;   // two stories about one floor: the row stands
            over[ownWord] = overWord;
        }
        if (overByBuilding.Count == 0) return this;
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var over in overByBuilding.Values)
        {
            // the chain from its bottom: a word nothing is shown over ... up to the top word or a number
            var shownOver = over.Values.ToHashSet(StringComparer.Ordinal);
            var bottoms = over.Keys.Where(w => !shownOver.Contains(w)).ToList();
            if (bottoms.Count != 1) return this;
            var chain = new List<string>();
            for (string? w = bottoms[0]; w is not null && words.Contains(w) && !chain.Contains(w); w = over.GetValueOrDefault(w)) chain.Add(w);
            // every clause must agree with the chain: a word shown over a word above it (a cycle with a tail, B5) is two stories
            foreach (var (lower, upper) in over)
                if (chain.Contains(upper) && (!chain.Contains(lower) || chain.IndexOf(upper) != chain.IndexOf(lower) + 1)) return this;
            if (chain.Count < 2 && !(chain.Count == 1 && over.TryGetValue(chain[0], out var top) && int.TryParse(top, out _))) continue;
            int first = 1;
            // a numbered floor shown over the top word anchors the chain: "MAIN FLOOR SHOWING 2ND FLOOR FRAMING OVER" keeps MAIN at 1
            if (over.TryGetValue(chain[^1], out string? above) && int.TryParse(above, out int n)) first = n - chain.Count;
            for (int i = 0; i < chain.Count; i++)
            {
                if (ranks.TryGetValue(chain[i], out int had) && had != first + i) return this;   // two buildings disagree: the row stands
                ranks[chain[i]] = first + i;
            }
        }
        if (ranks.Count == 0) return this;
        var ranked = ranks.Select(kv => $"{kv.Key}={kv.Value}").ToList();
        var kept = FloorWords.Where(e => !ranks.ContainsKey(e.Split('=')[0].Trim().ToUpperInvariant()));
        return this with { FloorWords = ranked.Concat(kept).ToList() };

        string FloorWordIn(string s)
        {
            var m = WordFloor.Match(s);
            if (m.Success)
            {
                string w = m.Groups[1].Value.ToUpperInvariant();
                if (words.Contains(w)) return w;
                if (LevelOfWord(w) is int level) return level.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            // "LEVEL 4" shown over a word anchors the chain as "4TH FLOOR" does (B1)
            var n = SingleLevel.Match(s);
            return n.Success && int.TryParse(n.Groups[1].Value, out int number) ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        }
    }

    /// <summary>The title before its framing-over clause: what the plan is the plan OF.</summary>
    public string OwnStoreyPart(string title)
    {
        foreach (string w in FramingOverWords)
        {
            int at = title.IndexOf(w, StringComparison.OrdinalIgnoreCase);
            if (at > 0 && (at + w.Length == title.Length || !char.IsLetter(title[at + w.Length])) && !char.IsLetter(title[at - 1]))
                title = title[..at];
        }
        return title;
    }

    public static DrawingVocabulary Default { get; } = new();

    /// <summary>Whether a name carries any of these words, case-insensitively.</summary>
    public static bool Mentions(string name, IReadOnlyList<string> words)
        => words.Any(w => w.Length > 0
                          && name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);

    public bool IsRoofName(string name) => Mentions(name, RoofWords);
    public bool IsMezzanineName(string name) => Mentions(name, MezzanineWords);
    public bool IsFoundationName(string name) => Mentions(name, FoundationWords);

    /// <summary>Whether a drawing title carries a sheet number, and so is an issued drawing.</summary>
    public bool IsIssuedSheetName(string name)
        => !string.IsNullOrWhiteSpace(SheetNumberPattern)
           && (_issued ??= new Regex(SheetNumberPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
               .IsMatch(name);
    public bool IsElevatorRoofName(string name) => Mentions(name, ElevatorRoofWords);

    // ------------------------------------------------------------------------------------------
    // The patterns, built once per vocabulary rather than compiled into the assembly.
    //
    // Cached on the record because Parse runs per sheet and a job has scores of them; building a
    // Regex per call would be the kind of quiet cost that only shows up on a big drawing set.
    // ------------------------------------------------------------------------------------------

    private Regex? _building, _prefixBuilding, _range, _levelList, _singleLevel, _parkadeLevel, _parkadeStory, _issued, _wordFloor, _basement, _topFloor;

    /// <summary>"MAIN FLOOR", "2ND FLOOR", "SECOND LEVEL": a floor word or an ordinal, then a floor noun.</summary>
    public Regex WordFloor => _wordFloor ??= new Regex(
        $@"\b({Any(FloorWords.Select(e => e.Split('=')[0].Trim()).ToList())}|{string.Join("|", OrdinalWords)}|\d{{1,2}}(?:ST|ND|RD|TH))\s+(?:{Any(FloorNouns)})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>"BASEMENT", "LOWER FLOOR": the storey below the main floor.</summary>
    public Regex Basement => _basement ??= new Regex($@"\b(?:{Any(BasementWords)})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>"LOFT", "ATTIC": the storey above the highest numbered plan.</summary>
    public Regex TopFloor => _topFloor ??= new Regex($@"\b(?:{Any(TopFloorWords)})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string Any(IReadOnlyList<string> words)
        => string.Join("|", words.Where(w => w.Length > 0)
                                 .OrderByDescending(w => w.Length)     // LEVEL before L
                                 .Select(Regex.Escape));

    /// <summary>
    /// "BLDG A", "BUILDING A &amp; B" — and "BUILDING 1", "BUILDING 1A", "BUILDING 12" (step 69, 2026-09-14): a
    /// building is named by a letter or a number with an optional letter. Ten sets in the corpus number their
    /// buildings; with letters alone their tags read as nothing and 31185's five buildings' LEVEL 1 plans all
    /// landed on one storey. A word boundary closes the tag, so "BUILDING PERMIT" names no building.
    /// </summary>
    public Regex Building => _building ??= new Regex(
        $@"(?:{Any(BuildingWords)})\s*((?:[A-Z]|\d{{1,2}}[A-Z]?)(?:\s*&\s*(?:[A-Z]|\d{{1,2}}[A-Z]?))*)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>"A-LEVEL 28", "1-LEVEL 2" — a building named as a prefix on the storey itself.</summary>
    public Regex PrefixBuilding => _prefixBuilding ??= new Regex(
        $@"(?<![A-Z0-9])([A-Z]|\d{{1,2}}[A-Z]?)-(?:{Any(LevelWords)})\s*\d",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>"LEVEL 4 TO 14", "L15-26".</summary>
    public Regex Range => _range ??= new Regex(
        $@"(?:{Any(LevelWords)})\s*(\d+)\s*(?:{Any(RangeWords)})\s*(?:{Any(LevelWords)})?\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>"LEVEL 8, 9" — two floors on one sheet, and reading only the 8 loses a storey.</summary>
    public Regex LevelList => _levelList ??= new Regex(
        $@"(?:{Any(LevelWords)})\s*(\d+)((?:\s*(?:,|&|and)\s*\d+)+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>"LEVEL 9".</summary>
    public Regex SingleLevel => _singleLevel ??= new Regex(
        $@"(?:{Any(LevelWords)})\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>"LEVEL P2" on a sheet title.</summary>
    public Regex ParkadeLevel => _parkadeLevel ??= new Regex(
        $@"(?:{Any(LevelWords)})\s*(?:{Any(ParkadeWords)})\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// "P2" as a MODEL names the storey. Drafting titles a sheet "LEVEL P2" and a model may call
    /// the storey just "P2" — 31138 does, and because the pattern demanded the word LEVEL, every
    /// below-grade sheet in that project matched no storey and the whole parkade went missing.
    /// </summary>
    public Regex ParkadeStory => _parkadeStory ??= new Regex(
        $@"^\s*(?:(?:{Any(LevelWords)})\s*)?(?:{Any(ParkadeWords)})\s*(\d+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
