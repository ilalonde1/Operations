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
            // Schema is created via manual script — transmittals_app does not have CREATE TABLE permission.
        }

        public List<ProposalStaffMember> LoadAll() =>
            LoadAllAsync().GetAwaiter().GetResult();

        public void SaveAll(List<ProposalStaffMember> staff) =>
            SaveAllAsync(staff ?? new List<ProposalStaffMember>()).GetAwaiter().GetResult();

        private async System.Threading.Tasks.Task EnsureSchemaAsync(System.Threading.CancellationToken ct = default)
        {
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
IF OBJECT_ID('dbo.ProposalStaff', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProposalStaff
    (
        Id NVARCHAR(32) NOT NULL CONSTRAINT PK_ProposalStaff PRIMARY KEY,
        FullName NVARCHAR(200) NOT NULL CONSTRAINT DF_ProposalStaff_FullName DEFAULT '',
        Credentials NVARCHAR(200) NOT NULL CONSTRAINT DF_ProposalStaff_Credentials DEFAULT '',
        Title NVARCHAR(200) NOT NULL CONSTRAINT DF_ProposalStaff_Title DEFAULT '',
        Email NVARCHAR(200) NOT NULL CONSTRAINT DF_ProposalStaff_Email DEFAULT '',
        Phone NVARCHAR(50) NOT NULL CONSTRAINT DF_ProposalStaff_Phone DEFAULT '',
        Bio NVARCHAR(MAX) NOT NULL CONSTRAINT DF_ProposalStaff_Bio DEFAULT '',
        SignatureImagePath NVARCHAR(500) NOT NULL CONSTRAINT DF_ProposalStaff_SignatureImagePath DEFAULT ''
    );
END";

                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SqlTimeouts.Batch };
                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);
        }

        private async System.Threading.Tasks.Task<List<ProposalStaffMember>> LoadAllAsync(System.Threading.CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
SELECT Id, FullName, Credentials, Title, Email, Phone, Bio, SignatureImagePath
FROM dbo.ProposalStaff
ORDER BY FullName;";

                var list = new List<ProposalStaffMember>();
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SqlTimeouts.UiFacing };
                await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt);
                while (await r.ReadAsync(innerCt))
                {
                    list.Add(new ProposalStaffMember
                    {
                        Id = r.GetStringOrEmpty(0),
                        FullName = r.GetStringOrEmpty(1),
                        Credentials = r.GetStringOrEmpty(2),
                        Title = r.GetStringOrEmpty(3),
                        Email = r.GetStringOrEmpty(4),
                        Phone = r.GetStringOrEmpty(5),
                        Bio = r.GetStringOrEmpty(6),
                        SignatureImagePath = r.GetStringOrEmpty(7),
                    });
                }

                return list;
            }, ct);
        }

        private async System.Threading.Tasks.Task SaveAllAsync(List<ProposalStaffMember> staff, System.Threading.CancellationToken ct = default)
        {
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string deleteSql = "DELETE FROM dbo.ProposalStaff;";
                const string insertSql = @"
INSERT INTO dbo.ProposalStaff
    (Id, FullName, Credentials, Title, Email, Phone, Bio, SignatureImagePath)
VALUES
    (@Id, @FullName, @Credentials, @Title, @Email, @Phone, @Bio, @SignatureImagePath);";

                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(innerCt);

                await using (var deleteCmd = new SqlCommand(deleteSql, cn, tx) { CommandTimeout = SqlTimeouts.Batch })
                {
                    await deleteCmd.ExecuteNonQueryAsync(innerCt);
                }

                foreach (var member in staff)
                {
                    var jsonRoundTrip = JsonSerializer.Serialize(member, JsonOptions);
                    var normalized = JsonSerializer.Deserialize<ProposalStaffMember>(jsonRoundTrip, JsonOptions) ?? member;

                    await using var insertCmd = new SqlCommand(insertSql, cn, tx) { CommandTimeout = SqlTimeouts.Batch };
                    insertCmd.Parameters.AddWithValue("@Id", normalized.Id ?? string.Empty);
                    insertCmd.Parameters.AddWithValue("@FullName", normalized.FullName ?? string.Empty);
                    insertCmd.Parameters.AddWithValue("@Credentials", normalized.Credentials ?? string.Empty);
                    insertCmd.Parameters.AddWithValue("@Title", normalized.Title ?? string.Empty);
                    insertCmd.Parameters.AddWithValue("@Email", normalized.Email ?? string.Empty);
                    insertCmd.Parameters.AddWithValue("@Phone", normalized.Phone ?? string.Empty);
                    insertCmd.Parameters.AddWithValue("@Bio", normalized.Bio ?? string.Empty);
                    insertCmd.Parameters.AddWithValue("@SignatureImagePath", normalized.SignatureImagePath ?? string.Empty);
                    await insertCmd.ExecuteNonQueryAsync(innerCt);
                }

                await tx.CommitAsync(innerCt);
            }, ct);
        }
    }
}
