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
// claims embed                 fill missing claim vectors in batches of 500
// claims relate <file.json> [--by <name>]
// claims search "<question>" [--take 12]
// brief export [--week <yyyy-MM-dd>] --to <folder>
// brief import <export.json> <sections.json> --by <name> [--force]
// hour import <hour.json> --by <name>       the prepared hour, written in session, for the Today tab and the 6 a.m. email
// hour render <hour.json> --to <file.html>  the email exactly as it would be sent, for checking; sends nothing
// rhythm render --to <folder>               agendas and Monday-list drafts from the live action register; sends nothing
// --db <connection string>   default: env KOR_OPPORTUNITIES_OPPORTUNITIESDB
// --dry reads/chunks only, without a database connection or embedding call.

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Kor.Opportunities.Knowledge;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Knowledge;
using Kor.Opportunities.Data.MajorProjects;
using Kor.Opportunities.Knowledge.Brief;
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
            if (verb is not ("ingest" or "embed" or "search" or "list" or "export" or "claims" or "brief" or "pursuit" or "hour" or "rhythm" or "doctrine" or "prompt")) { Usage(); return 2; }
            options.ValidateFor(verb);
            var ct = cancellation.Token;
            if (verb == "hour" && options.Positionals[1].Equals("render", StringComparison.OrdinalIgnoreCase))
                return await HourRenderAsync(options, ct);
            var dry = verb == "ingest" && options.Flag("--dry");
            IKnowledgeStore? store = null;
            string? connection = null;
            if (!dry)
            {
                connection = options.Value("--db") ?? Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB");
                if (string.IsNullOrWhiteSpace(connection))
                    throw new ArgumentException("Missing connection string. Pass --db or set KOR_OPPORTUNITIES_OPPORTUNITIESDB.");
                // Validate before per-file processing, without echoing credentials on failure.
                try { _ = new SqlConnectionStringBuilder(connection); }
                catch (ArgumentException) { throw new ArgumentException("Invalid database connection string."); }
                store = new SqlKnowledgeStore(connection);
            }
            // Claim export/import, relation import and brief import do not construct an embedder.
            // Claims embed/search and brief export construct the local embedder in their branches.
            if (verb == "export") return await ExportAsync(options, store!, ct);
            if (verb == "claims") return await ClaimsAsync(options, store!, ct);
            if (verb == "brief") return await BriefAsync(options, store!, connection!, ct);
            if (verb == "hour") return await HourImportAsync(options, new SqlBdRhythmStore(connection!), ct);
            if (verb == "rhythm") return await RhythmRenderAsync(options, new SqlBdRhythmStore(connection!), ct);
            if (verb == "doctrine") return await DoctrineAsync(options, new SqlBdDoctrineStore(connection!), ct);
            if (verb == "prompt") return await PromptReportAsync(options, new SqlBdDoctrineStore(connection!), ct);
            if (verb == "pursuit")
            {
                var json = await File.ReadAllTextAsync(options.Positionals[4], ct);
                await PursuitChecklistImport.ImportAsync(new SqlPursuitRequirementStore(connection!),
                    long.Parse(options.Positionals[3], CultureInfo.InvariantCulture), json,
                    options.Value("--by")!, options.Flag("--replace"), ct);
                Console.WriteLine("Checklist imported.");
                return 0;
            }
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

    private static async Task<int> HourImportAsync(Arguments args, IBdRhythmStore rhythm, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(args.Positionals[2], ct);
        var (content, errors) = DirectorHourFile.Parse(json);
        if (content is null)
        {
            foreach (var e in errors) Console.Error.WriteLine("  " + e);
            Console.Error.WriteLine("Not imported: the hour has " + errors.Count.ToString(CultureInfo.InvariantCulture) + " problem(s).");
            return 1;
        }

        var forDate = DateOnly.ParseExact(content.ForDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var id = await rhythm.ImportHourAsync(content.Kind, forDate, json, args.Value("--by")!, Environment.UserName, ct);
        Console.WriteLine($"OK hour {id.ToString(CultureInfo.InvariantCulture)} for {content.ForDate} ({content.Kind}): {content.Moves.Count.ToString(CultureInfo.InvariantCulture)} moves, about {DirectorHourFile.TotalMinutes(content).ToString(CultureInfo.InvariantCulture)} minutes.");
        return 0;
    }

    private static async Task<int> HourRenderAsync(Arguments args, CancellationToken ct)
    {
        var (content, errors) = DirectorHourFile.Parse(await File.ReadAllTextAsync(args.Positionals[2], ct));
        if (content is null)
        {
            foreach (var e in errors) Console.Error.WriteLine("  " + e);
            return 1;
        }

        var to = Path.GetFullPath(args.Value("--to")!);
        await File.WriteAllTextAsync(to, "<!doctype html><meta charset=\"utf-8\"><title>" + System.Net.WebUtility.HtmlEncode(DirectorHourEmail.Subject(content))
            + "</title>" + DirectorHourEmail.Render(content), ct);
        Console.WriteLine("Subject: " + DirectorHourEmail.Subject(content));
        Console.WriteLine(to);
        return 0;
    }

    private static async Task<int> RhythmRenderAsync(Arguments args, IBdRhythmStore rhythm, CancellationToken ct)
    {
        var folder = Path.GetFullPath(args.Value("--to")!);
        Directory.CreateDirectory(folder);
        var now = DateTimeOffset.UtcNow;
        var actions = await rhythm.ListActionsAsync(ct);
        var meetings = await rhythm.ListMeetingsAsync(ct);
        var lastAllHands = meetings.Where(m => m.Kind == "AllHands" && m.Status == "Held").Select(m => (DateTimeOffset?)m.ScheduledForUtc).FirstOrDefault();
        await File.WriteAllTextAsync(Path.Combine(folder, "monday-lists-drafts.html"),
            "<!doctype html><meta charset=\"utf-8\"><title>Monday lists</title>" + BdLeadLists.RenderDraftsEmail(actions, now), ct);
        await File.WriteAllTextAsync(Path.Combine(folder, "all-hands-agenda.txt"), BdAgenda.ForAllHands(actions, now, lastAllHands).ToText(), ct);
        foreach (var (key, name, _) in BdLeadLists.Group(actions))
        {
            var last = meetings.Where(m => m.Kind == "OneToOne" && m.Status == "Held" && string.Equals(m.WithStaffKey, key, StringComparison.OrdinalIgnoreCase))
                .Select(m => (DateTimeOffset?)m.ScheduledForUtc).FirstOrDefault();
            var file = "one-to-one-" + new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant() + ".txt";
            await File.WriteAllTextAsync(Path.Combine(folder, file),
                BdAgenda.ForOneToOne(name, actions.Where(a => string.Equals(a.OwnerKey, key, StringComparison.OrdinalIgnoreCase)), now, last).ToText(), ct);
        }

        Console.WriteLine($"{actions.Count(a => a.IsOpen).ToString(CultureInfo.InvariantCulture)} open actions; files in {folder}");
        return 0;
    }

    private static async Task<int> BriefAsync(Arguments args, IKnowledgeStore store, string connection, CancellationToken ct)
    {
        if (args.Positionals[1].Equals("import", StringComparison.OrdinalIgnoreCase))
        {
            var result = await DirectorBriefFiles.ImportAsync(await File.ReadAllBytesAsync(args.Positionals[2], ct),
                await File.ReadAllBytesAsync(args.Positionals[3], ct), args.Positionals[3], args.Value("--by")!, args.Flag("--force"),
                new SqlDirectorBriefStore(connection), ct);
            foreach (var reason in result.Rejected) Console.WriteLine("REJECT " + OneLine(reason));
            if (result.Rejected.Count != 0) return 1;
            var name = OneLine(Path.GetFileName(args.Positionals[3]));
            Console.WriteLine(result.SectionsWritten == 0 ? $"SKIP already imported  {name}" : $"OK {result.SectionsWritten} sections written  {name}");
            return 0;
        }
        var week = args.Value("--week") is { } raw
            ? DateOnly.ParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : DirectorBriefExport.NextSunday(DateTimeOffset.UtcNow);
        using var embedder = new KnowledgeEmbedder();
        var verdicts = await new SqlKnowledgeClaimStore(connection).ListClaimVerdictsAsync(ct);
        using var search = new KnowledgeSearch(store, embedder, verdicts.ToDictionary(v => v.ClaimId));
        var facts = new DirectorFacts(new SqlBdDirectorStore(connection), new SqlBdDirectorLedgerStore(connection), new SqlBdDashboardStore(connection));
        var export = await new DirectorBriefExport(facts, search, new SqlDirectorBriefStore(connection)).ExportAsync(week, ct);
        var folder = Path.GetFullPath(args.Value("--to")!);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, week.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "-director-brief.export.json");
        await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(export, DirectorBriefFiles.JsonOptions), ct);
        foreach (var section in export.Sections) Console.WriteLine($"{section.Section}  {section.Facts.Length} facts characters  {section.Claims.Count} claims  {section.PriorCalls.Count} prior calls");
        Console.WriteLine(path);
        return 0;
    }

    private static async Task<int> ClaimsAsync(Arguments args, IKnowledgeStore store, CancellationToken ct)
    {
        var action = args.Positionals[1].ToLowerInvariant();
        if (action == "import") return await ImportClaimsAsync(args, store, ct);
        if (action == "relate")
        {
            var file = JsonSerializer.Deserialize<RelationFile>(await File.ReadAllTextAsync(args.Positionals[2], ct), ClaimFile.JsonOptions)
                ?? throw new InvalidDataException("A relations document is required.");
            var result = await store.ImportRelationsAsync(file, args.Value("--by") ?? file.WrittenBy, ct);
            foreach (var reject in file.Rejections) Console.WriteLine($"REJECT relation {reject.Number}: {OneLine(reject.Reason)}");
            Console.WriteLine($"{result.Written} written / {result.Rejected} rejected");
            return 0;
        }
        using var embedder = new KnowledgeEmbedder();
        if (action == "search")
        {
            using var search = new KnowledgeSearch(store, embedder);
            foreach (var hit in await search.SearchClaimsAsync(args.Positionals[2], args.PositiveInt("--take", 12), ct: ct))
                Console.WriteLine($"{hit.Score.ToString("0.000", CultureInfo.InvariantCulture)}  {OneLine(hit.SourceTitle)}  {hit.Kind}  {OneLine(hit.Statement)}");
            return 0;
        }
        var embedded = 0;
        while (true)
        {
            var batch = await store.ListUnembeddedClaimsAsync(500, ct);
            if (batch.Count == 0) break;
            var vectors = new List<(long ClaimId, byte[] Embedding)>();
            foreach (var claim in batch)
            {
                ct.ThrowIfCancellationRequested();
                vectors.Add((claim.Id, KnowledgeEmbedder.ToBytes(embedder.Embed(claim.Statement))));
            }
            await store.SetClaimEmbeddingsAsync(vectors, KnowledgeEmbedder.ModelName, ct);
            embedded += vectors.Count;
        }
        Console.WriteLine($"embedded {embedded} claims");
        return 0;
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
    /// <summary>The doctrine a session reads: accepted cards by title, or one card in full (any status).</summary>
    private static async Task<int> DoctrineAsync(Arguments args, IBdDoctrineStore store, CancellationToken ct)
    {
        var cards = await store.ListCardsAsync(args.Value("--role") ?? "BD", ct);
        if (args.Positionals[1].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var c in cards.Where(c => c.Status is "accepted" or "proposed"))
                Console.WriteLine($"#{c.Id}  {c.Status}  {OneLine(c.Title)}  [{c.AppliesTo}]");
            Console.WriteLine($"{cards.Count(c => c.Status == "accepted")} accepted, {cards.Count(c => c.Status == "proposed")} proposed.");
            return 0;
        }

        var id = long.Parse(args.Positionals[2], CultureInfo.InvariantCulture);
        if (cards.FirstOrDefault(c => c.Id == id) is not { } card) { Console.Error.WriteLine($"FAIL No {args.Value("--role") ?? "BD"} card #{id}."); return 2; }
        Console.WriteLine($"#{card.Id} {card.Title} ({card.Status})\nApplies to: {card.AppliesTo}\nPlain: {card.Plain}\nPrinciple: {card.Principle}\n"
            + $"You are in it when: {card.Recognise}\nThe play: {card.Play}\nWarning sign: {card.Warning ?? "—"}\nFrom: {card.Citations}\nTags: {card.Tags ?? "—"}");
        return 0;
    }

    /// <summary>A session's report, checked against its one-time token. Refused with every reason when it does not validate.</summary>
    private static async Task<int> PromptReportAsync(Arguments args, IBdDoctrineStore store, CancellationToken ct)
    {
        var runId = long.Parse(args.Positionals[2], CultureInfo.InvariantCulture);
        var (report, errors) = BdDoctrine.ParseReport(await File.ReadAllTextAsync(args.Positionals[3], ct));
        if (report is null)
        {
            foreach (var e in errors) Console.Error.WriteLine("FAIL " + e);
            return 2;
        }

        if (!await store.ReportAsync(runId, args.Value("--token")!, report, ct))
        {
            Console.Error.WriteLine($"FAIL The token does not match run {runId}. Use the command exactly as the prompt gave it.");
            return 2;
        }

        Console.WriteLine($"Reported run {runId}: {report.Outcome}, {report.Cards.Count} card(s) proposed. Ian sees them in BD Director, Doctrine.");
        return 0;
    }

    private static void Usage() => Console.WriteLine("""
        BdKnowledgeIngest ingest <file|folder> [--kind Book] [--licence OwnedCopyPrivateStudy]
          [--title ..] [--author ..] [--year ..] [--force] [--dry] [--calibre <path>] [--by <name>]
        BdKnowledgeIngest embed [--source <id>]
        BdKnowledgeIngest search "<question>" [--take 10] [--per-source 2]
        BdKnowledgeIngest list
        BdKnowledgeIngest export <sourceId|all> --to <folder>
        BdKnowledgeIngest claims import <file.json|folder> [--force] [--by <name>]
        BdKnowledgeIngest claims embed
        BdKnowledgeIngest claims relate <file.json> [--by <name>]
        BdKnowledgeIngest claims search "<question>" [--take 12]
        BdKnowledgeIngest brief export [--week <yyyy-MM-dd>] --to <folder>
        BdKnowledgeIngest brief import <export.json> <sections.json> --by <name> [--force]
        BdKnowledgeIngest pursuit checklist import <engagementId> <file.json> --by <name> [--replace]
        BdKnowledgeIngest doctrine list [--role BD|Operations]
        BdKnowledgeIngest doctrine show <id> [--role BD|Operations]
        BdKnowledgeIngest prompt report <runId> <report.json> --token <token>
        --db <connection string> defaults to env KOR_OPPORTUNITIES_OPPORTUNITIESDB
        """);

    private sealed class Arguments
    {
        private static readonly HashSet<string> Switches = new(StringComparer.Ordinal) { "--force", "--dry", "--help", "--replace" };
        private static readonly HashSet<string> Values = new(StringComparer.Ordinal)
            { "--db", "--kind", "--licence", "--title", "--author", "--year", "--calibre", "--by", "--source", "--take", "--per-source", "--to", "--week", "--token", "--role" };
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
            var claimAction = verb == "claims" && Positionals.Count > 1 ? Positionals[1].ToLowerInvariant() : "";
            var briefAction = verb == "brief" && Positionals.Count > 1 ? Positionals[1].ToLowerInvariant() : "";
            var doctrineAction = verb == "doctrine" && Positionals.Count > 1 ? Positionals[1].ToLowerInvariant() : "";
            var expected = verb == "prompt" ? 4 : verb == "doctrine" ? doctrineAction == "show" ? 3 : 2
                : verb == "pursuit" ? 5 : verb == "hour" ? 3 : verb == "rhythm" ? 2 : verb == "brief" ? briefAction == "import" ? 4 : 2
                : verb == "claims" ? claimAction == "embed" ? 2 : 3 : verb is "ingest" or "search" or "export" ? 2 : 1;
            if (Positionals.Count != expected) throw new ArgumentException($"Wrong number of arguments for {verb}; use --help.");
            if (verb == "pursuit")
            {
                if (!Positionals[1].Equals("checklist", StringComparison.OrdinalIgnoreCase)
                    || !Positionals[2].Equals("import", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Use pursuit checklist import <engagementId> <file.json> --by <name> [--replace].");
                if (!long.TryParse(Positionals[3], NumberStyles.None, CultureInfo.InvariantCulture, out var engagementId))
                    throw new ArgumentException("A positive pursuit ID is required.");
                PursuitRequirementValidation.Identity(engagementId, Value("--by") ?? "");
            }
            if (verb == "hour")
            {
                var hourAction = Positionals[1].ToLowerInvariant();
                if (hourAction is not ("import" or "render")) throw new ArgumentException("Use hour import <hour.json> --by <name>, or hour render <hour.json> --to <file.html>.");
                if (hourAction == "import" && (string.IsNullOrWhiteSpace(Value("--by")) || Value("--by")!.Length > 150))
                    throw new ArgumentException("hour import requires --by <name> (1–150 characters).");
                if (hourAction == "render" && string.IsNullOrWhiteSpace(Value("--to"))) throw new ArgumentException("hour render requires --to <file.html>.");
            }
            if (verb == "rhythm")
            {
                if (!Positionals[1].Equals("render", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(Value("--to")))
                    throw new ArgumentException("Use rhythm render --to <folder>.");
            }
            if (verb == "doctrine")
            {
                if (doctrineAction is not ("list" or "show")) throw new ArgumentException("Use doctrine list [--role BD|Operations], or doctrine show <id>.");
                if (doctrineAction == "show" && !long.TryParse(Positionals[2], NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    throw new ArgumentException("doctrine show needs a card id.");
                if (Value("--role") is { } role && role is not ("BD" or "Operations")) throw new ArgumentException("--role is BD or Operations.");
            }
            if (verb == "prompt")
            {
                if (!Positionals[1].Equals("report", StringComparison.OrdinalIgnoreCase)
                    || !long.TryParse(Positionals[2], NumberStyles.None, CultureInfo.InvariantCulture, out _)
                    || Value("--token") is not { Length: >= 20 and <= 100 })
                    throw new ArgumentException("Use prompt report <runId> <report.json> --token <token>.");
            }
            if (verb == "claims" && claimAction is not ("import" or "embed" or "relate" or "search"))
                throw new ArgumentException("Use claims import, embed, relate or search.");
            if (verb == "brief")
            {
                if (briefAction is not ("export" or "import")) throw new ArgumentException("Use brief export or import.");
                if (briefAction == "export" && string.IsNullOrWhiteSpace(Value("--to"))) throw new ArgumentException("brief export requires --to <folder>.");
                if (briefAction == "import" && (string.IsNullOrWhiteSpace(Value("--by")) || Value("--by")!.Length > 100))
                    throw new ArgumentException("brief import requires --by <name> (1–100 characters).");
                if (Value("--week") is { } week && !DateOnly.TryParseExact(week, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    throw new ArgumentException("--week must be yyyy-MM-dd.");
            }
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
                "pursuit" => new[] { "--by", "--replace" },
                "hour" => new[] { "--by", "--to" },
                "rhythm" => new[] { "--to" },
                "doctrine" => new[] { "--role" },
                "prompt" => new[] { "--token" },
                "claims" => claimAction switch { "import" => new[] { "--force", "--by" }, "relate" => new[] { "--by" }, "search" => new[] { "--take" }, _ => Array.Empty<string>() },
                "brief" => briefAction == "import" ? new[] { "--force", "--by" } : new[] { "--week", "--to" },
                _ => Array.Empty<string>(),
            };
            foreach (var key in _options.Keys)
                if (key != "--db" && !allowed.Contains(key)) throw new ArgumentException($"{key} does not apply to {verb}.");
        }
    }
}
