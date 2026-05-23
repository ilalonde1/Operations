#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Awards;

public sealed class SqlKorPursuitStore : IKorPursuitStore
{
    private const int CommandTimeoutSeconds = 30;
    private readonly string _connectionString;

    public SqlKorPursuitStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public async Task<long> AddAsync(KorPursuitCreate p, CancellationToken ct)
    {
        const string sql = @"
INSERT INTO opportunities.KorPursuits
    (Stage, OpportunityId, HistoricalOpportunityId, OpportunityAwardId, SourceExternalRef,
     BuyerCanonicalOrgId, BuyerName, Title, LostToCanonicalOrgId, LostToName,
     BidFee, BidCurrency, EstConstructionValue, OurRole, KorBusinessDeveloper, KorProposalManager,
     PursuitOpenedDate, DueDate, SubmittedDate, AwardDate,
     ClosedReason, Strengths, Weaknesses, Notes, CreatedByUser,
     ExternalSource, ExternalSourceKey)
OUTPUT INSERTED.Id
VALUES
    (@stage, @oppId, @histId, @awardId, @extRef,
     @buyerCanon, @buyerName, @title, @lostToCanon, @lostToName,
     @fee, @currency, @estCv, @role, @bd, @pm,
     @openedDate, @dueDate, @submitDate, @awardDate,
     @closedReason, @strengths, @weaknesses, @notes, @user,
     @externalSource, @externalSourceKey);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };

        cmd.Parameters.Add("@stage", SqlDbType.NVarChar, 40).Value = p.Stage;
        cmd.Parameters.Add("@oppId", SqlDbType.BigInt).Value = (object?)p.OpportunityId ?? DBNull.Value;
        cmd.Parameters.Add("@histId", SqlDbType.BigInt).Value = (object?)p.HistoricalOpportunityId ?? DBNull.Value;
        cmd.Parameters.Add("@awardId", SqlDbType.BigInt).Value = (object?)p.OpportunityAwardId ?? DBNull.Value;
        cmd.Parameters.Add("@extRef", SqlDbType.NVarChar, 200).Value = (object?)p.SourceExternalRef ?? DBNull.Value;
        cmd.Parameters.Add("@buyerCanon", SqlDbType.BigInt).Value = (object?)p.BuyerCanonicalOrgId ?? DBNull.Value;
        cmd.Parameters.Add("@buyerName", SqlDbType.NVarChar, 300).Value = p.BuyerName;
        cmd.Parameters.Add("@title", SqlDbType.NVarChar, 500).Value = p.Title;
        cmd.Parameters.Add("@lostToCanon", SqlDbType.BigInt).Value = (object?)p.LostToCanonicalOrgId ?? DBNull.Value;
        cmd.Parameters.Add("@lostToName", SqlDbType.NVarChar, 300).Value = (object?)p.LostToName ?? DBNull.Value;
        AddDecimal(cmd, "@fee", p.BidFee);
        cmd.Parameters.Add("@currency", SqlDbType.Char, 3).Value = p.BidCurrency;
        AddDecimal(cmd, "@estCv", p.EstConstructionValue);
        cmd.Parameters.Add("@role", SqlDbType.NVarChar, 30).Value = (object?)p.OurRole ?? DBNull.Value;
        cmd.Parameters.Add("@bd", SqlDbType.NVarChar, 100).Value = (object?)p.KorBusinessDeveloper ?? DBNull.Value;
        cmd.Parameters.Add("@pm", SqlDbType.NVarChar, 100).Value = (object?)p.KorProposalManager ?? DBNull.Value;
        cmd.Parameters.Add("@openedDate", SqlDbType.Date).Value = (object?)p.PursuitOpenedDate ?? DBNull.Value;
        cmd.Parameters.Add("@dueDate", SqlDbType.Date).Value = (object?)p.DueDate ?? DBNull.Value;
        cmd.Parameters.Add("@submitDate", SqlDbType.Date).Value = (object?)p.SubmittedDate ?? DBNull.Value;
        cmd.Parameters.Add("@awardDate", SqlDbType.Date).Value = (object?)p.AwardDate ?? DBNull.Value;
        cmd.Parameters.Add("@closedReason", SqlDbType.NVarChar, 120).Value = (object?)p.ClosedReason ?? DBNull.Value;
        cmd.Parameters.Add("@strengths", SqlDbType.NVarChar, -1).Value = (object?)p.Strengths ?? DBNull.Value;
        cmd.Parameters.Add("@weaknesses", SqlDbType.NVarChar, -1).Value = (object?)p.Weaknesses ?? DBNull.Value;
        cmd.Parameters.Add("@notes", SqlDbType.NVarChar, -1).Value = (object?)p.Notes ?? DBNull.Value;
        cmd.Parameters.Add("@user", SqlDbType.NVarChar, 100).Value = (object?)p.CreatedByUser ?? DBNull.Value;
        cmd.Parameters.Add("@externalSource", SqlDbType.NVarChar, 40).Value = (object?)p.ExternalSource ?? DBNull.Value;
        cmd.Parameters.Add("@externalSourceKey", SqlDbType.NVarChar, 80).Value = (object?)p.ExternalSourceKey ?? DBNull.Value;

        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(v);
    }

    public async Task<long> UpsertByExternalKeyAsync(KorPursuitCreate p, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(p.ExternalSource) || string.IsNullOrWhiteSpace(p.ExternalSourceKey))
            throw new ArgumentException("ExternalSource and ExternalSourceKey are required for upsert.");

        const string sql = @"
