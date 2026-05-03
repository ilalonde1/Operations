#nullable enable
using System;
using System.Linq;
using System.Windows.Media;
using Kor.Opportunities.Core.Models;

namespace Kor.Operations.App.Opportunities;

/// <summary>
/// Flat row view used by the Opportunities DataGrid. Wraps the immutable
/// <see cref="Opportunity"/> record with display-friendly accessors so the
/// XAML can bind without converters for every field.
/// </summary>
public sealed class OpportunityRowView
{
    public OpportunityRowView(Opportunity model)
    {
        Model = model;
    }

    /// <summary>The underlying immutable record. The grid never mutates this;
    /// edits happen via the entry dialog or status-change command, which call
    /// the store and return a fresh record.</summary>
    public Opportunity Model { get; }

    public long Id => Model.Id;
    public string OpportunityKey => Model.OpportunityKey;
    public string Name => Model.Name;
    public string BuyerName => Model.BuyerName;
    public string Status => Model.Status.ToString();
    public string Discipline => Model.Discipline == OpportunityDiscipline.Unknown ? "" : Model.Discipline.ToString();
    public string Location => string.Join(", ", new[] { Model.ProjectCity, Model.ProjectProvince }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string EstimatedValueDisplay =>
        Model.EstimatedValue.HasValue
            ? $"{Model.EstimatedValue.Value:C0} {Model.EstimatedValueCurrency}"
            : "";

    public string DeadlineDisplay =>
        Model.SubmissionDeadlineUtc.HasValue
            ? Model.SubmissionDeadlineUtc.Value.LocalDateTime.ToString("yyyy-MM-dd")
            : "";

    public string OwnerStaffId => Model.OwnerStaffId ?? "";

    public string UpdatedAtDisplay => Model.UpdatedAtUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    public string ScoreDisplay =>
        Model.RelevanceScore.HasValue ? Model.RelevanceScore.Value.ToString("0.#") : "";

    public string TierDisplay =>
        Model.RelevanceTier?.ToString() ?? "";

    /// <summary>Frozen so the row can be created off the UI thread (e.g. inside
    /// LoadAsync) and still bind safely.</summary>
    public Brush TierBrush => Model.RelevanceTier switch
    {
        RelevanceTier.High => TierBrushHigh,
        RelevanceTier.Medium => TierBrushMedium,
        RelevanceTier.Low => TierBrushLow,
        RelevanceTier.HardReject => TierBrushReject,
        _ => TierBrushNone,
    };

    private static readonly Brush TierBrushHigh = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0x8B, 0x22))); // green
    private static readonly Brush TierBrushMedium = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0xA8, 0x00))); // amber
    private static readonly Brush TierBrushLow = Freeze(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80))); // grey
    private static readonly Brush TierBrushReject = Freeze(new SolidColorBrush(Color.FromRgb(0xC1, 0x1E, 0x1E))); // red
    private static readonly Brush TierBrushNone = Freeze(new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD2))); // light grey

    private static Brush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
