# Codex — BD Director 9d: claims and concepts, from literature to a rule bank

**DO NOT BUILD. DO NOT RUN TESTS. DO NOT RUN ANY TOOL AGAINST A DATABASE. DO NOT READ THE BOOKS OR
THE PASSAGES. DO NOT CALL ANY MODEL.** Make the edits, add the migration and the regression gates
as source, and ping when applied. Claude builds, runs the gates, applies migration 350 on the
server, exports the passages, writes the claims itself in its own session, imports them, renders
the new tab and reads it, and installs the app locally. Ian runs nothing.

## Why this exists

The knowledge store holds 18 books as 8,515 cited passages (brief 9c, commit `18432ca0`). That is
literature with a search index. This brief turns it into a rule bank: **claims**, one sentence
each, what the book asserts, each cited to the single passage it rests on, tagged to shared
**concepts** so the same idea from three books sits in one place. Ian reviews claims per book in
a new tab of the BD Director window and keeps or retires them. Nothing is deleted.

**Extraction is a build step, not a product feature, and it does not use the Anthropic API.**
Claude writes the claims in its own session, book by book, from an export of the passages, and
imports them with the ingest tool. The API stays where it already is: the MCP server answering
questions at runtime. Nothing in this brief touches `Kor.Operations.Mcp`.

Measured 2026-10-14 at commit `18432ca0`. Use as given.

| Fact | Value |
|---|---|
| `knowledge.Source` / `knowledge.Passage` | 18 sources, 8,515 passages, 2,430,836 tokens (chars ÷ 4), every passage embedded. Passages ≤ 1,500 characters, ordered by `(SourceId, Ordinal)`, `Ref` a heading or `pp. a–b` |
| Source ids and passage counts | 1 Rumelt 564 · 2 Khalsa 389 · 3 Rackham Major Account 473 · 4 Maister MPSF 663 · 5 Mahoney 1085 · 6 Ferrazzi 411 · 7 Voss 323 · 8 Lafley 500 · 9 Harding 577 · 10 Shipley 806 · 11 Rackham SPIN 316 · 12 Tetlock 535 · 13 Dixon 392 · 14 SPIN Fieldbook 299 · 15 Trusted Advisor 319 · 16 TA Fieldbook 451 · 17 Care 298 · 18 Enns 114 |
| `knowledge.Claim` (migration 349) | `Id, SourceId, PassageId, Kind IN (Rule, Fact, Definition, Heuristic), Statement NVARCHAR(1000), Confidence DECIMAL(4,3) 0–1, ExtractedBy NVARCHAR(100), ExtractedAtUtc, RetiredAtUtc, RetiredReason`. 0 rows |
| The ingest tool | `tools/BdKnowledgeIngest` (`ingest`, `embed`, `search`, `list`) over `Kor.Opportunities.Knowledge` (`Store/IKnowledgeStore.cs`, `SqlKnowledgeStore.cs`: typed parameters, `SqlBulkCopy`, one transaction per book). Connection from `--db` else env `KOR_OPPORTUNITIES_OPPORTUNITIESDB` |
| The Director window | `BdDirectorWindow.xaml` has five `TabItem`s (lines 67, 97, 142, 196, 241); `BdDirectorViewModel` ctor takes `(IBdDirectorStore, IPursuitOverwatchStore, ICrmEngagementStore, IBdAttributionStore, IBdDirectorLedgerStore, IDeltekClientContextService)`; `BdScreensRenderTests` writes five `director-*.png` and asserts `written >= 10` |
| JoeBrain's shape (reference only) | `Concept(Slug, Name, Summary, Aliases)`, `Claim(ConceptId, Kind, Statement, SourceId, PassageId, Confidence)`, `LinkedBy { Human, Model }` in `src/JoeBrain.Core/Domain/KnowledgeModel.cs` and `Enums.cs` |

## The decisions, so you build the one thing

- **Two CLI verbs, no service.** `export` writes one JSON file per book with its passages;
  `claims import` reads one JSON file per book with the claims and writes them. Claude produces
  the claims file in between. The tool never calls a model.
