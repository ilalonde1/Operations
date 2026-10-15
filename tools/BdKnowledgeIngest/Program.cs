// BdKnowledgeIngest — private, local knowledge ingestion and meaning search.
//
// ingest <file|folder> [--kind Book] [--licence OwnedCopyPrivateStudy]
//   [--title ..] [--author ..] [--year ..] [--force] [--dry]
//   [--calibre <ebook-convert.exe>] [--by <name>]
// embed [--source <id>]       repair NULL embeddings, in batches of 500
// search "<question>" [--take 10] [--per-source 2]
// list
// export <sourceId|all> --to <folder>       one JSON document per source
// claims import <file.json|folder> [--force] [--by <name>]
// --db <connection string>   default: env KOR_OPPORTUNITIES_OPPORTUNITIESDB
// --dry reads/chunks only, without a database connection or embedding call.

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Kor.Opportunities.Knowledge;
using Kor.Opportunities.Knowledge.Claims;
using Kor.Opportunities.Knowledge.Embedding;
using Kor.Opportunities.Knowledge.Readers;
using Kor.Opportunities.Knowledge.Search;
using Kor.Opportunities.Knowledge.Store;
using Microsoft.Data.SqlClient;

return await KnowledgeCli.RunAsync(args);

internal static class KnowledgeCli
{
    internal static async Task<int> RunAsync(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var options = Arguments.Parse(args);
            if (options.Flag("--help")) { Usage(); return 0; }
            var verb = options.Positionals.FirstOrDefault()?.ToLowerInvariant();
            if (verb is not ("ingest" or "embed" or "search" or "list" or "export" or "claims")) { Usage(); return 2; }
            options.ValidateFor(verb);
            var ct = cancellation.Token;
            var dry = verb == "ingest" && options.Flag("--dry");
            IKnowledgeStore? store = null;
            if (!dry)
            {
                var connection = options.Value("--db") ?? Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB");
                if (string.IsNullOrWhiteSpace(connection))
                    throw new ArgumentException("Missing connection string. Pass --db or set KOR_OPPORTUNITIES_OPPORTUNITIESDB.");
                // Validate before per-file processing, without echoing credentials on failure.
                try { _ = new SqlConnectionStringBuilder(connection); }
                catch (ArgumentException) { throw new ArgumentException("Invalid database connection string."); }
                store = new SqlKnowledgeStore(connection);
            }
            // These verbs neither construct an embedder nor invoke a model.
            if (verb == "export") return await ExportAsync(options, store!, ct);
            if (verb == "claims") return await ImportClaimsAsync(options, store!, ct);
            using var embedder = new KnowledgeEmbedder(); // ONNX loads lazily; dry/list never load it.
            switch (verb)
            {
                case "ingest":
                    return await IngestAsync(options, store, embedder, ct);
                case "embed":
                    return await RepairAsync(options, store!, embedder, ct);
                case "search":
                {
                    using var search = new KnowledgeSearch(store!, embedder);
                    var hits = await search.SearchAsync(options.Positionals[1], options.PositiveInt("--take", 10),
                        perSource: options.NonnegativeInt("--per-source", 2), ct: ct);
                    foreach (var hit in hits)
                    {
                        var excerpt = hit.Passage.Text[..Math.Min(200, hit.Passage.Text.Length)];
                        Console.WriteLine($"{hit.Score.ToString("0.000", CultureInfo.InvariantCulture)}  {OneLine(hit.SourceTitle)}  {OneLine(hit.Passage.Ref ?? "—")}  \"{OneLine(excerpt)}\"");
                    }
                    return 0;
                }
                default:
                    foreach (var source in await store!.ListSourcesAsync(ct))
                        Console.WriteLine($"{source.Id}  {source.Passages} passages  {source.Tokens} tokens  {source.Claims} claims  {source.Licence}  {OneLine(source.Author ?? "—")}  {OneLine(source.Title)}");
                    return 0;
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + Reason(ex));
            return ex is ArgumentException ? 2 : 1;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private static async Task<int> IngestAsync(Arguments args, IKnowledgeStore? store, IKnowledgeEmbedder embedder, CancellationToken ct)
    {
        var path = Path.GetFullPath(args.Positionals[1]);
        var isFolder = Directory.Exists(path);
        if (!isFolder && !File.Exists(path)) throw new FileNotFoundException("Input file or folder was not found.");
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = false, MatchCasing = MatchCasing.CaseInsensitive,
            IgnoreInaccessible = false, AttributesToSkip = FileAttributes.Directory,
        };
        // Push extension filters to the filesystem; no recursive walk or book copying.
        var files = isFolder
            ? ReaderPicker.Extensions.SelectMany(ext => Directory.EnumerateFiles(path, "*" + ext, enumeration))
                .Distinct(StringComparer.Ordinal).OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { path };
        var kind = args.Value("--kind") ?? KnowledgeKinds.Book;
        var licence = args.Value("--licence") ?? KnowledgeLicences.OwnedCopyPrivateStudy;
        if (!KnowledgeKinds.All.Contains(kind)) throw new ArgumentException("Unknown --kind. Choose Book, Document or Note.");
        if (!KnowledgeLicences.All.Contains(licence)) throw new ArgumentException("Unknown --licence. Choose " + string.Join(", ", KnowledgeLicences.All) + ".");
        short? year = null;
        if (args.Value("--year") is { } rawYear)
        {
            if (!short.TryParse(rawYear, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed is < 1 or > 9999)
                throw new ArgumentException("--year must be a year from 1 to 9999.");
            year = parsed;
        }
        var options = new KnowledgeIngestOptions(kind, licence, args.Value("--title"), args.Value("--author"),
            year, args.Value("--by"), args.Flag("--force"), args.Flag("--dry"));
        var ingestor = new KnowledgeIngestor(new ReaderPicker(args.Value("--calibre")), store, embedder);
        var ok = 0;
        var failed = 0;
        var dryRead = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var timer = Stopwatch.StartNew();
            try
            {
                var result = await ingestor.IngestAsync(file, options, ct);
                if (result.Skipped)
                    Console.WriteLine($"SKIP already ingested as source {result.SourceId}  {OneLine(Path.GetFileName(file))}");
                else if (options.Dry)
                {
                    dryRead++;
                    Console.WriteLine($"DRY {result.Passages} passages  {result.Tokens} tokens  {OneLine(Path.GetFileName(file))}  {OneLine(result.FirstRef ?? "—")}  {OneLine(result.FirstLine ?? "")}");
                }
                else
                {
                    ok++;
                    Console.WriteLine($"OK  {result.Passages} passages  {result.Tokens} tokens  {timer.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)}s  {OneLine(Path.GetFileName(file))}");
                }
            }
            catch (NoTextLayerException)
            {
                Console.WriteLine($"SKIP scan (no text layer)  {OneLine(Path.GetFileName(file))}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"FAIL {Reason(ex)}  {OneLine(Path.GetFileName(file))}");
            }
        }
        if (options.Dry) Console.WriteLine($"DRY {dryRead} files read; no database writes.");
        Console.WriteLine($"{ok} of {files.Length} ingested");
        return failed > 0 ? 1 : 0;
    }

    private static async Task<int> RepairAsync(Arguments args, IKnowledgeStore store, IKnowledgeEmbedder embedder, CancellationToken ct)
    {
        long? sourceId = null;
        if (args.Value("--source") is { } raw)
        {
            if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                throw new ArgumentException("--source must be a positive source ID.");
            sourceId = parsed;
        }
        var rows = await store.ListUnembeddedAsync(sourceId, ct);
        var written = 0;
        foreach (var batch in rows.Chunk(500))
        {
            var vectors = new List<(long PassageId, byte[] Embedding)>(batch.Length);
            foreach (var passage in batch)
            {
                ct.ThrowIfCancellationRequested();
                if (passage.Text.Length > Chunking.MaxChars)
                    throw new InvalidDataException($"Passage {passage.Id} exceeds 1500 characters; re-ingest its source.");
                vectors.Add((passage.Id, KnowledgeEmbedder.ToBytes(embedder.Embed(passage.Text))));
            }
            await store.SetEmbeddingsAsync(vectors, ct);
            written += vectors.Count;
            Console.WriteLine($"Embedded {written} of {rows.Count} missing passages.");
        }
        if (rows.Count == 0) Console.WriteLine("No missing embeddings.");
        return 0;
    }

    private static async Task<int> ExportAsync(Arguments args, IKnowledgeStore store, CancellationToken ct)
    {
        var folder = Path.GetFullPath(args.Value("--to")!);
        var selector = args.Positionals[1];
        var ids = selector.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? (await store.ListSourcesAsync(ct)).Select(s => s.Id).Order().ToArray()
            : new[] { long.Parse(selector, CultureInfo.InvariantCulture) };
        Directory.CreateDirectory(folder);
        var ok = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var file = await store.ExportAsync(id, ct);
                var slug = ClaimValidation.NormalizeSlug(file.Title);
                if (slug.Length == 0) slug = "book";
                if (slug.Length > 100) slug = slug[..100].TrimEnd('-');
                var name = $"{id.ToString(CultureInfo.InvariantCulture)}-{slug}.json";
                await File.WriteAllBytesAsync(Path.Combine(folder, name), JsonSerializer.SerializeToUtf8Bytes(file, ClaimFile.JsonOptions), ct);
                ok++;
                Console.WriteLine($"OK {file.Passages.Count} passages exported  {name}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Console.WriteLine($"FAIL source {id}  {Reason(ex)}"); }
        }
        Console.WriteLine($"{ok} of {ids.Length} sources exported");
        return ok == ids.Length ? 0 : 1;
    }

    private static async Task<int> ImportClaimsAsync(Arguments args, IKnowledgeStore store, CancellationToken ct)
    {
        var path = Path.GetFullPath(args.Positionals[2]);
        var isFolder = Directory.Exists(path);
        if (!isFolder && !File.Exists(path)) throw new FileNotFoundException("Claims file or folder was not found.");
        var files = isFolder
            ? Directory.EnumerateFiles(path, "*.claims.json", new EnumerationOptions
                { RecurseSubdirectories = false, MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = false })
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ThenBy(f => f, StringComparer.Ordinal).ToArray()
            : new[] { path };
        var by = args.Value("--by") ?? Environment.UserName;
        var ok = 0;
        var claims = 0;
        var rejected = 0;
        foreach (var input in files)
        {
            ct.ThrowIfCancellationRequested();
            ClaimFile? file = null;
            try
            {
                file = ClaimFile.Parse(await File.ReadAllBytesAsync(input, ct), Path.GetFileName(input));
                var result = await store.ImportClaimsAsync(file, args.Flag("--force"), by, ct);
                ok++;
                claims += result.ClaimsWritten;
                Console.WriteLine($"OK {result.ClaimsWritten} accepted / {result.ClaimsRejected} rejected / {result.PassagesCovered} passages covered  {OneLine(Path.GetFileName(input))}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Console.WriteLine($"FAIL {OneLine(Path.GetFileName(input))}  {Reason(ex)}"); }
            if (file is not null)
            {
                rejected += file.Rejections.Count;
                foreach (var reject in file.Rejections)
                    Console.WriteLine($"  REJECT passage {reject.PassageId}, claim {reject.ClaimNumber}: {OneLine(reject.Reason)}");
            }
        }
        Console.WriteLine($"{ok} of {files.Length} files imported, {claims} claims, {rejected} rejected");
        return ok == files.Length ? 0 : 1;
    }

    private static string OneLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
    private static string Reason(Exception ex) => ex is SqlException sql
        ? $"Database operation failed (SQL {sql.Number})."
        : OneLine(ex.Message);
    private static void Usage() => Console.WriteLine("""
        BdKnowledgeIngest ingest <file|folder> [--kind Book] [--licence OwnedCopyPrivateStudy]
          [--title ..] [--author ..] [--year ..] [--force] [--dry] [--calibre <path>] [--by <name>]
        BdKnowledgeIngest embed [--source <id>]
        BdKnowledgeIngest search "<question>" [--take 10] [--per-source 2]
        BdKnowledgeIngest list
        BdKnowledgeIngest export <sourceId|all> --to <folder>
        BdKnowledgeIngest claims import <file.json|folder> [--force] [--by <name>]
        --db <connection string> defaults to env KOR_OPPORTUNITIES_OPPORTUNITIESDB
        """);

    private sealed class Arguments
    {
        private static readonly HashSet<string> Switches = new(StringComparer.Ordinal) { "--force", "--dry", "--help" };
        private static readonly HashSet<string> Values = new(StringComparer.Ordinal)
            { "--db", "--kind", "--licence", "--title", "--author", "--year", "--calibre", "--by", "--source", "--take", "--per-source", "--to" };
        private readonly Dictionary<string, string?> _options = new(StringComparer.Ordinal);
        internal List<string> Positionals { get; } = new();
        internal string? Value(string name) => _options.GetValueOrDefault(name);
        internal bool Flag(string name) => _options.ContainsKey(name);
        internal int NonnegativeInt(string name, int fallback)
        {
            if (Value(name) is not { } raw) return fallback;
            if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0)
                throw new ArgumentException($"{name} must be a nonnegative integer.");
            return value;
        }
        internal int PositiveInt(string name, int fallback)
        {
            var value = NonnegativeInt(name, fallback);
            if (value == 0) throw new ArgumentException($"{name} must be positive.");
            return value;
        }
        internal static Arguments Parse(string[] args)
        {
            var result = new Arguments();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal)) { result.Positionals.Add(arg); continue; }
                if (!Switches.Contains(arg) && !Values.Contains(arg)) throw new ArgumentException("Unknown option; use --help.");
                string? value = null;
                if (Values.Contains(arg))
                {
                    if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException("Missing value for " + arg);
                    value = args[i];
                }
                if (!result._options.TryAdd(arg, value)) throw new ArgumentException("Repeated option: " + arg);
            }
            return result;
        }
        internal void ValidateFor(string verb)
        {
            var expected = verb == "claims" ? 3 : verb is "ingest" or "search" or "export" ? 2 : 1;
            if (Positionals.Count != expected) throw new ArgumentException($"Wrong number of arguments for {verb}; use --help.");
            if (verb == "claims" && !Positionals[1].Equals("import", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Use claims import <file.json|folder>.");
            if (verb == "export")
            {
                if (string.IsNullOrWhiteSpace(Value("--to"))) throw new ArgumentException("export requires --to <folder>.");
                if (!Positionals[1].Equals("all", StringComparison.OrdinalIgnoreCase)
                    && (!long.TryParse(Positionals[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0))
                    throw new ArgumentException("export requires a positive source ID or all.");
            }
            var allowed = verb switch
            {
                "ingest" => new[] { "--kind", "--licence", "--title", "--author", "--year", "--calibre", "--by", "--force", "--dry" },
                "embed" => new[] { "--source" },
                "search" => new[] { "--take", "--per-source" },
                "export" => new[] { "--to" },
                "claims" => new[] { "--force", "--by" },
                _ => Array.Empty<string>(),
            };
            foreach (var key in _options.Keys)
                if (key != "--db" && !allowed.Contains(key)) throw new ArgumentException($"{key} does not apply to {verb}.");
        }
    }
}
