#nullable enable
using System.Data;
using System.Diagnostics;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Core.PrimeConsultant;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Kor.PrimeRfpClassifierBackfill;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var options = ToolOptions.Load(config, args);
        if (string.IsNullOrWhiteSpace(options.OpportunitiesDb))
        {
            Console.Error.WriteLine("Missing connection string. Set KOR_OPPORTUNITIES_OPPORTUNITIESDB, appsettings:OpportunitiesDb, or --db.");
            return 2;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var sw = Stopwatch.StartNew();
        var rows = await LoadRowsAsync(options.OpportunitiesDb, cts.Token).ConfigureAwait(false);
        var stats = new BackfillStats();
        Console.WriteLine($"Prime RFP classifier backfill starting: rows={rows.Count:N0}; dry-run={options.DryRun.ToString().ToLowerInvariant()}");

        await using var con = new SqlConnection(options.OpportunitiesDb);
        if (!options.DryRun)
        {
            await con.OpenAsync(cts.Token).ConfigureAwait(false);
        }

        foreach (var row in rows)
        {
            cts.Token.ThrowIfCancellationRequested();
            var classification = PrimeConsultantClassifier.Classify(row.Opportunity);
            stats.Total++;
            if (classification.IsPrimeConsultantRfp)
            {
                stats.FlaggedAsPrime++;
            }

            AddCount(stats.BySector, classification.ProjectSector ?? "(blank)");
            AddCount(stats.ByLikelyType, classification.LikelyPrimeType ?? "(blank)");

            if (!options.DryRun)
            {
                await UpdateClassificationAsync(con, row.Id, classification, cts.Token).ConfigureAwait(false);
            }

            if (stats.Total % 2_000 == 0)
            {
                Console.WriteLine($"Progress: {stats.Total:N0}/{rows.Count:N0}; flagged={stats.FlaggedAsPrime:N0}");
            }
        }

        sw.Stop();
        Console.WriteLine($"Prime RFP classifier backfill complete: total={stats.Total:N0}, flaggedAsPrime={stats.FlaggedAsPrime:N0}, elapsed={sw.Elapsed:g}.");
        PrintCounts("By sector", stats.BySector);
        PrintCounts("By likely type", stats.ByLikelyType);
        return 0;
    }

    private static async Task<IReadOnlyList<BackfillRow>> LoadRowsAsync(string connectionString, CancellationToken ct)
    {
        const string sql = @"
SELECT Id, OpportunityKey, Name, BuyerName, BuyerType, Discipline, ProjectCategory, ProjectProvince
FROM opportunities.Opportunities
ORDER BY Id;";

        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 60 };
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<BackfillRow>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new BackfillRow(
                reader.GetInt64(0),
                new Opportunity
                {
                    Id = reader.GetInt64(0),
                    OpportunityKey = reader.GetString(1),
                    Name = reader.GetString(2),
                    BuyerName = reader.GetString(3),
                    BuyerType = (BuyerType)reader.GetInt32(4),
                    Discipline = (OpportunityDiscipline)reader.GetInt32(5),
                    ProjectCategory = reader.IsDBNull(6) ? null : reader.GetString(6),
                    ProjectProvince = reader.IsDBNull(7) ? null : reader.GetString(7),
                }));
        }

        return rows;
    }

    private static async Task UpdateClassificationAsync(
        SqlConnection con,
        long opportunityId,
        PrimeRfpClassification c,
        CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.Opportunities
SET IsPrimeConsultantRfp = @isPrime,
    PrimeConfidence = @confidence,
    PrimeDisciplineType = @disciplineType,
    PrimeProjectSector = @sector,
    PrimeLikelyType = @likelyType,
    PrimeKorSectorMatch = @sectorMatch,
    PrimeKorLocationMatch = @locationMatch,
    PrimeClassifiedAtUtc = sysdatetimeoffset()
WHERE Id = @id;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 60 };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = opportunityId;
        cmd.Parameters.Add("@isPrime", SqlDbType.Bit).Value = c.IsPrimeConsultantRfp;
        var confidence = cmd.Parameters.Add("@confidence", SqlDbType.Decimal);
        confidence.Precision = 4;
        confidence.Scale = 3;
        confidence.Value = c.Confidence;
        cmd.Parameters.Add("@disciplineType", SqlDbType.NVarChar, 40).Value = (object?)c.DisciplineType ?? DBNull.Value;
        cmd.Parameters.Add("@sector", SqlDbType.NVarChar, 40).Value = (object?)c.ProjectSector ?? DBNull.Value;
        cmd.Parameters.Add("@likelyType", SqlDbType.NVarChar, 20).Value = (object?)c.LikelyPrimeType ?? DBNull.Value;
        cmd.Parameters.Add("@sectorMatch", SqlDbType.Bit).Value = c.KorSectorMatch;
        cmd.Parameters.Add("@locationMatch", SqlDbType.Bit).Value = c.KorLocationMatch;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void AddCount(Dictionary<string, int> counts, string key)
    {
        counts.TryGetValue(key, out var current);
        counts[key] = current + 1;
    }

    private static void PrintCounts(string label, Dictionary<string, int> counts)
    {
        Console.WriteLine(label + ":");
        foreach (var (key, count) in counts.OrderByDescending(kvp => kvp.Value).ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {key}: {count:N0}");
        }
    }

    private sealed record BackfillRow(long Id, Opportunity Opportunity);

    private sealed class BackfillStats
    {
        public int Total { get; set; }
        public int FlaggedAsPrime { get; set; }
        public Dictionary<string, int> BySector { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> ByLikelyType { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ToolOptions(string OpportunitiesDb, bool DryRun)
    {
        public static ToolOptions Load(IConfiguration config, string[] args)
        {
            var db = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
                ?? config["OpportunitiesDb"]
                ?? "";
            var dryRun = false;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--db":
                        db = RequireValue(args, ref i, "--db");
                        break;
                    case "--dry-run":
                        dryRun = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument '{args[i]}'.");
                }
            }

            return new ToolOptions(db, dryRun);
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
}