- **A claim is one sentence in plain words, standalone, actionable or testable by a Director of
  BD at a sixty-person structural engineering consultancy.** Four kinds as the CHECK allows.
  Confidence is how directly the passage states it (a quotation is 1.0; an inference is lower).
  Front matter, praise, contents pages, exercises and anecdotes without an assertion yield no
  claim. These rules are in the import's validation and in `docs/BD-Knowledge-Claims.md`, the
  one-page rulebook Claude follows when writing claims (Piece 3).
- **Concepts are a controlled list that grows.** Each claim carries one to three concepts as a
  slug plus a short name. Slugs are normalised (lower-case, letters, digits, hyphens, 3–80). The
  first name seen for a slug is kept. `export` includes the current concept list so the writer
  reuses slugs before inventing.
- **Import is idempotent and stamps what it covered.** A passage is stamped
  `ClaimsExtractedAtUtc`/`By` when its file is imported, including passages the file lists with
  no claims. A file for a source whose passages are already stamped is refused unless `--force`,
  which retires (never deletes) that source's unreviewed model claims and re-imports. Kept or
  retired-by-Ian claims are never touched by an import.
- **Review lives in the Director window, tab six: Knowledge.** Keep marks `ReviewedAtUtc`/`By`.
  Retire sets `RetiredAtUtc`/`RetiredReason` (and `ReviewedAtUtc`/`By`). Nothing is deleted;
  nothing is reworded. The App reads and writes claims through a new store in
  `Kor.Opportunities.Data/Knowledge/` (plain `Microsoft.Data.SqlClient`; the App already
  references Data; it must **not** reference `Kor.Opportunities.Knowledge`, which carries ONNX
  and PdfPig).
- **No GRANT, no MCP edit, no model call, no email.**

## Pieces

### Piece 1 — migration `Kor.Opportunities.Data/Schema/350_KnowledgeClaimsAndConcepts.sql`

Same shape as 348 and 349 (block header, `SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; GO`, one
guarded batch per object, no `USE`, no `DROP`, no data):

- `knowledge.Concept`: `Id BIGINT IDENTITY PK`, `Slug NVARCHAR(80) NOT NULL` with
  `UX_Knowledge_Concept_Slug UNIQUE`, `Name NVARCHAR(150) NOT NULL`, `Summary NVARCHAR(500) NULL`,
  `CreatedAtUtc` default, `CreatedBy NVARCHAR(100) NOT NULL`.
- `knowledge.ClaimConcept`: `ClaimId BIGINT` FK → `knowledge.Claim(Id)`, `ConceptId BIGINT` FK →
  `knowledge.Concept(Id)`, `Confidence DECIMAL(4,3) NOT NULL` CHECK 0–1,
  `LinkedBy NVARCHAR(10) NOT NULL` CHECK `IN (N'Model', N'Human')`, PK `(ClaimId, ConceptId)`,
  index on `ConceptId`.
- `knowledge.ClaimImport`: `Id BIGINT IDENTITY PK`, `SourceId BIGINT` FK, `FileName NVARCHAR(260)`,
  `FileSha256 CHAR(64)`, `ImportedAtUtc` default, `ImportedBy NVARCHAR(100)`, `PassagesCovered INT`,
  `ClaimsWritten INT`, `ClaimsRejected INT`, `Forced BIT`.
- `ALTER TABLE knowledge.Claim ADD ReviewedAtUtc DATETIMEOFFSET NULL, ReviewedBy NVARCHAR(150) NULL`
  guarded by `COL_LENGTH`, its own batch.
- `ALTER TABLE knowledge.Passage ADD ClaimsExtractedAtUtc DATETIMEOFFSET NULL, ClaimsExtractedBy NVARCHAR(100) NULL`
  guarded by `COL_LENGTH`, its own batch; then
  `IX_Knowledge_Passage_Unextracted ON knowledge.Passage (SourceId, Ordinal) WHERE ClaimsExtractedAtUtc IS NULL`
  (filtered), guarded, its own batch.

### Piece 2 — the claim store for the App: `Kor.Opportunities.Data/Knowledge/`

