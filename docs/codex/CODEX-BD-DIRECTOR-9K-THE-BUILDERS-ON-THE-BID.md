# BD Director 9k — the builders on the bid

Bounded brief for Codex. Source edits only; nothing is built, run, applied or deployed by you.
Response goes beside this file as `CODEX-BD-DIRECTOR-9K-THE-BUILDERS-ON-THE-BID-RESPONSE.md` in the
9a–9j format.

## Why this exists

Ian's finish line includes **one submission produced end to end from the platform**. The Leduc RFP
(2026-RFP-IP-061, submitted 2026-10-15) was produced by hand: a compliance check of the draft, an org
chart, a fee-schedule reconciliation, and a brochure-style document, each done in a script or in
Word. `docs/KOR-BD-Opportunity-to-Proposal-Handoff-2026-10-15.md` §3–§5 records each action, the
checklist that held and the traps paid for. This brief puts those actions on the bid, inside the
builders KOR already has. **It builds no new builder.**

Searched before writing (CLAUDE.md rule 1). What exists:

- **The bid** — a `CrmEngagements` row with `OpportunityId` (9g), on the Director's Pending bids tab,
  with attachments as bytes in `opportunities.OpportunityFiles.Content` (9h, `IPursuitFileStore`).
- **The fee proposal builder** — `Kor.Operations.App/FeeProposal/` over `dbo.FeeProposals` in the
  App's own database; blocks in `Kor.Operations.Core/Models/Proposal/ProposalBlocks.cs`
  (`ProposalBlockType` Cover, Introduction, Company, Personnel, References, ProjectDescription,
  FeeTable, Scope, ExcludedServices, FreeText, PageBreak); rendered to Word by
  `Kor.Operations.Rendering/Proposal/FeeProposalRenderer.cs` (a `switch` on `BlockType`, ~line 257);
  launched on a pursuit and attached on render by `FeeProposalLauncher` (9h, the `ProposalRendered`
  pattern). The block library is `dbo.ProposalBlockTemplates` (`SqlProposalBlockLibraryStore`);
  staff are `dbo.ProposalStaff`.
- **The brochure builder** — `Kor.Operations.App/Brochures/` over `dbo.BrochureProposals`. 9h left it
  untouched and named the same `ProposalRendered` pattern for it.
- **The three Leduc scripts** — `tools/ProposalReview/inspect_docx.py` (paragraphs, tables, text
  boxes), `docx_meta.py` (author, last save, revision, the Word lock-file owner),
  `tools/New-ProposalOrgChart.ps1` with `tools/New-ProposalOrgChart.example.json` (draws through Visio
  COM; spec = title, subtitle, context, a leadership `chain`, `groups` of people with name, cred,
  title, role, and a continuity `note`). PowerShell and Python are prototypes; this brief ports what
  the app needs to C# (`feedback_powershell_is_prototype_only_port_to_csharp`).
- **Word and PDF in C#** — `DocumentFormat.OpenXml` is referenced by the App and Data; `PdfPig` by the
  App and Knowledge.
- **The books** — `IKnowledgeClaimStore.ListClaimsAsync(sourceId, filter, take)` and
  `ListClaimsByConceptAsync`; the Knowledge tab already reads them.

Measured on the live store 2026-10-15 (fixture facts; do not remeasure):

| Fact | Value |
|---|---|
| Live bids (`OpportunityId IS NOT NULL`, Stage 1 or 3) | 1 |
| `OpportunityFiles` rows / with bytes | 1 / 0 (9h's path has not been used yet) |
| Knowledge source "Shipley Associates Proposal Guide" | Id 10 · 1,076 live claims · 705 Rules |
| Concepts | 1,784; "Writing a proposal" (`proposals`) holds 63 claims |
| Highest migration | 357 |

## The decisions, so you build the one thing

- **No model call anywhere.** A checklist is either entered by a person or written in Claude's
  session from the RFP and imported by the CLI (CLAUDE.md rule 14). Nothing reads an RFP and decides
  what it requires.
- **A requirement quotes the RFP.** ⛔ Never turn "such as 11-point Arial" into a rule (hand-off §3,
  a reviewer caught it on 10-15). Each item carries the quoted text and where it came from.
- **One table**, for the checklist (migration 358). Org charts, schedules and brochures are files
  attached to the bid through the existing `IPursuitFileStore`; nothing else is stored anew.
- **The org chart is drawn in C#, without Visio**, to SVG and PNG. The spec keeps the example JSON's
  shape, so the PowerShell script can still produce a `.vsdx` from the same spec when someone wants
  one.
- **Shipley's rules are guidance beside a block, not blocks.** They are rules about writing, not
  proposal content; they are shown read-only next to the block being edited, with their citations.

## Pieces

### Piece 1 — migration `Kor.Opportunities.Data/Schema/358_PursuitRequirements.sql`

`opportunities.PursuitRequirement`: `Id` identity · `EngagementId` FK to `CrmEngagements(Id)` NOT
NULL · `Ordinal` INT · `Requirement` NVARCHAR(1000) NOT NULL (quoted) · `SourceRef` NVARCHAR(200)
NOT NULL (e.g. `RFP Part III 3.2` or `Generic (Leduc 2026-RFP-IP-061)`) · `CheckKind` NVARCHAR(20)
CHECK IN (`Manual`, `PhrasePresent`, `PageLimit`, `FileName`) · `CheckParameter` NVARCHAR(400) NULL ·
`Status` NVARCHAR(20) DEFAULT `Open` CHECK IN (`Open`, `Met`, `NotMet`, `NotApplicable`) ·
`StatusNote` NVARCHAR(500) NULL · `CheckedBy` / `CheckedAtUtc` NULL · `CreatedBy` / `CreatedAtUtc`.
Index on (`EngagementId`, `Ordinal`). Guarded, idempotent, no data statements. The table references
no `CanonicalOrg`, so `BdCanonicalDedup`'s foreign-key guard needs no change (unlike 348, which
blocked every org merge until 2026-10-15).

