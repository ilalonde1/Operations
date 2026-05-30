#nullable enable
using System.Windows.Media;
using Kor.Opportunities.Core.Models;

namespace Kor.Operations.App.Crm;

/// <summary>
/// Display-friendly projection of a <see cref="CrmEngagement"/>. Pairs the
/// engagement with the <see cref="Opportunity"/> it pursues so the master
/// list can show both buyer + name without a second query.
/// </summary>
public sealed class CrmEngagementRowView
{
    public CrmEngagementRowView(CrmEngagement engagement, Opportunity? opportunity)
    {
        Engagement = engagement;
        Opportunity = opportunity;
    }

    public CrmEngagement Engagement { get; }

    public Opportunity? Opportunity { get; }

    public long Id => Engagement.Id;
    public long? OpportunityId => Engagement.OpportunityId;
    public string StageDisplay => Engagement.Stage.ToString();
    public string OwnerDisplay => Engagement.OwnerStaffId ?? "—";

    /// <summary>True for engagements rooted in a formal Opportunity (RFP); false for BD-tracking touchpoints.</summary>
    public bool HasOpportunity => Engagement.OpportunityId.HasValue;

    public string OpportunityKey => Opportunity?.OpportunityKey
        ?? (Engagement.OpportunityId.HasValue ? $"Opp#{Engagement.OpportunityId.Value}" : "(BD-tracking)");
    public string ProjectName => Opportunity?.Name ?? (Engagement.PotentialProjects ?? "(no project named yet)");
    public string Buyer => Opportunity?.BuyerName ?? "";

    public string ProposedFeeDisplay =>
        Engagement.ProposedFee.HasValue ? $"{Engagement.ProposedFee.Value:C0}" : "";

    public string TargetMarginDisplay =>
        Engagement.TargetMargin.HasValue ? $"{Engagement.TargetMargin.Value:0.#}%" : "";

    public string OpenedDisplay => Engagement.OpenedAtUtc.LocalDateTime.ToString("yyyy-MM-dd");

    public string UpdatedDisplay => Engagement.UpdatedAtUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    public Brush StageBrush => Engagement.Stage switch
    {
        CrmEngagementStage.Won => StageBrushWon,
        CrmEngagementStage.Lost => StageBrushClosedNeg,
        CrmEngagementStage.Submitted => StageBrushHot,
        _ => StageBrushDefault,
    };

    private static readonly Brush StageBrushWon = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0x8B, 0x22)));
    private static readonly Brush StageBrushClosedNeg = Freeze(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)));
    private static readonly Brush StageBrushHot = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0xA8, 0x00)));
    private static readonly Brush StageBrushDefault = Freeze(new SolidColorBrush(Color.FromRgb(0x60, 0x9B, 0xD1)));

    private static Brush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
