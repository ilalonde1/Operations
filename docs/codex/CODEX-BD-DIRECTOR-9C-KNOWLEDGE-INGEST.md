# Codex — BD Director 9c: the knowledge store's ingestion, ported from JoeBrain

**DO NOT BUILD. DO NOT RUN TESTS. DO NOT RUN ANY TOOL AGAINST A DATABASE. DO NOT READ OR COPY THE
BOOKS.** Make the edits, add the regression gates as source, and ping when applied. Claude builds,
runs the tests, ingests the books from KOR-1001, embeds and searches, and reports. Ian runs nothing.

## Why this exists

Migration 349 (commit `33a6ea4a`, applied 2026-10-14) created `knowledge.Source`, `knowledge.Passage`
and `knowledge.Claim` in KorOpportunitiesDb. They are empty. Ian has bought 18 business-development
books, held privately in `C:\Users\ilalonde\Desktop\BD\Books` and never entering git. The BD
Director will cite passages from them and, in a later brief, carry rules distilled from them as
claims. This brief builds the one tool that fills the store: a reader for each format, a chunker, a
local embedding for meaning search, and a store that writes the two tables, all **ported from
JoeBrain** (`C:\VIsual Studio Projects\JoeBrain`, a separate personal repo), which already does this
for Joe's study material. Port the code; do not reference the JoeBrain assemblies, and do not touch
the JoeBrain repo.

The port drops what is Joe's: courses, consent, sessions, notes, slides, transcripts, subtitles,
Wikipedia, archive.org, Gutenberg, workbooks, OCR, concepts, the judge. What remains is: a file in,
cited passages out, pinned by hash and licence, embedded locally with no service.

Measured 2026-10-14. Use as given.

