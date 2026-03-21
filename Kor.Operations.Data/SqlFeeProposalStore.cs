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
    public sealed class SqlFeeProposalStore : IFeeProposalStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly string _cs;

        public SqlFeeProposalStore(string connectionString)
        {
            _cs = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            EnsureSchemaAsync().GetAwaiter().GetResult();
        }

        public void Save(FeeProposal proposal) =>
            SaveAsync(proposal).GetAwaiter().GetResult();

        public List<FeeProposal> LoadAll() =>
            LoadAllAsync().GetAwaiter().GetResult();

        public void Delete(string id) =>
            DeleteAsync(id).GetAwaiter().GetResult();

        private async System.Threading.Tasks.Task EnsureSchemaAsync(System.Threading.CancellationToken ct = default)
        {
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
IF OBJECT_ID('dbo.FeeProposals', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FeeProposals
    (
        Id NVARCHAR(32) NOT NULL CONSTRAINT PK_FeeProposals PRIMARY KEY,
        Name NVARCHAR(200) NOT NULL CONSTRAINT DF_FeeProposals_Name DEFAULT '',
        ContentJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_FeeProposals_ContentJson DEFAULT '',
        ModifiedAt DATETIME2 NOT NULL CONSTRAINT DF_FeeProposals_ModifiedAt DEFAULT SYSUTCDATETIME()
    );
END";

                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SqlTimeouts.Batch };
                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);
        }

        private async System.Threading.Tasks.Task SaveAsync(FeeProposal proposal, System.Threading.CancellationToken ct = default)
        {
            if (proposal is null)
                throw new ArgumentNullException(nameof(proposal));

            proposal.ModifiedAt = DateTime.UtcNow;

            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
MERGE dbo.FeeProposals AS t
USING (VALUES(@Id, @Name, @ContentJson, @ModifiedAt))
       AS s(Id, Name, ContentJson, ModifiedAt)
ON t.Id = s.Id
WHEN MATCHED THEN
    UPDATE SET Name = s.Name,
               ContentJson = s.ContentJson,
               ModifiedAt = s.ModifiedAt
WHEN NOT MATCHED THEN
    INSERT (Id, Name, ContentJson, ModifiedAt)
    VALUES (s.Id, s.Name, s.ContentJson, s.ModifiedAt);";

                var json = JsonSerializer.Serialize(proposal, JsonOptions);

                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SqlTimeouts.Batch };
                cmd.Parameters.AddWithValue("@Id", proposal.Id ?? string.Empty);
                cmd.Parameters.AddWithValue("@Name", proposal.Name ?? string.Empty);
                cmd.Parameters.AddWithValue("@ContentJson", json);
                cmd.Parameters.AddWithValue("@ModifiedAt", proposal.ModifiedAt);
                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);
        }

        private async System.Threading.Tasks.Task<List<FeeProposal>> LoadAllAsync(System.Threading.CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
SELECT ContentJson
FROM dbo.FeeProposals
ORDER BY ModifiedAt DESC;";

                var list = new List<FeeProposal>();
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SqlTimeouts.UiFacing };
                await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt);
                while (await r.ReadAsync(innerCt))
                {
                    var json = r.GetStringOrEmpty(0);
                    if (string.IsNullOrWhiteSpace(json))
                        continue;

                    var proposal = JsonSerializer.Deserialize<FeeProposal>(json, JsonOptions);
                    if (proposal is not null)
                        list.Add(proposal);
                }

                return list;
            }, ct);
        }

        private async System.Threading.Tasks.Task DeleteAsync(string id, System.Threading.CancellationToken ct = default)
        {
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = "DELETE FROM dbo.FeeProposals WHERE Id = @Id;";

                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SqlTimeouts.Batch };
                cmd.Parameters.AddWithValue("@Id", id ?? string.Empty);
                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);
        }
    }
}
