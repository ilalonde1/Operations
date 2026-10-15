# BD Director 9f — the brief is written in session, and the MCP server leaves the loop

Bounded brief for Codex. Source edits only; nothing is built, run, applied or deployed by you.
Response goes beside this file as `CODEX-BD-DIRECTOR-9F-THE-BRIEF-WRITTEN-IN-SESSION-RESPONSE.md`
in the 9a–9e format.

## Why this exists

9e (`832ffafa`, fix `369fe574`) generated the Director's weekly brief at runtime on the MCP server
through the model gateway. That design was wrong: CLAUDE.md rule 14 — no model call anywhere in
the BD Director / knowledge work. The server was rolled back to 0.4.2 on 2026-10-15; the 9e
server code is in git only. This brief removes that path entirely and replaces it with the method
every other part of this line of work already uses: **the platform exports the inputs to a file,
Claude writes the output in its own session, a CLI verb imports it.** The App reads a table.

The MCP server has no part in the Director's brief after this. It keeps nothing of 9e's Director
code, and it stops referencing the Knowledge library (which only the deleted path needed).

## The decisions, so you build the one thing

- **Where the brief lives:** `opportunities.DirectorBrief` in KorOpportunitiesDb (migration 352),
  not `Mcp.DirectorBrief` in KorMcp. One database, the existing app connection, no new secret.
  The KorMcp table created on 2026-10-15 becomes unused; the runbook says so and leaves dropping
  it to Ian.
- **Who writes it:** the CLI, from a sections file Claude wrote in session, after validating it
  against the export it answers. Nothing else writes the table. The App only reads and
  acknowledges.
- **What the export carries:** per section, the question, the facts block (the 9e `DirectorFacts`
  reads, same stores, same 6,000-character bound) and the twelve nearest claims by meaning search
  (the 9e `ClaimsQuery` per section, `KnowledgeSearch.SearchClaimsAsync`). Claude reads that and
  writes the five sections; citations may only name offered claim ids.
- **The five sections and their questions are unchanged** from 9e (ThisWeek, Pursuits, Accounts,
  Market, Asks). They move from the deleted prompts file to a plain-text constants file, because
  they are now questions for Claude to read, not a prompt for a gateway.
- **Run now is gone.** The This week tab shows the latest imported sections, their citations and
  the passage pane, and Acknowledge. The caption says where the sections come from.
- **No model call anywhere in this brief.** If a piece seems to need one, stop and say so in the
  response; do not add one.

## Pieces

### Piece 1 — migration `Kor.Opportunities.Data/Schema/352_DirectorBrief.sql`

Same shape as 348–351: guarded batches, no `USE`, no data statements, no grants.

- `opportunities.DirectorBrief`: `Id BIGINT IDENTITY PK`, `WeekOf DATE NOT NULL`, `Section
  NVARCHAR(32) NOT NULL` CHECK `IN (N'ThisWeek', N'Pursuits', N'Accounts', N'Market', N'Asks')`,
  `Headline NVARCHAR(512) NOT NULL`, `Body NVARCHAR(MAX) NOT NULL`, `Recommendation NVARCHAR(MAX)
  NULL`, `Citations NVARCHAR(MAX) NOT NULL DEFAULT N'[]'` (JSON array of
  `{ "claimId", "passageId", "statement", "source" }`), `WrittenBy NVARCHAR(100) NOT NULL`,
  `ImportedAtUtc DATETIMEOFFSET NOT NULL DEFAULT sysdatetimeoffset()`, `ImportedBy NVARCHAR(100)
  NOT NULL`, `AcknowledgedAtUtc DATETIMEOFFSET NULL`, `AcknowledgedBy NVARCHAR(100) NULL`;
  index `IX_Opportunities_DirectorBrief_WeekSection (WeekOf DESC, Section)`.
- `opportunities.DirectorBriefImport`: the `knowledge.ClaimImport` ledger shape — `Id`,
  `WeekOf`, `FileName`, `FileSha256 CHAR(64)`, `ExportSha256 CHAR(64)`, `ImportedAtUtc`,
  `ImportedBy`, `SectionsWritten INT`, `Forced BIT`. A repeat import of the same file hash is
  skipped unless `--force`; `--force` inserts new rows (history keeps the old ones).

### Piece 2 — the Data store

`Kor.Opportunities.Data/Crm/IDirectorBriefStore.cs` and `SqlDirectorBriefStore.cs`, beside the
other Director stores:

