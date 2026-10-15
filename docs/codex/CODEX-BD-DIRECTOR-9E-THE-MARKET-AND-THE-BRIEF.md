# Codex — BD Director 9e: the market tab, the Director's brief, and claims the reasoner can find

**DO NOT BUILD. DO NOT RUN TESTS. DO NOT RUN ANY TOOL AGAINST A DATABASE. DO NOT CALL ANY MODEL.
DO NOT DEPLOY. DO NOT READ THE BOOKS, THE PASSAGES OR THE CLAIMS.** Make the edits, add the
migrations and the regression gates as source, and ping when applied. Claude builds, runs the
gates, applies the migrations on the server, publishes the MCP service by its runbook, embeds
the claims, runs the first brief, renders the two new tabs and reads them, and installs the app
locally. Ian runs nothing.

## Why this exists

Briefs 9a–9d built the Director window (six tabs), the ledger of targets, asks and decisions,
the knowledge store (18 books, 8,515 passages) and the rule bank (claims cited to passages,
tagged to concepts; Enns is in, 214 claims, the rest follow one book at a time by the same
method). Two things are still missing before any of it reasons:

1. **The market.** The window has no view of what is coming: the early-signal radar, the forward
   pipeline, RFPs closing, open structural seats. Every one of those reads exists for the BD
   Dashboard today; the Director has no tab that shows them.
2. **The brief.** Nothing synthesises. The MCP server already writes a weekly COO brief by asking
   the model six questions over the firm's data. This brief adds the Director's brief in the same
   shape: five questions a week over the Director's own facts **and the claims that fit**, with
   every recommendation citing the claims it rests on. That requires the reasoner to find claims
   by meaning, so claims get embeddings, and a table for the relations between claims so that
   books that disagree can be seen to disagree.

The model is used **at runtime, on the server, through the API the server already uses** for
`/ask` and the COO brief. It is never used as a build step. Extraction of claims stays what it
is: a session task by the rulebook.

Measured 2026-10-14 at commit `2db6f183`. Use as given.

