#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Awards;

/// <summary>
/// How the backfill wheel's pass-2 may CREATE a canonical for an orphan name
/// this column holds, when pass-1's read-only link finds nothing to link to.
/// </summary>
public enum CanonicalCreateMode
{
    /// <summary>Never create — link-only. For columns whose orphan residue is
    /// dominated by resolve-failures/junk descriptors that ingest already tried.</summary>
    Never = 0,

    /// <summary>Create as a live org — real firms/buyers on live pursuits.</summary>
    Live = 1,

    /// <summary>Create born-archived (firehose keep-cold): commodity vendors we
    /// want referenced-but-cold, never warming the working set.</summary>
    Archived = 2,
}

/// <summary>
/// One row of the canonical-column registry: a (table, name-column, FK-column)
/// triple in the <c>opportunities</c> schema where a raw ingested organisation
/// name is meant to resolve to a <c>CanonicalOrg</c>. <see cref="KindHint"/> is
/// the org kind used when the create pass mints; <see cref="HasRetiredAtUtc"/>
/// flags tables that carry a soft-retire column so scans skip retired rows;
/// <see cref="CleanAsTeamName"/> routes project-team columns through the shared
/// <c>TeamNameCleaner</c> before any resolve (its paren/segment semantics are
/// wrong for buyer names like "Vancouver (City of)", so it is opt-in).
/// </summary>
public sealed record CanonicalColumnRef(
    string Schema,
    string Table,
    string NameColumn,
    string FkColumn,
    string KindHint,
    bool HasRetiredAtUtc,
    string KeyColumn = "Id",
    CanonicalCreateMode CreateMode = CanonicalCreateMode.Never,
    bool CleanAsTeamName = false)
{
    /// <summary>Bracket-quoted <c>[schema].[table]</c> for use in SQL.</summary>
    public string QualifiedTable => $"[{Schema}].[{Table}]";

    /// <summary>Short human label, e.g. <c>MajorProjectsInventory.ArchitectName</c>.</summary>
    public string Label => $"{Table}.{NameColumn}";
}

