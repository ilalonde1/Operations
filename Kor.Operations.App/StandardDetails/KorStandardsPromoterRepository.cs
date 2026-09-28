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
    internal async Task<(bool ok, string message)> SetRenderedImageAsync(string entityKind, string entityKey, byte[] png, int width, int height, string source, string changedBy, string basis)
    {
        try
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("detail.SetRenderedImage", cn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            AddGovernanceParameters(cmd, changedBy, basis);
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

    internal async Task<(bool ok, bool stored, string message)> SetRenderedPdfAsync(string entityKind, string entityKey, byte[] pdf, string changedBy, string basis)
    {
        try
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("detail.SetRenderedPdf", cn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            AddGovernanceParameters(cmd, changedBy, basis);
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

    internal async Task<(bool ok, string message)> SetDetailKindAsync(string detailNumber, string? kind, string changedBy, string basis)
    {
        try
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("detail.SetDetailKind", cn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            AddGovernanceParameters(cmd, changedBy, basis);
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

    internal async Task<(bool ok, string message)> SetDetailIsSheetAsync(string detailNumber, bool isSheet, string changedBy, string basis)
    {
        try
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("detail.SetDetailIsSheet", cn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            AddGovernanceParameters(cmd, changedBy, basis);
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

    internal async Task<(bool ok, string message)> SetDetailTypeAsync(string detailNumber, string detailType, string changedBy, string basis)
    {
        var (kind, isSheet, display) = DetailTypeFields(detailType);
        try
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            using var tx = cn.BeginTransaction();

            var kindAffected = await ExecuteSetDetailKindAsync(cn, tx, detailNumber, kind, changedBy, basis);
            if (kindAffected == 0)
            {
                tx.Rollback();
                return (false, $"Detail {detailNumber} was not found.");
            }

            var sheetAffected = await ExecuteSetDetailIsSheetAsync(cn, tx, detailNumber, isSheet, changedBy, basis);
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

    private static async Task<int> ExecuteSetDetailKindAsync(SqlConnection cn, SqlTransaction tx, string detailNumber, string kind, string changedBy, string basis)
    {
        await using var cmd = new SqlCommand("detail.SetDetailKind", cn, tx);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddGovernanceParameters(cmd, changedBy, basis);
        AddNVarChar(cmd, "@DetailNumber", 64, detailNumber);
        AddNVarChar(cmd, "@Kind", 16, kind);
        var returnValue = cmd.Parameters.Add("@ReturnValue", SqlDbType.Int);
        returnValue.Direction = ParameterDirection.ReturnValue;

        await cmd.ExecuteNonQueryAsync();
        return returnValue.Value is int value ? value : Convert.ToInt32(returnValue.Value ?? 0);
    }

    private static async Task<int> ExecuteSetDetailIsSheetAsync(SqlConnection cn, SqlTransaction tx, string detailNumber, bool isSheet, string changedBy, string basis)
    {
        await using var cmd = new SqlCommand("detail.SetDetailIsSheet", cn, tx);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddGovernanceParameters(cmd, changedBy, basis);
        AddNVarChar(cmd, "@DetailNumber", 64, detailNumber);
        cmd.Parameters.Add("@IsSheet", SqlDbType.Bit).Value = isSheet;
        var returnValue = cmd.Parameters.Add("@ReturnValue", SqlDbType.Int);
        returnValue.Direction = ParameterDirection.ReturnValue;

        await cmd.ExecuteNonQueryAsync();
        return returnValue.Value is int value ? value : Convert.ToInt32(returnValue.Value ?? 0);
    }

    // ------------------------------------------------------------------------------------ intake
    // Everything above this line edits a detail that already exists. Until migration 102 there was
    // nothing below it: the catalogue could be curated but never added to, and all 612 rows carried
    // CreatedBy = 'mint-018 (matcher collapse)' from the August crawl.

    /// <summary>
    /// Mints the next KOR-D number and binds it to a Revit drafting view, in one act. The detail and
    /// its occurrence are written together because every consumer joins them — a detail with no
    /// occurrence exists in the register and nowhere a drafter can reach.
    /// </summary>
    internal async Task<(bool ok, string detailNumber, string message)> AddDetailAsync(
        NewDetailRequest request, string changedBy, string basis)
    {
        try
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("detail.AddDetail", cn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            AddGovernanceParameters(cmd, changedBy, basis);
            AddNVarChar(cmd, "@Title", 400, request.Title);
            AddNVarChar(cmd, "@Discipline", 32, request.Discipline ?? string.Empty);
            AddNVarChar(cmd, "@Kind", 16, request.Kind ?? string.Empty);
            cmd.Parameters.Add("@IsSheet", SqlDbType.Bit).Value = request.IsSheet;
            AddNVarChar(cmd, "@DocumentName", 260, request.DocumentName);
            cmd.Parameters.Add("@ViewElementId", SqlDbType.BigInt).Value = request.ViewElementId;
            AddNVarChar(cmd, "@ViewName", 400, request.ViewName);
            AddNVarChar(cmd, "@ViewKind", 32, request.ViewKind);

            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                return (false, string.Empty, "The catalogue accepted the detail but did not return its number; nothing was confirmed.");
            }

            var number = r.GetStringOrEmpty(0);
            return (true, number, $"{number} added as unverified. Capture its drawing, then approve it.");
        }
        catch (SqlException ex)
        {
            return (false, string.Empty, ex.Message);
        }
    }

    /// <summary>Retires a detail. The reason is not optional — see KOR-D-00003.</summary>
    internal async Task<(bool ok, string message)> RetireDetailAsync(string detailNumber, string reason, string changedBy)
        => await CallAndReportAsync("detail.RetireDetail", $"{detailNumber} retired.", cmd =>
        {
            AddNVarChar(cmd, "@DetailNumber", 24, detailNumber);
            AddNVarChar(cmd, "@Reason", 400, reason);
            AddNVarChar(cmd, "@ChangedBy", 150, changedBy);
        });

    internal async Task<(bool ok, string message)> RestoreDetailAsync(string detailNumber, string basis, string changedBy)
        => await CallAndReportAsync("detail.RestoreDetail", $"{detailNumber} restored.", cmd =>
        {
            AddNVarChar(cmd, "@DetailNumber", 24, detailNumber);
            AddNVarChar(cmd, "@Basis", 1000, basis);
            AddNVarChar(cmd, "@ChangedBy", 150, changedBy);
        });

    /// <summary>
    /// Kind and IsSheet had setters; the title and the discipline did not, so a typo in either was
    /// an sa job. Pass null to leave a field alone; pass "" for the discipline to clear it.
    /// </summary>
    internal async Task<(bool ok, string message)> SetDetailTitleDisciplineAsync(
        string detailNumber, string? title, string? discipline, string changedBy, string basis)
        => await CallAndReportAsync("detail.SetDetailTitleDiscipline", $"{detailNumber} updated.", cmd =>
        {
            AddGovernanceParameters(cmd, changedBy, basis);
            AddNVarChar(cmd, "@DetailNumber", 24, detailNumber);
            AddNVarChar(cmd, "@Title", 400, title ?? string.Empty);
            // NOT AddNVarChar: it folds "" to NULL, and here the two mean different things —
            // NULL leaves the discipline as it is, "" clears it.
            AddNVarCharDistinguishingEmpty(cmd, "@Discipline", 32, discipline);
        });

    /// <summary>Upsert on (DocumentName, ViewElementId) — how reconcile heals a renamed view.</summary>
    internal async Task<(bool ok, string message)> RecordOccurrenceAsync(
        string documentName, long viewElementId, string viewName, string viewKind,
        string? detailNumber, string changedBy, string basis)
        => await CallAndReportAsync("detail.RecordOccurrence", "Occurrence recorded.", cmd =>
        {
            AddGovernanceParameters(cmd, changedBy, basis);
            AddNVarChar(cmd, "@DocumentName", 260, documentName);
            cmd.Parameters.Add("@ViewElementId", SqlDbType.BigInt).Value = viewElementId;
            AddNVarChar(cmd, "@ViewName", 400, viewName);
            AddNVarChar(cmd, "@ViewKind", 32, viewKind);
            AddNVarChar(cmd, "@DetailNumber", 24, detailNumber ?? string.Empty);
        });

    internal async Task<(bool ok, string message)> RemoveOccurrenceAsync(
        string documentName, long viewElementId, string basis, string changedBy)
        => await CallAndReportAsync("detail.RemoveOccurrence", "Occurrence removed.", cmd =>
        {
            AddNVarChar(cmd, "@DocumentName", 260, documentName);
            cmd.Parameters.Add("@ViewElementId", SqlDbType.BigInt).Value = viewElementId;
            AddNVarChar(cmd, "@Basis", 1000, basis);
            AddNVarChar(cmd, "@ChangedBy", 150, changedBy);
        });

    /// <summary>
    /// The shape every intake proc shares: it returns RowsAffected first, and a Message second when
    /// it has something more specific to say than "done". Zero rows is reported as a failure — the
    /// proc found nothing to change, and telling the gatekeeper it worked would be a lie.
    /// </summary>
    private async Task<(bool ok, string message)> CallAndReportAsync(
        string proc, string successMessage, Action<SqlCommand> bind)
    {
        try
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(proc, cn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            bind(cmd);

            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                return (true, successMessage);
            }

            var affected = r.IsDBNull(0) ? 0 : r.GetInt32(0);
            var reported = r.FieldCount > 1 && !r.IsDBNull(1) ? r.GetString(1) : null;
            return affected > 0
                ? (true, reported ?? successMessage)
                : (false, reported ?? "Nothing was changed.");
        }
        catch (SqlException ex)
        {
            return (false, ex.Message);
        }
    }

    internal static void AddGovernanceParameters(SqlCommand cmd, string changedBy, string basis)
    {
        AddNVarChar(cmd, "@Basis", 1000, basis);
        AddNVarChar(cmd, "@ChangedBy", 150, changedBy);
    }

    private static void AddNVarChar(SqlCommand cmd, string name, int size, string value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.NVarChar, size);
        p.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static void AddNVarCharDistinguishingEmpty(SqlCommand cmd, string name, int size, string? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.NVarChar, size);
        p.Value = value is null ? DBNull.Value : value;
    }
}

/// <summary>
/// One drawing being made a standard: what the gatekeeper picked in the model, plus what they typed
/// about it. The view id and document name are the binding the number is minted against.
/// </summary>
internal sealed record NewDetailRequest(
    string Title,
    string? Discipline,
    string? Kind,
    bool IsSheet,
    string DocumentName,
    long ViewElementId,
    string ViewName,
    string ViewKind);
