#nullable enable
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.MajorProjects;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kor.BcMpiImporter;

internal static class Program
{
    private const string Province = "BC";
    private const string DefaultDirectory = @"C:\VIsual Studio Projects\KOR-BC-MPI-Archive\downloads";
    private const string SourceLandingPage = "https://www2.gov.bc.ca/gov/content/employment-business/economic-development/industry/bc-major-projects-inventory";
    private static readonly Regex YearRegex = new(@"\b(19|20)\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex FilenameYearQuarter1 = new(@"(?<year>20\d{2}|19\d{2}).*?[Qq](?<quarter>[1-4])", RegexOptions.Compiled);
    private static readonly Regex FilenameYearQuarter2 = new(@"[Qq](?<quarter>[1-4]).*?(?<year>20\d{2}|19\d{2})", RegexOptions.Compiled);

    private static async Task<int> Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var options = ToolOptions.Load(config, args);
        if (string.IsNullOrWhiteSpace(options.OpportunitiesDb))
        {
            Console.Error.WriteLine("Missing connection string. Set KOR_OPPORTUNITIES_OPPORTUNITIESDB, appsettings:OpportunitiesDb, or --db.");
            return 2;
        }

        if (!Directory.Exists(options.InputDirectory))
        {
            Console.Error.WriteLine($"Input directory not found: {options.InputDirectory}");
            return 2;
        }

        var files = Directory.GetFiles(options.InputDirectory, "*.xlsx")
            .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal))
            .Select(f => new ImportFile(f, ParseIssueFromFilename(Path.GetFileNameWithoutExtension(f))))
            .OrderBy(f => f.Issue.Score)
            .ThenBy(f => Path.GetFileName(f.Path), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            Console.WriteLine($"No .xlsx files found in {options.InputDirectory}");
            return 0;
        }

        var resolver = new CanonicalOrgResolver(
            new SqlCanonicalOrgStore(options.OpportunitiesDb),
            NullLogger<CanonicalOrgResolver>.Instance);
        var stats = new ImportStats();
        var sw = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        Console.WriteLine($"BC MPI import starting: files={files.Count}; dir={options.InputDirectory}");
        foreach (var file in files)
        {
            cts.Token.ThrowIfCancellationRequested();
            await ProcessWorkbookAsync(file, options.OpportunitiesDb, resolver, stats, cts.Token).ConfigureAwait(false);
        }

        sw.Stop();
        Console.WriteLine(
            $"BC MPI import complete: files={stats.FilesProcessed}/{files.Count}, rows={stats.RowsProcessed}, uniqueProjects={stats.UniqueProjects.Count}, upserted={stats.Upserted}, skipped={stats.Skipped}, architectsResolved={stats.ArchitectResolved}, indigenousFlagged={stats.IndigenousFlagged}, elapsed={sw.Elapsed}.");
        return 0;
    }

    private static async Task ProcessWorkbookAsync(
        ImportFile file,
        string connectionString,
        CanonicalOrgResolver resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var fileName = Path.GetFileName(file.Path);
        var fileRows = 0;
        var fileUpserted = 0;
        var fileSkipped = 0;

        using var workbook = new XLWorkbook(file.Path);
        if (!TryFindDataSheet(workbook, out var sheet, out var headerRow, out var headers))
        {
            stats.Skipped++;
            Console.WriteLine($"[{fileName}] skipped: no MPI data sheet with PROJECT_NAME and ESTIMATED_COST.");
            return;
        }

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow;
        for (var rowNumber = headerRow + 1; rowNumber <= lastRow; rowNumber++)
        {
            ct.ThrowIfCancellationRequested();
            var row = sheet.Row(rowNumber);
            if (IsEmptyRow(row, headers.Values))
            {
                continue;
            }

            var record = await MapRowAsync(row, headers, file.Issue, fileName, resolver, ct).ConfigureAwait(false);
            if (record is null)
            {
                stats.Skipped++;
                fileSkipped++;
                continue;
            }

            fileRows++;
            stats.RowsProcessed++;
            stats.UniqueProjects.Add(record.SourceKey);
            if (record.ArchitectCanonicalOrgId.HasValue)
            {
                stats.ArchitectResolved++;
            }

            if (record.IndigenousInd == true)
            {
                stats.IndigenousFlagged++;
            }

            await UpsertAsync(connectionString, record, ct).ConfigureAwait(false);
            stats.Upserted++;
            fileUpserted++;
        }

        stats.FilesProcessed++;
        Console.WriteLine($"[{fileName}] {fileRows} rows - {fileUpserted} upserted, {fileSkipped} skipped");
    }

    private static async Task<BcMpiRecord?> MapRowAsync(
        IXLRow row,
        IReadOnlyDictionary<string, int> headers,
        IssueInfo issue,
        string fileName,
        CanonicalOrgResolver resolver,
        CancellationToken ct)
    {
        var projectName = Read(row, headers, "PROJECT_NAME", "Project Name", "Name");
        var costText = Read(row, headers, "ESTIMATED_COST", "Estimated Cost", "Cost");
        if (string.IsNullOrWhiteSpace(projectName) || string.IsNullOrWhiteSpace(costText))
        {
            return null;
        }

        var externalProjectId = Read(row, headers, "PROJECT_ID", "Project Id");
        var municipality = Read(row, headers, "MUNICIPALITY", "Municipality");
        var developer = Read(row, headers, "DEVELOPER", "Developer", "Owner", "Proponent");
        var architect = Read(row, headers, "ARCHITECT", "Architect");
        var standardizedStart = Read(row, headers, "STANDARDIZED_START_DATE", "Standardized Start Date");
        var standardizedCompletion = Read(row, headers, "STANDARDIZED_COMPLETION_DATE", "Standardized Completion Date");
        var proponentId = !string.IsNullOrWhiteSpace(developer)
            ? await resolver.ResolveAsync(
                developer,
                OrgKinds.Unknown,
                "MajorProjectsInventory.Proponent",
                ct,
                allowCreate: true,
                minConfidenceForCreate: 70).ConfigureAwait(false)
            : null;
        var architectId = !string.IsNullOrWhiteSpace(architect)
            ? await resolver.ResolveAsync(
                architect,
                OrgKinds.Unknown,
                "MajorProjectsInventory.Architect",
                ct,
                allowCreate: true,
                minConfidenceForCreate: 70).ConfigureAwait(false)
            : null;

        return new BcMpiRecord(
            SourceKey: !string.IsNullOrWhiteSpace(externalProjectId) ? externalProjectId.Trim() : BuildFallbackSourceKey(projectName, municipality),
            ExternalProjectId: NullIfBlank(externalProjectId),
            ProjectName: projectName.Trim(),
            ProjectDescription: Read(row, headers, "PROJECT_DESCRIPTION", "Project Description"),
            EstimatedCostCad: ParseBcCostMillions(costText),
            EstimatedCostText: costText,
            Sector: Read(row, headers, "CONSTRUCTION_TYPE", "Construction Type"),
            SubSector: Read(row, headers, "CONSTRUCTION_SUBTYPE", "Construction Subtype"),
            ConstructionType: Read(row, headers, "CONSTRUCTION_TYPE", "Construction Type"),
            ConstructionSubtype: Read(row, headers, "CONSTRUCTION_SUBTYPE", "Construction Subtype"),
            ProjectType: Read(row, headers, "PROJECT_TYPE", "Project Type"),
            RegionName: Read(row, headers, "REGION", "Region"),
            MunicipalityName: municipality,
            ProponentName: developer,
            ProponentCanonicalOrgId: proponentId,
            ArchitectName: architect,
            ArchitectCanonicalOrgId: architectId,
            Stage: Read(row, headers, "PROJECT_STATUS", "Project Status"),
            ProjectStatus: Read(row, headers, "PROJECT_STATUS", "Project Status"),
            ProjectStage: Read(row, headers, "PROJECT_STAGE", "Project Stage"),
            ProjectCategoryName: Read(row, headers, "PROJECT_CATEGORY_NAME", "Project Category Name"),
            PublicFundingInd: ParseBool(Read(row, headers, "PUBLIC_FUNDING_IND", "Public Funding Ind")),
            ProvincialFunding: ParseBool(Read(row, headers, "PROVINCIAL_FUNDING", "Provincial Funding")),
            FederalFunding: ParseBool(Read(row, headers, "FEDERAL_FUNDING", "Federal Funding")),
            MunicipalFunding: ParseBool(Read(row, headers, "MUNICIPAL_FUNDING", "Municipal Funding")),
            OtherPublicFunding: ParseBool(Read(row, headers, "OTHER_PUBLIC_FUNDING", "Other Public Funding")),
            GreenBuildingInd: ParseBool(Read(row, headers, "GREEN_BUILDING_IND", "Green Building Ind")),
            IndigenousInd: ParseBool(Read(row, headers, "INDIGENOUS_IND", "Indigenous Ind")),
            IndigenousNames: Read(row, headers, "INDIGENOUS_NAMES", "Indigenous Names"),
            ConstructionJobs: ParseInt(Read(row, headers, "CONSTRUCTION_JOBS", "Construction Jobs")),
            OperatingJobs: ParseInt(Read(row, headers, "OPERATING_JOBS", "Operating Jobs")),
            StandardizedStartDate: standardizedStart,
            StandardizedCompletionDate: standardizedCompletion,
            StartYear: ParseLeadingYear(standardizedStart),
            CompletionYear: ParseLeadingYear(standardizedCompletion),
            ScheduleNotes: BuildScheduleNotes(standardizedStart, standardizedCompletion),
            Latitude: ParseDecimal(Read(row, headers, "LATITUDE", "Latitude")),
            Longitude: ParseDecimal(Read(row, headers, "LONGITUDE", "Longitude")),
            ProjectWebsite: Read(row, headers, "PROJECT_WEBSITE", "Project Website", "Website"),
            SourceUrl: SourceLandingPage,
            IssueYear: issue.Year,
            IssueQuarter: issue.Quarter,
            RawJson: BuildRawJson(row, headers, fileName));
    }

    private static async Task UpsertAsync(string connectionString, BcMpiRecord r, CancellationToken ct)
    {
        const string sql = @"
SET XACT_ABORT ON;

DECLARE @inserted table (Id bigint NOT NULL);
DECLARE @incomingIssueScore int = COALESCE(CONVERT(int, @issueYear) * 4 + CONVERT(int, @issueQuarter), 0);
DECLARE @existingIssueScore int;

BEGIN TRAN;

UPDATE opportunities.MajorProjectsInventory WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
SET
    LastSeenAtUtc = sysdatetimeoffset(),
    UpdatedAtUtc = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN sysdatetimeoffset() ELSE UpdatedAtUtc END,
    ExternalProjectId = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @externalProjectId ELSE ExternalProjectId END,
    ProjectName = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @projectName ELSE ProjectName END,
    ProjectDescription = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @projectDescription ELSE ProjectDescription END,
    EstimatedCostCad = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @estimatedCostCad ELSE EstimatedCostCad END,
    EstimatedCostText = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @estimatedCostText ELSE EstimatedCostText END,
    Sector = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @sector ELSE Sector END,
    SubSector = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @subSector ELSE SubSector END,
    ConstructionType = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @constructionType ELSE ConstructionType END,
    ConstructionSubtype = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @constructionSubtype ELSE ConstructionSubtype END,
    ProjectType = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @projectType ELSE ProjectType END,
    RegionName = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @regionName ELSE RegionName END,
    MunicipalityName = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @municipalityName ELSE MunicipalityName END,
    ProponentName = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @proponentName ELSE ProponentName END,
    ProponentCanonicalOrgId = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @proponentCanonicalOrgId ELSE ProponentCanonicalOrgId END,
    ArchitectName = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @architectName ELSE ArchitectName END,
    ArchitectCanonicalOrgId = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @architectCanonicalOrgId ELSE ArchitectCanonicalOrgId END,
    Stage = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @stage ELSE Stage END,
    ProjectStatus = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @projectStatus ELSE ProjectStatus END,
    ProjectStage = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @projectStage ELSE ProjectStage END,
    KorPipelineTag = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @korPipelineTag ELSE KorPipelineTag END,
    ProjectCategoryName = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @projectCategoryName ELSE ProjectCategoryName END,
    PublicFundingInd = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @publicFundingInd ELSE PublicFundingInd END,
    ProvincialFunding = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @provincialFunding ELSE ProvincialFunding END,
    FederalFunding = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @federalFunding ELSE FederalFunding END,
    MunicipalFunding = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @municipalFunding ELSE MunicipalFunding END,
    OtherPublicFunding = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @otherPublicFunding ELSE OtherPublicFunding END,
    GreenBuildingInd = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @greenBuildingInd ELSE GreenBuildingInd END,
    IndigenousInd = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @indigenousInd ELSE IndigenousInd END,
    IndigenousNames = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @indigenousNames ELSE IndigenousNames END,
    ConstructionJobs = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @constructionJobs ELSE ConstructionJobs END,
    OperatingJobs = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @operatingJobs ELSE OperatingJobs END,
    StandardizedStartDate = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @standardizedStartDate ELSE StandardizedStartDate END,
    StandardizedCompletionDate = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @standardizedCompletionDate ELSE StandardizedCompletionDate END,
    StartYear = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @startYear ELSE StartYear END,
    CompletionYear = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @completionYear ELSE CompletionYear END,
    ScheduleNotes = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @scheduleNotes ELSE ScheduleNotes END,
    Latitude = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @latitude ELSE Latitude END,
    Longitude = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @longitude ELSE Longitude END,
    ProjectWebsite = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @projectWebsite ELSE ProjectWebsite END,
    SourceUrl = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @sourceUrl ELSE SourceUrl END,
    IssueYear = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @issueYear ELSE IssueYear END,
    IssueQuarter = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @issueQuarter ELSE IssueQuarter END,
    RawJson = CASE WHEN @incomingIssueScore > COALESCE(IssueYear * 4 + IssueQuarter, -1) THEN @rawJson ELSE RawJson END
WHERE Province = @province
  AND SourceKey = @sourceKey;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO opportunities.MajorProjectsInventory
        (Province, SourceKey, ExternalProjectId, ProjectName, ProjectDescription, EstimatedCostCad,
         EstimatedCostText, Sector, SubSector, ConstructionType, ConstructionSubtype, ProjectType,
         RegionName, MunicipalityName, ProponentName, ProponentCanonicalOrgId, ArchitectName,
         ArchitectCanonicalOrgId, Stage, ProjectStatus, ProjectStage, KorPipelineTag, ProjectCategoryName,
         PublicFundingInd, ProvincialFunding, FederalFunding, MunicipalFunding, OtherPublicFunding,
         GreenBuildingInd, IndigenousInd, IndigenousNames, ConstructionJobs, OperatingJobs,
         StandardizedStartDate, StandardizedCompletionDate, StartYear, CompletionYear,
         ScheduleNotes, Latitude, Longitude, ProjectWebsite, SourceUrl, IssueYear,
         IssueQuarter, RawJson)
    OUTPUT inserted.Id INTO @inserted
    VALUES
        (@province, @sourceKey, @externalProjectId, @projectName, @projectDescription, @estimatedCostCad,
         @estimatedCostText, @sector, @subSector, @constructionType, @constructionSubtype, @projectType,
         @regionName, @municipalityName, @proponentName, @proponentCanonicalOrgId, @architectName,
         @architectCanonicalOrgId, @stage, @projectStatus, @projectStage, @korPipelineTag, @projectCategoryName,
         @publicFundingInd, @provincialFunding, @federalFunding, @municipalFunding, @otherPublicFunding,
         @greenBuildingInd, @indigenousInd, @indigenousNames, @constructionJobs, @operatingJobs,
         @standardizedStartDate, @standardizedCompletionDate, @startYear, @completionYear,
         @scheduleNotes, @latitude, @longitude, @projectWebsite, @sourceUrl, @issueYear,
         @issueQuarter, @rawJson);
END;

COMMIT TRAN;

SELECT CASE WHEN EXISTS (SELECT 1 FROM @inserted) THEN 1 ELSE 0 END;";

        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 60 };
        AddParams(cmd, r);
        await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }

    private static void AddParams(SqlCommand cmd, BcMpiRecord r)
    {
        AddString(cmd, "@province", ProvinceNormalizer.Normalize(Province, Province), 2);
        AddString(cmd, "@sourceKey", r.SourceKey, 200);
        AddString(cmd, "@externalProjectId", r.ExternalProjectId, 50);
        AddString(cmd, "@projectName", r.ProjectName, 500);
        AddString(cmd, "@projectDescription", r.ProjectDescription, -1);
        AddDecimal(cmd, "@estimatedCostCad", r.EstimatedCostCad, 18, 0);
        AddString(cmd, "@estimatedCostText", r.EstimatedCostText, 200);
        AddString(cmd, "@sector", r.Sector, 100);
        AddString(cmd, "@subSector", r.SubSector, 100);
        AddString(cmd, "@constructionType", r.ConstructionType, 100);
        AddString(cmd, "@constructionSubtype", r.ConstructionSubtype, 100);
        AddString(cmd, "@projectType", r.ProjectType, 100);
        AddString(cmd, "@regionName", r.RegionName, 200);
        AddString(cmd, "@municipalityName", r.MunicipalityName, 200);
        AddString(cmd, "@proponentName", r.ProponentName, 500);
        AddLong(cmd, "@proponentCanonicalOrgId", r.ProponentCanonicalOrgId);
        AddString(cmd, "@architectName", r.ArchitectName, 500);
        AddLong(cmd, "@architectCanonicalOrgId", r.ArchitectCanonicalOrgId);
        AddString(cmd, "@stage", r.Stage, 50);
        AddString(cmd, "@projectStatus", r.ProjectStatus, 100);
        ProjectStageRouter.Route(r.ProjectStage, out var routedStage, out var routedTag, out _);
        AddString(cmd, "@projectStage", routedStage, 100);
        AddString(cmd, "@korPipelineTag", routedTag, 80);
        AddString(cmd, "@projectCategoryName", r.ProjectCategoryName, 200);
        AddBool(cmd, "@publicFundingInd", r.PublicFundingInd);
        AddBool(cmd, "@provincialFunding", r.ProvincialFunding);
        AddBool(cmd, "@federalFunding", r.FederalFunding);
        AddBool(cmd, "@municipalFunding", r.MunicipalFunding);
        AddBool(cmd, "@otherPublicFunding", r.OtherPublicFunding);
        AddBool(cmd, "@greenBuildingInd", r.GreenBuildingInd);
        AddBool(cmd, "@indigenousInd", r.IndigenousInd);
        AddString(cmd, "@indigenousNames", r.IndigenousNames, 500);
        AddInt(cmd, "@constructionJobs", r.ConstructionJobs);
        AddInt(cmd, "@operatingJobs", r.OperatingJobs);
        AddString(cmd, "@standardizedStartDate", r.StandardizedStartDate, 20);
        AddString(cmd, "@standardizedCompletionDate", r.StandardizedCompletionDate, 20);
        AddShort(cmd, "@startYear", r.StartYear);
        AddShort(cmd, "@completionYear", r.CompletionYear);
        AddString(cmd, "@scheduleNotes", r.ScheduleNotes, 1000);
        AddDecimal(cmd, "@latitude", r.Latitude, 9, 6);
        AddDecimal(cmd, "@longitude", r.Longitude, 9, 6);
        AddString(cmd, "@projectWebsite", r.ProjectWebsite, 1000);
        AddString(cmd, "@sourceUrl", r.SourceUrl, 1000);
        AddShort(cmd, "@issueYear", r.IssueYear);
        AddByte(cmd, "@issueQuarter", r.IssueQuarter);
        AddString(cmd, "@rawJson", r.RawJson, -1);
    }

    private static bool TryFindDataSheet(XLWorkbook workbook, out IXLWorksheet sheet, out int headerRow, out Dictionary<string, int> headers)
    {
        foreach (var candidate in workbook.Worksheets.Where(s => s.Name.StartsWith("mpi_dataset", StringComparison.OrdinalIgnoreCase)))
        {
            if (TryFindHeaderRow(candidate, out headerRow, out headers))
            {
                sheet = candidate;
                return true;
            }
        }

        foreach (var candidate in workbook.Worksheets)
        {
            if (TryFindHeaderRow(candidate, out headerRow, out headers))
            {
                sheet = candidate;
                return true;
            }
        }

        sheet = workbook.Worksheets.First();
        headerRow = 0;
        headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return false;
    }

    private static bool TryFindHeaderRow(IXLWorksheet sheet, out int headerRow, out Dictionary<string, int> headers)
    {
        var lastRow = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 0, 20);
        for (var rowNumber = 1; rowNumber <= lastRow; rowNumber++)
        {
            var row = sheet.Row(rowNumber);
            var lastCell = row.LastCellUsed()?.Address.ColumnNumber ?? 0;
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var col = 1; col <= lastCell; col++)
            {
                var normalized = NormalizeHeader(row.Cell(col).GetString());
                if (normalized.Length > 0 && !map.ContainsKey(normalized))
                {
                    map[normalized] = col;
                }
            }

            if (map.ContainsKey(NormalizeHeader("PROJECT_NAME")) && map.ContainsKey(NormalizeHeader("ESTIMATED_COST")))
            {
                headerRow = rowNumber;
                headers = map;
                return true;
            }
        }

        headerRow = 0;
        headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return false;
    }

    private static bool IsEmptyRow(IXLRow row, IEnumerable<int> columns)
        => columns.All(c => string.IsNullOrWhiteSpace(row.Cell(c).GetString()));

    private static string? Read(IXLRow row, IReadOnlyDictionary<string, int> headers, params string[] names)
    {
        foreach (var name in names)
        {
            if (headers.TryGetValue(NormalizeHeader(name), out var col))
            {
                var value = row.Cell(col).GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        return null;
    }

    private static string BuildRawJson(IXLRow row, IReadOnlyDictionary<string, int> headers, string fileName)
    {
        var values = new SortedDictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["_source_file"] = fileName,
        };

        foreach (var (header, col) in headers)
        {
            values[header] = NullIfBlank(row.Cell(col).GetString());
        }

        return JsonSerializer.Serialize(values);
    }

    private static string NormalizeHeader(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    private static IssueInfo ParseIssueFromFilename(string name)
    {
        var m = FilenameYearQuarter1.Match(name);
        if (!m.Success)
        {
            m = FilenameYearQuarter2.Match(name);
        }

        if (m.Success
            && short.TryParse(m.Groups["year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            && byte.TryParse(m.Groups["quarter"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quarter))
        {
            return new IssueInfo(year, quarter);
        }

        var yearMatch = YearRegex.Match(name);
        if (yearMatch.Success && short.TryParse(yearMatch.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var yearOnly))
        {
            return new IssueInfo(yearOnly, 4);
        }

        return new IssueInfo(0, 0);
    }

    private static string BuildFallbackSourceKey(string projectName, string? municipality)
    {
        var input = $"{projectName.Trim()}|{municipality?.Trim()}";
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(input.ToUpperInvariant()));
        return "BC-" + Convert.ToHexString(hash);
    }

    private static decimal? ParseBcCostMillions(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var lower = value.Trim().ToLowerInvariant();
        var numeric = Regex.Replace(lower, @"[$,\s]", "");
        numeric = Regex.Replace(numeric, "(billion|million|[mb])", "", RegexOptions.IgnoreCase);
        if (!decimal.TryParse(numeric, NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
        {
            return null;
        }

        var multiplier = lower.Contains("billion") || Regex.IsMatch(lower, @"\bb\b")
            ? 1_000_000_000m
            : lower.Contains("million") || Regex.IsMatch(lower, @"\bm\b")
                ? 1_000_000m
                : parsed >= 100_000m
                    ? 1m
                    : 1_000_000m;

        return decimal.Round(parsed * multiplier, 0);
    }

    private static bool? ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "y" or "yes" or "true" or "t" => true,
            "0" or "n" or "no" or "false" or "f" => false,
            _ => null,
        };
    }

    private static int? ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = Regex.Replace(value, @"[,\s]", "");
        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = Regex.Replace(value, @"[,\s]", "");
        return decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static short? ParseLeadingYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = YearRegex.Match(value);
        return match.Success && short.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;
    }

    private static string? BuildScheduleNotes(string? start, string? completion)
    {
        if (string.IsNullOrWhiteSpace(start) && string.IsNullOrWhiteSpace(completion))
        {
            return null;
        }

        return $"{start ?? "unknown"} - {completion ?? "unknown"}";
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void AddString(SqlCommand cmd, string name, string? value, int size)
    {
        var parameter = cmd.Parameters.Add(name, SqlDbType.NVarChar, size);
        if (string.IsNullOrWhiteSpace(value))
        {
            parameter.Value = DBNull.Value;
            return;
        }

        var trimmed = value.Trim();
        parameter.Value = size > 0 && trimmed.Length > size
            ? trimmed.Substring(0, size)
            : trimmed;
    }

    private static void AddDecimal(SqlCommand cmd, string name, decimal? value, byte precision, byte scale)
    {
        var parameter = cmd.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value = value.HasValue ? (object)value.Value : DBNull.Value;
    }

    private static void AddLong(SqlCommand cmd, string name, long? value)
        => cmd.Parameters.Add(name, SqlDbType.BigInt).Value = value.HasValue ? (object)value.Value : DBNull.Value;

    private static void AddInt(SqlCommand cmd, string name, int? value)
        => cmd.Parameters.Add(name, SqlDbType.Int).Value = value.HasValue ? (object)value.Value : DBNull.Value;

    private static void AddShort(SqlCommand cmd, string name, short? value)
        => cmd.Parameters.Add(name, SqlDbType.SmallInt).Value = value.HasValue ? (object)value.Value : DBNull.Value;

    private static void AddByte(SqlCommand cmd, string name, byte? value)
        => cmd.Parameters.Add(name, SqlDbType.TinyInt).Value = value.HasValue ? (object)value.Value : DBNull.Value;

    private static void AddBool(SqlCommand cmd, string name, bool? value)
        => cmd.Parameters.Add(name, SqlDbType.Bit).Value = value.HasValue ? (object)value.Value : DBNull.Value;

    private sealed record ImportFile(string Path, IssueInfo Issue);
    private sealed record IssueInfo(short Year, byte Quarter)
    {
        public int Score => Year <= 0 ? 0 : Year * 4 + Quarter;
    }

    private sealed class ImportStats
    {
        public int FilesProcessed { get; set; }
        public int RowsProcessed { get; set; }
        public int Upserted { get; set; }
        public int Skipped { get; set; }
        public int ArchitectResolved { get; set; }
        public int IndigenousFlagged { get; set; }
        public HashSet<string> UniqueProjects { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record BcMpiRecord(
        string SourceKey,
        string? ExternalProjectId,
        string ProjectName,
        string? ProjectDescription,
        decimal? EstimatedCostCad,
        string EstimatedCostText,
        string? Sector,
        string? SubSector,
        string? ConstructionType,
        string? ConstructionSubtype,
        string? ProjectType,
        string? RegionName,
        string? MunicipalityName,
        string? ProponentName,
        long? ProponentCanonicalOrgId,
        string? ArchitectName,
        long? ArchitectCanonicalOrgId,
        string? Stage,
        string? ProjectStatus,
        string? ProjectStage,
        string? ProjectCategoryName,
        bool? PublicFundingInd,
        bool? ProvincialFunding,
        bool? FederalFunding,
        bool? MunicipalFunding,
        bool? OtherPublicFunding,
        bool? GreenBuildingInd,
        bool? IndigenousInd,
        string? IndigenousNames,
        int? ConstructionJobs,
        int? OperatingJobs,
        string? StandardizedStartDate,
        string? StandardizedCompletionDate,
        short? StartYear,
        short? CompletionYear,
        string? ScheduleNotes,
        decimal? Latitude,
        decimal? Longitude,
        string? ProjectWebsite,
        string SourceUrl,
        short? IssueYear,
        byte? IssueQuarter,
        string RawJson);

    private sealed record ToolOptions(string InputDirectory, string OpportunitiesDb)
    {
        public static ToolOptions Load(IConfiguration config, string[] args)
        {
            var dir = config["InputDirectory"] ?? DefaultDirectory;
            var db = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
                ?? config["OpportunitiesDb"]
                ?? "";

            for (var i = 0; i < args.Length; i++)
            {
                var key = args[i];
                var value = i + 1 < args.Length ? args[i + 1] : "";
                switch (key)
                {
                    case "--dir":
                        dir = string.IsNullOrWhiteSpace(value) ? dir : value;
                        i++;
                        break;
                    case "--db":
                        db = string.IsNullOrWhiteSpace(value) ? db : value;
                        i++;
                        break;
                }
            }

            return new ToolOptions(dir, db);
        }
    }
}
