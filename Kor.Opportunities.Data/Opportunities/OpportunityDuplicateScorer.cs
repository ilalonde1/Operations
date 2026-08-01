#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Opportunities.Data.Opportunities;

/// <summary>How confident we are that two opportunities are the same real-world RFP.</summary>
public enum DuplicateConfidence
{
    None = 0,
    Medium = 1,
    High = 2,
}

/// <summary>
/// Pure name-similarity scorer for the manual-entry duplicate guard
/// (2026-07-07). Deliberately reuses nothing fuzzy from CanonicalOrgResolver
/// for the NAME (that toolkit is org-name specific); buyer matching is done by
/// the caller via the canonical-org resolver. This scores opportunity TITLES.
///
/// Combined score = mean(token Jaccard, character-trigram Dice), both in [0,1]:
/// Jaccard catches reordered/added words ("Seismic Upgrade Consulting" vs
/// "Consulting Services Seismic Upgrade"); trigram Dice catches typos and
/// partial tokens. A shared buyer LOWERS the confidence thresholds (the caller
/// passes sameBuyer) because same-buyer near-titles are far likelier dupes.
/// </summary>
public static class OpportunityDuplicateScorer
{
    // Procurement boilerplate that appears in most titles and carries no
    // discriminating signal — dropped from the token set so "RFP for X" and
    // "ITT X" compare on X.
    private static readonly HashSet<string> StopTokens = new(StringComparer.Ordinal)
    {
        "the", "for", "of", "and", "a", "an", "to", "at", "on", "in",
        "rfp", "rfq", "rfpq", "rfsq", "itt", "eoi", "itb", "nrfp", " nrfp",
        "services", "service", "consulting", "consultant", "project", "projects",
        "provision", "supply", "professional",
    };

    /// <summary>
    /// Name similarity in [0,1]. Empty/blank inputs score 0.
    /// </summary>
    public static double NameSimilarity(string? a, string? b)
    {
        // A duplicate check with an empty side is meaningless — and two blanks
        // must NOT score 1.0 (they'd flag every new entry with a missing name).
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return 0.0;
        }

        var ta = Tokens(a);
        var tb = Tokens(b);

        // Both halves score over the SIGNIFICANT tokens only — never the raw
        // string (adversarial review 2026-07-07: trigrams over shared
        // procurement boilerplate like "consultingengineeringservicesfor…"
        // falsely inflated similarity between different capital projects at a
        // coarse buyer). All-boilerplate titles collapse to 0 (not flagged).
        if (ta.Count == 0 || tb.Count == 0)
        {
            return 0.0;
        }

        var jaccard = Jaccard(ta, tb);
        var trigram = TrigramDice(string.Concat(ta.OrderBy(t => t, StringComparer.Ordinal)),
                                  string.Concat(tb.OrderBy(t => t, StringComparer.Ordinal)));
        return (jaccard + trigram) / 2.0;
    }

    /// <summary>
    /// Bucket a raw score into a confidence, with same-buyer opportunities held
    /// to a lower bar (they are much likelier to be genuine dups).
    /// </summary>
    public static DuplicateConfidence Classify(double score, bool sameBuyer)
    {
        if (sameBuyer)
        {
            if (score >= 0.60) return DuplicateConfidence.High;
            if (score >= 0.40) return DuplicateConfidence.Medium;
            return DuplicateConfidence.None;
        }

        if (score >= 0.82) return DuplicateConfidence.High;
        if (score >= 0.68) return DuplicateConfidence.Medium;
        return DuplicateConfidence.None;
    }

    private static List<string> Tokens(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return new List<string>();
        return SplitWords(s)
            .Select(w => w.ToLowerInvariant())
            .Where(w => w.Length > 2 && !StopTokens.Contains(w))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<string> SplitWords(string s)
    {
        var word = new System.Text.StringBuilder();
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch))
            {
                word.Append(ch);
            }
            else if (word.Length > 0)
            {
                yield return word.ToString();
                word.Clear();
            }
        }

        if (word.Length > 0)
        {
            yield return word.ToString();
        }
    }

    private static double Jaccard(List<string> a, List<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return 0.0;
        var setA = new HashSet<string>(a, StringComparer.Ordinal);
        var setB = new HashSet<string>(b, StringComparer.Ordinal);
        var intersection = setA.Count(setB.Contains);
        var union = setA.Count + setB.Count - intersection;
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static double TrigramDice(string a, string b)
    {
        var ta = Trigrams(a);
        var tb = Trigrams(b);
        if (ta.Count == 0 && tb.Count == 0) return a == b ? 1.0 : 0.0;
        if (ta.Count == 0 || tb.Count == 0) return 0.0;

        var intersection = 0;
        // Multiset intersection so repeated trigrams don't over-credit.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var g in ta)
        {
            counts[g] = counts.TryGetValue(g, out var c) ? c + 1 : 1;
        }

        foreach (var g in tb)
        {
            if (counts.TryGetValue(g, out var c) && c > 0)
            {
                counts[g] = c - 1;
                intersection++;
            }
        }

        return 2.0 * intersection / (ta.Count + tb.Count);
    }

    private static List<string> Trigrams(string s)
    {
        var result = new List<string>();
        if (s.Length < 3)
        {
            if (s.Length > 0) result.Add(s);
            return result;
        }

        for (var i = 0; i + 3 <= s.Length; i++)
        {
            result.Add(s.Substring(i, 3));
        }

        return result;
    }
}
