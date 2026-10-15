# BD Director 9e — the market and the brief

Applied as source edits on 2026-10-14. **Nothing was built or tested; no application,
CLI verb, gate, render, database tool or model was run or called. Neither migration
351 nor the KorMcp table script was applied. Neither the books, the passages nor
the claims were read.** No embedding, import, model download, deployment, service
action or commit was performed. Source review and whitespace checks only:
`git diff --check` is clean, including separate no-index checks of new files.
No executed pre-fix failure, post-fix pass, SQL verification or visual proof is claimed.

Prior art: the bounded 9b–9d briefs/responses, COO brief/Card chain, existing
Director/ledger/dashboard stores, Knowledge library and CLI, App client/options,
Director sources and fixtures. Production measurements were accepted as supplied.
Narrow dependency reads resolved stale line numbers and signatures: the current
Dashboard deadline filter/formatter and deadline presentation, opportunity list
signature/model fields, dashboard constructor and ledger record/interface types.
`McpServerOptions` names its URL property `ServiceUrl`. Existing unrelated
untracked PDFs, task briefs and vocabulary file were left alone.

## Applied changes

- **Piece 1 — migration 351:** Individually guarded claim embedding/model columns;
  guarded relation table with foreign keys, composite primary key, kind/confidence/
  provenance/self-link checks; guarded reverse-claim index. The last batch grants
  SELECT on `knowledge` only if `mcp_app` exists. No database selection, destructive
  SQL or knowledge write grant.
- **Piece 2 — KorMcp table:** `CreateDirectorBriefTable.sql` mirrors the COO table,
  renamed constraints/index included, with the required non-null citations JSON
  column and empty-array default. The deploy runbook names this one-time SQL step
  and requires model, vocabulary and ONNX native checks before deployment.
- **Piece 3 — claim vectors/search:** Added the store contracts and typed SQL reads.
  Repair stages at most 500 distinct claim vectors in `#KnowledgeClaimEmbeddings`
  and performs one NULL-only joined UPDATE inside a transaction. Retired claims
  are excluded. Search caches vectors once per instance, ranks by cosine with a
  stable ID tie-break, applies the per-source cap and resolves active claim rows
  in batches. Retirement is checked again during resolution; later eligible hits
  fill gaps left by newly retired claims. The configured model name is enforced.
- **Piece 3 — relations/CLI:** Added camel-case relation JSON, per-row rejection
  reasons, active-ID validation and duplicate skipping. Import locks referenced
  active claims and relation keys in a serializable transaction; an existing
  relation is preserved. CLI dispatch/help now includes `claims embed`,
  `claims relate` and `claims search`. Embedding loops in batches of 500 and prints
  the requested total; search prints score/book/kind/statement. Relate reads text
  with BOM handling and reports written/rejected counts and reasons. Import and
  relate return before constructing an embedder.
- **Piece 4 — MCP brief:** Five Director sections, bounded facts blocks, meaning
  queries and explicit FACTS/CLAIMS prompts. Runtime generation uses the existing
  `AskService`, offers at most twelve claims, filters citation IDs to those offered
  and stores resolved citation snapshots. Each section has its own failure boundary;
  cancellation propagates. Fenced JSON is accepted; malformed/non-JSON answers
  are skipped with a warning. Missing claims allow facts-only generation and add
  an explicit sentence to the stored body. An uncited recommendation keeps an empty
  citation list. Generation is serialized across scheduled and manual runs.
- **Piece 4 — persistence/wiring:** Latest/history/acknowledge/run-now endpoints sit
  beside the COO endpoints under existing authentication. Latest returns the newest
  row per section in the newest stored week; history retains earlier attempts.
  Acknowledgement preserves the first actor/time. The scheduler calls the Director
  after the COO Card in its own try. DI supplies a singleton embedder/store and a
  fresh search/generator per job or request. The MCP references Knowledge, retains
  its Playwright/Graph exclusions and is version **0.5.0**.
- **Piece 5 — Market:** Appended tab seven with the exact caption, funnel counts
  and four grids. Existing dashboard and opportunity stores supply planned projects,
  open seats and closing RFPs. The deadline filter matches the Dashboard and reuses
  its row formatter. The sole new Director SQL read selects recent active signals
  joined to active organisations. Market reads happen on load/refresh, with no tab
  selection handler.
- **Piece 5 — This week:** Appended tab eight with the exact caption, five-section
  list, generated dates through `BdDay`, Run now and Acknowledge. The Basic-auth
  client uses existing MCP options and forwards the signed-in actor. Run now is
  disabled while pending and reloads after completion. A new read-only
  `IKnowledgeClaimStore.GetClaimAsync` includes retired claims for citation resolution.
  Retired citations stay visible with their reason; a missing claim uses the saved
  snapshot. Citation selection reads the passage by ID through the same template
  and cancellation-controlled pane used by Knowledge. The constructor has ten
  dependencies; DI and all three older fixtures are updated.

