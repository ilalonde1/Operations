#nullable enable
using System;
using System.Text.Json;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Core.Services;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.Data
{
    public sealed class SqlBrochureProposalStore : SqlJsonStore<BrochureProposal>, IBrochureProposalStore
    {
        protected override string TableName => "dbo.BrochureProposals";

        // Strip image bytes when loading the full list — picker only needs names/metadata.
        protected override JsonSerializerOptions LoadAllDeserializeOptions => JsonOptionsNoImages;

        public SqlBrochureProposalStore(string connectionString) : base(connectionString)
        {
            // Tables created via manual DDL script — transmittals_app lacks CREATE TABLE permission.
        }

        // Explicit IBrochureProposalStore.Load — base class Load<T> return type satisfies the interface.
        BrochureProposal? IBrochureProposalStore.Load(string id) => Load(id);

        public void Save(BrochureProposal proposal)
        {
            if (proposal is null) throw new ArgumentNullException(nameof(proposal));
            proposal.ModifiedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(proposal, JsonOptions);

            const string sql = @"
MERGE dbo.BrochureProposals AS t
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
    }
}
