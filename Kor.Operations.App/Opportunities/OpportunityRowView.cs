#nullable enable
using System;
using System.Collections.Generic;
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

    public string SourceDisplay
    {
        get
        {
            var key = Model.OpportunityKey ?? string.Empty;
            var dashIdx = key.IndexOf('-');
            if (dashIdx <= 0)
            {
                return "Manual";
            }

            var prefix = key[..dashIdx].ToUpperInvariant();

            // All Bonfire tenants share a "BONFIRE" stem (BONFIREU/BONFIREK/BONFIREF/etc.).
            // Collapse them to "Bonfire" — the buyer column distinguishes the tenant.
            if (prefix.StartsWith("BONFIRE", StringComparison.Ordinal))
            {
                return "Bonfire";
            }

            return SourcePrefixDisplay.TryGetValue(prefix, out var friendly) ? friendly : prefix;
        }
    }

    public string Name => Model.Name;
    public string BuyerName => Model.BuyerName;
    public string Status => Model.Status.ToString();

    /// <summary>True when this opportunity is in the Bazaar's grabbable pool — still
    /// New and not yet owned. Surfaced as a badge so RFPs shows where it can be claimed.</summary>
    public bool IsAvailableInBazaar =>
        Model.Status == OpportunityStatus.New && string.IsNullOrWhiteSpace(Model.OwnerStaffId);

    public string Discipline => Model.Discipline == OpportunityDiscipline.Unknown ? "" : Model.Discipline.ToString();
    public string Location => string.Join(", ", new[] { Model.ProjectCity, Model.ProjectProvince }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string LocationDisplay
    {
        get
        {
            var city = Model.ProjectCity?.Trim();
            var province = Model.ProjectProvince?.Trim();
            if (string.IsNullOrEmpty(city) && string.IsNullOrEmpty(province)) return "";
            if (string.IsNullOrEmpty(city)) return province!;
            if (string.IsNullOrEmpty(province)) return city;
            return $"{city}, {province}";
        }
    }

    public string EstimatedValueDisplay =>
        Model.EstimatedValue.HasValue
            ? $"{Model.EstimatedValue.Value:C0} {Model.EstimatedValueCurrency}"
            : "";

    public string DeadlineDisplay =>
        Model.SubmissionDeadlineUtc.HasValue
            ? Model.SubmissionDeadlineUtc.Value.LocalDateTime.ToString("yyyy-MM-dd")
            : "";

    public string OwnerStaffId => Model.OwnerStaffId ?? "";

    public bool IsRepeatClient => !string.IsNullOrWhiteSpace(Model.DeltekClientId);

    public string UpdatedAtDisplay => Model.UpdatedAtUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    public string ScoreDisplay =>
        Model.RelevanceScore.HasValue ? Model.RelevanceScore.Value.ToString("0.#") : "";

    public string TierDisplay =>
        Model.RelevanceTier?.ToString() ?? "";

    public bool IsPrimeConsultantRfp => Model.IsPrimeConsultantRfp == true;
    public string PrimeProjectSector => Model.PrimeProjectSector ?? "";
    public string PrimeLikelyType => Model.PrimeLikelyType ?? "";
    public string PrimeConfidenceDisplay => Model.PrimeConfidence.HasValue ? Model.PrimeConfidence.Value.ToString("0.000") : "";
    public string KorFitDisplay
    {
        get
        {
            var sector = Model.PrimeKorSectorMatch == true;
            var location = Model.PrimeKorLocationMatch == true;
            return (sector, location) switch
            {
                (true, true) => "Sector + Location",
                (true, false) => "Sector",
                (false, true) => "Location",
                _ => "",
            };
        }
    }

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

    private static readonly Dictionary<string, string> SourcePrefixDisplay = new(StringComparer.OrdinalIgnoreCase)
    {
        // Keys are the 8-char-max alphanumeric prefix produced by
        // IngestionService.SourcePrefix() from OpportunitySource.Name.
        ["CIVICINF"] = "CivicInfo BC",
        ["CANADABU"] = "CanadaBuys",
        ["SAMGOV"] = "SAM.gov",
        ["BDALERTS"] = "BD Alerts (Email)",
        ["BCGOVNEW"] = "BC Gov News",
        ["MAN"] = "Manual",
    };

    private static Brush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
