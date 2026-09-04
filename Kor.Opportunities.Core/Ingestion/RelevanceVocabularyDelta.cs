#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kor.Opportunities.Core.Ingestion;

/// <summary>
/// A PROPOSED addition to <see cref="StructuralRelevanceGate"/>'s vocabulary,
/// compiled once and handed to <c>Evaluate</c> so the same corpus can be scored
/// with and without it.
///
/// WHY THIS EXISTS: the gate is shared by every source. Editing its word lists
/// changes what BC Bid, Bonfire, bids&amp;tenders, APC and CanadaBuys ingest, and
/// nothing in the system would say so. A vocabulary change is exactly the shape
/// of change the repo's rule 11 names — "where a change is supposed to alter one
/// property and nothing else, run it both ways and diff everything else" — so a
/// delta is proved against the whole 13,521-row reject corpus and the 3,803 kept
/// observations BEFORE its terms are promoted into the gate itself.
///
/// Nothing in production constructs one. See <c>tools/RelevanceGateDiff</c>.
/// </summary>
public sealed class RelevanceVocabularyDelta
{
    private readonly (string Signal, Regex Pattern)[] _building;
    private readonly (string Signal, Regex Pattern)[] _professional;

    public RelevanceVocabularyDelta(
        IEnumerable<string> extraBuildingSignals,
        IEnumerable<string>? extraProfessionalSignals = null)
    {
        ArgumentNullException.ThrowIfNull(extraBuildingSignals);

        _building = Compile(extraBuildingSignals);
        _professional = Compile(extraProfessionalSignals ?? Array.Empty<string>());
    }

    /// <summary>The added building terms, in the order they were supplied.</summary>
    public IReadOnlyList<string> BuildingSignals => _building.Select(b => b.Signal).ToArray();

    /// <summary>The added professional-services terms.</summary>
    public IReadOnlyList<string> ProfessionalSignals => _professional.Select(p => p.Signal).ToArray();

    /// <summary>
    /// Which added term matched, or null. Used to attribute a verdict flip to a
    /// specific word — a diff that says "1,400 rows changed" and cannot say
    /// which word did it is not evidence.
    /// </summary>
    public string? FirstMatch(string loweredText)
    {
        foreach (var (signal, pattern) in _building)
        {
            if (pattern.IsMatch(loweredText))
            {
                return signal;
            }
        }

        foreach (var (signal, pattern) in _professional)
        {
            if (pattern.IsMatch(loweredText))
            {
                return signal;
            }
        }

        return null;
    }

    internal bool MatchesBuilding(string loweredText) => Matches(_building, loweredText);

    internal bool MatchesProfessional(string loweredText) => Matches(_professional, loweredText);

    private static bool Matches((string Signal, Regex Pattern)[] set, string loweredText)
    {
        foreach (var (_, pattern) in set)
        {
            if (pattern.IsMatch(loweredText))
            {
                return true;
            }
        }

        return false;
    }

    // Same shape as the gate's own keep-signal regexes: word-bounded with the
    // short morphological suffix, so "tower" covers "towers" but "structure"
    // still does not swallow "infrastructure" (audit-v2 #14).
    private static (string, Regex)[] Compile(IEnumerable<string> signals)
        => signals
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Select(s => (s, new Regex(
                $@"\b{Regex.Escape(s)}(?:s|es|d|ed|ing)?\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)))
            .ToArray();
}
