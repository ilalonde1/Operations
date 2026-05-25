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
Stage, ProjectStage, ProjectStatus, EstimatedCostCad, EstimatedCostText,
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
ORDER BY EstimatedCostCad DESC, ProjectName;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
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
WHERE Province IS NOT NULL AND LTRIM(RTRIM(Province)) <> N''
ORDER BY Province;

SELECT DISTINCT StageName
FROM (
    SELECT ProjectStage AS StageName FROM opportunities.MajorProjectsInventory
    UNION
    SELECT Stage AS StageName FROM opportunities.MajorProjectsInventory
) s
WHERE StageName IS NOT NULL AND LTRIM(RTRIM(StageName)) <> N''
ORDER BY StageName;

SELECT DISTINCT Sector
FROM opportunities.MajorProjectsInventory
WHERE Sector IS NOT NULL AND LTRIM(RTRIM(Sector)) <> N''
ORDER BY Sector;

SELECT DISTINCT RegionName
FROM opportunities.MajorProjectsInventory
WHERE RegionName IS NOT NULL AND LTRIM(RTRIM(RegionName)) <> N''
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
        EstimatedCostCad: r.IsDBNull(11) ? null : r.GetDecimal(11),
        EstimatedCostText: r.IsDBNull(12) ? null : r.GetString(12),
        ProponentName: r.IsDBNull(13) ? null : r.GetString(13),
        ProponentCanonicalOrgId: r.IsDBNull(14) ? null : r.GetInt64(14),
        ArchitectName: r.IsDBNull(15) ? null : r.GetString(15),
        ArchitectCanonicalOrgId: r.IsDBNull(16) ? null : r.GetInt64(16),
        MunicipalityName: r.IsDBNull(17) ? null : r.GetString(17),
        RegionName: r.IsDBNull(18) ? null : r.GetString(18),
        StartYear: r.IsDBNull(19) ? null : r.GetInt16(19),
        CompletionYear: r.IsDBNull(20) ? null : r.GetInt16(20),
        StandardizedStartDate: r.IsDBNull(21) ? null : r.GetString(21),
        StandardizedCompletionDate: r.IsDBNull(22) ? null : r.GetString(22),
        IndigenousInd: r.IsDBNull(23) ? null : r.GetBoolean(23),
        IndigenousNames: r.IsDBNull(24) ? null : r.GetString(24),
        PublicFundingInd: r.IsDBNull(25) ? null : r.GetBoolean(25),
        ProvincialFunding: r.IsDBNull(26) ? null : r.GetBoolean(26),
        FederalFunding: r.IsDBNull(27) ? null : r.GetBoolean(27),
        MunicipalFunding: r.IsDBNull(28) ? null : r.GetBoolean(28),
        GreenBuildingInd: r.IsDBNull(29) ? null : r.GetBoolean(29),
        ConstructionJobs: r.IsDBNull(30) ? null : r.GetInt32(30),
        OperatingJobs: r.IsDBNull(31) ? null : r.GetInt32(31),
        Latitude: r.IsDBNull(32) ? null : r.GetDecimal(32),
        Longitude: r.IsDBNull(33) ? null : r.GetDecimal(33),
        ProjectDescription: r.IsDBNull(34) ? null : r.GetString(34),
        ProjectWebsite: r.IsDBNull(35) ? null : r.GetString(35),
        SourceUrl: r.IsDBNull(36) ? null : r.GetString(36),
        IssueYear: r.IsDBNull(37) ? null : r.GetInt16(37),
        IssueQuarter: r.IsDBNull(38) ? null : r.GetByte(38),
        LastSeenAtUtc: r.GetDateTimeOffset(39));
}