| Fact | Value |
|---|---|
| The books (18 files, 87.7 MB) | 11 `.epub`, 3 `.mobi` (Rumelt *Good Strategy Bad Strategy*; Rackham *Major Account Sales Strategy*; Harding *Rain Making*), 1 `.azw3` (Care *The Trusted Advisor Sales Engineer*), 3 `.pdf` (Mahoney *Marketing Handbook for the Design & Construction Professional* 34.8 MB; Newman *Shipley Associates Proposal Guide* 23.8 MB; Tetlock & Gardner *Superforecasting* 3.8 MB) |
| The three PDFs carry a text layer | `pdftotext` produced 1,190,215 / 976,675 / 622,699 bytes of text respectively (measured before that conversion was abandoned) |
| File-name shape (Anna's Archive style) | `<Title> -- <Author> -- <Year> -- <Publisher>.<ext>` with any of the trailing parts absent, and sometimes `<Title> -- <Author> [<Author, Sorted>].<ext>`. Separator is ` -- ` (space, two hyphens, space) |
| JoeBrain readers | `IReader { Task<IReadOnlyList<Chunk>> ReadAsync(string path, CancellationToken ct) }` (`src/JoeBrain.Core/Ingest/Readers.cs` line 11); `Chunk(string? Ref, string Text)` and `Chunking.Split(text, maxChars)` (`Ingest/Chunking.cs`); `EpubReader` (`Ingest/HtmlReaders.cs` line 156: zip → OPF → spine order → each XHTML through `HtmlReader`); `HtmlReader` (line 88); `PdfTextReader(pagesPerChunk = 3)` with `NoTextLayerException` (`Readers.cs` lines 17 and 153, PdfPig) |
| JoeBrain embedding | `SmartComponents.LocalEmbeddings` 0.1.0-preview10148; model `bge-small-en-v1.5` (384 dimensions, MIT) acquired at build by the `AcquireJoeBrainSecondModel` target in `src/JoeBrain.Core/JoeBrain.Core.csproj` lines 25–50 (two `DownloadFile`s from huggingface.co into the package's `.cache`, copied to the output as `LocalEmbeddingsModel\bge-small-en-v1.5\{model.onnx,vocab.txt}`, 33 MB); `new LocalEmbedder("bge-small-en-v1.5")`; `Semantic.WindowChars = 1500`; JoeBrain stores one vector per 1,500-character *window* of a passage (`PassageVector`). **Migration 349 stores one vector per passage**, so passages here are at most 1,500 characters |
| JoeBrain packages that come across | `PdfPig` 0.1.10, `SmartComponents.LocalEmbeddings` 0.1.0-preview10148. Not `Docnet.Core` (OCR rendering), not EF, not `Anthropic`, not `ClosedXML`, not `YamlDotNet` |
| Mobi and AZW3 | No reader exists in JoeBrain (0 references in `Ingest/`). Calibre is installed on KOR-1001: `C:\Program Files\Calibre2\ebook-convert.exe` |
| Tools convention in this repo | `tools/<Name>/<Name>.csproj`: net8.0, `Nullable`/`ImplicitUsings` enable, `RootNamespace Kor.<Name>`, `Microsoft.Data.SqlClient` 6.1.4, `ProjectReference` to `Kor.Opportunities.Data`; connection string from `--db` else env `KOR_OPPORTUNITIES_OPPORTUNITIESDB` (`tools/BdIntegrityCheck/Program.cs` line 29). No solution file holds the tools; each is built by path |
| Typed-parameter write pattern | `Kor.Opportunities.Data/Crm/SqlBdDirectorLedgerStore.cs` (brief 9b) |
| `knowledge` tables | `Kor.Opportunities.Data/Schema/349_KnowledgeStore.sql`. `Source.Sha256 CHAR(64)` upper-case hex, unique; `Source.Licence` CHECKed against eight values; `Passage.Embedding VARBINARY(1536)` = 384 × float32 little-endian; `Passage` unique on `(SourceId, Ordinal)` |

## The decisions, so you build the one thing

- **One library, one CLI, nothing in the App.** New class library `Kor.Opportunities.Knowledge/`
  (net8.0, root of the repo beside `Kor.Opportunities.Data`) holds readers, chunker, embedder, store
  and search, so the MCP server can reference it in a later brief. New console tool
  `tools/BdKnowledgeIngest/` is the only thing that runs now. The App, the Worker and the MCP
  server are not edited.
- **The hash is of the file Ian bought.** `Source.Sha256` is the SHA-256 of the original file, in
  every case, including Mobi and AZW3, which are converted to a temporary ePub for reading and the
  temporary file deleted. `Source.Origin` is the original file name exactly as Ian named it.
- **Licence is a flag, with one default.** `--licence` defaults to `OwnedCopyPrivateStudy`; the eight
  allowed values are constants in the library matching the CHECK in 349. `--kind` defaults to
  `Book`.
- **A passage is one embedding.** Chunk with the ported `Chunking.Split` at **1,500 characters**
  maximum, cutting at paragraph then sentence boundaries as it does. Embed each passage once; store
  the 384 floats little-endian in `Passage.Embedding`. There is no window table.
- **Embedding happens during ingest, and the write is one bulk copy per book.** Ian is on the VPN;
  a per-passage UPDATE is 27,000 round trips. The CLI reads, chunks, embeds every passage in
  memory, then writes the source row and `SqlBulkCopy`s its passages (with embeddings) in one
  transaction, then sets `Passages`, `Tokens` and `EmbeddingModel` on the source. `embed` exists
  only as a repair verb for passages whose `Embedding` is NULL, and it writes in batches of 500
  through a temp table and one UPDATE per batch, never one UPDATE per row.
- **Idempotent by hash.** A file whose SHA-256 already has a source with passages is skipped and
  reported, unless `--force`, which deletes that source's passages (the only DELETE in the tool,
  scoped to one `SourceId`, in the same transaction as the re-insert) and re-reads. Claims are
  never touched by this brief: a source with claims refuses `--force`.
- **Title, author, year come from the file name, overridable.** Split the stem on ` -- `: part 1
  is the title; part 2 the author (strip a trailing ` [ … ]` block); the year is the first 4-digit
  token `19xx`/`20xx` found in parts 3 onward, else none. `--title`, `--author`, `--year` override.
  Replace `_` with `:` only when it sits between a word and a space (`Playing to Win_ How` → `Playing to Win: How`), which is how these names encode a colon.
- **Search is the proof.** `search "<question>"` embeds the question and returns the nearest
  passages, cosine over every stored vector loaded once, at most two per source by default (as
  `Semantic.SearchAsync`'s `perSource`). It prints score, book, ref and the first 200 characters.
- **No model call.** Nothing here talks to Anthropic or any service. The embedder is local ONNX.

## Pieces

### Piece 1 — `Kor.Opportunities.Knowledge/Kor.Opportunities.Knowledge.csproj`

net8.0, `Nullable`/`ImplicitUsings` enable, `RootNamespace Kor.Opportunities.Knowledge`. Packages:
`PdfPig` 0.1.10, `SmartComponents.LocalEmbeddings` 0.1.0-preview10148, `Microsoft.Data.SqlClient`
6.1.4. Copy the model-acquisition target from `JoeBrain.Core.csproj` lines 25–50 verbatim in
behaviour, renaming the properties `KorKnowledgeModel*` and the target `AcquireKorKnowledgeModel`;
keep the two huggingface URLs and the `.cache` path. The model lands in the output as
`LocalEmbeddingsModel\bge-small-en-v1.5\{model.onnx,vocab.txt}`. Add a comment that the first build
downloads 33 MB once.

### Piece 2 — readers and chunker (`Kor.Opportunities.Knowledge/Readers/`)

Port, with namespaces changed and nothing else that is not needed:
- `IReader.cs`: the interface, `Chunk`, `NoTextLayerException`, `NaturalOrder` if `HtmlReader` uses it.
- `Chunking.cs`: `Split(string text, int maxChars = 1500)` and its helpers. **`MaxChars` is 1500.**
- `HtmlReader.cs`, `EpubReader.cs` (from `HtmlReaders.cs`; keep the OPF/spine/manifest regexes and
  the per-document heading-to-ref logic).
- `PdfTextReader.cs` (from `Readers.cs` line 153 onward: page groups of 3, `Ref = "pp. a–b"`, the
  text-layer and unspaced checks that throw `NoTextLayerException`). No `ScannedPdfReader`, no OCR:
  a scan is reported and skipped.
- `TextReader.cs`: a plain `.txt`/`.md` file, paragraphs through `Chunking.Split`, `Ref` null.
- `ReaderPicker.cs`: by extension — `.epub` → `EpubReader`; `.pdf` → `PdfTextReader`;
  `.html`/`.htm` → `HtmlReader`; `.txt`/`.md` → `TextReader`; `.mobi`/`.azw3` → `CalibreConverter`
  (Piece 4) then `EpubReader`; anything else → `NotSupportedException` naming the extension.
- `Tokens.cs`: the token estimate JoeBrain uses for `Chunk`/`Passage.Tokens` (find it where
  `c.Tokens` is computed; port that one function).

### Piece 3 — embedding and store (`Kor.Opportunities.Knowledge/`)

- `Embedding/KnowledgeEmbedder.cs`: `public const string ModelName = "bge-small-en-v1.5"`,
  `public const int Dimensions = 384`; wraps `LocalEmbedder(ModelName)`; `float[] Embed(string)`;
  `static byte[] ToBytes(ReadOnlySpan<float>)` and `static float[] FromBytes(ReadOnlySpan<byte>)`,
  little-endian, asserting 384; `IDisposable`.
- `Store/KnowledgeLicences.cs`, `KnowledgeKinds.cs`: the constant lists matching 349's CHECKs.
- `Store/IKnowledgeStore.cs` + `SqlKnowledgeStore.cs` (`Microsoft.Data.SqlClient`, typed
  parameters, the 9b pattern). Records: `KnowledgeSource(long Id, string Kind, string Title,
  string? Author, short? Year, string Origin, string Sha256, string Licence, string? EmbeddingModel,
  int Passages, int Tokens, DateTimeOffset IngestedAtUtc, string IngestedBy, int Claims)`,
  `KnowledgePassage(long Id, long SourceId, int Ordinal, string? Ref, string Text, int Tokens)`,
  `KnowledgeHit(KnowledgePassage Passage, string SourceTitle, float Score)`. Methods:
  `FindBySha256Async(sha)`, `ListSourcesAsync()` (with the claim count as a correlated COUNT),
  `IngestAsync(KnowledgeSource source, IReadOnlyList<(KnowledgePassage Passage, byte[]? Embedding)> passages, bool force, ct)`
  — one transaction: on `force`, refuse if `knowledge.Claim` has rows for the source, else DELETE
  its passages; INSERT or reuse the source row; `SqlBulkCopy` into `knowledge.Passage` (map
  `SourceId, Ordinal, Ref, Text, Tokens, Embedding`); UPDATE the source's `Passages`, `Tokens`,
  `EmbeddingModel`; return the source id. `ListUnembeddedAsync(sourceId?)`,
  `SetEmbeddingsAsync(IReadOnlyList<(long PassageId, byte[] Embedding)>)` (temp table + one UPDATE
  per call), `LoadVectorsAsync()` → every `(PassageId, SourceId, float[])` with a non-null
  embedding, `GetPassagesAsync(ids)`.
- `Search/KnowledgeSearch.cs`: `SearchAsync(string query, int take = 10, float minScore = 0f,
  int perSource = 2)`: embed the query, cosine against the vectors (loaded once per instance),
  cap per source, return `KnowledgeHit`s with the passages fetched by id.

### Piece 4 — the CLI `tools/BdKnowledgeIngest/`

`BdKnowledgeIngest.csproj` per the tools convention, `ProjectReference` to
`Kor.Opportunities.Knowledge` (not to `Kor.Opportunities.Data`; nothing there is needed).
`Program.cs` with a header comment listing the verbs, as `BdIntegrityCheck` does:

```
ingest <file|folder> [--kind Book] [--licence OwnedCopyPrivateStudy] [--title ..] [--author ..] [--year ..]
                     [--force] [--dry] [--calibre <path to ebook-convert.exe>] [--by <name>]
embed  [--source <id>]
search "<question>" [--take 10] [--per-source 2]
list
--db <connection string>      default: env KOR_OPPORTUNITIES_OPPORTUNITIESDB
```

`ingest` on a folder takes every file whose extension the picker knows, in name order, and
reports each as one line: `OK  <passages> passages  <tokens> tokens  <seconds>s  <file>` or
`SKIP already ingested as source <id>` or `SKIP scan (no text layer)` or `FAIL <reason>`; it never
stops on one file's failure; the last line is `<ok> of <n> ingested`. `--dry` reads and chunks and
prints the counts and the first passage's ref and first line, and writes nothing. `--by` defaults
to `Environment.UserName`. `CalibreConverter.cs` (in the library, `Readers/`): runs
`ebook-convert.exe <in> <tmp>.epub` with the path from `--calibre`, else
`C:\Program Files\Calibre2\ebook-convert.exe`, else fails with a sentence naming the flag; the
temp file goes in `Path.GetTempPath()` and is deleted in a `finally`. `FileNameParser.cs` (library)
implements the title/author/year rule. `search` prints one hit per line:
`0.812  Maister, Managing the Professional Service Firm  ch. 3  "…first 200 chars…"`.

### Piece 5 — gates: `Kor.Opportunities.Knowledge.Tests/` (xunit, net8.0, beside the library)

Rule 11 summary in the class: what is covered, what is not, one same-class miss (a reader that
silently drops a spine item would pass every gate here but the ePub one). Gates:

1. `EpubReader_ReadsTheSpineInOrder_AndRefsEachDocument`: build a minimal ePub in a temp folder
   (container.xml, OPF with three XHTML items whose spine order differs from manifest order,
   each with an `<h1>`), read it, assert chunk order follows the spine and refs carry the headings.
2. `Chunking_NeverExceeds1500Chars_AndPrefersParagraphBoundaries`: a 10,000-character text of
   paragraphs; every chunk ≤ 1500; no chunk starts mid-word; total text preserved.
3. `FileNameParser_ReadsIansNames`: a `[Theory]` over all 18 real stems (verbatim) with the
   expected title, author and year for each. The author is the second part as written, with a
   trailing ` [ … ]` block removed and `_` → `.` only inside an initials pair (`A_G_ Lafley` →
   `A.G. Lafley`, `William D_ Mahoney` → `William D. Mahoney`). The title keeps `_` → `:` only
   where `_` sits between a word and a space (`Playing to Win_ How` → `Playing to Win: How`).
   Do not invent any other correction; state the rule in the parser's summary and test exactly it.
4. `ReaderPicker_RoutesEveryExtension_AndRefusesUnknown`: the six known extensions route as
   specified; `.docx` throws `NotSupportedException` naming `.docx`.
5. `Embedder_Returns384_AndBytesRoundTrip`: embed one sentence; length 384; `ToBytes` is 1536
   bytes; `FromBytes` reproduces the floats exactly. (Needs the model in the test output: the
   library's acquisition target runs for the test project too because it references the library;
   state in the summary that this gate downloads 33 MB on the first build.)
6. `Search_PutsTheParaphraseFirst`: three passages (a sentence about asking for referrals; one
   about pricing; one about seismic bracing) embedded in memory through a fake store that holds
   vectors; query a paraphrase of the first; it ranks first and the bracing one last.
7. `Store_WritesOnlyKnowledgeSourceAndPassage_AndDeletesOnlyOnForce`: SQL text gate over
   `SqlKnowledgeStore.cs`: every `INSERT INTO`/`UPDATE`/bulk-copy destination is
   `knowledge.Source` or `knowledge.Passage`; exactly one `DELETE`, targeting `knowledge.Passage`
   with `WHERE SourceId = @sourceId`; the claim-count guard `knowledge.Claim` appears before it;
   no `knowledge.Claim` write; no `DROP`/`TRUNCATE`.
8. `Ingest_HashesTheOriginal_NotTheConvertedFile`: with a fake converter that writes a different
   file, the sha passed to the store is the original's (compute with `SHA256.HashData`).

### Piece 6 — nothing else

No App, Worker, MCP, Director window or Dashboard edits. No migration. No email. No model call.

## Claude's run after apply (not yours)

Build both projects and the tests; run the tests; then on KOR-1001:
`BdKnowledgeIngest ingest "C:\Users\ilalonde\Desktop\BD\Books"` (18 files, Calibre for 4),
`list`, and `search` with two questions ("when should a professional firm decline to pitch",
"how to ask a client for an introduction"). The report states passages and tokens per book, which
files were skipped and why, and the top hits with scores.

## Read bound

Read these and nothing else (about 95 KB):

- JoeBrain (read-only, never edited): `src/JoeBrain.Core/Ingest/Readers.cs` (22.9 KB),
  `Ingest/HtmlReaders.cs` (11.5 KB), `Ingest/Chunking.cs`, `Ingest/Ingestor.cs` lines 30–130 and the
  `PickReader` method only, `Brain/Semantic.cs` (18.6 KB; `Embed`, `Windows`, `SearchAsync`, the
  cosine), `Domain/KnowledgeModel.cs` (5.2 KB), `Domain/Enums.cs` (1.6 KB),
  `JoeBrain.Core.csproj` (3.7 KB), `src/JoeBrain.Cli/Program.cs` lines 107–150 only (the `ingest`
  case; the file is 116 KB, do not read the rest)
- Operations: `Kor.Opportunities.Data/Schema/349_KnowledgeStore.sql`,
  `tools/BdIntegrityCheck/BdIntegrityCheck.csproj` and `Program.cs` lines 1–40,
  `Kor.Opportunities.Data/Crm/SqlBdDirectorLedgerStore.cs` (the typed-parameter pattern),
  `Kor.Opportunities.Data/Kor.Opportunities.Data.csproj` (framework and package style)

Do not open the Books folder. Do not read `Kor.Operations.App`.

## Files

Creates: `Kor.Opportunities.Knowledge/` (csproj; `Readers/IReader.cs`, `Chunking.cs`,
`HtmlReader.cs`, `EpubReader.cs`, `PdfTextReader.cs`, `TextReader.cs`, `ReaderPicker.cs`,
`CalibreConverter.cs`, `FileNameParser.cs`, `Tokens.cs`; `Embedding/KnowledgeEmbedder.cs`;
`Store/KnowledgeLicences.cs`, `KnowledgeKinds.cs`, `IKnowledgeStore.cs`, `SqlKnowledgeStore.cs`;
`Search/KnowledgeSearch.cs`); `Kor.Opportunities.Knowledge.Tests/` (csproj,
`KnowledgeIngestRegressionTests.cs`); `tools/BdKnowledgeIngest/` (csproj, `Program.cs`);
`docs/codex/CODEX-BD-DIRECTOR-9C-KNOWLEDGE-INGEST-RESPONSE.md`.

Edits: none.

Remember `using System.IO;` is implicit here (`ImplicitUsings` enable), unlike the App.

## Response

Write the response file in the 9a/9b format: applied changes by piece, the gate table ("authored,
not executed"), what was ported from which JoeBrain file and what was dropped, limits. State
plainly that nothing was built, run or ingested, and that the Books folder was not opened.
`git diff --check` must be clean. Do not commit.

## Appendix A — the 18 file stems, verbatim (for gate 3)

Extension in brackets; the stem is everything before it. Characters are as on disk: `’` (U+2019) in two, `®` in one, `&` in two.

- `Good strategy, bad strategy _ the difference and why it -- Richard Rumelt [Rumelt, Richard]` [.mobi]
- `Let’s Get Real or Let’s Not Play_ Transforming the -- Khalsa, Mahan; Illig, Randy -- 2008` [.epub]
- `Major Account Sales Strategy -- Rackham, Neil -- 2009` [.mobi]
- `Managing the professional service firm -- Maister, David` [.epub]
- `Marketing Handbook for the Design & Construction -- William D_ Mahoney` [.pdf]
- `Never Eat Alone _ And Other Secrets to Success, One` [.epub]
- `Never Split the Difference_ Negotiating As If Your Life -- Voss, Chris -- 2016` [.epub]
- `Playing to Win_ How Strategy Really Works -- A_G_ Lafley [Lafley, A_G_]` [.epub]
- `Rain making_ attracting new clients no matter what your -- Ford Harding -- 2007 -- Adams Media` [.mobi]
- `SPIN Selling_ Situation Problem Implication Need-payoff -- Neil Rackham [Rackham, Neil]` [.epub]
- `Shipley Associates Proposal Guide -- Newman, Larry` [.pdf]
- `Superforecasting _ The Art and Science of Prediction -- Philip E_ Tetlock & Dan Gardner` [.pdf]
- `The Challenger Sale -- Matthew Dixon -- 2017` [.epub]
- `The SPIN® Selling Fieldbook_ Practical Tools, Methods, -- Neil Rackham -- 1st edition, New York, 1996` [.epub]
- `The Trusted Advisor -- David H_ Maister, Robert M_ Galford, Charles H_ Green -- 2001 -- Free Press` [.epub]
- `The Trusted Advisor Fieldbook _ A Comprehensive Toolkit for -- Charles H_ Green and Andrea P_ Howe -- 1st edition, 2011` [.epub]
- `The Trusted Advisor Sales Engineer -- John Care, (Managing director of Mastering Technical Sales -- 2016 -- Mastering Technical Sales` [.azw3]
- `The win without pitching manifesto -- Blair Enns` [.epub]
