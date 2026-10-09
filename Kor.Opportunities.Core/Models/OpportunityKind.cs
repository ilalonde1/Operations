#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Opportunities.Core.Models;

public enum OpportunityKind { Other, Application, Bid, Award }

/// <summary>Transport enums do not imply content. Mixed transports require bd.kind source metadata.</summary>
public static class OpportunityKinds
{
    public static OpportunityKind FromSource(OpportunitySourceType type, string? metadata = null) => type switch
    {
        OpportunitySourceType.ArcGisFeatureService or OpportunitySourceType.TempestProspero
            or OpportunitySourceType.EngagementHq => OpportunityKind.Application,
        OpportunitySourceType.CivicInfoHtml or OpportunitySourceType.SamGov or OpportunitySourceType.BcBid
            or OpportunitySourceType.BidsAndTenders or OpportunitySourceType.AlbertaPurchasingConnection
            or OpportunitySourceType.MerxDcc => OpportunityKind.Bid,
        OpportunitySourceType.BcBidAwards or OpportunitySourceType.BidsAndTendersAwards
            or OpportunitySourceType.AlbertaPurchasingConnectionAwards or OpportunitySourceType.BcBidUnverifiedBidResults
            or OpportunitySourceType.BcBidHistorical or OpportunitySourceType.GenericCsvAward
            or OpportunitySourceType.GenericJsonAward => OpportunityKind.Award,
        OpportunitySourceType.GenericCsv or OpportunitySourceType.GenericJson or OpportunitySourceType.Rss
            or OpportunitySourceType.Manual or OpportunitySourceType.GraphEmail => ParseMetadata(metadata),
        _ => OpportunityKind.Other,
    };

    private static OpportunityKind ParseMetadata(string? value) => value?.Trim().Trim('"').ToLowerInvariant() switch
    {
        "application" => OpportunityKind.Application,
        "bid" => OpportunityKind.Bid,
        "award" => OpportunityKind.Award,
        _ => OpportunityKind.Other,
    };

    // Cross-posts count once. Contradictory known kinds remain visibly unclassified.
    public static OpportunityKind Combine(IEnumerable<OpportunityKind> kinds)
    {
        var known = kinds.Where(k => k != OpportunityKind.Other).Distinct().ToArray();
        return known.Length == 1 ? known[0] : OpportunityKind.Other;
    }
}
