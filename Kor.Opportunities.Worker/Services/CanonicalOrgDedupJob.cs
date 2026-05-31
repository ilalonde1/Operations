#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// Round 46 — weekly canonical-org dedup. Mirrors the canonical-name pass of
/// <c>tools/BdCanonicalDedup</c> (the same NormalizeKey / KindRank / FkTargets
/// shape that has been audited 5+ times in BD-PM-AUDIT-20260530-R*). Catches
/// the ~26 name-equivalent dup groups the live ingest sources create per week
/// (R41 captured 94, R45 smoke run already showed 26 new ones). Only the safe
/// canonical-name pass runs here — the --pairs / honing-pass / DBA merge paths
/// stay manual via the CLI tool because they need human curation.
///
/// Safety:
/// - Feature-flag gated (<see cref="OpportunitiesWorkerOptions.CanonicalOrgDedupEnabled"/>).
/// - Hard cap on groups merged per run
///   (<see cref="OpportunitiesWorkerOptions.CanonicalOrgDedupMaxGroupsPerRun"/>);
///   anything past the cap aborts with an alarm log so an algorithm regression
///   can't silently collapse the org graph.
/// - Every merge is logged individually (loser id + name -> survivor id + name);
///   the JobRun audit trail shows exactly what got merged.
/// - Each group commits in its own transaction; one failure rolls back that
///   group only and the job continues.
/// </summary>
[DisallowConcurrentExecution]
public sealed class CanonicalOrgDedupJob : IJob
{
    private const string AliasSource = "DedupeMerge";

    // Same FK landscape as tools/BdCanonicalDedup/Program.cs:54-77. Keep in sync
    // when adding new canonical FK references (e.g. Round 37a added
    // CrmEngagements.BuyerCanonicalOrgId after Migration 48).
    private static readonly (string Table, string Column)[] FkTargets = new[]
    {
        ("BuildingPermit",                  "ApplicantCanonicalOrgId"),
        ("BuildingPermit",                  "ContractorCanonicalOrgId"),
        ("BuildingPermit",                  "OwnerCanonicalOrgId"),
        ("MajorProjectsInventory",          "ArchitectCanonicalOrgId"),
        ("MajorProjectsInventory",          "ProponentCanonicalOrgId"),
        ("MajorProjectsInventory",          "StructuralEngineerCanonicalOrgId"),
        ("MajorProjectsInventory",          "GeneralContractorCanonicalOrgId"),
        ("OpportunityAwards",               "AwardedToCanonicalOrgId"),
        ("OpportunityAwards",               "AwardingCanonicalOrgId"),
        ("Opportunities",                   "BuyerCanonicalOrgId"),
        ("KorPursuits",                     "BuyerCanonicalOrgId"),
        ("KorPursuits",                     "LostToCanonicalOrgId"),
        ("NewsArticleOrgMention",           "CanonicalOrgId"),
        ("OrgAlias",                        "CanonicalOrgId"),
        ("CanonicalOrgEnrichment",          "CanonicalOrgId"),
        ("CrmEngagements",                  "BuyerCanonicalOrgId"),
    };

