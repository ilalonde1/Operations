#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Models.Contracts;
using Kor.Operations.Core.Services;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.Data
{
    /// <summary>dbo.vw_ContractPositions in KorTransmittals (db/KorTransmittals/011_ContractClauseLibrary.sql).</summary>
    public sealed class SqlContractClauseStore : IContractClauseStore
    {
        private readonly string _cs;

        public SqlContractClauseStore(string connectionString)
        {
            _cs = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public async Task<List<ContractPosition>> SearchAsync(ContractPositionQuery query, CancellationToken ct = default)
        {
            if (query is null) throw new ArgumentNullException(nameof(query));
            const string sql = @"
SELECT SourceKey, Job, ClientName, ProjectName, Paper, Role, IsLatest, FileDate, FilePath, Part,
       ClauseId, Section, Heading, Topics, Words, ClauseText
  FROM dbo.vw_ContractPositions
 WHERE (@Topic IS NULL OR ('|' + Topics + '|') LIKE '%|' + @Topic + '|%')
   AND (@Client IS NULL OR ClientName = @Client)
   AND (@Text IS NULL OR ClauseText LIKE '%' + @Text + '%' OR Heading LIKE '%' + @Text + '%')
   AND (@SignedOnly = 0 OR Role = N'executed')
 ORDER BY CASE WHEN Role = N'executed' THEN 0 ELSE 1 END, ClientName, Job, FileDate DESC, Section, ClauseId
 OFFSET 0 ROWS FETCH NEXT @Take ROWS ONLY;";

            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                var list = new List<ContractPosition>();
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt).ConfigureAwait(false);
                await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SqlTimeouts.UiFacing };
                cmd.Parameters.Add("@Topic", SqlDbType.NVarChar, 64).Value = (object?)Clean(query.Topic) ?? DBNull.Value;
                cmd.Parameters.Add("@Client", SqlDbType.NVarChar, 200).Value = (object?)Clean(query.ClientName) ?? DBNull.Value;
                cmd.Parameters.Add("@Text", SqlDbType.NVarChar, 200).Value = (object?)EscapeLike(Clean(query.Text)) ?? DBNull.Value;
                cmd.Parameters.Add("@SignedOnly", SqlDbType.Bit).Value = query.SignedOnly;
                cmd.Parameters.Add("@Take", SqlDbType.Int).Value = Math.Clamp(query.Take, 1, 5000);
                await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt).ConfigureAwait(false);
                while (await r.ReadAsync(innerCt).ConfigureAwait(false))
                {
                    list.Add(new ContractPosition(
                        SourceKey: r.GetStringOrEmpty(0),
                        Job: r.IsDBNull(1) ? null : r.GetString(1),
                        ClientName: r.IsDBNull(2) ? null : r.GetString(2),
                        ProjectName: r.IsDBNull(3) ? null : r.GetString(3),
                        Paper: r.GetStringOrEmpty(4),
                        Role: r.GetStringOrEmpty(5),
                        IsLatest: !r.IsDBNull(6) && r.GetBoolean(6),
                        FileDate: r.IsDBNull(7) ? null : r.GetDateTime(7),
                        FilePath: r.GetStringOrEmpty(8),
                        Part: r.GetStringOrEmpty(9),
                        ClauseId: r.GetInt32(10),
                        Section: r.GetStringOrEmpty(11),
                        Heading: r.IsDBNull(12) ? null : r.GetString(12),
                        Topics: r.GetStringOrEmpty(13),
                        Words: r.GetInt32(14),
                        ClauseText: r.GetStringOrEmpty(15)));
                }
                return list;
            }, ct).ConfigureAwait(false);
        }

        public async Task<List<string>> ListClientsAsync(CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                var list = new List<string>();
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt).ConfigureAwait(false);
                await using var cmd = new SqlCommand(
                    "SELECT DISTINCT ClientName FROM dbo.vw_ContractPositions WHERE ClientName IS NOT NULL AND ClientName <> N'' ORDER BY ClientName;",
                    cn) { CommandTimeout = SqlTimeouts.UiFacing };
                await using var r = await cmd.ExecuteReaderAsync(innerCt).ConfigureAwait(false);
                while (await r.ReadAsync(innerCt).ConfigureAwait(false)) list.Add(r.GetStringOrEmpty(0));
                return list;
            }, ct).ConfigureAwait(false);
        }

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static string? EscapeLike(string? s)
            => s is null ? null : s.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
    }
}
