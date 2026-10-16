#nullable enable
using System;
using System.Collections.Generic;
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
    int Opens30);

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
