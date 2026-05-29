#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
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

    public SqlBriefDataStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
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

        return new OpportunityBriefData(
            id, name, buyerName, buyerOrgId, deadline, estVal,
            primeConf, sector, province, city,
            ownerKor, ownerPipeline,
            likelyArch, likelyArchId, likelyArchProjects, korArchJoint,
            matchedEvent);
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
        var cityParam = string.IsNullOrWhiteSpace(city) ? null : city.Trim();

        var liveRfpCount = await ScalarIntAsync(con, @"
SELECT COUNT(*) FROM opportunities.Opportunities
WHERE Status = 1 AND IsPrimeConsultantRfp = 1 AND ProjectProvince = @prov
  AND (@city IS NULL OR ProjectCity LIKE '%' + @city + '%');",
            cmd =>
            {
                cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 20).Value = province;
                cmd.Parameters.Add("@city", SqlDbType.NVarChar, 150).Value = (object?)cityParam ?? DBNull.Value;
            }, ct).ConfigureAwait(false);

        var forwardCount = await ScalarIntAsync(con, @"
SELECT COUNT(*) FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL AND Province = @prov
  AND (@city IS NULL OR MunicipalityName LIKE '%' + @city + '%')
  AND ProjectStage IN (N'CapitalPlan', N'FacilityRenewal');",
            cmd =>
            {
                cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 20).Value = province;
                cmd.Parameters.Add("@city", SqlDbType.NVarChar, 150).Value = (object?)cityParam ?? DBNull.Value;
            }, ct).ConfigureAwait(false);

        var activeMpiCount = await ScalarIntAsync(con, @"
SELECT COUNT(*) FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL AND Province = @prov
  AND (@city IS NULL OR MunicipalityName LIKE '%' + @city + '%');",
            cmd =>
            {
                cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 20).Value = province;
                cmd.Parameters.Add("@city", SqlDbType.NVarChar, 150).Value = (object?)cityParam ?? DBNull.Value;
            }, ct).ConfigureAwait(false);

        var topArchitects = await GetTopOrgsAsync(con, province, cityParam, korIdVal, "Architect", "ArchitectCanonicalOrgId", includeKorJointSubquery: true, ct).ConfigureAwait(false);
        var topOwners = await GetTopOwnersAsync(con, province, cityParam, ct).ConfigureAwait(false);
        var topCompetitors = await GetTopOrgsAsync(con, province, cityParam, korIdVal, "Competitor", "StructuralEngineerCanonicalOrgId", includeKorJointSubquery: false, ct).ConfigureAwait(false);
        var liveRfps = await GetRegionLiveRfpsAsync(con, province, cityParam, ct).ConfigureAwait(false);
        var forwardProjects = await GetRegionForwardProjectsAsync(con, province, cityParam, ct).ConfigureAwait(false);
        var events = await GetRegionEventsAsync(con, province, cityParam, ct).ConfigureAwait(false);

        return new RegionBriefData(
            province, cityParam,
            liveRfpCount, forwardCount, activeMpiCount,
            topArchitects, topOwners, topCompetitors,
            liveRfps, forwardProjects, events);
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
        {
            const string sql = @"
SELECT Id, Kind, DisplayName, Website, ISNULL(KorProjectsCount, 0) AS KorProjectsCount, LastKorProjectAtUtc
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
            lastKor = r.IsDBNull(5) ? null : r.GetDateTimeOffset(5);
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

        return new OrgBriefData(
            id, kind, displayName, website, korProjects, lastKor,
            recentProjects, korJointCount, korJointProjects, enrichmentJson);
    }

    // === Helpers ===

    private static async Task<long?> GetKorIdAsync(SqlConnection con, CancellationToken ct)
    {
        const string sql = "SELECT TOP 1 Id FROM opportunities.CanonicalOrg WHERE Kind = N'KorStructural';";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return v is null or DBNull ? null : Convert.ToInt64(v);
    }

    private static async Task<int> ScalarIntAsync(SqlConnection con, string sql, Action<SqlCommand> bind, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        bind(cmd);
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return v is null or DBNull ? 0 : Convert.ToInt32(v);
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
        string? city,
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
       AND (@city IS NULL OR m.MunicipalityName LIKE '%' + @city + '%')) AS ProjectCount,
  {korJointSql} AS KorJointCount
