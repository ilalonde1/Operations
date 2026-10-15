# BD Director 9c — knowledge ingestion

Applied 2026-10-14. **Source edits and regression sources only; nothing was built,
run, embedded, searched or ingested. The Books folder was not opened and no book
was read or copied.** No database tool, migration, model download, Calibre process,
application, deployment or test was executed. JoeBrain was read only and was not
edited. No commit was made.

Source searches, source review and whitespace checks were performed.
`git diff --check` passed; the new files were also checked without staging them.
No executed pre-fix failure, post-fix pass, SQL verification or search score is
claimed.

Prior art: searched the allowed `tools/` and `docs/codex/` filenames for knowledge
tooling, then read the specified JoeBrain reader/chunking/ingest/semantic sources,
model target and domain definitions. Used migration 349 for the database contract,
the existing typed SQL parameter pattern, and BdIntegrityCheck for the tool project
and connection-string convention. Accepted the supplied measurements without
remeasuring. Ian supplied the 18 filename stems in chat; those exact strings
provide the filename theory's inputs.

## Applied changes

- **Piece 1 — library/project:** Added the net8.0 library with the requested package
  versions, nullable/implicit usings and namespace. Ported the two model downloads,
  cache and output paths, renaming the target to `AcquireKorKnowledgeModel` and
  properties to `KorKnowledgeModel*`. Model/vocabulary content is marked for output
  and publish copying. The first-build 33 MB download is documented, not executed.
- **Piece 2 — readers/chunking:** Added contracts/chunks, token estimation, HTML,
  ePub, PDF text, plain text/Markdown readers and extension routing. Retained the
  OPF/container/manifest/spine regexes, heading references, HTML cleanup, PDF
  three-page groups, glyph/word extraction, ligatures and text-layer/unspaced checks.
  The maximum is 1,500 characters. Paragraph/sentence cuts precede word-boundary
  fallback; an individual token longer than the bound must split. Missing referenced
  spine documents fail the file instead of silently accepting a partial book.
  Scans are reported and skipped; no OCR was added.
- **Piece 3 — embedding/store/search:** Added the local embedder, explicit
  384-float/1,536-byte little-endian conversion, schema-matching constants, records
  and SQL store. Ingest rechecks the hash under a serializable transaction and
  unique-hash range lock. Force checks claims, deletes only that source's passages,
  bulk-copies its replacement passages/embeddings once, and updates source
  metadata/counts/model before commit. Failure disposes the uncommitted transaction.
  Repair accepts at most 500 vectors per call, bulk-copies a local temp table and
  performs one joined, NULL-only passage UPDATE. Search caches vectors/source titles
  per instance, ranks by cosine, caps per source and fetches passages by ID.
- **Piece 4 — CLI/conversion:** Added `ingest`, `embed`, `search`, `list`, requested
  defaults/flags, validation, cancellation and per-file reporting. Folder inputs are
  nonrecursive, filtered by extension during enumeration and sorted by filename.
  One file failure does not abort later files. Dry mode prints counts/first ref/first
  line without store or embedding calls. Calibre receives separate process arguments;
  its unique temporary ePub is removed in `finally`, and cancellation stops the
  process. Original filename/hash remain the provenance. Added
  `KnowledgeIngestor.cs` so CLI and fake-converter gates exercise the same pipeline.
  Filename parsing applies only the requested rules and supports CLI overrides.
- **Piece 5 — regression sources:** Added the isolated net8.0 xUnit project, eight
  requested gate methods and additional cache/cap, dry/skip/claim, cleanup and
  metadata-contract gates. The class summary states coverage, exclusions, download
  requirements and a same-class miss. All 18 filename inputs came from Ian's message.
- **Piece 6 — scope:** New library, CLI, tests and this response only. No App, Worker,
  MCP, Director, Dashboard, existing project or migration was edited.

## JoeBrain port map

