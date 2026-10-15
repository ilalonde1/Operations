using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Opportunities.Knowledge.Claims;
using Kor.Opportunities.Knowledge.Embedding;
using Kor.Opportunities.Knowledge.Readers;
using Kor.Opportunities.Knowledge.Search;
using Kor.Opportunities.Knowledge.Store;
using Xunit;

namespace Kor.Opportunities.Knowledge.Tests;

/// <summary>
/// Covers synthetic EPUB spine ordering/refs, bounded chunks, the 18 verbatim stems
/// supplied by Ian, reader routing, real local embedding bytes/ranking, SQL source
/// write boundaries and original-file hashing with a fake converter/store.
/// Also checks dry/skip/claim guards, index caching/source caps, claim JSON/validation,
/// import provenance, stamp/force decisions and permitted claim/concept SQL writes.
/// Does not read the Books folder, connect to SQL, invoke Calibre, prove PDF fidelity,
/// SQL validity/rollback/concurrency, model content propagation or real-book coverage.
/// Same-class miss: dropping an untested EPUB spine/markup variant can pass this fixture;
/// the other gates alone cannot detect a reader silently dropping a spine item.
/// Embedding gates need the model: the referenced library's first build downloads 33 MB
/// and copies its content into the test output. No remote inference is performed.
/// </summary>
public sealed class KnowledgeIngestRegressionTests
{
    [Fact]
    public async Task EpubReader_ReadsTheSpineInOrder_AndRefsEachDocument()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "synthetic.epub");
        WriteEpub(path);
        var chunks = await new EpubReader().ReadAsync(path);
        Check(chunks.Count == 3, "three spine documents", chunks.Count.ToString());
        Check(chunks.Select(c => c.Ref).SequenceEqual(new[] { "Third chapter", "First chapter", "Second chapter" }),
            "headings in spine order (3,1,2)", string.Join(" | ", chunks.Select(c => c.Ref)));
        Check(chunks.Select(c => c.Text).SequenceEqual(new[] { "Third body.", "First body.", "Second body." }),
            "body text from every spine document", string.Join(" | ", chunks.Select(c => c.Text)));
    }

    [Fact]
    public void Chunking_NeverExceeds1500Chars_AndPrefersParagraphBoundaries()
    {
        var paragraphs = Enumerable.Range(0, 20)
            .Select(i => $"Paragraph {i:00}: " + string.Join(" ", Enumerable.Repeat("word", 98))).ToArray();
        var text = string.Join("\n\n", paragraphs);
        Check(text.Length >= 10000, "at least 10,000 characters", text.Length.ToString());
        var chunks = Chunking.Split(text).ToArray();
        Check(Chunking.MaxChars == 1500 && chunks.All(c => c.Length <= 1500), "1500-character bound", string.Join(",", chunks.Select(c => c.Length)));
        Check(chunks.All(c => c.StartsWith("Paragraph ", StringComparison.Ordinal)), "chunks begin at whole paragraphs", string.Join(" | ", chunks.Select(c => c[..Math.Min(20, c.Length)])));
        Check(string.Join("\n\n", chunks) == text, "complete normalized text preserved", "joined chunks differ");
        var sentence = string.Join(" ", Enumerable.Repeat("unbroken", 600));
        var wordChunks = Chunking.Split(sentence).ToArray();
        Check(wordChunks.All(c => c.Length <= 1500 && c.Split(' ').All(w => w == "unbroken")),
            "long sentence splits between words", string.Join(",", wordChunks.Select(c => c.Length)));
        Check(string.Join(" ", wordChunks) == sentence, "all long-sentence words preserved", "joined chunks differ");
        var token = new string('x', 3001);
        Check(string.Concat(Chunking.Split(token)) == token && Chunking.Split(token).All(c => c.Length <= 1500),
            "unbreakable token preserved within hard bound", "token split differs");
    }

    // These 18 stems were supplied verbatim in the conversation, not read from Books.
    [Theory]
    [InlineData("Good strategy, bad strategy _ the difference and why it -- Richard Rumelt [Rumelt, Richard]", "Good strategy, bad strategy _ the difference and why it", "Richard Rumelt", null)]
    [InlineData("Let’s Get Real or Let’s Not Play_ Transforming the -- Khalsa, Mahan; Illig, Randy -- 2008", "Let’s Get Real or Let’s Not Play: Transforming the", "Khalsa, Mahan; Illig, Randy", 2008)]
    [InlineData("Major Account Sales Strategy -- Rackham, Neil -- 2009", "Major Account Sales Strategy", "Rackham, Neil", 2009)]
    [InlineData("Managing the professional service firm -- Maister, David", "Managing the professional service firm", "Maister, David", null)]
    [InlineData("Marketing Handbook for the Design & Construction -- William D_ Mahoney", "Marketing Handbook for the Design & Construction", "William D. Mahoney", null)]
    [InlineData("Never Eat Alone _ And Other Secrets to Success, One", "Never Eat Alone _ And Other Secrets to Success, One", null, null)]
    [InlineData("Never Split the Difference_ Negotiating As If Your Life -- Voss, Chris -- 2016", "Never Split the Difference: Negotiating As If Your Life", "Voss, Chris", 2016)]
    [InlineData("Playing to Win_ How Strategy Really Works -- A_G_ Lafley [Lafley, A_G_]", "Playing to Win: How Strategy Really Works", "A.G. Lafley", null)]
    [InlineData("Rain making_ attracting new clients no matter what your -- Ford Harding -- 2007 -- Adams Media", "Rain making: attracting new clients no matter what your", "Ford Harding", 2007)]
    [InlineData("SPIN Selling_ Situation Problem Implication Need-payoff -- Neil Rackham [Rackham, Neil]", "SPIN Selling: Situation Problem Implication Need-payoff", "Neil Rackham", null)]
    [InlineData("Shipley Associates Proposal Guide -- Newman, Larry", "Shipley Associates Proposal Guide", "Newman, Larry", null)]
    [InlineData("Superforecasting _ The Art and Science of Prediction -- Philip E_ Tetlock & Dan Gardner", "Superforecasting _ The Art and Science of Prediction", "Philip E. Tetlock & Dan Gardner", null)]
    [InlineData("The Challenger Sale -- Matthew Dixon -- 2017", "The Challenger Sale", "Matthew Dixon", 2017)]
    [InlineData("The SPIN® Selling Fieldbook_ Practical Tools, Methods, -- Neil Rackham -- 1st edition, New York, 1996", "The SPIN® Selling Fieldbook: Practical Tools, Methods,", "Neil Rackham", 1996)]
    [InlineData("The Trusted Advisor -- David H_ Maister, Robert M_ Galford, Charles H_ Green -- 2001 -- Free Press", "The Trusted Advisor", "David H. Maister, Robert M. Galford, Charles H. Green", 2001)]
    [InlineData("The Trusted Advisor Fieldbook _ A Comprehensive Toolkit for -- Charles H_ Green and Andrea P_ Howe -- 1st edition, 2011", "The Trusted Advisor Fieldbook _ A Comprehensive Toolkit for", "Charles H. Green and Andrea P. Howe", 2011)]
    [InlineData("The Trusted Advisor Sales Engineer -- John Care, (Managing director of Mastering Technical Sales -- 2016 -- Mastering Technical Sales", "The Trusted Advisor Sales Engineer", "John Care, (Managing director of Mastering Technical Sales", 2016)]
    [InlineData("The win without pitching manifesto -- Blair Enns", "The win without pitching manifesto", "Blair Enns", null)]
    public void FileNameParser_ReadsIansNames(string stem, string title, string? author, int? year)
    {
        var actual = FileNameParser.ParseStem(stem);
        Check(actual.Title == title && actual.Author == author && actual.Year == year,
            $"{title} / {author ?? "(none)"} / {year?.ToString() ?? "(none)"}", actual.ToString());
    }

    [Fact]
    public void FileNameParser_DoesNotInventCorrectionsOrUseTitleYears()
    {
        var result = FileNameParser.ParseStem("A_ Title_Inside 2001 -- Some_Name [sorted] -- edition 1899, 2012, 2015");
        Check(result == new FileNameMetadata("A: Title_Inside 2001", "Some_Name", 2012),
            "only the specified colon/author/year rules", result.ToString());
    }

    [Fact]
    public void ReaderPicker_RoutesEveryExtension_AndRefusesUnknown()
    {
        var picker = new ReaderPicker();
        var expected = new Dictionary<string, Type>
        {
            [".epub"] = typeof(EpubReader), [".pdf"] = typeof(PdfTextReader),
            [".html"] = typeof(HtmlReader), [".htm"] = typeof(HtmlReader),
            [".txt"] = typeof(Kor.Opportunities.Knowledge.Readers.TextReader), [".md"] = typeof(Kor.Opportunities.Knowledge.Readers.TextReader),
            [".mobi"] = typeof(CalibreConverter), [".azw3"] = typeof(CalibreConverter),
        };
        Check(ReaderPicker.Extensions.Order().SequenceEqual(expected.Keys.Order()), "all eight extension spellings", string.Join(",", ReaderPicker.Extensions));
        foreach (var (extension, type) in expected)
            Check(picker.Pick("file" + extension.ToUpperInvariant()).GetType() == type, type.Name, extension);
        var exception = Assert.Throws<NotSupportedException>(() => picker.Pick("file.docx"));
        Check(exception.Message.Contains(".docx", StringComparison.Ordinal), "unknown extension named", exception.Message);
    }

    [Fact]
    public void Embedder_Returns384_AndBytesRoundTrip()
    {
        using var embedder = new KnowledgeEmbedder();
        var floats = embedder.Embed("Ask a satisfied client for an introduction.");
        var bytes = KnowledgeEmbedder.ToBytes(floats);
        var restored = KnowledgeEmbedder.FromBytes(bytes);
        Check(floats.Length == 384 && bytes.Length == 1536, "384 floats / 1536 bytes", $"{floats.Length} / {bytes.Length}");
        Check(floats.Select(BitConverter.SingleToInt32Bits).SequenceEqual(restored.Select(BitConverter.SingleToInt32Bits)),
            "bit-exact float round trip", "bytes differ");
        var known = new float[384]; known[0] = 1f;
        Check(KnowledgeEmbedder.ToBytes(known).Take(4).SequenceEqual(new byte[] { 0, 0, 128, 63 }),
            "IEEE float32 little-endian", Convert.ToHexString(KnowledgeEmbedder.ToBytes(known)[..4]));
        Assert.Throws<ArgumentException>(() => KnowledgeEmbedder.ToBytes(new float[383]));
        Assert.Throws<ArgumentException>(() => KnowledgeEmbedder.FromBytes(new byte[1535]));
    }

    [Fact]
    public async Task Search_PutsTheParaphraseFirst()
    {
        using var embedder = new KnowledgeEmbedder();
        var store = new MemoryStore();
        var texts = new[]
        {
            "Ask satisfied clients to introduce you to colleagues who could benefit from your professional services.",
            "Set professional fees to reflect the value delivered to the client rather than charging only for hours worked.",
            "Seismic bracing restrains building components against earthquake forces and transfers lateral loads to the structure.",
        };
        for (var i = 0; i < texts.Length; i++) store.Add(i + 1, i + 1, texts[i], embedder.Embed(texts[i]));
        using var search = new KnowledgeSearch(store, embedder);
        var hits = await search.SearchAsync("How can I get a happy customer to connect me with potential new clients?", take: 3, minScore: -1);
        Check(hits.Count == 3 && hits[0].Passage.Id == 1 && hits[^1].Passage.Id == 3,
            "referrals first, seismic bracing last", string.Join(", ", hits.Select(h => $"{h.Passage.Id}:{h.Score:0.000}")));
    }

    [Fact]
    public async Task Search_LoadsVectorsOnce_AndCapsEachSource()
    {
        var store = new MemoryStore();
        var embedder = new CountingEmbedder();
        var vector = embedder.Embed("fixture");
        store.Add(1, 1, "first", vector);
        store.Add(2, 1, "second", vector);
        store.Add(3, 1, "third", vector);
        store.Add(4, 2, "fourth", vector);
        using var search = new KnowledgeSearch(store, embedder);
        var hits = await search.SearchAsync("query", take: 4);
        await search.SearchAsync("query again", take: 4);
        Check(store.VectorReads == 1, "one vector load per instance", store.VectorReads.ToString());
        Check(hits.Select(h => h.Passage.Id).SequenceEqual(new long[] { 1, 2, 4 }),
            "two per source without starving other sources", string.Join(",", hits.Select(h => h.Passage.Id)));
    }

    [Fact]
    public void Store_StillWritesOnlyKnowledgeTables_AndNeverDeletes()
    {
        var source = Regex.Replace(Source("Kor.Opportunities.Knowledge/Store/SqlKnowledgeStore.cs"),
            @"//[^\r\n]*|/\*.*?\*/", "", RegexOptions.Singleline);
        var writes = Regex.Matches(source, @"\b(?:INSERT\s+INTO|UPDATE)\s+([#\w.]+)", RegexOptions.IgnoreCase);
        Check(writes.Count == 9, "three original writes plus six claim import writes", writes.Count.ToString());
        foreach (Match match in writes)
            Check(match.Groups[1].Value is "knowledge.Source" or "knowledge.Passage" or "knowledge.Concept"
                or "knowledge.Claim" or "knowledge.ClaimConcept" or "knowledge.ClaimImport", "only knowledge table writes", match.Value);
        var inserts = Regex.Matches(source, @"\bINSERT\s+INTO\s+(\w+\.\w+)", RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value).ToArray();
        Check(inserts.Order().SequenceEqual(new[] { "knowledge.Source", "knowledge.Concept", "knowledge.Claim", "knowledge.ClaimConcept", "knowledge.ClaimImport" }.Order()),
            "existing Source insert plus exactly the four import tables", string.Join(",", inserts));
        var destinations = Regex.Matches(source, "DestinationTableName\\s*=\\s*\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToArray();
        // Piece 3 requires temp staging; the brief's blanket bulk-destination rule
        // cannot forbid that staging table and also require the repair implementation.
        Check(destinations.SequenceEqual(new[] { "knowledge.Passage", "#KnowledgeEmbeddings" }),
            "one permanent passage destination and one local repair staging destination", string.Join(",", destinations));
        Check(Regex.Matches(source, @"\bDELETE\b", RegexOptions.IgnoreCase).Count == 1,
            "exactly one deletion statement", "unexpected deletion count");
        const string deletion = "DELETE FROM knowledge.Passage WHERE SourceId = @sourceId;";
        Check(source.Contains(deletion, StringComparison.Ordinal), "deletion scoped to SourceId", "missing scoped statement");
        var force = source.IndexOf("if (force && existingId", StringComparison.Ordinal);
        var guard = source.IndexOf("FROM knowledge.Claim WITH (HOLDLOCK)", StringComparison.Ordinal);
        var remove = source.IndexOf(deletion, StringComparison.Ordinal);
        Check(force >= 0 && guard > force && remove > guard && source.Contains("has claims; --force is refused.", StringComparison.Ordinal),
            "force branch and locked claim guard precede deletion", $"{force}/{guard}/{remove}");
        Check(!Regex.IsMatch(source, @"\b(?:DROP|TRUNCATE)\b", RegexOptions.IgnoreCase),
            "no destructive DDL (the sole deletion remains 9c's guarded passage replacement)", "forbidden statement found");
        Check(source.Contains("WHERE SourceId = @sourceId AND ReviewedAtUtc IS NULL AND RetiredAtUtc IS NULL", StringComparison.Ordinal),
            "force retirement excludes reviewed and already retired claims", "missing guard");
        Check(source.Contains("SqlDbType", StringComparison.Ordinal) && !source.Contains("AddWithValue", StringComparison.Ordinal),
            "typed SQL parameters", "parameter pattern differs");
        Check(source.Contains("IsolationLevel.Serializable", StringComparison.Ordinal)
            && source.Contains("UPDLOCK, HOLDLOCK", StringComparison.Ordinal)
            && source.Contains("SqlBulkCopyOptions.CheckConstraints, tx", StringComparison.Ordinal)
            && source.Contains("await tx.CommitAsync(ct)", StringComparison.Ordinal),
            "locked hash and transactional bulk copy", "transaction contract missing");
        Check(source.Contains("embeddings.Count > 500", StringComparison.Ordinal)
            && source.Contains("WHERE knowledge.Passage.Embedding IS NULL", StringComparison.Ordinal),
            "bounded NULL-only repair", "repair contract missing");
    }

    [Theory]
    [InlineData(".mobi")]
    [InlineData(".azw3")]
    public async Task Ingest_HashesTheOriginal_NotTheConvertedFile(string extension)
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "Synthetic -- Author -- 2001" + extension);
        var original = Encoding.UTF8.GetBytes("Original synthetic MOBI bytes, not an EPUB.");
        await File.WriteAllBytesAsync(path, original);
        var converter = new FakeConverter();
        var store = new MemoryStore();
        var embedder = new CountingEmbedder();
        var ingestor = new KnowledgeIngestor(new ReaderPicker(converter: converter), store, embedder);
        var result = await ingestor.IngestAsync(path, new KnowledgeIngestOptions(By: "gate"));
        var captured = store.LastSource;
        Check(captured is not null && captured.Sha256 == Convert.ToHexString(SHA256.HashData(original))
            && captured.Sha256 != converter.ConvertedSha, "SHA-256 of original purchased file bytes", captured?.Sha256 ?? "no source");
        Check(captured?.Origin == Path.GetFileName(path), "original filename as origin", captured?.Origin ?? "none");
        Check(result.Passages == 3 && embedder.Calls == 3 && store.IngestCalls == 1,
            "each passage embedded before one store ingest", $"{result.Passages}/{embedder.Calls}/{store.IngestCalls}");
        Check(converter.OutputPath is not null && !File.Exists(converter.OutputPath), "temporary EPUB deleted", converter.OutputPath ?? "not converted");
    }

    [Fact]
    public async Task Converter_RemovesTemporaryFile_WhenEpubReadingFails()
    {
        var converter = new InvalidEpubConverter();
        await Assert.ThrowsAsync<InvalidDataException>(() => new CalibreConverter(converter: converter)
            .ReadAsync("synthetic.mobi"));
        Check(converter.OutputPath is not null && !File.Exists(converter.OutputPath),
            "temporary conversion removed after reader failure", converter.OutputPath ?? "not converted");
    }

    [Fact]
    public async Task Ingest_DryAndSkipAvoidEmbeddings_AndClaimsRefuseForce()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "Synthetic.txt");
        await File.WriteAllTextAsync(path, "One synthetic paragraph.");
        var store = new MemoryStore();
        var embedder = new CountingEmbedder();
        var ingestor = new KnowledgeIngestor(new ReaderPicker(), store, embedder);
        var dry = await ingestor.IngestAsync(path, new KnowledgeIngestOptions(Dry: true));
        Check(dry.Passages == 1 && store.Lookups == 0 && store.IngestCalls == 0 && embedder.Calls == 0,
            "dry reads only, without store or embedder calls", $"{store.Lookups}/{store.IngestCalls}/{embedder.Calls}");
        var sha = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        store.Sources.Add(ExampleSource(1) with { Sha256 = sha, Passages = 1, Claims = 1 });
        var skipped = await ingestor.IngestAsync(path, new KnowledgeIngestOptions());
        Check(skipped.Skipped && embedder.Calls == 0 && store.IngestCalls == 0, "existing hash skipped before embedding", skipped.ToString());
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => ingestor.IngestAsync(path, new KnowledgeIngestOptions(Force: true)));
        Check(refusal.Message.Contains("has claims", StringComparison.Ordinal) && store.IngestCalls == 0,
            "force refuses claimed source before writes", refusal.Message);
    }

    [Fact]
    public void LicenceAndKindConstants_MatchMigration349()
    {
        var sql = Source("Kor.Opportunities.Data/Schema/349_KnowledgeStore.sql");
        foreach (var (column, values) in new[] { ("Licence", KnowledgeLicences.All), ("Kind", KnowledgeKinds.All) })
        {
            var match = Regex.Match(sql, @"CK_Knowledge_Source_" + column + @"\s+CHECK\s*\(" + column + @"\s+IN\s*\(([^)]*)\)", RegexOptions.Singleline);
            var actual = Regex.Matches(match.Groups[1].Value, "N'([^']+)'").Select(m => m.Groups[1].Value).Order().ToArray();
            Check(match.Success && actual.SequenceEqual(values.Order()), column + " values match migration 349", string.Join(",", actual));
        }
    }

    [Fact]
    public void ClaimFile_RoundTrips_AndTextIsOptionalOnImport()
    {
        var original = ExampleClaimFile();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(original, ClaimFile.JsonOptions);
        var restored = ClaimFile.Parse(bytes, "synthetic.claims.json");
        var json = Encoding.UTF8.GetString(bytes);
        Check(restored.SourceId == 18 && restored.WrittenBy == "synthetic writer"
            && restored.Passages[0].Claims[0].Statement == original.Passages[0].Claims[0].Statement,
            "claim document round trip", json);
        Check(restored.Passages[0].Text is null && !json.Contains("\"text\"", StringComparison.Ordinal),
            "omitted text accepted", json);
        Check(restored.FileSha256 == Convert.ToHexString(SHA256.HashData(bytes)) && restored.FileName == "synthetic.claims.json",
            "SHA of original file bytes and filename", restored.FileSha256 ?? "missing");
        var bomBytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(bytes).ToArray();
        var fromEditor = ClaimFile.Parse(bomBytes, "editor.claims.json");
        Check(fromEditor.SourceId == 18 && fromEditor.FileSha256 == Convert.ToHexString(SHA256.HashData(bomBytes)),
            "UTF-8 BOM accepted and included in provenance hash", fromEditor.FileSha256 ?? "missing");
        Check(!json.Contains("fileSha256", StringComparison.Ordinal) && !json.Contains("rejections", StringComparison.Ordinal),
            "provenance and diagnostics excluded from JSON", json);
        var withText = original with { Passages = new[] { original.Passages[0] with { Text = "Synthetic fixture passage." }, original.Passages[1] } };
        Check(ClaimFile.Parse(JsonSerializer.SerializeToUtf8Bytes(withText, ClaimFile.JsonOptions), "export.json").Passages[0].Text == "Synthetic fixture passage.",
            "export text survives", "text changed");
        Assert.Throws<JsonException>(() => ClaimFile.Parse(Encoding.UTF8.GetBytes("""{"passages":[{"ordinal":1,"claims":[]}]}"""), "bad.json"));
        Assert.Throws<JsonException>(() => ClaimFile.Parse(Encoding.UTF8.GetBytes("""{"passages":[{"id":1,"claims":[]}]}"""), "bad.json"));
        Assert.Throws<JsonException>(() => ClaimFile.Parse(Encoding.UTF8.GetBytes("""{"passages":[{"id":1,"ordinal":1}]}"""), "bad.json"));
    }

    [Fact]
    public void ClaimValidation_RejectsEachBadShape_WithAReason()
    {
        var valid = ExampleClaim();
        var bad = new[]
        {
            valid with { Kind = "Advice" }, valid with { Statement = new string('a', 1200) },
            valid with { Statement = "One line.\nAnother line." }, valid with { Confidence = 1.4m },
            valid with { Concepts = Array.Empty<ClaimFileConcept>() },
            valid with { Concepts = Enumerable.Range(1, 4).Select(i => new ClaimFileConcept("concept-" + i, "Concept " + i)).ToArray() },
            valid with { Concepts = new[] { new ClaimFileConcept("!!!", "Bad slug") } },
        };
        var file = ExampleClaimFile() with { Passages = new[]
        {
            new ClaimFilePassage { Id = 1, Ordinal = 1, Claims = bad.Append(valid).ToArray() },
            new ClaimFilePassage { Id = 99, Ordinal = 1, Claims = new[] { valid } },
        } };
        var result = ClaimValidation.Validate(file, new[] { new StoredClaimPassage(1, 18, 1, null), new StoredClaimPassage(99, 17, 1, null) });
        Check(result.Accepted.Count == 1 && result.Rejected.Count == 8, "one accepted and eight rejected", $"{result.Accepted.Count}/{result.Rejected.Count}");
        var expected = new[] { "kind", "1–1,000", "line break", "confidence", "1–3", "1–3", "slug", "does not belong" };
        for (var i = 0; i < expected.Length; i++)
            Check(result.Rejected[i].Reason.Contains(expected[i], StringComparison.Ordinal), expected[i], result.Rejected[i].Reason);
        Check(result.FileErrors.Count == 1, "wrong-source passage also refuses the file before stamping", string.Join("; ", result.FileErrors));
    }

    [Fact]
    public void Slug_IsNormalised()
    {
        Check(ClaimValidation.NormalizeSlug("Qualify Before Investing!") == "qualify-before-investing", "normalized slug", "normalization differs");
        var file = ExampleClaimFile();
        var stored = new[] { new StoredClaimPassage(1, 18, 1, null), new StoredClaimPassage(2, 18, 2, null) };
        var accepted = ClaimValidation.Validate(file, stored).Accepted.Single();
        Check(accepted.Claim.Concepts.Single().Slug == "qualify-before-investing", "normalized accepted concept", accepted.Claim.Concepts.Single().Slug);
        var shortSlug = file with { Passages = new[] { file.Passages[0] with { Claims = new[]
            { ExampleClaim() with { Concepts = new[] { new ClaimFileConcept("A!", "Too short") } } } } } };
        Check(ClaimValidation.Validate(shortSlug, stored).Rejected.Single().Reason.Contains("3–80", StringComparison.Ordinal),
            "too-short slug rejected", "wrong reason");
    }

    [Fact]
    public void ClaimValidation_RefusesInvalidCoverage_AndRequiresWriter()
    {
        var file = ExampleClaimFile();
        var stored = new[] { new StoredClaimPassage(1, 18, 1, null), new StoredClaimPassage(2, 18, 2, null) };
        foreach (var invalid in new[]
        {
            file with { WrittenBy = null }, file with { Passages = Array.Empty<ClaimFilePassage>() },
            file with { Passages = new[] { file.Passages[0], file.Passages[0] } },
            file with { Passages = new[] { file.Passages[0] with { Ordinal = 2 } } },
            file with { Passages = new[] { file.Passages[0] with { Claims = null! } } },
        }) Assert.Throws<InvalidDataException>(() => ClaimValidation.PrepareImport(invalid, stored, force: true));
    }

    [Fact]
    public async Task Import_RefusesStampedPassages_UnlessForce_AndForceRetiresOnlyUnreviewedModelClaims()
    {
        var source = Source("Kor.Opportunities.Knowledge/Store/SqlKnowledgeStore.cs");
        var import = source[source.IndexOf("public async Task<(int PassagesCovered", StringComparison.Ordinal)..];
        foreach (var clause in new[] { "IsolationLevel.Serializable", "UPDLOCK, HOLDLOCK", "ClaimValidation.PrepareImport(file, stored, force)",
                     "if (plan.SupersedeUnreviewedModelClaims)", "ReviewedAtUtc IS NULL AND RetiredAtUtc IS NULL",
                     "link.LinkedBy = N'Model'", "AND NOT EXISTS", "link.LinkedBy = N'Human'",
                     "SELECT @id = Id FROM knowledge.Concept WITH (UPDLOCK, HOLDLOCK) WHERE Slug = @slug;", "IF @id IS NULL",
                     "file.WrittenBy", "file.FileSha256", "ClaimsExtractedBy = @writer", "file.Passages.Chunk(500)", "await tx.CommitAsync(ct)" })
            Check(import.Contains(clause, StringComparison.Ordinal), clause, "missing from import source");
        Check(!Regex.IsMatch(import, @"\bDELETE\b", RegexOptions.IgnoreCase), "no deletion in claims import", "found deletion");
        Check(import.IndexOf("ClaimValidation.PrepareImport", StringComparison.Ordinal) < import.IndexOf("UPDATE knowledge.Claim", StringComparison.Ordinal),
            "validation before retirement", "wrong order");
        var store = new MemoryStore();
        var stamp = DateTimeOffset.UnixEpoch;
        store.ClaimPassages.AddRange(new[] { new StoredClaimPassage(1, 18, 1, stamp), new StoredClaimPassage(2, 18, 2, null), new StoredClaimPassage(3, 18, 3, null) });
        store.ClaimRows.AddRange(new[]
        {
            new MemoryClaim(18, 1, false, false, new[] { "Model" }),
            new MemoryClaim(18, 1, true, false, new[] { "Model" }),
            new MemoryClaim(18, 1, true, true, new[] { "Model" }, "Ian's reason"),
            new MemoryClaim(18, 1, false, false, new[] { "Human" }),
            new MemoryClaim(18, 1, false, false, Array.Empty<string>()),
            new MemoryClaim(17, 99, false, false, new[] { "Model" }),
            new MemoryClaim(18, 1, false, false, new[] { "Model", "Human" }),
        });
        var original = store.ClaimRows.ToArray();
        var file = ExampleClaimFile();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ImportClaimsAsync(file, false, "importer"));
        Check(store.ClaimImports == 0 && store.ClaimRows.SequenceEqual(original), "refusal before any fake writes", "fake state changed");
        var result = await store.ImportClaimsAsync(file, true, "importer");
        Check(result == (2, 1, 0) && store.ClaimImports == 1, "two covered including empty / one claim / no rejects", result.ToString());
        Check(store.ClaimRows[0].Retired && store.ClaimRows[0].Reason == "Superseded by re-import", "active unreviewed model claim retired", store.ClaimRows[0].ToString());
        Check(store.ClaimRows.Skip(1).Take(6).SequenceEqual(original.Skip(1)), "reviewed, retired, human, unlinked, mixed and other-source claims preserved", "rows changed");
        Check(store.ClaimPassages[1].ClaimsExtractedAtUtc is not null && store.ClaimPassages[2].ClaimsExtractedAtUtc is null,
            "empty passage stamped; omitted passage untouched", "wrong coverage");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ImportClaimsAsync(file, false, "importer"));
        var fresh = new MemoryStore();
        fresh.ClaimPassages.AddRange(store.ClaimPassages.Select(p => p with { ClaimsExtractedAtUtc = null }));
        Check(await fresh.ImportClaimsAsync(file, false, "importer") == (2, 1, 0), "unstamped file imports without force", "wrong counts");
        var rejectedOnly = new MemoryStore();
        rejectedOnly.ClaimPassages.Add(new StoredClaimPassage(1, 18, 1, null));
        var badFile = file with { Passages = new[] { file.Passages[0] with { Claims = new[] { ExampleClaim() with { Kind = "Advice" } } } } };
        Check(await rejectedOnly.ImportClaimsAsync(badFile, false, "importer") == (1, 0, 1)
            && rejectedOnly.ClaimPassages[0].ClaimsExtractedAtUtc is not null,
            "rejected-only passage covered with rejection reason", string.Join("; ", badFile.Rejections.Select(r => r.Reason)));
    }

    private static ClaimFileClaim ExampleClaim() => new() { Kind = "Rule", Statement = "Confirm a need before preparing a proposal.",
        Confidence = 0.9m, Concepts = new[] { new ClaimFileConcept("Qualify Before Investing!", "Qualify before investing") } };
    private static ClaimFile ExampleClaimFile() => new() { SourceId = 18, Title = "Synthetic book", WrittenBy = "synthetic writer",
        KnownConcepts = new[] { new ClaimFileConcept("qualify-before-investing", "Qualify before investing") },
        Passages = new[] { new ClaimFilePassage { Id = 1, Ordinal = 1, Claims = new[] { ExampleClaim() } }, new ClaimFilePassage { Id = 2, Ordinal = 2 } } };
    private sealed record MemoryClaim(long SourceId, long PassageId, bool Reviewed, bool Retired, IReadOnlyList<string> LinkedBy, string? Reason = null);

    private static KnowledgeSource ExampleSource(long id) => new(id, KnowledgeKinds.Book, "Source " + id,
        null, null, "synthetic.txt", new string('A', 64), KnowledgeLicences.OwnedCopyPrivateStudy,
        KnowledgeEmbedder.ModelName, 0, 0, DateTimeOffset.UnixEpoch, "gate", 0);

    private sealed class CountingEmbedder : IKnowledgeEmbedder
    {
        public int Calls { get; private set; }
        public float[] Embed(string text) { Calls++; var vector = new float[384]; vector[0] = 1; return vector; }
    }

    private sealed class FakeConverter : IEpubConverter
    {
        internal string? OutputPath { get; private set; }
        internal string? ConvertedSha { get; private set; }
        public Task ConvertAsync(string originalPath, string epubPath, CancellationToken ct)
        {
            OutputPath = epubPath;
            WriteEpub(epubPath);
            ConvertedSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(epubPath)));
            return Task.CompletedTask;
        }
    }

    private sealed class InvalidEpubConverter : IEpubConverter
    {
        internal string? OutputPath { get; private set; }
        public async Task ConvertAsync(string originalPath, string epubPath, CancellationToken ct)
        {
            OutputPath = epubPath;
            await File.WriteAllTextAsync(epubPath, "This synthetic file is not a ZIP archive.", ct);
        }
    }

    private sealed class MemoryStore : IKnowledgeStore
    {
        internal List<StoredClaimPassage> ClaimPassages { get; } = new();
        internal List<MemoryClaim> ClaimRows { get; } = new();
        internal int ClaimImports { get; private set; }
        public Task<ClaimFile> ExportAsync(long sourceId, CancellationToken ct = default)
        {
            var source = Sources.Single(s => s.Id == sourceId);
            return Task.FromResult(new ClaimFile { SourceId = sourceId, Title = source.Title, Author = source.Author,
                Passages = _rows.Where(r => r.Passage.SourceId == sourceId).OrderBy(r => r.Passage.Ordinal)
                    .Select(r => new ClaimFilePassage { Id = r.Passage.Id, Ordinal = r.Passage.Ordinal, Ref = r.Passage.Ref, Text = r.Passage.Text }).ToArray() });
        }
        public Task<(int PassagesCovered, int ClaimsWritten, int ClaimsRejected)> ImportClaimsAsync(ClaimFile file, bool force, string by, CancellationToken ct = default)
        {
            // The shared production decision runs before any fake mutation. SQL predicates are gated separately.
            var plan = ClaimValidation.PrepareImport(file, ClaimPassages, force);
            if (plan.SupersedeUnreviewedModelClaims)
                for (var i = 0; i < ClaimRows.Count; i++)
                {
                    var row = ClaimRows[i];
                    if (row.SourceId == file.SourceId && !row.Reviewed && !row.Retired && row.LinkedBy.Contains("Model") && !row.LinkedBy.Contains("Human"))
                        ClaimRows[i] = row with { Retired = true, Reason = "Superseded by re-import" };
                }
            foreach (var claim in plan.Validation.Accepted) ClaimRows.Add(new(file.SourceId, claim.PassageId, false, false, new[] { "Model" }));
            for (var i = 0; i < ClaimPassages.Count; i++)
                if (file.Passages.Any(p => p.Id == ClaimPassages[i].Id)) ClaimPassages[i] = ClaimPassages[i] with { ClaimsExtractedAtUtc = DateTimeOffset.UtcNow };
            ClaimImports++;
            return Task.FromResult((file.Passages.Count, plan.Validation.Accepted.Count, plan.Validation.Rejected.Count));
        }
        internal List<KnowledgeSource> Sources { get; } = new();
        private readonly List<(KnowledgePassage Passage, byte[]? Embedding)> _rows = new();
        internal KnowledgeSource? LastSource { get; private set; }
        internal int Lookups { get; private set; }
        internal int IngestCalls { get; private set; }
        internal int VectorReads { get; private set; }
        internal void Add(long id, long sourceId, string text, float[] vector)
        {
            if (Sources.All(s => s.Id != sourceId)) Sources.Add(ExampleSource(sourceId));
            _rows.Add((new KnowledgePassage(id, sourceId, 1, "synthetic", text, TokenEstimate.Of(text)), KnowledgeEmbedder.ToBytes(vector)));
        }
        public Task<KnowledgeSource?> FindBySha256Async(string sha256, CancellationToken ct = default)
        {
            Lookups++;
            return Task.FromResult(Sources.FirstOrDefault(s => s.Sha256 == sha256));
        }
        public Task<IReadOnlyList<KnowledgeSource>> ListSourcesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<KnowledgeSource>>(Sources);
        public Task<long> IngestAsync(KnowledgeSource source, IReadOnlyList<(KnowledgePassage Passage, byte[]? Embedding)> passages, bool force, CancellationToken ct = default)
        {
            LastSource = source; IngestCalls++;
            _rows.AddRange(passages);
            return Task.FromResult(1L);
        }
        public Task<IReadOnlyList<KnowledgePassage>> ListUnembeddedAsync(long? sourceId = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<KnowledgePassage>>(_rows.Where(r => r.Embedding is null && (sourceId is null || r.Passage.SourceId == sourceId)).Select(r => r.Passage).ToArray());
        public Task SetEmbeddingsAsync(IReadOnlyList<(long PassageId, byte[] Embedding)> embeddings, CancellationToken ct = default)
            => throw new NotSupportedException("This fixture does not emulate SQL repair.");
        public Task<IReadOnlyList<(long PassageId, long SourceId, float[] Vector)>> LoadVectorsAsync(CancellationToken ct = default)
        {
            VectorReads++;
            return Task.FromResult<IReadOnlyList<(long, long, float[])>>(_rows.Where(r => r.Embedding is not null)
                .Select(r => (r.Passage.Id, r.Passage.SourceId, KnowledgeEmbedder.FromBytes(r.Embedding!))).ToArray());
        }
        public Task<IReadOnlyList<KnowledgePassage>> GetPassagesAsync(IReadOnlyList<long> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<KnowledgePassage>>(_rows.Where(r => ids.Contains(r.Passage.Id)).Select(r => r.Passage).ToArray());
    }

    private static void WriteEpub(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Entry("META-INF/container.xml", """<container><rootfiles><rootfile full-path="OPS/book.opf"/></rootfiles></container>""");
        Entry("OPS/book.opf", """
            <package xmlns:opf="http://www.idpf.org/2007/opf"><manifest>
            <opf:item id="one" href="one.xhtml" media-type="application/xhtml+xml"/>
            <opf:item href="two.xhtml" id="two" media-type="application/xhtml+xml"/>
            <opf:item id="three" href="three.xhtml" media-type="application/xhtml+xml"/>
            </manifest><spine><opf:itemref idref="three"/><opf:itemref idref="one"/><opf:itemref idref="two"/></spine></package>
            """);
        Entry("OPS/one.xhtml", "<html><head><title>Book</title></head><body><h1>First chapter</h1><p>First body.</p></body></html>");
        Entry("OPS/two.xhtml", "<html><head><title>Book</title></head><body><h1>Second chapter</h1><p>Second body.</p></body></html>");
        Entry("OPS/three.xhtml", "<html><head><title>Book</title></head><body><h1>Third chapter</h1><p>Third body.</p></body></html>");
        void Entry(string name, string text)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(text);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kor-knowledge-gate-" + Guid.NewGuid().ToString("N"));
        internal TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
    private static string Source(string relative)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found."), relative));
    }
    private static void Check(bool condition, string expected, string actual)
        => Assert.True(condition, $"EXPECTED {expected}; ACTUAL {actual}");
}
