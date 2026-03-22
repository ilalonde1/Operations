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
            // Tables created via manual DDL script — transmittals_app lacks CREATE TABLE permission.
        }

        public List<ProposalBlockTemplate> LoadAll()
        {
            var list = new List<ProposalBlockTemplate>();
            using var cn = new SqlConnection(_cs);
            cn.Open();
            using var cmd = new SqlCommand(
                "SELECT ContentJson FROM dbo.ProposalBlockTemplates ORDER BY Category, Name;",
                cn) { CommandTimeout = SqlTimeouts.UiFacing };
            using var r = cmd.ExecuteReader(CommandBehavior.SequentialAccess);
            while (r.Read())
            {
                var json = r.GetStringOrEmpty(0);
                if (string.IsNullOrWhiteSpace(json)) continue;
                var t = JsonSerializer.Deserialize<ProposalBlockTemplate>(json, JsonOptions);
                if (t is not null) list.Add(t);
            }
            return list;
        }

        public void Save(ProposalBlockTemplate template)
        {
            if (template is null) throw new ArgumentNullException(nameof(template));
            template.ModifiedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(template, JsonOptions);

            const string sql = @"
MERGE dbo.ProposalBlockTemplates AS t
USING (VALUES(@Id, @Name, @Category, @BlockType, @ContentJson, @ModifiedAt))
       AS s(Id, Name, Category, BlockType, ContentJson, ModifiedAt)
ON t.Id = s.Id
WHEN MATCHED THEN
    UPDATE SET Name = s.Name, Category = s.Category, BlockType = s.BlockType,
               ContentJson = s.ContentJson, ModifiedAt = s.ModifiedAt
WHEN NOT MATCHED THEN
    INSERT (Id, Name, Category, BlockType, ContentJson, ModifiedAt)
    VALUES (s.Id, s.Name, s.Category, s.BlockType, s.ContentJson, s.ModifiedAt);";

            using var cn = new SqlConnection(_cs);
            cn.Open();
            using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SqlTimeouts.Batch };
            cmd.Parameters.AddWithValue("@Id",          template.Id       ?? string.Empty);
            cmd.Parameters.AddWithValue("@Name",        template.Name     ?? string.Empty);
            cmd.Parameters.AddWithValue("@Category",    template.Category ?? string.Empty);
            cmd.Parameters.AddWithValue("@BlockType",   template.BlockType.ToString());
            cmd.Parameters.AddWithValue("@ContentJson", json);
            cmd.Parameters.AddWithValue("@ModifiedAt",  template.ModifiedAt);
            cmd.ExecuteNonQuery();
        }

        public void Delete(string id)
        {
            using var cn = new SqlConnection(_cs);
            cn.Open();
            using var cmd = new SqlCommand("DELETE FROM dbo.ProposalBlockTemplates WHERE Id = @Id;", cn)
                { CommandTimeout = SqlTimeouts.Batch };
            cmd.Parameters.AddWithValue("@Id", id ?? string.Empty);
            cmd.ExecuteNonQuery();
        }
    }
}
