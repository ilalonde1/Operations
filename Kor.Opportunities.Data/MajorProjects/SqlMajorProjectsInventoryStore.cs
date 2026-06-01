#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.MajorProjects;

public sealed class SqlMajorProjectsInventoryStore : IMajorProjectsInventoryStore
{
    private const int CommandTimeoutSeconds = 60;

    private const string AllColumns = @"
Id, Province, ProjectName, Sector, SubSector, ConstructionType, ProjectType, ProjectCategoryName,
Stage, ProjectStage, ProjectStatus, SeatStatus, EstimatedCostCad, EstimatedCostText,
ProponentName, ProponentCanonicalOrgId, ArchitectName, ArchitectCanonicalOrgId,
MunicipalityName, RegionName, StartYear, CompletionYear, StandardizedStartDate, StandardizedCompletionDate,
IndigenousInd, IndigenousNames, PublicFundingInd, ProvincialFunding, FederalFunding, MunicipalFunding, GreenBuildingInd,
ConstructionJobs, OperatingJobs, Latitude, Longitude, ProjectDescription, ProjectWebsite, SourceUrl,
IssueYear, IssueQuarter, LastSeenAtUtc";

    private readonly string _connectionString;

    public SqlMajorProjectsInventoryStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<MajorProjectRow>> ListAllAsync(CancellationToken ct)
    {
        var sql = $@"
SELECT {AllColumns}
FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL
ORDER BY EstimatedCostCad DESC, ProjectName;";

        return await ReadRowsAsync(sql, null, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MajorProjectRow>> ListByCanonicalOrgAsync(long canonicalOrgId, CancellationToken ct)
    {
        var sql = $@"
SELECT {AllColumns}
FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL
  AND (ProponentCanonicalOrgId = @id OR ArchitectCanonicalOrgId = @id)
ORDER BY EstimatedCostCad DESC, ProjectName;";

        return await ReadRowsAsync(sql, canonicalOrgId, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<MajorProjectRow>> ReadRowsAsync(string sql, long? canonicalOrgId, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        if (canonicalOrgId.HasValue)
        {
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId.Value;
        }

        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<MajorProjectRow>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(MapReader(r));
        }

        return rows;
    }

    public async Task<MajorProjectsFilterOptions> GetFilterOptionsAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT DISTINCT Province
FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL
  AND Province IS NOT NULL AND LTRIM(RTRIM(Province)) <> N''
ORDER BY Province;

SELECT DISTINCT StageName
FROM (
    SELECT ProjectStage AS StageName FROM opportunities.MajorProjectsInventory WHERE RetiredAtUtc IS NULL
    UNION
    SELECT Stage AS StageName FROM opportunities.MajorProjectsInventory WHERE RetiredAtUtc IS NULL
) s
WHERE StageName IS NOT NULL AND LTRIM(RTRIM(StageName)) <> N''
ORDER BY StageName;

SELECT DISTINCT Sector
FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL
  AND Sector IS NOT NULL AND LTRIM(RTRIM(Sector)) <> N''
ORDER BY Sector;

SELECT DISTINCT RegionName
FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL
  AND RegionName IS NOT NULL AND LTRIM(RTRIM(RegionName)) <> N''
ORDER BY RegionName;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var provinces = await ReadStringResultAsync(r, ct).ConfigureAwait(false);
        await r.NextResultAsync(ct).ConfigureAwait(false);
        var stages = await ReadStringResultAsync(r, ct).ConfigureAwait(false);
        await r.NextResultAsync(ct).ConfigureAwait(false);
        var sectors = await ReadStringResultAsync(r, ct).ConfigureAwait(false);
        await r.NextResultAsync(ct).ConfigureAwait(false);
        var regions = await ReadStringResultAsync(r, ct).ConfigureAwait(false);

        return new MajorProjectsFilterOptions(provinces, stages, sectors, regions);
    }

    private static async Task<IReadOnlyList<string>> ReadStringResultAsync(SqlDataReader r, CancellationToken ct)
    {
        var values = new List<string>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            if (!r.IsDBNull(0))
            {
                var value = r.GetString(0).Trim();
                if (value.Length > 0)
                {
                    values.Add(value);
                }
            }
        }

        return values;
    }

    private static MajorProjectRow MapReader(SqlDataReader r) => new(
        Id: r.GetInt64(0),
        Province: r.GetString(1),
        ProjectName: r.GetString(2),
        Sector: r.IsDBNull(3) ? null : r.GetString(3),
        SubSector: r.IsDBNull(4) ? null : r.GetString(4),
        ConstructionType: r.IsDBNull(5) ? null : r.GetString(5),
        ProjectType: r.IsDBNull(6) ? null : r.GetString(6),
        ProjectCategoryName: r.IsDBNull(7) ? null : r.GetString(7),
        Stage: r.IsDBNull(8) ? null : r.GetString(8),
        ProjectStage: r.IsDBNull(9) ? null : r.GetString(9),
        ProjectStatus: r.IsDBNull(10) ? null : r.GetString(10),
        SeatStatus: r.IsDBNull(11) ? null : r.GetString(11),
        EstimatedCostCad: r.IsDBNull(12) ? null : r.GetDecimal(12),
        EstimatedCostText: r.IsDBNull(13) ? null : r.GetString(13),
        ProponentName: r.IsDBNull(14) ? null : r.GetString(14),
        ProponentCanonicalOrgId: r.IsDBNull(15) ? null : r.GetInt64(15),
        ArchitectName: r.IsDBNull(16) ? null : r.GetString(16),
        ArchitectCanonicalOrgId: r.IsDBNull(17) ? null : r.GetInt64(17),
        MunicipalityName: r.IsDBNull(18) ? null : r.GetString(18),
        RegionName: r.IsDBNull(19) ? null : r.GetString(19),
        StartYear: r.IsDBNull(20) ? null : r.GetInt16(20),
        CompletionYear: r.IsDBNull(21) ? null : r.GetInt16(21),
        StandardizedStartDate: r.IsDBNull(22) ? null : r.GetString(22),
        StandardizedCompletionDate: r.IsDBNull(23) ? null : r.GetString(23),
        IndigenousInd: r.IsDBNull(24) ? null : r.GetBoolean(24),
        IndigenousNames: r.IsDBNull(25) ? null : r.GetString(25),
        PublicFundingInd: r.IsDBNull(26) ? null : r.GetBoolean(26),
        ProvincialFunding: r.IsDBNull(27) ? null : r.GetBoolean(27),
        FederalFunding: r.IsDBNull(28) ? null : r.GetBoolean(28),
        MunicipalFunding: r.IsDBNull(29) ? null : r.GetBoolean(29),
        GreenBuildingInd: r.IsDBNull(30) ? null : r.GetBoolean(30),
        ConstructionJobs: r.IsDBNull(31) ? null : r.GetInt32(31),
        OperatingJobs: r.IsDBNull(32) ? null : r.GetInt32(32),
        Latitude: r.IsDBNull(33) ? null : r.GetDecimal(33),
        Longitude: r.IsDBNull(34) ? null : r.GetDecimal(34),
        ProjectDescription: r.IsDBNull(35) ? null : r.GetString(35),
        ProjectWebsite: r.IsDBNull(36) ? null : r.GetString(36),
        SourceUrl: r.IsDBNull(37) ? null : r.GetString(37),
        IssueYear: r.IsDBNull(38) ? null : r.GetInt16(38),
        IssueQuarter: r.IsDBNull(39) ? null : r.GetByte(39),
        LastSeenAtUtc: r.GetDateTimeOffset(40));
}
