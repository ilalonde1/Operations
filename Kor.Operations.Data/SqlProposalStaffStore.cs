#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Models.Proposal;
using Kor.Operations.Core.Services;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.Data
{
    public sealed class SqlProposalStaffStore : IProposalStaffStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly string _cs;

        public SqlProposalStaffStore(string connectionString)
        {
            _cs = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public async Task<List<ProposalStaffMember>> LoadAllAsync(CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                var list = new List<ProposalStaffMember>();
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt).ConfigureAwait(false);
                var hasPhotoPathColumn = await HasPhotoPathColumnAsync(cn, innerCt).ConfigureAwait(false);
                var hasPhotoBytesColumn = await HasPhotoBytesColumnAsync(cn, innerCt).ConfigureAwait(false);
                await using var cmd = new SqlCommand(
                    BuildSelectSql(hasPhotoPathColumn, hasPhotoBytesColumn),
                    cn) { CommandTimeout = SqlTimeouts.UiFacing };
                await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt).ConfigureAwait(false);
                while (await r.ReadAsync(innerCt).ConfigureAwait(false))
                {
                    var ordinal = 0;
                    list.Add(new ProposalStaffMember
                    {
                        Id                 = r.GetStringOrEmpty(ordinal++),
                        FullName           = r.GetStringOrEmpty(ordinal++),
                        Credentials        = r.GetStringOrEmpty(ordinal++),
                        Title              = r.GetStringOrEmpty(ordinal++),
                        Email              = r.GetStringOrEmpty(ordinal++),
                        Phone              = r.GetStringOrEmpty(ordinal++),
                        Bio                = r.GetStringOrEmpty(ordinal++),
                        SignatureImagePath = r.GetStringOrEmpty(ordinal++),
                        PhotoPath          = hasPhotoPathColumn && !r.IsDBNull(ordinal) ? r.GetString(ordinal++) : string.Empty,
                        PhotoBytes         = hasPhotoBytesColumn && !r.IsDBNull(ordinal) ? (byte[])r.GetValue(ordinal) : Array.Empty<byte>(),
                    });
                }
                return list;
            }, ct).ConfigureAwait(false);
        }

        public async Task SaveAllAsync(List<ProposalStaffMember> staff, CancellationToken ct = default)
        {
            staff ??= new List<ProposalStaffMember>();

            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt).ConfigureAwait(false);
                var hasPhotoPathColumn = await HasPhotoPathColumnAsync(cn, innerCt).ConfigureAwait(false);
                var hasPhotoBytesColumn = await HasPhotoBytesColumnAsync(cn, innerCt).ConfigureAwait(false);
                await using var tx = await cn.BeginTransactionAsync(innerCt).ConfigureAwait(false);

                await using (var del = new SqlCommand("DELETE FROM dbo.ProposalStaff;", cn, (SqlTransaction)tx) { CommandTimeout = SqlTimeouts.Batch })
                    await del.ExecuteNonQueryAsync(innerCt).ConfigureAwait(false);

                var insertSql = BuildInsertSql(hasPhotoPathColumn, hasPhotoBytesColumn);

                foreach (var m in staff)
                {
                    await using var ins = new SqlCommand(insertSql, cn, (SqlTransaction)tx) { CommandTimeout = SqlTimeouts.Batch };
                    ins.Parameters.AddWithValue("@Id",                 m.Id                 ?? string.Empty);
                    ins.Parameters.AddWithValue("@FullName",           m.FullName           ?? string.Empty);
                    ins.Parameters.AddWithValue("@Credentials",        m.Credentials        ?? string.Empty);
                    ins.Parameters.AddWithValue("@Title",              m.Title              ?? string.Empty);
                    ins.Parameters.AddWithValue("@Email",              m.Email              ?? string.Empty);
                    ins.Parameters.AddWithValue("@Phone",              m.Phone              ?? string.Empty);
                    ins.Parameters.AddWithValue("@Bio",                m.Bio                ?? string.Empty);
                    ins.Parameters.AddWithValue("@SignatureImagePath", m.SignatureImagePath ?? string.Empty);
                    if (hasPhotoPathColumn)
                        ins.Parameters.AddWithValue("@PhotoPath", m.PhotoPath ?? string.Empty);
                    if (hasPhotoBytesColumn)
                        ins.Parameters.Add("@PhotoBytes", SqlDbType.VarBinary, -1).Value = (object?)m.PhotoBytes ?? DBNull.Value;
                    await ins.ExecuteNonQueryAsync(innerCt).ConfigureAwait(false);
                }

                await tx.CommitAsync(innerCt).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }

        private static async Task<bool> HasPhotoPathColumnAsync(SqlConnection cn, CancellationToken ct)
        {
            await using var cmd = new SqlCommand(
                "SELECT CASE WHEN COL_LENGTH('dbo.ProposalStaff', 'PhotoPath') IS NULL THEN 0 ELSE 1 END;",
                cn) { CommandTimeout = SqlTimeouts.UiFacing };
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false)) == 1;
        }

        private static async Task<bool> HasPhotoBytesColumnAsync(SqlConnection cn, CancellationToken ct)
        {
            await using var cmd = new SqlCommand(
                "SELECT CASE WHEN COL_LENGTH('dbo.ProposalStaff', 'PhotoBytes') IS NULL THEN 0 ELSE 1 END;",
                cn) { CommandTimeout = SqlTimeouts.UiFacing };
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false)) == 1;
        }

        private static string BuildSelectSql(bool hasPhotoPathColumn, bool hasPhotoBytesColumn)
        {
            var sql = "SELECT Id, FullName, Credentials, Title, Email, Phone, Bio, SignatureImagePath";
            if (hasPhotoPathColumn)
                sql += ", PhotoPath";
            if (hasPhotoBytesColumn)
                sql += ", PhotoBytes";
            return sql + " FROM dbo.ProposalStaff ORDER BY FullName;";
        }

        private static string BuildInsertSql(bool hasPhotoPathColumn, bool hasPhotoBytesColumn)
        {
            var columns = "Id, FullName, Credentials, Title, Email, Phone, Bio, SignatureImagePath";
            var values = "@Id, @FullName, @Credentials, @Title, @Email, @Phone, @Bio, @SignatureImagePath";
            if (hasPhotoPathColumn)
            {
                columns += ", PhotoPath";
                values += ", @PhotoPath";
            }
            if (hasPhotoBytesColumn)
            {
                columns += ", PhotoBytes";
                values += ", @PhotoBytes";
            }
            return $"INSERT INTO dbo.ProposalStaff ({columns}) VALUES ({values});";
        }
    }
}
