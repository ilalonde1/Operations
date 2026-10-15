#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Knowledge;
using Kor.Operations.Core;
using Kor.Operations.Services;

namespace Kor.Operations.App.BusinessDevelopment.Director;

public sealed class BdDirectorPersonRow
{
    public BdDirectorPersonRow(BdDirectorPerson person) => Person = person;
    public BdDirectorPerson Person { get; }
    public string PersonKey => Person.PersonKey;
    public string DisplayName => Person.DisplayName;
    public IReadOnlyList<string> OwnerKeys => Person.OwnerKeys;
    public int Live => Person.Live;
    public int OverdueNextAction => Person.OverdueNextAction;
    public int NoNextAction => Person.NoNextAction;
    public int Untouched14d => Person.Untouched14d;
    public int Activities30 => Person.Activities30;
    public int Activities90 => Person.Activities90;
    public int StageMoves90 => Person.StageMoves90;
    public int BuyersMailed90 => Person.BuyersMailed90;
    public int Opens30 => Person.Opens30;
}

public sealed class BdDirectorPursuitRow
{
    public BdDirectorPursuitRow(PursuitOverwatchRow pursuit) => Pursuit = pursuit;
    public PursuitOverwatchRow Pursuit { get; }
    public long PursuitId => Pursuit.EngagementId;
    public string ProjectName => Pursuit.ProjectName;
    public string Buyer => Pursuit.Buyer;
    public string Stage => ((CrmEngagementStage)Pursuit.Stage).ToString();
    public string LastActivity => Pursuit.LastActivityUtc is { } at ? BdDateDisplay.Day(at) : "never";
    public string Opened => BdDateDisplay.Day(Pursuit.OpenedAtUtc);
}

public sealed record BdDirectorStageRow(string Stage, int Pursuits, double? MedianDays)
{
    public string MedianDisplay => MedianDays?.ToString("0.0", CultureInfo.CurrentCulture) ?? "—";
}

public sealed record BdDirectorDeadlineRow(long PursuitId, string ProjectName, string Buyer, string Owner, DateTimeOffset Deadline);

public sealed record BdDirectorTargetRow(BdTarget Target, decimal Actual)
{
    public string? PersonDisplayName { get; init; }
    public string MetricDisplay => MetricName(Target.Metric);
    public string ScopeDisplay => Target.Scope == "Firm" ? "Firm" : $"{Target.Scope} · {PersonDisplayName ?? Target.ScopeKey}";
    public string TargetDisplay => FormatValue(Target.TargetValue);
    public string ActualDisplay => FormatValue(Actual);
    public string Progress => Target.TargetValue == 0 ? "—" : (Actual / Target.TargetValue).ToString("P0", CultureInfo.CurrentCulture);
    public bool IsPointInTime => BdTargetMetrics.PointInTime.Contains(Target.Metric);
    public string PeriodNote => IsPointInTime ? "Current snapshot; period does not filter the actual." : "Start included; end excluded.";
    private string FormatValue(decimal value) => value.ToString(Target.Metric == BdTargetMetrics.PipelineFee ? "C0" : "#,0.##", CultureInfo.CurrentCulture);
    public static string MetricName(string metric) => metric switch
    {
        BdTargetMetrics.LivePursuits => "Live pursuits",
        BdTargetMetrics.PipelineFee => "Pipeline fee",
        BdTargetMetrics.AccountsWithLivePursuit => "Accounts with a live pursuit",
        BdTargetMetrics.PursuitsOpened => "Pursuits opened",
        BdTargetMetrics.BuyersTouched => "Buyers touched",
        _ => "Unknown metric",
    };
}

public sealed record BdDirectorAccountRow(BdDirectorAccount Account)
{
    public string DisplayName => Account.DisplayName;
    public string Kind => Account.Kind;
    public string Projects => $"{Account.KorProjectsCount:N0}" + (Account.CountIsRecordsNotWins ? " records" : " wins");
    public string LastProject => Account.LastKorProjectAtUtc is { } date ? BdDateDisplay.Day(date) : "—";
    public string LastTouch => Account.LastTouchUtc is { } date ? BdDateDisplay.Day(date) : "never";
    public int LivePursuits => Account.LivePursuits;
    public int OpenAsks => Account.OpenAsks;
}

public sealed record BdDirectorAskRow(BdAsk Record, string ToDisplay, string About)
{
    public bool IsOpen => Record.Status == "Open";
}

public sealed record BdDirectorDecisionRow(BdDecision Record)
{
    public string Kind => Record.Kind == "NoGo" ? "No go" : Record.Kind;
    public bool IsPending => Record.Outcome == "Pending";
}

public sealed class BdKnowledgeSourceRow(KnowledgeSourceSummary source) : ObservableObject
{
    public KnowledgeSourceSummary Source { get; private set; } = source;
    public string Extracted => $"{Source.PassagesExtracted}/{Source.Passages}";
    public void Update(KnowledgeSourceSummary value)
    {
        Source = value;
        OnPropertyChanged(nameof(Source));
        OnPropertyChanged(nameof(Extracted));
    }
}

public sealed record BdKnowledgeClaimRow(KnowledgeClaimRow Claim)
{
    public string Concepts => string.Join(" · ", Claim.Concepts);
    public string Status => Claim.RetiredAtUtc is not null ? "Retired" : Claim.ReviewedAtUtc is not null ? "Kept" : "Unreviewed";
    public bool CanReview => Claim.RetiredAtUtc is null;
}

public sealed record BdKnowledgeConceptRow(KnowledgeConceptRow Concept)
{
    public string Display => $"{Concept.Name} ({Concept.Claims} claims across {Concept.Sources} sources)";
}

public sealed record BdDirectorCitationRow(DirectorCitationDto Citation, KnowledgeClaimRow? Current)
{
    public long PassageId => Current?.PassageId ?? Citation.PassageId;
    public string Display => $"({Current?.SourceTitle ?? Citation.Source}) {Current?.Statement ?? Citation.Statement}";
    public string Status => Current is null ? "Claim unavailable; showing the saved citation."
        : Current.RetiredAtUtc is not null ? "Retired · " + (Current.RetiredReason ?? "No reason recorded.") : "";
}
public sealed record BdDirectorBriefRow(DirectorBriefDto Brief, IReadOnlyList<BdDirectorCitationRow> Citations)
{
    public DateTimeOffset Generated => new(DateTime.SpecifyKind(Brief.GeneratedAt, DateTimeKind.Utc));
    public string Section => Brief.Section == "ThisWeek" ? "This week" : Brief.Section;
    public bool CanAcknowledge => Brief.AcknowledgedAt is null;
    public string Acknowledgement => Brief.AcknowledgedAt is null ? "" : "Acknowledged by " + Brief.AcknowledgedBy;
}
