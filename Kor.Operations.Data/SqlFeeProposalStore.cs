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
            // Tables created via manual DDL script — transmittals_app lacks CREATE TABLE permission.
        }

        public List<FeeProposal> LoadAll()
        {
            var list = new List<FeeProposal>();
            using var cn = new SqlConnection(_cs);
            cn.Open();
            using var cmd = new SqlCommand(
                "SELECT ContentJson FROM dbo.FeeProposals ORDER BY ModifiedAt DESC;",
                cn) { CommandTimeout = SqlTimeouts.UiFacing };
            using var r = cmd.ExecuteReader(CommandBehavior.SequentialAccess);
            while (r.Read())
            {
                var json = r.GetStringOrEmpty(0);
                if (string.IsNullOrWhiteSpace(json)) continue;
                var p = JsonSerializer.Deserialize<FeeProposal>(json, JsonOptions);
                if (p is not null) list.Add(p);
            }
            return list;
        }

        public void Save(FeeProposal proposal)
        {
            if (proposal is null) throw new ArgumentNullException(nameof(proposal));
            proposal.ModifiedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(proposal, JsonOptions);

            const string sql = @"
MERGE dbo.FeeProposals AS t
USING (VALUES(@Id, @Name, @ContentJson, @ModifiedAt))
       AS s(Id, Name, ContentJson, ModifiedAt)
ON t.Id = s.Id
WHEN MATCHED THEN
    UPDATE SET Name = s.Name, ContentJson = s.ContentJson, ModifiedAt = s.ModifiedAt
WHEN NOT MATCHED THEN
    INSERT (Id, Name, ContentJson, ModifiedAt)
    VALUES (s.Id, s.Name, s.ContentJson, s.ModifiedAt);";

            using var cn = new SqlConnection(_cs);
            cn.Open();
            using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SqlTimeouts.Batch };
            cmd.Parameters.AddWithValue("@Id",          proposal.Id   ?? string.Empty);
            cmd.Parameters.AddWithValue("@Name",        proposal.Name ?? string.Empty);
            cmd.Parameters.AddWithValue("@ContentJson", json);
            cmd.Parameters.AddWithValue("@ModifiedAt",  proposal.ModifiedAt);
            cmd.ExecuteNonQuery();
        }

        public void Delete(string id)
        {
            using var cn = new SqlConnection(_cs);
            cn.Open();
            using var cmd = new SqlCommand("DELETE FROM dbo.FeeProposals WHERE Id = @Id;", cn)
                { CommandTimeout = SqlTimeouts.Batch };
            cmd.Parameters.AddWithValue("@Id", id ?? string.Empty);
            cmd.ExecuteNonQuery();
        }
    }
}
