#nullable enable
using System.Data;
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Core.Prompts;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.NetworkOps.Service.Store;

// The Prompt Library's runs (db/KorNetworkOps/007_PromptLibrary.sql): what was handed out, and what came back.
internal sealed partial class NetworkOpsStore
{
    public async Task<bool> PromptRunsAvailableAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "SELECT CASE WHEN OBJECT_ID(N'NetworkOps.PromptRuns', N'U') IS NULL THEN 0 ELSE 1 END;");
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! == 1;
    }

    /// <summary>Records a run before its prompt is written (the prompt carries the run id). The prompt hash is set after.</summary>
    public async Task<long> CreatePromptRunAsync(string kind, string subject, int? deviceId, long? findingId, string? ruleKey, string by, byte[] tokenSha256, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            INSERT NetworkOps.PromptRuns (Kind, Subject, DeviceId, FindingId, RuleKey, CreatedBy, PromptSha256, TokenSha256)
            OUTPUT inserted.RunId VALUES (@k, @s, @d, @f, @r, @by, 0x00, @t);
            """);
        cmd.Parameters.Add("@k", SqlDbType.VarChar, 16).Value = kind;
        cmd.Parameters.Add("@s", SqlDbType.NVarChar, 200).Value = Truncate(subject, 200)!;
        cmd.Parameters.Add("@d", SqlDbType.Int).Value = (object?)deviceId ?? DBNull.Value;
        cmd.Parameters.Add("@f", SqlDbType.BigInt).Value = (object?)findingId ?? DBNull.Value;
        cmd.Parameters.Add("@r", SqlDbType.VarChar, 100).Value = (object?)ruleKey ?? DBNull.Value;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = by;
        cmd.Parameters.Add("@t", SqlDbType.Binary, 32).Value = tokenSha256;
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task SetPromptHashAsync(long runId, byte[] promptSha256, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "UPDATE NetworkOps.PromptRuns SET PromptSha256 = @h WHERE RunId = @id;");
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = runId;
        cmd.Parameters.Add("@h", SqlDbType.Binary, 32).Value = promptSha256;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public sealed record ReportedRun(long RunId, int? DeviceId, string Subject, string CreatedBy);

    /// <summary>
    /// Writes a session's outcome -- once: only while no outcome is recorded and only with the run's own token (compared
    /// in SQL against its SHA-256). Null when the token is wrong, the run is unknown, or it has already reported.
    /// </summary>
    public async Task<ReportedRun?> RecordPromptOutcomeAsync(long runId, byte[] tokenSha256, string outcome, string summary, string? learned, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, """
            UPDATE NetworkOps.PromptRuns
            SET OutcomeUtc = SYSUTCDATETIME(), Outcome = @o, Summary = @s, LearnedText = @l, LearnedStatus = CASE WHEN @l IS NULL THEN NULL ELSE 'proposed' END
            OUTPUT inserted.RunId, inserted.DeviceId, inserted.Subject, inserted.CreatedBy
            WHERE RunId = @id AND TokenSha256 = @t AND OutcomeUtc IS NULL;
            """);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = runId;
        cmd.Parameters.Add("@t", SqlDbType.Binary, 32).Value = tokenSha256;
        cmd.Parameters.Add("@o", SqlDbType.VarChar, 16).Value = outcome;
        cmd.Parameters.Add("@s", SqlDbType.NVarChar, 2000).Value = Truncate(summary, 2000)!;
        cmd.Parameters.Add("@l", SqlDbType.NVarChar, 2000).Value = string.IsNullOrWhiteSpace(learned) ? DBNull.Value : Truncate(learned.Trim(), 2000)!;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false)
            ? new ReportedRun(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt32(1), r.GetString(2), r.GetString(3))
            : null;
    }

    public async Task<IReadOnlyList<PromptRunRow>> PromptRunsAsync(int top, CancellationToken ct)
    {
        var list = new List<PromptRunRow>();
        try
        {
            await using var c = await OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = Cmd(c, """
                SELECT TOP (@n) RunId, Kind, Subject, CreatedBy, CreatedUtc, OutcomeUtc, Outcome, Summary, LearnedText, LearnedStatus
                FROM NetworkOps.PromptRuns ORDER BY RunId DESC;
                """);
            cmd.Parameters.Add("@n", SqlDbType.Int).Value = top;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                list.Add(new PromptRunRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), Utc(r, 4)!.Value, Utc(r, 5),
                    r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9)));
        }
        catch (SqlException ex) when (ex.Number == 208) { }
        return list;
    }

    /// <summary>Ian's decision on a proposed learning. False when there is no proposed learning on that run.</summary>
    public async Task<bool> DecideLearnedAsync(long runId, bool accept, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "UPDATE NetworkOps.PromptRuns SET LearnedStatus = @s WHERE RunId = @id AND LearnedStatus = 'proposed';");
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = runId;
        cmd.Parameters.Add("@s", SqlDbType.VarChar, 16).Value = accept ? "accepted" : "rejected";
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    /// <summary>Accepted learnings about the same kind of problem (rule family), for every later prompt about it.</summary>
    public async Task<IReadOnlyList<SessionLearning>> AcceptedLearningsAsync(string ruleKey, CancellationToken ct)
    {
        var family = FixLearning.FamilyOf(ruleKey);
        var list = new List<SessionLearning>();
        try
        {
            await using var c = await OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = Cmd(c, """
                SELECT p.RuleKey, p.OutcomeUtc, ISNULL(d.Name, p.Subject), p.LearnedText
                FROM NetworkOps.PromptRuns p LEFT JOIN NetworkOps.Devices d ON d.DeviceId = p.DeviceId
                WHERE p.LearnedStatus = 'accepted' AND p.RuleKey IS NOT NULL;
                """);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                if (FixLearning.FamilyOf(r.GetString(0)) == family)
                    list.Add(new SessionLearning(Utc(r, 1) ?? DateTime.UtcNow, r.GetString(2), r.GetString(3)));
        }
        catch (SqlException ex) when (ex.Number == 208) { }
        return list;
    }
}
