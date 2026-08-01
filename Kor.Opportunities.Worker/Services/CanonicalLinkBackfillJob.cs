#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// The canonical-link backfill "wheel" (rank 1 of the BD pipeline remediation).
///
/// Write-path-agnostic: no matter how a raw org name reached a <c>*Name</c>
/// column — provider ingest, a research import, a one-shot migration, or a
/// transient resolve failure — this scheduled job re-visits any row whose name
/// is set but whose canonical FK is still null and links it. It walks the
/// declarative <see cref="CanonicalColumnRegistry"/>, so coverage is declared in
/// exactly one place.
///
/// Safety guarantees, by construction:
///   1. Pass-1 never mints: <see cref="CanonicalOrgResolver.FindExistingAsync"/>
///      is a pure lookup. Pass-2 (create) is OFF by default and per-column
///      policy-gated via <see cref="CanonicalCreateMode"/>: Live entries mint
///      live orgs, Archived entries mint born-archived (keep-cold doctrine),
///      Never entries stay link-only. ResolveAsync carries the full guard stack
///      (denylist, concat guard, resurrect/keep-cold contract) so junk names
///      remain visible orphans instead of becoming organizations.
///   2. It never overwrites or nulls an existing link. Every write is a
///      fill-if-null guarded UPDATE that re-checks <c>FK IS NULL</c>, so a
///      concurrent writer's link is never clobbered.
///
/// One bad row never kills the batch; per-row failures are logged and skipped.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class CanonicalLinkBackfillJob : IJob
{
    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<CanonicalLinkBackfillJob> _logger;
    private readonly CanonicalOrgResolver _resolver;

    public CanonicalLinkBackfillJob(
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<CanonicalLinkBackfillJob> logger,
        CanonicalOrgResolver resolver)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.CanonicalLinkBackfillEnabled)
        {
            _logger.LogInformation("{Job}: disabled by configuration", nameof(CanonicalLinkBackfillJob));
            return;
        }

        var connStr = opt.OpportunitiesDb;
        if (string.IsNullOrWhiteSpace(connStr))
        {
            _logger.LogWarning("{Job}: OpportunitiesDb connection string not configured; skipping", nameof(CanonicalLinkBackfillJob));
            return;
        }

        var ct = context.CancellationToken;
        var perEntryCap = Math.Max(1, opt.CanonicalLinkBackfillBatchSize);
        var createEnabled = opt.CanonicalLinkBackfillCreateEnabled;

        int totalScanned = 0, totalLinked = 0, totalCreated = 0;
        var sections = new List<string>();

        foreach (var entry in CanonicalColumnRegistry.All)
        {
            ct.ThrowIfCancellationRequested();
            var (scanned, linked, created) = await BackfillEntryAsync(connStr, entry, perEntryCap, createEnabled, ct).ConfigureAwait(false);
            totalScanned += scanned;
            totalLinked += linked;
            totalCreated += created;
            if (scanned > 0)
            {
                sections.Add($"{entry.Label}: {linked}+{created}c/{scanned}");
            }
        }

        var summary = totalScanned == 0
            ? "no name-set / FK-null rows; nothing to do"
            : $"linked={totalLinked}; created={totalCreated}; scanned={totalScanned}; stillOrphan={totalScanned - totalLinked - totalCreated} :: " + string.Join(" | ", sections);
        _logger.LogInformation("{Job}: {Summary}", nameof(CanonicalLinkBackfillJob), summary);
        context.Result = summary;
    }

    /// <summary>
    /// Loads up to <paramref name="cap"/> orphan rows for one registry entry
    /// (name set, FK null, not retired) and links the ones the resolver can match
    /// to an existing canonical. Returns (scanned, linked).
    /// </summary>
    private async Task<(int scanned, int linked, int created)> BackfillEntryAsync(
        string connStr, CanonicalColumnRef entry, int pageSize, bool createEnabled, CancellationToken ct)
    {
        var retiredFilter = entry.HasRetiredAtUtc ? " AND [RetiredAtUtc] IS NULL" : string.Empty;
        // Keyset pagination by the primary key. A plain TOP(n) with no ORDER BY
        // re-chews the same leading unresolvable rows every run and never reaches
        // the tail of a large backlog; walking key-ascending guarantees every
        // orphan is attempted once per run and the cursor always moves forward.
        var selectSql =
            $"SELECT TOP (@page) [{entry.KeyColumn}], [{entry.NameColumn}] " +
            $"FROM {entry.QualifiedTable} " +
            $"WHERE [{entry.NameColumn}] IS NOT NULL AND LTRIM(RTRIM([{entry.NameColumn}])) <> '' " +
            $"AND [{entry.FkColumn}] IS NULL AND [{entry.KeyColumn}] > @lastKey{retiredFilter} " +
            $"ORDER BY [{entry.KeyColumn}];";

        // Fill-if-null guarded update. The re-checked `FK IS NULL` makes the write
        // idempotent and un-clobbering: if a concurrent writer linked the row
        // since our SELECT, this affects zero rows and we simply move on.
        var updateSql =
            $"UPDATE {entry.QualifiedTable} SET [{entry.FkColumn}] = @id " +
            $"WHERE [{entry.KeyColumn}] = @key AND [{entry.FkColumn}] IS NULL;";
        var source = "CanonicalLinkBackfill:" + entry.Label;

        long lastKey = long.MinValue;
        int scanned = 0, linked = 0, created = 0;
        var mayCreate = createEnabled && entry.CreateMode != CanonicalCreateMode.Never;
        // Defensive ceiling far above any real table (pageSize * this many pages);
        // a runaway loop can never spin here, but a healthy backlog is fully walked.
        const int maxPagesPerRun = 10_000;

        await using var con = new SqlConnection(connStr);
        await con.OpenAsync(ct).ConfigureAwait(false);

        for (var pageIdx = 0; pageIdx < maxPagesPerRun; pageIdx++)
        {
            ct.ThrowIfCancellationRequested();

            var page = new List<(long Id, string Name)>();
            await using (var cmd = new SqlCommand(selectSql, con) { CommandTimeout = 60 })
            {
                cmd.Parameters.Add("@page", SqlDbType.Int).Value = pageSize;
                cmd.Parameters.Add("@lastKey", SqlDbType.BigInt).Value = lastKey;
                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    // Key column may be int or bigint across tables — read it widened.
                    var id = Convert.ToInt64(reader.GetValue(0));
                    page.Add((id, reader.GetString(1)));
                    lastKey = id; // advance the cursor regardless of link outcome
                }
            }

            if (page.Count == 0)
            {
                break;
            }
            scanned += page.Count;

            foreach (var row in page)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // Team-name columns get the shared cleaner first — their orphan
                    // names arrived via research banking that predates the guard.
                    // Buyer-style names skip it (its paren/segment rules would
                    // mangle "Vancouver (City of)").
                    var name = entry.CleanAsTeamName ? TeamNameCleaner.Clean(row.Name) : row.Name;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    // PASS 1 — read-only resolve. Links to an existing canonical only;
                    // never creates one, never writes an OrgAlias row.
                    var canonicalId = await _resolver.FindExistingAsync(name, source, ct).ConfigureAwait(false);
                    var wasCreate = false;

                    // PASS 2 — flag-gated create, per the entry's declared policy.
                    // ResolveAsync carries the full guard stack (denylist, concat
                    // guard, resurrect/keep-cold contract): Live entries mint a live
                    // org; Archived entries mint born-archived (referenced-but-cold,
                    // per the firehose doctrine). Junk names return null and simply
                    // remain orphans — visible in the audit, never minted.
                    if (!canonicalId.HasValue && mayCreate)
                    {
                        canonicalId = await _resolver.ResolveAsync(
                            name,
                            entry.KindHint,
                            source,
                            ct,
                            allowCreate: true,
                            minConfidenceForCreate: 70,
                            createArchived: entry.CreateMode == CanonicalCreateMode.Archived).ConfigureAwait(false);
                        wasCreate = canonicalId.HasValue;
                    }

                    if (!canonicalId.HasValue)
                    {
                        continue;
                    }

                    await using var update = new SqlCommand(updateSql, con) { CommandTimeout = 30 };
                    update.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalId.Value;
                    update.Parameters.Add("@key", SqlDbType.BigInt).Value = row.Id;
                    if (await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0)
                    {
                        if (wasCreate) { created++; } else { linked++; }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "{Job}: {Label} id={Id} name='{Name}' resolve/link failed; continuing",
                        nameof(CanonicalLinkBackfillJob), entry.Label, row.Id, row.Name);
                }
            }

            // A short page means the orphan set for this entry is exhausted.
            if (page.Count < pageSize)
            {
                break;
            }
        }

        return (scanned, linked, created);
    }
}
