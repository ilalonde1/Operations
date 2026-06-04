#nullable enable
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Kor.Opportunities.Data.Intel;
using Microsoft.Data.SqlClient;

namespace Kor.BdIntelExtract;

internal static class Program
{
    private static readonly string DefaultOutputDirectory = ResolveDefaultOutputDirectory();

    private static string ResolveDefaultOutputDirectory()
    {
        var asmDir = Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? Directory.GetCurrentDirectory();
        var probe = new DirectoryInfo(asmDir);
        while (probe is not null)
        {
            if (Directory.Exists(Path.Combine(probe.FullName, ".git")))
            {
                return Path.Combine(probe.FullName, "tools", "BdIntelExtract", "output");
            }

            probe = probe.Parent;
        }

        return asmDir;
    }

    private static async Task<int> Main(string[] args)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var options = ImportOptions.Parse(args);
            if (string.IsNullOrWhiteSpace(options.OpportunitiesDb))
            {
                Console.Error.WriteLine("Missing connection string. Set KOR_OPPORTUNITIES_OPPORTUNITIESDB.");
                return 2;
            }

            Directory.CreateDirectory(options.OutputDirectory);
            Console.WriteLine($"Output directory: {Path.GetFullPath(options.OutputDirectory)}");
            Console.WriteLine($"Mode: {(options.Commit ? "commit" : "dry-run")}");

            var reportPath = Path.Combine(options.OutputDirectory, "extract-report.csv");
            var extractors = new IIntelExtractor[]
            {
                new DataHoningExtractor(),
                new PublicSectorResearchExtractor(),
                new CompetitorProfileExtractor(),
                new FirmNarrativeExtractor(),
            };
            var fallback = new DefaultIntelExtractor();
            var registry = new IntelExtractorRegistry(extractors, fallback);
            var persistence = options.Commit ? new IntelPersistenceService(options.OpportunitiesDb) : null;

            var summary = new RunSummary();
            await using var report = new StreamWriter(reportPath, append: false, Encoding.UTF8);
            await report.WriteLineAsync("EnrichmentId,CanonicalOrgId,ProviderName,ExtractorType,Persons,Affiliations,Signals,Actions,Works,Risks,Narratives,Inserted,Updated,Status,ErrorMessage").ConfigureAwait(false);