    // Survivor selection: lower rank wins. Matches tools/BdCanonicalDedup/Program.cs:37-49.
    private static readonly Dictionary<string, int> KindRank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["KorClient"] = 0,
        ["KorStructural"] = 0,
        ["Competitor"] = 1,
        ["Developer"] = 2,
        ["Architect"] = 3,
        ["GC"] = 4,
        ["Subcontractor"] = 5,
        ["Buyer"] = 6,
        ["Vendor"] = 7,
        ["Unknown"] = 8,
    };

    private static readonly string[] StripTrailingTokens =
        { "inc", "incorporated", "ltd", "limited", "llp", "llc", "lp", "corp",
          "corporation", "co", "company", "architects", "architect", "architecture",
          "partnership", "partners", "group" };

    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<CanonicalOrgDedupJob> _logger;

    public CanonicalOrgDedupJob(
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<CanonicalOrgDedupJob> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.CanonicalOrgDedupEnabled)
        {
            _logger.LogDebug(
                "{Job} skipped: feature disabled via {Flag}.",
                nameof(CanonicalOrgDedupJob), nameof(opt.CanonicalOrgDedupEnabled));
            return;
        }

        var maxGroups = Math.Max(1, opt.CanonicalOrgDedupMaxGroupsPerRun);
        var ct = context.CancellationToken;
        var sw = Stopwatch.StartNew();

        await using var con = new SqlConnection(opt.OpportunitiesDb);
        await con.OpenAsync(ct).ConfigureAwait(false);

        var orgs = await LoadOrgsAsync(con, ct).ConfigureAwait(false);
        var groups = BuildGroups(orgs);
        if (groups.Count == 0)
        {
            _logger.LogInformation(
                "CanonicalOrgDedupJob: no duplicate groups detected. Orgs scanned={Orgs}. ElapsedMs={Elapsed}.",
                orgs.Count, sw.ElapsedMilliseconds);
            return;
        }

        if (groups.Count > maxGroups)
        {
            _logger.LogError(
                "CanonicalOrgDedupJob ABORT: detected {Groups} dup groups, above safety cap of {Cap}. " +
                "Possible algorithm regression — refusing to merge. Investigate via the CLI tool's --commit dry-run.",
                groups.Count, maxGroups);
            return;
        }

        var committed = 0;
        var failed = 0;
        var fkRepointsByTable = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var aliasesPreserved = 0;
        var rowsBefore = orgs.Count;

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await CommitGroupAsync(con, group, ct).ConfigureAwait(false);
                committed++;
                aliasesPreserved += result.AliasesPreserved;
                foreach (var (table, count) in result.FkRepointsByTable)
                {
                    fkRepointsByTable.TryGetValue(table, out var existing);
                    fkRepointsByTable[table] = existing + count;
                }
                foreach (var loser in group.Losers)
                {
                    _logger.LogInformation(
                        "CanonicalOrgDedupJob merged: loser id={LoserId} \"{LoserName}\" ({LoserKind}) -> survivor id={SurvivorId} \"{SurvivorName}\" ({SurvivorKind}).",
                        loser.Id, loser.DisplayName, loser.Kind,
                        group.Survivor.Id, group.Survivor.DisplayName, group.BestKind);
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex,
                    "CanonicalOrgDedupJob: group \"{GroupKey}\" failed and was rolled back; survivor id={SurvivorId}.",
                    group.GroupKey, group.Survivor.Id);
            }
        }

        var fkSummary = string.Join(", ", fkRepointsByTable.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
        _logger.LogInformation(
            "CanonicalOrgDedupJob complete. Groups={Groups}; committed={Committed}; failed={Failed}; " +
            "rowsBefore={RowsBefore}; aliasesPreserved={Aliases}; fkRepoints=[{FkSummary}]; elapsedMs={Elapsed}.",
            groups.Count, committed, failed, rowsBefore, aliasesPreserved, fkSummary, sw.ElapsedMilliseconds);
    }

    // ===== Load + group =====================================================

    private static async Task<List<OrgRow>> LoadOrgsAsync(SqlConnection con, CancellationToken ct)
    {
        var list = new List<OrgRow>();
        await using var cmd = con.CreateCommand();
        cmd.CommandTimeout = 120;
        cmd.CommandText = "SELECT Id, ISNULL(Kind, '') AS Kind, ISNULL(DisplayName, '') AS DisplayName FROM opportunities.CanonicalOrg;";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new OrgRow(r.GetInt64(0), r.GetString(1), r.GetString(2)));
        }
        return list;
    }

    private static List<DuplicateGroup> BuildGroups(IReadOnlyList<OrgRow> orgs)
    {
        var byKey = new Dictionary<string, List<OrgRow>>(StringComparer.Ordinal);
        foreach (var o in orgs)
        {
            var key = NormalizeKey(o.DisplayName);
            if (key.Length == 0) continue;
            if (!byKey.TryGetValue(key, out var bucket))
            {
                bucket = new List<OrgRow>();
                byKey[key] = bucket;
            }
            bucket.Add(o);
        }

        var groups = new List<DuplicateGroup>();
        foreach (var (key, members) in byKey)
        {
            if (members.Count < 2) continue;
            // Survivor = lowest kind rank; tiebreak by lowest Id (stable preference for the
            // earliest-created canonical, same as the CLI tool).
            var ordered = members
                .OrderBy(o => KindRank.TryGetValue(o.Kind, out var rank) ? rank : KindRank["Unknown"])
                .ThenBy(o => o.Id)
                .ToList();
            var survivor = ordered[0];
            var losers = ordered.Skip(1).ToList();
            groups.Add(new DuplicateGroup(key, survivor, survivor.Kind, losers));
        }
        return groups;
    }

    private static string NormalizeKey(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return string.Empty;
        var sb = new StringBuilder(displayName.Length);
        foreach (var ch in displayName.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        }
        var s = sb.ToString();
        // Strip the same trailing suffix tokens the CLI tool uses, in order. The
        // tokens are lowercase and already in $s by this point.
        bool changed;
        do
        {
            changed = false;
            foreach (var t in StripTrailingTokens)
            {
                if (s.Length > t.Length && s.EndsWith(t, StringComparison.Ordinal))
                {
                    s = s.Substring(0, s.Length - t.Length);
                    changed = true;
                    break;
                }
            }
        } while (changed);
        return s;
    }

    // ===== Commit one group =================================================

    private static async Task<GroupCommitResult> CommitGroupAsync(
        SqlConnection con, DuplicateGroup group, CancellationToken ct)
    {
        var result = new GroupCommitResult();
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var loser in group.Losers)
            {
                // Preserve the loser's display name as an alias on the survivor so
                // future ingestion of the loser's name resolves to the survivor.
                var aliasInserted = await ExecuteNonQueryAsync(con, tx, @"
IF NOT EXISTS (SELECT 1 FROM opportunities.OrgAlias
               WHERE CanonicalOrgId = @sid AND RawName = @raw)
INSERT INTO opportunities.OrgAlias (CanonicalOrgId, RawName, Source, Confidence)
VALUES (@sid, @raw, @src, 100);", ct, cmd =>
                {
                    cmd.Parameters.Add("@sid", SqlDbType.BigInt).Value = group.Survivor.Id;
                    cmd.Parameters.Add("@raw", SqlDbType.NVarChar, 400).Value = loser.DisplayName;
                    cmd.Parameters.Add("@src", SqlDbType.NVarChar, 50).Value = AliasSource;
                }).ConfigureAwait(false);
                if (aliasInserted > 0) result.AliasesPreserved++;

                // Repoint every FK reference from loser id to survivor id.
                foreach (var (table, column) in FkTargets)
                {
                    var sql = $"UPDATE opportunities.{table} SET {column} = @sid WHERE {column} = @lid;";
                    var n = await ExecuteNonQueryAsync(con, tx, sql, ct, cmd =>
                    {
                        cmd.Parameters.Add("@sid", SqlDbType.BigInt).Value = group.Survivor.Id;
                        cmd.Parameters.Add("@lid", SqlDbType.BigInt).Value = loser.Id;
                    }).ConfigureAwait(false);
                    if (n > 0)
                    {
                        result.FkRepointsByTable.TryGetValue(table, out var existing);
                        result.FkRepointsByTable[table] = existing + n;
                    }
                }

                // Delete the loser row.
                await ExecuteNonQueryAsync(con, tx,
                    "DELETE FROM opportunities.CanonicalOrg WHERE Id = @lid;", ct, cmd =>
                {
                    cmd.Parameters.Add("@lid", SqlDbType.BigInt).Value = loser.Id;
                }).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<int> ExecuteNonQueryAsync(
        SqlConnection con, SqlTransaction tx, string sql, CancellationToken ct,
        Action<SqlCommand>? bind = null)
    {
        await using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandTimeout = 120;
        cmd.CommandText = sql;
        bind?.Invoke(cmd);
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ===== Types ============================================================

    private sealed record OrgRow(long Id, string Kind, string DisplayName);

    private sealed record DuplicateGroup(string GroupKey, OrgRow Survivor, string BestKind, IReadOnlyList<OrgRow> Losers);

    private sealed class GroupCommitResult
    {
        public int AliasesPreserved { get; set; }
        public Dictionary<string, long> FkRepointsByTable { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