- `GetLatestAsync()` → up to five `DirectorBriefRow` (newest row per section within the newest
  `WeekOf` that has any row).
- `GetHistoryAsync(int weeks)` → rows for the newest `weeks` weeks, newest first.
- `AcknowledgeAsync(long id, string by)` → preserves the first actor and time, as 9e did.
- `ImportAsync(DirectorBriefFile file, string exportSha256, string fileName, string fileSha256,
  string by, bool force)` → `(int SectionsWritten, IReadOnlyList<string> Rejected)`; one
  transaction; the ledger row is written in the same transaction.
- `DirectorBriefRow(long Id, DateOnly WeekOf, string Section, string Headline, string Body,
  string? Recommendation, IReadOnlyList<DirectorCitation> Citations, string WrittenBy,
  DateTimeOffset ImportedAtUtc, DateTimeOffset? AcknowledgedAtUtc, string? AcknowledgedBy)` and
  `DirectorCitation(long ClaimId, long PassageId, string Statement, string Source)` live in Data
  (they are read by the App and written by the CLI). Register the store in
  `OpportunitiesModule.cs` beside `SqlBdDirectorStore`.

The store's write boundary gate (the existing "allowed write statements" test for Director SQL,
`BdDirectorRegressionTests`) extends by exactly: the two inserts of `ImportAsync` and the one
update of `AcknowledgeAsync`.

### Piece 3 — the Knowledge library: export and the sections file

- Move `DirectorFacts` from `Kor.Operations.Mcp/Director/DirectorFacts.cs` to
  `Kor.Opportunities.Knowledge/Brief/DirectorFacts.cs` unchanged in behaviour: the same store
  reads, the same text-table rendering, the same 6,000-character bound per block, constructed on
  the KorOpportunitiesDb connection string. It stays a facts renderer; it answers nothing.
- `Kor.Opportunities.Knowledge/Brief/DirectorQuestions.cs`: `enum DirectorSection { ThisWeek,
  Pursuits, Accounts, Market, Asks }`, `Question(section)` (the five 9e questions, verbatim, minus
  the output-shape instruction) and `ClaimsQuery(section)` (the five 9e meaning queries).
- `Kor.Opportunities.Knowledge/Brief/DirectorBriefExport.cs`: `DirectorBriefExportFile(DateOnly
  WeekOf, DateTimeOffset ExportedAtUtc, IReadOnlyList<ExportSection> Sections)`,
  `ExportSection(string Section, string Question, string ClaimsQuery, string Facts,
  IReadOnlyList<OfferedClaim> Claims)`, `OfferedClaim(long ClaimId, long PassageId, long SourceId,
  string SourceTitle, string Kind, string Statement, float Score)`. Twelve claims per section from
  `KnowledgeSearch.SearchClaimsAsync(ClaimsQuery(section), 12)`; fewer is allowed and is not an
  error. Camel-case JSON, like `ClaimFile`.
- `Kor.Opportunities.Knowledge/Brief/DirectorBriefFile.cs` + `DirectorBriefValidation.cs`: the
  file Claude writes. `{ "weekOf": "2026-10-19", "writtenBy": "…", "sections": [ { "section":
  "ThisWeek", "headline": "…", "body": "…", "recommendation": "…" | null, "citations": [ 123, 456 ]
  } ] }`. Validation, each with its own reason string: exactly the five sections, each once;
  headline 1–512 chars, body 1–20,000 chars, no line breaks in the headline; `weekOf` equals the
  export's; every citation id is in that section's offered claims in the export, and the claim is
  not retired at import time; a recommendation may have no citations. Rejection of any section
  rejects the file; nothing is written.

### Piece 4 — the CLI (`tools/BdKnowledgeIngest/Program.cs`)

Two verbs, added to the header and the switch beside `claims …`:

- `brief export --week <yyyy-MM-dd> --to <folder>` → `<folder>\<week>-director-brief.export.json`;
  prints each section's facts length and claims count, one line per section, then the path. `--week`
  defaults to the next Sunday (the weekly cadence of the alert runner).
- `brief import <export.json> <sections.json> --by <name> [--force]` → validates, resolves the
  citation snapshots from the store (statement and source title at import time), writes rows and
  the ledger, prints `OK 5 sections written  <file>` or every rejection reason, one per line, and
  exits non-zero on rejection. Returns before constructing an embedder (import needs no vectors).

### Piece 5 — the MCP server leaves the loop (`Kor.Operations.Mcp`, version 0.5.1)