| Fact | Value |
|---|---|
| The COO chain | `Kor.Operations.Mcp/Alerts/AlertJob.cs` (Quartz, Monday 06:00 Pacific) runs `AlertRunner`, then `CooBriefGenerator.GenerateForWeekAsync(weekOf)`, then `CooCardGenerator`, each in its own try. `CooBriefGenerator` (`Brief/CooBriefGenerator.cs`, 7 KB) loops `BriefSection`, builds `BriefPrompts.For(section)` = `(AnalystOverlay, question)`, calls `AskService.AskAsync(new AskRequest(overlay + question) { UserUpn = "coo-brief@kor" })`, parses the JSON `{headline, body, recommendation}`, and `CooBriefRepository.InsertAsync(weekOf, RichBriefSection)`. Types in `Brief/BriefTypes.cs` |
| The COO brief table | `Kor.Operations.Mcp/Sql/CreateCooBriefTable.sql`: `Mcp.CooBrief (Id, GeneratedAt, WeekOf, Section, Headline, Body, Recommendation, InputTokens, OutputTokens, ToolCalls, AcknowledgedAt, AcknowledgedBy)` in the **KorMcp** database, created by hand per the deploy runbook (`docs/runbooks/Kor.Operations.Mcp.deploy.md`); `CooBriefRepository` reads it with `McpOptions.SqlConnectionString` |
| Endpoints | `Program.cs` lines 285–311: `GET /coo-brief/latest`, `GET /coo-brief/history?weeks=`, `POST /coo-brief/{id}/acknowledge`, `POST /coo-brief/run-now`. `BasicAuthMiddleware` on every route but `/health` |
| The MCP's Opportunities access | `Program.cs` lines 110–121 build a KorOpportunitiesDb connection string from `McpOptions.SqlConnectionString` with `InitialCatalog = "KorOpportunitiesDb"` (login `mcp_app`, **read** access) and construct `SqlBdReportService` with it |
| The MCP csproj | net8.0-windows, Web SDK, references `Kor.Operations.Core`, `Kor.Operations.Business`, `Kor.Opportunities.Data`; excludes Playwright and Graph runtime assets with a stated CONSTRAINT comment. **It does not reference `Kor.Opportunities.Knowledge`.** No Anthropic SDK; raw HTTPS in `AskService` |
| The App's MCP client | `Kor.Operations.App/Services/CooCardClient.cs` (4.5 KB): `McpServerOptions` (`AppOptions.cs` line 70: Url, Username, Password), `Authorization: Basic …`, `GET …/coo-card/latest`; registered in `CompositionModules/AppModule.cs` line 127 |
| The dashboard's market reads | `Kor.Opportunities.Data/MajorProjects/IBdDashboardStore.cs`: `GetOpenStructuralSeatsAsync(take)`, `GetCompetitorWatchAsync(take)`, `GetForwardPipelineAsync(take)` → `BdForwardPipelineRow(Id, ProjectName, ProponentName, Province, Sector, EstimatedCostCad, Stage)`, `GetPipelineFunnelAsync()` → `PipelineFunnel(RadarCount, BidWindowCount, OpenSeatCount)`. RFP deadlines: `DashboardViewModel.cs` line 265 `_opportunities.ListAsync(ct, includeClosed: false, includeNonPrime: false)` then its 30-day deadline rule (read lines 255–300) |
| The early-signal radar | `opportunities.IntelSignal (CanonicalOrgId, SignalType IN LeadershipChange/HiringSurge/OfficeMove/OwnershipMnA/CapacityStrain/RecentWin/…, Subject, Detail, OccurredAtApprox, SourceUrl, Corroborations, FirstSeenAtUtc, LastSeenAtUtc, RetiredAtUtc)` and `opportunities.IntelProjectSignal (MajorProjectsInventoryId, SignalType, Subject, …)` |
| The Director window | eight files in `Kor.Operations.App/BusinessDevelopment/Director/`; six `TabItem`s in order The number, People, Funnel, Accounts, Asks & decisions, Knowledge; `BdDirectorViewModel` ctor has seven parameters ending `IKnowledgeClaimStore claims`; the render harness writes six `director-*.png` and asserts `written >= 11` |
| The knowledge store | `knowledge.Claim` has no embedding; `knowledge.Passage.Embedding` is `VARBINARY(1536)` (384 × float32 LE, bge-small-en-v1.5); `Kor.Opportunities.Knowledge/Search/KnowledgeSearch.cs` searches passages; `Store/SqlKnowledgeStore.cs` has the temp-table repair pattern (`#KnowledgeEmbeddings`, batches of 500, one UPDATE); `tools/BdKnowledgeIngest` verbs: ingest, embed, search, list, export, claims import |
| 9a gate still in force | `SqlBdDirectorStore.cs` must stay SELECT-only |

## The decisions, so you build the one thing

- **Two tabs appended, order of the existing six unchanged.** Tab 7 **Market**, tab 8 **This
  week**. Nothing moves.
- **The Market tab reads what the Dashboard reads.** No new SQL for the forward pipeline, open
  seats, competitor watch, funnel counts or RFP deadlines; those come from `IBdDashboardStore`
  and the dashboard's own deadline rule over `IOpportunityStore.ListAsync`. One new read on the
  SELECT-only `IBdDirectorStore`: recent signals.
- **The Director's brief is the COO brief's twin.** Same generator shape, same repository shape,
  same table shape in KorMcp, same endpoints, same place in `AlertJob` (after the COO Card, its
  own try). Five sections. What differs: the prompt carries the Director's facts and up to twelve
  claims found by meaning, and the answer must cite claim ids from that set. A recommendation
  with no citation is stored with an empty citation list, never invented ones.
- **Claims become findable by meaning.** `knowledge.Claim` gains `Embedding`/`EmbeddingModel`;
  the CLI gains `claims embed`; `KnowledgeSearch` gains `SearchClaimsAsync`. The MCP references
  `Kor.Opportunities.Knowledge` for the search and the embedder only. The 33 MB model must land
  in the MCP publish output (the library's acquisition target does this for any project that
  references it; Claude checks `LocalEmbeddingsModel\bge-small-en-v1.5\model.onnx` and the ONNX
  runtime native in `C:\Publish\Kor.Operations.Mcp` before copying).
- **Relations are a table and an import, not a model pass.** `knowledge.ClaimRelation` holds
  supports / contradicts / refines between two claims. A relations file is written in session
  like a claims file and imported with `claims relate`. This brief builds the table and the verb;
  writing the first relations is Claude's later work, book by book.
