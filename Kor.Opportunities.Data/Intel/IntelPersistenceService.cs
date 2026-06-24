#nullable enable
using System.Data;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Intel;

public sealed record IntelPersistResult(
    int PersonsInserted, int PersonsUpdated,
    int AffiliationsInserted, int AffiliationsUpdated,
    int SignalsInserted, int SignalsUpdated,
    int ActionsInserted, int ActionsUpdated,
    int WorksInserted, int WorksUpdated,
    int RisksInserted, int RisksUpdated,
    int NarrativesInserted, int NarrativesUpdated);

public sealed class IntelPersistenceService
{
    private const int CommandTimeoutSeconds = 30;
    private readonly string _connectionString;

    public IntelPersistenceService(string opportunitiesConnectionString)
    {
        if (string.IsNullOrWhiteSpace(opportunitiesConnectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(opportunitiesConnectionString));
        }

        _connectionString = opportunitiesConnectionString;
    }

    /// <summary>
    /// R81: KOR-side mutation of IntelAction.Status. Allowed transitions:
    /// any non-retired action → 'Open' / 'Done' / 'Dismissed'. Updates
    /// Status, StatusChangedByUser, StatusChangedAtUtc, UpdatedAtUtc.
    /// Returns true when the row was found and updated; false when the
    /// action id does not exist or is already retired (RetiredAtUtc set).
    /// </summary>
    public async Task<bool> SetActionStatusAsync(long actionId, string newStatus, string? changedByUser, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newStatus))
        {
            throw new ArgumentException("Status is required.", nameof(newStatus));
        }
        if (newStatus is not ("Open" or "Done" or "Dismissed"))
        {
            throw new ArgumentException($"Status must be 'Open', 'Done', or 'Dismissed' (got '{newStatus}').", nameof(newStatus));
        }

        const string sql = @"
