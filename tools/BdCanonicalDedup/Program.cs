#nullable enable
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Kor.BdCanonicalDedup;

internal static class Program
{
    private const string AliasSource = "DedupeMerge";

    // Round 45 (R6-T3.001): default output dir used to be the cwd-relative
    // string "tools\BdCanonicalDedup\output". That produced a nested
    // tools/BdCanonicalDedup/tools/BdCanonicalDedup/output/dedupe-plan.csv
    // whenever the tool was launched from inside its own directory (R41 hit
    // this — almost reviewed the wrong plan file). Resolve the default by
    // walking up from the assembly location to the repo root (a directory
    // containing a `.git` folder), so the path is stable no matter where the
    // user runs the tool from. Falls back to the assembly directory if no
    // repo-root marker is found (e.g. when running a published single-file).
    private static readonly string DefaultOutputDirectory = ResolveDefaultOutputDirectory();

    private static string ResolveDefaultOutputDirectory()
    {
        var asmDir = Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? Directory.GetCurrentDirectory();
        var probe = new DirectoryInfo(asmDir);
        while (probe is not null)
        {
            if (Directory.Exists(Path.Combine(probe.FullName, ".git")))
            {
                return Path.Combine(probe.FullName, "tools", "BdCanonicalDedup", "output");
            }
            probe = probe.Parent;
        }
        return asmDir;
    }

    private static readonly Regex DbaRegex = new(@"^(.*)\s+dba[:\s]+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] StripTrailingTokens = new[]
    {
        "inc",
        "incorporated",
        "ltd",
        "limited",
        "llp",
        "llc",
        "lp",
        "corp",
        "corporation",
        "co",
        "company",
        "architects",
        "architect",
        "architecture",
        "partnership",
        "partners",
        "group",
    };

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

    // These identifiers are trusted hard-coded schema targets, not user/config
    // input. Repoint SQL interpolates table/column names only from this list;
    // row values still flow through parameters.
    private static readonly FkTarget[] FkTargets = new FkTarget[]
    {
        new("BuildingPermit", "ApplicantCanonicalOrgId"),
        new("BuildingPermit", "ContractorCanonicalOrgId"),
        new("BuildingPermit", "OwnerCanonicalOrgId"),
        new("MajorProjectsInventory", "ArchitectCanonicalOrgId"),
        new("MajorProjectsInventory", "ProponentCanonicalOrgId"),
        new("MajorProjectsInventory", "StructuralEngineerCanonicalOrgId"),
        new("MajorProjectsInventory", "GeneralContractorCanonicalOrgId"),
        new("OpportunityAwards", "AwardedToCanonicalOrgId"),
        new("OpportunityAwards", "AwardingCanonicalOrgId"),
        new("Opportunities", "BuyerCanonicalOrgId"),
        new("KorPursuits", "BuyerCanonicalOrgId"),
        new("KorPursuits", "LostToCanonicalOrgId"),
        new("NewsArticleOrgMention", "CanonicalOrgId"),
        new("OrgAlias", "CanonicalOrgId"),
        new("CanonicalOrgEnrichment", "CanonicalOrgId"),
        // Round 37a (BD-AUDIT-20260530-R2 T1.002): migration 48 added
        // CrmEngagements.BuyerCanonicalOrgId. Missing from this list meant the
        // next merge would fail FK validation or leave BD-tracking engagements
        // attached to the loser canonical id.
        new("CrmEngagements", "BuyerCanonicalOrgId"),
        // Round 60e (2026-06-02): migration 59 added ArchitectDisplacementBriefs
        // with FK ON DELETE CASCADE. Without this entry, every merge of an
        // architect canonical silently destroyed its displacement brief.
        // The table has UNIQUE(ArchitectCanonicalOrgId) so the collision
        // handler (DeleteDisplacementBriefCollisionsAsync) runs before repoint.
        new("ArchitectDisplacementBriefs", "ArchitectCanonicalOrgId"),
        // Round 61a (2026-06-03): migration 60 added OpportunityInterestedFirms
        // with FK to CanonicalOrg (NO CASCADE - resolver re-resolves on next
        // pass). Without this entry, merges of any canonical that has been
        // resolved as an interested firm fail with FK reference constraint
        // violation. The table's unique key is (OpportunityId, RawFirmName)
        // so no collision handler is needed - the same firm registering on
        // the same opp via two different canonicals is impossible by design
        // (resolver returns one canonical id per name).
        new("OpportunityInterestedFirms", "ResolvedCanonicalOrgId"),
    };

    private static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ImportOptions.Parse(args);
            if (string.IsNullOrWhiteSpace(options.OpportunitiesDb))
            {
                Console.Error.WriteLine("Missing connection string. Set KOR_OPPORTUNITIES_OPPORTUNITIESDB or pass --db.");
                return 2;
            }

            Directory.CreateDirectory(options.OutputDirectory);
            // Round 45 (R6-T3.001): always show the absolute output path up
            // front. Avoids "did I just inspect a stale CSV?" confusion when
            // the cwd / --out interaction is non-obvious.
            Console.WriteLine($"Output directory: {Path.GetFullPath(options.OutputDirectory)}");
            var planPath = Path.Combine(options.OutputDirectory, "dedupe-plan.csv");

            await using var con = new SqlConnection(options.OpportunitiesDb);
            await con.OpenAsync().ConfigureAwait(false);

            var schema = await VerifySchemaAsync(con).ConfigureAwait(false);
            var beforeCount = await CountCanonicalOrgsAsync(con).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(options.PairsFile))
            {
                return await RunPairsMergeAsync(con, options, schema, beforeCount).ConfigureAwait(false);
            }

