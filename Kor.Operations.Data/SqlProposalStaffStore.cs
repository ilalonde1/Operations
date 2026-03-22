#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            // Tables created via manual DDL script — transmittals_app lacks CREATE TABLE permission.
        }

        public List<ProposalStaffMember> LoadAll()
        {
            var list = new List<ProposalStaffMember>();
            using var cn = new SqlConnection(_cs);
            cn.Open();
            var hasPhotoPathColumn = HasPhotoPathColumn(cn);
            using var cmd = new SqlCommand(
                hasPhotoPathColumn
                    ? "SELECT Id, FullName, Credentials, Title, Email, Phone, Bio, SignatureImagePath, PhotoPath FROM dbo.ProposalStaff ORDER BY FullName;"
                    : "SELECT Id, FullName, Credentials, Title, Email, Phone, Bio, SignatureImagePath FROM dbo.ProposalStaff ORDER BY FullName;",
                cn) { CommandTimeout = SqlTimeouts.UiFacing };
            using var r = cmd.ExecuteReader(CommandBehavior.SequentialAccess);
            while (r.Read())
            {
                list.Add(new ProposalStaffMember
                {
                    Id                = r.GetStringOrEmpty(0),
                    FullName          = r.GetStringOrEmpty(1),
                    Credentials       = r.GetStringOrEmpty(2),
                    Title             = r.GetStringOrEmpty(3),
                    Email             = r.GetStringOrEmpty(4),
                    Phone             = r.GetStringOrEmpty(5),
                    Bio               = r.GetStringOrEmpty(6),
                    SignatureImagePath = r.GetStringOrEmpty(7),
                    PhotoPath         = hasPhotoPathColumn && !r.IsDBNull(8) ? r.GetString(8) : string.Empty,
                });
            }
            return list;
        }

        public void SaveAll(List<ProposalStaffMember> staff)
        {
            staff ??= new List<ProposalStaffMember>();
            using var cn = new SqlConnection(_cs);
            cn.Open();
            var hasPhotoPathColumn = HasPhotoPathColumn(cn);
            using var tx = cn.BeginTransaction();

            using (var del = new SqlCommand("DELETE FROM dbo.ProposalStaff;", cn, tx) { CommandTimeout = SqlTimeouts.Batch })
                del.ExecuteNonQuery();

            var insertSql = hasPhotoPathColumn
                ? "INSERT INTO dbo.ProposalStaff (Id, FullName, Credentials, Title, Email, Phone, Bio, SignatureImagePath, PhotoPath) " +
                  "VALUES (@Id, @FullName, @Credentials, @Title, @Email, @Phone, @Bio, @SignatureImagePath, @PhotoPath);"
                : "INSERT INTO dbo.ProposalStaff (Id, FullName, Credentials, Title, Email, Phone, Bio, SignatureImagePath) " +
                  "VALUES (@Id, @FullName, @Credentials, @Title, @Email, @Phone, @Bio, @SignatureImagePath);";

            foreach (var m in staff)
            {
                using var ins = new SqlCommand(insertSql, cn, tx) { CommandTimeout = SqlTimeouts.Batch };
                ins.Parameters.AddWithValue("@Id",                m.Id                ?? string.Empty);
                ins.Parameters.AddWithValue("@FullName",          m.FullName          ?? string.Empty);
                ins.Parameters.AddWithValue("@Credentials",       m.Credentials       ?? string.Empty);
                ins.Parameters.AddWithValue("@Title",             m.Title             ?? string.Empty);
                ins.Parameters.AddWithValue("@Email",             m.Email             ?? string.Empty);
                ins.Parameters.AddWithValue("@Phone",             m.Phone             ?? string.Empty);
                ins.Parameters.AddWithValue("@Bio",               m.Bio               ?? string.Empty);
                ins.Parameters.AddWithValue("@SignatureImagePath", m.SignatureImagePath ?? string.Empty);
                if (hasPhotoPathColumn)
                    ins.Parameters.AddWithValue("@PhotoPath", m.PhotoPath ?? string.Empty);
                ins.ExecuteNonQuery();
            }

            tx.Commit();
        }

        private static bool HasPhotoPathColumn(SqlConnection cn)
        {
            using var cmd = new SqlCommand(
                "SELECT CASE WHEN COL_LENGTH('dbo.ProposalStaff', 'PhotoPath') IS NULL THEN 0 ELSE 1 END;",
                cn) { CommandTimeout = SqlTimeouts.UiFacing };
            return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
        }
    }
}
