#nullable enable
#pragma warning disable SA1649
using System;
using System.Data;
using System.Threading.Tasks;
using Kor.Operations.Data;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.StandardDetails;

internal sealed class KorStandardsPromoterRepository
{
    private readonly string _connectionString;

    internal KorStandardsPromoterRepository(string promoterConnectionString)
    {
        _connectionString = promoterConnectionString ?? throw new ArgumentNullException(nameof(promoterConnectionString));
    }

    internal async Task<(bool ok, string message)> PromoteAsync(string detailNumber, string toConfidence, string basis, string changedBy)
    {
        try
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("detail.PromoteDetail", cn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            AddNVarChar(cmd, "@DetailNumber", 24, detailNumber);
            AddNVarChar(cmd, "@ToConfidence", 32, toConfidence);
            AddNVarChar(cmd, "@Basis", 1000, basis);
            AddNVarChar(cmd, "@ChangedBy", 150, changedBy);

            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                return (true, "Promotion completed.");
            }

            var resultDetailNumber = r.GetStringOrEmpty(1);
            var fromConfidence = r.GetStringOrEmpty(2);
            var resultToConfidence = r.GetStringOrEmpty(3);
            var changed = !r.IsDBNull(4) && r.GetBoolean(4);
            return (true, changed
                ? $"{resultDetailNumber} promoted from {fromConfidence} to {resultToConfidence}."
                : $"{resultDetailNumber} already {resultToConfidence}; no change needed.");
        }
        catch (SqlException ex)
        {
            return (false, ex.Message);
        }
    }

    // Parts share the details' confidence ladder: detail.PromoteComponent, keyed on (FamilyName, TypeName).
    internal async Task<(bool ok, string message)> PromoteComponentAsync(string familyName, string typeName, string toConfidence, string basis, string changedBy)
    {
        try
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("detail.PromoteComponent", cn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            AddNVarChar(cmd, "@FamilyName", 200, familyName);
            AddNVarChar(cmd, "@TypeName", 200, typeName);
            AddNVarChar(cmd, "@ToConfidence", 32, toConfidence);
            AddNVarChar(cmd, "@Basis", 1000, basis);
            AddNVarChar(cmd, "@ChangedBy", 150, changedBy);

            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                return (true, "Promotion completed.");
            }

            var fromConfidence = r.GetStringOrEmpty(1);
            var resultToConfidence = r.GetStringOrEmpty(2);
            var changed = !r.IsDBNull(3) && r.GetBoolean(3);
            var label = string.IsNullOrWhiteSpace(typeName) ? familyName : $"{familyName} / {typeName}";
            return (true, changed
                ? $"{label} promoted from {fromConfidence} to {resultToConfidence}."
                : $"{label} already {resultToConfidence}; no change needed.");
        }
        catch (SqlException ex)
        {
            return (false, ex.Message);
        }
    }

    // Upsert one image into the governed art store (detail.SetRenderedImage). Used by the in-app
    // "Sync Part Images" tool; standards_promoter holds EXECUTE.
    internal async Task<(bool ok, string message)> SetRenderedImageAsync(string entityKind, string entityKey, byte[] png, int width, int height, string source)
    {
        try
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("detail.SetRenderedImage", cn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            AddNVarChar(cmd, "@EntityKind", 16, entityKind);
            AddNVarChar(cmd, "@EntityKey", 410, entityKey);
            cmd.Parameters.Add("@Png", SqlDbType.VarBinary, -1).Value = png;
            cmd.Parameters.Add("@Width", SqlDbType.Int).Value = width;
            cmd.Parameters.Add("@Height", SqlDbType.Int).Value = height;
            AddNVarChar(cmd, "@Source", 64, source);
            await cmd.ExecuteNonQueryAsync();
            return (true, "ok");
        }
        catch (SqlException ex)
        {
            return (false, ex.Message);
        }
    }

    internal async Task<(bool ok, bool stored, string message)> SetRenderedPdfAsync(string entityKind, string entityKey, byte[] pdf)
    {
        try
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("detail.SetRenderedPdf", cn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            AddNVarChar(cmd, "@EntityKind", 16, entityKind);
            AddNVarChar(cmd, "@EntityKey", 410, entityKey);
            cmd.Parameters.Add("@Pdf", SqlDbType.VarBinary, -1).Value = pdf;
            var returnValue = cmd.Parameters.Add("@ReturnValue", SqlDbType.Int);
            returnValue.Direction = ParameterDirection.ReturnValue;

            await cmd.ExecuteNonQueryAsync();
            var affected = returnValue.Value is int value ? value : Convert.ToInt32(returnValue.Value ?? 0);
            return affected == 0
                ? (true, false, $"{entityKey}: no rendered image row exists yet.")
                : (true, true, "ok");
        }
        catch (SqlException ex)
        {
            return (false, false, ex.Message);
        }
    }

    internal async Task<(bool ok, string message)> SetDetailKindAsync(string detailNumber, string? kind)
    {
        try
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("detail.SetDetailKind", cn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            AddNVarChar(cmd, "@DetailNumber", 64, detailNumber);
            AddNVarChar(cmd, "@Kind", 16, kind ?? string.Empty);
            var returnValue = cmd.Parameters.Add("@ReturnValue", SqlDbType.Int);
            returnValue.Direction = ParameterDirection.ReturnValue;

            await cmd.ExecuteNonQueryAsync();
            var affected = returnValue.Value is int value ? value : Convert.ToInt32(returnValue.Value ?? 0);
            return affected == 0
                ? (false, $"Detail {detailNumber} was not found.")
                : (true, string.IsNullOrWhiteSpace(kind) ? $"{detailNumber} kind cleared." : $"{detailNumber} kind set to {kind}.");
        }
        catch (SqlException ex)
        {
            return (false, ex.Message);
        }
    }

    internal async Task<(bool ok, string message)> SetDetailIsSheetAsync(string detailNumber, bool isSheet)
    {
        try
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("detail.SetDetailIsSheet", cn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            AddNVarChar(cmd, "@DetailNumber", 64, detailNumber);
            cmd.Parameters.Add("@IsSheet", SqlDbType.Bit).Value = isSheet;
            var returnValue = cmd.Parameters.Add("@ReturnValue", SqlDbType.Int);
            returnValue.Direction = ParameterDirection.ReturnValue;

            await cmd.ExecuteNonQueryAsync();
            var affected = returnValue.Value is int value ? value : Convert.ToInt32(returnValue.Value ?? 0);
            return affected == 0
                ? (false, $"Detail {detailNumber} was not found.")
                : (true, isSheet ? $"{detailNumber} moved to Sheets." : $"{detailNumber} moved to Details.");
        }
        catch (SqlException ex)
        {
            return (false, ex.Message);
        }
    }

    internal async Task<(bool ok, string message)> SetDetailTypeAsync(string detailNumber, string detailType)
    {
        var (kind, isSheet, display) = DetailTypeFields(detailType);
        try
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            using var tx = cn.BeginTransaction();

            var kindAffected = await ExecuteSetDetailKindAsync(cn, tx, detailNumber, kind);
            if (kindAffected == 0)
            {
                tx.Rollback();
                return (false, $"Detail {detailNumber} was not found.");
            }

            var sheetAffected = await ExecuteSetDetailIsSheetAsync(cn, tx, detailNumber, isSheet);
            if (sheetAffected == 0)
            {
                tx.Rollback();
                return (false, $"Detail {detailNumber} was not found.");
            }

            tx.Commit();
            return (true, $"{detailNumber} type set to {display}.");
        }
        catch (SqlException ex)
        {
            return (false, ex.Message);
        }
    }

    private static (string Kind, bool IsSheet, string Display) DetailTypeFields(string detailType)
        => detailType switch
        {
            "custom" => ("custom", false, "Custom detail"),
            "note-schedule" => ("general-note", true, "Note / schedule"),
            _ => ("typical", false, "Typical detail")
        };

    private static async Task<int> ExecuteSetDetailKindAsync(SqlConnection cn, SqlTransaction tx, string detailNumber, string kind)
    {
        await using var cmd = new SqlCommand("detail.SetDetailKind", cn, tx);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddNVarChar(cmd, "@DetailNumber", 64, detailNumber);
        AddNVarChar(cmd, "@Kind", 16, kind);
        var returnValue = cmd.Parameters.Add("@ReturnValue", SqlDbType.Int);
        returnValue.Direction = ParameterDirection.ReturnValue;

        await cmd.ExecuteNonQueryAsync();
        return returnValue.Value is int value ? value : Convert.ToInt32(returnValue.Value ?? 0);
    }

    private static async Task<int> ExecuteSetDetailIsSheetAsync(SqlConnection cn, SqlTransaction tx, string detailNumber, bool isSheet)
    {
        await using var cmd = new SqlCommand("detail.SetDetailIsSheet", cn, tx);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddNVarChar(cmd, "@DetailNumber", 64, detailNumber);
        cmd.Parameters.Add("@IsSheet", SqlDbType.Bit).Value = isSheet;
        var returnValue = cmd.Parameters.Add("@ReturnValue", SqlDbType.Int);
        returnValue.Direction = ParameterDirection.ReturnValue;

        await cmd.ExecuteNonQueryAsync();
        return returnValue.Value is int value ? value : Convert.ToInt32(returnValue.Value ?? 0);
    }

    private static void AddNVarChar(SqlCommand cmd, string name, int size, string value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.NVarChar, size);
        p.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }
}