- **The App shows the brief through the MCP, as the COO Card does.** A `DirectorBriefClient`
  modelled on `CooCardClient`, same options, same auth. Citations are resolved to claim text and
  book through `IKnowledgeClaimStore` so the Director can open the passage behind any
  recommendation. The Director can press **Run now**.
- **Plain words, the same gates, the same migration shape.**

## Pieces

### Piece 1 — migration `Kor.Opportunities.Data/Schema/351_KnowledgeClaimVectorsAndRelations.sql`

Same shape as 348–350:
- `ALTER TABLE knowledge.Claim ADD Embedding VARBINARY(1536) NULL, EmbeddingModel NVARCHAR(40) NULL`
  guarded by `COL_LENGTH`, its own batch.
- `knowledge.ClaimRelation`: `FromClaimId BIGINT` FK → `knowledge.Claim(Id)`, `ToClaimId BIGINT` FK
  → `knowledge.Claim(Id)`, `Kind NVARCHAR(12) NOT NULL` CHECK `IN (N'Supports', N'Contradicts',
  N'Refines')`, `Confidence DECIMAL(4,3)` CHECK 0–1, `LinkedBy NVARCHAR(10)` CHECK `IN (N'Model',
  N'Human')`, `Note NVARCHAR(500) NULL`, `CreatedAtUtc` default, `CreatedBy NVARCHAR(100) NOT NULL`,
  PK `(FromClaimId, ToClaimId, Kind)`, CHECK `FromClaimId <> ToClaimId`, index on `ToClaimId`.
- Last batch, guarded by the `mcp_app` principal existing: `GRANT SELECT ON SCHEMA::knowledge TO mcp_app;`
  (read only; the brief writes nothing to the knowledge schema). Comment says so.

### Piece 2 — `Kor.Operations.Mcp/Sql/CreateDirectorBriefTable.sql`

Exactly `CreateCooBriefTable.sql` with the name `Mcp.DirectorBrief`, the index
`IX_DirectorBrief_WeekSection`, and one more column: `Citations NVARCHAR(MAX) NOT NULL DEFAULT N'[]'`
(a JSON array of `{ "claimId": 123, "passageId": 8450, "statement": "…", "source": "…" }`). Add a
line to the deploy runbook's one-time SQL step naming the file.

### Piece 3 — the Knowledge library and the CLI

- `IKnowledgeStore`: `ListUnembeddedClaimsAsync(take)` → `(long Id, string Statement)`;
  `SetClaimEmbeddingsAsync(IReadOnlyList<(long ClaimId, byte[] Embedding)>, string model)` (temp
  table `#KnowledgeClaimEmbeddings` + one UPDATE, NULL-only, the 9c pattern);
  `LoadClaimVectorsAsync()` → `(ClaimId, SourceId, float[])` for `RetiredAtUtc IS NULL AND Embedding IS NOT NULL`;
  `GetClaimsAsync(ids)` → `(Id, SourceId, SourceTitle, PassageId, Kind, Statement, Confidence)`;
  `ImportRelationsAsync(RelationFile, by)` → `(int Written, int Rejected)`.
- `Claims/RelationFile.cs`: `{ "writtenBy": "…", "relations": [ { "from": 123, "to": 456, "kind": "Contradicts", "confidence": 0.8, "note": "…" } ] }`;
  `Claims/RelationValidation.cs`: kinds; `from != to`; both ids exist and are not retired; confidence
  0–1; note ≤ 500; a duplicate `(from, to, kind)` is skipped, not an error.
- `Search/KnowledgeSearch.cs`: `SearchClaimsAsync(string query, int take = 12, float minScore = 0f, int perSource = 3)`
  → `KnowledgeClaimHit(long ClaimId, long SourceId, string SourceTitle, long PassageId, string Kind, string Statement, float Score)`,
  vectors loaded once per instance, cosine, per-source cap, as the passage search does.
- CLI verbs, added to the header and the switch: `claims embed` (batches of 500, prints
  `embedded N claims`), `claims relate <file.json>` (prints written / rejected with reasons),
  `claims search "<question>" [--take 12]` (one hit per line: score, book, kind, statement).

### Piece 4 — the Director's brief on the MCP server (`Kor.Operations.Mcp/Director/`)

