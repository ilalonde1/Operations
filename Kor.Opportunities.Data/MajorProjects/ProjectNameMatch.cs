#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Kor.Opportunities.Data.MajorProjects;

/// <summary>
/// Whether two project names are the same project: a pursuit's "Mayerthorpe school" and a tender's "Mayerthorpe - Prime
/// Consultant Services - RFP", or a BD-tracking phrase and an inventory row. One matcher for every caller (the research
/// import's cross-link and the grab's carry-over), so the same pair always scores the same.
/// <para>
/// Scores 1.0 when one flattened name (12+ characters) contains the other, else the Jaccard of the significant tokens with
/// at least two in common, else 0. Does not compare place, buyer or date: a caller restricts candidates to the same province
/// first. A generic two-word overlap ("school replacement") between different towns' projects would still score; that is a
/// fault this matcher would NOT catch without the caller's narrowing.
/// </para>
/// </summary>
public static class ProjectNameMatch
{
    /// <summary>The score at or above which two names are taken as the same project.</summary>
    public const double Confident = 0.60;

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","an","the","of","and","or","for","to","in","at","on","by","with","from",
        "new","existing","project","building","centre","center","place",
        // Procurement words: every tender has them, so they say nothing about which project it is.
        "request","proposal","proposals","rfp","rfq","rfsq","services","service","consultant","consulting","prime","engineering",
        "structural","architectural","design","professional",
    };

    public static HashSet<string> Tokens(string name)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        void Flush()
        {
            if (sb.Length >= 3 && !Stopwords.Contains(sb.ToString())) tokens.Add(sb.ToString());
            sb.Clear();
        }
        foreach (var ch in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else Flush();
        }
        Flush();
        return tokens;
    }

    public static double Score(string a, string b) => Score(a, Tokens(a), b, Tokens(b));

    public static double Score(string a, HashSet<string> aTokens, string b, HashSet<string> bTokens)
    {
        if (aTokens.Count == 0 || bTokens.Count == 0) return 0;

        // Containment: the whole of one name inside the other. At least 12 characters, so a single word like "surrey" or
        // "towers" does not fire on every project that happens to contain it.
        var aFlat = string.Concat(a.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        var bFlat = string.Concat(b.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        if (Math.Min(aFlat.Length, bFlat.Length) >= 12 && (bFlat.Contains(aFlat) || aFlat.Contains(bFlat))) return 1.0;

        // Two significant tokens in common at least: a lone "tower" or "school" otherwise dominates.
        var common = aTokens.Intersect(bTokens, StringComparer.OrdinalIgnoreCase).Count();
        if (common < 2) return 0;
        return (double)common / aTokens.Union(bTokens, StringComparer.OrdinalIgnoreCase).Count();
    }
}
