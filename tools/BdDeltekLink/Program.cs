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

            if (!string.IsNullOrWhiteSpace(options.PairsPath))
            {
                var updated = await ApplyReviewedPairsAsync(db, options.PairsPath, CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine($"BdDeltekLink pairs applied. Rows updated: {updated:N0}");
                return 0;
            }

            var deltek = new DeltekLookupCache(config);
            var clients = deltek.LoadClients();
            Console.WriteLine($"Loaded {clients.Count:N0} active Deltek client candidate(s).");

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
            if (match.TopScore >= 1.0d)
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

    private static async Task<int> ApplyReviewedPairsAsync(CanonicalOrgLinkStore db, string pairsPath, CancellationToken ct)
    {
        if (!File.Exists(pairsPath))
        {
            throw new FileNotFoundException("Reviewed pairs CSV not found.", pairsPath);
        }

        var updated = 0;
        var lines = await File.ReadAllLinesAsync(pairsPath, ct).ConfigureAwait(false);
        if (lines.Length == 0)
        {
            return 0;
        }

        var header = ParseCsvLine(lines[0]);
        var orgIdIndex = IndexOfHeader(header, "OrgId");
        var clientIdIndex = IndexOfHeader(header, "ClendorClientId");
        if (orgIdIndex < 0 || clientIdIndex < 0)
        {
            throw new InvalidOperationException("--pairs CSV must include OrgId and ClendorClientId columns.");
        }

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

            updated += await db.TrySetClendorClientIdAsync(orgId, clientId, ct).ConfigureAwait(false);
        }

        return updated;
    }

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

internal sealed record LinkOptions(IReadOnlyList<string> Kinds, string OutputDir, bool Commit, string? PairsPath)
{
    public static LinkOptions Parse(string[] args)
    {
        var kinds = new[] { "Buyer", "Architect" };
        var outputDir = "output";
        var commit = false;
        string? pairsPath = null;

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
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        if (kinds.Length == 0)
        {
            throw new ArgumentException("--kinds must contain at least one kind.");
        }

        return new LinkOptions(kinds, outputDir, commit, pairsPath);
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
        "inc", "incorporated", "ltd", "limited", "llc", "llp", "lp",
        "corp", "corporation", "co", "company",
        "group", "holdings", "international", "intl",
        "properties", "property",
        "construction", "constructors",
        "development", "developments", "developers", "dev",
        "consulting", "consultants",
        "architects", "architecture",
        "engineering", "engineers"
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