- `DirectorBriefTypes.cs`: `enum DirectorSection { ThisWeek, Pursuits, Accounts, Market, Asks }`;
  `DirectorCitation(long ClaimId, long PassageId, string Statement, string Source)`;
  `RichDirectorSection(DirectorSection Section, string Headline, string Body, string? Recommendation, IReadOnlyList<DirectorCitation> Citations, int InputTokens, int OutputTokens, int ToolCalls)`;
  `DirectorBriefRow` = `BriefRow` plus `Citations` (the JSON string, deserialised by the client).
- `DirectorBriefRepository.cs`: `CooBriefRepository` over `Mcp.DirectorBrief` with the citations column.
- `DirectorFacts.cs`: one read per section over KorOpportunitiesDb using the stores that already
  exist (`SqlBdDirectorStore.GetPeopleAsync/GetFunnelAsync/GetAccountsAsync/GetTargetActualsAsync/GetRecentSignalsAsync`,
  `SqlBdDirectorLedgerStore.ListTargetsAsync/ListAsksAsync(null)/ListDecisionsAsync`,
  `SqlBdDashboardStore.GetForwardPipelineAsync(12)/GetPipelineFunnelAsync()`), rendered as a
  compact plain-text table block per section, ≤ 6,000 characters, numbers only from the stores.
  Construct the stores with the KorOpportunitiesDb connection string as `Program.cs` lines 115–121 do.
- `DirectorBriefPrompts.cs`: `DirectorOverlay` (the COO `AnalystOverlay` reworded for the
  Director of BD of a sixty-person structural engineering consultancy whose business developers
  are its engineers) and `For(section)` → `(overlay, question)` with these five questions, each
  ending with the same instruction: *"Use the FACTS block and the CLAIMS block below. Output one
  JSON object: {"headline": …, "body": …, "recommendation": … or null, "citations": [claim ids
  from the CLAIMS block that the recommendation rests on]}. Cite only ids offered. If no claim
  applies, citations is []."*
  - ThisWeek: the one or two actions this week that would move the most, across people, pursuits and accounts.
  - Pursuits: what has moved, what has not, and which pursuit to decide on first.
  - Accounts: which Deltek clients to touch this month and why, given last touch and live pursuits.
  - Market: which signals, deadlines or forward-pipeline rows deserve a conversation this week.
  - Asks: which open asks are overdue or misdirected, and what to ask next.
  `ClaimsQuery(section)` → the meaning-search question for that section (e.g. Accounts: "when and how a professional firm should re-contact a dormant client").
- `DirectorBriefGenerator.cs`: for each section: facts → `KnowledgeSearch.SearchClaimsAsync(ClaimsQuery(section), 12)` → prompt = overlay + question + `FACTS:` block + `CLAIMS:` block (`[id] (Book) statement`) → `AskService.AskAsync(new AskRequest(prompt) { UserUpn = "director-brief@kor" })` → parse; citations filtered to the offered ids, resolved to `DirectorCitation` from the hits; `InsertAsync`. Each section in its own try, as the COO generator does. A missing or empty CLAIMS set is allowed: the section runs on facts alone and says so in one sentence of the body.
- `Program.cs`: register `DirectorFacts`, `DirectorBriefRepository`, `DirectorBriefGenerator`,
  `KnowledgeSearch` (with a `KnowledgeEmbedder` singleton) and a `SqlKnowledgeStore` on the
  KorOpportunitiesDb string; endpoints `GET /director-brief/latest`, `GET /director-brief/history?weeks=`,
  `POST /director-brief/{id}/acknowledge`, `POST /director-brief/run-now`, beside the COO ones;
  `AlertJob` runs the Director generator after the Card, in its own try. Add
  `<ProjectReference Include="..\Kor.Opportunities.Knowledge\Kor.Opportunities.Knowledge.csproj" />`
  with a comment that it brings the embedder and PdfPig and that the ONNX runtime natives must
  reach the publish output. Bump `<Version>` to `0.5.0`.

### Piece 5 — the two tabs

