#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Models.Proposal;
using Kor.Operations.Core.Services;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.Data
{
    public sealed class SqlFeeProposalStore : SqlJsonStore<FeeProposal>, IFeeProposalStore
    {
        protected override string TableName => "dbo.FeeProposals";

        public SqlFeeProposalStore(string connectionString) : base(connectionString)
        {
        }

        public async Task<FeeProposal?> LoadByIdAsync(string id, CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt).ConfigureAwait(false);
                await using var cmd = new SqlCommand(
                    "SELECT ContentJson FROM dbo.FeeProposals WHERE Id = @Id;",
                    cn) { CommandTimeout = SqlTimeouts.UiFacing };
                cmd.Parameters.AddWithValue("@Id", id ?? string.Empty);
                await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt).ConfigureAwait(false);
                if (!await r.ReadAsync(innerCt).ConfigureAwait(false))
                    return null;

                var json = r.GetStringOrEmpty(0);
                if (string.IsNullOrWhiteSpace(json))
                    return null;

                return JsonSerializer.Deserialize<FeeProposal>(json, JsonOptions);
            }, ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<FeeProposalSummary>> LoadSummariesAsync(CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                var list = new List<FeeProposalSummary>();
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt).ConfigureAwait(false);
                await using var cmd = new SqlCommand(
                    "SELECT Id, Name, ModifiedAt FROM dbo.FeeProposals ORDER BY ModifiedAt DESC;",
                    cn) { CommandTimeout = SqlTimeouts.UiFacing };
                await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt).ConfigureAwait(false);
                while (await r.ReadAsync(innerCt).ConfigureAwait(false))
                {
                    list.Add(new FeeProposalSummary(
                        r.GetStringOrEmpty(0),
                        r.GetStringOrEmpty(1),
                        r.IsDBNull(2) ? default : r.GetDateTime(2)));
                }
                return (IReadOnlyList<FeeProposalSummary>)list;
            }, ct).ConfigureAwait(false);
        }

        public async Task SaveAsync(FeeProposal proposal, CancellationToken ct = default)
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
