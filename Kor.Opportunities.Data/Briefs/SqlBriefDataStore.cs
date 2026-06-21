#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Intel;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Briefs;

/// <summary>
/// SQL Server implementation of <see cref="IBriefDataStore"/>. Pure ADO, mirrors
/// the convention of <c>SqlOpportunityStore</c>: short-lived connections,
/// parameterized queries, no EF. All FK column / Kind literals are hard-coded
/// schema targets — never sourced from user input.
/// </summary>
public sealed class SqlBriefDataStore : IBriefDataStore
{
    private const int CommandTimeoutSeconds = 30;

    // Stopwords are the generic infra/admin words that show up in
    // nearly every MPI name and produce false-positive LIKE matches.
    // Lowercase; matched case-insensitively.
    private static readonly HashSet<string> ProjectNameStopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "new", "old", "stand", "alone", "stand-alone", "the", "of", "and",
        "or", "in", "for", "at", "to", "with", "by", "from", "on",
        "north", "south", "east", "west", "central", "northern", "southern",
        "primary", "secondary", "main",
        "project", "projects", "phase", "site", "campus", "facility",
        "facilities", "building", "buildings", "tower", "towers", "complex",
        "construction", "redevelopment", "renewal", "replacement",
        "expansion", "upgrade", "renovation", "renovations", "addition",
        "modernization", "improvement", "improvements", "rehabilitation",
        "refurbishment",
        "hospital", "centre", "center", "school", "schools", "regional",
        "general", "community", "health", "medical", "clinic",
        "children", "children's", "elementary", "secondary",
        "planning", "design", "build", "development",
    };

    private readonly string _connectionString;
    private readonly IntelReadService _intelReadService;

    public SqlBriefDataStore(string connectionString, IntelReadService intelReadService)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
        _intelReadService = intelReadService ?? throw new ArgumentNullException(nameof(intelReadService));
    }

    public async Task<OpportunityBriefData?> GetOpportunityBriefAsync(long opportunityId, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        long id;
        string name;
        string buyerName;
        long? buyerOrgId;
        DateTimeOffset? deadline;
        decimal? estVal;
        decimal primeConf;
        string? sector;
        string? province;
        string? city;
        {
            const string sql = @"
SELECT Id, Name, BuyerName, BuyerCanonicalOrgId, SubmissionDeadlineUtc, EstimatedValue,
       PrimeConfidence, PrimeProjectSector, ProjectProvince, ProjectCity
FROM opportunities.Opportunities
WHERE Id = @id;";
            await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = opportunityId;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await r.ReadAsync(ct).ConfigureAwait(false))
            {
                return null;
            }

            id = r.GetInt64(0);
            name = r.GetString(1);
            buyerName = r.GetString(2);
            buyerOrgId = r.IsDBNull(3) ? null : r.GetInt64(3);
            deadline = r.IsDBNull(4) ? null : r.GetDateTimeOffset(4);
            estVal = r.IsDBNull(5) ? null : r.GetDecimal(5);
            primeConf = r.IsDBNull(6) ? 0m : r.GetDecimal(6);
            sector = r.IsDBNull(7) ? null : r.GetString(7);
            province = r.IsDBNull(8) ? null : r.GetString(8);
            city = r.IsDBNull(9) ? null : r.GetString(9);
        }

        var korId = await GetKorIdAsync(con, ct).ConfigureAwait(false);

        var ownerKor = 0;
        var ownerPipeline = 0;
        string? likelyArch = null;
        long? likelyArchId = null;
        var likelyArchProjects = 0;
        var korArchJoint = 0;

        if (buyerOrgId.HasValue)
        {
            {
                const string sql = "SELECT ISNULL(KorProjectsCount, 0) FROM opportunities.CanonicalOrg WHERE Id = @id AND RetiredAtUtc IS NULL;";
                await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
                cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = buyerOrgId.Value;
                var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                ownerKor = v is null or DBNull ? 0 : Convert.ToInt32(v);
            }
            {
                const string sql = @"
SELECT COUNT(*) FROM opportunities.MajorProjectsInventory
WHERE ProponentCanonicalOrgId = @id AND RetiredAtUtc IS NULL;";
                await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
                cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = buyerOrgId.Value;
                var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                ownerPipeline = v is null or DBNull ? 0 : Convert.ToInt32(v);
            }
            {
                // BD-Audit-2026-06-09 M18 (documented rule): "likely architect"
                // and the KOR-joint counts below are ALL-TIME credential/history
                // signals — built and retired projects are deliberately included
                // (the strongest who-designs-for-this-owner signal IS their built
                // work). Pipeline-scoped counts elsewhere (sector brief, owner
                // pipeline above) filter RetiredAtUtc — that difference is
                // intentional, not drift.
                const string sql = @"
SELECT TOP 1 ArchitectName, ArchitectCanonicalOrgId, COUNT(*) AS C
FROM opportunities.MajorProjectsInventory
WHERE ProponentCanonicalOrgId = @id AND ArchitectName IS NOT NULL
GROUP BY ArchitectName, ArchitectCanonicalOrgId
ORDER BY COUNT(*) DESC;";
                await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
                cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = buyerOrgId.Value;
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    likelyArch = r.GetString(0);
                    likelyArchId = r.IsDBNull(1) ? null : r.GetInt64(1);
                    likelyArchProjects = r.GetInt32(2);
                }
            }
        }

        if (likelyArchId.HasValue && korId.HasValue)
        {
            // All-time KOR-joint history (see M18 rule note above) — includes
            // built/retired projects by design.
            const string sql = @"
SELECT COUNT(*) FROM opportunities.MajorProjectsInventory
WHERE ArchitectCanonicalOrgId = @arch AND StructuralEngineerCanonicalOrgId = @kor;";
            await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@arch", SqlDbType.BigInt).Value = likelyArchId.Value;
            cmd.Parameters.Add("@kor", SqlDbType.BigInt).Value = korId.Value;
            var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            korArchJoint = v is null or DBNull ? 0 : Convert.ToInt32(v);
        }

        var matchedEvent = await GetMatchedEventAsync(con, province, sector, ct).ConfigureAwait(false);
        var oppIntel = await _intelReadService.GetOpportunityIntelAsync(buyerOrgId, likelyArchId, ct).ConfigureAwait(false);

        return new OpportunityBriefData(
            id, name, buyerName, buyerOrgId, deadline, estVal,
            primeConf, sector, province, city,
            ownerKor, ownerPipeline,
            likelyArch, likelyArchId, likelyArchProjects, korArchJoint,
            matchedEvent)
        {
            Intel = oppIntel,
        };
    }

    public async Task<IReadOnlyList<ProjectSearchRow>> SearchProjectsAsync(string query, int take, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@take) m.Id, m.ProjectName, m.ProponentName,
       m.Stage, m.MunicipalityName, m.Province
FROM opportunities.MajorProjectsInventory m
WHERE m.RetiredAtUtc IS NULL
  AND (@q IS NULL
       OR m.ProjectName LIKE '%' + @q + '%' ESCAPE '\'
       OR m.ProponentName LIKE '%' + @q + '%' ESCAPE '\'
       OR m.ArchitectName LIKE '%' + @q + '%' ESCAPE '\')
ORDER BY m.UpdatedAtUtc DESC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = Math.Max(1, take);
        cmd.Parameters.Add("@q", SqlDbType.NVarChar, 300).Value =
            string.IsNullOrWhiteSpace(query) ? DBNull.Value : EscapeLikeQuery(query.Trim());

        var rows = new List<ProjectSearchRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new ProjectSearchRow(
                r.GetInt64(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.GetString(5)));
        }

        return rows;
    }

    public async Task<ProjectBriefData?> GetProjectBriefAsync(long mpiId, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        const string sql = @"
SELECT Id, ProjectName, Province, MunicipalityName, RegionName, Sector, SubSector,
       Stage, EstimatedCostCad, EstimatedCostText, StartYear, CompletionYear,
       ScheduleNotes, ProponentName, ArchitectName, StructuralEngineerName,
       GeneralContractorName, SourceUrl, ProjectDescription,
       ProponentCanonicalOrgId, ArchitectCanonicalOrgId,
       StructuralEngineerCanonicalOrgId, GeneralContractorCanonicalOrgId
FROM opportunities.MajorProjectsInventory
WHERE Id = @id AND RetiredAtUtc IS NULL;";

        long id;
        string projectName;
        string province;
        string? city;
        string? region;
        string? sector;
        string? subSector;
        string? stage;
        decimal? estimatedCostCad;
        string? estimatedCostText;
        short? startYear;
        short? completionYear;
        string? scheduleNotes;
        string? proponentName;
        string? architectName;
        string? structuralEngineerName;
        string? generalContractorName;
        string? sourceUrl;
        string? projectDescription;
        long? proponentOrgId;
        long? architectOrgId;
        long? structuralOrgId;
        long? gcOrgId;

        await using (var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds })
        {
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = mpiId;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await r.ReadAsync(ct).ConfigureAwait(false))
            {
                return null;
            }

            id = r.GetInt64(0);
            projectName = r.GetString(1);
            province = r.GetString(2);
            city = r.IsDBNull(3) ? null : r.GetString(3);
            region = r.IsDBNull(4) ? null : r.GetString(4);
            sector = r.IsDBNull(5) ? null : r.GetString(5);
            subSector = r.IsDBNull(6) ? null : r.GetString(6);
            stage = r.IsDBNull(7) ? null : r.GetString(7);
            estimatedCostCad = r.IsDBNull(8) ? null : r.GetDecimal(8);
            estimatedCostText = r.IsDBNull(9) ? null : r.GetString(9);
            startYear = r.IsDBNull(10) ? null : r.GetInt16(10);
            completionYear = r.IsDBNull(11) ? null : r.GetInt16(11);
            scheduleNotes = r.IsDBNull(12) ? null : r.GetString(12);
            proponentName = r.IsDBNull(13) ? null : r.GetString(13);
            architectName = r.IsDBNull(14) ? null : r.GetString(14);
            structuralEngineerName = r.IsDBNull(15) ? null : r.GetString(15);
            generalContractorName = r.IsDBNull(16) ? null : r.GetString(16);
            sourceUrl = r.IsDBNull(17) ? null : r.GetString(17);
            projectDescription = r.IsDBNull(18) ? null : r.GetString(18);
            proponentOrgId = r.IsDBNull(19) ? null : r.GetInt64(19);
            architectOrgId = r.IsDBNull(20) ? null : r.GetInt64(20);
            structuralOrgId = r.IsDBNull(21) ? null : r.GetInt64(21);
            gcOrgId = r.IsDBNull(22) ? null : r.GetInt64(22);
        }

        var proponentSummary = proponentOrgId.HasValue
            ? await BuildLinkedOrgSummaryAsync(con, proponentOrgId.Value, ct).ConfigureAwait(false)
            : null;
        var architectSummary = architectOrgId.HasValue
            ? await BuildLinkedOrgSummaryAsync(con, architectOrgId.Value, ct).ConfigureAwait(false)
            : null;
        var structuralSummary = structuralOrgId.HasValue
            ? await BuildLinkedOrgSummaryAsync(con, structuralOrgId.Value, ct).ConfigureAwait(false)
            : null;
        var gcSummary = gcOrgId.HasValue
            ? await BuildLinkedOrgSummaryAsync(con, gcOrgId.Value, ct).ConfigureAwait(false)
            : null;

        var tokens = ExtractDistinctiveTokens(projectName);
        IReadOnlyList<ProjectMentionInWorkHistory> workMentions = Array.Empty<ProjectMentionInWorkHistory>();
        IReadOnlyList<ProjectRecentMentionSignal> signalMentions = Array.Empty<ProjectRecentMentionSignal>();
        IReadOnlyList<ProjectOpenActionMention> actionMentions = Array.Empty<ProjectOpenActionMention>();

        if (tokens.Count > 0)
        {
            workMentions = await LoadProjectWorkMentionsAsync(con, id, tokens, ct).ConfigureAwait(false);
            signalMentions = await LoadProjectSignalMentionsAsync(con, tokens, ct).ConfigureAwait(false);
            actionMentions = await LoadProjectOpenActionMentionsAsync(con, tokens, ct).ConfigureAwait(false);
        }

        var projectIntel = await LoadProjectIntelAsync(con, id, ct).ConfigureAwait(false);
        var projectIntelSignals = await LoadProjectIntelSignalsAsync(con, id, ct).ConfigureAwait(false);
        var projectIntelActions = await LoadProjectIntelActionsAsync(con, id, ct).ConfigureAwait(false);
        var projectIntelRisks = await LoadProjectIntelRisksAsync(con, id, ct).ConfigureAwait(false);
        var projectIntelKeyPeople = await LoadProjectIntelKeyPeopleAsync(con, id, ct).ConfigureAwait(false);

        return new ProjectBriefData(
            id,
            projectName,
            province,
            city,
            region,
            sector,
            subSector,
            stage,
            estimatedCostCad,
            estimatedCostText,
            startYear,
            completionYear,
            scheduleNotes,
            proponentName,
            architectName,
            structuralEngineerName,
            generalContractorName,
            sourceUrl,
            string.IsNullOrWhiteSpace(projectDescription) ? scheduleNotes : projectDescription,
            proponentSummary,
            architectSummary,
            structuralSummary,
            gcSummary,
            workMentions,
            signalMentions,
            actionMentions,
            projectIntel.Description,
            projectIntel.Schedule,
            projectIntel.Status,
            projectIntel.KorAngle,
            projectIntelKeyPeople,
            projectIntelSignals,
            projectIntelActions,
            projectIntelRisks);
    }

    public async Task<IReadOnlyList<PersonSearchRow>> SearchPeopleAsync(string query, int take, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@take)
    p.Id,
    p.DisplayName,
    cur.Title       AS CurrentTitle,
    co.DisplayName  AS CurrentEmployerName