FROM opportunities.CanonicalOrg c
WHERE c.Kind = @kind AND EXISTS (
  SELECT 1 FROM opportunities.MajorProjectsInventory m
  WHERE m.{mpiFkColumn} = c.Id AND m.Province = @prov
    AND m.RetiredAtUtc IS NULL
    AND (@city IS NULL OR m.MunicipalityName LIKE '%' + @city + '%'))
ORDER BY ProjectCount DESC, c.DisplayName;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 20).Value = province;
        cmd.Parameters.Add("@city", SqlDbType.NVarChar, 150).Value = (object?)city ?? DBNull.Value;
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
                Convert.ToInt32(r.GetValue(3))));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<RegionTopOrg>> GetTopOwnersAsync(SqlConnection con, string province, string? city, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 5 c.Id, c.DisplayName,
  (SELECT COUNT(*) FROM opportunities.MajorProjectsInventory m
     WHERE m.ProponentCanonicalOrgId = c.Id AND m.Province = @prov
       AND m.RetiredAtUtc IS NULL
       AND (@city IS NULL OR m.MunicipalityName LIKE '%' + @city + '%')) AS ProjectCount,
  ISNULL(c.KorProjectsCount, 0) AS KorJointCount
FROM opportunities.CanonicalOrg c
WHERE c.Kind IN (N'Buyer', N'Client', N'KorClient', N'Developer') AND EXISTS (
  SELECT 1 FROM opportunities.MajorProjectsInventory m
  WHERE m.ProponentCanonicalOrgId = c.Id AND m.Province = @prov
    AND m.RetiredAtUtc IS NULL
    AND (@city IS NULL OR m.MunicipalityName LIKE '%' + @city + '%'))
ORDER BY ProjectCount DESC, c.DisplayName;";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 20).Value = province;
        cmd.Parameters.Add("@city", SqlDbType.NVarChar, 150).Value = (object?)city ?? DBNull.Value;

        var rows = new List<RegionTopOrg>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new RegionTopOrg(
                r.GetInt64(0),
                r.GetString(1),
                Convert.ToInt32(r.GetValue(2)),
                Convert.ToInt32(r.GetValue(3))));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<RegionLiveRfp>> GetRegionLiveRfpsAsync(SqlConnection con, string province, string? city, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 5 Id, Name, BuyerName, SubmissionDeadlineUtc, PrimeProjectSector, PrimeConfidence
FROM opportunities.Opportunities
WHERE Status = 1 AND IsPrimeConsultantRfp = 1 AND ProjectProvince = @prov
  AND (@city IS NULL OR ProjectCity LIKE '%' + @city + '%')
ORDER BY PrimeConfidence DESC, SubmissionDeadlineUtc ASC;";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 20).Value = province;
        cmd.Parameters.Add("@city", SqlDbType.NVarChar, 150).Value = (object?)city ?? DBNull.Value;

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

    private static async Task<IReadOnlyList<RegionForwardProject>> GetRegionForwardProjectsAsync(SqlConnection con, string province, string? city, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 5 Id, ProjectName, ProponentName, Stage, EstimatedCostCad
FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL AND Province = @prov
  AND (@city IS NULL OR MunicipalityName LIKE '%' + @city + '%')
ORDER BY EstimatedCostCad DESC;";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 20).Value = province;
        cmd.Parameters.Add("@city", SqlDbType.NVarChar, 150).Value = (object?)city ?? DBNull.Value;

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

    private static async Task<IReadOnlyList<EventMatch>> GetRegionEventsAsync(SqlConnection con, string province, string? city, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 5 Name, StartDate, City, Market, SectorsThemes, Audience, TargetsPresent, RegistrationUrl
FROM opportunities.IndustryEvents
WHERE RetiredAtUtc IS NULL
  AND (EndDate IS NULL OR EndDate >= CAST(sysdatetimeoffset() AS date))
  AND (Market LIKE '%' + @prov + '%' OR (@city IS NOT NULL AND City LIKE '%' + @city + '%'))
ORDER BY ISNULL(KorRelevance, 0) DESC, StartDate ASC;";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 50).Value = province;
        cmd.Parameters.Add("@city", SqlDbType.NVarChar, 150).Value = (object?)city ?? DBNull.Value;

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
