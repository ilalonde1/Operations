#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Opportunities;

/// <summary>
/// SQL Server implementation of <see cref="IOpportunityStore"/>. Pure ADO,
/// no EF — matches repo convention. Connection string is injected as a
/// plain string so this project stays free of host-specific Options classes.
/// </summary>
public sealed class SqlOpportunityStore : IOpportunityStore
{
    private const int CommandTimeoutSeconds = 30;

    /// <summary>
    /// Canonical SELECT column list. Used by both read queries and OUTPUT
    /// clauses. Order matches <see cref="MapReader"/>; do not reorder one
    /// without the other.
    /// </summary>
    private const string AllColumns = @"
Id, OpportunityKey, Name,
BuyerName, BuyerType, DeltekClientId, DeltekContactId, BuyerContactName, BuyerContactEmail, BuyerContactPhone,
ProjectAddress, ProjectCity, ProjectProvince, ProjectPostalCode, ProjectLatitude, ProjectLongitude,
Discipline, ConstructionType, ProjectCategory,
EstimatedValue, EstimatedValueCurrency, RfpReleaseDate, SubmissionDeadlineUtc,
Status, IdentifiedAtUtc, ReviewingSinceUtc, QualifiedAtUtc, PursuingSinceUtc, ProposalSubmittedAtUtc, OutcomeAtUtc,
OwnerStaffId, ReviewerStaffIds,
RelevanceScore, RelevanceTier,
WonLostOutcome, OutcomeReason, WonProjectWbs1, AwardedValue,
CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy,
RowVersion";

    private readonly string _connectionString;

    public SqlOpportunityStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<Opportunity>> ListAsync(CancellationToken ct)
    {
        var sql = $@"
SELECT {AllColumns}
FROM opportunities.Opportunities
ORDER BY UpdatedAtUtc DESC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<Opportunity>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(MapReader(reader));
        }