            var orgs = await LoadOrgsAsync(con).ConfigureAwait(false);
            var groups = BuildGroups(orgs, options.MergeDba);
            var plans = BuildPlans(groups).ToList();

            WritePlanCsv(planPath, plans);
            Console.WriteLine($"Plan written: {planPath}");
            Console.WriteLine($"Mode: {(options.Commit ? "commit" : "dry-run")}; merge-dba={options.MergeDba.ToString().ToLowerInvariant()}");

            var summary = new MergeSummary
            {
                GroupsFound = groups.Count,
                RowsBefore = beforeCount,
                RowsToMerge = plans.Count,
                DbaGroups = groups.Count(g => g.HasDbaKey),
                DbaMergeRows = plans.Count(p => p.FromDbaKey),
            };

            if (!options.Commit)
            {
                foreach (var group in groups)
                {
                    foreach (var loser in group.Losers)
                    {
                        foreach (var (table, count) in loser.FkRefsByTable)
                        {
                            AddTableCount(summary.FkRepointsByTable, table, count);
                        }
                    }
                }
            }

            if (options.Commit && plans.Count > 0)
            {
                foreach (var group in groups)
                {
                    if (group.Losers.Count == 0)
                    {
                        continue;
                    }

                    try
                    {
                        var result = await CommitGroupAsync(con, group, schema.NewsMentionTypeKeyExists).ConfigureAwait(false);
                        summary.EnrichmentCollisionsResolved += result.EnrichmentCollisionsResolved;
                        summary.NewsMentionCollisionsResolved += result.NewsMentionCollisionsResolved;
                        summary.DisplacementBriefCollisionsResolved += result.DisplacementBriefCollisionsResolved;
                        summary.AliasesPreserved += result.AliasesPreserved;
                        summary.GroupsCommitted++;
                        foreach (var (table, count) in result.FkRepointsByTable)
                        {
                            summary.FkRepointsByTable.TryGetValue(table, out var existing);
                            summary.FkRepointsByTable[table] = existing + count;
                        }
                    }
                    catch (Exception ex)
                    {
                        summary.GroupsFailed++;
                        Console.Error.WriteLine($"[WARN] Group {group.GroupKey} failed and was rolled back: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            else if (!options.Commit)
            {
                Console.WriteLine("Dry-run only: no write transaction opened.");
            }

            summary.RowsAfter = options.Commit
                ? await CountCanonicalOrgsAsync(con).ConfigureAwait(false)
                : beforeCount - plans.Count;
            WriteSummary(summary, options.Commit);
            return summary.GroupsFailed == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"BdCanonicalDedup failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    // T1.001 similarity-gate helpers (post 2026-05-30 Abbotsford incident).
    // The honing pass's merge-pairs.csv emitted a wrong SurvivorId that this
    // tool committed unchallenged; now every --pairs row must clear a
    // fuzzy-name match (or be allowlisted) before commit.

    private static readonly HashSet<(long Loser, long Survivor)> _allowlistCache = LoadDedupAllowlist();

    private static bool IsAllowlistedNonSimilar(long loserId, long survivorId)
        => _allowlistCache.Contains((loserId, survivorId));

    private static HashSet<(long, long)> LoadDedupAllowlist()
    {
        var path = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? ".", "dedup-non-similar-allowlist.csv");
        var set = new HashSet<(long, long)>();
        if (!File.Exists(path)) return set;
        foreach (var line in File.ReadAllLines(path))
        {
            var parts = line.Split(',');
            if (parts.Length < 2) continue;
            if (long.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
                && long.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
            {
                set.Add((l, s));
            }
        }
        return set;
    }

    private static readonly object _rejectedLock = new();
    private static bool _rejectedHeaderWritten;

    private static void AppendRejectedPair(ImportOptions options, long loserId, long survivorId, string loserName, string survivorName, string loserFuzzy, string survivorFuzzy)
    {
        var path = Path.Combine(options.OutputDirectory, "rejected-pairs.csv");
        Directory.CreateDirectory(options.OutputDirectory);
        lock (_rejectedLock)
        {
            if (!_rejectedHeaderWritten)
            {
                File.WriteAllText(path, "LoserId,SurvivorId,LoserName,SurvivorName,LoserFuzzy,SurvivorFuzzy,RejectedAtUtc\r\n");
                _rejectedHeaderWritten = true;
            }
            var ts = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            File.AppendAllText(path, $"{loserId},{survivorId},\"{loserName.Replace("\"", "\"\"")}\",\"{survivorName.Replace("\"", "\"\"")}\",{loserFuzzy},{survivorFuzzy},{ts}\r\n");
        }
    }

    // Explicit-pair merge: loser -> survivor pairs chosen by the data-honing pass
    // (often different display names the fuzzy grouper can't match). Reuses the
    // tested per-group FK-repoint/commit logic; survivor is fixed (not chosen).
    private static async Task<int> RunPairsMergeAsync(SqlConnection con, ImportOptions options, SchemaInfo schema, int beforeCount)
    {
        var lines = await File.ReadAllLinesAsync(options.PairsFile!).ConfigureAwait(false);
        var orgs = await LoadOrgsAsync(con).ConfigureAwait(false);
        var byId = orgs.ToDictionary(o => o.Id);
        var summary = new MergeSummary { RowsBefore = beforeCount };

        foreach (var line in lines)
        {
            var parts = line.Split(',');
            if (parts.Length < 2
                || !long.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var loserId)
                || !long.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var survivorId))
            {
                continue; // header or blank
            }

