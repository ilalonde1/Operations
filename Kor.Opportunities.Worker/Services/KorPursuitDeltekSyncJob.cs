#nullable enable
using System;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Deltek;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

[DisallowConcurrentExecution]
public sealed class KorPursuitDeltekSyncJob : IJob
{
    private readonly IKorPursuitDeltekAccessor _accessor;
    private readonly ICanonicalOrgStore _canonical;
    private readonly CanonicalOrgResolver _resolver;
    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<KorPursuitDeltekSyncJob> _logger;

    public KorPursuitDeltekSyncJob(
        IKorPursuitDeltekAccessor accessor,
        ICanonicalOrgStore canonical,
        CanonicalOrgResolver resolver,
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<KorPursuitDeltekSyncJob> logger)
    {
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.KorPursuitDeltekSyncEnabled)
        {
            _logger.LogDebug(
                "{Job} skipped: feature disabled via {Flag}.",
                nameof(KorPursuitDeltekSyncJob),
                nameof(opt.KorPursuitDeltekSyncEnabled));
            return;
        }

        if (_accessor is NullKorPursuitDeltekAccessor)
        {
            _logger.LogDebug("KorPursuit Deltek sync skipped: Deltek access unavailable.");
            return;
        }

        var sw = Stopwatch.StartNew();
        var ct = context.CancellationToken;
        var explicitRows = await _accessor.GetExplicitStagePursuitsAsync(ct).ConfigureAwait(false);
        var promotionalRows = await _accessor.GetPromotionalPursuitsAsync(ct).ConfigureAwait(false);
        var inserted = 0;
        var updated = 0;
        var failures = 0;

        foreach (var row in explicitRows)
        {
            Count(await TrySyncAsync(row, PursuitExternalSources.DeltekPR, ct).ConfigureAwait(false));
        }

        foreach (var row in promotionalRows)
        {
            Count(await TrySyncAsync(row, PursuitExternalSources.DeltekPRProposals, ct).ConfigureAwait(false));
        }

        _logger.LogInformation(
            "KorPursuit Deltek sync: explicit={Explicit} promotional={Promotional} inserted={Inserted} updated={Updated} resolutionFailures={Failures} elapsedMs={Ms}.",
            explicitRows.Count,
            promotionalRows.Count,
            inserted,
            updated,
            failures,
            sw.ElapsedMilliseconds);

        void Count(int outcome)
        {
            if (outcome > 0) inserted++;
            else if (outcome == 0) updated++;
            else failures++;
        }

