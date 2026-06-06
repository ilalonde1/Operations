#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
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
                const string sql = "SELECT ISNULL(KorProjectsCount, 0) FROM opportunities.CanonicalOrg WHERE Id = @id;";
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
            gcSummary);
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

    public async Task<OrgBriefData?> GetOrgBriefAsync(long canonicalOrgId, CancellationToken ct)
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
WHERE Id = @id;";
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
        return new OrgBriefData(
            id, kind, displayName, website, korProjects, lastKor,
            recentProjects, korJointCount, korJointProjects, enrichmentJson,
            deltekClientId, Deltek: null)
        {
            Intel = intelBundle,
        };
    }

    // === Helpers ===

    private static async Task<long?> GetKorIdAsync(SqlConnection con, CancellationToken ct)
    {
        const string sql = "SELECT TOP 1 Id FROM opportunities.CanonicalOrg WHERE Kind = N'KorStructural';";
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
WHERE co.Id = @id;";

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
