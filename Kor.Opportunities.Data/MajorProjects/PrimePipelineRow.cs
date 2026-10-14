#nullable enable
using System;
using System.Globalization;
using System.Linq;

namespace Kor.Opportunities.Data.MajorProjects;

public sealed record PrimePipelineRow(
    string PipelineType,
    string SourceRef,
    string ProjectName,
    string? BuyerOrOwner,
    string? Sector,
    string? Stage,
    decimal? EstimatedValueCad,
    string? Province,
    string? City,
    string? ArchitectName,
    string? SourceUrl)
{
    private static readonly CultureInfo CanadianCulture = CultureInfo.GetCultureInfo("en-CA");

    // Original amount/units from migration 346; no exchange-rate assumption.
    public decimal? EstimatedValue { get; init; }
    public string? EstimatedValueCurrency { get; init; }
    public bool HasUnconvertedValue => (EstimatedValue ?? EstimatedValueCad).HasValue &&
        !string.Equals(EstimatedValueCurrency?.Trim(), "CAD", StringComparison.OrdinalIgnoreCase);

    public string CostDisplay => (EstimatedValue ?? EstimatedValueCad) is { } value
        ? string.Format(CanadianCulture, "{0:N0} {1}", value,
            string.IsNullOrWhiteSpace(EstimatedValueCurrency) ? "(currency unknown)" : EstimatedValueCurrency.Trim().ToUpperInvariant())
        : "";

    public string LocationDisplay => string.Join(", ", new[] { City, Province }
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Select(v => v!.Trim()));

    public bool HasSourceUrl => !string.IsNullOrWhiteSpace(SourceUrl);
    public bool HasNoSourceUrl => !HasSourceUrl;
}