| JoeBrain source | Retained/adapted | Dropped |
|---|---|---|
| `Ingest/Readers.cs` | Reader contract, text-layer exception, PDF extraction/page groups/spacing/glyph helpers. | Gutenberg, course/PDF-directory wrappers, OCR, subtitles and measurement/probe helpers. |
| `Ingest/HtmlReaders.cs` | HtmlText, HtmlReader, EpubReader, NaturalOrder, regexes and heading references. Missing spine entries fail explicitly. | No web acquisition was added. |
| `Ingest/Chunking.cs` | Paragraph/sentence splitting, normalization, Chunk and chars/4 estimate. Maximum 8,000 → 1,500; word-boundary fallback before hard cuts. | Larger-passage assumption. |
| `Ingest/Ingestor.cs` and selected CLI ingest case | Hash-before-read, skip-by-hash, ordered passages and per-file outcomes, adapted to embedding before bulk persistence. | EF, course/session/consent/seed handling, claim mutation and post-save embedding. |
| `Brain/Semantic.cs` | Named LocalEmbedder call, dimensions/size, cosine formula and per-source cap. | Window table, global database caches, model selection, notes, concepts, definitions and judge integration. |
| `JoeBrain.Core.csproj` | Requested package versions and both cached Hugging Face downloads/output paths. | Docnet, EF, Anthropic, spreadsheets, YAML and unrelated dependencies. |
| `Domain/KnowledgeModel.cs`, `Domain/Enums.cs` | Provenance shape as context; exact implemented records and CHECK lists come from 349. | JoeBrain entities, relationships and extra kinds/licences. |

Calibre conversion, filename parsing, the SQL bulk store and CLI argument/reporting
code are new adapters around the port. No JoeBrain assembly/project reference was
added. Cosine uses the same dot-product/norm formula directly, without adding a
tensor package.

## Gates authored, not executed

| Gate | Expected versus actual checks authored |
|---|---|
| `EpubReader_ReadsTheSpineInOrder_AndRefsEachDocument` | Synthetic spine 3/1/2, bodies/headings, prefixed OPF tags and both manifest attribute orders. |
| `Chunking_NeverExceeds1500Chars_AndPrefersParagraphBoundaries` | Over 10,000 characters, bound, whole-paragraph starts, preserved text, long sentences and unbreakable tokens. |
| `FileNameParser_ReadsIansNames` | All 18 verbatim stems and exact expected title/author/year. |
| `ReaderPicker_RoutesEveryExtension_AndRefusesUnknown` | Eight extension spellings, case-insensitive routing and named .docx refusal. |
| `Embedder_Returns384_AndBytesRoundTrip` | Local sentence embedding, dimensions, byte length, bit-exact round trip, known endian bytes and invalid-length rejection. |
| `Search_PutsTheParaphraseFirst` | Real local vectors for three synthetic passages: referrals first, seismic bracing last. |
| `Store_WritesOnlyKnowledgeSourceAndPassage_AndDeletesOnlyOnForce` | Permanent targets, exact bulk destinations, one scoped deletion after force/claim guard, no claim writes/destructive DDL, typed parameters, locks/transaction and bounded NULL-only repair. |
| `Ingest_HashesTheOriginal_NotTheConvertedFile` | MOBI and AZW3: original SHA/name, one embedding per passage before one store call, temporary output removed. |
| `FileNameParser_DoesNotInventCorrectionsOrUseTitleYears` | Other underscores preserved, title years ignored, first valid year from later parts. |
| `Search_LoadsVectorsOnce_AndCapsEachSource` | Cached index and two-per-source cap without hiding the next source. |
| `Converter_RemovesTemporaryFile_WhenEpubReadingFails` | Invalid synthetic output removed after a reader exception. |
| `Ingest_DryAndSkipAvoidEmbeddings_AndClaimsRefuseForce` | Dry avoids store/embedding calls; existing hashes skip; claims refuse force before writes. |
| `LicenceAndKindConstants_MatchMigration349` | Library lists match source CHECK values exactly. |

