#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.Crm;

/// <summary>Read-only facts not already exposed by the pursuit and attribution stores.</summary>
public interface IBdDirectorStore
{
    Task<IReadOnlyList<BdDirectorPerson>> GetPeopleAsync(CancellationToken ct);
    Task<BdDirectorFunnel> GetFunnelAsync(CancellationToken ct);
}

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
