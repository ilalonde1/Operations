using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// WHAT KIND OF NAME A STOREY CARRIES (intake step 84, 2026-09-16; WP6a item 7). A model's storeys are named as the set
/// names them, and the plan's finish line for item 7 is a count of names outside the ladder's vocabulary at zero. This
/// is the vocabulary as a classer, so `corpus-query storeys` can say, over every built set, which names are the
/// ladder's own shapes, which are roofs and mezzanines by word, which carry an elevation for a name, and which are
/// garbage - a title's words taken for a storey ("DESIGN", "VERTS.", "LEVEL -"). Only the last class is a defect.
/// </summary>
public static class StoreyNameClass
{
    public enum Kind
    {
        /// <summary>L7, P3, B-L12, A-P2, ROOF, B-ROOF, Base.</summary>
        Ladder,
        /// <summary>A roof by kind: MAIN ROOF, LOW ROOF, UPPER ROOF, MECH. ROOF, ELEV. ROOF, ROOF LEVEL, PENTHOUSE, T.O.CORE, CANOPY.</summary>
        RoofByWord,
        /// <summary>A sub-level of a numbered one: L1M, P1M, L4B, L4C, L7A, L1A, L16R, P1(P1A), L0/P1.</summary>
        SubLevel,
        /// <summary>A basement or cellar by letter and count: B1..B4, C1..C4, R5 (a residential level).</summary>
        LetterAndCount,
        /// <summary>An elevation as a name: LEVEL +2.0.</summary>
        Elevation,
        /// <summary>A title's words taken for a storey: LEVEL, LEVEL -, DESIGN, VERTS., AMANITY. The defect class.</summary>
        Garbage,
    }

    private static readonly Regex LadderShape = new(@"^(?:[A-Z]-)?(?:L\d+|P\d+|ROOF)$|^Base$", RegexOptions.CultureInvariant);
    private static readonly Regex SubLevelShape = new(@"^(?:[A-Z]-)?(?:L|P)\d+(?:[A-Z]|\(\w+\)|/[A-Z]\d+)$", RegexOptions.CultureInvariant);
    private static readonly Regex LetterAndCountShape = new(@"^[BCR]\d+$", RegexOptions.CultureInvariant);
    private static readonly Regex ElevationShape = new(@"^LEVEL\s*[+-]\d+\.\d+$", RegexOptions.CultureInvariant);   // a decimal: an elevation, not a level counted downward (LEVEL -5 is P5, step 84)
    private static readonly string[] RoofWords = ["ROOF", "PENTHOUSE", "T.O.CORE", "T.O. CORE", "CANOPY", "MECH", "ELEV"];

    public static Kind Classify(string storeyName)
    {
        ArgumentNullException.ThrowIfNull(storeyName);
        string n = storeyName.Trim().ToUpperInvariant();
        if (n == "BASE" || LadderShape.IsMatch(storeyName.Trim())) return Kind.Ladder;
        if (SubLevelShape.IsMatch(n)) return Kind.SubLevel;
        if (LetterAndCountShape.IsMatch(n)) return Kind.LetterAndCount;
        if (ElevationShape.IsMatch(n)) return Kind.Elevation;
        if (RoofWords.Any(w => n.Contains(w, StringComparison.Ordinal))) return Kind.RoofByWord;
        return Kind.Garbage;
    }
}
