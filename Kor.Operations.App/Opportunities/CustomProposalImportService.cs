#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Kor.Operations.Data;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.App.Opportunities;

public sealed class CustomProposalImportService
{
    private readonly VpOdbcDsnFactory _odbc;
    private readonly IKorPursuitStore _pursuits;
    private readonly ICanonicalOrgStore _canonical;
    private readonly ILogger<CustomProposalImportService> _logger;

    public CustomProposalImportService(
        VpOdbcDsnFactory odbc,
        IKorPursuitStore pursuits,
        ICanonicalOrgStore canonical,
        ILogger<CustomProposalImportService> logger)
    {
        _odbc = odbc;
        _pursuits = pursuits;
        _canonical = canonical;
        _logger = logger;
    }

    public sealed record ImportResult(int Total, int Imported, int Updated, int Skipped, int Errors);

    public async Task<ImportResult> RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("CustomProposal import: starting.");

        // CustomProposal.ClientID is never populated in KOR's instance; the real link
        // is via WBS1 -> PR (root row: WBS2=WBS3=' ') -> Clendor.
        const string sql = @"
SELECT
    cp.CustomPropID,
    cp.Name,
    pr.ClientID,
    COALESCE(cl.Name, '(unknown client)') AS ClientName,
    cp.Fee,
    cp.Status,
    cp.Source,
    cp.Type,
    cp.DateAdvertised,
    cp.DueDate,
    cp.SubmittalDate,
    cp.AwardDate,
    cp.WBS1,
    cp.ProposalManager
FROM CustomProposal cp
LEFT JOIN PR pr ON pr.WBS1 = cp.WBS1 AND pr.WBS2 = ' ' AND pr.WBS3 = ' '
LEFT JOIN Clendor cl ON cl.ClientID = pr.ClientID
ORDER BY cp.SubmittalDate DESC";

        var rows = new List<CustomProposalRow>();
        await using (var con = _odbc.Create())
        {
            await con.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = con.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 60;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                rows.Add(new CustomProposalRow(
                    CustomPropID: r.IsDBNull(0) ? "" : r.GetString(0),
                    Name: r.IsDBNull(1) ? "(no title)" : r.GetString(1),
                    ClientID: r.IsDBNull(2) ? null : r.GetString(2),
                    ClientName: r.IsDBNull(3) ? "(unknown client)" : r.GetString(3),
                    Fee: r.IsDBNull(4) ? (decimal?)null : Convert.ToDecimal(r.GetValue(4)),
                    Status: r.IsDBNull(5) ? null : r.GetString(5),
                    Source: r.IsDBNull(6) ? null : r.GetString(6),
                    Type: r.IsDBNull(7) ? null : r.GetString(7),
                    DateAdvertised: r.IsDBNull(8) ? (DateTime?)null : r.GetDateTime(8),
                    DueDate: r.IsDBNull(9) ? (DateTime?)null : r.GetDateTime(9),
                    SubmittalDate: r.IsDBNull(10) ? (DateTime?)null : r.GetDateTime(10),
                    AwardDate: r.IsDBNull(11) ? (DateTime?)null : r.GetDateTime(11),
                    WBS1: r.IsDBNull(12) ? null : r.GetString(12),
                    ProposalManager: r.IsDBNull(13) ? null : r.GetString(13)));
            }
        }

        _logger.LogInformation("CustomProposal import: pulled {Count} rows from Deltek.", rows.Count);

        var imported = 0;
        var updated = 0;
        var skipped = 0;
        var errors = 0;

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(row.CustomPropID))
            {
                skipped++;
                continue;
            }

            try
            {
                long? buyerCanonId = null;
                if (!string.IsNullOrWhiteSpace(row.ClientID))
                {
                    var existing = await _canonical.GetCanonicalOrgByClendorIdAsync(row.ClientID, ct).ConfigureAwait(false);
                    buyerCanonId = existing?.Id ?? (await _canonical.UpsertCanonicalOrgAsync(
                        OrgKinds.KorClient,
                        row.ClientName,
                        row.ClientID,
                        null,
                        null,
                        ct).ConfigureAwait(false)).Id;
                }

                var beforeCount = await _pursuits.CountByExternalSourceAsync(PursuitExternalSources.DeltekCustomProposal, ct)
                    .ConfigureAwait(false);
                var create = new KorPursuitCreate(
                    Stage: DeriveStage(row),
                    OpportunityId: null,
                    HistoricalOpportunityId: null,
                    OpportunityAwardId: null,
                    SourceExternalRef: row.WBS1,
                    BuyerCanonicalOrgId: buyerCanonId,
                    BuyerName: row.ClientName,
                    Title: row.Name,
                    LostToCanonicalOrgId: null,
                    LostToName: null,
                    BidFee: row.Fee,
                    BidCurrency: "CAD",
                    EstConstructionValue: null,
                    OurRole: null,
                    KorBusinessDeveloper: null,
                    KorProposalManager: row.ProposalManager,
                    PursuitOpenedDate: row.DateAdvertised?.Date,
                    DueDate: row.DueDate?.Date,
                    SubmittedDate: row.SubmittalDate?.Date,
                    AwardDate: row.AwardDate?.Date,
                    ClosedReason: null,
                    Strengths: null,
                    Weaknesses: null,
                    Notes: row.Status is null ? null : $"Deltek Status: {row.Status}",
                    CreatedByUser: "Deltek.Import",
                    ExternalSource: PursuitExternalSources.DeltekCustomProposal,
                    ExternalSourceKey: row.CustomPropID);

                await _pursuits.UpsertByExternalKeyAsync(create, ct).ConfigureAwait(false);
                var afterCount = await _pursuits.CountByExternalSourceAsync(PursuitExternalSources.DeltekCustomProposal, ct)
                    .ConfigureAwait(false);
                if (afterCount > beforeCount) imported++;
                else updated++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to import CustomProposal {Id} ({Name}).", row.CustomPropID, row.Name);
                errors++;
            }
        }

        _logger.LogInformation("CustomProposal import done: imported={I} updated={U} skipped={S} errors={E}.",
            imported, updated, skipped, errors);
        return new ImportResult(rows.Count, imported, updated, skipped, errors);
    }

    private static string DeriveStage(CustomProposalRow row)
    {
        if (row.AwardDate.HasValue) return PursuitStages.Won;
        if (row.SubmittalDate.HasValue) return PursuitStages.Submitted;
        return PursuitStages.Pursuing;
    }

    private sealed record CustomProposalRow(
        string CustomPropID,
        string Name,
        string? ClientID,
        string ClientName,
        decimal? Fee,
        string? Status,
        string? Source,
        string? Type,
        DateTime? DateAdvertised,
        DateTime? DueDate,
        DateTime? SubmittalDate,
        DateTime? AwardDate,
        string? WBS1,
        string? ProposalManager);
}
