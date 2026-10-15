# BD Director 9d — claims and concepts

Applied as source edits on 2026-10-14. **Nothing was built or tested; no application,
CLI verb, gate, render, database tool or model was run or called. Migration 350 was
not applied. Neither the books nor their passages were read.** No export, claim
extraction, import, model download, email, deployment or service action was performed.
No commit was made. Source review and whitespace checks only: `git diff --check`
is clean, including separate no-index checks of the new files. No executed
pre-fix failure, post-fix pass, SQL verification or visual proof is claimed.

Prior art: used the bounded 9b/9c briefs/responses, migrations 348/349, typed ledger
store, Knowledge store/tool/tests, Director sources, specified DI registrations
and Director fixtures. Accepted the supplied production measurements without
remeasuring. The workspace already contained most of the Knowledge tab, Data
claim store, DI/fixture changes and a draft 350. Those were reviewed and retained
where they matched this brief. The draft migration's extraction-run table and
permission grant were replaced by the specified file-import audit table and no
grant. `Kor.Operations.Mcp` was neither opened nor edited. Unrelated workspace
changes were left alone.

## Applied changes

- **Piece 1 — migration 350:** Three guarded tables (`Concept`, `ClaimConcept`,
  `ClaimImport`), unique slug, non-cascading foreign keys, link confidence/provenance
  checks, concept index, separately guarded review/stamp columns and filtered
  unextracted-passage index. Settings and object batches follow 348/349. No `USE`,
  destructive SQL, data statements or grants. Individual guards on both columns
  of each pair also tolerate partially applied column additions.
- **Piece 2 — App claim store:** Retained the new Data-only records/interface and
  typed `Microsoft.Data.SqlClient` store. Lists provide books, all four claim
  filters, random unreviewed samples, concepts and claims across books. Passage
  text is fetched by ID. Keep changes only review attribution; Retire changes
  review/retirement fields and requires 1–200 characters. There is no insert,
  deletion or statement-edit API. The singleton is beside the ledger registration;
  the App gains no Knowledge-library reference.
- **Piece 3 — file contract and validation:** Added the common camel-case JSON
  document, optional import text, required passage ID/ordinal/claims-array fields,
  normalized concept slugs and accepted/rejected diagnostics. Kind, statement
  length/line breaks, numeric confidence, concept count/name/slug/distinctness,
  source membership and ordinals are validated. Writer attribution is required.
  A shared import decision rejects invalid coverage or previously stamped listed
  passages before any write. File provenance is calculated from original bytes;
  a UTF-8 BOM is included in the hash and removed only for JSON parsing.
- **Piece 3 — export/import store:** Export reads source metadata, the current
  controlled concept list and ordered passages. Import holds one serializable
  transaction for source/passage locks, validation, optional retirement, concept
  lookup/insert, claim/link insertion, passage stamps and the audit row. Existing
  concept names are never updated. Concept locks are taken in slug order; passage
  stamps use batches of at most 500 IDs. Importer attribution and file writer
  attribution remain separate. The audit stores original filename/hash and
  covered/written/rejected/force counts. Exceptions leave the transaction uncommitted.
- **Piece 3 — CLI/rulebook:** Added `export <sourceId|all> --to <folder>` and
  `claims import <file.json|folder> [--force] [--by <name>]` to parsing, routing and
  help. Export names files by source ID and title slug. Folder import processes
  `*.claims.json` nonrecursively in filename order, continues after file failure,
  prints accepted/rejected counts and passage/claim-specific reasons, and ends
  with the requested aggregate line. These branches return before any embedder
  is constructed. Added `docs/BD-Knowledge-Claims.md` with definitions/examples,
  confidence, exclusion rules, concept reuse, file shape and citation obligation.
- **Piece 4 — Knowledge tab:** Retained/completed the sixth tab with the exact
  static caption, book counts, status filters, Sample 20, Keep, required-reason
  Retire, concept navigation and full cited passage pane. Claim rows have automatic
  height, wrapping and no maximum-height clipping or text trimming. Book columns
  share the available width. Review forwards the window actor, reloads the current
  list and updates only the affected book row from refreshed summaries. Other
  Director panes and the concept catalogue are not reloaded after review. Late
  list/passage reads are cancelled and discarded. The seventh constructor argument,
  9a/9b mocks and required-note dialog support remain wired.
- **Piece 5 — gates/render source:** Added `BdDirectorKnowledgeRegressionTests`
  with coverage/exclusions/same-class miss. Extended the existing Knowledge gates
  and fake store, preserving the original ingest/embedding gates. Updated the
  older tab expectation to six. RenderDirector now includes
  `director-knowledge.png`; the minimum output count is eleven. No PNG was made.

## Gates authored, not executed