## Gates authored, not executed

| Brief gate | Source coverage |
|---|---|
| 1 — tabs/captions/words | Eight tabs in order, exact static captions, shared passage template, plain-word literals across every Director C#/XAML file. Older tab expectations updated. |
| 2 — client | Fake HTTP handler checks options, Basic authorization and latest/run-now/acknowledge paths and methods. No network access. |
| 3 — citations | Distinct claim resolution, retired marking, retained citations and passage lookup by ID. |
| 4 — Market reads | Exact sizes and one call each, shared deadline semantics excluding dismissed/past/distant rows, no tab selection read handler. |
| 5 — dates | Director date ratchet remains zero; new date bindings use `BdDay`. |
| Additional App gate | Pending Run now disables repeat submission and reloads five sections when completed. |
| 6 — prompts | Every section includes FACTS, CLAIMS, the output shape and offered-only citation instruction. |
| 7 — parser | Fenced JSON, offered-ID filtering, deduplication, uncited recommendation and non-JSON warning/no row. |
| 8 — isolation | A synthetic third-section Ask failure still attempts all five and inserts four rows. Production orchestration accepts delegates because `AskService` is sealed. |
| 9 — facts | All five blocks bounded to 6,000 characters on a fixture with 101 pursuits and 40 accounts; truncation identified. |
| 10 — table | Director SQL normalizes to COO SQL after removing the citations column; its empty-array default is checked. |
| 11 — embedding | Temp staging, single NULL-only UPDATE, active-claim guard, model field, transaction and CLI batches of 500. |
| 12 — search | Fake-vector source caps, initial retired exclusion, retirement after index caching, backfill and one vector load. |
| 13 — relations | Invalid kind, self-link, missing/retired ID, high/negative/missing confidence, long note and null row each have a reason; duplicates skip; JSON round trip and locked SQL duplicate guard. |
| 14 — migration | Additive guards, constraints, index and exactly one SELECT-only grant. |
| Existing write boundary | Eleven allowed Knowledge write statements, unchanged existing passage deletion guard, and only the added claim-repair staging destination/relation insert. |
| Render harness | Adds `director-market.png` and `director-week.png`; minimum total thirteen. No PNG was produced. |

## Fixture details and limits

The Market fixture contains three signals, three in-window RFPs, three planned
projects and two seats. The brief fixture has five sections and two citations per
section, including a retired claim. All new wording is marked illustrative; no
production records, book text or extracted claims supplied it. The Knowledge render
keeps the previously supplied book IDs/counts and illustrative passage content.

The specified server facts-store list has no RFP deadline read. Its Market block
therefore supplies signals, forward projects and funnel counts and explicitly says
deadline rows are absent. The App Market tab does show the Dashboard's closing
RFPs. No new deadline SQL was introduced. Pursuit movement is the existing 90-day
measure, not a fabricated weekly delta. Missing target actuals remain unrecorded,
not zero. Long fact blocks identify omitted rows; they are not exhaustive exports.

Relations imported from session-authored files use `LinkedBy = Model`; `CreatedBy`
uses `--by` or the file's `writtenBy`. This release creates/imports relations; it does
not generate them or add relation traversal to claim ranking. Repeat imports do not
change existing relation confidence, note or provenance.

Snapshots preserve citations historically; current claim metadata supplies text,
book and retirement status when available. A failed brief refresh preserves existing
sections with a visible message. A partial rerun may show sections with different
generation dates, which are displayed. Mechanical ID filtering cannot establish
that a recommendation is supported by its cited claim or fits the firm.

SQL source gates do not prove syntax, permissions, concurrency, rollback or query
performance. No build or render establishes package compatibility, native/model
publish propagation, bindings, layout or live API behavior in this session.

## Files and handoff

Thirteen new task artifacts: migration 351; Director table SQL; five MCP Director
files; MCP Director tests; two relation files; App client; App Market/brief tests;
this response. Twenty-three tracked files changed across App, MCP, Data, Knowledge,
CLI, fixtures and the deploy runbook. The two Data claim-store files are the small
additional read contract needed to show historical citations as retired.

Claude owns changed-project Debug builds, the gate classes/negative controls,
migration 351 and KorMcp table application, claim embedding, MCP publish by the
runbook with asset verification, the first runtime brief, citation review, both
new tab renders/read-through and local App installation. **Ian has no execution
step in this handoff.**
