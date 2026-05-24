#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Awards;

public sealed class SqlBuildingPermitStore : IBuildingPermitStore
{
    private const int CommandTimeoutSeconds = 30;
    private readonly string _connectionString;

    public SqlBuildingPermitStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<PermitSourceRow>> ListActiveSourcesAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT Id, Name, Adapter, Endpoint, Region, Municipality, IsActive, LastPolledAtUtc
FROM   opportunities.PermitSource
WHERE  IsActive = 1
ORDER  BY Id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };

        var list = new List<PermitSourceRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new PermitSourceRow(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.GetBoolean(6),
                r.IsDBNull(7) ? (DateTimeOffset?)null : r.GetDateTimeOffset(7)));
        }

        return list;
    }

    public async Task<long> UpsertAsync(BuildingPermitUpsert p, CancellationToken ct)
    {
        const string sql = @"
SET XACT_ABORT ON;

DECLARE @ids table (Id bigint NOT NULL);

BEGIN TRAN;

UPDATE opportunities.BuildingPermit WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
SET
    PermitNumber          = @permitNumber,
    PermitCategory        = @category,
    WorkType              = @workType,
    ProjectDescription    = @description,
    EstimatedValue        = @value,
    NumberOfDwellingUnits = @units,
    Address               = @address,
    City                  = @city,
    PostalCode            = @postal,
    GeoLocalArea          = @area,
    Latitude              = @lat,
    Longitude             = @lng,
    AppliedDate           = @applied,
    IssuedDate            = @issued,
    OwnerName             = @owner,
    ApplicantName         = @applicant,
    ContractorName        = @contractor,
    SpecificUseCategory   = @specificUse,
    PropertyUse           = @propertyUse,
    RawJson               = @raw,
    UpdatedAtUtc          = sysdatetimeoffset()
OUTPUT inserted.Id INTO @ids
WHERE PermitSourceId = @sourceId
  AND ExternalId = @externalId;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO opportunities.BuildingPermit
    (PermitSourceId, ExternalId, PermitNumber, PermitCategory, WorkType, ProjectDescription,
     EstimatedValue, NumberOfDwellingUnits, Address, City, PostalCode, GeoLocalArea,
     Latitude, Longitude, AppliedDate, IssuedDate, OwnerName, ApplicantName, ContractorName,
     SpecificUseCategory, PropertyUse, RawJson)
    OUTPUT inserted.Id INTO @ids
    VALUES
    (@sourceId, @externalId, @permitNumber, @category, @workType, @description,
     @value, @units, @address, @city, @postal, @area,
     @lat, @lng, @applied, @issued, @owner, @applicant, @contractor,
     @specificUse, @propertyUse, @raw);
END;

COMMIT TRAN;

SELECT TOP (1) Id FROM @ids;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };

        cmd.Parameters.Add("@sourceId", SqlDbType.BigInt).Value = p.PermitSourceId;
        cmd.Parameters.Add("@externalId", SqlDbType.NVarChar, 120).Value = p.ExternalId;
        cmd.Parameters.Add("@permitNumber", SqlDbType.NVarChar, 80).Value = (object?)p.PermitNumber ?? DBNull.Value;
        cmd.Parameters.Add("@category", SqlDbType.NVarChar, 120).Value = (object?)p.PermitCategory ?? DBNull.Value;
        cmd.Parameters.Add("@workType", SqlDbType.NVarChar, 120).Value = (object?)p.WorkType ?? DBNull.Value;
        cmd.Parameters.Add("@description", SqlDbType.NVarChar, -1).Value = (object?)p.ProjectDescription ?? DBNull.Value;
        AddDecimal(cmd, "@value", p.EstimatedValue, 19, 2);
        cmd.Parameters.Add("@units", SqlDbType.Int).Value = (object?)p.NumberOfDwellingUnits ?? DBNull.Value;
        cmd.Parameters.Add("@address", SqlDbType.NVarChar, 300).Value = (object?)p.Address ?? DBNull.Value;
        cmd.Parameters.Add("@city", SqlDbType.NVarChar, 100).Value = (object?)p.City ?? DBNull.Value;
        cmd.Parameters.Add("@postal", SqlDbType.NVarChar, 20).Value = (object?)p.PostalCode ?? DBNull.Value;
        cmd.Parameters.Add("@area", SqlDbType.NVarChar, 120).Value = (object?)p.GeoLocalArea ?? DBNull.Value;
        AddDecimal(cmd, "@lat", p.Latitude, 9, 6);
        AddDecimal(cmd, "@lng", p.Longitude, 9, 6);
        cmd.Parameters.Add("@applied", SqlDbType.Date).Value = (object?)p.AppliedDate ?? DBNull.Value;
        cmd.Parameters.Add("@issued", SqlDbType.Date).Value = (object?)p.IssuedDate ?? DBNull.Value;
        cmd.Parameters.Add("@owner", SqlDbType.NVarChar, 300).Value = (object?)p.OwnerName ?? DBNull.Value;
        cmd.Parameters.Add("@applicant", SqlDbType.NVarChar, 300).Value = (object?)p.ApplicantName ?? DBNull.Value;
        cmd.Parameters.Add("@contractor", SqlDbType.NVarChar, 300).Value = (object?)p.ContractorName ?? DBNull.Value;
        cmd.Parameters.Add("@specificUse", SqlDbType.NVarChar, 200).Value = (object?)p.SpecificUseCategory ?? DBNull.Value;
        cmd.Parameters.Add("@propertyUse", SqlDbType.NVarChar, 200).Value = (object?)p.PropertyUse ?? DBNull.Value;
        cmd.Parameters.Add("@raw", SqlDbType.NVarChar, -1).Value = (object?)p.RawJson ?? DBNull.Value;

        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(v);
    }

    public async Task<(string? OwnerName, string? ApplicantName, string? ContractorName)?> GetOrgNamesSnapshotAsync(
        long sourceId,
        string externalId,
        CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 1 OwnerName, ApplicantName, ContractorName
FROM   opportunities.BuildingPermit
WHERE  PermitSourceId = @sourceId
  AND  ExternalId = @externalId;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@sourceId", SqlDbType.BigInt).Value = sourceId;
        cmd.Parameters.Add("@externalId", SqlDbType.NVarChar, 120).Value = externalId;

        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return (
            r.IsDBNull(0) ? null : r.GetString(0),
            r.IsDBNull(1) ? null : r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2));
    }

    public Task SetOwnerCanonicalAsync(long permitId, long? canonicalOrgId, CancellationToken ct) =>
        SetCanonicalAsync(permitId, "OwnerCanonicalOrgId", canonicalOrgId, ct);

    public Task SetApplicantCanonicalAsync(long permitId, long? canonicalOrgId, CancellationToken ct) =>
        SetCanonicalAsync(permitId, "ApplicantCanonicalOrgId", canonicalOrgId, ct);

    public Task SetContractorCanonicalAsync(long permitId, long? canonicalOrgId, CancellationToken ct) =>
        SetCanonicalAsync(permitId, "ContractorCanonicalOrgId", canonicalOrgId, ct);

    public async Task UpdateSourceHeartbeatAsync(long sourceId, string? errorMessage, CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.PermitSource
SET    LastPolledAtUtc = sysdatetimeoffset(),
       LastErrorMessage = @err
WHERE  Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = sourceId;
        cmd.Parameters.Add("@err", SqlDbType.NVarChar, 1000).Value = (object?)errorMessage ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> CountAsync(CancellationToken ct)
    {
        const string sql = "SELECT COUNT(*) FROM opportunities.BuildingPermit;";
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return v is null || v is DBNull ? 0 : Convert.ToInt32(v);
    }

    public async Task<int> CountBySourceAsync(long sourceId, CancellationToken ct)
    {
        const string sql = "SELECT COUNT(*) FROM opportunities.BuildingPermit WHERE PermitSourceId = @id;";
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = sourceId;
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return v is null || v is DBNull ? 0 : Convert.ToInt32(v);
    }

    private async Task SetCanonicalAsync(long permitId, string columnName, long? canonicalOrgId, CancellationToken ct)
    {
        var sql = $@"
UPDATE opportunities.BuildingPermit
SET    {columnName} = @canon,
       UpdatedAtUtc = sysdatetimeoffset()
WHERE  Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = permitId;
        cmd.Parameters.Add("@canon", SqlDbType.BigInt).Value = canonicalOrgId.HasValue
            ? (object)canonicalOrgId.Value
            : DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void AddDecimal(SqlCommand cmd, string name, decimal? value, byte precision, byte scale)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
        p.Precision = precision;
        p.Scale = scale;
        p.Value = value.HasValue ? (object)value.Value : DBNull.Value;
    }
}
