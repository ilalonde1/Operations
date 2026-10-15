# BD Director 9f — the brief is written in session

Applied as source edits on 2026-10-15. **Nothing was built, run, applied to a database
or deployed.** No application, CLI verb, test, gate, render, embedding, model download,
service action or commit was performed. The Books folder, claims files and live
database were not opened. Source inspection and whitespace checks only;
`git diff --check` is clean, including separate no-index checks of the new files.
No pre-fix failure, post-fix pass, SQL verification or visual proof is claimed.

**No generative-model or gateway call exists anywhere in this change.** The Director
answers now come exclusively from the session-authored sections file. Export uses
the existing local meaning-search embedder required by the brief; import constructs
no embedder. The MCP Director generation path is deleted.

Prior art searched/read: the 9e brief and response, MCP Director implementation and
facts fixture, claim-file validation/import conventions, migrations 348–351, existing
Director/ledger/dashboard and claim stores, Knowledge search, CLI dispatch, App
Director sources, shared fixtures, render harness and MCP deploy runbook. The 9e
facts renderer was moved with only its namespace changed. Source comparison confirms
10 of 10 question/meaning-query literals preserved verbatim. Existing unrelated
untracked PDFs, briefs and the vocabulary file were left alone.

## Applied changes

- **Piece 1 — migration 352:** Two guarded tables in `opportunities`, the specified
  section constraint, citation default, author/import/acknowledgement fields and
  file/export hash ledger. One guarded week/section index. No database selection,
  data statements, grants or changes to the old KorMcp table.
- **Piece 2 — Data store:** Shared typed rows/citations and `IDirectorBriefStore`.
  Latest reads one newest row per section within the newest week; history reads
  the requested newest distinct weeks. Acknowledgement preserves the first actor
  and timestamp. Import validates the shared shape, locks the ledger hash, resolves
  current claim statements/source titles while checking retirement, and writes the
  five rows and ledger in one serializable transaction. A repeated valid file hash
  returns zero rows, including after a cited claim has retired; force revalidates
  citations before appending history. Claims are locked until commit so a
  concurrent retirement cannot slip into a new brief.
- **Piece 3 — Knowledge:** Moved the unchanged bounded facts renderer; added plain
  questions, five-section export, camel-case JSON, BOM-aware parsing, file-wide
  validation and import coordination. Each export search requests twelve claims;
  fewer, including zero, is valid. Checks cover exactly five distinct sections,
  headline/body limits, headline line breaks, matching week, section-specific
  offered IDs, and current claim retirement. Recommendations may be uncited.
- **Piece 4 — CLI:** Added both brief verbs, argument validation and help. Export
  prints facts lengths and claim counts followed by its path. The default week is
  strictly the next Sunday in Pacific time, including a seven-day advance when
  invoked on Sunday; an explicit week is used as supplied. Import hashes original
  bytes, reports each rejection on its own line with a nonzero exit, and prints
  the committed count or a duplicate-skip message before any embedder construction.
- **Piece 5 — MCP:** Removed all five Director files, the table script and old
  Director tests, registrations, four endpoints and the alert-job call. Removed
  the Knowledge project reference and set version 0.5.1. The Hosting scan now
  covers MCP and Data. The runbook identifies `Mcp.DirectorBrief` as unused and
  leaves dropping it to Ian; its three mirror operations now exclude `Logs`.
  Removed the model/vocabulary/ONNX pre-stop checks.
- **Piece 6 — App:** Removed the HTTP client/registration and generation action.
  The tenth constructor argument is the shared store, registered on the existing
  Opportunities connection. Load/refresh read imported sections; acknowledgement
  passes the window's resolved actor. The exact new caption, citation/passage pane,
  retirement marking and `BdDay` date presentation are retained, with dates now
  sourced from `ImportedAtUtc`.

One layout adjustment resolves a circular dependency in the requested signatures:
`DirectorBriefFile` and `DirectorBriefSection` live with the shared Data contracts
in `Crm/IDirectorBriefStore.cs`, because the Data store accepts them and Knowledge
now references Data. `Knowledge/Brief/DirectorBriefFile.cs` contains their JSON codec
and import coordinator (`DirectorBriefFiles`); `DirectorBriefValidation.cs` owns
export validation and exposes the shared current-citation gate. Data checks the
shared shape and current citations at its transactional write boundary.
There is no Data-to-Knowledge reference or duplicate file type.

## Gates authored, not executed

- `BdDirectorMarketAndBriefRegressionTests`: database load count and refresh,
  absence of the client and generation action, actor forwarding, imported date,
  retired citations and passage resolution, eight tabs, captions and date ratchet.
  Both empty and populated shared brief fixtures now supply `IDirectorBriefStore`,
  covering the existing Director/ledger/knowledge fixture consumers.
- `BdScreensRenderTests`: the five-section store fixture still supplies two
  citations per section with one retired claim. `director-week.png` remains in
  the eight Director renders; the overall minimum remains thirteen. After selecting
  all eight tabs, the harness asserts exactly one brief read.
- `Knowledge.Tests/DirectorBriefTests`: ported the 101-pursuit/40-account facts
  fixture; export content/counts and camel-case round trip; individual validation
  reasons, empty-claim acceptance, BOM-inclusive hashes, malformed inputs,
  rejection before writing, repeat/force behavior even after retirement with a fake store, and Sunday
  defaults across the Pacific/UTC day boundary.
- `BdDirectorRegressionTests`: migration 352 shape/guards and the new store's exact
  two INSERT statements plus one acknowledgement UPDATE; existing read-only
  Director-store gate remains. Also checks hash/claim locking and latest/history
  query clauses as source.
- `EndpointParameterBindingTests`: narrowed assembly scan and removal gates for
  Director types/routes, the Knowledge reference and alert call; retains the
  binder-shape negative control.

## Fixture details and limits

Source inspection found zero generation-action markers and zero inline date
formats across the twelve Director C#/XAML files, and `/XD Logs` on three of three
runbook mirror lines. These are source observations, not executed application gates.

Facts retain 9e's omissions and truncation notices, including the missing Market
deadline rows and the 90-day pursuit movement measure. Citation validation proves
ID eligibility, not whether the recommendation is well supported. Saved citation
snapshots are resolved at import time; the UI still uses current claim metadata
when available and preserves retired/unavailable citations for historical reading.

The SQL and fake-store gates do not establish SQL validity, permissions, concurrent
import behavior, rollback or performance. A failed import must leave both tables
untouched; that transaction behavior still needs live verification. No build or
render establishes compilation, WPF layout or publish contents in this session.
A fresh MCP publish should omit Knowledge's model/native assets; old staging output
can retain files from an earlier publish, so use fresh staging for the handoff check.

## Files and handoff

Ten new artifacts: migration 352, two Data contract/store files, five Knowledge
Brief files, Knowledge brief tests and this response. Sixteen existing files edited
and eight deleted. The shared fixture changes cover other Director constructors
without editing their callers individually.

Claude owns the changed-project Debug builds and filtered gates, their negative
controls, export for the explicitly requested 2026-10-19 week, writing the five
sections in session, import, rendering and reading `director-week.png`, and local
App installation. Migration 352 remains for Ian in SSMS or NetworkOps once signed
in. MCP 0.5.1 publication remains on Ian's word, following the corrected runbook,
with `/health` verification and confirmation that fresh publish output contains
no `LocalEmbeddingsModel` folder. No commit was made.