FROM opportunities.IntelPerson p
OUTER APPLY (
    SELECT TOP 1 a.Title, a.CanonicalOrgId
    FROM opportunities.IntelPersonAffiliation a
    WHERE a.IntelPersonId = p.Id
      AND a.IsCurrent = 1
      AND a.RetiredAtUtc IS NULL
    ORDER BY a.LastSeenAtUtc DESC
) cur
LEFT JOIN opportunities.CanonicalOrg co ON co.Id = cur.CanonicalOrgId
    AND co.RetiredAtUtc IS NULL
WHERE p.RetiredAtUtc IS NULL
  AND (@q IS NULL OR p.DisplayName LIKE '%' + @q + '%' ESCAPE '\')
ORDER BY p.LastSeenAtUtc DESC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = Math.Max(1, take);
        cmd.Parameters.Add("@q", SqlDbType.NVarChar, 300).Value =
            string.IsNullOrWhiteSpace(query) ? DBNull.Value : EscapeLikeQuery(query.Trim());

        var rows = new List<PersonSearchRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new PersonSearchRow(
                r.GetInt64(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3)));
        }

        return rows;
    }

    public async Task<PersonBriefData?> GetPersonBriefAsync(long intelPersonId, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        long id;
        string displayName;
        string? email;
        string? phone;
        string? linkedinUrl;
        string? notes;
        DateTimeOffset lastSeen;

        {
            const string sql = @"
SELECT Id, DisplayName, Email, Phone, LinkedinUrl, Notes, LastSeenAtUtc
FROM opportunities.IntelPerson
WHERE Id = @id AND RetiredAtUtc IS NULL;";
            await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = intelPersonId;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await r.ReadAsync(ct).ConfigureAwait(false))
            {
                return null;
            }

            id = r.GetInt64(0);
            displayName = r.GetString(1);
            email = r.IsDBNull(2) ? null : r.GetString(2);
            phone = r.IsDBNull(3) ? null : r.GetString(3);
            linkedinUrl = r.IsDBNull(4) ? null : r.GetString(4);
            notes = r.IsDBNull(5) ? null : r.GetString(5);
            lastSeen = r.GetDateTimeOffset(6);
        }

        var affiliations = await GetPersonAffiliationsAsync(con, id, ct).ConfigureAwait(false);
        var currentWithSeen = affiliations
            .Where(a => a.Row.IsCurrent)
            .OrderByDescending(a => a.LastSeenAtUtc)
            .ToList();
        var current = currentWithSeen.Select(a => a.Row).ToList();
        var former = affiliations
            .Where(a => !a.Row.IsCurrent)
            .OrderBy(a => string.IsNullOrWhiteSpace(a.Row.EndDateApprox))
            .ThenByDescending(a => a.Row.EndDateApprox ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(a => a.Row)
            .ToList();

        var primaryCurrent = currentWithSeen.FirstOrDefault();
        var signals = await GetPersonSignalsAsync(con, displayName, ct).ConfigureAwait(false);
        var actions = await GetPersonActionsAsync(con, displayName, ct).ConfigureAwait(false);

        return new PersonBriefData(
            id,
            displayName,
            primaryCurrent?.Row.Title,
            primaryCurrent?.Row.OrgName,
            primaryCurrent?.Row.CanonicalOrgId,
            email,
            phone,
            linkedinUrl,
            notes,
            lastSeen,
            current,
            former,
            signals,
            actions);
    }

    public async Task<RegionBriefData> GetRegionBriefAsync(string province, string? city, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(province))
        {
            throw new ArgumentException("Province is required.", nameof(province));
        }

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        var korId = await GetKorIdAsync(con, ct).ConfigureAwait(false);
        var korIdVal = korId ?? -1L;
        var cityTokens = TokenizeCity(city);
        var cityLabel = string.IsNullOrWhiteSpace(city) ? null : city.Trim();

        var liveRfpCount = await ScalarIntAsync(con, $@"
SELECT COUNT(*) FROM opportunities.Opportunities
WHERE Status = 1 AND IsPrimeConsultantRfp = 1 AND ProjectProvince = @prov
{BuildCityClause(cityTokens, OpportunityCityColumns, "c")};",
            cmd =>
            {
                cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 20).Value = province;
                BindCityTokens(cmd, cityTokens, "c");
            }, ct).ConfigureAwait(false);

        var forwardCount = await ScalarIntAsync(con, $@"
SELECT COUNT(*) FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL AND Province = @prov
{BuildCityClause(cityTokens, MpiCityColumns, "c")}
  AND ProjectStage IN (N'CapitalPlan', N'FacilityRenewal');",
            cmd =>
            {
                cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 20).Value = province;
                BindCityTokens(cmd, cityTokens, "c");
            }, ct).ConfigureAwait(false);

        var activeMpiCount = await ScalarIntAsync(con, $@"
SELECT COUNT(*) FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL AND Province = @prov
{BuildCityClause(cityTokens, MpiCityColumns, "c")};",
            cmd =>
            {
                cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 20).Value = province;
                BindCityTokens(cmd, cityTokens, "c");
            }, ct).ConfigureAwait(false);

        var topArchitects = await GetTopOrgsAsync(con, province, cityTokens, korIdVal, "Architect", "ArchitectCanonicalOrgId", includeKorJointSubquery: true, ct).ConfigureAwait(false);
        var topOwners = await GetTopOwnersAsync(con, province, cityTokens, ct).ConfigureAwait(false);
        var topCompetitors = await GetTopOrgsAsync(con, province, cityTokens, korIdVal, "Competitor", "StructuralEngineerCanonicalOrgId", includeKorJointSubquery: false, ct).ConfigureAwait(false);
        var liveRfps = await GetRegionLiveRfpsAsync(con, province, cityTokens, ct).ConfigureAwait(false);
        var forwardProjects = await GetRegionForwardProjectsAsync(con, province, cityTokens, ct).ConfigureAwait(false);
        var events = await GetRegionEventsAsync(con, province, cityTokens, ct).ConfigureAwait(false);
        var rollup = await _intelReadService.GetRegionIntelAsync(province, city, ct).ConfigureAwait(false);

        return new RegionBriefData(
            province, cityLabel,
            liveRfpCount, forwardCount, activeMpiCount,
            topArchitects, topOwners, topCompetitors,
            liveRfps, forwardProjects, events)
        {
            Intel = rollup,
        };
    }

    public async Task<SectorBriefData> GetSectorBriefAsync(
        SectorBriefRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        var (mpiWhere, mpiParams) = BuildSectorMpiFilter(request);
        var (oppWhere, oppParams) = BuildSectorOpportunityFilter(request);
        var (awardWhere, awardParams) = BuildSectorAwardFilter(request);
        var korId = await GetKorIdAsync(con, ct).ConfigureAwait(false) ?? -1L;

        var liveRfpCount = await ScalarIntAsync(con, $@"
SELECT COUNT(*)
FROM opportunities.Opportunities o
WHERE {oppWhere};",
            cmd => AddClonedParameters(cmd, oppParams), ct).ConfigureAwait(false);

        var forwardPipelineCount = await ScalarIntAsync(con, $@"
SELECT COUNT(*)
FROM opportunities.MajorProjectsInventory mpi
WHERE {mpiWhere};",
            cmd => AddClonedParameters(cmd, mpiParams), ct).ConfigureAwait(false);

        var recentAwardCount = await ScalarIntAsync(con, $@"
SELECT COUNT(*)
FROM opportunities.OpportunityAwards a
WHERE {awardWhere}
  AND a.AwardedAtUtc >= DATEADD(DAY, -365, sysdatetimeoffset());",
            cmd => AddClonedParameters(cmd, awardParams), ct).ConfigureAwait(false);

        var totalForwardPipelineCostCad = await ScalarDecimalAsync(con, $@"
SELECT SUM(mpi.EstimatedCostCad)
FROM opportunities.MajorProjectsInventory mpi
WHERE {mpiWhere};",
            cmd => AddClonedParameters(cmd, mpiParams), ct).ConfigureAwait(false);

        var counts = new SectorBriefCounts(
            liveRfpCount,
            forwardPipelineCount,
            recentAwardCount,
            totalForwardPipelineCostCad);

        var liveRfps = await GetSectorLiveRfpsAsync(con, oppWhere, oppParams, ct).ConfigureAwait(false);
        var forwardProjects = await GetSectorForwardProjectsAsync(con, mpiWhere, mpiParams, ct).ConfigureAwait(false);
        var recentAwards = await GetSectorRecentAwardsAsync(con, awardWhere, awardParams, ct).ConfigureAwait(false);
        var topArchitects = await GetSectorTopOrgsAsync(con, mpiWhere, mpiParams, "ArchitectCanonicalOrgId", includeKorJointCount: true, korId, excludeKor: false, ct).ConfigureAwait(false);
        var topOwners = await GetSectorTopOwnersAsync(con, mpiWhere, mpiParams, ct).ConfigureAwait(false);
        var topGcs = await GetSectorTopOrgsAsync(con, mpiWhere, mpiParams, "GeneralContractorCanonicalOrgId", includeKorJointCount: true, korId, excludeKor: false, ct).ConfigureAwait(false);
        var topStructuralCompetitors = await GetSectorTopOrgsAsync(con, mpiWhere, mpiParams, "StructuralEngineerCanonicalOrgId", includeKorJointCount: false, korId, excludeKor: true, ct).ConfigureAwait(false);
        var korPortfolio = await GetSectorKorPortfolioAsync(con, mpiWhere, mpiParams, korId, ct).ConfigureAwait(false);
        var relevantSignals = await GetSectorRelevantSignalsAsync(con, mpiWhere, mpiParams, ct).ConfigureAwait(false);

        return new SectorBriefData(
            request,
            counts,
            liveRfps,
            forwardProjects,
            recentAwards,
            topArchitects,
            topOwners,
            topGcs,
            topStructuralCompetitors,
            korPortfolio,
            relevantSignals);
    }

    public async Task<OrgBriefData?> GetOrgBriefAsync(long canonicalOrgId, CancellationToken ct)
        => await GetOrgBriefAsync(canonicalOrgId, ct, alreadyResolved: false, resolvedFromNote: null).ConfigureAwait(false);

    private async Task<OrgBriefData?> GetOrgBriefAsync(
        long canonicalOrgId,
        CancellationToken ct,
        bool alreadyResolved,
        string? resolvedFromNote)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        long id;
        string kind;
        string displayName;
        string? website;
        int korProjects;
        DateTimeOffset? lastKor;
        string? deltekClientId;
        {
            const string sql = @"
SELECT Id, Kind, DisplayName, Website, ISNULL(KorProjectsCount, 0) AS KorProjectsCount, LastKorProjectAtUtc, ClendorClientId
FROM opportunities.CanonicalOrg
WHERE Id = @id
  AND RetiredAtUtc IS NULL;";
            await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await r.ReadAsync(ct).ConfigureAwait(false))
            {
                return null;
            }

            id = r.GetInt64(0);
            kind = r.GetString(1);
            displayName = r.GetString(2);
            website = r.IsDBNull(3) ? null : r.GetString(3);
            korProjects = Convert.ToInt32(r.GetValue(4));
            lastKor = r.IsDBNull(5) ? null : new DateTimeOffset(r.GetDateTime(5), TimeSpan.Zero);
            deltekClientId = r.IsDBNull(6) ? null : r.GetString(6);
        }

        var korId = await GetKorIdAsync(con, ct).ConfigureAwait(false);
        var korIdVal = korId ?? -1L;

        var recentProjects = await ReadRecentProjectsAsync(con, @"
SELECT TOP 5 m.Id, m.ProjectName, m.CompletionYear, m.Sector, m.Province
FROM opportunities.MajorProjectsInventory m
WHERE m.RetiredAtUtc IS NULL
  AND (m.ArchitectCanonicalOrgId = @id OR m.ProponentCanonicalOrgId = @id
    OR m.GeneralContractorCanonicalOrgId = @id OR m.StructuralEngineerCanonicalOrgId = @id)
ORDER BY ISNULL(m.CompletionYear, 0) DESC, m.Id DESC;",
            cmd => cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId, ct).ConfigureAwait(false);

        var korJointCount = 0;
        var korJointProjects = (IReadOnlyList<OrgRecentProject>)Array.Empty<OrgRecentProject>();
        if (korId.HasValue)
        {
            korJointCount = await ScalarIntAsync(con, @"
SELECT COUNT(*) FROM opportunities.MajorProjectsInventory m
WHERE m.StructuralEngineerCanonicalOrgId = @kor
  AND (m.ArchitectCanonicalOrgId = @id OR m.ProponentCanonicalOrgId = @id
    OR m.GeneralContractorCanonicalOrgId = @id);",
                cmd =>
                {
                    cmd.Parameters.Add("@kor", SqlDbType.BigInt).Value = korIdVal;
                    cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
                }, ct).ConfigureAwait(false);

            korJointProjects = await ReadRecentProjectsAsync(con, @"
SELECT TOP 5 m.Id, m.ProjectName, m.CompletionYear, m.Sector, m.Province
FROM opportunities.MajorProjectsInventory m
WHERE m.StructuralEngineerCanonicalOrgId = @kor
  AND (m.ArchitectCanonicalOrgId = @id OR m.ProponentCanonicalOrgId = @id
    OR m.GeneralContractorCanonicalOrgId = @id)
ORDER BY ISNULL(m.CompletionYear, 0) DESC, m.Id DESC;",
                cmd =>
                {
                    cmd.Parameters.Add("@kor", SqlDbType.BigInt).Value = korIdVal;
                    cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
                }, ct).ConfigureAwait(false);
        }

        string? enrichmentJson;
        {
            const string sql = @"
SELECT TOP 1 ResultJson FROM opportunities.CanonicalOrgEnrichment
WHERE CanonicalOrgId = @id AND ProviderName = N'DataHoning'
ORDER BY UpdatedAtUtc DESC;";
            await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
            var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            enrichmentJson = v is null or DBNull ? null : (string)v;
        }

        var intelBundle = await _intelReadService.GetOrgIntelAsync(canonicalOrgId, ct).ConfigureAwait(false);
        var contactCount = await ScalarIntAsync(con, @"
SELECT COUNT(*)
FROM opportunities.IntelPersonAffiliation
WHERE CanonicalOrgId = @id
  AND RetiredAtUtc IS NULL;",
            cmd => cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId,
            ct).ConfigureAwait(false);

        var isThin = korProjects == 0 && recentProjects.Count == 0 && contactCount == 0;
        if (isThin)
        {
            if (!alreadyResolved)
            {
                var richer = await FindRicherSameBrandCanonicalAsync(con, canonicalOrgId, ct).ConfigureAwait(false);
                if (richer is { } rc)
                {
                    // Only a genuine prefix match - one normalized name is a leading substring
                    // of the other (e.g. "bosa" -> "bosaproperties") - auto-redirects, and only
                    // when the shared name is >= 6 chars so a short generic word cannot hijack.
                    // Brand-stem matches (e.g. "<brand>development" vs "<brand>properties") are
                    // surfaced as a merge suggestion ONLY - never silently rendered as this
                    // company's data, since two different firms can share a stem.
                    if (rc.IsPrefix && rc.MinLen >= 6)
                    {
                        var note = $"Resolved to the primary record for this company (requested org #{canonicalOrgId} had minimal data).";
                        return await GetOrgBriefAsync(rc.Id, ct, alreadyResolved: true, resolvedFromNote: note).ConfigureAwait(false);
                    }

                    resolvedFromNote ??= $"This record is sparse. A likely primary record for this company may be #{rc.Id} \"{rc.DisplayName}\" - review for merge.";
                }
            }

            resolvedFromNote ??= "This organization has minimal data in the graph - the brief reflects what we have.";
        }

        return new OrgBriefData(
            id, kind, displayName, website, korProjects, lastKor,
            recentProjects, korJointCount, korJointProjects, enrichmentJson,
            deltekClientId, Deltek: null)
        {
            ResolvedFromNote = resolvedFromNote,
            Intel = intelBundle,
        };
    }

    // === Helpers ===

    private readonly record struct RicherCandidate(long Id, string DisplayName, bool IsPrefix, int MinLen);

    private static async Task<RicherCandidate?> FindRicherSameBrandCanonicalAsync(SqlConnection con, long canonicalOrgId, CancellationToken ct)
    {
        // A candidate is a PREFIX match when one normalized name is a leading substring
        // of the other; otherwise it may still be a (weaker) brand-stem match. The caller
        // auto-redirects only on prefix matches with a >= 6 char shared name; brand-stem
        // matches are returned for a "review for merge" suggestion, never a silent redirect.
        const string sql = @"
SELECT TOP 1
    r.Id,
    (SELECT COUNT(*) FROM opportunities.MajorProjectsInventory m
     WHERE m.RetiredAtUtc IS NULL
       AND (m.ProponentCanonicalOrgId = r.Id
         OR m.ArchitectCanonicalOrgId = r.Id
         OR m.GeneralContractorCanonicalOrgId = r.Id
         OR m.StructuralEngineerCanonicalOrgId = r.Id))
  + (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation a
     WHERE a.CanonicalOrgId = r.Id
       AND a.RetiredAtUtc IS NULL) AS Richness,
    r.DisplayName,
    CASE WHEN (r.NormalizedName LIKE t.NormalizedName + N'%' OR t.NormalizedName LIKE r.NormalizedName + N'%')
         THEN 1 ELSE 0 END AS IsPrefix,
    CASE WHEN (r.NormalizedName LIKE t.NormalizedName + N'%' OR t.NormalizedName LIKE r.NormalizedName + N'%')
         THEN (CASE WHEN LEN(t.NormalizedName) <= LEN(r.NormalizedName) THEN LEN(t.NormalizedName) ELSE LEN(r.NormalizedName) END)
         ELSE 0 END AS MinLen
FROM opportunities.CanonicalOrg t
JOIN opportunities.CanonicalOrg r
  ON r.RetiredAtUtc IS NULL
 AND r.Id <> t.Id
CROSS APPLY (SELECT
    TBase = CASE WHEN t.NormalizedName LIKE N'%llc' THEN LEFT(t.NormalizedName, LEN(t.NormalizedName) - 3) ELSE t.NormalizedName END,
    RBase = CASE WHEN r.NormalizedName LIKE N'%llc' THEN LEFT(r.NormalizedName, LEN(r.NormalizedName) - 3) ELSE r.NormalizedName END) base
CROSS APPLY (SELECT
    TBrand = CASE
        WHEN base.TBase LIKE N'%realestatepartners' THEN LEFT(base.TBase, LEN(base.TBase) - LEN(N'realestatepartners'))
        WHEN base.TBase LIKE N'%developmentwest' THEN LEFT(base.TBase, LEN(base.TBase) - LEN(N'developmentwest'))
        WHEN base.TBase LIKE N'%development' THEN LEFT(base.TBase, LEN(base.TBase) - LEN(N'development'))
        WHEN base.TBase LIKE N'%developments' THEN LEFT(base.TBase, LEN(base.TBase) - LEN(N'developments'))
        WHEN base.TBase LIKE N'%properties' THEN LEFT(base.TBase, LEN(base.TBase) - LEN(N'properties'))
        WHEN base.TBase LIKE N'%partners' THEN LEFT(base.TBase, LEN(base.TBase) - LEN(N'partners'))
        ELSE base.TBase
    END,
    RBrand = CASE
        WHEN base.RBase LIKE N'%realestatepartners' THEN LEFT(base.RBase, LEN(base.RBase) - LEN(N'realestatepartners'))
        WHEN base.RBase LIKE N'%developmentwest' THEN LEFT(base.RBase, LEN(base.RBase) - LEN(N'developmentwest'))
        WHEN base.RBase LIKE N'%development' THEN LEFT(base.RBase, LEN(base.RBase) - LEN(N'development'))
        WHEN base.RBase LIKE N'%developments' THEN LEFT(base.RBase, LEN(base.RBase) - LEN(N'developments'))
        WHEN base.RBase LIKE N'%properties' THEN LEFT(base.RBase, LEN(base.RBase) - LEN(N'properties'))
        WHEN base.RBase LIKE N'%partners' THEN LEFT(base.RBase, LEN(base.RBase) - LEN(N'partners'))
        ELSE base.RBase
    END) brand
WHERE t.Id = @id
  AND t.NormalizedName <> N''
  AND (
      r.NormalizedName LIKE t.NormalizedName + N'%'
      OR t.NormalizedName LIKE r.NormalizedName + N'%'
      OR (LEN(brand.TBrand) >= 6 AND brand.TBrand = brand.RBrand)
  )
ORDER BY IsPrefix DESC, Richness DESC, r.Id;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var richness = Convert.ToInt32(r.GetValue(1));
        if (richness <= 0)
        {
            return null;
        }

        var id = r.GetInt64(0);
        var name = r.IsDBNull(2) ? string.Empty : r.GetString(2);
        var isPrefix = Convert.ToInt32(r.GetValue(3)) == 1;
        var minLen = Convert.ToInt32(r.GetValue(4));
        return new RicherCandidate(id, name, isPrefix, minLen);
    }

    private static async Task<long?> GetKorIdAsync(SqlConnection con, CancellationToken ct)
    {
        const string sql = "SELECT TOP 1 Id FROM opportunities.CanonicalOrg WHERE Kind = N'KorStructural' AND RetiredAtUtc IS NULL;";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return v is null or DBNull ? null : Convert.ToInt64(v);
    }

    private static async Task<LinkedOrgSummary?> BuildLinkedOrgSummaryAsync(
        SqlConnection con,
        long canonicalOrgId,
        CancellationToken ct)
    {
        const string sql = @"
SELECT co.Id, co.DisplayName, co.Kind,
  (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation a
   WHERE a.CanonicalOrgId = co.Id AND a.RetiredAtUtc IS NULL)
      AS PeopleCount,
  (SELECT COUNT(*) FROM opportunities.IntelAction x
   WHERE x.CanonicalOrgId = co.Id AND x.Status = 'Open'
     AND x.RetiredAtUtc IS NULL)
      AS OpenActions,
  (SELECT COUNT(*) FROM opportunities.IntelSignal s
   WHERE s.CanonicalOrgId = co.Id
     AND s.RetiredAtUtc IS NULL
     AND s.LastSeenAtUtc >= DATEADD(DAY, -180, sysdatetimeoffset()))
      AS RecentSignals,
  (SELECT MAX(e.LastRefreshAtUtc)
   FROM opportunities.CanonicalOrgEnrichment e
   WHERE e.CanonicalOrgId = co.Id)
      AS LastRefreshAtUtc
FROM opportunities.CanonicalOrg co
WHERE co.Id = @id
  AND co.RetiredAtUtc IS NULL;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new LinkedOrgSummary(
            r.GetInt64(0),
            r.GetString(1),
            r.GetString(2),
            Convert.ToInt32(r.GetValue(3)),
            Convert.ToInt32(r.GetValue(4)),
            Convert.ToInt32(r.GetValue(5)),
            r.IsDBNull(6) ? null : r.GetDateTimeOffset(6));
    }

    private static async Task<(string? Description, string? Schedule, string? Status, string? KorAngle)> LoadProjectIntelAsync(
        SqlConnection con,
        long mpiId,
        CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 1 Description, Schedule, Status, KorAngle
FROM opportunities.IntelProject
WHERE MajorProjectsInventoryId = @id
  AND RetiredAtUtc IS NULL
ORDER BY LastSeenAtUtc DESC;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = mpiId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
        {
            return (null, null, null, null);
        }

        return (
            r.IsDBNull(0) ? null : r.GetString(0),
            r.IsDBNull(1) ? null : r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3));
    }

    private static async Task<IReadOnlyList<ProjectIntelSignalRow>> LoadProjectIntelSignalsAsync(
        SqlConnection con,
        long mpiId,
        CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (10) SignalType, Subject, Detail, OccurredAtApprox, LastSeenAtUtc
FROM opportunities.IntelProjectSignal
WHERE MajorProjectsInventoryId = @id
  AND RetiredAtUtc IS NULL
ORDER BY LastSeenAtUtc DESC;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = mpiId;
        var rows = new List<ProjectIntelSignalRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new ProjectIntelSignalRow(
                r.GetString(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetDateTimeOffset(4)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<ProjectIntelActionRow>> LoadProjectIntelActionsAsync(
        SqlConnection con,
        long mpiId,
        CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (10)
    a.ActionType,
    a.Recommendation,
    a.TargetPersonName,
    co.DisplayName AS TargetOrgName,
    a.TimingNotes,
    a.LastSeenAtUtc
FROM opportunities.IntelProjectAction a
LEFT JOIN opportunities.CanonicalOrg co ON co.Id = a.TargetCanonicalOrgId
    AND co.RetiredAtUtc IS NULL
WHERE a.MajorProjectsInventoryId = @id
  AND a.RetiredAtUtc IS NULL
  AND a.Status = N'Open'
ORDER BY a.LastSeenAtUtc DESC;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = mpiId;
        var rows = new List<ProjectIntelActionRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new ProjectIntelActionRow(
                r.GetString(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.GetDateTimeOffset(5)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<ProjectIntelRiskRow>> LoadProjectIntelRisksAsync(
        SqlConnection con,
        long mpiId,
        CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (10) RiskType, Description, MitigationNotes
FROM opportunities.IntelProjectRisk
WHERE MajorProjectsInventoryId = @id
  AND RetiredAtUtc IS NULL
ORDER BY LastSeenAtUtc DESC;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = mpiId;
        var rows = new List<ProjectIntelRiskRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new ProjectIntelRiskRow(
                r.GetString(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<ProjectIntelKeyPersonRow>> LoadProjectIntelKeyPeopleAsync(
        SqlConnection con,
        long mpiId,
        CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (20)
    p.DisplayName,
    p.Title,
    p.Side,
    co.DisplayName AS OrgName
FROM opportunities.IntelProjectKeyPerson p
LEFT JOIN opportunities.CanonicalOrg co ON co.Id = p.CanonicalOrgId
    AND co.RetiredAtUtc IS NULL
WHERE p.MajorProjectsInventoryId = @id
  AND p.RetiredAtUtc IS NULL
ORDER BY p.Side ASC, p.LastSeenAtUtc DESC;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = mpiId;
        var rows = new List<ProjectIntelKeyPersonRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new ProjectIntelKeyPersonRow(
                r.GetString(0),
                r.IsDBNull(1) ? null : r.GetString(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3)));
        }

        return rows;
    }

    // Tokens are case-preserved (for SQL LIKE bind), filtered:
    //   - drop parentheticals "(...)"
    //   - split on whitespace + comma + slash + dash + colon
    //   - drop stopwords
    //   - drop tokens with length < 5
    //   - drop pure-digit tokens
    //   - dedup case-insensitively
    //   - pick up to 3 longest (proper nouns are usually longest)
    private static IReadOnlyList<string> ExtractDistinctiveTokens(string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return Array.Empty<string>();
        }

        var cleaned = System.Text.RegularExpressions.Regex.Replace(projectName, @"\([^)]*\)", " ");
        var raw = cleaned.Split(
            new[] { ' ', '\t', ',', '/', '-', ':', ';', '.', '"' },
            StringSplitOptions.RemoveEmptyEntries);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keepers = new List<string>(raw.Length);
        foreach (var t in raw)
        {
            var trim = t.Trim('\'', '"');
            if (trim.Length < 5)
            {
                continue;
            }
            if (ProjectNameStopwords.Contains(trim))
            {
                continue;
            }
            if (trim.All(char.IsDigit))
            {
                continue;
            }
            if (!seen.Add(trim))
            {
                continue;
            }

            keepers.Add(trim);
        }

        return keepers
            .OrderByDescending(s => s.Length)
            .Take(3)
            .ToList();
    }

    private static async Task<IReadOnlyList<ProjectMentionInWorkHistory>> LoadProjectWorkMentionsAsync(
        SqlConnection con,
        long mpiId,
        IReadOnlyList<string> tokens,
        CancellationToken ct)
    {
        var likeClauses = string.Join(" OR ",
            Enumerable.Range(0, tokens.Count).Select(i => $"iw.ProjectName LIKE '%' + @t{i} + '%' ESCAPE '\\'"));
        var sql = $@"
SELECT TOP (10)
    co.DisplayName,
    co.Id,
    co.Kind,
    iw.Role,
    iw.YearApprox,
    iw.Notes
FROM opportunities.IntelWork iw
INNER JOIN opportunities.CanonicalOrg co ON co.Id = iw.CanonicalOrgId
WHERE iw.RetiredAtUtc IS NULL
  AND co.RetiredAtUtc IS NULL
  AND (
      iw.MajorProjectsInventoryId = @id
      OR {likeClauses}
  )
ORDER BY iw.LastSeenAtUtc DESC;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = mpiId;
        BindProjectMentionTokens(cmd, tokens);
        var rows = new List<ProjectMentionInWorkHistory>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new ProjectMentionInWorkHistory(
                r.GetString(0),
                r.GetInt64(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<ProjectRecentMentionSignal>> LoadProjectSignalMentionsAsync(
        SqlConnection con,
        IReadOnlyList<string> tokens,
        CancellationToken ct)
    {
        var likeClauses = string.Join(" OR ",
            Enumerable.Range(0, tokens.Count).Select(i =>
                $"s.Subject LIKE '%' + @t{i} + '%' ESCAPE '\\' OR s.Detail LIKE '%' + @t{i} + '%' ESCAPE '\\'"));
        var sql = $@"
SELECT TOP (10)
    co.DisplayName,
    co.Id,
    s.SignalType,
    s.Subject,
    s.Detail,
    s.OccurredAtApprox,
    s.LastSeenAtUtc
FROM opportunities.IntelSignal s
INNER JOIN opportunities.CanonicalOrg co ON co.Id = s.CanonicalOrgId
WHERE s.RetiredAtUtc IS NULL
  AND co.RetiredAtUtc IS NULL
  AND s.LastSeenAtUtc >= DATEADD(DAY, -365, sysdatetimeoffset())
  AND ( {likeClauses} )
ORDER BY s.LastSeenAtUtc DESC;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        BindProjectMentionTokens(cmd, tokens);
        var rows = new List<ProjectRecentMentionSignal>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new ProjectRecentMentionSignal(
                r.GetString(0),
                r.GetInt64(1),
                r.GetString(2),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.GetDateTimeOffset(6)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<ProjectOpenActionMention>> LoadProjectOpenActionMentionsAsync(
        SqlConnection con,
        IReadOnlyList<string> tokens,
        CancellationToken ct)
    {
        var likeClauses = string.Join(" OR ",
            Enumerable.Range(0, tokens.Count).Select(i =>
                $"a.Recommendation LIKE '%' + @t{i} + '%' ESCAPE '\\'"));
        var sql = $@"
SELECT TOP (10)
    co.DisplayName,
    co.Id,
    a.ActionType,
    a.Recommendation,
    a.TimingNotes,
    a.LastSeenAtUtc
FROM opportunities.IntelAction a
INNER JOIN opportunities.CanonicalOrg co ON co.Id = a.CanonicalOrgId
WHERE a.RetiredAtUtc IS NULL
  AND co.RetiredAtUtc IS NULL
  AND a.Status = N'Open'
  AND ( {likeClauses} )
ORDER BY a.LastSeenAtUtc DESC;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        BindProjectMentionTokens(cmd, tokens);
        var rows = new List<ProjectOpenActionMention>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new ProjectOpenActionMention(
                r.GetString(0),
                r.GetInt64(1),
                r.GetString(2),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.GetDateTimeOffset(5)));
        }

        return rows;
    }

    private static void BindProjectMentionTokens(SqlCommand cmd, IReadOnlyList<string> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            cmd.Parameters.Add($"@t{i}", SqlDbType.NVarChar, 200).Value = EscapeLikeQuery(tokens[i]);
        }
    }

    private static async Task<IReadOnlyList<PersonAffiliationWithSeen>> GetPersonAffiliationsAsync(
        SqlConnection con,
        long intelPersonId,
        CancellationToken ct)
    {
        const string sql = @"
SELECT co.DisplayName, a.CanonicalOrgId, a.Title, a.Department, a.IsCurrent,
       a.StartDateApprox, a.EndDateApprox, a.LastSeenAtUtc
FROM opportunities.IntelPersonAffiliation a
JOIN opportunities.CanonicalOrg co ON co.Id = a.CanonicalOrgId
WHERE a.IntelPersonId = @id
  AND a.RetiredAtUtc IS NULL
  AND co.RetiredAtUtc IS NULL
ORDER BY a.IsCurrent DESC, a.LastSeenAtUtc DESC;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = intelPersonId;
        var rows = new List<PersonAffiliationWithSeen>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new PersonAffiliationWithSeen(
                new PersonAffiliationRow(
                    r.GetString(0),
                    r.GetInt64(1),
                    r.IsDBNull(2) ? null : r.GetString(2),
                    r.IsDBNull(3) ? null : r.GetString(3),
                    r.GetBoolean(4),
                    r.IsDBNull(5) ? null : r.GetString(5),
                    r.IsDBNull(6) ? null : r.GetString(6)),
                r.GetDateTimeOffset(7)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<PersonSignalRow>> GetPersonSignalsAsync(
        SqlConnection con,
        string displayName,
        CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 10 s.OccurredAtApprox, s.SignalType, s.Subject, s.Detail, co.DisplayName
FROM opportunities.IntelSignal s
JOIN opportunities.CanonicalOrg co ON co.Id = s.CanonicalOrgId
WHERE s.RetiredAtUtc IS NULL
  AND co.RetiredAtUtc IS NULL
  AND s.LastSeenAtUtc >= DATEADD(DAY, -180, sysdatetimeoffset())
  AND (s.Subject LIKE '%' + @name + '%' ESCAPE '\'
       OR s.Detail LIKE '%' + @name + '%' ESCAPE '\')
ORDER BY s.LastSeenAtUtc DESC;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 300).Value = EscapeLikeQuery(displayName);
        var rows = new List<PersonSignalRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new PersonSignalRow(
                r.IsDBNull(0) ? null : r.GetString(0),
                r.GetString(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<PersonActionRow>> GetPersonActionsAsync(
        SqlConnection con,
        string displayName,
        CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 10 a.ActionType, a.Recommendation, a.TimingNotes, co.DisplayName
FROM opportunities.IntelAction a
JOIN opportunities.CanonicalOrg co ON co.Id = a.CanonicalOrgId
WHERE a.TargetPersonName = @name
  AND a.Status = N'Open'
  AND a.RetiredAtUtc IS NULL
  AND co.RetiredAtUtc IS NULL
ORDER BY a.LastSeenAtUtc DESC;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = displayName;
        var rows = new List<PersonActionRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new PersonActionRow(
                r.GetString(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.GetString(3)));
        }

        return rows;
    }

    private sealed record PersonAffiliationWithSeen(PersonAffiliationRow Row, DateTimeOffset LastSeenAtUtc);

    private static string EscapeLikeQuery(string value)
    {
        return value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal)
            .Replace("[", @"\[", StringComparison.Ordinal);
    }

    private static async Task<int> ScalarIntAsync(SqlConnection con, string sql, Action<SqlCommand> bind, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        bind(cmd);
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return v is null or DBNull ? 0 : Convert.ToInt32(v);
    }

    private static async Task<decimal?> ScalarDecimalAsync(SqlConnection con, string sql, Action<SqlCommand> bind, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        bind(cmd);
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return v is null or DBNull ? null : Convert.ToDecimal(v);
    }

    // Maps a canonical sector bucket name to the WHERE-clause fragment
    // that matches MPI rows in that bucket. Returns null for unknown
    // buckets (defensive).
    private static string? BuildSectorBucketWhere(string bucket)
    {
        return bucket switch
        {
            "Healthcare" =>
                "(mpi.Sector = N'Healthcare' OR mpi.SubSector LIKE N'%hospital%' " +
                "OR mpi.SubSector LIKE N'%health%' OR mpi.ProjectName LIKE N'%hospital%' " +
                "OR mpi.ProjectName LIKE N'%medical%')",
            "K-12" =>
                "(mpi.Sector IN (N'School', N'K-12') " +
                "OR (mpi.Sector = N'Education' AND mpi.SubSector LIKE N'%K-12%') " +
                "OR mpi.SubSector LIKE N'%K-12%' " +
                "OR mpi.ProjectName LIKE N'%elementary%' " +
                "OR mpi.ProjectName LIKE N'%middle school%' " +
                "OR mpi.ProjectName LIKE N'%high school%' " +
                "OR mpi.ProjectName LIKE N'%secondary school%')",
            "Post-Secondary" =>
                "(mpi.Sector IN (N'Post-Secondary', N'Institutional') " +
                "OR (mpi.Sector = N'Education' AND " +
                    "(mpi.SubSector LIKE N'%university%' OR mpi.SubSector LIKE N'%college%')) " +
                "OR mpi.ProjectName LIKE N'%university%' " +
                "OR mpi.ProjectName LIKE N'%college%')",
            "Civic" =>
                "(mpi.Sector IN (N'Civic', N'Government', N'Public Safety') " +
                "OR mpi.ProjectName LIKE N'%fire hall%' " +
                "OR mpi.ProjectName LIKE N'%fire station%' " +
                "OR mpi.ProjectName LIKE N'%city hall%' " +
                "OR mpi.ProjectName LIKE N'%library%' " +
                "OR mpi.ProjectName LIKE N'%community centre%')",
            "Recreation" =>
                "(mpi.Sector IN (N'recreation', N'Recreation') " +
                "OR mpi.SubSector LIKE N'%recreation%' " +
                "OR mpi.ProjectName LIKE N'%aquatic%' " +
                "OR mpi.ProjectName LIKE N'%arena%' " +
                "OR mpi.ProjectName LIKE N'%pool%' " +
                "OR mpi.ProjectName LIKE N'%gymnasium%')",
            "Residential-LowRise" =>
                "(mpi.Sector IN (N'Residential', N'Housing') " +
                "AND (mpi.SubSector IS NULL OR mpi.SubSector NOT LIKE N'%High%') " +
                "AND mpi.ProjectName NOT LIKE N'%tower%' " +
                "AND mpi.ProjectName NOT LIKE N'%high-rise%')",
            "Residential-HighRise" =>
                "(mpi.Sector = N'Residential High-Rise' " +
                "OR (mpi.Sector IN (N'Residential', N'Housing', N'Mixed-use') " +
                "    AND (mpi.ProjectName LIKE N'%tower%' " +
                "         OR mpi.ProjectName LIKE N'%high-rise%' " +
                "         OR mpi.ProjectName LIKE N'%highrise%')))",
            "Industrial-Other" =>
                "(mpi.Sector IN (N'Industrial', N'Commercial', N'Mixed-use', N'Other', N'Manufacturing'))",
            _ => null,
        };
    }

    private static string? BuildStructuralTypeWhere(string type)
    {
        return type switch
        {
            "Wood-Frame" =>
                "(mpi.ProjectName LIKE N'%wood frame%' " +
                "OR mpi.ProjectName LIKE N'%woodframe%' " +
                "OR mpi.ProjectDescription LIKE N'%wood frame%' " +
                "OR mpi.ProjectDescription LIKE N'%woodframe%')",
            "Concrete-Tower" =>
                "(mpi.ProjectName LIKE N'%tower%' " +
                "OR mpi.ProjectName LIKE N'%high-rise%' " +
                "OR mpi.ProjectName LIKE N'%highrise%' " +
                "OR mpi.Sector = N'Residential High-Rise')",
            "Mass-Timber" =>
                "(mpi.ProjectName LIKE N'%mass timber%' " +
                "OR mpi.ProjectName LIKE N'%mass-timber%' " +
                "OR mpi.ProjectName LIKE N'%CLT%' " +
                "OR mpi.ProjectDescription LIKE N'%mass timber%' " +
                "OR mpi.ProjectDescription LIKE N'%cross-laminated%')",
            "Steel" =>
                "(mpi.ProjectName LIKE N'%steel frame%' " +
                "OR mpi.ProjectName LIKE N'%steel structure%' " +
                "OR mpi.ProjectName LIKE N'%steel-framed%' " +
                "OR mpi.ProjectDescription LIKE N'%steel frame%' " +
                "OR mpi.ProjectDescription LIKE N'%steel structure%')",
            "Hybrid" =>
                "(mpi.ProjectName LIKE N'%hybrid%' " +
                "OR mpi.ProjectDescription LIKE N'%hybrid%')",
            _ => null,
        };
    }

    private static (string MpiWhere, List<SqlParameter> Params) BuildSectorMpiFilter(SectorBriefRequest req)
    {
        var clauses = new List<string>
        {
            "mpi.RetiredAtUtc IS NULL",
            "mpi.Province = @province",
        };
        var ps = new List<SqlParameter>
        {
            new("@province", SqlDbType.NVarChar, 100) { Value = req.Province },
        };

        if (!string.IsNullOrWhiteSpace(req.City))
        {
            clauses.Add("mpi.MunicipalityName = @city");
            ps.Add(new SqlParameter("@city", SqlDbType.NVarChar, 200) { Value = req.City });
        }

        var sectorFrags = req.SectorBuckets
            .Select(BuildSectorBucketWhere)
            .Where(f => f is not null)
            .Select(f => f!)
            .ToList();
        if (sectorFrags.Count > 0)
        {
            clauses.Add("(" + string.Join(" OR ", sectorFrags) + ")");
        }

        var typeFrags = req.StructuralTypes
            .Select(BuildStructuralTypeWhere)
            .Where(f => f is not null)
            .Select(f => f!)
            .ToList();
        if (typeFrags.Count > 0)
        {
            clauses.Add("(" + string.Join(" OR ", typeFrags) + ")");
        }

        if (req.IndigenousOnly)
        {
            clauses.Add("mpi.IndigenousInd = 1");
            if (!string.IsNullOrWhiteSpace(req.IndigenousNationFilter))
            {
                clauses.Add("mpi.IndigenousNames LIKE '%' + @nation + '%' ESCAPE '\\'");
                ps.Add(new SqlParameter("@nation", SqlDbType.NVarChar, 200)
                    { Value = EscapeLikeQuery(req.IndigenousNationFilter.Trim()) });
            }
        }

        if (!string.IsNullOrWhiteSpace(req.ExtraKeyword))
        {
            clauses.Add(
                "(mpi.ProjectName LIKE '%' + @extraKw + '%' ESCAPE '\\' " +
                "OR mpi.ProjectDescription LIKE '%' + @extraKw + '%' ESCAPE '\\')");
            ps.Add(new SqlParameter("@extraKw", SqlDbType.NVarChar, 300)
                { Value = EscapeLikeQuery(req.ExtraKeyword.Trim()) });
        }

        return (string.Join(" AND ", clauses), ps);
    }

    private static (string Where, List<SqlParameter> Params) BuildSectorOpportunityFilter(SectorBriefRequest req)
    {
        var clauses = new List<string>
        {
            "o.Status = 1",
            "o.IsPrimeConsultantRfp = 1",
            "o.ProjectProvince = @oppProvince",
        };
        var ps = new List<SqlParameter>
        {
            new("@oppProvince", SqlDbType.NVarChar, 100) { Value = req.Province },
        };

        if (!string.IsNullOrWhiteSpace(req.City))
        {
            clauses.Add("o.ProjectCity = @oppCity");
            ps.Add(new SqlParameter("@oppCity", SqlDbType.NVarChar, 200) { Value = req.City });
        }

        AddSectorTextClauses(req.SectorBuckets, clauses, "o.PrimeProjectSector", "o.Name");
        AddStructuralTextClauses(req.StructuralTypes, clauses, "o.Name", "o.Name");

        if (req.IndigenousOnly)
        {
            clauses.Add("(o.Name LIKE N'%Indigenous%' OR o.BuyerName LIKE N'%First Nation%' OR o.BuyerName LIKE N'%Nation%')");
            if (!string.IsNullOrWhiteSpace(req.IndigenousNationFilter))
            {
                clauses.Add("(o.Name LIKE '%' + @oppNation + '%' ESCAPE '\\' OR o.BuyerName LIKE '%' + @oppNation + '%' ESCAPE '\\')");
                ps.Add(new SqlParameter("@oppNation", SqlDbType.NVarChar, 200) { Value = EscapeLikeQuery(req.IndigenousNationFilter.Trim()) });
            }
        }

        if (!string.IsNullOrWhiteSpace(req.ExtraKeyword))
        {
            clauses.Add("(o.Name LIKE '%' + @oppExtraKw + '%' ESCAPE '\\' OR o.BuyerName LIKE '%' + @oppExtraKw + '%' ESCAPE '\\')");
            ps.Add(new SqlParameter("@oppExtraKw", SqlDbType.NVarChar, 300) { Value = EscapeLikeQuery(req.ExtraKeyword.Trim()) });
        }

        return (string.Join(" AND ", clauses), ps);
    }

    private static (string Where, List<SqlParameter> Params) BuildSectorAwardFilter(SectorBriefRequest req)
    {
        var clauses = new List<string>
        {
            "(a.IssuingLocation LIKE '%' + @awardProvince + '%' OR a.Title LIKE '%' + @awardProvince + '%')",
        };
        var ps = new List<SqlParameter>
        {
            new("@awardProvince", SqlDbType.NVarChar, 100) { Value = req.Province },
        };

        if (!string.IsNullOrWhiteSpace(req.City))
        {
            clauses.Add("(a.IssuingLocation LIKE '%' + @awardCity + '%' ESCAPE '\\' OR a.Title LIKE '%' + @awardCity + '%' ESCAPE '\\')");
            ps.Add(new SqlParameter("@awardCity", SqlDbType.NVarChar, 200) { Value = EscapeLikeQuery(req.City.Trim()) });
        }

        AddSectorTextClauses(req.SectorBuckets, clauses, "a.Title", "a.Title");
        AddStructuralTextClauses(req.StructuralTypes, clauses, "a.Title", "a.Title");

        if (req.IndigenousOnly)
        {
            clauses.Add("(a.Title LIKE N'%Indigenous%' OR a.AwardingOrganization LIKE N'%First Nation%' OR a.AwardingOrganization LIKE N'%Nation%')");
            if (!string.IsNullOrWhiteSpace(req.IndigenousNationFilter))
            {
                clauses.Add("(a.Title LIKE '%' + @awardNation + '%' ESCAPE '\\' OR a.AwardingOrganization LIKE '%' + @awardNation + '%' ESCAPE '\\')");
                ps.Add(new SqlParameter("@awardNation", SqlDbType.NVarChar, 200) { Value = EscapeLikeQuery(req.IndigenousNationFilter.Trim()) });
            }
        }

        if (!string.IsNullOrWhiteSpace(req.ExtraKeyword))
        {
            clauses.Add("(a.Title LIKE '%' + @awardExtraKw + '%' ESCAPE '\\' OR a.AwardingOrganization LIKE '%' + @awardExtraKw + '%' ESCAPE '\\' OR a.AwardedToOrganization LIKE '%' + @awardExtraKw + '%' ESCAPE '\\')");
            ps.Add(new SqlParameter("@awardExtraKw", SqlDbType.NVarChar, 300) { Value = EscapeLikeQuery(req.ExtraKeyword.Trim()) });
        }

        return (string.Join(" AND ", clauses), ps);
    }

    private static void AddSectorTextClauses(
        IReadOnlyList<string> buckets,
        List<string> clauses,
        string sectorColumn,
        string titleColumn)
    {
        var frags = buckets.Select(bucket => bucket switch
            {
                "Healthcare" => $"({sectorColumn} LIKE N'%health%' OR {titleColumn} LIKE N'%hospital%' OR {titleColumn} LIKE N'%medical%')",
                "K-12" => $"({sectorColumn} LIKE N'%school%' OR {titleColumn} LIKE N'%elementary%' OR {titleColumn} LIKE N'%middle school%' OR {titleColumn} LIKE N'%high school%' OR {titleColumn} LIKE N'%secondary school%')",
                "Post-Secondary" => $"({sectorColumn} LIKE N'%post%' OR {titleColumn} LIKE N'%university%' OR {titleColumn} LIKE N'%college%')",
                "Civic" => $"({sectorColumn} LIKE N'%civic%' OR {sectorColumn} LIKE N'%government%' OR {titleColumn} LIKE N'%fire hall%' OR {titleColumn} LIKE N'%city hall%' OR {titleColumn} LIKE N'%library%' OR {titleColumn} LIKE N'%community centre%')",
                "Recreation" => $"({sectorColumn} LIKE N'%recreation%' OR {titleColumn} LIKE N'%aquatic%' OR {titleColumn} LIKE N'%arena%' OR {titleColumn} LIKE N'%pool%' OR {titleColumn} LIKE N'%gymnasium%')",
                "Residential-LowRise" => $"({sectorColumn} LIKE N'%residential%' AND {titleColumn} NOT LIKE N'%tower%' AND {titleColumn} NOT LIKE N'%high-rise%' AND {titleColumn} NOT LIKE N'%highrise%')",
                "Residential-HighRise" => $"({sectorColumn} LIKE N'%high%' OR ({sectorColumn} LIKE N'%residential%' AND ({titleColumn} LIKE N'%tower%' OR {titleColumn} LIKE N'%high-rise%' OR {titleColumn} LIKE N'%highrise%')))",
                "Industrial-Other" => $"({sectorColumn} LIKE N'%industrial%' OR {sectorColumn} LIKE N'%commercial%' OR {sectorColumn} LIKE N'%mixed%' OR {sectorColumn} LIKE N'%manufacturing%')",
                _ => null,
            })
            .Where(f => f is not null)
            .Select(f => f!)
            .ToList();

        if (frags.Count > 0)
        {
            clauses.Add("(" + string.Join(" OR ", frags) + ")");
        }
    }

    private static void AddStructuralTextClauses(
        IReadOnlyList<string> types,
        List<string> clauses,
        string titleColumn,
        string descriptionColumn)
    {
        var frags = types.Select(type => type switch
            {
                "Wood-Frame" => $"({titleColumn} LIKE N'%wood frame%' OR {titleColumn} LIKE N'%woodframe%' OR {descriptionColumn} LIKE N'%wood frame%' OR {descriptionColumn} LIKE N'%woodframe%')",
                "Concrete-Tower" => $"({titleColumn} LIKE N'%tower%' OR {titleColumn} LIKE N'%high-rise%' OR {titleColumn} LIKE N'%highrise%')",
                "Mass-Timber" => $"({titleColumn} LIKE N'%mass timber%' OR {titleColumn} LIKE N'%mass-timber%' OR {titleColumn} LIKE N'%CLT%' OR {descriptionColumn} LIKE N'%mass timber%')",
                "Steel" => $"({titleColumn} LIKE N'%steel frame%' OR {titleColumn} LIKE N'%steel structure%' OR {titleColumn} LIKE N'%steel-framed%' OR {descriptionColumn} LIKE N'%steel frame%')",
                "Hybrid" => $"({titleColumn} LIKE N'%hybrid%' OR {descriptionColumn} LIKE N'%hybrid%')",
                _ => null,
            })
            .Where(f => f is not null)
            .Select(f => f!)
            .ToList();

        if (frags.Count > 0)
        {
            clauses.Add("(" + string.Join(" OR ", frags) + ")");
        }
    }

    private static void AddClonedParameters(SqlCommand cmd, IReadOnlyList<SqlParameter> parameters)
    {
        foreach (var p in parameters)
        {
            var clone = new SqlParameter(p.ParameterName, p.SqlDbType, p.Size)
            {
                Value = p.Value,
            };
            cmd.Parameters.Add(clone);
        }
    }

    private static async Task<IReadOnlyList<RegionLiveRfp>> GetSectorLiveRfpsAsync(
        SqlConnection con,
        string oppWhere,
        IReadOnlyList<SqlParameter> parameters,
        CancellationToken ct)
    {
        var sql = $@"
SELECT TOP (15) o.Id, o.Name, o.BuyerName, o.SubmissionDeadlineUtc, o.PrimeProjectSector, o.PrimeConfidence
FROM opportunities.Opportunities o
WHERE {oppWhere}
ORDER BY o.PrimeConfidence DESC, o.SubmissionDeadlineUtc ASC;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        AddClonedParameters(cmd, parameters);
        var rows = new List<RegionLiveRfp>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new RegionLiveRfp(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetDateTimeOffset(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? 0m : r.GetDecimal(5)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<RegionForwardProject>> GetSectorForwardProjectsAsync(
        SqlConnection con,
        string mpiWhere,
        IReadOnlyList<SqlParameter> parameters,
        CancellationToken ct)
    {
        var sql = $@"
SELECT TOP (15) mpi.Id, mpi.ProjectName, mpi.ProponentName, mpi.Stage, mpi.EstimatedCostCad
FROM opportunities.MajorProjectsInventory mpi
WHERE {mpiWhere}
ORDER BY mpi.EstimatedCostCad DESC, mpi.ProjectName;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        AddClonedParameters(cmd, parameters);
        var rows = new List<RegionForwardProject>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new RegionForwardProject(
                r.GetInt64(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetDecimal(4)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<SectorRecentAward>> GetSectorRecentAwardsAsync(
        SqlConnection con,
        string awardWhere,
        IReadOnlyList<SqlParameter> parameters,
        CancellationToken ct)
    {
        var sql = $@"
SELECT TOP (15) a.Id, a.Title, a.AwardingOrganization, a.AwardedToOrganization, a.ContractValue, a.AwardedAtUtc
FROM opportunities.OpportunityAwards a
WHERE {awardWhere}
  AND a.AwardedAtUtc >= DATEADD(DAY, -365, sysdatetimeoffset())
ORDER BY a.AwardedAtUtc DESC, a.Id DESC;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        AddClonedParameters(cmd, parameters);
        var rows = new List<SectorRecentAward>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new SectorRecentAward(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetDecimal(4),
                r.IsDBNull(5) ? null : r.GetDateTimeOffset(5)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<SectorTopOrg>> GetSectorTopOrgsAsync(
        SqlConnection con,
        string mpiWhere,
        IReadOnlyList<SqlParameter> parameters,
        string fkColumn,
        bool includeKorJointCount,
        long korId,
        bool excludeKor,
        CancellationToken ct)
    {
        var korJointSql = includeKorJointCount
            ? "SUM(CASE WHEN mpi.StructuralEngineerCanonicalOrgId = @kor THEN 1 ELSE 0 END)"
            : "0";
        var excludeKorSql = excludeKor ? "AND co.Id <> @kor" : "";
        var sql = $@"
SELECT TOP (10)
    co.Id,
    co.DisplayName,
    co.Kind,
    COUNT(*) AS ProjectCount,
    {korJointSql} AS KorJointCount
FROM opportunities.MajorProjectsInventory mpi
JOIN opportunities.CanonicalOrg co ON co.Id = mpi.{fkColumn}
WHERE {mpiWhere}
  AND mpi.{fkColumn} IS NOT NULL
  {excludeKorSql}
  AND co.RetiredAtUtc IS NULL
GROUP BY co.Id, co.DisplayName, co.Kind
ORDER BY ProjectCount DESC, co.DisplayName;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        AddClonedParameters(cmd, parameters);
        if (includeKorJointCount || excludeKor)
        {
            cmd.Parameters.Add("@kor", SqlDbType.BigInt).Value = korId;
        }

        var rows = new List<SectorTopOrg>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new SectorTopOrg(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                Convert.ToInt32(r.GetValue(3)),
                Convert.ToInt32(r.GetValue(4))));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<SectorTopOrg>> GetSectorTopOwnersAsync(
        SqlConnection con,
        string mpiWhere,
        IReadOnlyList<SqlParameter> parameters,
        CancellationToken ct)
    {
        var sql = $@"
SELECT TOP (10)
    co.Id,
    co.DisplayName,
    co.Kind,
    COUNT(*) AS ProjectCount,
    ISNULL(MAX(co.KorProjectsCount), 0) AS KorJointCount
FROM opportunities.MajorProjectsInventory mpi
JOIN opportunities.CanonicalOrg co ON co.Id = mpi.ProponentCanonicalOrgId
WHERE {mpiWhere}
  AND mpi.ProponentCanonicalOrgId IS NOT NULL
  AND co.RetiredAtUtc IS NULL
GROUP BY co.Id, co.DisplayName, co.Kind
ORDER BY ProjectCount DESC, co.DisplayName;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        AddClonedParameters(cmd, parameters);
        var rows = new List<SectorTopOrg>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new SectorTopOrg(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                Convert.ToInt32(r.GetValue(3)),
                Convert.ToInt32(r.GetValue(4))));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<SectorKorPortfolioRow>> GetSectorKorPortfolioAsync(
        SqlConnection con,
        string mpiWhere,
        IReadOnlyList<SqlParameter> parameters,
        long korId,
        CancellationToken ct)
    {
        var sql = $@"
SELECT TOP (15) mpi.Id, mpi.ProjectName, mpi.Stage, mpi.EstimatedCostCad, mpi.MunicipalityName
FROM opportunities.MajorProjectsInventory mpi
WHERE {mpiWhere}
  AND mpi.StructuralEngineerCanonicalOrgId = @kor
ORDER BY mpi.EstimatedCostCad DESC, mpi.ProjectName;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        AddClonedParameters(cmd, parameters);
        cmd.Parameters.Add("@kor", SqlDbType.BigInt).Value = korId;
        var rows = new List<SectorKorPortfolioRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new SectorKorPortfolioRow(
                r.GetInt64(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetDecimal(3),
                r.IsDBNull(4) ? null : r.GetString(4)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<SectorIntelSignalRow>> GetSectorRelevantSignalsAsync(
        SqlConnection con,
        string mpiWhere,
        IReadOnlyList<SqlParameter> parameters,
        CancellationToken ct)
    {
        var sql = $@"
WITH SliceOrgIds AS (
    SELECT DISTINCT v.CanonicalOrgId
    FROM opportunities.MajorProjectsInventory mpi
    CROSS APPLY (VALUES
        (mpi.ArchitectCanonicalOrgId),
        (mpi.ProponentCanonicalOrgId),
        (mpi.GeneralContractorCanonicalOrgId),
        (mpi.StructuralEngineerCanonicalOrgId)
    ) v(CanonicalOrgId)
    WHERE {mpiWhere}
      AND v.CanonicalOrgId IS NOT NULL
)
SELECT TOP (10)
    co.DisplayName,
    s.SignalType,
    s.Subject,
    s.Detail,
    s.OccurredAtApprox,
    s.LastSeenAtUtc
FROM opportunities.IntelSignal s
JOIN SliceOrgIds soi ON soi.CanonicalOrgId = s.CanonicalOrgId
JOIN opportunities.CanonicalOrg co ON co.Id = s.CanonicalOrgId
WHERE s.RetiredAtUtc IS NULL
  AND co.RetiredAtUtc IS NULL
  AND s.LastSeenAtUtc >= DATEADD(DAY, -365, sysdatetimeoffset())
ORDER BY s.LastSeenAtUtc DESC;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        AddClonedParameters(cmd, parameters);
        var rows = new List<SectorIntelSignalRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new SectorIntelSignalRow(
                r.GetString(0),
                r.GetString(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.GetDateTimeOffset(5)));
        }

        return rows;
    }

    // City input is free-text; split common separators into independent LIKE matches.
    private static readonly string[] OpportunityCityColumns = { "ProjectCity" };
    private static readonly string[] MpiCityColumns = { "MunicipalityName", "RegionName" };
    private static readonly string[] AliasedMpiCityColumns = { "m.MunicipalityName", "m.RegionName" };
    private static readonly string[] EventCityColumns = { "City" };

    // R81: City + region-alias tokenization moved to the shared
    // Kor.Opportunities.Data.Intel.IntelRegionTokenizer so the Region Brief
    // and IntelReadService.GetRegion*Async queries cannot drift. Adding a
    // new alias goes in IntelRegionTokenizer only.
    private static IReadOnlyList<string> TokenizeCity(string? city) =>
        Kor.Opportunities.Data.Intel.IntelRegionTokenizer.Tokenize(city);

    private static string BuildCityClause(IReadOnlyList<string> tokens, string[] columns, string paramPrefix)
    {
        if (tokens.Count == 0)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(" AND (");
        for (var i = 0; i < tokens.Count; i++)
        {
            if (i > 0) sb.Append(" OR ");
            for (var j = 0; j < columns.Length; j++)
            {
                if (j > 0) sb.Append(" OR ");
                sb.Append(columns[j]).Append(" LIKE '%' + @").Append(paramPrefix).Append(i).Append(" + '%'");
            }
        }

        sb.Append(')');
        return sb.ToString();
    }

    private static string BuildCityOrClause(IReadOnlyList<string> tokens, string[] columns, string paramPrefix)
    {
        if (tokens.Count == 0)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(" OR (");
        for (var i = 0; i < tokens.Count; i++)
        {
            if (i > 0) sb.Append(" OR ");
            for (var j = 0; j < columns.Length; j++)
            {
                if (j > 0) sb.Append(" OR ");
                sb.Append(columns[j]).Append(" LIKE '%' + @").Append(paramPrefix).Append(i).Append(" + '%'");
            }
        }

        sb.Append(')');
        return sb.ToString();
    }

    private static void BindCityTokens(SqlCommand cmd, IReadOnlyList<string> tokens, string paramPrefix)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            cmd.Parameters.Add("@" + paramPrefix + i, SqlDbType.NVarChar, 150).Value = tokens[i];
        }
    }

    private static async Task<EventMatch?> GetMatchedEventAsync(SqlConnection con, string? province, string? sector, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 1 Name, StartDate, City, Market, SectorsThemes, Audience, TargetsPresent, RegistrationUrl
FROM opportunities.IndustryEvents
WHERE RetiredAtUtc IS NULL
  AND (EndDate IS NULL OR EndDate >= CAST(sysdatetimeoffset() AS date))
  AND (Market LIKE '%' + @prov + '%' OR Market LIKE '%BC%' OR Market LIKE '%British%' OR Market LIKE '%Canada%')
ORDER BY CASE WHEN SectorsThemes LIKE '%' + @sector + '%' THEN 0 ELSE 1 END,
         ISNULL(KorRelevance, 0) DESC, StartDate ASC;";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 50).Value = province ?? string.Empty;
        cmd.Parameters.Add("@sector", SqlDbType.NVarChar, 80).Value = sector ?? string.Empty;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new EventMatch(
            r.GetString(0),
            r.IsDBNull(1) ? null : r.GetDateTime(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5),
            r.IsDBNull(6) ? null : r.GetString(6),
            r.IsDBNull(7) ? null : r.GetString(7));
    }

    // Trusted schema targets (Kind values + FK column names) supplied by callers
    // in this class — never user input. SQL interpolates them as identifiers; row
    // values still flow through parameters.
    private static async Task<IReadOnlyList<RegionTopOrg>> GetTopOrgsAsync(
        SqlConnection con,
        string province,
        IReadOnlyList<string> cityTokens,
        long korId,
        string kind,
        string mpiFkColumn,
        bool includeKorJointSubquery,
        CancellationToken ct)
    {
        var korJointSql = includeKorJointSubquery
            ? "(SELECT COUNT(*) FROM opportunities.MajorProjectsInventory mj WHERE mj." + mpiFkColumn
              + " = c.Id AND mj.StructuralEngineerCanonicalOrgId = @kor)"
            : "0";

        var sql = $@"
SELECT TOP 5 c.Id, c.DisplayName,
  (SELECT COUNT(*) FROM opportunities.MajorProjectsInventory m
     WHERE m.{mpiFkColumn} = c.Id AND m.Province = @prov
       AND m.RetiredAtUtc IS NULL
{BuildCityClause(cityTokens, AliasedMpiCityColumns, "c1")}) AS ProjectCount,
  {korJointSql} AS KorJointCount,
  c.ClendorClientId AS ClendorClientId
FROM opportunities.CanonicalOrg c
WHERE c.Kind = @kind AND EXISTS (
  SELECT 1 FROM opportunities.MajorProjectsInventory m
  WHERE m.{mpiFkColumn} = c.Id AND m.Province = @prov
    AND m.RetiredAtUtc IS NULL
{BuildCityClause(cityTokens, AliasedMpiCityColumns, "c2")})
  AND c.RetiredAtUtc IS NULL
ORDER BY ProjectCount DESC, c.DisplayName;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 20).Value = province;
        BindCityTokens(cmd, cityTokens, "c1");
        BindCityTokens(cmd, cityTokens, "c2");
        cmd.Parameters.Add("@kind", SqlDbType.NVarChar, 40).Value = kind;
        if (includeKorJointSubquery)
        {
            cmd.Parameters.Add("@kor", SqlDbType.BigInt).Value = korId;
        }

        var rows = new List<RegionTopOrg>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new RegionTopOrg(
                r.GetInt64(0),
                r.GetString(1),
                Convert.ToInt32(r.GetValue(2)),
                Convert.ToInt32(r.GetValue(3)),
                r.IsDBNull(4) ? null : r.GetString(4)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<RegionTopOrg>> GetTopOwnersAsync(SqlConnection con, string province, IReadOnlyList<string> cityTokens, CancellationToken ct)
    {
        var sql = $@"
SELECT TOP 5 c.Id, c.DisplayName,
  (SELECT COUNT(*) FROM opportunities.MajorProjectsInventory m
     WHERE m.ProponentCanonicalOrgId = c.Id AND m.Province = @prov
       AND m.RetiredAtUtc IS NULL
{BuildCityClause(cityTokens, AliasedMpiCityColumns, "c1")}) AS ProjectCount,
  ISNULL(c.KorProjectsCount, 0) AS KorJointCount,
  c.ClendorClientId AS ClendorClientId
FROM opportunities.CanonicalOrg c
WHERE c.Kind IN (N'Buyer', N'Client', N'KorClient', N'Developer') AND EXISTS (
  SELECT 1 FROM opportunities.MajorProjectsInventory m
  WHERE m.ProponentCanonicalOrgId = c.Id AND m.Province = @prov
    AND m.RetiredAtUtc IS NULL
{BuildCityClause(cityTokens, AliasedMpiCityColumns, "c2")})
  AND c.RetiredAtUtc IS NULL
ORDER BY ProjectCount DESC, c.DisplayName;";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 20).Value = province;
        BindCityTokens(cmd, cityTokens, "c1");
        BindCityTokens(cmd, cityTokens, "c2");

        var rows = new List<RegionTopOrg>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new RegionTopOrg(
                r.GetInt64(0),
                r.GetString(1),
                Convert.ToInt32(r.GetValue(2)),
                Convert.ToInt32(r.GetValue(3)),
                r.IsDBNull(4) ? null : r.GetString(4)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<RegionLiveRfp>> GetRegionLiveRfpsAsync(SqlConnection con, string province, IReadOnlyList<string> cityTokens, CancellationToken ct)
    {
        var sql = $@"
SELECT TOP 5 Id, Name, BuyerName, SubmissionDeadlineUtc, PrimeProjectSector, PrimeConfidence
FROM opportunities.Opportunities
WHERE Status = 1 AND IsPrimeConsultantRfp = 1 AND ProjectProvince = @prov
{BuildCityClause(cityTokens, OpportunityCityColumns, "c")}
ORDER BY PrimeConfidence DESC, SubmissionDeadlineUtc ASC;";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 20).Value = province;
        BindCityTokens(cmd, cityTokens, "c");

        var rows = new List<RegionLiveRfp>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new RegionLiveRfp(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetDateTimeOffset(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? 0m : r.GetDecimal(5)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<RegionForwardProject>> GetRegionForwardProjectsAsync(SqlConnection con, string province, IReadOnlyList<string> cityTokens, CancellationToken ct)
    {
        var sql = $@"
SELECT TOP 5 Id, ProjectName, ProponentName, Stage, EstimatedCostCad
FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL AND Province = @prov
{BuildCityClause(cityTokens, MpiCityColumns, "c")}
ORDER BY EstimatedCostCad DESC;";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 20).Value = province;
        BindCityTokens(cmd, cityTokens, "c");

        var rows = new List<RegionForwardProject>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new RegionForwardProject(
                r.GetInt64(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetDecimal(4)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<EventMatch>> GetRegionEventsAsync(SqlConnection con, string province, IReadOnlyList<string> cityTokens, CancellationToken ct)
    {
        var sql = $@"
SELECT TOP 5 Name, StartDate, City, Market, SectorsThemes, Audience, TargetsPresent, RegistrationUrl
FROM opportunities.IndustryEvents
WHERE RetiredAtUtc IS NULL
  AND (EndDate IS NULL OR EndDate >= CAST(sysdatetimeoffset() AS date))
  AND (Market LIKE '%' + @prov + '%'{BuildCityOrClause(cityTokens, EventCityColumns, "c")})
ORDER BY ISNULL(KorRelevance, 0) DESC, StartDate ASC;";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 50).Value = province;
        BindCityTokens(cmd, cityTokens, "c");

        var rows = new List<EventMatch>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new EventMatch(
                r.GetString(0),
                r.IsDBNull(1) ? null : r.GetDateTime(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<OrgRecentProject>> ReadRecentProjectsAsync(
        SqlConnection con, string sql, Action<SqlCommand> bind, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        bind(cmd);

        var rows = new List<OrgRecentProject>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new OrgRecentProject(
                r.GetInt64(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : Convert.ToInt32(r.GetValue(2)),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4)));
        }

        return rows;
    }
}