### Piece 2 — the checklist on the bid (Data, CLI, App)

- `IPursuitRequirementStore` (Data): list by engagement, add, update status (who, when, note),
  replace-all on import inside one transaction.
- CLI `BdKnowledgeIngest pursuit checklist import <engagementId> <file.json> --by <name> [--replace]`:
  a JSON array of `{ requirement, sourceRef, checkKind, checkParameter }`; every item validated
  (non-blank quoted requirement ≤ 1000, known kind, parameter present for automatic kinds) before any
  write; refuses a non-empty checklist without `--replace`.
- Pending bids row: `Checklist…` opens a panel listing the items with status, source and note;
  `Add item`, set status with a note, and `Add the generic checklist`, which inserts hand-off §4's
  items as `Manual` with `SourceRef = Generic (Leduc 2026-RFP-IP-061)` and the note "verify against
  this RFP".
- **Check the draft**: pick one of the bid's attachments. `PhrasePresent` passes when the text holds
  the parameter (ordinal, case-insensitive); `PageLimit` passes when the PDF's page count ≤ the
  parameter (PdfPig; a `.docx` cannot be paginated and reads `Render to PDF to count pages`);
  `FileName` passes when the attachment's name matches the parameter, where `{rfp}` is the bid's
  RFP number and `*` matches any text (hand-off §3.5: `{rfp} - KOR Structural - *.pdf`). An automatic
  result sets `Met`/`NotMet` with a note naming the file and what was found; `Manual` items are only
  ever set by a person.
- **Docx text** (port of `inspect_docx.py`): paragraphs, table cells and text boxes, each text box
  read once although Word stores it twice (DrawingML and VML fallback; hand-off §5). **Docx
  metadata** (port of `docx_meta.py`): author, last modified by, revision, last saved, shown on the
  attachment row in the panel.

### Piece 3 — the org chart (App, rendering)

- `OrgChartSpec` in C#, the example JSON's shape exactly; `OrgChartRenderer` draws it to SVG and PNG
  (the leadership chain stacked, groups side by side with tree lines, roles in capitals, the note
  beneath), no Visio and no COM.
- Pending bids row: `Org chart…` opens an editor pre-filled from the linked fee proposal's Personnel
  block (`LeadStaffId`, `SupportingStaffId`, `AdditionalStaffIds` → `dbo.ProposalStaff` name,
  credentials, title), with chain, groups, roles, title, subtitle and note editable; `Save JSON`
  writes the spec for the PowerShell script; `Render and attach` attaches `{rfp} - Org Chart.svg`
  and `.png` to the bid through `IPursuitFileStore`.

### Piece 4 — the RFP rate schedule block (fee proposal builder)

- `ProposalBlockType.RfpRateSchedule` with `RfpRateScheduleBlockContent`: stated burden %, overhead %,
  profit %, statutory load % (the person's own figure, used only to flag), and rows: name, position,
  scored (bool), base hourly, quoted hourly (optional).
- Computed per row (hand-off §3.3): `excl. OH = base × (1 + burden)`, `incl. OH = excl. OH × (1 +
  OH)`, `incl. OH & profit = incl. OH × (1 + profit)`; one overhead % and one profit % stated above
  the table.
- Flags: burden below the stated statutory load; a row whose quoted hourly implies an overhead that
  differs from the stated one by more than 0.5 percentage points (Leduc: stated 24%, implied 60–179%);
  a scored position with no rate.
- An editor beside the existing `RatesTableEditor`, the renderer's `switch` gains the block (the
  stated percentages, then the table, scored positions in bold), and the block library can save it as
  a template like any other block.

### Piece 5 — Shipley beside the block (fee proposal builder)

