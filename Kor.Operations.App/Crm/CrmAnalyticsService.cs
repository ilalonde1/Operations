#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Opportunities.Core.Models;

namespace Kor.Operations.App.Crm;

/// <summary>
/// One-shot snapshot of CRM performance — feeds the analytics panel and
/// the AI context. Pure projection over engagements + linked opportunities;
/// no DB hit beyond what CrmViewModel already loaded.
/// </summary>
public sealed record CrmAnalyticsSnapshot(
    int TotalEngagements,
    IReadOnlyList<CrmStageCount> ByStage,
    int Won,
    int Lost,
    double WinRate,                                      // won / (won + lost); 0 if denominator 0
    IReadOnlyList<CrmSlicedWinRate> ByBuyerType,
    IReadOnlyList<CrmSlicedWinRate> ByOwner,
    decimal AvgWonProposedFee,
    decimal AvgLostProposedFee,
    TimeSpan? AvgPursuitDuration);                       // OpenedAtUtc → ClosedAtUtc, Won/Lost only

public sealed record CrmStageCount(CrmEngagementStage Stage, int Count);

public sealed record CrmSlicedWinRate(
    string Bucket,
    int Won,
    int Lost,
    int Active,
    double WinRate);

public static class CrmAnalyticsService
{
    public static CrmAnalyticsSnapshot Compute(
        IReadOnlyCollection<CrmEngagement> engagements,
        IReadOnlyDictionary<long, Opportunity> opportunitiesById)
    {
        if (engagements.Count == 0)
        {
            return Empty();
        }

        var byStage = engagements
            .GroupBy(e => e.Stage)
            .OrderBy(g => (int)g.Key)
            .Select(g => new CrmStageCount(g.Key, g.Count()))
            .ToList();

        var won = engagements.Count(e => e.Stage == CrmEngagementStage.Won);
        var lost = engagements.Count(e => e.Stage == CrmEngagementStage.Lost);
        var winRate = (won + lost) > 0 ? (double)won / (won + lost) : 0.0;

        // Slice 1: by buyer type. We pull the linked Opportunity's BuyerType.
        // Engagements whose Opportunity is missing get bucketed as "Unknown".
        var byBuyerType = engagements
            .GroupBy(e => opportunitiesById.TryGetValue(e.OpportunityId, out var o)
                ? o.BuyerType.ToString()
                : "Unknown")
            .Select(g => SliceWinRate(g.Key, g))
            .OrderByDescending(s => s.Won + s.Lost + s.Active)
            .ThenByDescending(s => s.WinRate)
            .ToList();

        // Slice 2: by owner staff id. Empty / null bucketed as "(unassigned)".
        var byOwner = engagements
            .GroupBy(e => string.IsNullOrWhiteSpace(e.OwnerStaffId) ? "(unassigned)" : e.OwnerStaffId!)
            .Select(g => SliceWinRate(g.Key, g))
            .OrderByDescending(s => s.Won + s.Lost + s.Active)
            .ThenByDescending(s => s.WinRate)
            .ToList();

        var avgWonFee = AvgFee(engagements.Where(e => e.Stage == CrmEngagementStage.Won));
        var avgLostFee = AvgFee(engagements.Where(e => e.Stage == CrmEngagementStage.Lost));

        var resolved = engagements
            .Where(e => (e.Stage == CrmEngagementStage.Won || e.Stage == CrmEngagementStage.Lost)
                     && e.ClosedAtUtc.HasValue)
            .Select(e => e.ClosedAtUtc!.Value - e.OpenedAtUtc)
            .ToList();
        TimeSpan? avgDuration = resolved.Count > 0
            ? TimeSpan.FromTicks((long)resolved.Average(d => d.Ticks))
            : null;

        return new CrmAnalyticsSnapshot(
            TotalEngagements: engagements.Count,
            ByStage: byStage,
            Won: won,
            Lost: lost,
            WinRate: winRate,
            ByBuyerType: byBuyerType,
            ByOwner: byOwner,
            AvgWonProposedFee: avgWonFee,
            AvgLostProposedFee: avgLostFee,
            AvgPursuitDuration: avgDuration);
    }

    private static CrmSlicedWinRate SliceWinRate(string bucket, IEnumerable<CrmEngagement> rows)
    {
        var w = 0; var l = 0; var a = 0;
        foreach (var r in rows)
        {
            switch (r.Stage)
            {
                case CrmEngagementStage.Won: w++; break;
                case CrmEngagementStage.Lost: l++; break;
                default: a++; break;                                 // any in-flight stage
            }
        }
        var rate = (w + l) > 0 ? (double)w / (w + l) : 0.0;
        return new CrmSlicedWinRate(bucket, w, l, a, rate);
    }

    private static decimal AvgFee(IEnumerable<CrmEngagement> rows)
    {
        var fees = rows.Where(r => r.ProposedFee.HasValue).Select(r => r.ProposedFee!.Value).ToList();
        return fees.Count == 0 ? 0m : fees.Average();
    }

    private static CrmAnalyticsSnapshot Empty() => new(
        TotalEngagements: 0,
        ByStage: Array.Empty<CrmStageCount>(),
        Won: 0, Lost: 0,
        WinRate: 0.0,
        ByBuyerType: Array.Empty<CrmSlicedWinRate>(),
        ByOwner: Array.Empty<CrmSlicedWinRate>(),
        AvgWonProposedFee: 0m,
        AvgLostProposedFee: 0m,
        AvgPursuitDuration: null);
}
