#nullable enable
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Core.Services;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.Data
{
    public sealed class SqlBrochureProposalStore : SqlJsonStore<BrochureProposal>, IBrochureProposalStore
    {
        protected override string TableName => "dbo.BrochureProposals";

        protected override JsonSerializerOptions LoadAllDeserializeOptions => JsonOptionsNoImages;

        public SqlBrochureProposalStore(string connectionString) : base(connectionString)
        {
        }

        Task<BrochureProposal?> IBrochureProposalStore.LoadAsync(string id, CancellationToken ct)
            => LoadAsync(id, ct);

        public async Task SaveAsync(BrochureProposal proposal, CancellationToken ct = default)
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

            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt).ConfigureAwait(false);
                await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SqlTimeouts.Batch };
                cmd.Parameters.AddWithValue("@Id",          proposal.Id   ?? string.Empty);
                cmd.Parameters.AddWithValue("@Name",        proposal.Name ?? string.Empty);
                cmd.Parameters.AddWithValue("@ContentJson", json);
                cmd.Parameters.AddWithValue("@ModifiedAt",  proposal.ModifiedAt);
                await cmd.ExecuteNonQueryAsync(innerCt).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
    }
}
