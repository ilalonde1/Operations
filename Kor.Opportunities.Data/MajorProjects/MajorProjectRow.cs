#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

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
    string? ScheduleNotes,
    string? FootprintSource = null,
    string? FootprintRole = null,
    // Pursuit lifecycle (migration 284): human ownership + "not for us".
    string? OwnerStaffId = null,
    DateTimeOffset? OwnedAtUtc = null,
    DateTimeOffset? DismissedAtUtc = null,
    string? DismissedBy = null,
    string? DismissedReason = null)
{
    private static readonly CultureInfo CanadianCulture = CultureInfo.GetCultureInfo("en-CA");

    public string StageDisplay => PlainStage(FirstNonBlank(ProjectStage, Stage, ProjectStatus));

    // Source codes in words: the California CEQA notices, the camel-case stages, lower-case feed values.
    private static readonly Dictionary<string, string> StageWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CapitalPlan"] = "Capital plan",
        ["Announced-Funded"] = "Announced and funded",
        ["NOE"] = "CEQA exemption filed",
        ["NOD"] = "CEQA approval filed",
        ["NOP"] = "CEQA review starting",
        ["MND"] = "CEQA mitigated declaration",
        ["EIR"] = "CEQA impact report",
        ["filing"] = "Filed",
        ["triage"] = "New",
        ["reinstated"] = "Reinstated",
        ["on-hold"] = "On hold",
        ["in-procurement"] = "Procurement",
        ["DP"] = "Development permit",
    };

    public static string PlainStage(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return "";
        if (StageWords.TryGetValue(s, out var words)) return words;
        s = Regex.Replace(s, @"(?<=[a-z])(?=[A-Z])", " ");
        return char.ToUpper(s[0], CanadianCulture) + s[1..];
    }
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

    // Which quarterly issue of a major-projects inventory listed it. A project from any other source has none, and the
    // panel showed a bare "Q" for it (2026-10-22).
    public string IssueDisplay => IssueYear is null ? "" : IssueQuarter is null ? $"{IssueYear}" : $"{IssueYear} Q{IssueQuarter}";
    public bool HasIssue => IssueYear is not null;

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
