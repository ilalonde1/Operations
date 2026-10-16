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
using Kor.Operations.App.Opportunities;
using Kor.Operations.App.Crm;

namespace Kor.Operations.App.BusinessDevelopment.Director;

public sealed class BdDirectorPersonRow
{
    public BdDirectorPersonRow(BdDirectorPerson person) => Person = person;
    public BdDirectorPerson Person { get; }
    public string PersonKey => Person.PersonKey;
    public string DisplayName => Person.DisplayName;
    public IReadOnlyList<string> OwnerKeys => Person.OwnerKeys;
    public int Live => Person.Live;
    public int SecondSeat => Person.SecondSeat;
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
    public string Stage => CrmStageNames.Display((CrmEngagementStage)Pursuit.Stage);
    public string LastActivity => Pursuit.LastActivityUtc is { } at ? BdDateDisplay.Day(at) : "never";
    public string Opened => BdDateDisplay.Day(Pursuit.OpenedAtUtc);
}

public sealed record BdDirectorPendingBidRow(PendingBidRow Bid, DateTimeOffset AsOf)
{
    public string Assignee => Bid.OwnerDisplayName ?? Bid.OwnerMailbox ?? Bid.OwnerStaffId ?? "Unassigned";
    public string Stage => CrmStageNames.Display(Bid.Stage);
    public bool IsPending => Bid.Stage == CrmEngagementStage.Drafting;
    public DateTimeOffset? SubmittedAtUtc => IsPending ? null : Bid.SubmittedAtUtc;
    public string Countdown => Bid.DueUtc is { } due ? OpportunityDeadlineDisplay.Countdown(due, AsOf) : "not specified";
    public bool IsUrgent => IsPending && Bid.DueUtc is { } due && due <= AsOf.AddHours(24);
    public bool HasFolderLink => !string.IsNullOrWhiteSpace(Bid.FolderLink);
    public bool HasSubmittedFile => Bid.SubmittedFileId.HasValue;
    public bool CanSubmit => IsPending || (Bid.Stage == CrmEngagementStage.Submitted && HasSubmittedFile);
    public bool IsMine(string actor) => !string.IsNullOrWhiteSpace(actor)
        && (string.Equals(Bid.OwnerMailbox, actor, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Bid.OwnerStaffId, actor, StringComparison.OrdinalIgnoreCase));
}

public sealed record BdEngineerCandidate(long EngagementId, BdStaffRow Staff, int Jobs, int SameRegionPursuits,
    int LivePursuits, string Reason)
{
    public string Display => $"{Staff.DisplayName ?? Staff.Mailbox} · {Reason}";
}