        return rows;
    }

    public async Task<Opportunity?> GetByIdAsync(long id, CancellationToken ct)
    {
        var sql = $@"
SELECT {AllColumns}
FROM opportunities.Opportunities
WHERE Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapReader(reader) : null;
    }

    public async Task<Opportunity?> GetByKeyAsync(string opportunityKey, CancellationToken ct)
    {
        var sql = $@"
SELECT {AllColumns}
FROM opportunities.Opportunities
WHERE OpportunityKey = @key;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@key", SqlDbType.NVarChar, 64).Value = opportunityKey;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapReader(reader) : null;
    }

    public async Task<Opportunity> InsertAsync(Opportunity opportunity, string actorDisplay, CancellationToken ct)
    {
        // CreatedAtUtc / UpdatedAtUtc fall back to SQL defaults so the timestamps are
        // server-side and atomic with the insert. We set CreatedBy / UpdatedBy explicitly
        // so we capture the WPF user, not the SQL login (suser_sname() = 'opportunities_app').
        var sql = $@"
INSERT INTO opportunities.Opportunities
    (OpportunityKey, Name,
     BuyerName, BuyerType, DeltekClientId, DeltekContactId, BuyerContactName, BuyerContactEmail, BuyerContactPhone,
     ProjectAddress, ProjectCity, ProjectProvince, ProjectPostalCode, ProjectLatitude, ProjectLongitude,
     Discipline, ConstructionType, ProjectCategory,
     EstimatedValue, EstimatedValueCurrency, RfpReleaseDate, SubmissionDeadlineUtc,
     Status, IdentifiedAtUtc, ReviewingSinceUtc, QualifiedAtUtc, PursuingSinceUtc, ProposalSubmittedAtUtc, OutcomeAtUtc,
     OwnerStaffId, ReviewerStaffIds,
     RelevanceScore, RelevanceTier,
     WonLostOutcome, OutcomeReason, WonProjectWbs1, AwardedValue,
     CreatedBy, UpdatedBy)
OUTPUT {OutputInsertedColumns()}
VALUES
    (@key, @name,
     @buyer, @buyerType, @deltekId, @deltekContactId, @buyerContactName, @buyerContactEmail, @buyerContactPhone,
     @addr, @city, @prov, @postal, @lat, @lng,
     @disc, @ctype, @pcat,
     @value, @ccy, @rfpDate, @deadline,
     @status, @identifiedAt, @reviewingAt, @qualifiedAt, @pursuingAt, @submittedAt, @outcomeAt,
     @ownerId, @reviewerIds,
     @score, @tier,
     @outcome, @outcomeReason, @wbs1, @awarded,
     @actor, @actor);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        BindOpportunityParams(cmd, opportunity);
        cmd.Parameters.Add("@actor", SqlDbType.NVarChar, 150).Value = actorDisplay;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("INSERT did not return a row. This should be impossible.");
        }

        return MapReader(reader);
    }

    public async Task<Opportunity> UpdateAsync(Opportunity opportunity, string actorDisplay, CancellationToken ct)
    {
        var sql = $@"
UPDATE opportunities.Opportunities
SET OpportunityKey = @key,
    Name = @name,
    BuyerName = @buyer, BuyerType = @buyerType, DeltekClientId = @deltekId,
    DeltekContactId = @deltekContactId, BuyerContactName = @buyerContactName, BuyerContactEmail = @buyerContactEmail, BuyerContactPhone = @buyerContactPhone,
    ProjectAddress = @addr, ProjectCity = @city, ProjectProvince = @prov, ProjectPostalCode = @postal,
    ProjectLatitude = @lat, ProjectLongitude = @lng,
    Discipline = @disc, ConstructionType = @ctype, ProjectCategory = @pcat,
    EstimatedValue = @value, EstimatedValueCurrency = @ccy, RfpReleaseDate = @rfpDate, SubmissionDeadlineUtc = @deadline,
    Status = @status,
    IdentifiedAtUtc = @identifiedAt,
    ReviewingSinceUtc = @reviewingAt, QualifiedAtUtc = @qualifiedAt, PursuingSinceUtc = @pursuingAt,
    ProposalSubmittedAtUtc = @submittedAt, OutcomeAtUtc = @outcomeAt,
    OwnerStaffId = @ownerId, ReviewerStaffIds = @reviewerIds,
    RelevanceScore = @score, RelevanceTier = @tier,
    WonLostOutcome = @outcome, OutcomeReason = @outcomeReason, WonProjectWbs1 = @wbs1, AwardedValue = @awarded,
    UpdatedAtUtc = sysdatetimeoffset(), UpdatedBy = @actor
OUTPUT {OutputInsertedColumns()}
WHERE Id = @id AND RowVersion = @rv;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        BindOpportunityParams(cmd, opportunity);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = opportunity.Id;
        cmd.Parameters.Add("@rv", SqlDbType.Binary, 8).Value = opportunity.RowVersion;
        cmd.Parameters.Add("@actor", SqlDbType.NVarChar, 150).Value = actorDisplay;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new OpportunityConcurrencyException(opportunity.Id);
        }

        return MapReader(reader);
    }

    public async Task<Opportunity> ChangeStatusAsync(
        long id,
        OpportunityStatus newStatus,
        byte[] expectedRowVersion,
        string actorDisplay,
        CancellationToken ct)
    {
        // Map the target status to the milestone column we should stamp. ISNULL keeps
        // the first transition's timestamp if the user yo-yos a status (e.g. moves
        // back to Reviewing after a brief Pursuing) - the original ReviewingSinceUtc
        // is preserved.
        var milestoneColumn = newStatus switch
        {
            OpportunityStatus.Identified => "IdentifiedAtUtc",
            OpportunityStatus.Reviewing => "ReviewingSinceUtc",
            OpportunityStatus.Qualified => "QualifiedAtUtc",
            OpportunityStatus.Pursuing => "PursuingSinceUtc",
            OpportunityStatus.ProposalSubmitted => "ProposalSubmittedAtUtc",
            OpportunityStatus.Won
                or OpportunityStatus.Lost
                or OpportunityStatus.NoBid
                or OpportunityStatus.Withdrawn => "OutcomeAtUtc",
            _ => throw new ArgumentOutOfRangeException(nameof(newStatus), newStatus, "Unknown status."),
        };

        // milestoneColumn is sourced from a closed enum switch, never user input -
        // safe to interpolate into SQL.
        var sql = $@"
UPDATE opportunities.Opportunities
SET Status = @status,
    {milestoneColumn} = ISNULL({milestoneColumn}, sysdatetimeoffset()),
    UpdatedAtUtc = sysdatetimeoffset(),
    UpdatedBy = @actor
OUTPUT {OutputInsertedColumns()}
WHERE Id = @id AND RowVersion = @rv;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@status", SqlDbType.Int).Value = (int)newStatus;
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        cmd.Parameters.Add("@rv", SqlDbType.Binary, 8).Value = expectedRowVersion;
        cmd.Parameters.Add("@actor", SqlDbType.NVarChar, 150).Value = actorDisplay;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new OpportunityConcurrencyException(id);
        }

        return MapReader(reader);
    }

    /// <summary>
    /// Builds the OUTPUT clause's column list. Matches <see cref="AllColumns"/>
    /// exactly, but each column is prefixed with <c>INSERTED.</c> per T-SQL
    /// OUTPUT semantics.
    /// </summary>
    private static string OutputInsertedColumns()
    {
        var columnNames = AllColumns
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(", ", Array.ConvertAll(columnNames, c => $"INSERTED.{c}"));
    }

    private static void BindOpportunityParams(SqlCommand cmd, Opportunity o)
    {
        cmd.Parameters.Add("@key", SqlDbType.NVarChar, 64).Value = o.OpportunityKey;
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 400).Value = o.Name;
        cmd.Parameters.Add("@buyer", SqlDbType.NVarChar, 300).Value = o.BuyerName;
        cmd.Parameters.Add("@buyerType", SqlDbType.Int).Value = (int)o.BuyerType;
        cmd.Parameters.Add("@deltekId", SqlDbType.NVarChar, 50).Value = (object?)o.DeltekClientId ?? DBNull.Value;
        cmd.Parameters.Add("@deltekContactId", SqlDbType.NVarChar, 32).Value = (object?)o.DeltekContactId ?? DBNull.Value;
        cmd.Parameters.Add("@buyerContactName", SqlDbType.NVarChar, 120).Value = (object?)o.BuyerContactName ?? DBNull.Value;
        cmd.Parameters.Add("@buyerContactEmail", SqlDbType.NVarChar, 255).Value = (object?)o.BuyerContactEmail ?? DBNull.Value;
        cmd.Parameters.Add("@buyerContactPhone", SqlDbType.NVarChar, 40).Value = (object?)o.BuyerContactPhone ?? DBNull.Value;

        cmd.Parameters.Add("@addr", SqlDbType.NVarChar, 500).Value = (object?)o.ProjectAddress ?? DBNull.Value;
        cmd.Parameters.Add("@city", SqlDbType.NVarChar, 150).Value = (object?)o.ProjectCity ?? DBNull.Value;
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 20).Value = (object?)o.ProjectProvince ?? DBNull.Value;
        cmd.Parameters.Add("@postal", SqlDbType.NVarChar, 20).Value = (object?)o.ProjectPostalCode ?? DBNull.Value;
        AddDecimal(cmd, "@lat", precision: 9, scale: 6, value: o.ProjectLatitude);
        AddDecimal(cmd, "@lng", precision: 9, scale: 6, value: o.ProjectLongitude);

        cmd.Parameters.Add("@disc", SqlDbType.Int).Value = (int)o.Discipline;
        cmd.Parameters.Add("@ctype", SqlDbType.NVarChar, 100).Value = (object?)o.ConstructionType ?? DBNull.Value;
        cmd.Parameters.Add("@pcat", SqlDbType.NVarChar, 100).Value = (object?)o.ProjectCategory ?? DBNull.Value;

        AddDecimal(cmd, "@value", precision: 18, scale: 2, value: o.EstimatedValue);
        cmd.Parameters.Add("@ccy", SqlDbType.NVarChar, 3).Value = string.IsNullOrEmpty(o.EstimatedValueCurrency) ? "CAD" : o.EstimatedValueCurrency;
        cmd.Parameters.Add("@rfpDate", SqlDbType.Date).Value = o.RfpReleaseDate.HasValue
            ? (object)o.RfpReleaseDate.Value.ToDateTime(TimeOnly.MinValue)
            : DBNull.Value;
        cmd.Parameters.Add("@deadline", SqlDbType.DateTimeOffset).Value = (object?)o.SubmissionDeadlineUtc ?? DBNull.Value;

        cmd.Parameters.Add("@status", SqlDbType.Int).Value = (int)o.Status;
        cmd.Parameters.Add("@identifiedAt", SqlDbType.DateTimeOffset).Value = o.IdentifiedAtUtc;
        cmd.Parameters.Add("@reviewingAt", SqlDbType.DateTimeOffset).Value = (object?)o.ReviewingSinceUtc ?? DBNull.Value;
        cmd.Parameters.Add("@qualifiedAt", SqlDbType.DateTimeOffset).Value = (object?)o.QualifiedAtUtc ?? DBNull.Value;
        cmd.Parameters.Add("@pursuingAt", SqlDbType.DateTimeOffset).Value = (object?)o.PursuingSinceUtc ?? DBNull.Value;
        cmd.Parameters.Add("@submittedAt", SqlDbType.DateTimeOffset).Value = (object?)o.ProposalSubmittedAtUtc ?? DBNull.Value;
        cmd.Parameters.Add("@outcomeAt", SqlDbType.DateTimeOffset).Value = (object?)o.OutcomeAtUtc ?? DBNull.Value;

        cmd.Parameters.Add("@ownerId", SqlDbType.NVarChar, 20).Value = (object?)o.OwnerStaffId ?? DBNull.Value;
        cmd.Parameters.Add("@reviewerIds", SqlDbType.NVarChar, 500).Value = (object?)o.ReviewerStaffIds ?? DBNull.Value;

        AddDecimal(cmd, "@score", precision: 10, scale: 4, value: o.RelevanceScore);
        cmd.Parameters.Add("@tier", SqlDbType.Int).Value = o.RelevanceTier.HasValue ? (object)(int)o.RelevanceTier.Value : DBNull.Value;

        cmd.Parameters.Add("@outcome", SqlDbType.Int).Value = o.WonLostOutcome.HasValue ? (object)(int)o.WonLostOutcome.Value : DBNull.Value;
        cmd.Parameters.Add("@outcomeReason", SqlDbType.NVarChar, 2000).Value = (object?)o.OutcomeReason ?? DBNull.Value;
        cmd.Parameters.Add("@wbs1", SqlDbType.NVarChar, 50).Value = (object?)o.WonProjectWbs1 ?? DBNull.Value;
        AddDecimal(cmd, "@awarded", precision: 18, scale: 2, value: o.AwardedValue);
    }

    /// <summary>
    /// SqlClient defaults <c>SqlDbType.Decimal</c> parameters to precision 18 / scale 0,
    /// which silently truncates fractional digits. Always set Precision/Scale to match
    /// the column's declared shape.
    /// </summary>
    private static void AddDecimal(SqlCommand cmd, string name, byte precision, byte scale, decimal? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
        p.Precision = precision;
        p.Scale = scale;
        p.Value = (object?)value ?? DBNull.Value;
    }

    /// <summary>
    /// Maps a single row from <see cref="AllColumns"/> order to <see cref="Opportunity"/>.
    /// Column ordinals are hard-coded against <see cref="AllColumns"/>; if you reorder
    /// that constant you must reorder this method to match.
    /// </summary>
    private static Opportunity MapReader(SqlDataReader r) => new()
    {
        Id = r.GetInt64(0),
        OpportunityKey = r.GetString(1),
        Name = r.GetString(2),

        BuyerName = r.GetString(3),
        BuyerType = (BuyerType)r.GetInt32(4),
        DeltekClientId = r.IsDBNull(5) ? null : r.GetString(5),
        DeltekContactId = r.IsDBNull(6) ? null : r.GetString(6),
        BuyerContactName = r.IsDBNull(7) ? null : r.GetString(7),
        BuyerContactEmail = r.IsDBNull(8) ? null : r.GetString(8),
        BuyerContactPhone = r.IsDBNull(9) ? null : r.GetString(9),

        ProjectAddress = r.IsDBNull(10) ? null : r.GetString(10),
        ProjectCity = r.IsDBNull(11) ? null : r.GetString(11),
        ProjectProvince = r.IsDBNull(12) ? null : r.GetString(12),
        ProjectPostalCode = r.IsDBNull(13) ? null : r.GetString(13),
        ProjectLatitude = r.IsDBNull(14) ? null : r.GetDecimal(14),
        ProjectLongitude = r.IsDBNull(15) ? null : r.GetDecimal(15),

        Discipline = (OpportunityDiscipline)r.GetInt32(16),
        ConstructionType = r.IsDBNull(17) ? null : r.GetString(17),
        ProjectCategory = r.IsDBNull(18) ? null : r.GetString(18),

        EstimatedValue = r.IsDBNull(19) ? null : r.GetDecimal(19),
        EstimatedValueCurrency = r.GetString(20),
        RfpReleaseDate = r.IsDBNull(21) ? null : DateOnly.FromDateTime(r.GetDateTime(21)),
        SubmissionDeadlineUtc = r.IsDBNull(22) ? null : r.GetDateTimeOffset(22),

        Status = (OpportunityStatus)r.GetInt32(23),
        IdentifiedAtUtc = r.GetDateTimeOffset(24),
        ReviewingSinceUtc = r.IsDBNull(25) ? null : r.GetDateTimeOffset(25),
        QualifiedAtUtc = r.IsDBNull(26) ? null : r.GetDateTimeOffset(26),
        PursuingSinceUtc = r.IsDBNull(27) ? null : r.GetDateTimeOffset(27),
        ProposalSubmittedAtUtc = r.IsDBNull(28) ? null : r.GetDateTimeOffset(28),
        OutcomeAtUtc = r.IsDBNull(29) ? null : r.GetDateTimeOffset(29),

        OwnerStaffId = r.IsDBNull(30) ? null : r.GetString(30),
        ReviewerStaffIds = r.IsDBNull(31) ? null : r.GetString(31),

        RelevanceScore = r.IsDBNull(32) ? null : r.GetDecimal(32),
        RelevanceTier = r.IsDBNull(33) ? null : (RelevanceTier?)r.GetInt32(33),

        WonLostOutcome = r.IsDBNull(34) ? null : (WonLostOutcome?)r.GetInt32(34),
        OutcomeReason = r.IsDBNull(35) ? null : r.GetString(35),
        WonProjectWbs1 = r.IsDBNull(36) ? null : r.GetString(36),
        AwardedValue = r.IsDBNull(37) ? null : r.GetDecimal(37),

        CreatedAtUtc = r.GetDateTimeOffset(38),
        CreatedBy = r.GetString(39),
        UpdatedAtUtc = r.GetDateTimeOffset(40),
        UpdatedBy = r.GetString(41),

        RowVersion = (byte[])r.GetValue(42),
    };
}