## Clarifications and limits

The brief's blanket bulk-destination gate would prohibit the temporary table that
Piece 3 requires. The gate permits exactly `#KnowledgeEmbeddings` as session-local
staging alongside `knowledge.Passage`; permanent INSERT/UPDATE targets remain
`knowledge.Source` and `knowledge.Passage`. Repair has one passage UPDATE per batch
and leaves existing source metadata unchanged. Claims are never written.

“Six known extensions” names eight spellings including aliases: epub, pdf, html,
htm, txt, md, mobi and azw3. All eight are covered.

Incomplete supplied stems remain incomplete: the Never Eat Alone entry has no
author/year; the Care author retains its unfinished parenthesis. Underscores
surrounded by spaces stay underscores under the stated title rule. No title
completion or bibliographic fact was invented.

The reader retains regex-based HTML/OPF assumptions; synthetic fixtures do not
establish coverage of all ePub variants or PDF layouts. Chunking preserves
normalized text, not whitespace byte-for-byte. References over the schema's
200-character limit are rejected rather than silently truncated.

Passages/vectors and the bulk-copy table are held in memory per book. Search is an
index snapshot; create a new instance after ingest/repair. Repair fills NULL
vectors only. Calibre availability, model content copying, SQL validity,
permissions/rollback/concurrency and semantic rankings remain unverified by
execution. No book passage/token totals or search scores were measured here.

Claude owns changed-project Debug builds, tests, model/output checks, KOR-1001
ingestion/listing and the two requested search proofs/report. Ian has no execution
step in this handoff.

## Files created

23 new files; no existing tracked files edited:

- `Kor.Opportunities.Knowledge/Kor.Opportunities.Knowledge.csproj`
- `Kor.Opportunities.Knowledge/KnowledgeIngestor.cs` (shared orchestration for CLI/gates)
- `Kor.Opportunities.Knowledge/Readers/IReader.cs`
- `Kor.Opportunities.Knowledge/Readers/Chunking.cs`
- `Kor.Opportunities.Knowledge/Readers/HtmlReader.cs`
- `Kor.Opportunities.Knowledge/Readers/EpubReader.cs`
- `Kor.Opportunities.Knowledge/Readers/PdfTextReader.cs`
- `Kor.Opportunities.Knowledge/Readers/TextReader.cs`
- `Kor.Opportunities.Knowledge/Readers/ReaderPicker.cs`
- `Kor.Opportunities.Knowledge/Readers/CalibreConverter.cs`
- `Kor.Opportunities.Knowledge/Readers/FileNameParser.cs`
- `Kor.Opportunities.Knowledge/Readers/Tokens.cs`
- `Kor.Opportunities.Knowledge/Embedding/KnowledgeEmbedder.cs`
- `Kor.Opportunities.Knowledge/Store/KnowledgeLicences.cs`
- `Kor.Opportunities.Knowledge/Store/KnowledgeKinds.cs`
- `Kor.Opportunities.Knowledge/Store/IKnowledgeStore.cs`
- `Kor.Opportunities.Knowledge/Store/SqlKnowledgeStore.cs`
- `Kor.Opportunities.Knowledge/Search/KnowledgeSearch.cs`
- `Kor.Opportunities.Knowledge.Tests/Kor.Opportunities.Knowledge.Tests.csproj`
- `Kor.Opportunities.Knowledge.Tests/KnowledgeIngestRegressionTests.cs`
- `tools/BdKnowledgeIngest/BdKnowledgeIngest.csproj`
- `tools/BdKnowledgeIngest/Program.cs`
- `docs/codex/CODEX-BD-DIRECTOR-9C-KNOWLEDGE-INGEST-RESPONSE.md`

The seven files already untracked when 9c started, including its brief, were preserved.
