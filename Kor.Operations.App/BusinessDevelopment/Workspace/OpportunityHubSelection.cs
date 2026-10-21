#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.App.Opportunities;
using Kor.Opportunities.Core.Models;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public enum OpportunityHubSegment { Signals, Bids, Mine }
public enum OpportunityWorkFilter { CollapseSmaller, All, SmallerOnly }

internal static class OpportunityHubSelection
{
    // Presentation only. Signals: the SQL view is the sole scope classifier and unknowns remain visible by default.
    // Bids: tenders that are not structural-firm work (roads, utilities, supplies; KorBidFit) fold away the same way,
    // with the count and a Show button. Mine is never collapsed: what a lead owns always shows.
    internal static bool IsCollapsible(OpportunityRowView row, OpportunityHubSegment segment) => segment switch
    {
        OpportunityHubSegment.Signals => row.Kind == OpportunityKind.Application && row.IsBigTicket == false,
        OpportunityHubSegment.Bids => row.Kind is OpportunityKind.Bid or OpportunityKind.Award
            && !Kor.Opportunities.Data.Ingestion.KorBidFit.IsKorWork(row.Model.Name),
        _ => false,
    };

    internal static bool MatchesWorkSize(OpportunityRowView row, OpportunityHubSegment segment, OpportunityWorkFilter filter)
    {
        if (segment == OpportunityHubSegment.Mine) return true;
        var collapsed = IsCollapsible(row, segment);
        return filter switch { OpportunityWorkFilter.CollapseSmaller => !collapsed,
            OpportunityWorkFilter.SmallerOnly => collapsed, _ => true };
    }
    internal static bool Matches(OpportunityRowView row, OpportunityHubSegment segment, string actor, OpportunityKind? kind = null)
    {
        if (row.Model.DismissedAtUtc is not null || (kind.HasValue && row.Kind != kind)) return false;
        return segment switch
        {
            OpportunityHubSegment.Signals => row.Kind is OpportunityKind.Application or OpportunityKind.Other,
            OpportunityHubSegment.Bids => row.Kind is OpportunityKind.Bid or OpportunityKind.Award,
            OpportunityHubSegment.Mine => !string.IsNullOrWhiteSpace(actor)
                && string.Equals(row.OwnerStaffId.Trim(), actor.Trim(), StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    internal static IOrderedEnumerable<OpportunityRowView> Sort(IEnumerable<OpportunityRowView> rows, OpportunityHubSegment segment, DateTimeOffset now)
        => segment switch
        {
            OpportunityHubSegment.Signals => rows.OrderByDescending(r => r.FiledAtUtc.HasValue)
                .ThenByDescending(r => r.FiledAtUtc).ThenByDescending(r => r.Model.IdentifiedAtUtc).ThenBy(r => r.Id),
            OpportunityHubSegment.Bids => rows.OrderBy(r => DeadlineGroup(r, now))
                .ThenBy(r => r.Model.SubmissionDeadlineUtc ?? DateTimeOffset.MaxValue).ThenBy(r => r.Id),
            _ => rows.OrderByDescending(r => r.Model.UpdatedAtUtc).ThenBy(r => r.Id),
        };

    private static int DeadlineGroup(OpportunityRowView row, DateTimeOffset now)
        => row.Kind == OpportunityKind.Award ? 3 : row.Model.SubmissionDeadlineUtc is not { } deadline ? 2 : deadline >= now ? 0 : 1;

    internal static IComparer Comparer(IEnumerable<OpportunityRowView> rows, OpportunityHubSegment segment)
        => new RankedComparer(Sort(rows, segment, DateTimeOffset.UtcNow).Select((row, rank) => (row.Id, rank)).ToDictionary(x => x.Id, x => x.rank));

    private sealed class RankedComparer(Dictionary<long, int> ranks) : IComparer
    {
        public int Compare(object? x, object? y) => Rank(x).CompareTo(Rank(y));
        private int Rank(object? row) => row is OpportunityRowView r && ranks.TryGetValue(r.Id, out var rank) ? rank : int.MaxValue;
    }
}
