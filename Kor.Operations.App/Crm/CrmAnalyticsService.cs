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
///
/// Fix F12 (CRM plan 2026-07-06): populations are SEGMENTED. The 177
/// migration-268 wins (ExternalSource = 'Deltek.CustomProposal') are flat
/// history with no owner and no loss denominator — blending them yields a
/// vanity 100% win rate. Won/Lost/WinRate/slices/fees/duration below are
/// LIVE-population only; the backfill is reported as its own counts.
/// </summary>
public sealed record CrmAnalyticsSnapshot(
    int TotalEngagements,
    IReadOnlyList<CrmStageCount> ByStage,
    int Won,                                             // live population only
    int Lost,                                            // live population only
    double WinRate,                                      // live won / (won + lost); 0 if denominator 0
    IReadOnlyList<CrmSlicedWinRate> ByBuyerType,         // live population only
    IReadOnlyList<CrmSlicedWinRate> ByOwner,             // live population only
    decimal AvgWonProposedFee,                           // live population only
    decimal AvgLostProposedFee,                          // live population only
    TimeSpan? AvgPursuitDuration,                        // live Won/Lost with ClosedAtUtc only
    int BackfillWins,                                    // Deltek.CustomProposal rows (all Won)
    decimal BackfillWonFee);                             // their summed ProposedFee

public sealed record CrmStageCount(CrmEngagementStage Stage, int Count);

public sealed record CrmSlicedWinRate(
    string Bucket,
    int Won,
    int Lost,
    int Active,
    double WinRate);

public static class CrmAnalyticsService
{
    /// <summary>The migration-268 backfill provenance key. Rows carrying it are
    /// historical Deltek wins, not live pursuit outcomes.</summary>
    public const string BackfillExternalSource = "Deltek.CustomProposal";

    public static bool IsBackfill(CrmEngagement e)
        => string.Equals(e.ExternalSource, BackfillExternalSource, StringComparison.OrdinalIgnoreCase);

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

        // F12 segmentation: rates and fee stats over the LIVE population only.
        var live = engagements.Where(e => !IsBackfill(e)).ToList();
        var backfill = engagements.Where(IsBackfill).ToList();
        var backfillWins = backfill.Count(e => e.Stage == CrmEngagementStage.Won);
        var backfillWonFee = backfill
            .Where(e => e.Stage == CrmEngagementStage.Won && e.ProposedFee.HasValue)
            .Sum(e => e.ProposedFee!.Value);

        var won = live.Count(e => e.Stage == CrmEngagementStage.Won);
        var lost = live.Count(e => e.Stage == CrmEngagementStage.Lost);
        var winRate = (won + lost) > 0 ? (double)won / (won + lost) : 0.0;

        // Slice 1: by buyer type. We pull the linked Opportunity's BuyerType.
        // Engagements whose Opportunity is missing get bucketed as "Unknown".
        var byBuyerType = live
            .GroupBy(e => e.OpportunityId.HasValue && opportunitiesById.TryGetValue(e.OpportunityId.Value, out var o)
                ? o.BuyerType.ToString()
                : "Unknown")
            .Select(g => SliceWinRate(g.Key, g))
            .OrderByDescending(s => s.Won + s.Lost + s.Active)
            .ThenByDescending(s => s.WinRate)
            .ToList();

        // Slice 2: by owner staff id. Empty / null bucketed as "(unassigned)".
        var byOwner = live
            .GroupBy(e => string.IsNullOrWhiteSpace(e.OwnerStaffId) ? "(unassigned)" : e.OwnerStaffId!)
            .Select(g => SliceWinRate(g.Key, g))
            .OrderByDescending(s => s.Won + s.Lost + s.Active)
            .ThenByDescending(s => s.WinRate)
            .ToList();

        var avgWonFee = AvgFee(live.Where(e => e.Stage == CrmEngagementStage.Won));
        var avgLostFee = AvgFee(live.Where(e => e.Stage == CrmEngagementStage.Lost));

        var resolved = live
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
            AvgPursuitDuration: avgDuration,
            BackfillWins: backfillWins,
            BackfillWonFee: backfillWonFee);
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
        AvgPursuitDuration: null,
        BackfillWins: 0,
        BackfillWonFee: 0m);
}
