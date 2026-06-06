#nullable enable
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Intel;

public sealed class ProjectIntelPersistenceService
{
    private const int CommandTimeoutSeconds = 30;

    private readonly string _connectionString;
    private readonly ILogger<ProjectIntelPersistenceService> _logger;

    public ProjectIntelPersistenceService(string connectionString, ILogger<ProjectIntelPersistenceService> logger)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PersistAsync(
        ExtractedProjectIntel extracted,
        string providerName,
        long sourceEnrichmentId,
        DateTimeOffset refreshedAtUtc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(extracted);
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("Provider name is required.", nameof(providerName));
        }

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            var touchedMpis = new HashSet<long>();

            foreach (var draft in extracted.Projects)
            {
                touchedMpis.Add(draft.MajorProjectsInventoryId);
                await MergeProjectAsync(con, tx, draft, providerName, sourceEnrichmentId, refreshedAtUtc, ct).ConfigureAwait(false);
            }

            foreach (var draft in extracted.Signals)
            {
                touchedMpis.Add(draft.MajorProjectsInventoryId);
                await MergeSignalAsync(con, tx, draft, providerName, sourceEnrichmentId, refreshedAtUtc, ct).ConfigureAwait(false);
            }

            foreach (var draft in extracted.Actions)
            {
                touchedMpis.Add(draft.MajorProjectsInventoryId);
                await MergeActionAsync(con, tx, draft, providerName, sourceEnrichmentId, refreshedAtUtc, ct).ConfigureAwait(false);
            }

            foreach (var draft in extracted.Risks)
            {
                touchedMpis.Add(draft.MajorProjectsInventoryId);
                await MergeRiskAsync(con, tx, draft, providerName, sourceEnrichmentId, refreshedAtUtc, ct).ConfigureAwait(false);
            }

            foreach (var draft in extracted.KeyPeople)
            {
                touchedMpis.Add(draft.MajorProjectsInventoryId);
                await MergeKeyPersonAsync(con, tx, draft, providerName, sourceEnrichmentId, refreshedAtUtc, ct).ConfigureAwait(false);
            }

            foreach (var mpiId in touchedMpis)
            {
                var retired = await RetireSupersededAsync(con, tx, mpiId, providerName, refreshedAtUtc, ct).ConfigureAwait(false);
                if (retired > 0)
                {
                    _logger.LogInformation(
                        "Project intel retirement: MPI {MpiId} provider {ProviderName} retired {Count} superseded rows.",
                        mpiId,
                        providerName,
                        retired);
                }
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Project intel persistence rollback failed.");
            }

