#nullable enable
using System;
using System.Windows.Media;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

/// <summary>
/// Display projection of a <see cref="PursuitOverwatchRow"/> for the manager
/// board: turns the raw staleness signal into a day-count + colour so a
/// manager can scan what is going cold at a glance.
/// </summary>
public sealed class OverwatchRowView
{
    // Staleness thresholds (days since last touch). Below Watch = fresh (green),
    // Watch..Cold = amber, at/over Cold = red.
    private const int WatchDays = 7;
    private const int ColdDays = 21;

    public OverwatchRowView(PursuitOverwatchRow row)
    {
        Row = row;

        // Plan 3.1 fusion: the staleness reference is the NEWEST of logged
        // activity and filed email correspondence (nightly warmth rollup),
        // source-labelled so a manager knows what kind of touch it was.
        DateTimeOffset reference;
        if (row.EmailLastTouchUtc is { } email && (row.LastActivityUtc is not { } act || email > act))
        {
            reference = email;
            TouchSource = "email";
        }
        else if (row.LastActivityUtc is { } activity)
        {
            reference = activity;
            TouchSource = "activity";
        }
        else
        {
            reference = row.OpenedAtUtc;
            TouchSource = "none";
        }

        LastTouchUtc = reference;
        // Staleness badge is PURSUIT-scoped: floored at OpenedAtUtc so a
        // relationship's old email history can't make a fresh pursuit read as
        // 600d cold (review fix). The Last-touch column keeps the TRUE last
        // touch + source — relationship-scoped by design.
        var staleReference = reference < row.OpenedAtUtc ? row.OpenedAtUtc : reference;
        DaysSinceTouch = Math.Max(0, (int)(DateTimeOffset.Now - staleReference).TotalDays);
        StageAgeDays = Math.Max(0, (int)(DateTimeOffset.Now - row.StageSinceUtc).TotalDays);
    }

    public PursuitOverwatchRow Row { get; }

    public long EngagementId => Row.EngagementId;
    public long? OpportunityId => Row.OpportunityId;
    /// <summary>The person's name from the BD staff directory, never the address (2026-10-14, names 2026-10-21).</summary>
    public string OwnerDisplay => StaffDisplay.Short(Row.OwnerStaffId);
    /// <summary>The same words as My Pursuits and BD Director: "Drafting" was the enum's name, not anyone's (2026-10-21).</summary>
    public string StageDisplay => (CrmEngagementStage)Row.Stage == CrmEngagementStage.Drafting
        ? (Row.OpportunityId.HasValue && Row.DeadlineUtc is { } closes
            ? (closes < DateTimeOffset.Now ? "Closed, not submitted" : "Out for bid")
            : "No RFP yet")
        : CrmStageNames.Display((CrmEngagementStage)Row.Stage);
    public string ProjectName => Row.ProjectName == "(unnamed)" ? "No project named yet" : Row.ProjectName;
    public string Buyer => Row.Buyer;
    public string RegionDisplay => Row.Region ?? "";

    public int DaysSinceTouch { get; }

    /// <summary>Which signal produced the staleness reference: "activity", "email", or "none".</summary>
    public string TouchSource { get; }

    /// <summary>Days in the CURRENT stage (plan 2.2a; stage history with OpenedAtUtc fallback).</summary>
    public int StageAgeDays { get; }

    /// <summary>"Out for bid · 34 days": the stage plus how long it has sat there.</summary>
    public string StageAgeDisplay => $"{StageDisplay} · {StageAgeDays} day{(StageAgeDays == 1 ? "" : "s")}";

    private DateTimeOffset LastTouchUtc { get; }

    /// <summary>"3d" / "41d" — age since last touch (activity or filed email;
    /// since opened if neither exists).</summary>
    public string StalenessDisplay => $"{DaysSinceTouch}d";

    public string LastTouchDisplay => TouchSource == "none"
        ? "never"
        : $"{BdDateDisplay.Day(LastTouchUtc)} ({TouchSource})";

    public string OpenedDisplay => BdDateDisplay.Day(Row.OpenedAtUtc);

    public string ActivityCountDisplay => Row.ActivityCount.ToString();

    /// <summary>Green (fresh) → amber (watch) → red (cold), by days since last touch.</summary>
    public Brush StalenessBrush => DaysSinceTouch switch
    {
        < WatchDays => BrushFresh,
        < ColdDays => BrushWatch,
        _ => BrushCold,
    };

    private static readonly Brush BrushFresh = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0x8B, 0x22))); // green
    private static readonly Brush BrushWatch = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0xA8, 0x00))); // amber
    private static readonly Brush BrushCold = Freeze(new SolidColorBrush(Color.FromRgb(0xC1, 0x1E, 0x1E)));  // red

    private static Brush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
