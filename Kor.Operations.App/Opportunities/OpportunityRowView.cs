#nullable enable
using System;
using System.Linq;
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
}