/// <summary>
/// The single declarative source of truth for every place a raw organisation
/// name is stored alongside a canonical-org foreign key. Three systems consume
/// this list so that adding a canonical column is one registry entry rather than
/// a hunt across providers, jobs and migrations:
///   • <c>CanonicalLinkBackfillJob</c> — links name-set / FK-null rows on a schedule.
///   • the data-health audit — measures the FK-null rate per entry.
///   • the dedup tooling — knows every inbound FK to repoint on a merge.
///
/// Every entry here is verified against the live schema at Worker startup via
/// <see cref="StartupVerifyAsync"/>; a registry that names a column the database
/// does not have fails the Worker loudly instead of silently stranding rows.
/// </summary>
public static class CanonicalColumnRegistry
{
    public static readonly IReadOnlyList<CanonicalColumnRef> All = new List<CanonicalColumnRef>
    {
        // Major Projects Inventory — the four project-team roles. SE/GC have no
        // other scheduled writer at all, so the wheel is their only link path.
        // Create LIVE: these are real firms on live pursuits (research-sourced).
        new("opportunities", "MajorProjectsInventory", "ArchitectName",           "ArchitectCanonicalOrgId",           OrgKinds.Architect,         true, CreateMode: CanonicalCreateMode.Live, CleanAsTeamName: true),
        new("opportunities", "MajorProjectsInventory", "StructuralEngineerName",   "StructuralEngineerCanonicalOrgId",  OrgKinds.Competitor,        true, CreateMode: CanonicalCreateMode.Live, CleanAsTeamName: true),
        new("opportunities", "MajorProjectsInventory", "GeneralContractorName",    "GeneralContractorCanonicalOrgId",   OrgKinds.GeneralContractor, true, CreateMode: CanonicalCreateMode.Live, CleanAsTeamName: true),
        new("opportunities", "MajorProjectsInventory", "ProponentName",            "ProponentCanonicalOrgId",           OrgKinds.Unknown,           true, CreateMode: CanonicalCreateMode.Live, CleanAsTeamName: true),

        // Tender / award / interest surfaces — buyer and firm names that a
        // provider miss (transient failure, denylist, name not yet in the graph)
        // leaves un-linked forever without a scheduled retry.
        // Buyers create LIVE (public buyers feed client KPIs); award/bid vendors
        // create BORN-ARCHIVED (keep-cold commodity class); interested-firms
        // NEVER create (residue = ingest resolve-failures/junk descriptors).
        new("opportunities", "Opportunities",            "BuyerName",             "BuyerCanonicalOrgId",     OrgKinds.Buyer,   false, CreateMode: CanonicalCreateMode.Live),
        new("opportunities", "OpportunityInterestedFirms","RawFirmName",          "ResolvedCanonicalOrgId",  OrgKinds.Unknown, false),
        new("opportunities", "OpportunityBids",           "BidderName",           "BidderCanonicalOrgId",    OrgKinds.Vendor,  false, CreateMode: CanonicalCreateMode.Archived),
        new("opportunities", "OpportunityAwards",         "AwardingOrganization", "AwardingCanonicalOrgId",  OrgKinds.Buyer,   false, CreateMode: CanonicalCreateMode.Live),
        new("opportunities", "OpportunityAwards",         "AwardedToOrganization","AwardedToCanonicalOrgId", OrgKinds.Vendor,  false, CreateMode: CanonicalCreateMode.Archived),

        // Building permits — three independent org roles per permit; permit
        // parties are commodity-class, keep-cold.
        new("opportunities", "BuildingPermit", "ApplicantName",  "ApplicantCanonicalOrgId",  OrgKinds.Unknown,           false, CreateMode: CanonicalCreateMode.Archived),
        new("opportunities", "BuildingPermit", "ContractorName", "ContractorCanonicalOrgId", OrgKinds.GeneralContractor, false, CreateMode: CanonicalCreateMode.Archived),
        new("opportunities", "BuildingPermit", "OwnerName",      "OwnerCanonicalOrgId",      OrgKinds.Unknown,           false, CreateMode: CanonicalCreateMode.Archived),

        // KOR pursuits — the Deltek sync's resolve misses left these null forever
        // with no repair path (audit-v2 #6); the wheel now heals them weekly.
        new("opportunities", "KorPursuits", "BuyerName",  "BuyerCanonicalOrgId",  OrgKinds.Buyer,      false, CreateMode: CanonicalCreateMode.Live),
        new("opportunities", "KorPursuits", "LostToName", "LostToCanonicalOrgId", OrgKinds.Competitor, false, CreateMode: CanonicalCreateMode.Live),

        // Historical archive — its org strings had NO canonical FK columns at all
        // until migration 280; the wheel links the ~9,884-row archive from here.
        // Buyers live (client-KPI joins); historic awardees keep-cold.
        new("opportunities", "HistoricalOpportunities", "BuyerName",             "BuyerCanonicalOrgId",     OrgKinds.Buyer,  false, CreateMode: CanonicalCreateMode.Live),
        new("opportunities", "HistoricalOpportunities", "AwardedToOrganization", "AwardedToCanonicalOrgId", OrgKinds.Vendor, false, CreateMode: CanonicalCreateMode.Archived),
    };

    /// <summary>
    /// Fail-closed schema check. Reads every column in the <c>opportunities</c>
    /// schema in one round trip and confirms each registry entry's key, name and
    /// FK columns (plus <c>RetiredAtUtc</c> where declared) actually exist.
    /// Throws <see cref="InvalidOperationException"/> listing any drift so the
    /// Worker crashes at startup rather than running a blind backfill.
    /// </summary>
    public static async Task StartupVerifyAsync(string connectionString, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "A connection string is required to verify the canonical-column registry.",
                nameof(connectionString));
        }

        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const string sql = @"
SELECT s.name AS SchemaName, t.name AS TableName, c.name AS ColumnName
FROM sys.columns c
JOIN sys.tables t ON t.object_id = c.object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name = 'opportunities';";

        await using (var con = new SqlConnection(connectionString))
        {
            await con.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                present.Add($"{reader.GetString(0)}.{reader.GetString(1)}.{reader.GetString(2)}");
            }
        }

        var missing = new List<string>();
        foreach (var entry in All)
        {
            void Require(string column)
            {
                var key = $"{entry.Schema}.{entry.Table}.{column}";
                if (!present.Contains(key))
                {
                    missing.Add(key);
                }
            }

            Require(entry.KeyColumn);
            Require(entry.NameColumn);
            Require(entry.FkColumn);
            if (entry.HasRetiredAtUtc)
            {
                Require("RetiredAtUtc");
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "CanonicalColumnRegistry is out of sync with the database schema. Missing column(s): " +
                string.Join(", ", missing) +
                ". Update the registry (or add the column) before starting the Worker.");
        }
    }
}