`IKnowledgeClaimStore.cs` + `SqlKnowledgeClaimStore.cs` (typed parameters, the 9b pattern;
registered as a singleton in `OpportunitiesModule.cs` beside the 9b ledger line). Records:
`KnowledgeSourceSummary(long Id, string Title, string? Author, int Passages, int PassagesExtracted, int Claims, int Reviewed, int Retired)`,
`KnowledgeClaimRow(long Id, long SourceId, string SourceTitle, long PassageId, string? Ref, string Kind, string Statement, decimal Confidence, IReadOnlyList<string> Concepts, DateTimeOffset? ReviewedAtUtc, DateTimeOffset? RetiredAtUtc, string? RetiredReason)`,
`KnowledgeConceptRow(long Id, string Slug, string Name, int Claims, int Sources)`,
`KnowledgePassageText(long Id, string SourceTitle, string? Ref, string Text)`. Methods:
`ListSourcesAsync`, `ListClaimsAsync(long sourceId, string filter /* Unreviewed | Kept | Retired | All */, int take, ct)`,
`SampleUnreviewedAsync(long sourceId, int take, ct)` (`ORDER BY NEWID()`),
`ListConceptsAsync`, `ListClaimsByConceptAsync(long conceptId, int take, ct)`,
`GetPassageAsync(long passageId, ct)`,
`KeepAsync(long claimId, string by, ct)` (UPDATE `ReviewedAtUtc = sysdatetimeoffset(), ReviewedBy = @by WHERE Id = @id`),
`RetireAsync(long claimId, string by, string reason, ct)` (UPDATE the four columns `WHERE Id = @id AND RetiredAtUtc IS NULL`).
No INSERT, no DELETE, no edit of `Statement`.

### Piece 3 — export and import in the ingest tool

In `Kor.Opportunities.Knowledge`:
- `Claims/ClaimFile.cs`: the JSON shape both verbs share.
  ```json
  { "sourceId": 18, "title": "…", "author": "…", "exportedAtUtc": "…",
    "knownConcepts": [ { "slug": "qualify-before-investing", "name": "Qualify before investing" } ],
    "passages": [ { "id": 8402, "ordinal": 1, "ref": "…", "text": "…", "claims": [] } ] }
  ```
  A claims file is the same document with `claims` filled:
  `{ "kind": "Rule", "statement": "…", "confidence": 0.9, "concepts": [ { "slug": "…", "name": "…" } ] }`.
  `text` may be omitted in a claims file; `id` and `ordinal` are required and must match the
  store. `writtenBy` (string) is required on a claims file and becomes `ExtractedBy`.
- `Claims/ClaimValidation.cs`: kind in the four; statement 1–1,000 characters, one sentence (no
  line break; at most one terminal `.`/`?`/`!` apart from abbreviations is not checked, only the
  line break and length); confidence 0–1; 1–3 concepts; slug normalised to `[a-z0-9-]{3,80}`; a
  passage id must belong to the file's source. Returns the accepted claims and the rejects with
  reasons.
- `Store/IKnowledgeStore.cs` gains: `ExportAsync(long sourceId, ct)` → `ClaimFile` with texts
  and the current concept list; `ImportClaimsAsync(ClaimFile file, bool force, string by, ct)` →
  `(int PassagesCovered, int ClaimsWritten, int ClaimsRejected)`: one transaction: refuse when any
  listed passage is stamped and not `force`; on `force`, UPDATE that source's claims with
  `ReviewedAtUtc IS NULL` to `RetiredAtUtc = now, RetiredReason = N'Superseded by re-import'`;
  upsert concepts by slug (SELECT then INSERT, `CreatedBy = by`); INSERT claims
  (`ExtractedBy = file.writtenBy`) and links (`LinkedBy = N'Model'`); UPDATE the listed passages'
  stamps; INSERT the `ClaimImport` row with the file's SHA-256. No DELETE anywhere.

