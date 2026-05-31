#nullable enable
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Kor.BdOrphanOrgPurge;

internal static partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = PurgeOptions.Parse(args);
            Directory.CreateDirectory(options.OutputDir);

            var config = ImportConfig.FromEnvironment();
            await using var store = new OrphanOrgPurgeStore(config.OpportunitiesDb);

            var referencedIds = await store.LoadReferencedIdsAsync(CancellationToken.None).ConfigureAwait(false);
            var candidates = await store.LoadCandidatesAsync(options.TargetKinds, CancellationToken.None).ConfigureAwait(false);
            var plan = BuildPlan(candidates, referencedIds);
            WriteManifest(options.OutputDir, plan.Manifest);

            var categoryCounts = plan.Manifest
                .GroupBy(row => row.Category, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

            var deleted = 0;
            if (options.Commit && plan.Manifest.Count > 0)
            {
                deleted = await store.DeleteManifestAsync(plan.Manifest, options.TargetKinds, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            Console.WriteLine($"BdOrphanOrgPurge complete (commit={options.Commit.ToString().ToLowerInvariant()}).");
            Console.WriteLine($"  Target kinds:         {string.Join(", ", options.TargetKinds)}");
            Console.WriteLine($"  Candidates evaluated: {candidates.Count:N0}");
            Console.WriteLine($"  Referenced kept:      {plan.ReferencedKept:N0}");
            Console.WriteLine($"  KeptRealOwner:        {plan.KeptRealOwner:N0}");
            Console.WriteLine($"  Would delete:         {plan.Manifest.Count:N0}");
            PrintCategory("EmployeeCode", categoryCounts);
            PrintCategory("Person", categoryCounts);
            PrintCategory("Numbered", categoryCounts);
            PrintCategory("Orphan", categoryCounts);
            if (options.Commit)
            {
                Console.WriteLine($"  Rows deleted:         {deleted:N0}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"BdOrphanOrgPurge failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static OrphanOrgPurgePlan BuildPlan(
        IReadOnlyList<CanonicalOrgCandidate> candidates,
        IReadOnlySet<long> referencedIds)
    {
        var rows = new List<OrphanOrgManifestRow>();
        var referencedKept = 0;
        var keptRealOwner = 0;
        foreach (var candidate in candidates)
        {
            if (referencedIds.Contains(candidate.Id))
            {
                referencedKept++;
                continue;
            }

            var category = Categorize(candidate.DisplayName);
            if (category == "Orphan" && IsProtectedEntityName(candidate.DisplayName))
            {
                keptRealOwner++;
                continue;
            }

            rows.Add(new OrphanOrgManifestRow(
                candidate.Id,
                candidate.DisplayName,
                candidate.Kind,
                category));
        }

        return new OrphanOrgPurgePlan(rows, referencedKept, keptRealOwner);
    }

    private static string Categorize(string displayName)
    {
        var trimmed = displayName.TrimStart();
        if (EmployeeCodeRegex().IsMatch(trimmed))
        {
            return "EmployeeCode";
        }

        if (trimmed.StartsWith("?", StringComparison.Ordinal))
        {
            return "Person";
        }

        if (trimmed.Length > 0 && char.IsDigit(trimmed[0]))
        {
            return "Numbered";
        }

        return "Orphan";
    }

    private static bool IsProtectedEntityName(string displayName)
    {
        var lower = displayName.ToLowerInvariant();
        return ProtectedEntityKeywords.Any(keyword => lower.Contains(keyword, StringComparison.Ordinal))
            || ProtectedEntitySuffixRegex().IsMatch(displayName);
    }

    private static void PrintCategory(string category, IReadOnlyDictionary<string, int> categoryCounts)
    {
        categoryCounts.TryGetValue(category, out var count);
        Console.WriteLine($"  {category,-12}: {count:N0}");
    }

    private static void WriteManifest(string outputDir, IReadOnlyList<OrphanOrgManifestRow> rows)
    {
        File.WriteAllLines(
            Path.Combine(outputDir, "orphan-orgs.csv"),
            new[] { "Id,DisplayName,Kind,Category" }
                .Concat(rows.Select(row => CsvRow(
                    row.Id.ToString(CultureInfo.InvariantCulture),
                    row.DisplayName,
                    row.Kind,
                    row.Category))),
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

    [GeneratedRegex("^[0-9]{3}[A-Z]", RegexOptions.CultureInvariant)]
    private static partial Regex EmployeeCodeRegex();

    [GeneratedRegex(@"\b(inc|ltd|corp|llp|lp|co)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProtectedEntitySuffixRegex();

    private static readonly string[] ProtectedEntityKeywords =
    [
        "city of",
        "town of",
        "township",
        "village",
        "district of",
        "municipal",
        "municipalité",
        "regional district",
        "metro vancouver",
        "county",
        "comté",
        "ville de",
        "school district",
        "board of education",
        "college",
        "university",
        "université",
        "institute",
        "polytechnic",
        "health authorit",
        "health authority",
        "hospital",
        "health",
        "authority",
        "ministry",
        "ministère",
        "department of",
        "government",
        "province of",
        "crown",
        "first nation",
        "indian band",
        "métis",
        "inuit",
        "tribal council",
        "library",
        "transit",
        "transportation",
        "housing",
        "hydro",
        "utilities",
        "port authority",
        "airport",
        "police",
        "fire department",
        "fire rescue",
        "parks",
        "conservation",
        "society",
        "association",
        "council",
        "commission",
        "agency",
        "corporation",
        "cooperative",
        "co-operative",
        "development",
        "developments",
        "properties",
        "projects",
        "realty",
        "holdings",
        "partnership",
        "enterprises",
        "construction",
        "contracting",
        "ventures",
        "builders",
        "homes",
        "group",
        "limited",
        "incorporated",
        "nation",
        "band",
        "société",
        "ltée",
    ];
}

internal sealed record PurgeOptions(string OutputDir, bool Commit, IReadOnlyList<string> TargetKinds)
{
    public static PurgeOptions Parse(string[] args)
    {
        var outputDir = "output";
        var commit = false;
        var targetKinds = new[] { "Buyer" };

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out":
                    outputDir = RequireValue(args, ref i, "--out");
                    break;
                case "--commit":
                    commit = true;
                    break;
                case "--kinds":
                    targetKinds = RequireValue(args, ref i, "--kinds")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (targetKinds.Length == 0)
                    {
                        throw new ArgumentException("--kinds requires at least one kind.");
                    }

                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        return new PurgeOptions(outputDir, commit, targetKinds);
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

internal sealed record ImportConfig(string OpportunitiesDb)
{
    public static ImportConfig FromEnvironment()
        => new(RequiredAny("KOR_OPPORTUNITIES_OPPORTUNITIESDB", "KOR_BD_OPPORTUNITIESDB"));

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

internal sealed class OrphanOrgPurgeStore : IAsyncDisposable
{
    private const int CommandTimeoutSeconds = 300;
    private readonly SqlConnection _connection;

    public OrphanOrgPurgeStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connection = new SqlConnection(connectionString);
    }

    public async Task<HashSet<long>> LoadReferencedIdsAsync(CancellationToken ct)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var ids = new HashSet<long>();
        foreach (var reference in ReferenceColumns)
        {
            // Table and column names are trusted hard-coded constants, never user/config input.
            var sql = $"SELECT DISTINCT {reference.Column} FROM opportunities.{reference.Table} WHERE {reference.Column} IS NOT NULL;";
            await using var cmd = new SqlCommand(sql, _connection) { CommandTimeout = CommandTimeoutSeconds };
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                ids.Add(reader.GetInt64(0));
            }
        }

        return ids;
    }

    public async Task<IReadOnlyList<CanonicalOrgCandidate>> LoadCandidatesAsync(
        IReadOnlyList<string> targetKinds,
        CancellationToken ct)
    {
        if (targetKinds.Count == 0)
        {
            throw new ArgumentException("At least one target kind is required.", nameof(targetKinds));
        }

        var kindParams = targetKinds.Select((_, i) => $"@kind{i}").ToArray();
        var sql = $@"
SELECT Id, DisplayName, Kind, ClendorClientId
FROM opportunities.CanonicalOrg
WHERE ClendorClientId IS NULL
  AND Kind IN ({string.Join(", ", kindParams)})
ORDER BY Id;";

        await EnsureOpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, _connection) { CommandTimeout = CommandTimeoutSeconds };
        for (var i = 0; i < targetKinds.Count; i++)
        {
            cmd.Parameters.Add(kindParams[i], SqlDbType.NVarChar, 40).Value = targetKinds[i];
        }

        var rows = new List<CanonicalOrgCandidate>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new CanonicalOrgCandidate(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return rows;
    }

    public async Task<int> DeleteManifestAsync(
        IReadOnlyList<OrphanOrgManifestRow> manifest,
        IReadOnlyList<string> targetKinds,
        CancellationToken ct)
    {
        var deleted = 0;
        foreach (var row in manifest)
        {
            deleted += await DeleteOneAsync(row.Id, row.DisplayName, targetKinds, ct).ConfigureAwait(false);
        }

        return deleted;
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<int> DeleteOneAsync(
        long id,
        string displayName,
        IReadOnlyList<string> targetKinds,
        CancellationToken ct)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var tx = (SqlTransaction)await _connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            try
            {
                if (await IsReferencedAsync(id, tx, ct).ConfigureAwait(false))
                {
                    await tx.RollbackAsync(ct).ConfigureAwait(false);
                    Console.WriteLine($"Skipped referenced org {id}: {displayName}");
                    return 0;
                }

                await DeleteAliasesAsync(id, tx, ct).ConfigureAwait(false);
                var deleted = await DeleteOrgAsync(id, targetKinds, tx, ct).ConfigureAwait(false);
                if (deleted == 0)
                {
                    await tx.RollbackAsync(ct).ConfigureAwait(false);
                    Console.WriteLine($"Skipped changed org {id}: {displayName}");
                    return 0;
                }

                await tx.CommitAsync(ct).ConfigureAwait(false);
                return deleted;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                try
                {
                    await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollbackEx)
                {
                    Console.Error.WriteLine($"Rollback failed for org {id}: {rollbackEx.GetType().Name}: {rollbackEx.Message}");
                }

                Console.Error.WriteLine($"Skipped org {id} ({displayName}): {ex.GetType().Name}: {ex.Message}");
                return 0;
            }
        }
        finally
        {
            await tx.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<bool> IsReferencedAsync(long id, SqlTransaction tx, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (1) 1
WHERE EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory WHERE ProponentCanonicalOrgId = @id)
   OR EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory WHERE ArchitectCanonicalOrgId = @id)
   OR EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory WHERE StructuralEngineerCanonicalOrgId = @id)
   OR EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory WHERE GeneralContractorCanonicalOrgId = @id)
   OR EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId = @id)
   OR EXISTS (SELECT 1 FROM opportunities.Opportunities WHERE BuyerCanonicalOrgId = @id)
   OR EXISTS (SELECT 1 FROM opportunities.OpportunityAwards WHERE AwardedToCanonicalOrgId = @id)
   OR EXISTS (SELECT 1 FROM opportunities.OpportunityAwards WHERE AwardingCanonicalOrgId = @id)
   OR EXISTS (SELECT 1 FROM opportunities.KorPursuits WHERE BuyerCanonicalOrgId = @id)
   OR EXISTS (SELECT 1 FROM opportunities.KorPursuits WHERE LostToCanonicalOrgId = @id)
   OR EXISTS (SELECT 1 FROM opportunities.NewsArticleOrgMention WHERE CanonicalOrgId = @id)
   OR EXISTS (SELECT 1 FROM opportunities.BuildingPermit WHERE OwnerCanonicalOrgId = @id)
   OR EXISTS (SELECT 1 FROM opportunities.BuildingPermit WHERE ApplicantCanonicalOrgId = @id)
   OR EXISTS (SELECT 1 FROM opportunities.BuildingPermit WHERE ContractorCanonicalOrgId = @id);";

        await using var cmd = new SqlCommand(sql, _connection, tx) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        var value = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is not null && value is not DBNull;
    }

    private async Task DeleteAliasesAsync(long id, SqlTransaction tx, CancellationToken ct)
    {
        const string sql = "DELETE FROM opportunities.OrgAlias WHERE CanonicalOrgId = @id;";

        await using var cmd = new SqlCommand(sql, _connection, tx) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<int> DeleteOrgAsync(
        long id,
        IReadOnlyList<string> targetKinds,
        SqlTransaction tx,
        CancellationToken ct)
    {
        var kindParams = targetKinds.Select((_, i) => $"@kind{i}").ToArray();
        var sql = $@"
DELETE FROM opportunities.CanonicalOrg
WHERE Id = @id
  AND ClendorClientId IS NULL
  AND Kind IN ({string.Join(", ", kindParams)});";

        await using var cmd = new SqlCommand(sql, _connection, tx) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        for (var i = 0; i < targetKinds.Count; i++)
        {
            cmd.Parameters.Add(kindParams[i], SqlDbType.NVarChar, 40).Value = targetKinds[i];
        }

        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(ct).ConfigureAwait(false);
        }
    }

    private static readonly ReferenceColumn[] ReferenceColumns =
    [
        new("MajorProjectsInventory", "ProponentCanonicalOrgId"),
        new("MajorProjectsInventory", "ArchitectCanonicalOrgId"),
        new("MajorProjectsInventory", "StructuralEngineerCanonicalOrgId"),
        new("MajorProjectsInventory", "GeneralContractorCanonicalOrgId"),
        new("CanonicalOrgEnrichment", "CanonicalOrgId"),
        new("Opportunities", "BuyerCanonicalOrgId"),
        new("OpportunityAwards", "AwardedToCanonicalOrgId"),
        new("OpportunityAwards", "AwardingCanonicalOrgId"),
        new("KorPursuits", "BuyerCanonicalOrgId"),
        new("KorPursuits", "LostToCanonicalOrgId"),
        new("NewsArticleOrgMention", "CanonicalOrgId"),
        new("BuildingPermit", "OwnerCanonicalOrgId"),
        new("BuildingPermit", "ApplicantCanonicalOrgId"),
        new("BuildingPermit", "ContractorCanonicalOrgId"),
    ];
}

internal sealed record ReferenceColumn(string Table, string Column);

internal sealed record CanonicalOrgCandidate(
    long Id,
    string DisplayName,
    string Kind,
    string? ClendorClientId);

internal sealed record OrphanOrgManifestRow(
    long Id,
    string DisplayName,
    string Kind,
    string Category);

internal sealed record OrphanOrgPurgePlan(
    IReadOnlyList<OrphanOrgManifestRow> Manifest,
    int ReferencedKept,
    int KeptRealOwner);
