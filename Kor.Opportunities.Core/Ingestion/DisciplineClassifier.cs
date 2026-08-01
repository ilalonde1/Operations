#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Core.Ingestion;

/// <summary>
/// Deterministic, keyword/commodity-code derivation of the KOR structural-
/// relevance <see cref="OpportunityDiscipline"/> for an ingestion candidate.
/// No AI, no I/O — a pure function so it is cheap and unit-testable.
///
/// Scope discipline: this classifier answers "does this involve structural
/// work?" (Structural / Mixed / Inspections) and returns
/// <see cref="OpportunityDiscipline.Unknown"/> when it can't tell. It deliberately
/// does NOT emit <see cref="OpportunityDiscipline.OutOfScope"/> — hard relevance is
/// owned upstream by <c>StructuralRelevanceGate</c> at intake, which uses
/// word-boundary matching and a building-signal override. Re-deriving OutOfScope
/// here from a substring match would re-condemn candidates the gate deliberately
/// kept (e.g. "retrofit services" contains "it services"), so it is intentionally
/// omitted. Sets the <c>Discipline</c> column only; does not touch RelevanceTier
/// or PrimeDisciplineType (separate axes).
/// </summary>
public static class DisciplineClassifier
{
    // Structural signals — specific phrases + the UNSPSC structural-engineering
    // code. Deliberately NOT bare "structural"/"structure" (which would substring-
    // match "infrastructure"/"substructure").
    private static readonly string[] StructuralSignals =
    {
        "81101505",              // UNSPSC structural engineering
        "structural engineer",   // "structural engineer", "structural engineering"
        "seismic retrofit",
        "seismic upgrade",
        "structural design",
        "structural rehabilitation",
    };

    // Other design disciplines — used only to distinguish Structural vs Mixed.
    // Sibling UNSPSC leaf codes + phrases. The parent/family code 81101500 is
    // intentionally excluded: it co-occurs with 81101505 on pure-structural
    // notices and would spuriously flip Structural -> Mixed.
    private static readonly string[] OtherDesignSignals =
    {
        "81101508", "architectural engineer", "architectural engineering",
        "81101600", "mechanical engineer",
        "81101701", "electrical engineer",
        "civil engineer",
    };

    private static readonly string[] InspectionSignals =
    {
        "building envelope", "condition assessment", "building inspection",
        "structural inspection", "restoration engineering",
    };

    public static OpportunityDiscipline Classify(OpportunityCandidate candidate)
    {
        if (candidate is null) return OpportunityDiscipline.Unknown;
        return Classify(candidate.CommodityCodes, candidate.Title, candidate.Description);
    }

    public static OpportunityDiscipline Classify(
        IReadOnlyList<string>? commodityCodes,
        string? title,
        string? description)
    {
        var blob = BuildBlob(commodityCodes, title, description);
        if (blob.Length == 0) return OpportunityDiscipline.Unknown;

        var hasStructural = ContainsAny(blob, StructuralSignals);
        var hasOtherDesign = ContainsAny(blob, OtherDesignSignals);

        if (hasStructural)
        {
            return hasOtherDesign ? OpportunityDiscipline.Mixed : OpportunityDiscipline.Structural;
        }

        // Inspection / assessment work with no structural-design signal.
        if (ContainsAny(blob, InspectionSignals))
        {
            return OpportunityDiscipline.Inspections;
        }

        // Not confidently structural. Relevance/OutOfScope is the gate's job, not
        // ours — default Unknown.
        return OpportunityDiscipline.Unknown;
    }

    private static string BuildBlob(IReadOnlyList<string>? codes, string? title, string? description)
    {
        var sb = new StringBuilder();
        if (codes is not null)
        {
            foreach (var c in codes)
            {
                if (!string.IsNullOrWhiteSpace(c)) sb.Append(c).Append(' ');
            }
        }
        if (!string.IsNullOrWhiteSpace(title)) sb.Append(title).Append(' ');
        if (!string.IsNullOrWhiteSpace(description)) sb.Append(description);
        return sb.ToString().ToLowerInvariant();
    }

    private static bool ContainsAny(string blob, string[] needles)
    {
        foreach (var n in needles)
        {
            if (blob.Contains(n, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