In `tools/BdKnowledgeIngest/Program.cs`, two verbs added to the header and the switch:
```
export <sourceId|all> --to <folder>            one <sourceId>-<slug-of-title>.json per book, passages with text
claims import <file.json|folder> [--force]     validate, write, stamp; prints accepted / rejected per file with reasons
```
`claims import` on a folder takes every `*.claims.json` in name order and never stops on one
file's failure; the last line is `<ok> of <n> files imported, <claims> claims, <rejected> rejected`.

`docs/BD-Knowledge-Claims.md` (new, one page): the rulebook the claim writer follows: the
definition of a claim, the four kinds with one example each, the confidence rule, what yields
nothing, the concept rule (reuse before invent; slug form; 1–3 per claim), the file shape, and
the sentence "the passage is the citation; a claim the passage does not support is a defect".

### Piece 4 — the Knowledge tab in the Director window

Sixth `TabItem`, header exactly **Knowledge**, after Asks & decisions. Caption constant
`KnowledgeCaption` =
`What the books assert, one sentence each, cited to the passage it rests on. Keep what holds for KOR; retire what does not. Nothing is deleted.`
Layout, left to right:
- **Books** (DataGrid of `KnowledgeSourceSummary`): Book, Author, Passages, Extracted (as
  `PassagesExtracted/Passages`), Claims, Reviewed, Retired. Selecting a book loads its claims.
- **Claims** (DataGrid of `KnowledgeClaimRow`): Kind, Claim (wrapping; no fixed row `Height`),
  Confidence (`0.00`), Concepts (joined by `·`), Ref, Status (Unreviewed / Kept / Retired). Above
  it: filter ComboBox (Unreviewed default, Kept, Retired, All), **Sample 20**
  (`SampleUnreviewedAsync`), **Keep**, **Retire…** (NoteDialog for the reason, required, 1–200
  characters), and a **By concept** ComboBox listing `KnowledgeConceptRow` as
  `Name (claims across sources)`; choosing one replaces the list with `ListClaimsByConceptAsync`
  and the caption line reads `Concept · <name> · <n> claims from <m> books`.
- **Passage** pane: the selected claim's passage (`GetPassageAsync`): book, ref, full text in a
  read-only wrapping TextBox. A claim is never shown without its passage reachable.
`BdDirectorViewModel` gains `IKnowledgeClaimStore claims` as its seventh constructor parameter;
the 9a/9b fixtures and `RenderDirector` pass a mock. Keep/Retire reload the current list and the
book row's counts only. The actor is the window's `_actor`.

### Piece 5 — gates

**`Kor.Operations.App` tests, new class `BdDirectorKnowledgeRegressionTests.cs`** (rule 11 summary
with coverage, exclusions, one same-class miss):
1. `Migration350_IsAdditiveAndGuarded`: guards for the three tables, both `COL_LENGTH` guards, the
   filtered index; no `USE`, `DROP`, `TRUNCATE`, `GRANT`, `INSERT INTO`, `DELETE`.
2. `ClaimStore_NeverInsertsDeletesOrRewords`: SQL text over `SqlKnowledgeClaimStore.cs`: no
   `INSERT`, no `DELETE`; the two UPDATEs set only the review/retire columns; `Statement` never
   appears on the left of `=`.
3. `Window_HasSixTabs_KnowledgeLast_AndPlainWords`: six `TabItem`s in order, the caption by
   `x:Static`, the 9a plain-words regex over every Director file.
4. `Review_KeepAndRetire_ForwardActorAndReason_AndReloadCounts`: view-model with mocks.
5. `Director_StillContributesZeroInlineDates`: extended to the new files.
Render: `director-knowledge.png`; minimum renders 11; fixture: three books with the measured
passage counts (Maister 663, Rackham SPIN 316, Enns 114) and illustrative claim rows marked so.

**`Kor.Opportunities.Knowledge.Tests`, added to `KnowledgeIngestRegressionTests.cs` or a new
class beside it:**
6. `ClaimFile_RoundTrips_AndTextIsOptionalOnImport`.
7. `ClaimValidation_RejectsEachBadShape_WithAReason`: wrong kind, 1,200 characters, a line break,
   confidence 1.4, zero concepts, four concepts, a bad slug, a passage id from another source →
   each rejected with its reason; one valid claim survives.
