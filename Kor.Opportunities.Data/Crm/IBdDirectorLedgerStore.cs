#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.Crm;

public sealed record BdTarget(long Id, string Metric, string Scope, string? ScopeKey,
    DateTimeOffset PeriodStartUtc, DateTimeOffset PeriodEndUtc, decimal TargetValue, string? Note,
    string SetBy, DateTimeOffset SetAtUtc);
public sealed record BdAsk(long Id, string ToStaffKey, long? EngagementId, long? CanonicalOrgId,
    string Ask, string? WhyNow, int Minutes, DateTimeOffset DueUtc, string AskedBy,
    DateTimeOffset AskedAtUtc, string Status, DateTimeOffset? ClosedAtUtc, string? ClosedBy, string? ClosedNote);
public sealed record BdDecision(long Id, string Kind, long? EngagementId, long? CanonicalOrgId,
    string Decision, string Rationale, string ExpectedOutcome, DateTimeOffset ReviewAtUtc,
    string DecidedBy, DateTimeOffset DecidedAtUtc, string Outcome, string? OutcomeNote,
    DateTimeOffset? ScoredAtUtc, string? ScoredBy);

public static class BdTargetMetrics
{
    public const string LivePursuits = "LivePursuits", PipelineFee = "PipelineFee",
        AccountsWithLivePursuit = "AccountsWithLivePursuit", PursuitsOpened = "PursuitsOpened", BuyersTouched = "BuyersTouched";
    public static readonly IReadOnlyList<string> All = Array.AsReadOnly(new[]
        { LivePursuits, PipelineFee, AccountsWithLivePursuit, PursuitsOpened, BuyersTouched });
    public static readonly IReadOnlyList<string> PointInTime = Array.AsReadOnly(new[]
        { LivePursuits, PipelineFee, AccountsWithLivePursuit });
}

public interface IBdDirectorLedgerStore
{
    Task<IReadOnlyList<BdTarget>> ListTargetsAsync(CancellationToken ct);
    Task<long> SetTargetAsync(BdTarget target, CancellationToken ct);
    Task RetireTargetAsync(long id, string by, CancellationToken ct);
    Task<IReadOnlyList<BdAsk>> ListAsksAsync(string? status, CancellationToken ct);
    Task<IReadOnlyList<BdAsk>> ListOpenAsksForAsync(string toStaffKey, CancellationToken ct);
    Task<long> CreateAskAsync(BdAsk ask, CancellationToken ct);
    Task CloseAskAsync(long id, string status, string? note, string by, CancellationToken ct);
    Task<IReadOnlyList<BdDecision>> ListDecisionsAsync(CancellationToken ct);
    Task<long> RecordDecisionAsync(BdDecision decision, CancellationToken ct);
    Task ScoreDecisionAsync(long id, string outcome, string? note, string by, CancellationToken ct);
}