            await using var con = new SqlConnection(options.OpportunitiesDb);
            await con.OpenAsync().ConfigureAwait(false);
            await using var cmd = new SqlCommand(BuildSelectSql(options.ProviderName), con)
            {
                CommandTimeout = 60,
            };
            if (!string.IsNullOrWhiteSpace(options.ProviderName))
            {
                cmd.Parameters.Add("@provider", SqlDbType.NVarChar, 60).Value = options.ProviderName;
            }

            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess).ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                summary.RowsSeen++;

                var row = new EnrichmentRow(
                    Id: reader.GetInt64(0),
                    CanonicalOrgId: reader.GetInt64(1),
                    ProviderName: reader.GetString(2),
                    ResultJson: reader.GetString(3),
                    RefreshedAtUtc: reader.GetDateTimeOffset(4));

                await ProcessRowAsync(row, registry, persistence, options.Commit, summary, report).ConfigureAwait(false);

                if (summary.RowsSeen % 100 == 0)
                {
                    Console.WriteLine(
                        $"[{summary.RowsSeen.ToString("D4", CultureInfo.InvariantCulture)}] processed (status counts: {{dry={summary.StatusCount("DryRunOk")}, persisted={summary.StatusCount("Persisted")}, skipped={summary.StatusCount("SkippedNoExtractor")}, error={summary.StatusCount("ParseError") + summary.StatusCount("PersistError")}}})");
                }

                if (options.MaxRows.HasValue && summary.RowsSeen >= options.MaxRows.Value)
                {
                    break;
                }
            }

            sw.Stop();
            await report.FlushAsync().ConfigureAwait(false);

            Console.WriteLine($"Report written: {reportPath}");
            WriteSummary(summary, sw.Elapsed);
            return summary.StatusCount("ParseError") + summary.StatusCount("PersistError") == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"BdIntelExtract failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static string BuildSelectSql(string? providerName)
    {
        var sql = @"
SELECT Id,
       CanonicalOrgId,
       ProviderName,
       ResultJson,
       ISNULL(LastRefreshAtUtc, CreatedAtUtc) AS RefreshedAtUtc
FROM opportunities.CanonicalOrgEnrichment
WHERE Status = N'ok'
  AND ResultJson IS NOT NULL";

        if (!string.IsNullOrWhiteSpace(providerName))
        {
            sql += @"
  AND ProviderName = @provider";
        }

        return sql + @"
ORDER BY Id;";
    }

    private static async Task ProcessRowAsync(
        EnrichmentRow row,
        IntelExtractorRegistry registry,
        IntelPersistenceService? persistence,
        bool commit,
        RunSummary summary,
        StreamWriter report)
    {
        var extractor = registry.Resolve(row.ProviderName);
        var extractorType = extractor.GetType().Name;
        if (extractor is DefaultIntelExtractor)
        {
            summary.IncrementStatus("SkippedNoExtractor");
            await WriteReportRowAsync(report, row, extractorType, DraftCounts.Zero, 0, 0, "SkippedNoExtractor", string.Empty).ConfigureAwait(false);
            return;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(row.ResultJson);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            summary.IncrementStatus("ParseError");
            Console.Error.WriteLine($"[WARN] enrichment {row.Id} parse failed ({row.ProviderName}): {ex.Message}");
            await WriteReportRowAsync(report, row, extractorType, DraftCounts.Zero, 0, 0, "ParseError", ex.Message).ConfigureAwait(false);
            return;
        }

        using (doc)
        {
            var ctx = new IntelExtractionContext(row.CanonicalOrgId, row.Id, row.ProviderName, doc, row.RefreshedAtUtc);
            var drafts = extractor.Extract(ctx);
            var counts = DraftCounts.From(drafts);
            summary.AddDraftCounts(counts);

            if (counts.Total == 0)
            {
                summary.IncrementStatus("Empty");
                await WriteReportRowAsync(report, row, extractorType, counts, 0, 0, "Empty", string.Empty).ConfigureAwait(false);
                return;
            }

            if (!commit || persistence is null)
            {
                summary.IncrementStatus("DryRunOk");
                await WriteReportRowAsync(report, row, extractorType, counts, 0, 0, "DryRunOk", string.Empty).ConfigureAwait(false);
                return;
            }

            try
            {
                var result = await persistence.PersistAsync(drafts, ctx, CancellationToken.None).ConfigureAwait(false);
                var inserted = SumInserted(result);
                var updated = SumUpdated(result);
                summary.IncrementStatus("Persisted");
                await WriteReportRowAsync(report, row, extractorType, counts, inserted, updated, "Persisted", string.Empty).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                summary.IncrementStatus("PersistError");
                Console.Error.WriteLine($"[WARN] enrichment {row.Id} persist failed ({row.ProviderName}): {ex.Message}");
                await WriteReportRowAsync(report, row, extractorType, counts, 0, 0, "PersistError", ex.Message).ConfigureAwait(false);
            }
        }
    }

    private static int SumInserted(IntelPersistResult r) =>
        r.PersonsInserted
        + r.AffiliationsInserted
        + r.SignalsInserted
        + r.ActionsInserted
        + r.WorksInserted
        + r.RisksInserted
        + r.NarrativesInserted;

    private static int SumUpdated(IntelPersistResult r) =>
        r.PersonsUpdated
        + r.AffiliationsUpdated
        + r.SignalsUpdated
        + r.ActionsUpdated
        + r.WorksUpdated
        + r.RisksUpdated
        + r.NarrativesUpdated;

    private static Task WriteReportRowAsync(
        StreamWriter writer,
        EnrichmentRow row,
        string extractorType,
        DraftCounts counts,
        int inserted,
        int updated,
        string status,
        string errorMessage)
    {
        return writer.WriteLineAsync(string.Join(",",
            row.Id.ToString(CultureInfo.InvariantCulture),
            row.CanonicalOrgId.ToString(CultureInfo.InvariantCulture),
            Csv(row.ProviderName),
            Csv(extractorType),
            counts.Persons.ToString(CultureInfo.InvariantCulture),
            counts.Affiliations.ToString(CultureInfo.InvariantCulture),
            counts.Signals.ToString(CultureInfo.InvariantCulture),
            counts.Actions.ToString(CultureInfo.InvariantCulture),
            counts.Works.ToString(CultureInfo.InvariantCulture),
            counts.Risks.ToString(CultureInfo.InvariantCulture),
            counts.Narratives.ToString(CultureInfo.InvariantCulture),
            inserted.ToString(CultureInfo.InvariantCulture),
            updated.ToString(CultureInfo.InvariantCulture),
            Csv(status),
            Csv(errorMessage)));
    }

    private static string Csv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static void WriteSummary(RunSummary summary, TimeSpan elapsed)
    {
        Console.WriteLine("BD intel extraction complete.");
        Console.WriteLine($"  Rows seen:       {summary.RowsSeen}");
        Console.WriteLine("  Status counts:");
        foreach (var status in new[] { "DryRunOk", "Persisted", "SkippedNoExtractor", "Empty", "ParseError", "PersistError" })
        {
            Console.WriteLine($"    {status}: {summary.StatusCount(status)}");
        }

        Console.WriteLine("  Drafts emitted:");
        Console.WriteLine($"    Persons:      {summary.Persons}");
        Console.WriteLine($"    Affiliations: {summary.Affiliations}");
        Console.WriteLine($"    Signals:      {summary.Signals}");
        Console.WriteLine($"    Actions:      {summary.Actions}");
        Console.WriteLine($"    Works:        {summary.Works}");
        Console.WriteLine($"    Risks:        {summary.Risks}");
        Console.WriteLine($"    Narratives:   {summary.Narratives}");
        Console.WriteLine($"  Runtime:         {elapsed}");
    }

    private sealed record ImportOptions(
        string OpportunitiesDb,
        bool Commit,
        string? ProviderName,
        int? MaxRows,
        string OutputDirectory)
    {
        public static ImportOptions Parse(string[] args)
        {
            var db = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB") ?? string.Empty;
            var commit = false;
            string? provider = null;
            int? max = null;
            var output = DefaultOutputDirectory;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--dry-run":
                        commit = false;
                        break;
                    case "--commit":
                        commit = true;
                        break;
                    case "--provider":
                        provider = RequireValue(args, ref i, "--provider");
                        break;
                    case "--max":
                        var maxValue = RequireValue(args, ref i, "--max");
                        if (!int.TryParse(maxValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMax)
                            || parsedMax <= 0)
                        {
                            throw new ArgumentException("--max requires a positive integer.");
                        }

                        max = parsedMax;
                        break;
                    case "--output":
                        output = RequireValue(args, ref i, "--output");
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument '{args[i]}'.");
                }
            }

            return new ImportOptions(db, commit, provider, max, output);
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

    private sealed record EnrichmentRow(
        long Id,
        long CanonicalOrgId,
        string ProviderName,
        string ResultJson,
        DateTimeOffset RefreshedAtUtc);

    private sealed record DraftCounts(
        int Persons,
        int Affiliations,
        int Signals,
        int Actions,
        int Works,
        int Risks,
        int Narratives)
    {
        public static DraftCounts Zero { get; } = new(0, 0, 0, 0, 0, 0, 0);

        public int Total => Persons + Affiliations + Signals + Actions + Works + Risks + Narratives;

        public static DraftCounts From(ExtractedIntel drafts) => new(
            drafts.People.Count,
            drafts.Affiliations.Count,
            drafts.Signals.Count,
            drafts.Actions.Count,
            drafts.Works.Count,
            drafts.Risks.Count,
            drafts.Narratives.Count);
    }

    private sealed class RunSummary
    {
        private readonly Dictionary<string, int> _statusCounts = new(StringComparer.Ordinal);

        public int RowsSeen { get; set; }
        public int Persons { get; private set; }
        public int Affiliations { get; private set; }
        public int Signals { get; private set; }
        public int Actions { get; private set; }
        public int Works { get; private set; }
        public int Risks { get; private set; }
        public int Narratives { get; private set; }

        public int StatusCount(string status) => _statusCounts.TryGetValue(status, out var count) ? count : 0;

        public void IncrementStatus(string status)
        {
            _statusCounts.TryGetValue(status, out var existing);
            _statusCounts[status] = existing + 1;
        }

        public void AddDraftCounts(DraftCounts counts)
        {
            Persons += counts.Persons;
            Affiliations += counts.Affiliations;
            Signals += counts.Signals;
            Actions += counts.Actions;
            Works += counts.Works;
            Risks += counts.Risks;
            Narratives += counts.Narratives;
        }
    }
}
