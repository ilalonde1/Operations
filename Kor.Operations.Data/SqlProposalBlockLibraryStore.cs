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
    public sealed class SqlProposalBlockLibraryStore : IProposalBlockLibraryStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly string _cs;

        public SqlProposalBlockLibraryStore(string connectionString)
        {
            _cs = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            // Schema is created via manual script — transmittals_app does not have CREATE TABLE permission.
        }

        public void Save(ProposalBlockTemplate template) =>
            SaveAsync(template).GetAwaiter().GetResult();

        public List<ProposalBlockTemplate> LoadAll() =>
            LoadAllAsync().GetAwaiter().GetResult();

        public void Delete(string id) =>
            DeleteAsync(id).GetAwaiter().GetResult();

        private async System.Threading.Tasks.Task EnsureSchemaAsync(System.Threading.CancellationToken ct = default)
        {
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
IF OBJECT_ID('dbo.ProposalBlockTemplates', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProposalBlockTemplates
    (
        Id NVARCHAR(32) NOT NULL CONSTRAINT PK_ProposalBlockTemplates PRIMARY KEY,
        Name NVARCHAR(200) NOT NULL CONSTRAINT DF_ProposalBlockTemplates_Name DEFAULT '',
        Category NVARCHAR(100) NOT NULL CONSTRAINT DF_ProposalBlockTemplates_Category DEFAULT '',
        BlockType NVARCHAR(50) NOT NULL CONSTRAINT DF_ProposalBlockTemplates_BlockType DEFAULT '',
        ContentJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_ProposalBlockTemplates_ContentJson DEFAULT '',
        ModifiedAt DATETIME2 NOT NULL CONSTRAINT DF_ProposalBlockTemplates_ModifiedAt DEFAULT SYSUTCDATETIME()
    );
END";

                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SqlTimeouts.Batch };
                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);
        }

        private async System.Threading.Tasks.Task SaveAsync(ProposalBlockTemplate template, System.Threading.CancellationToken ct = default)
        {
            if (template is null)
                throw new ArgumentNullException(nameof(template));

            template.ModifiedAt = DateTime.UtcNow;

            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
MERGE dbo.ProposalBlockTemplates AS t
USING (VALUES(@Id, @Name, @Category, @BlockType, @ContentJson, @ModifiedAt))
       AS s(Id, Name, Category, BlockType, ContentJson, ModifiedAt)
ON t.Id = s.Id
WHEN MATCHED THEN
    UPDATE SET Name = s.Name,
               Category = s.Category,
               BlockType = s.BlockType,
               ContentJson = s.ContentJson,
               ModifiedAt = s.ModifiedAt
WHEN NOT MATCHED THEN
    INSERT (Id, Name, Category, BlockType, ContentJson, ModifiedAt)
    VALUES (s.Id, s.Name, s.Category, s.BlockType, s.ContentJson, s.ModifiedAt);";

                var json = JsonSerializer.Serialize(template, JsonOptions);

                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SqlTimeouts.Batch };
                cmd.Parameters.AddWithValue("@Id", template.Id ?? string.Empty);
                cmd.Parameters.AddWithValue("@Name", template.Name ?? string.Empty);
                cmd.Parameters.AddWithValue("@Category", template.Category ?? string.Empty);
                cmd.Parameters.AddWithValue("@BlockType", template.BlockType.ToString());
                cmd.Parameters.AddWithValue("@ContentJson", json);
                cmd.Parameters.AddWithValue("@ModifiedAt", template.ModifiedAt);
                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);
        }

        private async System.Threading.Tasks.Task<List<ProposalBlockTemplate>> LoadAllAsync(System.Threading.CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
SELECT ContentJson
FROM dbo.ProposalBlockTemplates
ORDER BY Category, Name;";

                var list = new List<ProposalBlockTemplate>();
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SqlTimeouts.UiFacing };
                await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt);
                while (await r.ReadAsync(innerCt))
                {
                    var json = r.GetStringOrEmpty(0);
                    if (string.IsNullOrWhiteSpace(json))
                        continue;

                    var template = JsonSerializer.Deserialize<ProposalBlockTemplate>(json, JsonOptions);
                    if (template is not null)
                        list.Add(template);
                }

                return list;
            }, ct);
        }

        private async System.Threading.Tasks.Task DeleteAsync(string id, System.Threading.CancellationToken ct = default)
        {
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = "DELETE FROM dbo.ProposalBlockTemplates WHERE Id = @Id;";

                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SqlTimeouts.Batch };
                cmd.Parameters.AddWithValue("@Id", id ?? string.Empty);
                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);
        }
    }
}