        async Task<int> TrySyncAsync(DeltekPursuitRow row, string externalSource, CancellationToken token)
        {
            try
            {
                return await SyncOneAsync(row, externalSource, token).ConfigureAwait(false) ? 1 : 0;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "KorPursuit Deltek sync skipped row {Source}:{Key}.", externalSource, row.Wbs1);
                return -1;
            }
        }
    }

    private async Task<bool> SyncOneAsync(DeltekPursuitRow row, string externalSource, CancellationToken ct)
    {
        var stage = MapStage(row.Stage, row.ChargeType);
        var buyerName = string.IsNullOrWhiteSpace(row.BuyerName) ? "(unknown client)" : row.BuyerName.Trim();
        var buyerCanonicalOrgId = await ResolveClendorOrgAsync(row.ClientID, buyerName, OrgKinds.KorClient, ct)
            .ConfigureAwait(false);
        if (buyerCanonicalOrgId is null && !string.IsNullOrWhiteSpace(buyerName))
        {
            buyerCanonicalOrgId = await _resolver.ResolveBuyerAsync(buyerName, ct).ConfigureAwait(false);
        }

        long? lostToCanonicalOrgId = null;
        string? lostToName = null;
        if (string.Equals(stage, PursuitStages.Lost, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(row.LostTo))
        {
            lostToName = string.IsNullOrWhiteSpace(row.LostToName) ? row.LostTo.Trim() : row.LostToName.Trim();
            lostToCanonicalOrgId = await ResolveClendorOrgAsync(row.LostTo, lostToName, OrgKinds.Unknown, ct)
                .ConfigureAwait(false);
            if (lostToCanonicalOrgId is null)
            {
                lostToCanonicalOrgId = await _resolver.ResolveVendorAsync(lostToName, ct).ConfigureAwait(false);
            }
        }

        const string sql = @"
SET XACT_ABORT ON;

DECLARE @inserted bit = CONVERT(bit, 0);

BEGIN TRAN;

UPDATE opportunities.KorPursuits WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
SET
    Stage = @stage,
    BuyerName = @buyerName,
    BuyerCanonicalOrgId = @buyerCanonicalOrgId,
    Title = @title,
    LostToCanonicalOrgId = @lostToCanonicalOrgId,
    LostToName = @lostToName,
    PursuitOpenedDate = @openDate,
    SubmittedDate = @proposalDueDate,
    AwardDate = @awardDate,
    KorBusinessDeveloper = COALESCE(KorBusinessDeveloper, @bizDev),
    KorProposalManager = COALESCE(KorProposalManager, @propMgr),
    UpdatedAtUtc = sysdatetimeoffset()
WHERE ExternalSource = @extSource AND ExternalSourceKey = @extKey;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO opportunities.KorPursuits
        (Stage, BuyerName, BuyerCanonicalOrgId, Title, LostToCanonicalOrgId, LostToName,
         PursuitOpenedDate, SubmittedDate, AwardDate,
         KorBusinessDeveloper, KorProposalManager, BidCurrency,
         ExternalSource, ExternalSourceKey, CreatedByUser, CreatedAtUtc, UpdatedAtUtc)
    VALUES
        (@stage, @buyerName, @buyerCanonicalOrgId, @title, @lostToCanonicalOrgId, @lostToName,
         @openDate, @proposalDueDate, @awardDate,
         @bizDev, @propMgr, 'CAD',
         @extSource, @extKey, 'system:deltek-sync', sysdatetimeoffset(), sysdatetimeoffset());

    SET @inserted = CONVERT(bit, 1);
END;

COMMIT TRAN;

SELECT @inserted;";

        await using var con = new SqlConnection(_options.Value.OpportunitiesDb);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@stage", SqlDbType.NVarChar, 40).Value = stage;
        cmd.Parameters.Add("@buyerName", SqlDbType.NVarChar, 300).Value = buyerName;
        cmd.Parameters.Add("@buyerCanonicalOrgId", SqlDbType.BigInt).Value = (object?)buyerCanonicalOrgId ?? DBNull.Value;
        cmd.Parameters.Add("@title", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(row.Name) ? row.Wbs1 : row.Name.Trim();
        cmd.Parameters.Add("@lostToCanonicalOrgId", SqlDbType.BigInt).Value = (object?)lostToCanonicalOrgId ?? DBNull.Value;
        cmd.Parameters.Add("@lostToName", SqlDbType.NVarChar, 300).Value = (object?)lostToName ?? DBNull.Value;
        cmd.Parameters.Add("@openDate", SqlDbType.Date).Value = (object?)row.OpenDate?.Date ?? DBNull.Value;
        cmd.Parameters.Add("@proposalDueDate", SqlDbType.Date).Value = (object?)row.ProposalDueDate?.Date ?? DBNull.Value;
        cmd.Parameters.Add("@awardDate", SqlDbType.Date).Value = (object?)row.AwardDate?.Date ?? DBNull.Value;
        cmd.Parameters.Add("@bizDev", SqlDbType.NVarChar, 100).Value = (object?)TrimOrNull(row.BusinessDeveloper) ?? DBNull.Value;
        cmd.Parameters.Add("@propMgr", SqlDbType.NVarChar, 100).Value = (object?)TrimOrNull(row.ProposalManager) ?? DBNull.Value;
        cmd.Parameters.Add("@extSource", SqlDbType.NVarChar, 40).Value = externalSource;
        cmd.Parameters.Add("@extKey", SqlDbType.NVarChar, 80).Value = row.Wbs1.Trim();

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is bool b && b;
    }

    private async Task<long?> ResolveClendorOrgAsync(
        string? clendorClientId,
        string? displayName,
        string kind,
        CancellationToken ct)
    {
        var id = TrimOrNull(clendorClientId);
        if (id is null)
        {
            return null;
        }

        var existing = await _canonical.GetCanonicalOrgByClendorIdAsync(id, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing.Id;
        }

        return (await _canonical.UpsertCanonicalOrgAsync(
            kind,
            string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim(),
            id,
            website: null,
            notes: null,
            ct).ConfigureAwait(false)).Id;
    }

    private static string MapStage(string stage, string chargeType)
    {
        if (string.Equals(stage, "InPursuit", StringComparison.OrdinalIgnoreCase)) return PursuitStages.Pursuing;
        if (string.Equals(stage, "LOST", StringComparison.OrdinalIgnoreCase)) return PursuitStages.Lost;
        if (string.Equals(stage, "DNP", StringComparison.OrdinalIgnoreCase)) return PursuitStages.Declined;
        if (string.Equals(chargeType, "P", StringComparison.OrdinalIgnoreCase)) return PursuitStages.Considering;
        return PursuitStages.Considering;
    }

    private static string? TrimOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
