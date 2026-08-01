#nullable enable
using System.Data;
using System.Globalization;
using System.Text;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.BdOpportunityPurge;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = PurgeOptions.Parse(args);
            Directory.CreateDirectory(options.OutputDir);

            var config = ImportConfig.FromEnvironment();
            await using var store = new OpportunityPurgeStore(config.OpportunitiesDb);

            var candidates = await store.LoadCandidatesAsync(CancellationToken.None).ConfigureAwait(false);
            var manifest = BuildManifest(candidates);
            WriteManifest(options.OutputDir, manifest);

            var deleted = 0;
            if (options.Commit && manifest.Count > 0)
            {
                deleted = await store.DeleteManifestAsync(manifest.Select(row => row.Id).ToArray(), CancellationToken.None)
                    .ConfigureAwait(false);
            }

            Console.WriteLine($"BdOpportunityPurge complete (commit={options.Commit.ToString().ToLowerInvariant()}).");
            Console.WriteLine($"  Candidates evaluated: {candidates.Count:N0}");
            Console.WriteLine($"  Would delete:         {manifest.Count:N0}");
            Console.WriteLine($"  Would keep:           {candidates.Count - manifest.Count:N0}");
            if (options.Commit)
            {
                Console.WriteLine($"  Rows deleted:         {deleted:N0}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"BdOpportunityPurge failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static IReadOnlyList<PurgeManifestRow> BuildManifest(IReadOnlyList<PurgeCandidate> candidates)
    {
        var rows = new List<PurgeManifestRow>();
        foreach (var candidate in candidates)
        {
            var decision = StructuralRelevanceGate.Evaluate(candidate.Name, candidate.Description, candidate.BuyerName);
            if (!decision.Keep)
            {
                rows.Add(new PurgeManifestRow(
                    candidate.Id,
                    candidate.Name,
                    candidate.BuyerName,
                    candidate.Status.ToString(),
                    decision.RejectReason ?? "non-structural"));
            }
        }

        return rows;
    }

    private static void WriteManifest(string outputDir, IReadOnlyList<PurgeManifestRow> rows)
    {
        File.WriteAllLines(
            Path.Combine(outputDir, "purge-opportunities.csv"),
            new[] { "Id,Name,Buyer,Status,Reason" }
                .Concat(rows.Select(row => CsvRow(
                    row.Id.ToString(CultureInfo.InvariantCulture),
                    row.Name,
                    row.Buyer,
                    row.Status,
                    row.Reason))),
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
}

internal sealed record PurgeOptions(string OutputDir, bool Commit)
{
    public static PurgeOptions Parse(string[] args)
    {
        var outputDir = "output";
        var commit = false;

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
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        return new PurgeOptions(outputDir, commit);
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

internal sealed class OpportunityPurgeStore : IAsyncDisposable
{
    private const int CommandTimeoutSeconds = 60;
    private static readonly int NewStatus = (int)OpportunityStatus.New;
    private readonly SqlConnection _connection;

    public OpportunityPurgeStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connection = new SqlConnection(connectionString);
    }

    public async Task<IReadOnlyList<PurgeCandidate>> LoadCandidatesAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT o.Id,
       o.Name,
       o.BuyerName,
       o.Status,
       (SELECT TOP 1 ob.Description
        FROM opportunities.OpportunityObservations ob
        WHERE ob.OpportunityId = o.Id
          AND ob.Description IS NOT NULL
        ORDER BY ob.Id DESC) AS Description
FROM opportunities.Opportunities o
WHERE o.CreatedBy = N'ingestion'
  AND o.Status = @newStatus
  AND NOT EXISTS (SELECT 1 FROM opportunities.KorPursuits p WHERE p.OpportunityId = o.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.OpportunityNotes n WHERE n.OpportunityId = o.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.OpportunityFiles f WHERE f.OpportunityId = o.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.CrmEngagements ce WHERE ce.OpportunityId = o.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.OpportunityFeeProposalLinks fp WHERE fp.OpportunityId = o.Id)
ORDER BY o.Id;";

        await EnsureOpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, _connection) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@newStatus", SqlDbType.Int).Value = NewStatus;

        var rows = new List<PurgeCandidate>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new PurgeCandidate(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                (OpportunityStatus)r.GetInt32(3),
                r.IsDBNull(4) ? null : r.GetString(4)));
        }

        return rows;
    }

    public async Task<int> DeleteManifestAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        var deleted = 0;
        foreach (var batch in ids.Distinct().Chunk(ProgramDeleteBatchSize))
        {
            deleted += await DeleteBatchAsync(batch, ct).ConfigureAwait(false);
        }

        return deleted;
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<int> DeleteBatchAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        var idParams = ids.Select((_, i) => $"@id{i}").ToArray();
        var sql = $@"
DELETE o
FROM opportunities.Opportunities o
WHERE o.Id IN ({string.Join(", ", idParams)})
  AND o.CreatedBy = N'ingestion'
  AND o.Status = @newStatus
  AND NOT EXISTS (SELECT 1 FROM opportunities.KorPursuits p WHERE p.OpportunityId = o.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.OpportunityNotes n WHERE n.OpportunityId = o.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.OpportunityFiles f WHERE f.OpportunityId = o.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.CrmEngagements ce WHERE ce.OpportunityId = o.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.OpportunityFeeProposalLinks fp WHERE fp.OpportunityId = o.Id);";

        await EnsureOpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, _connection) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@newStatus", SqlDbType.Int).Value = NewStatus;
        for (var i = 0; i < ids.Count; i++)
        {
            cmd.Parameters.Add($"@id{i}", SqlDbType.BigInt).Value = ids[i];
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

    private const int ProgramDeleteBatchSize = 500;
}

internal sealed record PurgeCandidate(
    long Id,
    string Name,
    string BuyerName,
    OpportunityStatus Status,
    string? Description);

internal sealed record PurgeManifestRow(
    long Id,
    string Name,
    string Buyer,
    string Status,
    string Reason);
