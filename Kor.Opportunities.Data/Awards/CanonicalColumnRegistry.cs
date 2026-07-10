#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Awards;

/// <summary>
/// One row of the canonical-column registry: a (table, name-column, FK-column)
/// triple in the <c>opportunities</c> schema where a raw ingested organisation
/// name is meant to resolve to a <c>CanonicalOrg</c>. <see cref="KindHint"/> is
/// the org kind to assume if/when a create pass is enabled (unused by the
/// read-only link pass). <see cref="HasRetiredAtUtc"/> flags tables that carry a
/// soft-retire column so scans can skip retired rows.
/// </summary>
public sealed record CanonicalColumnRef(
    string Schema,
    string Table,
    string NameColumn,
    string FkColumn,
    string KindHint,
    bool HasRetiredAtUtc,
    string KeyColumn = "Id")
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
        new("opportunities", "MajorProjectsInventory", "ArchitectName",           "ArchitectCanonicalOrgId",           OrgKinds.Architect,         true),
        new("opportunities", "MajorProjectsInventory", "StructuralEngineerName",   "StructuralEngineerCanonicalOrgId",  OrgKinds.Competitor,        true),
        new("opportunities", "MajorProjectsInventory", "GeneralContractorName",    "GeneralContractorCanonicalOrgId",   OrgKinds.GeneralContractor, true),
        new("opportunities", "MajorProjectsInventory", "ProponentName",            "ProponentCanonicalOrgId",           OrgKinds.Unknown,           true),

        // Tender / award / interest surfaces — buyer and firm names that a
        // provider miss (transient failure, denylist, name not yet in the graph)
        // leaves un-linked forever without a scheduled retry.
        new("opportunities", "Opportunities",            "BuyerName",             "BuyerCanonicalOrgId",     OrgKinds.Buyer,   false),
        new("opportunities", "OpportunityInterestedFirms","RawFirmName",          "ResolvedCanonicalOrgId",  OrgKinds.Unknown, false),
        new("opportunities", "OpportunityBids",           "BidderName",           "BidderCanonicalOrgId",    OrgKinds.Vendor,  false),
        new("opportunities", "OpportunityAwards",         "AwardingOrganization", "AwardingCanonicalOrgId",  OrgKinds.Buyer,   false),
        new("opportunities", "OpportunityAwards",         "AwardedToOrganization","AwardedToCanonicalOrgId", OrgKinds.Vendor,  false),

        // Building permits — three independent org roles per permit.
        new("opportunities", "BuildingPermit", "ApplicantName",  "ApplicantCanonicalOrgId",  OrgKinds.Unknown,           false),
        new("opportunities", "BuildingPermit", "ContractorName", "ContractorCanonicalOrgId", OrgKinds.GeneralContractor, false),
        new("opportunities", "BuildingPermit", "OwnerName",      "OwnerCanonicalOrgId",      OrgKinds.Unknown,           false),
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
