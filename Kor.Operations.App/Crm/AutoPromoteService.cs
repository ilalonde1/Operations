#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Opportunities;

namespace Kor.Operations.App.Crm;

/// <summary>
/// Result of an auto-promote sweep: which Opportunities crossed the bar
/// (high tier + non-terminal status + enough runway) and were turned into
/// CrmEngagements at stage Pursuing.
/// </summary>
public sealed record AutoPromoteResult(
    int Considered,        // total Opportunities that matched the tier filter
    int Skipped,           // already had an engagement
    int InsufficientTime,  // failed the deadline cutoff
    IReadOnlyList<long> PromotedOpportunityIds);

public interface IAutoPromoteService
{
    /// <summary>
    /// Promotes any High-tier Opportunity (in a non-terminal pursuit status)
    /// into a CrmEngagement at stage Pursuing — provided the submission
    /// deadline gives at least <paramref name="minDaysToDeadline"/> days of
    /// runway (or has no deadline at all). Idempotent: rows that already have
    /// an engagement are skipped.
    /// </summary>
    Task<AutoPromoteResult> PromoteHighTierAsync(string actor, int minDaysToDeadline, CancellationToken ct);
}

internal sealed class AutoPromoteService : IAutoPromoteService
{
    private readonly IOpportunityStore _opportunityStore;
    private readonly ICrmEngagementStore _engagementStore;

    public AutoPromoteService(
        IOpportunityStore opportunityStore,
        ICrmEngagementStore engagementStore)
    {
        _opportunityStore = opportunityStore;
        _engagementStore = engagementStore;
    }

    public async Task<AutoPromoteResult> PromoteHighTierAsync(string actor, int minDaysToDeadline, CancellationToken ct)
    {
        if (minDaysToDeadline < 0) minDaysToDeadline = 0;

        var opportunities = await _opportunityStore.ListAsync(ct).ConfigureAwait(false);
        var existingEngagements = await _engagementStore.ListAsync(ct).ConfigureAwait(false);
        var existingByOpportunityId = new HashSet<long>(existingEngagements.Select(e => e.OpportunityId));

        var deadlineCutoff = DateTimeOffset.UtcNow.AddDays(minDaysToDeadline);

        var candidates = opportunities
            .Where(o => o.RelevanceTier == RelevanceTier.High)
            .Where(o => !IsTerminalStatus(o.Status))
            .ToList();

        var skipped = 0;
        var insufficientTime = 0;
        var promoted = new List<long>();

        foreach (var opp in candidates)
        {
            ct.ThrowIfCancellationRequested();

            if (existingByOpportunityId.Contains(opp.Id))
            {
                skipped++;
                continue;
            }

            // No deadline = always enough runway. With a deadline, require N days.
            if (opp.SubmissionDeadlineUtc.HasValue
                && opp.SubmissionDeadlineUtc.Value < deadlineCutoff)
            {
                insufficientTime++;
                continue;
            }

            var engagement = new CrmEngagement
            {
                OpportunityId = opp.Id,
                Stage = CrmEngagementStage.Pursuing,
                OwnerStaffId = opp.OwnerStaffId,
                Notes = "Auto-promoted: High-tier opportunity with available runway.",
                OpenedAtUtc = DateTimeOffset.UtcNow,
            };

            await _engagementStore.InsertAsync(engagement, actor, ct).ConfigureAwait(false);
            promoted.Add(opp.Id);
        }

        return new AutoPromoteResult(
            Considered: candidates.Count,
            Skipped: skipped,
            InsufficientTime: insufficientTime,
            PromotedOpportunityIds: promoted);
    }

    private static bool IsTerminalStatus(OpportunityStatus s) =>
        s is OpportunityStatus.Won
            or OpportunityStatus.Lost
            or OpportunityStatus.NoBid
            or OpportunityStatus.Withdrawn;
}