UPDATE opportunities.IntelAction
   SET Status = @status,
       StatusChangedByUser = @user,
       StatusChangedAtUtc = sysdatetimeoffset(),
       UpdatedAtUtc = sysdatetimeoffset()
 WHERE Id = @id
   AND RetiredAtUtc IS NULL;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = actionId;
        cmd.Parameters.Add("@status", SqlDbType.NVarChar, 20).Value = newStatus;
        cmd.Parameters.Add("@user", SqlDbType.NVarChar, 100).Value = (object?)changedByUser ?? DBNull.Value;
        var rowsAffected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return rowsAffected > 0;
    }

    public async Task<IntelPersistResult> PersistAsync(ExtractedIntel drafts, IntelExtractionContext ctx, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            // FK + retirement guard: never attach intel to a missing or retired
            // canonical org. A concurrent merge/retire between enrichment-row
            // selection and here would otherwise throw FK-547 (aborting the whole
            // batch) or strand active intel on a dead parent. Skip cleanly instead.
            await using (var guard = new SqlCommand(
                "SELECT 1 FROM opportunities.CanonicalOrg WHERE Id = @id AND RetiredAtUtc IS NULL", con, tx)
                { CommandTimeout = CommandTimeoutSeconds })
            {
                guard.Parameters.Add("@id", SqlDbType.BigInt).Value = ctx.CanonicalOrgId;
                if (await guard.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
                {
                    await tx.RollbackAsync(ct).ConfigureAwait(false);
                    return new IntelPersistResult(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                }
            }

            var persons = new MutableCounts();
            var affiliations = new MutableCounts();
            var signals = new MutableCounts();
            var actions = new MutableCounts();
            var works = new MutableCounts();
            var risks = new MutableCounts();
            var narratives = new MutableCounts();
            var personIdsByNormalizedName = new Dictionary<string, long>(StringComparer.Ordinal);
            var uniquePersonIdsByNormalizedName = new Dictionary<string, long?>(StringComparer.Ordinal);

            foreach (var draft in drafts.People)
            {
                if (!IntelPersonNameGuard.IsValid(draft.DisplayName, out var reason))
                {
                    System.Diagnostics.Trace.TraceWarning("IntelPerson row rejected: name='{0}', reason={1}, provider={2}, enrichmentId={3}.", draft.DisplayName, reason, ctx.ProviderName, ctx.SourceEnrichmentId);
                    continue;
                }

                var personId = await ResolveOrCreatePersonAsync(
                    con,
                    tx,
                    draft.DisplayName,
                    IntelEmail.Sanitize(draft.Email),
                    draft.LinkedinUrl,
                    draft.Phone,
                    draft.Notes,
                    ctx.CanonicalOrgId,
                    ctx,
                    ct).ConfigureAwait(false);

                var normalizedName = IntelNaturalKey.Normalize(draft.DisplayName);
                if (normalizedName.Length > 0)
                {
                    personIdsByNormalizedName[BuildPersonMapKey(normalizedName, ctx.CanonicalOrgId)] = personId;
                    uniquePersonIdsByNormalizedName[normalizedName] = uniquePersonIdsByNormalizedName.ContainsKey(normalizedName)
                        ? null
                        : personId;
                }
            }

            foreach (var draft in drafts.Affiliations)
            {
                if (!IntelPersonNameGuard.IsValid(draft.PersonDisplayName, out var affReason))
                {
                    System.Diagnostics.Trace.TraceWarning("IntelPersonAffiliation row rejected: name='{0}', reason={1}, provider={2}.", draft.PersonDisplayName, affReason, ctx.ProviderName);
                    continue;
                }

                var normalizedName = IntelNaturalKey.Normalize(draft.PersonDisplayName);
                var personMapKey = BuildPersonMapKey(normalizedName, draft.CanonicalOrgId);
                if (!personIdsByNormalizedName.TryGetValue(personMapKey, out var personId))
                {
                    if (uniquePersonIdsByNormalizedName.TryGetValue(normalizedName, out var uniquePersonId) && uniquePersonId.HasValue)
                    {
                        personId = uniquePersonId.Value;
                    }
                    else
                    {
                        personId = await ResolveOrCreatePersonAsync(
                            con,
                            tx,
                            draft.PersonDisplayName,
                            null,
                            null,
                            null,
                            null,
                            draft.CanonicalOrgId,
                            ctx,
                            ct).ConfigureAwait(false);
                    }

                    if (normalizedName.Length > 0)
                    {
                        personIdsByNormalizedName[personMapKey] = personId;
                    }
                }

                affiliations.Add(await MergeAffiliationAsync(con, tx, draft, personId, ctx, ct).ConfigureAwait(false));
            }

            foreach (var draft in drafts.Signals)
            {
                signals.Add(await MergeSignalAsync(con, tx, draft, ctx, ct).ConfigureAwait(false));
            }

            foreach (var draft in drafts.Actions)
            {
                // Drain models emit "None"/"N/A" when they have nothing — that
                // is the absence of an action, not an action (IntelPlaceholders).
                if (IntelPlaceholders.IsPlaceholder(draft.Recommendation))
                {
                    continue;
                }

                var cleaned = draft with
                {
                    TargetPersonName = IntelPlaceholders.NullIfPlaceholder(draft.TargetPersonName),
                    TimingNotes = IntelPlaceholders.NullIfPlaceholder(draft.TimingNotes),
                };
                actions.Add(await MergeActionAsync(con, tx, cleaned, ctx, ct).ConfigureAwait(false));
            }

            foreach (var draft in drafts.Works)
            {
                works.Add(await MergeWorkAsync(con, tx, draft, ctx, ct).ConfigureAwait(false));
            }

            foreach (var draft in drafts.Risks)
            {
                risks.Add(await MergeRiskAsync(con, tx, draft, ctx, ct).ConfigureAwait(false));
            }

            foreach (var draft in drafts.Narratives)
            {
                narratives.Add(await MergeNarrativeAsync(con, tx, draft, ctx, ct).ConfigureAwait(false));
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);

            return new IntelPersistResult(
                persons.Inserted, persons.Updated,
                affiliations.Inserted, affiliations.Updated,
                signals.Inserted, signals.Updated,
                actions.Inserted, actions.Updated,
                works.Inserted, works.Updated,
                risks.Inserted, risks.Updated,
                narratives.Inserted, narratives.Updated);
        }
        catch
        {
            try
            {
                await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("Intel persistence rollback failed: {0}", ex.Message);
            }

            throw;
        }
    }

    private static async Task<long> ResolveOrCreatePersonAsync(
        SqlConnection con,
        SqlTransaction tx,
        string displayName,
        string? email,
        string? linkedinUrl,
        string? phone,
        string? notes,
        long orgId,
        IntelExtractionContext ctx,
        CancellationToken ct)
    {
        await using var cmd = new SqlCommand("opportunities.usp_ResolveOrCreateIntelPerson", con, tx)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = CommandTimeoutSeconds,
        };

        cmd.Parameters.Add("@displayName", SqlDbType.NVarChar, 400).Value = FirstN(displayName, 400);
        AddNullableString(cmd, "@email", SqlDbType.NVarChar, 400, email);
        AddNullableString(cmd, "@linkedinUrl", SqlDbType.NVarChar, 800, linkedinUrl);
        AddNullableString(cmd, "@phone", SqlDbType.NVarChar, 50, phone);
        AddNullableString(cmd, "@notes", SqlDbType.NVarChar, -1, notes);
        cmd.Parameters.Add("@orgId", SqlDbType.BigInt).Value = orgId;
        cmd.Parameters.Add("@sourceProviderName", SqlDbType.NVarChar, 200).Value = FirstN(ctx.ProviderName, 200);
        cmd.Parameters.Add("@emailSource", SqlDbType.NVarChar, 50).Value = DBNull.Value;
        cmd.Parameters.Add("@emailConfidence", SqlDbType.Int).Value = DBNull.Value;
        var personId = cmd.Parameters.Add("@personId", SqlDbType.BigInt);
        personId.Direction = ParameterDirection.Output;

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(personId.Value);
    }

    private static string BuildPersonMapKey(string normalizedName, long canonicalOrgId)
        => string.Concat(normalizedName, "|org:", canonicalOrgId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static Task<MergeCounts> MergeAffiliationAsync(
        SqlConnection con,
        SqlTransaction tx,
        IntelPersonAffiliationDraft draft,
        long personId,
        IntelExtractionContext ctx,
        CancellationToken ct)
    {
        if (!IntelPersonNameGuard.IsValid(draft.PersonDisplayName, out var affReason))
        {
            System.Diagnostics.Trace.TraceWarning("IntelPersonAffiliation row rejected: name='{0}', reason={1}, provider={2}.", draft.PersonDisplayName, affReason, ctx.ProviderName);
            return Task.FromResult(default(MergeCounts));
        }

        const string sql = @"
DECLARE @actions table ([Action] nvarchar(10) NOT NULL);

MERGE opportunities.IntelPersonAffiliation WITH (HOLDLOCK) AS T
USING (SELECT @naturalKey AS NaturalKey) AS S
   ON T.NaturalKey = S.NaturalKey
WHEN MATCHED THEN UPDATE SET
    SourceProviderName = @provider,
    SourceEnrichmentId = @enrichmentId,
    SourceConfidence = @confidence,
    LastSeenAtUtc = @refreshed,
    UpdatedAtUtc = sysdatetimeoffset(),
    IntelPersonId = @personId,
    CanonicalOrgId = @canonicalOrgId,
    Title = @title,
    Department = @department,
    IsCurrent = @isCurrent,
    StartDateApprox = @startDateApprox,
    EndDateApprox = @endDateApprox,
    Notes = @notes
WHEN NOT MATCHED THEN INSERT
    (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
     FirstSeenAtUtc, LastSeenAtUtc, IntelPersonId, CanonicalOrgId, Title,
     Department, IsCurrent, StartDateApprox, EndDateApprox, Notes)
    VALUES
    (@provider, @enrichmentId, @confidence, @naturalKey,
     @refreshed, @refreshed, @personId, @canonicalOrgId, @title,
     @department, @isCurrent, @startDateApprox, @endDateApprox, @notes)
OUTPUT $action INTO @actions;

SELECT
    COALESCE(SUM(CASE WHEN [Action] = N'INSERT' THEN 1 ELSE 0 END), 0) AS Inserted,
    COALESCE(SUM(CASE WHEN [Action] = N'UPDATE' THEN 1 ELSE 0 END), 0) AS Updated
FROM @actions;";

        var naturalKey = IntelNaturalKey.Compute(
            personId.ToString(),
            draft.CanonicalOrgId.ToString(),
            IntelNaturalKey.Normalize(draft.Title));
        return ExecuteMergeAsync(con, tx, sql, cmd =>
        {
            AddCommonParameters(cmd, ctx, draft.Confidence, naturalKey);
            cmd.Parameters.Add("@personId", SqlDbType.BigInt).Value = personId;
            cmd.Parameters.Add("@canonicalOrgId", SqlDbType.BigInt).Value = draft.CanonicalOrgId;
            AddNullableString(cmd, "@title", SqlDbType.NVarChar, 200, draft.Title);
            AddNullableString(cmd, "@department", SqlDbType.NVarChar, 200, draft.Department);
            cmd.Parameters.Add("@isCurrent", SqlDbType.Bit).Value = draft.IsCurrent;
            AddNullableString(cmd, "@startDateApprox", SqlDbType.NVarChar, 50, draft.StartDateApprox);
            AddNullableString(cmd, "@endDateApprox", SqlDbType.NVarChar, 50, draft.EndDateApprox);
            AddNullableString(cmd, "@notes", SqlDbType.NVarChar, -1, draft.Notes);
        }, ct);
    }

    private static Task<MergeCounts> MergeSignalAsync(
        SqlConnection con,
        SqlTransaction tx,
        IntelSignalDraft draft,
        IntelExtractionContext ctx,
        CancellationToken ct)
    {
        const string sql = @"
DECLARE @actions table ([Action] nvarchar(10) NOT NULL);

MERGE opportunities.IntelSignal WITH (HOLDLOCK) AS T
USING (SELECT @naturalKey AS NaturalKey) AS S
   ON T.NaturalKey = S.NaturalKey
WHEN MATCHED THEN UPDATE SET
    SourceProviderName = @provider,
    SourceEnrichmentId = @enrichmentId,
    SourceConfidence = @confidence,
    LastSeenAtUtc = @refreshed,
    UpdatedAtUtc = sysdatetimeoffset(),
    CanonicalOrgId = @canonicalOrgId,
    SignalType = @signalType,
    Subject = @subject,
    Detail = @detail,
    OccurredAtApprox = @occurredAtApprox,
    SourceUrl = @sourceUrl,
    Corroborations = T.Corroborations + 1
WHEN NOT MATCHED THEN INSERT
    (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
     FirstSeenAtUtc, LastSeenAtUtc, CanonicalOrgId, SignalType, Subject,
     Detail, OccurredAtApprox, SourceUrl)
    VALUES
    (@provider, @enrichmentId, @confidence, @naturalKey,
     @refreshed, @refreshed, @canonicalOrgId, @signalType, @subject,
     @detail, @occurredAtApprox, @sourceUrl)
OUTPUT $action INTO @actions;

SELECT
    COALESCE(SUM(CASE WHEN [Action] = N'INSERT' THEN 1 ELSE 0 END), 0) AS Inserted,
    COALESCE(SUM(CASE WHEN [Action] = N'UPDATE' THEN 1 ELSE 0 END), 0) AS Updated
FROM @actions;";

        var naturalKey = IntelNaturalKey.Compute(
            draft.CanonicalOrgId.ToString(),
            draft.SignalType,
            FirstN(IntelNaturalKey.Normalize(draft.Subject), 200));
        return ExecuteMergeAsync(con, tx, sql, cmd =>
        {
            AddCommonParameters(cmd, ctx, draft.Confidence, naturalKey);
            cmd.Parameters.Add("@canonicalOrgId", SqlDbType.BigInt).Value = draft.CanonicalOrgId;
            cmd.Parameters.Add("@signalType", SqlDbType.NVarChar, 50).Value = FirstN(draft.SignalType, 50);
            cmd.Parameters.Add("@subject", SqlDbType.NVarChar, 500).Value = FirstN(draft.Subject, 500);
            AddNullableString(cmd, "@detail", SqlDbType.NVarChar, -1, draft.Detail);
            AddNullableString(cmd, "@occurredAtApprox", SqlDbType.NVarChar, 50, draft.OccurredAtApprox);
            AddNullableString(cmd, "@sourceUrl", SqlDbType.NVarChar, 1000, draft.SourceUrl);
        }, ct);
    }

    private static Task<MergeCounts> MergeActionAsync(
        SqlConnection con,
        SqlTransaction tx,
        IntelActionDraft draft,
        IntelExtractionContext ctx,
        CancellationToken ct)
    {
        const string sql = @"
DECLARE @actions table ([Action] nvarchar(10) NOT NULL);

MERGE opportunities.IntelAction WITH (HOLDLOCK) AS T
USING (SELECT @naturalKey AS NaturalKey) AS S
   ON T.NaturalKey = S.NaturalKey
WHEN MATCHED THEN UPDATE SET
    SourceProviderName = @provider,
    SourceEnrichmentId = @enrichmentId,
    SourceConfidence = @confidence,
    LastSeenAtUtc = @refreshed,
    UpdatedAtUtc = sysdatetimeoffset(),
    CanonicalOrgId = @canonicalOrgId,
    ActionType = @actionType,
    Recommendation = @recommendation,
    TargetPersonName = @targetPersonName,
    TimingNotes = @timingNotes
WHEN NOT MATCHED THEN INSERT
    (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
     FirstSeenAtUtc, LastSeenAtUtc, CanonicalOrgId, ActionType, Recommendation,
     TargetPersonName, TimingNotes)
    VALUES
    (@provider, @enrichmentId, @confidence, @naturalKey,
     @refreshed, @refreshed, @canonicalOrgId, @actionType, @recommendation,
     @targetPersonName, @timingNotes)
OUTPUT $action INTO @actions;

SELECT
    COALESCE(SUM(CASE WHEN [Action] = N'INSERT' THEN 1 ELSE 0 END), 0) AS Inserted,
    COALESCE(SUM(CASE WHEN [Action] = N'UPDATE' THEN 1 ELSE 0 END), 0) AS Updated
FROM @actions;";

        var naturalKey = IntelNaturalKey.Compute(
            draft.CanonicalOrgId.ToString(),
            draft.ActionType,
            FirstN(IntelNaturalKey.Normalize(draft.Recommendation), 200));
        return ExecuteMergeAsync(con, tx, sql, cmd =>
        {
            AddCommonParameters(cmd, ctx, draft.Confidence, naturalKey);
            cmd.Parameters.Add("@canonicalOrgId", SqlDbType.BigInt).Value = draft.CanonicalOrgId;
            cmd.Parameters.Add("@actionType", SqlDbType.NVarChar, 50).Value = FirstN(draft.ActionType, 50);
            cmd.Parameters.Add("@recommendation", SqlDbType.NVarChar, -1).Value = draft.Recommendation;
            AddNullableString(cmd, "@targetPersonName", SqlDbType.NVarChar, 200, draft.TargetPersonName);
            AddNullableString(cmd, "@timingNotes", SqlDbType.NVarChar, 500, draft.TimingNotes);
        }, ct);
    }

    private static Task<MergeCounts> MergeWorkAsync(
        SqlConnection con,
        SqlTransaction tx,
        IntelWorkDraft draft,
        IntelExtractionContext ctx,
        CancellationToken ct)
    {
        const string sql = @"
DECLARE @actions table ([Action] nvarchar(10) NOT NULL);

MERGE opportunities.IntelWork WITH (HOLDLOCK) AS T
USING (SELECT @naturalKey AS NaturalKey) AS S
   ON T.NaturalKey = S.NaturalKey
WHEN MATCHED THEN UPDATE SET
    SourceProviderName = @provider,
    SourceEnrichmentId = @enrichmentId,
    SourceConfidence = @confidence,
    LastSeenAtUtc = @refreshed,
    UpdatedAtUtc = sysdatetimeoffset(),
    CanonicalOrgId = @canonicalOrgId,
    ProjectName = @projectName,
    NormalizedProjectName = @normalizedProjectName,
    Role = @role,
    YearApprox = @yearApprox,
    EstimatedValueCad = @estimatedValueCad,
    EstimatedValueText = @estimatedValueText,
    Notes = @notes
WHEN NOT MATCHED THEN INSERT
    (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
     FirstSeenAtUtc, LastSeenAtUtc, CanonicalOrgId, ProjectName, NormalizedProjectName,
     Role, YearApprox, EstimatedValueCad, EstimatedValueText, Notes)
    VALUES
    (@provider, @enrichmentId, @confidence, @naturalKey,
     @refreshed, @refreshed, @canonicalOrgId, @projectName, @normalizedProjectName,
     @role, @yearApprox, @estimatedValueCad, @estimatedValueText, @notes)
OUTPUT $action INTO @actions;

SELECT
    COALESCE(SUM(CASE WHEN [Action] = N'INSERT' THEN 1 ELSE 0 END), 0) AS Inserted,
    COALESCE(SUM(CASE WHEN [Action] = N'UPDATE' THEN 1 ELSE 0 END), 0) AS Updated
FROM @actions;";

        var normalizedProjectName = IntelNaturalKey.Normalize(draft.ProjectName);
        var naturalKey = IntelNaturalKey.Compute(
            draft.CanonicalOrgId.ToString(),
            normalizedProjectName,
            draft.Role ?? string.Empty);
        return ExecuteMergeAsync(con, tx, sql, cmd =>
        {
            AddCommonParameters(cmd, ctx, draft.Confidence, naturalKey);
            cmd.Parameters.Add("@canonicalOrgId", SqlDbType.BigInt).Value = draft.CanonicalOrgId;
            cmd.Parameters.Add("@projectName", SqlDbType.NVarChar, 500).Value = FirstN(draft.ProjectName, 500);
            cmd.Parameters.Add("@normalizedProjectName", SqlDbType.NVarChar, 500).Value = FirstN(normalizedProjectName, 500);
            AddNullableString(cmd, "@role", SqlDbType.NVarChar, 100, draft.Role);
            AddNullableString(cmd, "@yearApprox", SqlDbType.NVarChar, 20, draft.YearApprox);
            var estimatedValueCad = cmd.Parameters.Add("@estimatedValueCad", SqlDbType.Decimal);
            estimatedValueCad.Precision = 18;
            estimatedValueCad.Scale = 2;
            estimatedValueCad.Value = (object?)draft.EstimatedValueCad ?? DBNull.Value;
            AddNullableString(cmd, "@estimatedValueText", SqlDbType.NVarChar, 100, draft.EstimatedValueText);
            AddNullableString(cmd, "@notes", SqlDbType.NVarChar, -1, draft.Notes);
        }, ct);
    }

    private static Task<MergeCounts> MergeRiskAsync(
        SqlConnection con,
        SqlTransaction tx,
        IntelRiskDraft draft,
        IntelExtractionContext ctx,
        CancellationToken ct)
    {
        const string sql = @"
DECLARE @actions table ([Action] nvarchar(10) NOT NULL);

MERGE opportunities.IntelRisk WITH (HOLDLOCK) AS T
USING (SELECT @naturalKey AS NaturalKey) AS S
   ON T.NaturalKey = S.NaturalKey
WHEN MATCHED THEN UPDATE SET
    SourceProviderName = @provider,
    SourceEnrichmentId = @enrichmentId,
    SourceConfidence = @confidence,
    LastSeenAtUtc = @refreshed,
    UpdatedAtUtc = sysdatetimeoffset(),
    CanonicalOrgId = @canonicalOrgId,
    RiskType = @riskType,
    Description = @description,
    MitigationNotes = @mitigationNotes
WHEN NOT MATCHED THEN INSERT
    (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
     FirstSeenAtUtc, LastSeenAtUtc, CanonicalOrgId, RiskType, Description,
     MitigationNotes)
    VALUES
    (@provider, @enrichmentId, @confidence, @naturalKey,
     @refreshed, @refreshed, @canonicalOrgId, @riskType, @description,
     @mitigationNotes)
OUTPUT $action INTO @actions;

SELECT
    COALESCE(SUM(CASE WHEN [Action] = N'INSERT' THEN 1 ELSE 0 END), 0) AS Inserted,
    COALESCE(SUM(CASE WHEN [Action] = N'UPDATE' THEN 1 ELSE 0 END), 0) AS Updated
FROM @actions;";

        var naturalKey = IntelNaturalKey.Compute(
            draft.CanonicalOrgId.ToString(),
            draft.RiskType,
            FirstN(IntelNaturalKey.Normalize(draft.Description), 200));
        return ExecuteMergeAsync(con, tx, sql, cmd =>
        {
            AddCommonParameters(cmd, ctx, draft.Confidence, naturalKey);
            cmd.Parameters.Add("@canonicalOrgId", SqlDbType.BigInt).Value = draft.CanonicalOrgId;
            cmd.Parameters.Add("@riskType", SqlDbType.NVarChar, 50).Value = FirstN(draft.RiskType, 50);
            cmd.Parameters.Add("@description", SqlDbType.NVarChar, -1).Value = draft.Description;
            AddNullableString(cmd, "@mitigationNotes", SqlDbType.NVarChar, -1, draft.MitigationNotes);
        }, ct);
    }

    private static Task<MergeCounts> MergeNarrativeAsync(
        SqlConnection con,
        SqlTransaction tx,
        IntelNarrativeDraft draft,
        IntelExtractionContext ctx,
        CancellationToken ct)
    {
        const string sql = @"
DECLARE @actions table ([Action] nvarchar(10) NOT NULL);

MERGE opportunities.IntelNarrative WITH (HOLDLOCK) AS T
USING (SELECT @naturalKey AS NaturalKey) AS S
   ON T.NaturalKey = S.NaturalKey
WHEN MATCHED THEN UPDATE SET
    SourceProviderName = @provider,
    SourceEnrichmentId = @enrichmentId,
    SourceConfidence = @confidence,
    LastSeenAtUtc = @refreshed,
    UpdatedAtUtc = sysdatetimeoffset(),
    CanonicalOrgId = @canonicalOrgId,
    NarrativeType = @narrativeType,
    ParagraphText = @paragraphText
WHEN NOT MATCHED THEN INSERT
    (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
     FirstSeenAtUtc, LastSeenAtUtc, CanonicalOrgId, NarrativeType, ParagraphText)
    VALUES
    (@provider, @enrichmentId, @confidence, @naturalKey,
     @refreshed, @refreshed, @canonicalOrgId, @narrativeType, @paragraphText)
OUTPUT $action INTO @actions;

SELECT
    COALESCE(SUM(CASE WHEN [Action] = N'INSERT' THEN 1 ELSE 0 END), 0) AS Inserted,
    COALESCE(SUM(CASE WHEN [Action] = N'UPDATE' THEN 1 ELSE 0 END), 0) AS Updated
FROM @actions;";

        var naturalKey = IntelNaturalKey.Compute(draft.CanonicalOrgId.ToString(), draft.NarrativeType);
        return ExecuteMergeAsync(con, tx, sql, cmd =>
        {
            AddCommonParameters(cmd, ctx, draft.Confidence, naturalKey);
            cmd.Parameters.Add("@canonicalOrgId", SqlDbType.BigInt).Value = draft.CanonicalOrgId;
            cmd.Parameters.Add("@narrativeType", SqlDbType.NVarChar, 50).Value = FirstN(draft.NarrativeType, 50);
            cmd.Parameters.Add("@paragraphText", SqlDbType.NVarChar, -1).Value = draft.ParagraphText;
        }, ct);
    }

    private static async Task<MergeCounts> ExecuteMergeAsync(
        SqlConnection con,
        SqlTransaction tx,
        string sql,
        Action<SqlCommand> configure,
        CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, con, tx) { CommandTimeout = CommandTimeoutSeconds };
        configure(cmd);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return new MergeCounts(0, 0);
        }

        return new MergeCounts(reader.GetInt32(0), reader.GetInt32(1));
    }

    private static void AddCommonParameters(
        SqlCommand cmd,
        IntelExtractionContext ctx,
        IntelConfidence confidence,
        string naturalKey)
    {
        cmd.Parameters.Add("@provider", SqlDbType.NVarChar, 100).Value = FirstN(ctx.ProviderName, 100);
        cmd.Parameters.Add("@enrichmentId", SqlDbType.BigInt).Value = ctx.SourceEnrichmentId;
        cmd.Parameters.Add("@confidence", SqlDbType.NVarChar, 20).Value = confidence.ToString();
        cmd.Parameters.Add("@naturalKey", SqlDbType.NVarChar, 64).Value = naturalKey;
        cmd.Parameters.Add("@refreshed", SqlDbType.DateTimeOffset).Value = ctx.RefreshedAtUtc;
    }

    private static void AddNullableString(SqlCommand cmd, string name, SqlDbType type, int size, string? value)
    {
        cmd.Parameters.Add(name, type, size).Value = string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : size < 0 ? value : FirstN(value, size);
    }

    private static string FirstN(string s, int n) => s.Length <= n ? s : s.Substring(0, n);

    private readonly record struct MergeCounts(int Inserted, int Updated);

    private sealed class MutableCounts
    {
        public int Inserted { get; private set; }

        public int Updated { get; private set; }

        public void Add(MergeCounts counts)
        {
            Inserted += counts.Inserted;
            Updated += counts.Updated;
        }
    }
}