A collapsible `Guidance` panel in the builder showing up to 8 live `Rule` claims from the source whose
title is `Shipley Associates Proposal Guide` (looked up by title, never by Id), filtered by the
selected block's keywords — a fixed map in code, e.g. Cover → `cover`, Introduction → `executive
summary`, Personnel → `team|resume|staff`, References → `reference|past performance`, Scope →
`scope|approach|methodology`, FeeTable/RfpRateSchedule → `price|cost|fee`. Each row shows the
statement and `(Shipley, passage)` and opens the passage like the Knowledge tab. Read-only; no claim
is copied into a proposal.

### Piece 6 — the brochure on the bid (brochure builder)

Pending bids row: `Build brochure…` opens the brochure builder for that bid and, when its Generate
step writes the PDF, attaches it to the bid through `IPursuitFileStore` — the 9h `ProposalRendered`
pattern, as `FeeProposalLauncher` does for fee proposals. A failed attach after a successful render is
reported, never silent.

### Piece 7 — gates

**Data (`BdDirectorRegressionTests`):** (1) migration 358 is guarded, one table, its FK and index, the
four check kinds and four statuses, no data statements; (2) the checklist import validates every item
before its single transaction and refuses a non-empty checklist without replace; (3) no model call,
no API marker, no Visio or COM reference in the new App and Rendering code.

**Rendering/App (`BdDirectorBuildersRegressionTests`, new):** (4) docx text from a fixture holding a
paragraph, a table cell and one text box returns the box's text once; (5) `PhrasePresent`,
`PageLimit` (a 3-page PDF fixture against 2 and 3) and `FileName` (`{rfp} - KOR Structural - *.pdf`)
give Met/NotMet with notes, and a `.docx` under `PageLimit` stays Open with the render note; (6)
`Manual` items never change on Check the draft; (7) the generic checklist inserts hand-off §4's items
with the Generic source; (8) the org chart renders the Leduc example JSON to an SVG holding every
person's name once and both chain names, and the PNG is non-empty; (9) the rate schedule computes the
three columns for a row with base 50, burden 0.30, OH 0.24, profit 0.10 (65.00, 80.60, 88.66), flags
burden 0.05 against a statutory load of 0.12, flags a quoted rate implying 60% overhead, and renders
into the Word output with scored rows bold; (10) the guidance map returns Shipley rules for each
block type from a fixture store and never calls a write; (11) the brochure path attaches once after
generate and reports a failed attach.

**Render (`BdScreensRenderTests`):** (12) `director-checklist.png` (five items: two Met, one NotMet,
one Open manual, one Open PageLimit on a docx) and `fee-rate-schedule.png` (the block editor with
both flags); minimum total renders rises by two.

## Claude's run after apply (not yours)

Build App tests, Data tests, Rendering and CLI; run the gate classes and the full Director filter;
render and read the two new PNGs and the org chart from the Leduc example; migration 358 joins Ian's
next SSMS sitting; install the App locally; then write the next real bid's checklist in session from
its RFP and import it.

## Read bound

About 160 KB:
- `docs/KOR-BD-Opportunity-to-Proposal-Handoff-2026-10-15.md` §3–§5
- the 9g, 9h and 9j briefs and responses in `docs/codex/`
- `tools/ProposalReview/` (all four files), `tools/New-ProposalOrgChart.ps1`, `tools/New-ProposalOrgChart.example.json`
- `Kor.Operations.App/FeeProposal/` (`FeeProposalLauncher.cs`, `FeeProposalBuilderViewModel.cs`,
  `FeeProposalBuilderWindow.xaml` + `.cs`, `Editors/RatesTableEditor.xaml` + `.cs`)
- `Kor.Operations.Core/Models/Proposal/ProposalBlocks.cs`; `Kor.Operations.Rendering/Proposal/FeeProposalRenderer.cs`, `IFeeProposalRenderer.cs`
- `Kor.Operations.App/Brochures/` (`BrochureBuilderViewModel.cs`, `.Persistence.cs`, `BrochureGenerateStep.xaml.cs`)
- `Kor.Opportunities.Data/Crm/` (`IPursuitFileStore` and its SQL store), `Kor.Opportunities.Data/Knowledge/IKnowledgeClaimStore.cs`
- `Kor.Opportunities.Data/Schema/353_PursuitSubmission.sql`, `354_AttachmentsInTheDatabase.sql`
- the Director's Pending bids tab (view model, XAML, code-behind), `tools/BdKnowledgeIngest/Program.cs` (verb parsing)
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdDirectorRegressionTests.cs`,
  `BdDirectorPendingBidsRegressionTests.cs`, `BdScreensRenderTests.cs`

Do not open the live databases, the share or Deltek. App projects with `UseWPF` have no implicit
`System.IO`.

## Files

Creates: `Kor.Opportunities.Data/Schema/358_PursuitRequirements.sql`; the requirement store and its
interface; the docx reader and the org chart spec and renderer (in the project that already holds
OpenXML and rendering code); the checklist panel, org chart editor and rate schedule editor;
`BdDirectorBuildersRegressionTests.cs`; the RESPONSE.

Edits: `ProposalBlocks.cs`, `FeeProposalRenderer.cs`, the fee proposal builder view model and window,
the brochure builder's generate path, the Pending bids tab, `tools/BdKnowledgeIngest/Program.cs`,
`OpportunitiesModule.cs` for registration, `BdDirectorRegressionTests.cs`, `BdScreensRenderTests.cs`.

## Response

In the 9a–9j format: what changed per piece, the gates authored and not executed, what was not done
and why, and anything in the existing code that contradicted this brief. Do not build, test, run
tools, open a database or commit. `git diff --check` clean.
