#nullable enable
using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

namespace Kor.BdDeltekLink;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = LinkOptions.Parse(args);
            Directory.CreateDirectory(options.OutputDir);

            var config = ImportConfig.FromEnvironment();
            await using var db = new CanonicalOrgLinkStore(config.OpportunitiesDb);

            var deltek = new DeltekLookupCache(config);
            var clients = deltek.LoadClients();
            Console.WriteLine($"Loaded {clients.Count:N0} active Deltek client candidate(s).");

            if (!string.IsNullOrWhiteSpace(options.PairsPath))
            {
                var result = await ApplyReviewedPairsAsync(db, clients, options, CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine("BdDeltekLink pairs complete.");
                Console.WriteLine($"  Applied:                  {result.Applied:N0}");
                Console.WriteLine($"  Rejected (similarity):    {result.RejectedSimilarity:N0}");
                Console.WriteLine($"  Rejected (duplicate):     {result.RejectedDuplicate:N0}");
                Console.WriteLine($"  Skipped (already linked): {result.AlreadyLinked:N0}");
                if (result.RejectedSimilarity + result.RejectedDuplicate > 0)
                {
                    Console.WriteLine($"  Rejected pairs written to {Path.Combine(options.OutputDir, "pairs-rejected.csv")}");
                }

                return 0;
            }

            var linked = await db.LoadLinkedAsync(CancellationToken.None).ConfigureAwait(false);
            var targets = await db.LoadTargetsAsync(options.Kinds, CancellationToken.None).ConfigureAwait(false);

            var plan = BuildPlan(targets, clients, linked);
            WriteCsvs(options.OutputDir, plan);

            var updatedRows = 0;
            if (options.Commit)
            {
                updatedRows = await ApplyLinkPlanAsync(db, plan.LinkRows, CancellationToken.None).ConfigureAwait(false);
            }

            Console.WriteLine($"BdDeltekLink complete (commit={options.Commit.ToString().ToLowerInvariant()}).");
            Console.WriteLine($"  Targets:          {targets.Count:N0}");
            Console.WriteLine($"  Auto-link:        {plan.LinkRows.Count:N0}");
            Console.WriteLine($"  Review:           {plan.ReviewRows.Count:N0}");
            Console.WriteLine($"  Dedup-candidate:  {plan.DedupRows.Count:N0}");
            Console.WriteLine($"  No-match:         {plan.NoMatchCount:N0}");
            if (options.Commit)
            {
                Console.WriteLine($"  Rows updated:     {updatedRows:N0}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"BdDeltekLink failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static LinkPlan BuildPlan(
        IReadOnlyList<CanonicalOrgTarget> targets,
        IReadOnlyList<DeltekClientCandidate> clients,
        IReadOnlyDictionary<string, IReadOnlyList<long>> linked)
    {
        var linkRows = new List<LinkPlanRow>();
        var reviewRows = new List<ReviewRow>();
        var dedupRows = new List<DedupCandidateRow>();
        var noMatch = 0;
        var plannedByClientId = new Dictionary<string, LinkPlanRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in targets)
        {
            var match = DeltekFuzzyMatch.FindCompany(target.DisplayName, clients);
            if (match.Top.Count == 0)
            {
                noMatch++;
                continue;
            }

            var top = match.Top[0];
            var targetTokenCount = DeltekFuzzyMatch.NormalizeCompany(target.DisplayName)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            var candidateTokenCount = DeltekFuzzyMatch.NormalizeCompany(top.Name)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            var twoTokenSafe = targetTokenCount >= 2 && candidateTokenCount >= 2;

            if (match.TopScore >= 1.0d && twoTokenSafe)
            {
                if (linked.TryGetValue(top.ClientId, out var existingOrgIds) && existingOrgIds.Count > 0)
                {
                    dedupRows.Add(new DedupCandidateRow(
                        target.Id,
                        target.DisplayName,
                        existingOrgIds[0],
                        top.ClientId,
                        top.Name));
                    continue;
                }

                if (plannedByClientId.TryGetValue(top.ClientId, out var planned))
                {
                    dedupRows.Add(new DedupCandidateRow(
                        target.Id,
                        target.DisplayName,
                        planned.OrgId,
                        top.ClientId,
                        top.Name));
                    continue;
                }

                var row = new LinkPlanRow(target.Id, target.DisplayName, target.Kind, top.ClientId, top.Name, top.SimilarityScore);
                linkRows.Add(row);
                plannedByClientId[top.ClientId] = row;
            }
            else if (match.TopScore >= 0.85d)
            {
                // Review bucket. Because the auto-link branch above requires BOTH
                // a perfect score AND twoTokenSafe, this intentionally also catches
                // exact (score 1.0) single-token matches — a one-token name is too
                // generic to auto-link, so it goes to human review instead.
                reviewRows.Add(new ReviewRow(target.Id, target.DisplayName, target.Kind, top.ClientId, top.Name, top.SimilarityScore));
            }
            else
            {
                noMatch++;
            }
        }

        return new LinkPlan(linkRows, reviewRows, dedupRows, noMatch);
    }

    private static async Task<int> ApplyLinkPlanAsync(CanonicalOrgLinkStore db, IReadOnlyList<LinkPlanRow> rows, CancellationToken ct)
    {
        var updated = 0;
        foreach (var row in rows)
        {
            updated += await db.TrySetClendorClientIdAsync(row.OrgId, row.ClendorClientId, ct).ConfigureAwait(false);
        }

        return updated;
    }

    // Guarded reviewed-pairs apply (BD-Audit-2026-06-09 M21): a bad reviewed CSV
    // (digit-different numbered companies, M+/M3 -> M2) nearly applied wrong
    // ClendorClientIds on 2026-06-04 because this path trusted the CSV blindly.
    // Every pair must now clear (a) a name-similarity gate (same normalized
    // fuzzy similarity the auto path uses; bypassable with --force-dissimilar)
    // and (b) a duplicate-ClientId guard (one ClientId can only ever link one
    // canonical org, matching the auto path's plannedByClientId/linked checks).
    private const double PairsSimilarityThreshold = 0.70d;

    private static async Task<PairsApplyResult> ApplyReviewedPairsAsync(
        CanonicalOrgLinkStore db,
        IReadOnlyList<DeltekClientCandidate> clients,
        LinkOptions options,
        CancellationToken ct)
    {
        var pairsPath = options.PairsPath!;
        if (!File.Exists(pairsPath))
        {
            throw new FileNotFoundException("Reviewed pairs CSV not found.", pairsPath);
        }

        var lines = await File.ReadAllLinesAsync(pairsPath, ct).ConfigureAwait(false);
        if (lines.Length == 0)
        {
            return new PairsApplyResult(0, 0, 0, 0);
        }

        var header = ParseCsvLine(lines[0]);
        var orgIdIndex = IndexOfHeader(header, "OrgId");
        var clientIdIndex = IndexOfHeader(header, "ClendorClientId");
        if (orgIdIndex < 0 || clientIdIndex < 0)
        {
            throw new InvalidOperationException("--pairs CSV must include OrgId and ClendorClientId columns.");
        }

        var pairs = new List<(long OrgId, string ClientId)>();
        var seenRows = new HashSet<(long, string)>();
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var cells = ParseCsvLine(line);
            if (cells.Count <= Math.Max(orgIdIndex, clientIdIndex))
            {
                continue;
            }

            if (!long.TryParse(cells[orgIdIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var orgId))
            {
                continue;
            }

            var clientId = cells[clientIdIndex].Trim();
            if (string.IsNullOrWhiteSpace(clientId))
            {
                continue;
            }

            // Exact duplicate rows (same OrgId + same ClientId) collapse to one.
            if (seenRows.Add((orgId, clientId.ToUpperInvariant())))
            {
                pairs.Add((orgId, clientId));
            }
        }

        // Within-file duplicate guard: a ClientId mapped to two DIFFERENT orgs in
        // the same input file is always an error — reject every such row.
        var fileDuplicateClientIds = pairs
            .GroupBy(p => p.ClientId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(p => p.OrgId).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var linked = await db.LoadLinkedAsync(ct).ConfigureAwait(false);
        var orgNames = await db.LoadDisplayNamesAsync(pairs.Select(p => p.OrgId).Distinct().ToList(), ct).ConfigureAwait(false);

        var deltekNamesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var client in clients)
        {
            deltekNamesById.TryAdd(client.ClientId, client.Name);
        }

        var rejectedRows = new List<string>();
        var applied = 0;
        var rejectedSimilarity = 0;
        var rejectedDuplicate = 0;
        var alreadyLinked = 0;

        foreach (var (orgId, clientId) in pairs)
        {
            var orgFound = orgNames.TryGetValue(orgId, out var orgName);
            var clientFound = deltekNamesById.TryGetValue(clientId, out var deltekName);
            orgName ??= "(org not found)";
            deltekName ??= "(client not found among active Deltek candidates)";

            var score = orgFound && clientFound
                ? DeltekFuzzyMatch.Similarity(
                    DeltekFuzzyMatch.NormalizeCompany(orgName),
                    DeltekFuzzyMatch.NormalizeCompany(deltekName))
                : 0d;

            if (fileDuplicateClientIds.Contains(clientId))
            {
                rejectedDuplicate++;
                rejectedRows.Add(RejectedPairCsvRow(orgId, orgName, clientId, deltekName, score,
                    "ClientId mapped to more than one OrgId in the input file"));
                Console.Error.WriteLine($"[REJECT duplicate] OrgId {orgId} '{orgName}' -> {clientId}: ClientId appears multiple times in input mapped to different orgs.");
                continue;
            }

            if (linked.TryGetValue(clientId, out var existingOrgIds) && existingOrgIds.Count > 0)
            {
                var otherOrgId = existingOrgIds.FirstOrDefault(id => id != orgId, -1L);
                if (otherOrgId >= 0)
                {
                    rejectedDuplicate++;
                    rejectedRows.Add(RejectedPairCsvRow(orgId, orgName, clientId, deltekName, score,
                        $"ClientId already linked to a different canonical org (OrgId {otherOrgId}) in the DB"));
                    Console.Error.WriteLine($"[REJECT duplicate] OrgId {orgId} '{orgName}' -> {clientId}: already linked to OrgId {otherOrgId} in the DB.");
                    continue;
                }

                alreadyLinked++;
                Console.WriteLine($"[SKIP] OrgId {orgId} '{orgName}' -> {clientId}: already linked to this org (no-op).");
                continue;
            }

            if (!options.ForceDissimilar && score < PairsSimilarityThreshold)
            {
                rejectedSimilarity++;
                rejectedRows.Add(RejectedPairCsvRow(orgId, orgName, clientId, deltekName, score,
                    $"Name similarity {score.ToString("0.###", CultureInfo.InvariantCulture)} below {PairsSimilarityThreshold.ToString("0.##", CultureInfo.InvariantCulture)} (pass --force-dissimilar to override)"));
                Console.Error.WriteLine($"[REJECT similarity] OrgId {orgId} '{orgName}' -> {clientId} '{deltekName}': score {score:0.###} < {PairsSimilarityThreshold:0.##}.");
                continue;
            }

            Console.WriteLine($"[PLAN] OrgId {orgId} '{orgName}' -> ClendorClientId {clientId} '{deltekName}' (score {score:0.###})");
            applied += await db.TrySetClendorClientIdAsync(orgId, clientId, ct).ConfigureAwait(false);
        }

        Directory.CreateDirectory(options.OutputDir);
        File.WriteAllLines(
            Path.Combine(options.OutputDir, "pairs-rejected.csv"),
            new[] { "OrgId,OrgName,ClendorClientId,DeltekName,Score,Reason" }.Concat(rejectedRows),
            Encoding.UTF8);

        return new PairsApplyResult(applied, rejectedSimilarity, rejectedDuplicate, alreadyLinked);
    }

    private static string RejectedPairCsvRow(long orgId, string orgName, string clientId, string deltekName, double score, string reason)
        => CsvRow(
            orgId.ToString(CultureInfo.InvariantCulture),
            orgName,
            clientId,
            deltekName,
            score.ToString("0.###", CultureInfo.InvariantCulture),
            reason);

    private static int IndexOfHeader(IReadOnlyList<string> header, string name)
    {
        for (var i = 0; i < header.Count; i++)
        {
            if (string.Equals(header[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static void WriteCsvs(string outputDir, LinkPlan plan)
    {
        File.WriteAllLines(
            Path.Combine(outputDir, "link-plan.csv"),
            new[] { "OrgId,OrgName,Kind,ClendorClientId,DeltekName,Score" }
                .Concat(plan.LinkRows.Select(r => CsvRow(
                    r.OrgId.ToString(CultureInfo.InvariantCulture),
                    r.OrgName,
                    r.Kind,
                    r.ClendorClientId,
                    r.DeltekName,
                    r.Score.ToString("0.###", CultureInfo.InvariantCulture)))),
            Encoding.UTF8);

        File.WriteAllLines(
            Path.Combine(outputDir, "review.csv"),
            new[] { "OrgId,OrgName,Kind,ClendorClientId,DeltekName,Score" }
                .Concat(plan.ReviewRows.Select(r => CsvRow(
                    r.OrgId.ToString(CultureInfo.InvariantCulture),
                    r.OrgName,
                    r.Kind,
                    r.ClendorClientId,
                    r.DeltekName,
                    r.Score.ToString("0.###", CultureInfo.InvariantCulture)))),
            Encoding.UTF8);

        File.WriteAllLines(
            Path.Combine(outputDir, "dedup-candidates.csv"),
            new[] { "TargetOrgId,TargetName,ExistingOrgId,ClendorClientId,DeltekName" }
                .Concat(plan.DedupRows.Select(r => CsvRow(
                    r.TargetOrgId.ToString(CultureInfo.InvariantCulture),
                    r.TargetName,
                    r.ExistingOrgId.ToString(CultureInfo.InvariantCulture),
                    r.ClendorClientId,
                    r.DeltekName))),
            Encoding.UTF8);
    }

    private static string CsvRow(params string?[] values)
        => string.Join(",", values.Select(Csv));

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    sb.Append(ch);
                }
            }
            else if (ch == ',')
            {
                cells.Add(sb.ToString());
                sb.Clear();
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else
            {
                sb.Append(ch);
            }
        }

        cells.Add(sb.ToString());
        return cells;
    }
}

internal sealed record PairsApplyResult(int Applied, int RejectedSimilarity, int RejectedDuplicate, int AlreadyLinked);

internal sealed record LinkOptions(IReadOnlyList<string> Kinds, string OutputDir, bool Commit, string? PairsPath, bool ForceDissimilar)
{
    public static LinkOptions Parse(string[] args)
    {
        var kinds = new[] { "Buyer", "Architect" };
        var outputDir = "output";
        var commit = false;
        string? pairsPath = null;
        var forceDissimilar = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--kinds":
                    kinds = RequireValue(args, ref i, "--kinds")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;
                case "--out":
                    outputDir = RequireValue(args, ref i, "--out");
                    break;
                case "--commit":
                    commit = true;
                    break;
                case "--pairs":
                    pairsPath = RequireValue(args, ref i, "--pairs");
                    commit = true;
                    break;
                case "--force-dissimilar":
                    // Bypasses ONLY the --pairs name-similarity gate; the
                    // duplicate-ClientId guard can never be bypassed.
                    forceDissimilar = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        if (kinds.Length == 0)
        {
            throw new ArgumentException("--kinds must contain at least one kind.");
        }

        return new LinkOptions(kinds, outputDir, commit, pairsPath, forceDissimilar);
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

internal sealed record ImportConfig(string OpportunitiesDb, string Dsn, string User, string Password, string Catalog)
{
    public static ImportConfig FromEnvironment()
    {
        return new ImportConfig(
            RequiredAny("KOR_OPPORTUNITIES_OPPORTUNITIESDB", "KOR_BD_OPPORTUNITIESDB"),
            Required("KOR_BD_DELTEK_DSN"),
            Required("KOR_BD_DELTEK_USER"),
            Required("KOR_BD_DELTEK_PWD"),
            Environment.GetEnvironmentVariable("KOR_BD_DELTEK_CATALOG")?.Trim() is { Length: > 0 } catalog
                ? catalog
                : ProgramDefaultCatalog);
    }

    public string OdbcConnectionString => $"DSN={Dsn};UID={User};PWD={Password};";

    private const string ProgramDefaultCatalog = "C0000052267P_1_KOR00000000";

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Required environment variable {name} is missing.");
        }

        return value.Trim();
    }

    private static string RequiredAny(string primaryName, string fallbackName)
    {
        var primary = Environment.GetEnvironmentVariable(primaryName);
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        var fallback = Environment.GetEnvironmentVariable(fallbackName);
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback.Trim();
        }

        throw new InvalidOperationException($"Required environment variable {primaryName} or {fallbackName} is missing.");
    }
}

internal sealed class DeltekLookupCache
{
    private readonly ImportConfig _config;

    public DeltekLookupCache(ImportConfig config)
    {
        _config = config;
    }

    public IReadOnlyList<DeltekClientCandidate> LoadClients()
    {
        using var cn = new OdbcConnection(_config.OdbcConnectionString);
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = $@"
SELECT ClientID, Name, Type
FROM [{_config.Catalog}].dbo.Clendor
WHERE ClientInd = 'Y'
  AND (Status IS NULL OR Status <> 'I')";
        using var r = cmd.ExecuteReader();
        var rows = new List<DeltekClientCandidate>();
        while (r.Read())
        {
            var id = GetTrimmed(r, 0);
            var name = GetTrimmed(r, 1);
            if (id.Length == 0 || name.Length == 0)
            {
                continue;
            }

            rows.Add(new DeltekClientCandidate(id, name, r.IsDBNull(2) ? null : Convert.ToString(r.GetValue(2))?.Trim(), 0d));
        }

        return rows;
    }

    private static string GetTrimmed(IDataRecord r, int i)
        => r.IsDBNull(i) ? string.Empty : Convert.ToString(r.GetValue(i))?.Trim() ?? string.Empty;
}

internal sealed class CanonicalOrgLinkStore : IAsyncDisposable
{
    private const int CommandTimeoutSeconds = 60;
    private readonly SqlConnection _connection;

    public CanonicalOrgLinkStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connection = new SqlConnection(connectionString);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<long>>> LoadLinkedAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT Id, ClendorClientId
FROM opportunities.CanonicalOrg
WHERE ClendorClientId IS NOT NULL;";

        await EnsureOpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, _connection) { CommandTimeout = CommandTimeoutSeconds };

        var rows = new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var orgId = r.GetInt64(0);
            var clientId = r.GetString(1).Trim();
            if (!rows.TryGetValue(clientId, out var orgIds))
            {
                orgIds = new List<long>();
                rows[clientId] = orgIds;
            }

            orgIds.Add(orgId);
        }

        return rows.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<long>)kvp.Value, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<CanonicalOrgTarget>> LoadTargetsAsync(IReadOnlyList<string> kinds, CancellationToken ct)
    {
        var kindParams = kinds.Select((_, i) => $"@kind{i}").ToArray();
        var sql = $@"
SELECT Id, DisplayName, Kind
FROM opportunities.CanonicalOrg
WHERE ClendorClientId IS NULL
  AND Kind IN ({string.Join(", ", kindParams)})
ORDER BY DisplayName;";

        await EnsureOpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, _connection) { CommandTimeout = CommandTimeoutSeconds };
        for (var i = 0; i < kinds.Count; i++)
        {
            cmd.Parameters.Add($"@kind{i}", SqlDbType.NVarChar, 40).Value = kinds[i];
        }

        var rows = new List<CanonicalOrgTarget>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new CanonicalOrgTarget(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2)));
        }

        return rows;
    }

    public async Task<IReadOnlyDictionary<long, string>> LoadDisplayNamesAsync(IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        var result = new Dictionary<long, string>();
        if (ids.Count == 0)
        {
            return result;
        }

        await EnsureOpenAsync(ct).ConfigureAwait(false);
        foreach (var chunk in ids.Distinct().Chunk(500))
        {
            var paramNames = chunk.Select((_, i) => $"@id{i}").ToArray();
            var sql = $@"
SELECT Id, DisplayName
FROM opportunities.CanonicalOrg
WHERE Id IN ({string.Join(", ", paramNames)});";

            await using var cmd = new SqlCommand(sql, _connection) { CommandTimeout = CommandTimeoutSeconds };
            for (var i = 0; i < chunk.Length; i++)
            {
                cmd.Parameters.Add($"@id{i}", SqlDbType.BigInt).Value = chunk[i];
            }

            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                result[r.GetInt64(0)] = r.GetString(1);
            }
        }

        return result;
    }

    public async Task<int> TrySetClendorClientIdAsync(long orgId, string clendorClientId, CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.CanonicalOrg
SET ClendorClientId = @cid,
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = @id
  AND ClendorClientId IS NULL;";

        await EnsureOpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, _connection) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@cid", SqlDbType.VarChar, 32).Value = clendorClientId.Trim();
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = orgId;
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(ct).ConfigureAwait(false);
        }
    }
}