            if (loserId == survivorId)
            {
                continue;
            }

            if (!byId.TryGetValue(loserId, out var loser) || !byId.TryGetValue(survivorId, out var survivor))
            {
                Console.Error.WriteLine($"[WARN] pair {loserId}->{survivorId}: org row not found; skipped.");
                summary.GroupsFailed++;
                continue;
            }

            // Similarity gate (audit finding T1.001 / 2026-05-30): the 2026-05-30
            // Abbotsford-SD -> Alterra-Power-Corp incident shipped because this
            // path trusted the honing pass's merge-pairs.csv SurvivorId without
            // any name-similarity check. Now we require the loser and survivor
            // to resolve to the same fuzzy-normalized name (which collapses
            // suffix / SD-number / "City of X" / "&" vs "and" variants). Pairs
            // that fail the gate are written to rejected-pairs.csv for human
            // review and skipped.
            var loserFuzzy = Kor.Opportunities.Data.Awards.CanonicalOrgResolver.NormalizeForFuzzyMatch(loser.DisplayName);
            var survivorFuzzy = Kor.Opportunities.Data.Awards.CanonicalOrgResolver.NormalizeForFuzzyMatch(survivor.DisplayName);
            if (!string.Equals(loserFuzzy, survivorFuzzy, StringComparison.Ordinal) && !IsAllowlistedNonSimilar(loserId, survivorId))
            {
                Console.Error.WriteLine($"[REJECT] pair {loserId} ({loser.DisplayName}) -> {survivorId} ({survivor.DisplayName}): names not similar (fuzzy '{loserFuzzy}' vs '{survivorFuzzy}'); written to rejected-pairs.csv.");
                AppendRejectedPair(options, loserId, survivorId, loser.DisplayName, survivor.DisplayName, loserFuzzy, survivorFuzzy);
                summary.GroupsFailed++;
                continue;
            }

            var members = new List<OrgRow> { survivor, loser };
            var bestKind = members.OrderBy(o => RankKind(o.Kind)).ThenBy(o => o.Id).First().Kind;
            var group = new DuplicateGroup(
                GroupKey: $"pair:{loserId}->{survivorId}",
                HasDbaKey: false,
                Survivor: survivor,
                BestKind: bestKind,
                Losers: new List<OrgRow> { loser },
                Members: members);

            summary.GroupsFound++;
            summary.RowsToMerge++;

            if (!options.Commit)
            {
                Console.WriteLine($"[DRY-RUN] merge {loserId} ({loser.DisplayName}) -> {survivorId} ({survivor.DisplayName}); kind={bestKind}");
                continue;
            }