MERGE opportunities.KorPursuits AS t
USING (SELECT @extSrc AS ExternalSource, @extKey AS ExternalSourceKey) AS s
   ON t.ExternalSource = s.ExternalSource AND t.ExternalSourceKey = s.ExternalSourceKey
WHEN MATCHED THEN UPDATE SET
    Stage = @stage,
    BuyerCanonicalOrgId = COALESCE(@buyerCanon, t.BuyerCanonicalOrgId),
    BuyerName = COALESCE(NULLIF(@buyerName,''), t.BuyerName),
    Title = COALESCE(NULLIF(@title,''), t.Title),
    BidFee = COALESCE(@fee, t.BidFee),
    BidCurrency = @currency,
    EstConstructionValue = COALESCE(@estCv, t.EstConstructionValue),
    OurRole = COALESCE(@role, t.OurRole),
    KorProposalManager = COALESCE(@pm, t.KorProposalManager),
    PursuitOpenedDate = COALESCE(@openedDate, t.PursuitOpenedDate),
    DueDate = COALESCE(@dueDate, t.DueDate),
    SubmittedDate = COALESCE(@submitDate, t.SubmittedDate),
    AwardDate = COALESCE(@awardDate, t.AwardDate),
    Notes = COALESCE(@notes, t.Notes),
    UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN INSERT
    (Stage, OpportunityId, HistoricalOpportunityId, OpportunityAwardId, SourceExternalRef,
     BuyerCanonicalOrgId, BuyerName, Title, LostToCanonicalOrgId, LostToName,
     BidFee, BidCurrency, EstConstructionValue, OurRole, KorBusinessDeveloper, KorProposalManager,
     PursuitOpenedDate, DueDate, SubmittedDate, AwardDate,
     ClosedReason, Strengths, Weaknesses, Notes, CreatedByUser,
     ExternalSource, ExternalSourceKey)
VALUES
    (@stage, @oppId, @histId, @awardId, @extRef,
     @buyerCanon, @buyerName, @title, @lostToCanon, @lostToName,
     @fee, @currency, @estCv, @role, @bd, @pm,
     @openedDate, @dueDate, @submitDate, @awardDate,
     @closedReason, @strengths, @weaknesses, @notes, @user,
     @extSrc, @extKey)