- Delete `Director/DirectorBriefGenerator.cs`, `Director/DirectorBriefPrompts.cs`,
  `Director/DirectorFacts.cs` (moved, piece 3), `Director/DirectorBriefRepository.cs`,
  `Director/DirectorBriefTypes.cs`, `Sql/CreateDirectorBriefTable.sql`, and
  `Kor.Operations.Mcp.Tests/Director/DirectorBriefTests.cs`.
- `Program.cs`: remove the Director registrations (lines 137–151 as of `369fe574`) and the four
  `/director-brief/…` endpoints (lines 339–352). `Alerts/AlertJob.cs`: remove the Director call
  after the Card. `Kor.Operations.Mcp.csproj`: remove the `Kor.Opportunities.Knowledge` project
  reference and its comment; bump `<Version>` to `0.5.1`. The local embedder, its model folder and
  the ONNX native no longer ship with the MCP.
- `docs/runbooks/Kor.Operations.Mcp.deploy.md`: in the one-time SQL step, replace the
  `CreateDirectorBriefTable.sql` line with one sentence: `Mcp.DirectorBrief` in KorMcp is unused
  since 9f and may be dropped. Remove the model/vocab/ONNX pre-stop check. **Add `/XD Logs` to
  both robocopy mirror lines (snapshot and copy) and to the rollback restore**: on 2026-10-15 the
  mirror wiped the server's `Logs` folder on every copy and the rollback erased the 10:46–10:58
  lines of that day's log.
- `Kor.Operations.Mcp.Tests/Hosting/EndpointParameterBindingTests.cs` stays, with its Data and
  Knowledge assembly scan replaced by the MCP assembly plus Data only (the MCP no longer references
  Knowledge; the type reference used to load that assembly would not compile).

### Piece 6 — the App's This week tab reads the store

- Delete `Kor.Operations.App/Services/DirectorBriefClient.cs` and its `AppModule.cs` registration.
- `BdDirectorViewModel`: the tenth constructor parameter becomes `IDirectorBriefStore brief`;
  `GetLatestAsync()` on load and refresh; `AcknowledgeAsync(id, actor)` on the button; no Run now.
  Update both fixtures and the render harness.
- `BdDirectorWindow.xaml`: remove the Run now button and the pending text; `BriefCaption` =
  `The Director's brief: five questions a week over these tabs and the rule bank, written from the week's export and imported here. Open a citation to read the passage.`
  Citations, the passage pane (`IKnowledgeClaimStore.GetPassageAsync`), retired marking and the
  generation date via `BdDay` stay exactly as 9e built them, reading `ImportedAtUtc`.

### Piece 7 — gates

**App, `BdDirectorMarketAndBriefRegressionTests.cs` (edit the existing class):** replace the
client gate with (1) the tab calls `IDirectorBriefStore.GetLatestAsync()` once on load and never
on tab change; (2) no type named `DirectorBriefClient` exists in the App assembly and no Director
XAML or C# file contains `run-now` or `Run now`; (3) citations still resolve through
`IKnowledgeClaimStore.GetPassageAsync` and a retired claim still shows marked; (4) the eight tabs
and both captions unchanged except the new `BriefCaption` text; (5) the date ratchet still zero.
Render `director-week.png` from a store fixture of five sections with two citations each, one
retired; minimum total still thirteen.

**Knowledge.Tests, new `DirectorBriefTests.cs`:** (6) `DirectorFacts` blocks ≤ 6,000 characters on
the 9e fixture of 101 pursuits and 40 accounts (port of 9e gate 9); (7) export carries five
sections with question, claims query, facts and ≤ 12 offered claims, camel-case round trip; (8)
validation rejects, each with a reason: a missing section, a sixth section, a duplicate section, a
headline over 512, a line break in a headline, a wrong `weekOf`, a citation not offered for that
section, a retired citation; and accepts a recommendation with no citations and a section with
zero offered claims; (9) a repeat import of the same file hash is skipped unless forced (fake
store).

**Data / Mcp.Tests:** (10) migration 352 is additive, guarded, two tables, one index, no grant;
(11) no type remains in a namespace starting `Kor.Operations.Mcp.Director`, `Program.cs` has no
`/director-brief` literal, and `Kor.Operations.Mcp.csproj` has no `Kor.Opportunities.Knowledge`
reference; (12) the Hosting gate still passes with the narrowed assembly list.

## Claude's run after apply (not yours)

