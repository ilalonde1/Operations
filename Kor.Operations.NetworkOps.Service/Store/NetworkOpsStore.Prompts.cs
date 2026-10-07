#nullable enable
using System.Data;
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Core.Prompts;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.NetworkOps.Service.Store;

// The Prompt Library's runs (db/KorNetworkOps/007_PromptLibrary.sql): what was handed out, and what came back; and the
// knowledge cards sessions bank with their reports (008_AskAndKnowledgeCards.sql). Every 008 read degrades to "none"
// until 008 has run, so the library keeps working without it.
internal sealed partial class NetworkOpsStore
{
    public async Task<bool> PromptRunsAvailableAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "SELECT CASE WHEN OBJECT_ID(N'NetworkOps.PromptRuns', N'U') IS NULL THEN 0 ELSE 1 END;");
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! == 1;
    }

    /// <summary>Whether 008 has run: asks keep their question, and reports can bank a knowledge card.</summary>
    public async Task<bool> KnowledgeAvailableAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "SELECT CASE WHEN OBJECT_ID(N'NetworkOps.KnowledgeCards', N'U') IS NULL OR COL_LENGTH(N'NetworkOps.PromptRuns', N'Question') IS NULL THEN 0 ELSE 1 END;");
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! == 1;
    }

    /// <summary>Whether 012 has run: a card carries a plain-English explanation and may amend an earlier card. The service
    /// may be deployed before the migration is applied, so card reads/writes degrade to the 008 shape until it is.</summary>
    public async Task<bool> CardsAmendableAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "SELECT CASE WHEN COL_LENGTH(N'NetworkOps.KnowledgeCards', N'Plain') IS NULL THEN 0 ELSE 1 END;");
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! == 1;
    }

    /// <summary>Whether a card id exists, so an amendment names a real card to supersede (a clean 400 instead of an FK error).</summary>
    public async Task<bool> CardExistsAsync(long cardId, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, "SELECT CASE WHEN EXISTS (SELECT 1 FROM NetworkOps.KnowledgeCards WHERE CardId = @id) THEN 1 ELSE 0 END;");
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = cardId;
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! == 1;
    }

    /// <summary>Records a run before its prompt is written (the prompt carries the run id). The prompt hash is set after.</summary>
    /// <param name="question">An ask's question; kept only once 008 has run (else the subject carries its start).</param>
    public async Task<long> CreatePromptRunAsync(string kind, string subject, int? deviceId, long? findingId, string? ruleKey, string by, byte[] tokenSha256,
        string? question, bool knowledge, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, knowledge
            ? """
              INSERT NetworkOps.PromptRuns (Kind, Subject, DeviceId, FindingId, RuleKey, CreatedBy, PromptSha256, TokenSha256, Question)
              OUTPUT inserted.RunId VALUES (@k, @s, @d, @f, @r, @by, 0x00, @t, @q);
              """
            : """
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
        if (knowledge) cmd.Parameters.Add("@q", SqlDbType.NVarChar, 2000).Value = (object?)Truncate(question, 2000) ?? DBNull.Value;
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

    /// <param name="Corrected">True when this report replaced an earlier one from the same run.</param>
    public sealed record ReportedRun(long RunId, int? DeviceId, string Subject, string CreatedBy, bool Corrected);

    /// <summary>
    /// Writes a session's outcome with the run's own token (compared in SQL against its SHA-256). The token is reusable
    /// until Ian decides the run: a later report REPLACES the earlier outcome, learning and card, so a session that got
    /// it wrong can post the correction (2026-10-07: run 14 reported not-solved, then fixed it, and the token refused).
    /// Once the run's learning is accepted or rejected it is locked. Run is null when the token is wrong or the run is
    /// unknown (Locked false), or when the run is already decided (Locked true). A card is written in the same
    /// transaction, as proposed: it waits for Ian with the run's learning.
    /// </summary>
    public async Task<(ReportedRun? Run, bool Locked)> RecordPromptOutcomeAsync(long runId, byte[] tokenSha256, string outcome, string summary, string? learned,
        CardProposal? card, CancellationToken ct)
    {
        // The plain explanation + amends link land only when 012 has run; before that a card still banks in the 008 shape.
        var amendable = card is not null && await CardsAmendableAsync(ct).ConfigureAwait(false);
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        // Undecided = no learning waiting (NULL) or waiting for Ian ('proposed'); accepted/rejected runs never change.
        await using var cmd = Cmd(c, """
            UPDATE NetworkOps.PromptRuns
            SET OutcomeUtc = SYSUTCDATETIME(), Outcome = @o, Summary = @s, LearnedText = @l,
                LearnedStatus = CASE WHEN @l IS NULL AND @card = 0 THEN NULL ELSE 'proposed' END
            OUTPUT inserted.RunId, inserted.DeviceId, inserted.Subject, inserted.CreatedBy, CAST(CASE WHEN deleted.OutcomeUtc IS NULL THEN 0 ELSE 1 END AS bit)
            WHERE RunId = @id AND TokenSha256 = @t AND (LearnedStatus IS NULL OR LearnedStatus = 'proposed');
            """);
        cmd.Transaction = tx;
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = runId;
        cmd.Parameters.Add("@t", SqlDbType.Binary, 32).Value = tokenSha256;
        cmd.Parameters.Add("@o", SqlDbType.VarChar, 16).Value = outcome;
        cmd.Parameters.Add("@s", SqlDbType.NVarChar, 2000).Value = Truncate(summary, 2000)!;
        cmd.Parameters.Add("@l", SqlDbType.NVarChar, 2000).Value = string.IsNullOrWhiteSpace(learned) ? DBNull.Value : Truncate(learned.Trim(), 2000)!;
        cmd.Parameters.Add("@card", SqlDbType.Bit).Value = card is not null;
        ReportedRun? run = null;
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            if (await r.ReadAsync(ct).ConfigureAwait(false))
                run = new ReportedRun(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetBoolean(4));
        if (run is null)
        {
            // The right token on a decided run is a different answer from a wrong token: the session can be told why.
            await using var probe = Cmd(c, "SELECT CASE WHEN EXISTS (SELECT 1 FROM NetworkOps.PromptRuns WHERE RunId = @id AND TokenSha256 = @t) THEN 1 ELSE 0 END;", tx);
            probe.Parameters.Add("@id", SqlDbType.BigInt).Value = runId;
            probe.Parameters.Add("@t", SqlDbType.Binary, 32).Value = tokenSha256;
            var locked = Convert.ToInt32(await probe.ExecuteScalarAsync(ct).ConfigureAwait(false)) == 1;
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            return (null, locked);
        }

        // One card per run (UX_NetworkOps_KnowledgeCards_Run), so a correction rewrites the run's card IN PLACE -- never a
        // delete, which another card's AmendsCardId could block. The run is undecided, so that card was never accepted.
        // A correction without a card withdraws the earlier one (retired, its slot kept for a later correction to reuse).
        if (card is null && run.Corrected && await KnowledgeAvailableAsync(ct).ConfigureAwait(false))
        {
            await using var withdraw = Cmd(c, """
                UPDATE NetworkOps.KnowledgeCards SET Status = 'retired', DecidedUtc = SYSUTCDATETIME(), DecidedBy = N'withdrawn by its session''s correction'
                WHERE SourceRunId = @run AND Status = 'proposed';
                """, tx);
            withdraw.Parameters.Add("@run", SqlDbType.BigInt).Value = run.RunId;
            await withdraw.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        if (card is not null)
        {
            await using var ins = Cmd(c, amendable
                ? """
                  IF EXISTS (SELECT 1 FROM NetworkOps.KnowledgeCards WHERE SourceRunId = @run)
                      UPDATE NetworkOps.KnowledgeCards
                      SET Title = @ti, AppliesTo = @ap, Symptom = @sy, Cause = @ca, HowToCheck = @ch, Fix = @fx, Tags = @tg, Plain = @pl,
                          AmendsCardId = NULLIF(@am, CardId), SourceDeviceId = @dev, Status = 'proposed', DecidedUtc = NULL, DecidedBy = NULL, CreatedUtc = SYSUTCDATETIME()
                      WHERE SourceRunId = @run;
                  ELSE
                      INSERT NetworkOps.KnowledgeCards (Title, AppliesTo, Symptom, Cause, HowToCheck, Fix, Tags, Plain, AmendsCardId, SourceRunId, SourceDeviceId)
                      VALUES (@ti, @ap, @sy, @ca, @ch, @fx, @tg, @pl, @am, @run, @dev);
                  """
                : """
                  IF EXISTS (SELECT 1 FROM NetworkOps.KnowledgeCards WHERE SourceRunId = @run)
                      UPDATE NetworkOps.KnowledgeCards
                      SET Title = @ti, AppliesTo = @ap, Symptom = @sy, Cause = @ca, HowToCheck = @ch, Fix = @fx, Tags = @tg,
                          SourceDeviceId = @dev, Status = 'proposed', DecidedUtc = NULL, DecidedBy = NULL, CreatedUtc = SYSUTCDATETIME()
                      WHERE SourceRunId = @run;
                  ELSE
                      INSERT NetworkOps.KnowledgeCards (Title, AppliesTo, Symptom, Cause, HowToCheck, Fix, Tags, SourceRunId, SourceDeviceId)
                      VALUES (@ti, @ap, @sy, @ca, @ch, @fx, @tg, @run, @dev);
                  """);
            ins.Transaction = tx;
            ins.Parameters.Add("@ti", SqlDbType.NVarChar, 200).Value = Truncate(card.Title.Trim(), 200)!;
            ins.Parameters.Add("@ap", SqlDbType.NVarChar, 400).Value = Truncate(card.AppliesTo.Trim(), 400)!;
            ins.Parameters.Add("@sy", SqlDbType.NVarChar, 2000).Value = Truncate(card.Symptom.Trim(), 2000)!;
            ins.Parameters.Add("@ca", SqlDbType.NVarChar, 2000).Value = Opt(card.Cause, 2000);
            ins.Parameters.Add("@ch", SqlDbType.NVarChar, 2000).Value = Opt(card.Check, 2000);
            ins.Parameters.Add("@fx", SqlDbType.NVarChar, 2000).Value = Opt(card.Fix, 2000);
            ins.Parameters.Add("@tg", SqlDbType.NVarChar, 400).Value = Opt(card.Tags, 400);
            if (amendable)
            {
                ins.Parameters.Add("@pl", SqlDbType.NVarChar, 2000).Value = Opt(card.Plain, 2000);
                ins.Parameters.Add("@am", SqlDbType.BigInt).Value = (object?)card.AmendsCardId ?? DBNull.Value;
            }
            ins.Parameters.Add("@run", SqlDbType.BigInt).Value = run.RunId;
            ins.Parameters.Add("@dev", SqlDbType.Int).Value = (object?)run.DeviceId ?? DBNull.Value;
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return (run, false);
    }

    private static object Opt(string? s, int max) => string.IsNullOrWhiteSpace(s) ? DBNull.Value : Truncate(s.Trim(), max)!;

    public async Task<IReadOnlyList<PromptRunRow>> PromptRunsAsync(int top, CancellationToken ct)
    {
        var list = new List<PromptRunRow>();
        try
        {
            var knowledge = await KnowledgeAvailableAsync(ct).ConfigureAwait(false);
            var amendable = knowledge && await CardsAmendableAsync(ct).ConfigureAwait(false);
            // The card columns, in a fixed order (Title, Plain, AppliesTo, Symptom, Cause, Check, Fix, Tags, Amends), so the
            // approval view can show the plain explanation and the full technical card. Plain/Amends only exist after 012.
            var cardCols = !knowledge
                ? "CAST(NULL AS nvarchar(200)), CAST(NULL AS nvarchar(2000)), CAST(NULL AS nvarchar(400)), CAST(NULL AS nvarchar(2000)), CAST(NULL AS nvarchar(2000)), CAST(NULL AS nvarchar(2000)), CAST(NULL AS nvarchar(2000)), CAST(NULL AS nvarchar(400)), CAST(NULL AS bigint)"
                : amendable
                ? "k.Title, k.Plain, k.AppliesTo, k.Symptom, k.Cause, k.HowToCheck, k.Fix, k.Tags, k.AmendsCardId"
                : "k.Title, CAST(NULL AS nvarchar(2000)), k.AppliesTo, k.Symptom, k.Cause, k.HowToCheck, k.Fix, k.Tags, CAST(NULL AS bigint)";
            await using var c = await OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = Cmd(c, knowledge
                ? $"""
                  SELECT TOP (@n) p.RunId, p.Kind, p.Subject, p.CreatedBy, p.CreatedUtc, p.OutcomeUtc, p.Outcome, p.Summary, p.LearnedText, p.LearnedStatus,
                         p.Question, {cardCols}
                  FROM NetworkOps.PromptRuns p LEFT JOIN NetworkOps.KnowledgeCards k ON k.SourceRunId = p.RunId
                  ORDER BY p.RunId DESC;
                  """
                : $"""
                  SELECT TOP (@n) RunId, Kind, Subject, CreatedBy, CreatedUtc, OutcomeUtc, Outcome, Summary, LearnedText, LearnedStatus,
                         CAST(NULL AS nvarchar(2000)), {cardCols}
                  FROM NetworkOps.PromptRuns ORDER BY RunId DESC;
                  """);
            cmd.Parameters.Add("@n", SqlDbType.Int).Value = top;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                list.Add(new PromptRunRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), Utc(r, 4)!.Value, Utc(r, 5),
                    S(r, 6), S(r, 7), S(r, 8), S(r, 9), S(r, 10), S(r, 11),
                    S(r, 12), S(r, 13), S(r, 14), S(r, 15), S(r, 16), S(r, 17), S(r, 18), r.IsDBNull(19) ? null : r.GetInt64(19)));
        }
        catch (SqlException ex) when (MissingObject(ex, "Learning layer (prompts/knowledge migration)")) { }
        return list;
    }

    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    /// <summary>
    /// Ian's decision on what a run proposed: its learning and its card together. False when the run has nothing
    /// waiting for a decision.
    /// </summary>
    public async Task<LearnedDecisionResult> DecideLearnedAsync(long runId, bool accept, string by, CancellationToken ct)
    {
        var knowledge = await KnowledgeAvailableAsync(ct).ConfigureAwait(false);
        var amendable = knowledge && await CardsAmendableAsync(ct).ConfigureAwait(false);
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        // One transaction for the updates: a failure between them must not leave the run 'accepted' while its card
        // stays 'proposed' (as RecordPromptOutcomeAsync already does for the outcome+card pair).
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var cmd = Cmd(c, !knowledge
            ? "UPDATE NetworkOps.PromptRuns SET LearnedStatus = @s WHERE RunId = @id AND LearnedStatus = 'proposed'; SELECT @@ROWCOUNT;"
            : amendable
            ? """
              UPDATE NetworkOps.PromptRuns SET LearnedStatus = @s WHERE RunId = @id AND LearnedStatus = 'proposed';
              IF @@ROWCOUNT = 1
              BEGIN
                  UPDATE NetworkOps.KnowledgeCards SET Status = @s, DecidedUtc = SYSUTCDATETIME(), DecidedBy = @by
                  WHERE SourceRunId = @id AND Status = 'proposed';
                  -- Accepting an amendment retires the card it supersedes (the replace-by-retire path).
                  IF @s = 'accepted'
                      UPDATE NetworkOps.KnowledgeCards SET Status = 'retired', DecidedUtc = SYSUTCDATETIME(), DecidedBy = @by
                      WHERE CardId IN (SELECT AmendsCardId FROM NetworkOps.KnowledgeCards WHERE SourceRunId = @id AND AmendsCardId IS NOT NULL)
                        AND Status IN ('accepted', 'proposed');
                  SELECT 1;
              END
              ELSE SELECT 0;
              """
            : """
              UPDATE NetworkOps.PromptRuns SET LearnedStatus = @s WHERE RunId = @id AND LearnedStatus = 'proposed';
              IF @@ROWCOUNT = 1
              BEGIN
                  UPDATE NetworkOps.KnowledgeCards SET Status = @s, DecidedUtc = SYSUTCDATETIME(), DecidedBy = @by
                  WHERE SourceRunId = @id AND Status = 'proposed';
                  SELECT 1;
              END
              ELSE SELECT 0;
              """, tx);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = runId;
        cmd.Parameters.Add("@s", SqlDbType.VarChar, 16).Value = accept ? "accepted" : "rejected";
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = by;
        var decided = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false)) == 1;

        // Tie the verdict to the finding: accepting a session whose verdict RESOLVES what it was about (solved, or
        // no-action = nothing real/stale) closes that finding in the SAME transaction -- the decision and the finding can
        // never be out of step (Ian, 2026-10-05: "this should all be tied together"). partly/not-solved leave it open.
        var findingSettled = false;
        if (decided && accept)
        {
            string? outcome = null; long? findingId = null;
            await using (var read = Cmd(c, "SELECT Outcome, FindingId FROM NetworkOps.PromptRuns WHERE RunId = @rid;", tx))
            {
                read.Parameters.Add("@rid", SqlDbType.BigInt).Value = runId;
                await using var rr = await read.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await rr.ReadAsync(ct).ConfigureAwait(false))
                {
                    outcome = rr.IsDBNull(0) ? null : rr.GetString(0);
                    findingId = rr.IsDBNull(1) ? null : rr.GetInt64(1);
                }
            }
            if (findingId is { } fid && PromptVerdict.ResolvesFinding(outcome))
            {
                await using var ack = Cmd(c, """
                    UPDATE NetworkOps.Findings
                    SET AcknowledgedUtc = SYSUTCDATETIME(), AcknowledgedBy = @aby, SnoozedUntilUtc = NULL, AckNote = @note
                    WHERE FindingId = @fid AND ClearedUtc IS NULL AND AcknowledgedUtc IS NULL;
                    """, tx);
                ack.Parameters.Add("@fid", SqlDbType.BigInt).Value = fid;
                ack.Parameters.Add("@aby", SqlDbType.NVarChar, 128).Value = by;
                ack.Parameters.Add("@note", SqlDbType.NVarChar, 1000).Value = $"Closed from an Ask Claude session (#{runId}): verdict '{outcome}'.";
                findingSettled = await ack.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
            }
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return new LearnedDecisionResult(decided, findingSettled);
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
                WHERE p.LearnedStatus = 'accepted' AND p.RuleKey IS NOT NULL AND p.LearnedText IS NOT NULL;
                """);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                if (FixLearning.FamilyOf(r.GetString(0)) == family)
                    list.Add(new SessionLearning(Utc(r, 1) ?? DateTime.UtcNow, r.GetString(2), r.GetString(3)));
        }
        catch (SqlException ex) when (MissingObject(ex, "Learning layer (prompts/knowledge migration)")) { }
        return list;
    }

    /// <summary>Knowledge cards, newest first: accepted only (what prompts carry), or every status (the library's own view).</summary>
    public async Task<IReadOnlyList<KnowledgeCard>> KnowledgeCardsAsync(bool acceptedOnly, CancellationToken ct)
    {
        var list = new List<KnowledgeCard>();
        try
        {
            var amendable = await CardsAmendableAsync(ct).ConfigureAwait(false);
            await using var c = await OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = Cmd(c, $"""
                SELECT k.CardId, k.Title, k.AppliesTo, k.Symptom, k.Cause, k.HowToCheck, k.Fix, k.Tags, d.Name, k.SourceRunId, k.Status, k.CreatedUtc,
                       {(amendable ? "k.Plain, k.AmendsCardId" : "CAST(NULL AS nvarchar(2000)), CAST(NULL AS bigint)")}
                FROM NetworkOps.KnowledgeCards k LEFT JOIN NetworkOps.Devices d ON d.DeviceId = k.SourceDeviceId
                {(acceptedOnly ? "WHERE k.Status = 'accepted'" : "")}
                ORDER BY k.CardId DESC;
                """);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                list.Add(new KnowledgeCard(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), S(r, 4), S(r, 5), S(r, 6), S(r, 7), S(r, 8),
                    r.IsDBNull(9) ? null : r.GetInt64(9), r.GetString(10), Utc(r, 11)!.Value, S(r, 12), r.IsDBNull(13) ? null : r.GetInt64(13)));
        }
        catch (SqlException ex) when (MissingObject(ex, "Learning layer (prompts/knowledge migration)")) { }
        return list;
    }
}
