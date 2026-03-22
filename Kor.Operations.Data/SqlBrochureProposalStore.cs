#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Core.Services;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.Data
{
    public sealed class SqlBrochureProposalStore : IBrochureProposalStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly string _cs;

        public SqlBrochureProposalStore(string connectionString)
        {
            _cs = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            Task.Run(() => EnsureSchemaAsync()).GetAwaiter().GetResult();
        }

        public void Save(BrochureProposal proposal) =>
            SaveAsync(proposal).GetAwaiter().GetResult();

        public List<BrochureProposal> LoadAll() =>
            LoadAllAsync().GetAwaiter().GetResult();

        public void Delete(string id) =>
            DeleteAsync(id).GetAwaiter().GetResult();

        private async System.Threading.Tasks.Task EnsureSchemaAsync(System.Threading.CancellationToken ct = default)
        {
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
IF OBJECT_ID('dbo.BrochureProposals', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BrochureProposals
    (
        Id NVARCHAR(32) NOT NULL CONSTRAINT PK_BrochureProposals PRIMARY KEY,
        Name NVARCHAR(200) NOT NULL CONSTRAINT DF_BrochureProposals_Name DEFAULT '',
        ContentJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_BrochureProposals_ContentJson DEFAULT '',
        ModifiedAt DATETIME2 NOT NULL CONSTRAINT DF_BrochureProposals_ModifiedAt DEFAULT SYSUTCDATETIME()
    );
END";

                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SqlTimeouts.Batch };
                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);
        }

        private async System.Threading.Tasks.Task SaveAsync(BrochureProposal proposal, System.Threading.CancellationToken ct = default)
        {
            if (proposal is null)
                throw new ArgumentNullException(nameof(proposal));

            proposal.ModifiedAt = DateTime.UtcNow;

            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
MERGE dbo.BrochureProposals AS t
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

        private async System.Threading.Tasks.Task<List<BrochureProposal>> LoadAllAsync(System.Threading.CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
SELECT ContentJson
FROM dbo.BrochureProposals
ORDER BY ModifiedAt DESC;";

                var list = new List<BrochureProposal>();
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SqlTimeouts.UiFacing };
                await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt);
                while (await r.ReadAsync(innerCt))
                {
                    var json = r.GetStringOrEmpty(0);
                    if (string.IsNullOrWhiteSpace(json))
                        continue;

                    var proposal = JsonSerializer.Deserialize<BrochureProposal>(json, JsonOptions);
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
                const string sql = "DELETE FROM dbo.BrochureProposals WHERE Id = @Id;";

                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SqlTimeouts.Batch };
                cmd.Parameters.AddWithValue("@Id", id ?? string.Empty);
                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);
        }
    }
}