public static class BdMyDayRules
{
    public static bool IsLive(CrmEngagement e) => e.Stage is CrmEngagementStage.Drafting or CrmEngagementStage.Submitted;
    public static string RegionKey(string? region) => (region ?? "").ToLowerInvariant().Replace(" ", "").Split('-')[0];
    public static IReadOnlyList<string> Assigned(string? value) => (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
    public static BdStaffRow? Resolve(string? key, IReadOnlyList<BdStaffRow> staff) => string.IsNullOrWhiteSpace(key) ? null
        : staff.Where(s => !string.IsNullOrWhiteSpace(s.Mailbox)
            && (string.Equals(s.StaffKey, key.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(s.Mailbox, key.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(s => string.Equals(s.StaffKey, key.Trim(), StringComparison.OrdinalIgnoreCase))
            .ThenBy(s => s.Id).FirstOrDefault();
    public static bool IsActor(string? key, string actor, IReadOnlyList<BdStaffRow> staff) => !string.IsNullOrWhiteSpace(actor)
        && string.Equals(Resolve(key, staff)?.Mailbox, actor.Trim(), StringComparison.OrdinalIgnoreCase);
    public static bool HasCompanySeat(CrmEngagement e, IReadOnlyList<BdStaffRow> staff) => Assigned(e.AssignedStaffIds)
        .Any(key => IsCompanyMailbox(Resolve(key, staff)?.Mailbox));
    private static bool IsCompanyMailbox(string? mailbox) => mailbox?.EndsWith("@korstructural.com", StringComparison.OrdinalIgnoreCase) == true;
    public static string AppendSeat(string? assigned, string key)
    {
        var result = string.IsNullOrWhiteSpace(assigned) ? key : assigned.TrimEnd() + "," + key;
        if (result.Length > 500) throw new InvalidOperationException("There is no room for another assigned person on this pursuit.");
        return result;
    }
    public static string SeatDisplay(string? assigned, IReadOnlyList<BdStaffRow> staff) => string.Join(", ", Assigned(assigned)
        .Select(key => Resolve(key, staff)?.DisplayName ?? Resolve(key, staff)?.Mailbox ?? key));

    public static IReadOnlyList<BdEngineerCandidate> Candidates(CrmEngagement pursuit, string actor,
        IReadOnlyList<BdStaffRow> staff, IReadOnlyList<CrmEngagement> pursuits, DeltekClientIntelligence? history, string client)
    {
        if (!IsLive(pursuit) || !IsActor(pursuit.OwnerStaffId, actor, staff) || HasCompanySeat(pursuit, staff))
            return Array.Empty<BdEngineerCandidate>();
        var region = RegionKey(pursuit.Region);
        return staff.Where(s => s.IsActive && IsCompanyMailbox(s.Mailbox))
            .GroupBy(s => s.Mailbox!, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderBy(s => s.Id).First())
            .Where(s => !string.Equals(s.Mailbox, actor, StringComparison.OrdinalIgnoreCase)
                && !Assigned(pursuit.AssignedStaffIds).Any(key => IsActor(key, s.Mailbox!, staff)))
            .Select(s =>
            {
                var jobs = history?.KnownBy.Where(k => string.Equals(k.Email, s.Mailbox, StringComparison.OrdinalIgnoreCase))
                    .Sum(k => k.Jobs) ?? 0;
                var held = pursuits.Where(e => IsLive(e) && IsActor(e.OwnerStaffId, s.Mailbox!, staff)).ToArray();
                var sameRegion = region.Length == 0 ? 0 : held.Count(e => RegionKey(e.Region) == region);
                return new BdEngineerCandidate(pursuit.Id, s, jobs, sameRegion, held.Length,
                    jobs > 0 ? $"Led {jobs} jobs for {client}" : $"Holds {sameRegion} live pursuits in {pursuit.Region}");
            })
            .Where(c => c.Jobs > 0 || c.SameRegionPursuits > 0)
            .OrderByDescending(c => c.Jobs).ThenByDescending(c => c.SameRegionPursuits > 0)
            .ThenBy(c => c.LivePursuits).ThenBy(c => c.Staff.Mailbox, StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
    }

    public static string Draft(CallSheetItem item, DeltekClientIntelligence? history)
    {
        static string Fact(string? value) => (value ?? "").Replace("{", "").Replace("}", "").Trim();
        var sentences = new List<string>();
        var firstName = Fact(item.ContactName).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstName)) sentences.Add($"Hi {firstName},");
        var reason = Fact(!string.IsNullOrWhiteSpace(item.NextActionNote) ? item.NextActionNote : item.AskText);
        if (reason.Length > 0) sentences.Add(reason);
        if (history is { ProjectCount: > 0, LatestProjectStart: { } start }
            && Fact(history.ClientName).Length > 0 && Fact(history.LatestProjectName).Length > 0)
            sentences.Add($"KOR has worked with {Fact(history.ClientName)} on {history.ProjectCount} projects, most recently {Fact(history.LatestProjectName)} ({start.Year}).");
        var knownBy = Fact(history?.KnownBy.FirstOrDefault()?.Name);
        if (knownBy.Length > 0) sentences.Add($"{knownBy} led that work.");
        sentences.Add("Would you have twenty minutes in the next two weeks?");
        return string.Join(Environment.NewLine + Environment.NewLine, sentences);
    }
}

public sealed class BdDirectorMyDayRow : ObservableObject
{
    private CrmEngagement? _trackedPursuit;
    private string _trackedBy = "";
    public BdDirectorMyDayRow(CallSheetItem item, DateTimeOffset now, DeltekClientIntelligence? history,
        IReadOnlyList<BdEngineerCandidate>? candidates = null, string secondSeats = "")
    {
        Item = item;
        History = history;
        Candidates = candidates ?? Array.Empty<BdEngineerCandidate>();
        SecondSeats = secondSeats.Length == 0 ? "" : "Second seat: " + secondSeats;
        if (item.NextActionDueUtc is { } due)
        {
            WhyPrefix = due.LocalDateTime.Date < now.LocalDateTime.Date ? "Next action overdue since " : "Next action due ";
            WhyDate = due;
        }
        else if (item.Kind == CallSheetKind.Ask && item.AskDueUtc is { } askDue)
        {
            WhyPrefix = "Ask due "; WhyDate = askDue; WhySuffix = ": " + item.AskText;
        }
        else if (item.Kind == CallSheetKind.Pursuit) WhyPrefix = "No next action set";
        else if (item.LastTouchUtc is { } touch) { WhyPrefix = "No touch recorded since "; WhyDate = touch; }
        else WhyPrefix = "No touch recorded";
    }
    public CallSheetItem Item { get; }
    public DeltekClientIntelligence? History { get; }
    public IReadOnlyList<BdEngineerCandidate> Candidates { get; }
    public bool HasCandidates => Candidates.Count > 0;
    public string SecondSeats { get; }
    public string Contact => string.Join(" · ", new[] { Item.ContactName, Item.ContactEmail }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string WhyPrefix { get; }
    public DateTimeOffset? WhyDate { get; }
    public string WhySuffix { get; } = "";
    public bool HasWhyDate => WhyDate.HasValue;
    public bool HasSignal => Item.SignalLastSeenAtUtc.HasValue;
    public bool HasLastActivity => Item.LastActivityUtc.HasValue;
    public string HistoryText => string.IsNullOrWhiteSpace(Item.DeltekClientId) ? "Not linked to a Deltek client"
        : History is not { } h ? "Deltek history could not be read"
        : $"{h.ProjectCount} projects · {h.LifetimeFee.ToString("C0", CultureInfo.CurrentCulture)}"
            + (string.IsNullOrWhiteSpace(h.LatestProjectName) ? "" : $" · latest {h.LatestProjectName}" + (h.LatestProjectStart is { } date ? $" ({date.Year})" : ""));
    public string KnownBy => History?.KnownBy.FirstOrDefault() is { } k ? $"Known by {k.Name} ({k.Jobs} jobs)" : "";
    public string HistoryWarning => History?.HasDegradedSections == true ? "Some Deltek history could not be read." : "";
    public string DraftedNote => BdMyDayRules.Draft(Item, History);
    public bool CanLog => Item.Kind != CallSheetKind.Account && Item.EngagementId.HasValue;
    public bool IsAccount => Item.Kind == CallSheetKind.Account;
    public bool CanStartTracking => IsAccount && _trackedPursuit is null;
    public bool CanJoin => IsAccount && _trackedPursuit is not null;
    public CrmEngagement? TrackedPursuit => _trackedPursuit;
    public string TrackedBy => _trackedBy;
    public string? SelectedRegion { get; set; }
    public void ShowTracked(CrmEngagement pursuit, string owner)
    {
        _trackedPursuit = pursuit;
        _trackedBy = "Tracked by " + owner;
        foreach (var name in new[] { nameof(TrackedBy), nameof(CanJoin), nameof(CanStartTracking) }) OnPropertyChanged(name);
    }
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

public sealed record BdKnowledgeClaimRow(KnowledgeClaimRow Claim, ClaimVerdict? Verdict = null)
{
    public string Concepts => string.Join(" · ", Claim.Concepts);
    public string Status => Claim.RetiredAtUtc is not null ? "Retired" : Claim.ReviewedAtUtc is not null ? "Kept" : "Unreviewed";
    public bool CanReview => Claim.RetiredAtUtc is null;
    public string VerdictDisplay => $"{Verdict?.Wrong ?? 0} wrong · {Verdict?.Right ?? 0} right";
    public bool IsFlagged => Verdict is { Wrong: >= 2, Right: 0 };
    public string? RetirementReason => Verdict is { Wrong: >= 2, Right: 0 } verdict
        ? $"Cited by {verdict.Wrong} brief recommendations scored wrong: decisions {string.Join(", ", verdict.WrongDecisionIds)}"
        : null;
}

public sealed record BdKnowledgeConceptRow(KnowledgeConceptRow Concept)
{
    public string Display => $"{Concept.Name} ({Concept.Claims} claims across {Concept.Sources} sources)";
}

public sealed record BdDirectorCitationRow(DirectorCitation Citation, KnowledgeClaimRow? Current)
{
    public long PassageId => Current?.PassageId ?? Citation.PassageId;
    public string Display => $"({Current?.SourceTitle ?? Citation.Source}) {Current?.Statement ?? Citation.Statement}";
    public string Status => Current is null ? "Claim unavailable; showing the saved citation."
        : Current.RetiredAtUtc is not null ? "Retired · " + (Current.RetiredReason ?? "No reason recorded.") : "";
}
public sealed record BdDirectorBriefRow(DirectorBriefRow Brief, IReadOnlyList<BdDirectorCitationRow> Citations, BdDecision? Decision = null)
{
    public bool HasCall => Decision is not null;
    public bool IsCallPending => Decision?.Outcome == "Pending";
    public string CallStatus => Decision is null ? "" : "Call: " + Decision.Outcome;
    public DateTimeOffset Generated => Brief.ImportedAtUtc;
    public string Section => Brief.Section == "ThisWeek" ? "This week" : Brief.Section;
    public bool CanAcknowledge => Brief.AcknowledgedAtUtc is null;
    public string Acknowledgement => Brief.AcknowledgedAtUtc is null ? "" : "Acknowledged by " + Brief.AcknowledgedBy;
}

/// <summary>
/// One live pursuit on the Pursuits screen: what it is, who holds it, when it was last touched, and what is missing. A pursuit
/// is "going cold" after 14 days without a logged touch or email, the same line People draws for Untouched14d.
/// </summary>
public sealed record BdPursuitLine(PursuitOverwatchRow Pursuit, string OwnerName, DateTimeOffset Now)
{
    public long EngagementId => Pursuit.EngagementId;
    public string Title => Pursuit.ProjectName;
    public string StageText => CrmStageNames.Display((CrmEngagementStage)Pursuit.Stage);
    public DateTimeOffset? LastTouchUtc => new[] { Pursuit.LastActivityUtc, Pursuit.EmailLastTouchUtc }.Max();
    public string Line => string.Join(" · ", new[] { OwnerName, Pursuit.Buyer, Pursuit.Region }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string TouchText => LastTouchUtc is { } at ? "Last touch " + BdDateDisplay.Day(at) : "No touch logged since it opened " + BdDateDisplay.Day(Pursuit.OpenedAtUtc);
    public string Gap => LastTouchUtc switch
    {
        null when (Now - Pursuit.OpenedAtUtc).TotalDays >= 14 => "Never touched in " + ((int)(Now - Pursuit.OpenedAtUtc).TotalDays).ToString(CultureInfo.InvariantCulture) + " days",
        { } at when (Now - at).TotalDays >= 14 => "Going cold: no touch in " + ((int)(Now - at).TotalDays).ToString(CultureInfo.InvariantCulture) + " days",
        _ => "",
    };
}