**Market** (tab 7). Caption constant `MarketCaption` =
`What is coming: signals on the firms we watch, planned projects not yet procured, RFPs closing, and seats no structural engineer holds yet. All of it is what the BD Dashboard already reads.`
Four grids in a 2×2: **Signals, 30 days** (`IBdDirectorStore.GetRecentSignalsAsync(30, 25)`: Org,
Signal, Subject, Last seen `BdDay`, Corroborations; new SELECT-only read over `IntelSignal` joined
to `CanonicalOrg.DisplayName`, `RetiredAtUtc IS NULL`, ordered by `LastSeenAtUtc DESC`), **RFPs
closing in 30 days** (the dashboard's rule, same rows, same columns as its Deadlines grid),
**Forward pipeline** (`GetForwardPipelineAsync(12)`: Project, Proponent, Province, Sector, Est.
cost `C0`, Stage), **Open structural seats** (`GetOpenStructuralSeatsAsync(10)`). Above them one
line from `GetPipelineFunnelAsync`: `{RadarCount} on the radar · {BidWindowCount} in a bid window · {OpenSeatCount} open seats`.

**This week** (tab 8). Caption constant `BriefCaption` =
`The Director's brief: five questions a week over these tabs and the rule bank, answered with the claims each answer rests on. Open a citation to read the passage.`
A list of the five sections from `DirectorBriefClient.GetLatestAsync()` (headline bold, body,
recommendation, generated `BdDay`), each with its citations as `(Book) statement` rows; selecting
a citation shows the passage in the same pane the Knowledge tab uses (`GetPassageAsync`). Buttons:
**Run now** (`POST /director-brief/run-now`, then reload; disabled while running),
**Acknowledge**. `DirectorBriefClient` in `Kor.Operations.App/Services/`, registered in
`AppModule.cs` beside line 127; `BdDirectorViewModel` gains `IBdDashboardStore dashboard`,
`IOpportunityStore opportunities` and `DirectorBriefClient brief` (ten parameters; update both
fixtures and the harness).

### Piece 6 — gates

**App, `BdDirectorMarketAndBriefRegressionTests.cs`:** (1) eight tabs in order, the two captions
by `x:Static`, plain words over every Director file; (2) `DirectorBriefClient` uses
`McpServerOptions`, sends `Authorization: Basic`, calls `/director-brief/latest` and `/run-now`
(fake handler); (3) citations resolve through `IKnowledgeClaimStore.GetPassageAsync` and a citation
whose claim is retired still shows, marked retired; (4) the Market tab calls `GetForwardPipelineAsync(12)`,
`GetOpenStructuralSeatsAsync(10)`, `GetRecentSignalsAsync(30, 25)` once each on load and never on tab
change; (5) the date ratchet still zero in the Director folder. Render `director-market.png` and
`director-week.png`; minimum 13; fixtures: three signals, three RFPs, three forward rows, two seats
(illustrative, marked), one brief with five sections and two citations each.

**Mcp.Tests, `Director/DirectorBriefTests.cs`:** (6) every section's prompt contains `FACTS:`,
`CLAIMS:`, the JSON shape and the "cite only ids offered" sentence; (7) the parser drops a citation
id not in the offered set and keeps the rest; a fenced response parses; a non-JSON answer yields no
row and a logged warning; (8) the generator runs all five sections when one fails (fake AskService
throwing on section 3 → four rows inserted); (9) `DirectorFacts` blocks are ≤ 6,000 characters on a
fixture of 101 pursuits and 40 accounts; (10) `Mcp.DirectorBrief` SQL file mirrors the COO one
plus `Citations` with its default.

**Knowledge.Tests:** (11) `claims embed` uses the temp table and one UPDATE, NULL-only, batches of 500;
(12) `SearchClaimsAsync` caps per source and excludes retired claims (fake store); (13) relation
validation rejects each bad shape with a reason and skips duplicates; (14) migration 351 is additive,
guarded, grants SELECT only.

## Claude's run after apply (not yours)

Build App tests, Mcp, Mcp.Tests, Knowledge tests, CLI; run the gate classes; apply 351 on the
server through NetworkOps; create `Mcp.DirectorBrief` on KorMcp (the runbook's one-time step; if
the Opportunities login cannot reach KorMcp, Ian runs that one file in SSMS); `claims embed`;
publish the MCP by the runbook after confirming the model and the ONNX native are in the output;
`POST /director-brief/run-now`; read the five sections and their citations in the tab; render and
read both tabs; install the app locally.

## Read bound

About 220 KB:
- `docs/codex/CODEX-BD-DIRECTOR-9B-…`, `9C-…`, `9D-…` briefs and responses
- `Kor.Operations.Mcp/`: `Alerts/AlertJob.cs`, `Brief/CooBriefGenerator.cs`, `Brief/BriefPrompts.cs`, `Brief/BriefTypes.cs`, `Brief/CooBriefRepository.cs`, `Sql/CreateCooBriefTable.sql`, `Options/McpOptions.cs`, `Program.cs` lines 100–135 and 215–335, `Kor.Operations.Mcp.csproj`, `Ai/AskService.cs` lines 1–120 only (the request/response types)
- `Kor.Operations.Mcp.Tests/Kor.Operations.Mcp.Tests.csproj` and `Tools/QueryKorDataToolGateTests.cs`
- `docs/runbooks/Kor.Operations.Mcp.deploy.md`
- `Kor.Operations.App/Services/CooCardClient.cs`, `Options/AppOptions.cs` lines 60–90, `CompositionModules/AppModule.cs` lines 115–135, `BusinessDevelopment/Workspace/DashboardViewModel.cs` lines 220–300
- `Kor.Operations.App/BusinessDevelopment/Director/` (all), `CompositionModules/OpportunitiesModule.cs` lines 140–150 and 260–280
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdDirectorKnowledgeRegressionTests.cs`, `BdScreensRenderTests.cs` `RenderDirector`
- `Kor.Opportunities.Data/MajorProjects/IBdDashboardStore.cs`, `Data/Knowledge/IKnowledgeClaimStore.cs`, `Data/Crm/IBdDirectorStore.cs`, `SqlBdDirectorStore.cs`, `Schema/350_KnowledgeClaimsAndConcepts.sql`
- `Kor.Opportunities.Knowledge/` (`Store/IKnowledgeStore.cs`, `SqlKnowledgeStore.cs`, `Search/KnowledgeSearch.cs`, `Claims/ClaimFile.cs`, `ClaimValidation.cs`, csproj), `tools/BdKnowledgeIngest/Program.cs`, `Kor.Opportunities.Knowledge.Tests/KnowledgeIngestRegressionTests.cs` (summary and fixtures)

Do not open the Books folder or any `.claims.json`. App projects with `UseWPF` have no implicit
`System.IO`.

## Files

Creates: `351_KnowledgeClaimVectorsAndRelations.sql`; `Kor.Operations.Mcp/Sql/CreateDirectorBriefTable.sql`;
`Kor.Operations.Mcp/Director/DirectorBriefTypes.cs`, `DirectorBriefRepository.cs`, `DirectorFacts.cs`,
`DirectorBriefPrompts.cs`, `DirectorBriefGenerator.cs`; `Kor.Operations.Mcp.Tests/Director/DirectorBriefTests.cs`;
`Kor.Opportunities.Knowledge/Claims/RelationFile.cs`, `RelationValidation.cs`;
`Kor.Operations.App/Services/DirectorBriefClient.cs`;
`Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdDirectorMarketAndBriefRegressionTests.cs`;
`docs/codex/CODEX-BD-DIRECTOR-9E-THE-MARKET-AND-THE-BRIEF-RESPONSE.md`.

Edits: `Kor.Operations.Mcp/Program.cs`, `Alerts/AlertJob.cs`, `Kor.Operations.Mcp.csproj`,
`docs/runbooks/Kor.Operations.Mcp.deploy.md`; `Kor.Opportunities.Knowledge/Store/IKnowledgeStore.cs`,
`SqlKnowledgeStore.cs`, `Search/KnowledgeSearch.cs`; `tools/BdKnowledgeIngest/Program.cs`;
`Kor.Opportunities.Knowledge.Tests/KnowledgeIngestRegressionTests.cs`; `Kor.Opportunities.Data/Crm/IBdDirectorStore.cs`,
`SqlBdDirectorStore.cs`; `BdDirectorWindow.xaml` + `.xaml.cs`, `BdDirectorViewModel.cs`,
`BdDirectorPersonRow.cs`; `AppModule.cs`, `OpportunitiesModule.cs`; the three Director test
classes' fixtures and `BdScreensRenderTests.cs`.

## Response

The 9a–9d format. State plainly that nothing was built, run, applied, called or deployed, and
that neither the books, the passages nor the claims were read. `git diff --check` clean. Do not
commit.