| Gate | Expected versus actual checks authored |
|---|---|
| `Migration350_IsAdditiveAndGuarded` | Three table guards, four individual column guards, two guarded indexes, settings/batches, constraints/audit fields and forbidden SQL. |
| `ClaimStore_NeverInsertsDeletesOrRewords` | Two claim updates with exact permitted SET columns, immutable statement, no insert/delete/merge, typed parameters, random sample and Data-only contract. |
| `Window_HasSixTabs_KnowledgeLast_AndPlainWords` | Exact ordered tabs/static caption, wrapping/automatic row height, read-only passage, window actor forwarding, required 200-character dialog and the 9a literal regex across Director files. |
| `Review_KeepAndRetire_ForwardActorAndReason_AndReloadCounts` | Mocked actor/reason forwarding, updated reviewed/retired counts, list filtering, retained book-row identity, invalid reasons, retired-row protection and no other pane refresh. |
| `Director_StillContributesZeroInlineDates` | All Director C#/XAML files, including new Knowledge code, contribute zero inline date patterns. |
| `Knowledge_SamplesTwenty_AndGroupsByConcept_WithPassagesReachable` | Sample size/source, concept query/caption, passage identity, concept-list reload after review and return to a book filter. |
| `Knowledge_DiscardsStaleClaimsAndPassages` | Superseded tokens cancelled; delayed old list/text cannot replace the newer selection. |
| `ClaimFile_RoundTrips_AndTextIsOptionalOnImport` | JSON fields, absent/present text, original-byte hash including BOM, excluded runtime diagnostics and required passage fields. |
| `ClaimValidation_RejectsEachBadShape_WithAReason` | All eight requested invalid cases have reasons; a valid claim survives; wrong-source coverage also makes the file invalid. |
| `Slug_IsNormalised` | Required phrase becomes `qualify-before-investing` in the accepted claim; too-short normalized slug is rejected. |
| `ClaimValidation_RefusesInvalidCoverage_AndRequiresWriter` | Missing writer, empty file, duplicate passage, mismatched ordinal and null claims array refuse the file even with force. |
| `Import_RefusesStampedPassages_UnlessForce_AndForceRetiresOnlyUnreviewedModelClaims` | Transaction/lock/SQL predicates, first-name lookup/insert, provenance/stamps, validation-before-write and fake-store decision. Preserves kept, retired, human, mixed, unlinked and other-source rows; stamps empty and rejected-only passages; omitted passages remain unstamped. |
| `Store_StillWritesOnlyKnowledgeTables_AndNeverDeletes` | Nine allowed write statements, exactly four new insert targets, unchanged bulk destinations, reviewed/retired guard and only the existing 9c passage deletion under its force/claim guard. |
| Existing 9a/9b fixtures and render harness | Seventh mock argument, six-tab expectations, sixth output and minimum eleven total renders. |

## Fixture details and limits

Render books use the supplied IDs/counts: **4 Maister / 663**, **11 Rackham SPIN /
316**, **18 Enns / 114**. Extraction, claim, review and retirement totals are
illustrative. Render claim wording and passage text explicitly say illustrative;
they are not assertions extracted from those books. The passage mock returns the
requested ID/reference. Existing People/Funnel/Accounts fixture limitations from
9a/9b remain unchanged.

The schema has no claim-level model/human enum. Forced retirement therefore uses
`ClaimConcept.LinkedBy`: at least one Model link, no Human link, matching source,
and both review and retirement timestamps NULL. This preserves unlinked claims
conservatively. `ExtractedBy` remains the file's free-form writer attribution.

Coverage errors refuse the entire file rather than stamping an unverified passage.
Individual malformed claims are rejected while valid claims and every valid listed
passage are committed. A rejected-only passage is still covered. Partial files are
allowed; **force retires eligible claims across the source**, including on omitted
passages, so the rulebook directs replacement imports to use a complete book file.
Force inserts fresh accepted claims while retaining prior reviewed/retired records;
it does not deduplicate those preserved records by statement text.

Claims/reviewed/retired counts include retained history; concept counts and concept
queries include retired claims. Claim/concept views request at most 1,000 rows and
say when that bound is reached; samples request twenty unreviewed claims. Book
summary reads retrieve all summaries because that is the specified store API,
but review updates only the affected UI row. The file hash audits the original
input; it is not a claim-uniqueness key. Idempotence comes from passage stamps.

Mechanical validation cannot establish sentence meaning, passage support or BD
relevance. Punctuation/abbreviations are deliberately not counted. SQL source gates
cannot prove SQL validity, permissions, concurrency, rollback or performance; the
fake store exercises the shared decision and is not a database emulator. Claim/link
inserts use individual parameterized commands inside the transaction; runtime
import duration has not been measured. No build, test or render verifies binding,
layout, package compatibility or dialog behavior in this session.

## Files

New/untracked task artifacts (eight):

- `Kor.Opportunities.Data/Schema/350_KnowledgeClaimsAndConcepts.sql`
- `Kor.Opportunities.Data/Knowledge/IKnowledgeClaimStore.cs`
- `Kor.Opportunities.Data/Knowledge/SqlKnowledgeClaimStore.cs`
- `Kor.Opportunities.Knowledge/Claims/ClaimFile.cs`
- `Kor.Opportunities.Knowledge/Claims/ClaimValidation.cs`
- `docs/BD-Knowledge-Claims.md`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdDirectorKnowledgeRegressionTests.cs`
- `docs/codex/CODEX-BD-DIRECTOR-9D-CLAIMS-AND-CONCEPTS-RESPONSE.md`

Tracked task changes (thirteen, including retained pre-existing edits): Knowledge
store interface/implementation, ingest Program, Knowledge regression class,
Director row types/view model/window XAML/code-behind, NoteDialog code-behind,
OpportunitiesModule and the three existing Director/render test files. NoteDialog
is the one additional edited file needed for required retirement reasons; its
optional-note callers retain their existing behavior.

Claude owns the changed-project Debug builds, filtered gates/negative controls,
migration 350 apply through NetworkOps, Enns export and in-session writing/import,
Knowledge-tab render/read, remaining books in Ian's order, and local install.
Ian has no execution step in this handoff. Claims files and exported passages stay
outside the repository beside the books.