            throw;
        }
    }

    private static Task MergeProjectAsync(
        SqlConnection con,
        SqlTransaction tx,
        IntelProjectDraft draft,
        string providerName,
        long sourceEnrichmentId,
        DateTimeOffset refreshedAtUtc,
        CancellationToken ct)
    {
        const string sql = @"
MERGE opportunities.IntelProject WITH (HOLDLOCK) AS T
USING (SELECT @naturalKey AS NaturalKey) AS S
   ON T.NaturalKey = S.NaturalKey
WHEN MATCHED THEN UPDATE SET
    SourceProviderName = @provider,
    SourceEnrichmentId = @enrichmentId,
    SourceConfidence = @confidence,
    LastSeenAtUtc = @refreshed,
    RetiredAtUtc = NULL,
    RetiredReason = NULL,
    UpdatedAtUtc = sysdatetimeoffset(),
    MajorProjectsInventoryId = @mpi,
    Description = @description,
    Schedule = @schedule,
    Status = @status,
    KorAngle = @korAngle,
    Corroborations = T.Corroborations + 1
WHEN NOT MATCHED THEN INSERT
    (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
     FirstSeenAtUtc, LastSeenAtUtc, MajorProjectsInventoryId, Description,
     Schedule, Status, KorAngle)
    VALUES
    (@provider, @enrichmentId, @confidence, @naturalKey,
     @refreshed, @refreshed, @mpi, @description, @schedule, @status, @korAngle);";

        var naturalKey = IntelNaturalKey.Compute(draft.MajorProjectsInventoryId.ToString(), providerName);
        return ExecuteAsync(con, tx, sql, cmd =>
        {
            AddCommon(cmd, providerName, sourceEnrichmentId, draft.Confidence, naturalKey, refreshedAtUtc);
            cmd.Parameters.Add("@mpi", SqlDbType.BigInt).Value = draft.MajorProjectsInventoryId;
            AddNullableString(cmd, "@description", SqlDbType.NVarChar, -1, draft.Description);
            AddNullableString(cmd, "@schedule", SqlDbType.NVarChar, -1, draft.Schedule);
            AddNullableString(cmd, "@status", SqlDbType.NVarChar, -1, draft.Status);
            AddNullableString(cmd, "@korAngle", SqlDbType.NVarChar, -1, draft.KorAngle);
        }, ct);
    }

    private static Task MergeSignalAsync(
        SqlConnection con,
        SqlTransaction tx,
        IntelProjectSignalDraft draft,
        string providerName,
        long sourceEnrichmentId,
        DateTimeOffset refreshedAtUtc,
        CancellationToken ct)
    {
        const string sql = @"
MERGE opportunities.IntelProjectSignal WITH (HOLDLOCK) AS T
USING (SELECT @naturalKey AS NaturalKey) AS S
   ON T.NaturalKey = S.NaturalKey
WHEN MATCHED THEN UPDATE SET
    SourceProviderName = @provider,
    SourceEnrichmentId = @enrichmentId,
    SourceConfidence = @confidence,
    LastSeenAtUtc = @refreshed,
    RetiredAtUtc = NULL,
    RetiredReason = NULL,
    UpdatedAtUtc = sysdatetimeoffset(),
    MajorProjectsInventoryId = @mpi,
    SignalType = @signalType,
    Subject = @subject,
    Detail = @detail,
    OccurredAtApprox = @occurredAtApprox,
    SourceUrl = @sourceUrl,
    Corroborations = T.Corroborations + 1
WHEN NOT MATCHED THEN INSERT
    (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
     FirstSeenAtUtc, LastSeenAtUtc, MajorProjectsInventoryId, SignalType,
     Subject, Detail, OccurredAtApprox, SourceUrl)
    VALUES
    (@provider, @enrichmentId, @confidence, @naturalKey,
     @refreshed, @refreshed, @mpi, @signalType, @subject, @detail,
     @occurredAtApprox, @sourceUrl);";

        var naturalKey = IntelNaturalKey.Compute(
            draft.MajorProjectsInventoryId.ToString(),
            providerName,
            draft.SignalType,
            draft.Subject);
        return ExecuteAsync(con, tx, sql, cmd =>
        {
            AddCommon(cmd, providerName, sourceEnrichmentId, draft.Confidence, naturalKey, refreshedAtUtc);
            cmd.Parameters.Add("@mpi", SqlDbType.BigInt).Value = draft.MajorProjectsInventoryId;
            cmd.Parameters.Add("@signalType", SqlDbType.NVarChar, 50).Value = FirstN(draft.SignalType, 50);
            cmd.Parameters.Add("@subject", SqlDbType.NVarChar, 500).Value = FirstN(draft.Subject, 500);
            AddNullableString(cmd, "@detail", SqlDbType.NVarChar, -1, draft.Detail);
            AddNullableString(cmd, "@occurredAtApprox", SqlDbType.NVarChar, 50, draft.OccurredAtApprox);
            AddNullableString(cmd, "@sourceUrl", SqlDbType.NVarChar, 1000, draft.SourceUrl);
        }, ct);
    }

    private static Task MergeActionAsync(
        SqlConnection con,
        SqlTransaction tx,
        IntelProjectActionDraft draft,
        string providerName,
        long sourceEnrichmentId,
        DateTimeOffset refreshedAtUtc,
        CancellationToken ct)
    {
        const string sql = @"
MERGE opportunities.IntelProjectAction WITH (HOLDLOCK) AS T
USING (SELECT @naturalKey AS NaturalKey) AS S
   ON T.NaturalKey = S.NaturalKey
WHEN MATCHED THEN UPDATE SET
    SourceProviderName = @provider,
    SourceEnrichmentId = @enrichmentId,
    SourceConfidence = @confidence,
    LastSeenAtUtc = @refreshed,
    RetiredAtUtc = NULL,
    RetiredReason = NULL,
    UpdatedAtUtc = sysdatetimeoffset(),
    MajorProjectsInventoryId = @mpi,
    ActionType = @actionType,
    Recommendation = @recommendation,
    TargetPersonName = @targetPersonName,
    TargetCanonicalOrgId = @targetCanonicalOrgId,
    TimingNotes = @timingNotes,
    Corroborations = T.Corroborations + 1
WHEN NOT MATCHED THEN INSERT
    (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
     FirstSeenAtUtc, LastSeenAtUtc, MajorProjectsInventoryId, ActionType,
     Recommendation, TargetPersonName, TargetCanonicalOrgId, TimingNotes)
    VALUES
    (@provider, @enrichmentId, @confidence, @naturalKey,
     @refreshed, @refreshed, @mpi, @actionType, @recommendation,
     @targetPersonName, @targetCanonicalOrgId, @timingNotes);";

        var naturalKey = IntelNaturalKey.Compute(
            draft.MajorProjectsInventoryId.ToString(),
            providerName,
            draft.ActionType,
            FirstN(draft.Recommendation, 200));
        return ExecuteAsync(con, tx, sql, cmd =>
        {
            AddCommon(cmd, providerName, sourceEnrichmentId, draft.Confidence, naturalKey, refreshedAtUtc);
            cmd.Parameters.Add("@mpi", SqlDbType.BigInt).Value = draft.MajorProjectsInventoryId;
            cmd.Parameters.Add("@actionType", SqlDbType.NVarChar, 50).Value = FirstN(draft.ActionType, 50);
            cmd.Parameters.Add("@recommendation", SqlDbType.NVarChar, -1).Value = draft.Recommendation;
            AddNullableString(cmd, "@targetPersonName", SqlDbType.NVarChar, 200, draft.TargetPersonName);
            cmd.Parameters.Add("@targetCanonicalOrgId", SqlDbType.BigInt).Value = (object?)draft.TargetCanonicalOrgId ?? DBNull.Value;
            AddNullableString(cmd, "@timingNotes", SqlDbType.NVarChar, 500, draft.TimingNotes);
        }, ct);
    }

    private static Task MergeRiskAsync(
        SqlConnection con,
        SqlTransaction tx,
        IntelProjectRiskDraft draft,
        string providerName,
        long sourceEnrichmentId,
        DateTimeOffset refreshedAtUtc,
        CancellationToken ct)
    {
        const string sql = @"
MERGE opportunities.IntelProjectRisk WITH (HOLDLOCK) AS T
USING (SELECT @naturalKey AS NaturalKey) AS S
   ON T.NaturalKey = S.NaturalKey
WHEN MATCHED THEN UPDATE SET
    SourceProviderName = @provider,
    SourceEnrichmentId = @enrichmentId,
    SourceConfidence = @confidence,
    LastSeenAtUtc = @refreshed,
    RetiredAtUtc = NULL,
    RetiredReason = NULL,
    UpdatedAtUtc = sysdatetimeoffset(),
    MajorProjectsInventoryId = @mpi,
    RiskType = @riskType,
    Description = @description,
    MitigationNotes = @mitigationNotes,
    Corroborations = T.Corroborations + 1
WHEN NOT MATCHED THEN INSERT
    (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
     FirstSeenAtUtc, LastSeenAtUtc, MajorProjectsInventoryId, RiskType,
     Description, MitigationNotes)
    VALUES
    (@provider, @enrichmentId, @confidence, @naturalKey,
     @refreshed, @refreshed, @mpi, @riskType, @description, @mitigationNotes);";

        var naturalKey = IntelNaturalKey.Compute(
            draft.MajorProjectsInventoryId.ToString(),
            providerName,
            draft.RiskType,
            FirstN(draft.Description, 200));
        return ExecuteAsync(con, tx, sql, cmd =>
        {
            AddCommon(cmd, providerName, sourceEnrichmentId, draft.Confidence, naturalKey, refreshedAtUtc);
            cmd.Parameters.Add("@mpi", SqlDbType.BigInt).Value = draft.MajorProjectsInventoryId;
            cmd.Parameters.Add("@riskType", SqlDbType.NVarChar, 50).Value = FirstN(draft.RiskType, 50);
            cmd.Parameters.Add("@description", SqlDbType.NVarChar, -1).Value = draft.Description;
            AddNullableString(cmd, "@mitigationNotes", SqlDbType.NVarChar, -1, draft.MitigationNotes);
        }, ct);
    }

    private static Task MergeKeyPersonAsync(
        SqlConnection con,
        SqlTransaction tx,
        IntelProjectKeyPersonDraft draft,
        string providerName,
        long sourceEnrichmentId,
        DateTimeOffset refreshedAtUtc,
        CancellationToken ct)
    {
        const string sql = @"
MERGE opportunities.IntelProjectKeyPerson WITH (HOLDLOCK) AS T
USING (SELECT @naturalKey AS NaturalKey) AS S
   ON T.NaturalKey = S.NaturalKey
WHEN MATCHED THEN UPDATE SET
    SourceProviderName = @provider,
    SourceEnrichmentId = @enrichmentId,
    SourceConfidence = @confidence,
    LastSeenAtUtc = @refreshed,
    RetiredAtUtc = NULL,
    RetiredReason = NULL,
    UpdatedAtUtc = sysdatetimeoffset(),
    MajorProjectsInventoryId = @mpi,
    DisplayName = @displayName,
    NormalizedName = @normalizedName,
    Title = @title,
    Side = @side,
    CanonicalOrgId = @canonicalOrgId,
    Corroborations = T.Corroborations + 1
WHEN NOT MATCHED THEN INSERT
    (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
     FirstSeenAtUtc, LastSeenAtUtc, MajorProjectsInventoryId, DisplayName,
     NormalizedName, Title, Side, CanonicalOrgId)
    VALUES
    (@provider, @enrichmentId, @confidence, @naturalKey,
     @refreshed, @refreshed, @mpi, @displayName, @normalizedName, @title,
     @side, @canonicalOrgId);";

        var normalizedName = IntelNaturalKey.Normalize(draft.DisplayName);
        var naturalKey = IntelNaturalKey.Compute(
            draft.MajorProjectsInventoryId.ToString(),
            providerName,
            normalizedName,
            draft.Side);
        return ExecuteAsync(con, tx, sql, cmd =>
        {
            AddCommon(cmd, providerName, sourceEnrichmentId, draft.Confidence, naturalKey, refreshedAtUtc);
            cmd.Parameters.Add("@mpi", SqlDbType.BigInt).Value = draft.MajorProjectsInventoryId;
            cmd.Parameters.Add("@displayName", SqlDbType.NVarChar, 200).Value = FirstN(draft.DisplayName, 200);
            cmd.Parameters.Add("@normalizedName", SqlDbType.NVarChar, 200).Value = FirstN(normalizedName, 200);
            AddNullableString(cmd, "@title", SqlDbType.NVarChar, 200, draft.Title);
            cmd.Parameters.Add("@side", SqlDbType.NVarChar, 20).Value = FirstN(draft.Side, 20);
            cmd.Parameters.Add("@canonicalOrgId", SqlDbType.BigInt).Value = (object?)draft.CanonicalOrgId ?? DBNull.Value;
        }, ct);
    }

    private static async Task<int> RetireSupersededAsync(
        SqlConnection con,
        SqlTransaction tx,
        long mpiId,
        string providerName,
        DateTimeOffset refreshedAtUtc,
        CancellationToken ct)
    {
        const string sql = @"
DECLARE @retired int = 0;

UPDATE opportunities.IntelProject
SET RetiredAtUtc = @refreshed, RetiredReason = N'superseded-by-refresh', UpdatedAtUtc = sysdatetimeoffset()
WHERE MajorProjectsInventoryId = @mpi AND SourceProviderName = @provider
  AND RetiredAtUtc IS NULL AND LastSeenAtUtc < @refreshed;
SET @retired += @@ROWCOUNT;

UPDATE opportunities.IntelProjectSignal
SET RetiredAtUtc = @refreshed, RetiredReason = N'superseded-by-refresh', UpdatedAtUtc = sysdatetimeoffset()
WHERE MajorProjectsInventoryId = @mpi AND SourceProviderName = @provider
  AND RetiredAtUtc IS NULL AND LastSeenAtUtc < @refreshed;
SET @retired += @@ROWCOUNT;

UPDATE opportunities.IntelProjectAction
SET RetiredAtUtc = @refreshed, RetiredReason = N'superseded-by-refresh', UpdatedAtUtc = sysdatetimeoffset()
WHERE MajorProjectsInventoryId = @mpi AND SourceProviderName = @provider
  AND RetiredAtUtc IS NULL AND LastSeenAtUtc < @refreshed;
SET @retired += @@ROWCOUNT;

UPDATE opportunities.IntelProjectRisk
SET RetiredAtUtc = @refreshed, RetiredReason = N'superseded-by-refresh', UpdatedAtUtc = sysdatetimeoffset()
WHERE MajorProjectsInventoryId = @mpi AND SourceProviderName = @provider
  AND RetiredAtUtc IS NULL AND LastSeenAtUtc < @refreshed;
SET @retired += @@ROWCOUNT;

UPDATE opportunities.IntelProjectKeyPerson
SET RetiredAtUtc = @refreshed, RetiredReason = N'superseded-by-refresh', UpdatedAtUtc = sysdatetimeoffset()
WHERE MajorProjectsInventoryId = @mpi AND SourceProviderName = @provider
  AND RetiredAtUtc IS NULL AND LastSeenAtUtc < @refreshed;
SET @retired += @@ROWCOUNT;

SELECT @retired;";

        await using var cmd = new SqlCommand(sql, con, tx) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@mpi", SqlDbType.BigInt).Value = mpiId;
        cmd.Parameters.Add("@provider", SqlDbType.NVarChar, 100).Value = FirstN(providerName, 100);
        cmd.Parameters.Add("@refreshed", SqlDbType.DateTimeOffset).Value = refreshedAtUtc;
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    private static async Task ExecuteAsync(
        SqlConnection con,
        SqlTransaction tx,
        string sql,
        Action<SqlCommand> configure,
        CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, con, tx) { CommandTimeout = CommandTimeoutSeconds };
        configure(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void AddCommon(
        SqlCommand cmd,
        string providerName,
        long sourceEnrichmentId,
        IntelConfidence confidence,
        string naturalKey,
        DateTimeOffset refreshedAtUtc)
    {
        cmd.Parameters.Add("@provider", SqlDbType.NVarChar, 100).Value = FirstN(providerName, 100);
        cmd.Parameters.Add("@enrichmentId", SqlDbType.BigInt).Value = sourceEnrichmentId;
        cmd.Parameters.Add("@confidence", SqlDbType.NVarChar, 20).Value = confidence.ToString();
        cmd.Parameters.Add("@naturalKey", SqlDbType.NVarChar, 64).Value = naturalKey;
        cmd.Parameters.Add("@refreshed", SqlDbType.DateTimeOffset).Value = refreshedAtUtc;
    }

    private static void AddNullableString(SqlCommand cmd, string name, SqlDbType type, int size, string? value)
    {
        cmd.Parameters.Add(name, type, size).Value = string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : size < 0 ? value : FirstN(value, size);
    }

    private static void AddNullableString(SqlCommand cmd, string name, SqlDbType type, string? value)
    {
        cmd.Parameters.Add(name, type).Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static string FirstN(string s, int n) => s.Length <= n ? s : s.Substring(0, n);
}
