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

    private Regex? _building, _prefixBuilding, _range, _levelList, _singleLevel, _parkadeLevel, _parkadeStory, _issued;

    private static string Any(IReadOnlyList<string> words)
        => string.Join("|", words.Where(w => w.Length > 0)
                                 .OrderByDescending(w => w.Length)     // LEVEL before L
                                 .Select(Regex.Escape));

    /// <summary>"BLDG A", "BUILDING A &amp; B" — the buildings a sheet is drawn for.</summary>
    public Regex Building => _building ??= new Regex(
        $@"(?:{Any(BuildingWords)})\s*([A-Z](?:\s*&\s*[A-Z])*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>"A-LEVEL 28" — a building named as a prefix on the storey itself.</summary>
    public Regex PrefixBuilding => _prefixBuilding ??= new Regex(
        $@"(?<![A-Z0-9])([A-Z])-(?:{Any(LevelWords)})\s*\d",
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