8. `Slug_IsNormalised`: `"Qualify Before Investing!"` → `qualify-before-investing`; too short → rejected.
9. `Import_RefusesStampedPassages_UnlessForce_AndForceRetiresOnlyUnreviewedModelClaims`: SQL text
   gate over the import's statements plus a fake-store unit test of the decision.
10. `Store_StillWritesOnlyKnowledgeTables_AndNeverDeletes`: the 9c gate extended to the new
    statements: `INSERT` targets `Concept`, `Claim`, `ClaimConcept`, `ClaimImport` only; the
    force UPDATE carries `ReviewedAtUtc IS NULL`; no `DELETE` beyond the 9c passage one.

## Claude's run after apply (not yours)

Build App tests and Knowledge tests; run the gate classes; apply 350 on KOR-APP01 through
NetworkOps; `export 18 --to <scratchpad>`; write the Enns claims file in session by the rulebook;
`claims import`; read the result in the tab and show Ian; then the remaining books in the order
Ian set (Maister ×2, Harding, Khalsa, Enns first), one file each, imported as they are done;
install the app locally. The books' text never enters git; the claims files are kept beside the
books in Ian's folder, not in the repo.

## Read bound

Read these and nothing else (about 150 KB):
- `docs/codex/CODEX-BD-DIRECTOR-9B-…` and `9C-…` briefs and responses (what is already decided)
- `Kor.Opportunities.Data/Schema/349_KnowledgeStore.sql`, `348_BdDirectorLedger.sql`
- `Kor.Opportunities.Data/Crm/SqlBdDirectorLedgerStore.cs`, `IBdDirectorLedgerStore.cs`
- `Kor.Opportunities.Knowledge/` — `Store/IKnowledgeStore.cs`, `Store/SqlKnowledgeStore.cs`, `KnowledgeIngestor.cs`, the csproj; `tools/BdKnowledgeIngest/Program.cs`; `Kor.Opportunities.Knowledge.Tests/KnowledgeIngestRegressionTests.cs`
- `Kor.Operations.App/BusinessDevelopment/Director/` (all files), `CompositionModules/OpportunitiesModule.cs` lines 140–150 and 260–275
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdDirectorRegressionTests.cs`, `BdDirectorLedgerRegressionTests.cs` (summary and fixture only), `BdScreensRenderTests.cs` `RenderDirector`

Do not open `Kor.Operations.Mcp`. Do not open the Books folder. App projects with `UseWPF` have
no implicit `System.IO`; the Data, Knowledge and tool projects do.

## Files

Creates: `350_KnowledgeClaimsAndConcepts.sql`; `Kor.Opportunities.Data/Knowledge/IKnowledgeClaimStore.cs`,
`SqlKnowledgeClaimStore.cs`; `Kor.Opportunities.Knowledge/Claims/ClaimFile.cs`, `ClaimValidation.cs`;
`docs/BD-Knowledge-Claims.md`;
`Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdDirectorKnowledgeRegressionTests.cs`;
`docs/codex/CODEX-BD-DIRECTOR-9D-CLAIMS-AND-CONCEPTS-RESPONSE.md`.

Edits: `Kor.Opportunities.Knowledge/Store/IKnowledgeStore.cs`, `SqlKnowledgeStore.cs`;
`tools/BdKnowledgeIngest/Program.cs`; `Kor.Opportunities.Knowledge.Tests/KnowledgeIngestRegressionTests.cs`;
`BdDirectorWindow.xaml` + `.xaml.cs`; `BdDirectorViewModel.cs`; `BdDirectorPersonRow.cs` (row types);
`OpportunitiesModule.cs`; `BdDirectorRegressionTests.cs`, `BdDirectorLedgerRegressionTests.cs`
(fixture ctor), `BdScreensRenderTests.cs`.

## Response

The 9a–9c format. State plainly that nothing was built, run, applied or called, and that neither
the books nor the passages were read. `git diff --check` clean. Do not commit.