internal sealed record DeltekClientCandidate(string ClientId, string Name, string? Type, double SimilarityScore);

internal sealed record CompanyMatch(IReadOnlyList<DeltekClientCandidate> Top)
{
    public double TopScore => Top.Count == 0 ? 0d : Top[0].SimilarityScore;
}

internal static class DeltekFuzzyMatch
{
    internal static CompanyMatch FindCompany(string company, IReadOnlyList<DeltekClientCandidate> clients)
    {
        var normalized = NormalizeCompany(company);
        if (normalized.Length == 0)
        {
            return new CompanyMatch(Array.Empty<DeltekClientCandidate>());
        }

        var top = clients
            .Select(c => c with { SimilarityScore = Similarity(normalized, NormalizeCompany(c.Name)) })
            .OrderByDescending(c => c.SimilarityScore)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        return new CompanyMatch(top);
    }

    internal static string NormalizeCompany(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return string.Empty;
        }

        var lower = s.ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch) || ch == ' ')
            {
                sb.Append(ch);
            }
            else if (ch == ',' || ch == '.' || ch == '-' || ch == '/' || ch == '&')
            {
                sb.Append(' ');
            }
        }

        var tokens = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var kept = new List<string>(tokens.Length);
        foreach (var t in tokens)
        {
            if (CompanySuffixTokens.Contains(t))
            {
                continue;
            }

            kept.Add(t);
        }

        return string.Join(' ', kept);
    }

    internal static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0.0;
        }

        if (a == b)
        {
            return 1.0;
        }

        var distance = Levenshtein(a, b);
        var maxLen = Math.Max(a.Length, b.Length);
        return 1.0 - ((double)distance / maxLen);
    }

    private static readonly HashSet<string> CompanySuffixTokens = new(StringComparer.Ordinal)
    {
        // Legal-entity boilerplate only. Distinctive-name tokens
        // (properties / construction / architecture / engineering /
        // development / consulting / group / holdings) MUST stay
        // in the name so two firms that share a first word but
        // differ in line-of-business don't collide
        // (R60 audit: Fort Properties Ltd vs FORT Architecture).
        "inc", "incorporated", "ltd", "limited", "llc", "llp", "lp",
        "corp", "corporation", "co", "company",
        "international", "intl",
    };

    internal static int Levenshtein(string a, string b)
    {
        if (a.Length < b.Length)
        {
            (a, b) = (b, a);
        }

        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            (prev, cur) = (cur, prev);
        }

        return prev[b.Length];
    }
}

internal sealed record CanonicalOrgTarget(long Id, string DisplayName, string Kind);

internal sealed record LinkPlan(
    IReadOnlyList<LinkPlanRow> LinkRows,
    IReadOnlyList<ReviewRow> ReviewRows,
    IReadOnlyList<DedupCandidateRow> DedupRows,
    int NoMatchCount);

internal sealed record LinkPlanRow(long OrgId, string OrgName, string Kind, string ClendorClientId, string DeltekName, double Score);

internal sealed record ReviewRow(long OrgId, string OrgName, string Kind, string ClendorClientId, string DeltekName, double Score);

internal sealed record DedupCandidateRow(long TargetOrgId, string TargetName, long ExistingOrgId, string ClendorClientId, string DeltekName);
