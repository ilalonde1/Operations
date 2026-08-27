using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// What a drawing set's own shorthand means, learned from the set.
///
/// 31168 splits every shared podium level between two sheets: one says
/// "LEVEL 2 PLAN - CONCRETE OUTLINE - BLDG C" and the other says
/// "LEVEL 2 PLAN - CONCRETE OUTLINE - WEST (BLDG A &amp; B)". A level up, the same pair reads
/// "... - BLDG C" and "... - WEST" — the qualifier alone, because by then the set has said once
/// what WEST means and drafting does not repeat itself.
///
/// A person reading the set has no trouble with this. The tool had: the WEST sheet carried no
/// building letters, so building A and B's whole ground floor read as nobody's, stayed in the
/// YMCA model, and put 225,654 sq ft on a 22,000 sq ft storey.
///
/// So the set is read twice. The first pass collects every qualifier that appears somewhere
/// spelled out, and the second lets the short form mean what the long form said. Nothing is
/// assumed about the words themselves — WEST is not a compass direction to this class, it is a
/// token this particular set has defined. A set that never spells one out learns nothing and
/// behaves exactly as before.
/// </summary>
public sealed class SheetSetGlossary
{
    private readonly Dictionary<string, IReadOnlyList<string>> _means =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Meanings => _means;

    /// <summary>Reads the set and returns what it turns out to have defined.</summary>
    public static SheetSetGlossary Learn(IEnumerable<string> fileNames)
    {
        var glossary = new SheetSetGlossary();
        var conflicting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in fileNames)
        {
            var sheet = PlanSheetNaming.Parse(file);
            if (sheet.BuildingTags.Count == 0) continue;

            foreach (string word in QualifiersIn(file))
            {
                if (conflicting.Contains(word)) continue;

                if (!glossary._means.TryGetValue(word, out var already))
                {
                    glossary._means[word] = sheet.BuildingTags;
                    continue;
                }

                // The same word standing for two different buildings is not shorthand, it is a
                // coincidence — "OUTLINE" next to BLDG A on one sheet and BLDG B on another. A
                // word that cannot make up its mind teaches nothing and is struck out.
                if (!already.SequenceEqual(sheet.BuildingTags, StringComparer.OrdinalIgnoreCase))
                {
                    glossary._means.Remove(word);
                    conflicting.Add(word);
                }
            }
        }

        return glossary;
    }

    /// <summary>
    /// The tags a sheet carries once the set's shorthand is allowed to speak. A sheet that names
    /// its own buildings is left alone; the glossary only fills a silence.
    /// </summary>
    public IReadOnlyList<string> TagsFor(PlanSheetInfo sheet)
    {
        if (sheet.BuildingTags.Count > 0) return sheet.BuildingTags;

        foreach (string word in QualifiersIn(sheet.FileName))
            if (_means.TryGetValue(word, out var tags))
                return tags;

        return Array.Empty<string>();
    }

    /// <summary>
    /// The qualifier words of a sheet title: what follows the last dash, with any parenthesised
    /// aside removed. "LEVEL 2 PLAN - CONCRETE OUTLINE - WEST (BLDG A &amp; B)" qualifies on WEST.
    ///
    /// Only the last segment, and only when it is one or two words. A whole title is not shorthand
    /// and "CONCRETE OUTLINE" appears on every sheet in the set, so a rule that learned from it
    /// would teach the set that every sheet belongs to whichever building came first.
    /// </summary>
    private static IEnumerable<string> QualifiersIn(string fileName)
    {
        string title = Path.GetFileNameWithoutExtension(fileName);

        int dash = title.LastIndexOf(" - ", StringComparison.Ordinal);
        if (dash < 0) yield break;

        string tail = Regex.Replace(title[(dash + 3)..], @"\([^)]*\)", " ").Trim();
        if (tail.Length == 0) yield break;

        var words = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > 2) yield break;

        // A qualifier that is itself a level or a discipline is not a building shorthand.
        if (words.Any(w => w.Any(char.IsDigit))) yield break;
        if (PlanSheetNaming.Vocabulary.IsRoofName(tail)) yield break;

        yield return string.Join(' ', words);
    }
}
