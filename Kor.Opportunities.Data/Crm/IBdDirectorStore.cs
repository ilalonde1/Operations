#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Crm;

/// <summary>Read-only facts not already exposed by the pursuit and attribution stores.</summary>
public interface IBdDirectorStore
{
    Task<IReadOnlyList<BdDirectorSignal>> GetRecentSignalsAsync(int days, int take, CancellationToken ct);
    Task<IReadOnlyList<BdDirectorPerson>> GetPeopleAsync(CancellationToken ct);
    Task<BdDirectorFunnel> GetFunnelAsync(CancellationToken ct);
    Task<BdTargetActuals> GetTargetActualsAsync(IReadOnlyList<BdTargetKey> keys, CancellationToken ct);
    Task<IReadOnlyList<BdDirectorAccount>> GetAccountsAsync(int take, CancellationToken ct);
    Task<IReadOnlyList<CallSheetItem>> GetCallSheetAsync(string actorUpn, DateTimeOffset nowUtc, CancellationToken ct);
    Task<IReadOnlyList<PendingBidRow>> ListPendingBidsAsync(CancellationToken ct);
    Task<int> CountRelationshipPursuitsAsync(CancellationToken ct);
}

public sealed record PendingBidRow(long EngagementId, long OpportunityId, string RfpNumber,
    string Project, string Client, string? OwnerStaffId, string? OwnerDisplayName, string? OwnerMailbox,
    DateTimeOffset? DueUtc, CrmEngagementStage Stage, DateTimeOffset? SubmittedAtUtc,
    string? SubmittedFileName, string? FolderLink, DateTimeOffset? NextActionDueUtc, long? SubmittedFileId = null);

public sealed record BdDirectorSignal(long OrgId, string Org, string Signal, string Subject, DateTimeOffset LastSeenAtUtc, int Corroborations);

public sealed record BdDirectorPerson(
    string PersonKey,
    string DisplayName,
    IReadOnlyList<string> OwnerKeys,
    int Live,
    int OverdueNextAction,
    int NoNextAction,
    int Untouched14d,
    int Activities30,
    int Activities90,
    int StageMoves90,
    int BuyersMailed90,
    int Opens30,
    int SecondSeat = 0);

public enum CallSheetKind { Pursuit, Ask, Account }

public sealed record CallSheetItem
{
    public CallSheetKind Kind { get; init; }
    public long? EngagementId { get; init; }
    public long? AskId { get; init; }
    public long? BuyerOrgId { get; init; }
    public string Buyer { get; init; } = "";
    public string? DeltekClientId { get; init; }
    public string? ContactName { get; init; }
    public string? ContactEmail { get; init; }
    public DateTimeOffset? LastTouchUtc { get; init; }
    public int Emails90d { get; init; }
    public string? SignalSubject { get; init; }
    public DateTimeOffset? SignalLastSeenAtUtc { get; init; }
    public DateTimeOffset? LastActivityUtc { get; init; }
    public string? NextActionNote { get; init; }
    public DateTimeOffset? NextActionDueUtc { get; init; }
    public string? AskText { get; init; }
    public DateTimeOffset? AskDueUtc { get; init; }
    public string? Region { get; init; }
    public string? Seat { get; init; }
    public string? Pursuit { get; init; }
}

public static class CallSheetOrdering
{
    // Gate 4 puts open asks before pursuits with no next action. Account ties
    // retain GetAccountsAsync's history ranking (LINQ's ordering is stable).
    public static IReadOnlyList<CallSheetItem> Order(IEnumerable<CallSheetItem> items, DateTimeOffset nowUtc) => items
        .OrderBy(item => Priority(item, nowUtc))
        .ThenBy(item => item.Kind == CallSheetKind.Ask ? item.AskDueUtc : item.NextActionDueUtc)
        .Take(12).ToArray();

    private static int Priority(CallSheetItem item, DateTimeOffset nowUtc)
    {
        if (item.Kind == CallSheetKind.Account) return 5;
        if (item.Kind == CallSheetKind.Ask) return 3;
        if (item.NextActionDueUtc is not { } due) return 4;
        var today = nowUtc.LocalDateTime.Date;
        return due.LocalDateTime.Date < today ? 0 : due.LocalDateTime.Date == today ? 1 : 2;
    }
}

public sealed record BdDirectorUndecidedPursuit(
    long EngagementId,
    string ProjectName,
    string Buyer,
    string? OwnerKey,
    DateTimeOffset Deadline);

public sealed record BdDirectorFunnel(
    IReadOnlyDictionary<int, double> MedianDaysInStage,
    IReadOnlyList<BdDirectorUndecidedPursuit> UndecidedPastDeadline,
    int UndecidedNoDeadline,
    int LostWithCompetitor,
    int LostWithReason,
    int LostTotal);

public sealed record BdTargetKey(string Metric, string Scope, string? ScopeKey, DateTimeOffset PeriodStartUtc, DateTimeOffset PeriodEndUtc);
public sealed record BdTargetActuals(IReadOnlyDictionary<BdTargetKey, decimal> Actuals, int Live, int LiveWithFee);
public sealed record BdDirectorAccount(long CanonicalOrgId, string DisplayName, string Kind, string ClendorClientId,
    int KorProjectsCount, bool CountIsRecordsNotWins, DateTime? LastKorProjectAtUtc,
    int LivePursuits, DateTimeOffset? LastTouchUtc, int OpenAsks);