Build App tests, Knowledge tests, Mcp, Mcp.Tests, CLI; run the gate classes; apply 352 (Ian in
SSMS, or NetworkOps once signed in); `brief export --week 2026-10-19`; write the five sections in
session into `C:\Users\ilalonde\Desktop\BD\Briefs\2026-10-19-director-brief.sections.json`;
`brief import`; render and read `director-week.png`; install the App locally; publish MCP 0.5.1
by the runbook (with `/XD Logs`) on Ian's word, confirm `/health` and that the publish output has
no `LocalEmbeddingsModel` folder.

## Read bound

About 160 KB:
- `docs/codex/CODEX-BD-DIRECTOR-9E-THE-MARKET-AND-THE-BRIEF.md` and its RESPONSE
- `Kor.Operations.Mcp/Director/` (all five, to move and delete), `Program.cs` lines 100–160 and
  320–360, `Alerts/AlertJob.cs`, `Kor.Operations.Mcp.csproj`, `Sql/CreateDirectorBriefTable.sql`
- `Kor.Operations.Mcp.Tests/Director/DirectorBriefTests.cs`, `Hosting/EndpointParameterBindingTests.cs`
- `docs/runbooks/Kor.Operations.Mcp.deploy.md`
- `Kor.Opportunities.Data/Crm/IBdDirectorStore.cs`, `SqlBdDirectorStore.cs`, `Schema/348_…`,
  `Schema/351_…`, `Knowledge/IKnowledgeClaimStore.cs`
- `Kor.Opportunities.Knowledge/Claims/ClaimFile.cs`, `ClaimValidation.cs`,
  `Search/KnowledgeSearch.cs`, `Store/IKnowledgeStore.cs`, csproj;
  `Kor.Opportunities.Knowledge.Tests/KnowledgeIngestRegressionTests.cs` (summary and fixtures)
- `tools/BdKnowledgeIngest/Program.cs`
- `Kor.Operations.App/Services/DirectorBriefClient.cs`, `BusinessDevelopment/Director/` (all),
  `CompositionModules/AppModule.cs` lines 115–135, `OpportunitiesModule.cs` lines 140–150 and
  260–280, `Kor.Transmittals.App.Tests/BusinessDevelopment/BdDirectorMarketAndBriefRegressionTests.cs`,
  `BdDirectorRegressionTests.cs`, `BdScreensRenderTests.cs` `RenderDirector`

Do not open the Books folder, any `.claims.json`, or the live database. App projects with
`UseWPF` have no implicit `System.IO`.

## Files

Creates: `Kor.Opportunities.Data/Schema/352_DirectorBrief.sql`; `Kor.Opportunities.Data/Crm/IDirectorBriefStore.cs`,
`SqlDirectorBriefStore.cs`; `Kor.Opportunities.Knowledge/Brief/DirectorFacts.cs`, `DirectorQuestions.cs`,
`DirectorBriefExport.cs`, `DirectorBriefFile.cs`, `DirectorBriefValidation.cs`;
`Kor.Opportunities.Knowledge.Tests/DirectorBriefTests.cs`; the RESPONSE.

Deletes: the five `Kor.Operations.Mcp/Director/*.cs`, `Kor.Operations.Mcp/Sql/CreateDirectorBriefTable.sql`,
`Kor.Operations.Mcp.Tests/Director/DirectorBriefTests.cs`, `Kor.Operations.App/Services/DirectorBriefClient.cs`.

Edits: `Kor.Operations.Mcp/Program.cs`, `Alerts/AlertJob.cs`, `Kor.Operations.Mcp.csproj`,
`Kor.Operations.Mcp.Tests/Hosting/EndpointParameterBindingTests.cs`, `docs/runbooks/Kor.Operations.Mcp.deploy.md`;
`tools/BdKnowledgeIngest/Program.cs`; `Kor.Opportunities.Knowledge/Kor.Opportunities.Knowledge.csproj` (Data reference if absent);
`BdDirectorWindow.xaml` + `.xaml.cs`, `BdDirectorViewModel.cs`; `AppModule.cs`, `OpportunitiesModule.cs`;
`BdDirectorMarketAndBriefRegressionTests.cs`, `BdDirectorRegressionTests.cs`, the other Director fixtures,
`BdScreensRenderTests.cs`.

## Response

The 9a–9e format. State plainly that nothing was built, run, applied or deployed, and that no
model call exists anywhere in the change. `git diff --check` clean. Do not commit.