OUTPUT INSERTED.Id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        BindCreateParams(cmd, p);
        cmd.Parameters.Add("@extSrc", SqlDbType.NVarChar, 40).Value = p.ExternalSource ?? "";
        cmd.Parameters.Add("@extKey", SqlDbType.NVarChar, 80).Value = p.ExternalSourceKey ?? "";

        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(v);
    }

    public async Task<int> CountByExternalSourceAsync(string externalSource, CancellationToken ct)
    {
        const string sql = "SELECT COUNT(*) FROM opportunities.KorPursuits WHERE ExternalSource = @src;";
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@src", SqlDbType.NVarChar, 40).Value = externalSource;
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(v ?? 0);
    }

    public async Task<KorPursuitRow?> GetAsync(long id, CancellationToken ct)
    {
        var rows = await ReadRowsAsync(
            "WHERE Id = @id",
            new[] { ("@id", (object)id, SqlDbType.BigInt) },
            1,
            ct).ConfigureAwait(false);
        return rows.Count > 0 ? rows[0] : null;
    }

    public async Task<IReadOnlyList<KorPursuitRow>> ListRecentAsync(int top, CancellationToken ct)
        => await ReadRowsAsync("ORDER BY CreatedAtUtc DESC", Array.Empty<(string, object, SqlDbType)>(), top, ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<KorPursuitRow>> ListByBuyerCanonicalAsync(long canonicalOrgId, CancellationToken ct)
        => await ReadRowsAsync(
            "WHERE BuyerCanonicalOrgId = @id ORDER BY CreatedAtUtc DESC",
            new[] { ("@id", (object)canonicalOrgId, SqlDbType.BigInt) },
            200,
            ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<KorPursuitRow>> ListByLostToCanonicalAsync(long canonicalOrgId, CancellationToken ct)
        => await ReadRowsAsync(
            "WHERE LostToCanonicalOrgId = @id ORDER BY CreatedAtUtc DESC",
            new[] { ("@id", (object)canonicalOrgId, SqlDbType.BigInt) },
            200,
            ct).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<string, int>> GetStageCountsAsync(CancellationToken ct)
    {
        const string sql = "SELECT Stage, COUNT(*) FROM opportunities.KorPursuits GROUP BY Stage;";
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            dict[r.GetString(0)] = r.GetInt32(1);
        }

        return dict;
    }

    private async Task<IReadOnlyList<KorPursuitRow>> ReadRowsAsync(
        string whereOrOrderBy,
        IReadOnlyList<(string Name, object Value, SqlDbType Type)> args,
        int top,
        CancellationToken ct)
    {
        var sql = $@"
SELECT TOP ({top})
    Id, Stage, OpportunityId, HistoricalOpportunityId, OpportunityAwardId, SourceExternalRef,
    BuyerCanonicalOrgId, BuyerName, Title, LostToCanonicalOrgId, LostToName,
    BidFee, BidCurrency, EstConstructionValue, OurRole, KorBusinessDeveloper, KorProposalManager,
    PursuitOpenedDate, DueDate, SubmittedDate, AwardDate,
    ClosedReason, Strengths, Weaknesses, Notes, CreatedByUser,
    CreatedAtUtc, UpdatedAtUtc, ExternalSource, ExternalSourceKey
FROM opportunities.KorPursuits
{whereOrOrderBy};";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        foreach (var (name, value, type) in args) cmd.Parameters.Add(name, type).Value = value;

        var list = new List<KorPursuitRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new KorPursuitRow(
                r.GetInt64(0),
                r.GetString(1),
                r.IsDBNull(2) ? (long?)null : r.GetInt64(2),
                r.IsDBNull(3) ? (long?)null : r.GetInt64(3),
                r.IsDBNull(4) ? (long?)null : r.GetInt64(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? (long?)null : r.GetInt64(6),
                r.GetString(7),
                r.GetString(8),
                r.IsDBNull(9) ? (long?)null : r.GetInt64(9),
                r.IsDBNull(10) ? null : r.GetString(10),
                r.IsDBNull(11) ? (decimal?)null : r.GetDecimal(11),
                r.GetString(12),
                r.IsDBNull(13) ? (decimal?)null : r.GetDecimal(13),
                r.IsDBNull(14) ? null : r.GetString(14),
                r.IsDBNull(15) ? null : r.GetString(15),
                r.IsDBNull(16) ? null : r.GetString(16),
                r.IsDBNull(17) ? (DateTime?)null : r.GetDateTime(17),
                r.IsDBNull(18) ? (DateTime?)null : r.GetDateTime(18),
                r.IsDBNull(19) ? (DateTime?)null : r.GetDateTime(19),
                r.IsDBNull(20) ? (DateTime?)null : r.GetDateTime(20),
                r.IsDBNull(21) ? null : r.GetString(21),
                r.IsDBNull(22) ? null : r.GetString(22),
                r.IsDBNull(23) ? null : r.GetString(23),
                r.IsDBNull(24) ? null : r.GetString(24),
                r.IsDBNull(25) ? null : r.GetString(25),
                r.GetDateTimeOffset(26),
                r.GetDateTimeOffset(27),
                r.IsDBNull(28) ? null : r.GetString(28),
                r.IsDBNull(29) ? null : r.GetString(29)));
        }

        return list;
    }

    private static void AddDecimal(SqlCommand cmd, string name, decimal? value)
    {
        var p = new SqlParameter(name, SqlDbType.Decimal) { Precision = 19, Scale = 2 };
        p.Value = (object?)value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    private static void BindCreateParams(SqlCommand cmd, KorPursuitCreate p)
    {
        cmd.Parameters.Add("@stage", SqlDbType.NVarChar, 40).Value = p.Stage;
        cmd.Parameters.Add("@oppId", SqlDbType.BigInt).Value = (object?)p.OpportunityId ?? DBNull.Value;
        cmd.Parameters.Add("@histId", SqlDbType.BigInt).Value = (object?)p.HistoricalOpportunityId ?? DBNull.Value;
        cmd.Parameters.Add("@awardId", SqlDbType.BigInt).Value = (object?)p.OpportunityAwardId ?? DBNull.Value;
        cmd.Parameters.Add("@extRef", SqlDbType.NVarChar, 200).Value = (object?)p.SourceExternalRef ?? DBNull.Value;
        cmd.Parameters.Add("@buyerCanon", SqlDbType.BigInt).Value = (object?)p.BuyerCanonicalOrgId ?? DBNull.Value;
        cmd.Parameters.Add("@buyerName", SqlDbType.NVarChar, 300).Value = p.BuyerName;
        cmd.Parameters.Add("@title", SqlDbType.NVarChar, 500).Value = p.Title;
        cmd.Parameters.Add("@lostToCanon", SqlDbType.BigInt).Value = (object?)p.LostToCanonicalOrgId ?? DBNull.Value;
        cmd.Parameters.Add("@lostToName", SqlDbType.NVarChar, 300).Value = (object?)p.LostToName ?? DBNull.Value;
        AddDecimal(cmd, "@fee", p.BidFee);
        cmd.Parameters.Add("@currency", SqlDbType.Char, 3).Value = p.BidCurrency;
        AddDecimal(cmd, "@estCv", p.EstConstructionValue);
        cmd.Parameters.Add("@role", SqlDbType.NVarChar, 30).Value = (object?)p.OurRole ?? DBNull.Value;
        cmd.Parameters.Add("@bd", SqlDbType.NVarChar, 100).Value = (object?)p.KorBusinessDeveloper ?? DBNull.Value;
        cmd.Parameters.Add("@pm", SqlDbType.NVarChar, 100).Value = (object?)p.KorProposalManager ?? DBNull.Value;
        cmd.Parameters.Add("@openedDate", SqlDbType.Date).Value = (object?)p.PursuitOpenedDate ?? DBNull.Value;
        cmd.Parameters.Add("@dueDate", SqlDbType.Date).Value = (object?)p.DueDate ?? DBNull.Value;
        cmd.Parameters.Add("@submitDate", SqlDbType.Date).Value = (object?)p.SubmittedDate ?? DBNull.Value;
        cmd.Parameters.Add("@awardDate", SqlDbType.Date).Value = (object?)p.AwardDate ?? DBNull.Value;
        cmd.Parameters.Add("@closedReason", SqlDbType.NVarChar, 120).Value = (object?)p.ClosedReason ?? DBNull.Value;
        cmd.Parameters.Add("@strengths", SqlDbType.NVarChar, -1).Value = (object?)p.Strengths ?? DBNull.Value;
        cmd.Parameters.Add("@weaknesses", SqlDbType.NVarChar, -1).Value = (object?)p.Weaknesses ?? DBNull.Value;
        cmd.Parameters.Add("@notes", SqlDbType.NVarChar, -1).Value = (object?)p.Notes ?? DBNull.Value;
        cmd.Parameters.Add("@user", SqlDbType.NVarChar, 100).Value = (object?)p.CreatedByUser ?? DBNull.Value;
    }
}
