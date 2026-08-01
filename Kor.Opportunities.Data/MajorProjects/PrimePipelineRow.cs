#nullable enable
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

    public string CostDisplay => EstimatedValueCad.HasValue
        ? string.Format(CanadianCulture, "{0:C0}", EstimatedValueCad.Value)
        : "";

    public string LocationDisplay => string.Join(", ", new[] { City, Province }
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Select(v => v!.Trim()));

    public bool HasSourceUrl => !string.IsNullOrWhiteSpace(SourceUrl);
    public bool HasNoSourceUrl => !HasSourceUrl;
}
