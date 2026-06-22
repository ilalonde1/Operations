#nullable enable
using System;
using System.Globalization;

namespace Kor.Opportunities.Data.MajorProjects;

public sealed record MajorProjectRow(
    long Id,
    string Province,
    string ProjectName,
    string? Sector,
    string? SubSector,
    string? ConstructionType,
    string? ProjectType,
    string? ProjectCategoryName,
    string? Stage,
    string? ProjectStage,
    string? ProjectStatus,
    string? SeatStatus,
    decimal? EstimatedCostCad,
    string? EstimatedCostText,
    string? ProponentName,
    long? ProponentCanonicalOrgId,
    string? ArchitectName,
    long? ArchitectCanonicalOrgId,
    string? MunicipalityName,
    string? RegionName,
    short? StartYear,
    short? CompletionYear,
    string? StandardizedStartDate,
    string? StandardizedCompletionDate,
    bool? IndigenousInd,
    string? IndigenousNames,
    bool? PublicFundingInd,
    bool? ProvincialFunding,
    bool? FederalFunding,
    bool? MunicipalFunding,
    bool? GreenBuildingInd,
    int? ConstructionJobs,
    int? OperatingJobs,
    decimal? Latitude,
    decimal? Longitude,
    string? ProjectDescription,
    string? ProjectWebsite,
    string? SourceUrl,
    short? IssueYear,
    byte? IssueQuarter,
    DateTimeOffset LastSeenAtUtc,
    string? StructuralEngineerName,
    long? StructuralEngineerCanonicalOrgId,
    string? GeneralContractorName,
    long? GeneralContractorCanonicalOrgId,
    string? KorPipelineTag,
    string? ScheduleNotes)
{
    private static readonly CultureInfo CanadianCulture = CultureInfo.GetCultureInfo("en-CA");

    public string StageDisplay => FirstNonBlank(ProjectStage, Stage, ProjectStatus) ?? "";
    public string CostDisplay => EstimatedCostCad.HasValue
        ? string.Format(CanadianCulture, "{0:C0}", EstimatedCostCad.Value)
        : EstimatedCostText ?? "";
    public string LocationDisplay => JoinNonBlank(Province, RegionName, MunicipalityName);
    public string TimelineDisplay
    {
        get
        {
            var standardized = JoinRange(StandardizedStartDate, StandardizedCompletionDate);
            if (!string.IsNullOrWhiteSpace(standardized))
            {
                return standardized;
            }

            return JoinRange(
                StartYear?.ToString(CultureInfo.InvariantCulture),
                CompletionYear?.ToString(CultureInfo.InvariantCulture));
        }
    }

    public string FundingDisplay => JoinNonBlank(
        PublicFundingInd == true ? "Public" : null,
        ProvincialFunding == true ? "Provincial" : null,
        FederalFunding == true ? "Federal" : null,
        MunicipalFunding == true ? "Municipal" : null,
        GreenBuildingInd == true ? "Green" : null);

    public bool IsIndigenous => IndigenousInd == true;
    public bool HasFundingFlags => !string.IsNullOrWhiteSpace(FundingDisplay);
    public bool HasJobs => ConstructionJobs.HasValue || OperatingJobs.HasValue;
    public bool HasIndigenousNames => !string.IsNullOrWhiteSpace(IndigenousNames);
    public bool HasDescription => !string.IsNullOrWhiteSpace(ProjectDescription);
    public bool HasSourceUrl => !string.IsNullOrWhiteSpace(SourceUrl);
    public bool HasProjectWebsite => !string.IsNullOrWhiteSpace(ProjectWebsite);
    public bool HasTimeline => !string.IsNullOrWhiteSpace(TimelineDisplay);

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string JoinNonBlank(params string?[] values)
        => string.Join(", ", values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()));

    private static string JoinRange(string? start, string? finish)
    {
        if (string.IsNullOrWhiteSpace(start) && string.IsNullOrWhiteSpace(finish))
        {
            return "";
        }

        return $"{(string.IsNullOrWhiteSpace(start) ? "unknown" : start.Trim())} - {(string.IsNullOrWhiteSpace(finish) ? "unknown" : finish.Trim())}";
    }
}
