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
        var reference = row.LastActivityUtc ?? row.OpenedAtUtc;
        DaysSinceTouch = Math.Max(0, (int)(DateTimeOffset.Now - reference).TotalDays);
    }

    public PursuitOverwatchRow Row { get; }

    public long EngagementId => Row.EngagementId;
    public long? OpportunityId => Row.OpportunityId;
    public string OwnerDisplay => Row.OwnerStaffId;
    public string StageDisplay => ((CrmEngagementStage)Row.Stage).ToString();
    public string ProjectName => Row.ProjectName;
    public string Buyer => Row.Buyer;
    public string RegionDisplay => Row.Region ?? "";

    public int DaysSinceTouch { get; }

    /// <summary>"3d" / "41d" — age since last activity (or since opened if never touched).</summary>
    public string StalenessDisplay => $"{DaysSinceTouch}d";

    public string LastTouchDisplay => Row.LastActivityUtc.HasValue
        ? Row.LastActivityUtc.Value.LocalDateTime.ToString("yyyy-MM-dd")
        : "never";

    public string OpenedDisplay => Row.OpenedAtUtc.LocalDateTime.ToString("yyyy-MM-dd");

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
