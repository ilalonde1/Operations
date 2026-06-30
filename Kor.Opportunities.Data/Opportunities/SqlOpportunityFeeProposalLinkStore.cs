#nullable enable
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Opportunities;

/// <summary>
/// Writes the soft, app-managed bridge between an opportunity (KorOpportunitiesDb)
/// and a fee proposal (whose record lives in KorTransmittalsDb). The link table
/// stores only the proposal GUID — there is no cross-DB FK by design, so orphan
/// cleanup is the app's responsibility. Idempotent on (OpportunityId, FeeProposalId).
/// </summary>
public interface IOpportunityFeeProposalLinkStore
{
    Task LinkAsync(long opportunityId, Guid feeProposalId, string linkedBy, CancellationToken ct);
}

public sealed class SqlOpportunityFeeProposalLinkStore : IOpportunityFeeProposalLinkStore
{
    private const int CommandTimeoutSeconds = 30;

    private const string LinkSql = @"
IF NOT EXISTS (SELECT 1 FROM opportunities.OpportunityFeeProposalLinks
               WHERE OpportunityId = @opp AND FeeProposalId = @fp)
    INSERT INTO opportunities.OpportunityFeeProposalLinks (OpportunityId, FeeProposalId, LinkedBy)
    VALUES (@opp, @fp, @by);";

    private readonly string _connectionString;

    public SqlOpportunityFeeProposalLinkStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task LinkAsync(long opportunityId, Guid feeProposalId, string linkedBy, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(LinkSql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@opp", SqlDbType.BigInt).Value = opportunityId;
        cmd.Parameters.Add("@fp", SqlDbType.UniqueIdentifier).Value = feeProposalId;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 150).Value =
            string.IsNullOrWhiteSpace(linkedBy) ? "app" : linkedBy.Trim();

        try
        {
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // A concurrent caller won the race between IF NOT EXISTS and INSERT;
            // the link already exists, which is the desired end state — idempotent success.
        }
    }
}