            try
            {
                // Round 45 (R6-T3.002): capture the commit result so its
                // counters and per-table FK repoint counts roll up into the
                // summary. The canonical-name merge path already does this
                // (above, lines 145-154); the pair-merge path used to discard
                // the result, so the final "FK repoints by table" report was a
                // misleading zero even when pairs really did touch graph
                // edges (R42 hit this). Mirroring all four fields the
                // canonical path aggregates keeps the two report shapes equal.
                var result = await CommitGroupAsync(con, group, schema.NewsMentionTypeKeyExists).ConfigureAwait(false);
                summary.EnrichmentCollisionsResolved += result.EnrichmentCollisionsResolved;
                summary.NewsMentionCollisionsResolved += result.NewsMentionCollisionsResolved;
                summary.DisplacementBriefCollisionsResolved += result.DisplacementBriefCollisionsResolved;
                summary.AliasesPreserved += result.AliasesPreserved;
                summary.GroupsCommitted++;
                foreach (var (table, count) in result.FkRepointsByTable)
                {
                    summary.FkRepointsByTable.TryGetValue(table, out var existing);
                    summary.FkRepointsByTable[table] = existing + count;
                }
            }
            catch (Exception ex)
            {
                summary.GroupsFailed++;
                Console.Error.WriteLine($"[WARN] pair {loserId}->{survivorId} failed and was rolled back: {ex.GetType().Name}: {ex.Message}");
            }
        }

        summary.RowsAfter = options.Commit
            ? await CountCanonicalOrgsAsync(con).ConfigureAwait(false)
            : beforeCount - summary.RowsToMerge;
        WriteSummary(summary, options.Commit);
        return summary.GroupsFailed == 0 ? 0 : 1;
    }

    private static async Task<SchemaInfo> VerifySchemaAsync(SqlConnection con)
    {
        var required = new List<(string Table, string Column)>
        {
            ("CanonicalOrg", "Id"),
            ("CanonicalOrg", "Kind"),
            ("CanonicalOrg", "DisplayName"),
            ("CanonicalOrg", "ClendorClientId"),
            ("CanonicalOrg", "Website"),
            ("CanonicalOrg", "Notes"),
            ("CanonicalOrg", "UpdatedAtUtc"),
            ("OrgAlias", "RawName"),
            ("OrgAlias", "Source"),
            ("OrgAlias", "Confidence"),
            ("OrgAlias", "ClassifiedBy"),
            ("OrgAlias", "ClassifiedAtUtc"),
            ("OrgAlias", "Notes"),
            ("CanonicalOrgEnrichment", "ProviderName"),
            ("NewsArticleOrgMention", "NewsArticleId"),
        };
        required.AddRange(FkTargets.Select(t => (t.Table, t.Column)));

        const string sql = @"
SELECT COUNT(*)
FROM sys.schemas s
JOIN sys.tables t ON t.schema_id = s.schema_id
JOIN sys.columns c ON c.object_id = t.object_id
WHERE s.name = N'opportunities'
  AND t.name = @table
  AND c.name = @column;";

        var missing = new List<string>();
        foreach (var (table, column) in required.Distinct())
        {
            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.Add("@table", SqlDbType.NVarChar, 128).Value = table;
            cmd.Parameters.Add("@column", SqlDbType.NVarChar, 128).Value = column;
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (count == 0)
            {
                missing.Add($"opportunities.{table}.{column}");
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException("Missing expected schema column(s): " + string.Join(", ", missing));
        }

        var newsMentionTypeKeyExists = await ColumnExistsAsync(con, "NewsArticleOrgMention", "MentionTypeKey").ConfigureAwait(false);
        return new SchemaInfo(newsMentionTypeKeyExists);
    }

    private static async Task<bool> ColumnExistsAsync(SqlConnection con, string table, string column)
    {
        const string sql = @"
SELECT COUNT(*)
FROM sys.schemas s
JOIN sys.tables t ON t.schema_id = s.schema_id
JOIN sys.columns c ON c.object_id = t.object_id
WHERE s.name = N'opportunities'
  AND t.name = @table
  AND c.name = @column;";
        await using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.Add("@table", SqlDbType.NVarChar, 128).Value = table;
        cmd.Parameters.Add("@column", SqlDbType.NVarChar, 128).Value = column;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture) > 0;
    }

    private static async Task<int> CountCanonicalOrgsAsync(SqlConnection con)
    {
        await using var cmd = new SqlCommand("SELECT COUNT(*) FROM opportunities.CanonicalOrg;", con);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static async Task<List<OrgRow>> LoadOrgsAsync(SqlConnection con)
    {
        var sql = @"
SELECT co.Id,
       co.Kind,
       co.DisplayName,
       co.ClendorClientId,
       co.Website,
       co.Notes" + BuildRefCountSelectSql("co.Id") + @"
FROM opportunities.CanonicalOrg co
ORDER BY co.Id;";

        var orgs = new List<OrgRow>();
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 120 };
        await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await r.ReadAsync().ConfigureAwait(false))
        {
            orgs.Add(new OrgRow(
                Id: r.GetInt64(0),
                Kind: r.GetString(1),
                DisplayName: r.GetString(2),
                ClendorClientId: r.IsDBNull(3) ? null : r.GetString(3),
                Website: r.IsDBNull(4) ? null : r.GetString(4),
                Notes: r.IsDBNull(5) ? null : r.GetString(5),
                FkRefsByTable: ReadRefCounts(r, startOrdinal: 6)));
        }

        return orgs;
    }

    private static string BuildRefCountSelectSql(string idSql)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < FkTargets.Length; i++)
        {
            var target = FkTargets[i];
            sb.AppendLine(",");
            sb.Append($"       (SELECT COUNT_BIG(*) FROM opportunities.{target.Table} WHERE {target.Column} = {idSql}) AS Ref{i}");
        }

        return sb.ToString();
    }

    private static Dictionary<string, long> ReadRefCounts(SqlDataReader reader, int startOrdinal)
    {
        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < FkTargets.Length; i++)
        {
            var count = Convert.ToInt64(reader.GetValue(startOrdinal + i), CultureInfo.InvariantCulture);
            AddTableCount(counts, FkTargets[i].Table, count);
        }

        return counts;
    }

    private static List<DuplicateGroup> BuildGroups(IReadOnlyList<OrgRow> orgs, bool mergeDba)
    {
        var dsu = new DisjointSet(orgs.Count);
        var firstByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var keysByIndex = new List<List<GroupKey>>(orgs.Count);

        for (var i = 0; i < orgs.Count; i++)
        {
            var keys = BuildKeys(orgs[i].DisplayName, mergeDba);
            keysByIndex.Add(keys);
            foreach (var key in keys)
            {
                if (firstByKey.TryGetValue(key.Value, out var first))
                {
                    dsu.Union(first, i);
                }
                else
                {
                    firstByKey[key.Value] = i;
                }
            }
        }

        var buckets = new Dictionary<int, List<int>>();
        for (var i = 0; i < orgs.Count; i++)
        {
            if (keysByIndex[i].Count == 0)
            {
                continue;
            }

            var root = dsu.Find(i);
            if (!buckets.TryGetValue(root, out var list))
            {
                list = new List<int>();
                buckets[root] = list;
            }

            list.Add(i);
        }

        var groups = new List<DuplicateGroup>();
        foreach (var indexes in buckets.Values)
        {
            if (indexes.Count < 2)
            {
                continue;
            }

            var members = indexes.Select(i => orgs[i]).ToList();
            var allKeys = indexes
                .SelectMany(i => keysByIndex[i])
                .GroupBy(k => k.Value, StringComparer.Ordinal)
                .Select(g => new GroupKey(g.Key, g.Any(k => k.IsDba)))
                .OrderBy(k => k.Value, StringComparer.Ordinal)
                .ToList();

            var survivor = ChooseSurvivor(members);
            var bestKind = members
                .OrderBy(o => RankKind(o.Kind))
                .ThenBy(o => o.Id)
                .First()
                .Kind;
            var losers = members
                .Where(o => o.Id != survivor.Id)
                .OrderBy(o => o.Id)
                .ToList();
            var groupKey = string.Join("|", allKeys.Select(k => k.IsDba ? "dba:" + k.Value : k.Value));
            groups.Add(new DuplicateGroup(
                GroupKey: groupKey,
                HasDbaKey: allKeys.Any(k => k.IsDba),
                Survivor: survivor,
                BestKind: bestKind,
                Losers: losers,
                Members: members));
        }

        return groups
            .OrderBy(g => g.GroupKey, StringComparer.Ordinal)
            .ThenBy(g => g.Survivor.Id)
            .ToList();
    }

    private static IEnumerable<MergePlanRow> BuildPlans(IEnumerable<DuplicateGroup> groups)
    {
        foreach (var group in groups)
        {
            foreach (var loser in group.Losers)
            {
                yield return new MergePlanRow(
                    GroupKey: group.GroupKey,
                    FromDbaKey: group.HasDbaKey,
                    SurvivorId: group.Survivor.Id,
                    SurvivorName: group.Survivor.DisplayName,
                    SurvivorKind: group.BestKind,
                    LoserId: loser.Id,
                    LoserName: loser.DisplayName,
                    LoserKind: loser.Kind,
                    FkRefsRepointed: loser.FkRefCount);
            }
        }
    }

    private static List<GroupKey> BuildKeys(string displayName, bool mergeDba)
    {
        var keys = new List<GroupKey>();
        var primary = NormalizeKey(displayName);
        if (primary.Length > 0)
        {
            keys.Add(new GroupKey(primary, false));
        }

        if (mergeDba)
        {
            var match = DbaRegex.Match(displayName);
            if (match.Success)
            {
                var dba = NormalizeKey(match.Groups[2].Value);
                if (dba.Length > 0 && !keys.Any(k => k.Value == dba))
                {
                    keys.Add(new GroupKey(dba, true));
                }
            }
        }

        return keys;
    }

    private static string NormalizeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
        }

        var normalized = sb.ToString();
        bool stripped;
        do
        {
            stripped = false;
            foreach (var token in StripTrailingTokens)
            {
                if (normalized.Length >= token.Length && normalized.EndsWith(token, StringComparison.Ordinal))
                {
                    // Guard short, ambiguous tokens (e.g. "co", "lp") that are also common
                    // word-endings: only strip if the remaining base stays >= 4 chars.
                    // Prevents "SISCO" -> "sis" colliding with an unrelated "SIS", while still
                    // collapsing "Gregg Distributors Co Ltd" -> "Gregg Distributors".
                    if (token.Length <= 2 && normalized.Length - token.Length < 4)
                    {
                        continue;
                    }

                    normalized = normalized[..^token.Length];
                    stripped = true;
                    break;
                }
            }
        }
        while (stripped);

        return normalized;
    }

    private static OrgRow ChooseSurvivor(IReadOnlyList<OrgRow> members)
        => members
            .OrderByDescending(o => !string.IsNullOrWhiteSpace(o.ClendorClientId))
            .ThenBy(o => RankKind(o.Kind))
            .ThenByDescending(o => o.FkRefCount)
            .ThenBy(o => o.Id)
            .First();

    private static int RankKind(string kind)
        => KindRank.TryGetValue(kind, out var rank) ? rank : 8;

    private static async Task<GroupCommitResult> CommitGroupAsync(SqlConnection con, DuplicateGroup group, bool newsMentionTypeKeyExists)
    {
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync().ConfigureAwait(false);
        try
        {
            await ExecuteNonQueryAsync(con, tx, "SET XACT_ABORT ON;").ConfigureAwait(false);
            await CreateLosersTempTableAsync(con, tx, group.Losers).ConfigureAwait(false);
            await UpdateSurvivorAsync(con, tx, group.Survivor.Id, group.BestKind).ConfigureAwait(false);

            var result = new GroupCommitResult();
            result.EnrichmentCollisionsResolved = await DeleteEnrichmentCollisionsAsync(con, tx, group.Survivor.Id).ConfigureAwait(false);
            result.NewsMentionCollisionsResolved = await DeleteNewsMentionCollisionsAsync(con, tx, group.Survivor.Id, newsMentionTypeKeyExists).ConfigureAwait(false);
            result.DisplacementBriefCollisionsResolved = await DeleteDisplacementBriefCollisionsAsync(con, tx, group.Survivor.Id).ConfigureAwait(false);

            foreach (var target in FkTargets)
            {
                if (target.Table == "CanonicalOrgEnrichment")
                {
                    var updated = await RepointAsync(con, tx, target, group.Survivor.Id).ConfigureAwait(false);
                    AddTableCount(result.FkRepointsByTable, target.Table, updated);
                    continue;
                }

                var rows = await RepointAsync(con, tx, target, group.Survivor.Id).ConfigureAwait(false);
                AddTableCount(result.FkRepointsByTable, target.Table, rows);
            }

            result.AliasesPreserved = await PreserveLoserAliasesAsync(con, tx, group.Survivor.Id).ConfigureAwait(false);
            result.LosersDeleted = await DeleteLosersAsync(con, tx).ConfigureAwait(false);
            await tx.CommitAsync().ConfigureAwait(false);
            Console.WriteLine($"[COMMIT] {group.GroupKey}: survivor={group.Survivor.Id}; losers={result.LosersDeleted}; enrichmentCollisions={result.EnrichmentCollisionsResolved}");
            return result;
        }
        catch
        {
            await tx.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task CreateLosersTempTableAsync(SqlConnection con, SqlTransaction tx, IReadOnlyList<OrgRow> losers)
    {
        await ExecuteNonQueryAsync(con, tx, "IF OBJECT_ID('tempdb..#Losers') IS NOT NULL DROP TABLE #Losers; CREATE TABLE #Losers (Id bigint NOT NULL PRIMARY KEY, DisplayName nvarchar(300) NOT NULL);").ConfigureAwait(false);
        for (var offset = 0; offset < losers.Count; offset += 200)
        {
            var batch = losers.Skip(offset).Take(200).ToList();
            var values = new List<string>(batch.Count);
            await using var cmd = new SqlCommand { Connection = con, Transaction = tx };
            for (var i = 0; i < batch.Count; i++)
            {
                values.Add($"(@id{i}, @name{i})");
                cmd.Parameters.Add($"@id{i}", SqlDbType.BigInt).Value = batch[i].Id;
                cmd.Parameters.Add($"@name{i}", SqlDbType.NVarChar, 300).Value = batch[i].DisplayName;
            }

            cmd.CommandText = "INSERT INTO #Losers (Id, DisplayName) VALUES " + string.Join(", ", values) + ";";
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private static async Task UpdateSurvivorAsync(SqlConnection con, SqlTransaction tx, long survivorId, string bestKind)
    {
        const string sql = @"
UPDATE s
SET Kind = @bestKind,
    ClendorClientId = COALESCE(s.ClendorClientId, loserClendor.Value),
    Website = COALESCE(s.Website, loserWebsite.Value),
    Notes = COALESCE(s.Notes, loserNotes.Value),
    UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.CanonicalOrg s
OUTER APPLY (
    SELECT TOP 1 co.ClendorClientId AS Value
    FROM opportunities.CanonicalOrg co
    JOIN #Losers l ON l.Id = co.Id
    WHERE co.ClendorClientId IS NOT NULL
    ORDER BY co.Id
) loserClendor
OUTER APPLY (
    SELECT TOP 1 co.Website AS Value
    FROM opportunities.CanonicalOrg co
    JOIN #Losers l ON l.Id = co.Id
    WHERE co.Website IS NOT NULL
    ORDER BY co.Id
) loserWebsite
OUTER APPLY (
    SELECT TOP 1 co.Notes AS Value
    FROM opportunities.CanonicalOrg co
    JOIN #Losers l ON l.Id = co.Id
    WHERE co.Notes IS NOT NULL
    ORDER BY co.Id
) loserNotes
WHERE s.Id = @survivor;";
        await using var cmd = new SqlCommand(sql, con, tx);
        cmd.Parameters.Add("@survivor", SqlDbType.BigInt).Value = survivorId;
        cmd.Parameters.Add("@bestKind", SqlDbType.NVarChar, 40).Value = bestKind;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<int> DeleteEnrichmentCollisionsAsync(SqlConnection con, SqlTransaction tx, long survivorId)
    {
        const string sql = @"
DELETE loser
FROM opportunities.CanonicalOrgEnrichment loser
JOIN #Losers l ON l.Id = loser.CanonicalOrgId
WHERE EXISTS (
    SELECT 1
    FROM opportunities.CanonicalOrgEnrichment other
    JOIN #Losers otherLoser ON otherLoser.Id = other.CanonicalOrgId
    WHERE other.ProviderName = loser.ProviderName
      AND other.Id < loser.Id
);

DELETE loser
FROM opportunities.CanonicalOrgEnrichment loser
JOIN #Losers l ON l.Id = loser.CanonicalOrgId
WHERE EXISTS (
    SELECT 1
    FROM opportunities.CanonicalOrgEnrichment survivor
    WHERE survivor.CanonicalOrgId = @survivor
      AND survivor.ProviderName = loser.ProviderName
);";
        await using var cmd = new SqlCommand(sql, con, tx);
        cmd.Parameters.Add("@survivor", SqlDbType.BigInt).Value = survivorId;
        return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    // ArchitectDisplacementBriefs has UNIQUE(ArchitectCanonicalOrgId): one row per
    // architect. Repointing a loser's brief to a survivor that already has one
    // would violate the unique key. Resolve by deleting loser rows when:
    //   (a) two losers in the group both have briefs - keep the one with the
    //       lowest Id (deterministic, matches the enrichment-collision pattern); OR
    //   (b) survivor already has a brief - drop the loser's brief.
    // The dropped brief is reproducible: re-running BdResearchImport with
    // --only displacement-briefs upserts the JSON payload back onto the survivor.
    private static async Task<int> DeleteDisplacementBriefCollisionsAsync(SqlConnection con, SqlTransaction tx, long survivorId)
    {
        const string sql = @"
DELETE loser
FROM opportunities.ArchitectDisplacementBriefs loser
JOIN #Losers l ON l.Id = loser.ArchitectCanonicalOrgId
WHERE EXISTS (
    SELECT 1
    FROM opportunities.ArchitectDisplacementBriefs other
    JOIN #Losers otherLoser ON otherLoser.Id = other.ArchitectCanonicalOrgId
    WHERE other.Id < loser.Id
);

DELETE loser
FROM opportunities.ArchitectDisplacementBriefs loser
JOIN #Losers l ON l.Id = loser.ArchitectCanonicalOrgId
WHERE EXISTS (
    SELECT 1
    FROM opportunities.ArchitectDisplacementBriefs survivor
    WHERE survivor.ArchitectCanonicalOrgId = @survivor
);";
        await using var cmd = new SqlCommand(sql, con, tx);
        cmd.Parameters.Add("@survivor", SqlDbType.BigInt).Value = survivorId;
        return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<int> DeleteNewsMentionCollisionsAsync(SqlConnection con, SqlTransaction tx, long survivorId, bool mentionTypeKeyExists)
    {
        var sql = mentionTypeKeyExists
            ? @"
DELETE loser
FROM opportunities.NewsArticleOrgMention loser
JOIN #Losers l ON l.Id = loser.CanonicalOrgId
WHERE EXISTS (
    SELECT 1
    FROM opportunities.NewsArticleOrgMention other
    JOIN #Losers otherLoser ON otherLoser.Id = other.CanonicalOrgId
    WHERE other.NewsArticleId = loser.NewsArticleId
      AND other.MentionTypeKey = loser.MentionTypeKey
      AND other.Id < loser.Id
);

DELETE loser
FROM opportunities.NewsArticleOrgMention loser
JOIN #Losers l ON l.Id = loser.CanonicalOrgId
WHERE EXISTS (
    SELECT 1
    FROM opportunities.NewsArticleOrgMention survivor
    WHERE survivor.CanonicalOrgId = @survivor
      AND survivor.NewsArticleId = loser.NewsArticleId
      AND survivor.MentionTypeKey = loser.MentionTypeKey
);"
            : @"
DELETE loser
FROM opportunities.NewsArticleOrgMention loser
JOIN #Losers l ON l.Id = loser.CanonicalOrgId
WHERE EXISTS (
    SELECT 1
    FROM opportunities.NewsArticleOrgMention other
    JOIN #Losers otherLoser ON otherLoser.Id = other.CanonicalOrgId
    WHERE other.NewsArticleId = loser.NewsArticleId
      AND other.Id < loser.Id
);

DELETE loser
FROM opportunities.NewsArticleOrgMention loser
JOIN #Losers l ON l.Id = loser.CanonicalOrgId
WHERE EXISTS (
    SELECT 1
    FROM opportunities.NewsArticleOrgMention survivor
    WHERE survivor.CanonicalOrgId = @survivor
      AND survivor.NewsArticleId = loser.NewsArticleId
);";

        await using var cmd = new SqlCommand(sql, con, tx);
        cmd.Parameters.Add("@survivor", SqlDbType.BigInt).Value = survivorId;
        return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<int> RepointAsync(SqlConnection con, SqlTransaction tx, FkTarget target, long survivorId)
    {
        var sql = $@"
UPDATE t
SET {target.Column} = @survivor
FROM opportunities.{target.Table} t
JOIN #Losers l ON l.Id = t.{target.Column};";
        await using var cmd = new SqlCommand(sql, con, tx);
        cmd.Parameters.Add("@survivor", SqlDbType.BigInt).Value = survivorId;
        return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<int> PreserveLoserAliasesAsync(SqlConnection con, SqlTransaction tx, long survivorId)
    {
        const string sql = @"
INSERT INTO opportunities.OrgAlias
    (RawName, Source, CanonicalOrgId, Confidence, ClassifiedBy, ClassifiedAtUtc, Notes)
SELECT l.DisplayName,
       @source,
       @survivor,
       100,
       @classifiedBy,
       sysdatetimeoffset(),
       N'Preserved loser DisplayName during CanonicalOrg dedupe merge.'
-- DISTINCT: a group can contain two losers with an IDENTICAL DisplayName
-- (e.g. 'Alex  Liu DBA: ...' double-space variants). Without it the single
-- INSERT emits duplicate (RawName, Source) rows and violates
-- UX_OrgAlias_RawName_Source, rolling back the whole group.
FROM (SELECT DISTINCT DisplayName FROM #Losers) l
WHERE NOT EXISTS (
    SELECT 1
    FROM opportunities.OrgAlias a
    WHERE a.RawName = l.DisplayName
      AND a.Source = @source
);";
        await using var cmd = new SqlCommand(sql, con, tx);
        cmd.Parameters.Add("@survivor", SqlDbType.BigInt).Value = survivorId;
        cmd.Parameters.Add("@source", SqlDbType.NVarChar, 80).Value = AliasSource;
        cmd.Parameters.Add("@classifiedBy", SqlDbType.NVarChar, 50).Value = "BdCanonicalDedup";
        return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<int> DeleteLosersAsync(SqlConnection con, SqlTransaction tx)
    {
        const string sql = @"
DELETE co
FROM opportunities.CanonicalOrg co
JOIN #Losers l ON l.Id = co.Id;";
        return await ExecuteNonQueryAsync(con, tx, sql).ConfigureAwait(false);
    }

    private static async Task<int> ExecuteNonQueryAsync(SqlConnection con, SqlTransaction tx, string sql)
    {
        await using var cmd = new SqlCommand(sql, con, tx);
        return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static void AddTableCount(Dictionary<string, long> counts, string table, long rows)
    {
        counts.TryGetValue(table, out var existing);
        counts[table] = existing + rows;
    }

    private static void WritePlanCsv(string path, IReadOnlyList<MergePlanRow> plans)
    {
        var lines = new List<string>(plans.Count + 1)
        {
            "GroupKey,SurvivorId,SurvivorName,SurvivorKind,LoserId,LoserName,LoserKind,FkRefsRepointed",
        };
        foreach (var plan in plans)
        {
            lines.Add(string.Join(",",
                Csv(plan.GroupKey),
                plan.SurvivorId.ToString(CultureInfo.InvariantCulture),
                Csv(plan.SurvivorName),
                Csv(plan.SurvivorKind),
                plan.LoserId.ToString(CultureInfo.InvariantCulture),
                Csv(plan.LoserName),
                Csv(plan.LoserKind),
                plan.FkRefsRepointed.ToString(CultureInfo.InvariantCulture)));
        }

        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static string Csv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static void WriteSummary(MergeSummary summary, bool committed)
    {
        Console.WriteLine("Canonical dedupe complete.");
        Console.WriteLine($"  Groups found:                  {summary.GroupsFound}");
        Console.WriteLine($"  Rows to merge:                 {summary.RowsToMerge}");
        Console.WriteLine($"  Rows before:                   {summary.RowsBefore}");
        Console.WriteLine(committed
            ? $"  Rows after:                    {summary.RowsAfter}"
            : $"  Projected rows after (if all groups commit): {summary.RowsAfter}");
        Console.WriteLine($"  Groups committed successfully: {summary.GroupsCommitted}");
        Console.WriteLine($"  Groups failed:                 {summary.GroupsFailed}");
        Console.WriteLine($"  Enrichment collisions resolved: {summary.EnrichmentCollisionsResolved}");
        Console.WriteLine($"  Aliases preserved:             {summary.AliasesPreserved}");
        Console.WriteLine($"  DBA groups:                    {summary.DbaGroups}");
        Console.WriteLine($"  DBA merge rows:                {summary.DbaMergeRows}");
        if (summary.NewsMentionCollisionsResolved > 0)
        {
            Console.WriteLine($"  News mention collisions resolved: {summary.NewsMentionCollisionsResolved}");
        }
        if (summary.DisplacementBriefCollisionsResolved > 0)
        {
            Console.WriteLine($"  Displacement brief collisions resolved: {summary.DisplacementBriefCollisionsResolved}");
        }

        Console.WriteLine("  FK repoints by table:");
        foreach (var target in FkTargets.Select(t => t.Table).Distinct().OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
        {
            summary.FkRepointsByTable.TryGetValue(target, out var count);
            Console.WriteLine($"    {target}: {count}");
        }
    }

    private sealed record ImportOptions(string OpportunitiesDb, bool Commit, bool MergeDba, string OutputDirectory, string? PairsFile)
    {
        public static ImportOptions Parse(string[] args)
        {
            var db = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB") ?? string.Empty;
            var commit = false;
            var mergeDba = false;
            var output = DefaultOutputDirectory;
            string? pairs = null;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--db":
                        db = RequireValue(args, ref i, "--db");
                        break;
                    case "--commit":
                        commit = true;
                        break;
                    case "--merge-dba":
                        mergeDba = true;
                        break;
                    case "--out":
                        output = RequireValue(args, ref i, "--out");
                        break;
                    case "--pairs":
                        pairs = RequireValue(args, ref i, "--pairs");
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument '{args[i]}'.");
                }
            }

            return new ImportOptions(db, commit, mergeDba, output, pairs);
        }

        private static string RequireValue(string[] args, ref int i, string name)
        {
            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"{name} requires a value.");
            }

            i++;
            return args[i];
        }
    }

    private sealed class MergeSummary
    {
        public int GroupsFound { get; set; }
        public int RowsToMerge { get; set; }
        public int RowsBefore { get; set; }
        public int RowsAfter { get; set; }
        public int GroupsCommitted { get; set; }
        public int GroupsFailed { get; set; }
        public int EnrichmentCollisionsResolved { get; set; }
        public int NewsMentionCollisionsResolved { get; set; }
        public int DisplacementBriefCollisionsResolved { get; set; }
        public int AliasesPreserved { get; set; }
        public int DbaGroups { get; set; }
        public int DbaMergeRows { get; set; }
        public Dictionary<string, long> FkRepointsByTable { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class GroupCommitResult
    {
        public int EnrichmentCollisionsResolved { get; set; }
        public int NewsMentionCollisionsResolved { get; set; }
        public int DisplacementBriefCollisionsResolved { get; set; }
        public int AliasesPreserved { get; set; }
        public int LosersDeleted { get; set; }
        public Dictionary<string, long> FkRepointsByTable { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class DisjointSet
    {
        private readonly int[] _parent;
        private readonly int[] _rank;

        public DisjointSet(int count)
        {
            _parent = Enumerable.Range(0, count).ToArray();
            _rank = new int[count];
        }

        public int Find(int value)
        {
            if (_parent[value] != value)
            {
                _parent[value] = Find(_parent[value]);
            }

            return _parent[value];
        }

        public void Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra == rb)
            {
                return;
            }

            if (_rank[ra] < _rank[rb])
            {
                _parent[ra] = rb;
            }
            else if (_rank[ra] > _rank[rb])
            {
                _parent[rb] = ra;
            }
            else
            {
                _parent[rb] = ra;
                _rank[ra]++;
            }
        }
    }

    private sealed record SchemaInfo(bool NewsMentionTypeKeyExists);
    private sealed record FkTarget(string Table, string Column);
    private sealed record GroupKey(string Value, bool IsDba);
    private sealed record OrgRow(
        long Id,
        string Kind,
        string DisplayName,
        string? ClendorClientId,
        string? Website,
        string? Notes,
        Dictionary<string, long> FkRefsByTable)
    {
        public long FkRefCount => FkRefsByTable.Values.Sum();
    }
    private sealed record DuplicateGroup(
        string GroupKey,
        bool HasDbaKey,
        OrgRow Survivor,
        string BestKind,
        List<OrgRow> Losers,
        List<OrgRow> Members);
    private sealed record MergePlanRow(
        string GroupKey,
        bool FromDbaKey,
        long SurvivorId,
        string SurvivorName,
        string SurvivorKind,
        long LoserId,
        string LoserName,
        string LoserKind,
        long FkRefsRepointed);
}
