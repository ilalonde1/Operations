# BD Director 9k — the builders on the bid

Implemented as source edits on 2026-10-15. **Nothing was built, tested, rendered,
installed, applied to a database, deployed or committed.** No application, CLI,
database tool or gate was executed. The live databases, share and Deltek were not
opened. Compilation, runtime behavior and visual quality remain unverified.
Source inspection and whitespace checks only; `git diff --check` is clean,
including separate checks of the new files.

One new table, `opportunities.PursuitRequirement`. The existing builders and
attachment store carry the other features. No new model call or Office automation
was introduced. The four pre-existing untracked artifacts were left untouched.

Prior art searched/read: the handoff's checklist and Leduc traps; 9g/9h/9j briefs
and responses; the four ProposalReview files and org-chart script/spec; existing
pursuit attachment/link stores, proposal models, persistence, block library,
editors, PDF/Word renderers, brochure generation, Knowledge claim reads, Pending
bids UI and Director regression/render harness. The existing render-event pattern
and stores are reused.

## Applied changes

- **Piece 1 — migration 358:** One guarded table with the quoted requirement,
  source, ordinal, check kind/parameter, status/note, checking attribution and
  creation attribution. Four check kinds and four statuses have CHECK constraints;
  Open and creation time have defaults. The engagement FK and separately guarded
  engagement/ordinal index are included. No data statements or CanonicalOrg FK.
- **Piece 2 — checklist:** Added the requirement contract, SQL store and shared
  import validation. The CLI accepts `pursuit checklist import` with mandatory
  attribution and optional replacement. Every item is validated before any store
  call; the SQL store validates again before connecting. Append/import use one
  transaction with a parent-row lock, recheck whether the checklist is nonempty,
  and roll back failed replacements. Pending bids opens the checklist panel with
  source, status, note, checking attribution, add/status actions and the eight
  generic Manual prompts carrying the specified source and reminder. Draft checks
  use ordinal case-insensitive phrase matching, actual PDF page counts, and an
  anchored, escaped filename pattern. DOCX page checks become Open with the render
  note. Manual items are skipped by the checker and protected by the SQL update
  predicate. Partial automatic updates are reported. DOCX extraction reads body,
  tables, nested boxes, headers/footers and notes; alternate representations select
  one branch rather than deduplicating identical text across distinct boxes.
  Attachment rows show author, last editor, revision and last saved metadata.
- **Piece 3 — org chart:** Added the exact camel-case JSON shape, personnel prefill
  from the latest linked fee proposal, editable chain/groups/people/context/roles
  and continuity note, Save JSON, and Render and attach. Staff names, credentials
  and titles come from the existing staff store; RFP roles are left for the person
  to assign. One C# layout produces SVG and PNG with stacked leadership, group
  columns, tree lines, capitalized roles and the note below. Both files attach
  through `IPursuitFileStore`; a PNG upload failure after SVG success is explicit.
- **Piece 4 — RFP rate schedule:** Appended the enum member without renumbering
  existing values; added the block payload, editor, display name and creation
  switch. The three calculations retain decimal precision until display. Flags
  cover burden below the person's statutory figure, an implied overhead difference
  greater than 0.5 percentage points, and scored positions without a rate. The
  editor accepts percentages, and updates computed rates/flags when inputs change.
  Existing proposal/template persistence carries the block. Both PDF and Word
  renderers state burden/overhead/profit above the table and bold scored rows.
- **Piece 5 — guidance:** Added a collapsible read-only panel beside the block
  editor, with a fixed keyword map covering every block type. Shipley is resolved
  by title. A parameterized `Rule:` filter on the existing claim-list method applies
  live-rule/keyword predicates before the take limit, avoiding the 1,000-row cap
  hiding later matching passages. Up to eight statements carry Shipley citations;
  selecting one reads its passage. Selection versions reject stale async results.
  No guidance write or insertion into proposal content occurs.
- **Piece 6 — brochure:** Pending bids launches the existing brochure builder with
  a visible bid attachment caption. Successful PDF generation awaits the render
  event, which streams one attachment for that generation. The launch session
  serializes attachments, checks the bid still matches, and unsubscribes on close.
  The Generate screen and completion message report attachment success or failure,
  including the generated file location when upload fails. This does not change
  the bid's stage or submitted-file selection.
- **Piece 7 — integration/gates:** Registered the requirement store and brochure
  launcher; extended the existing Director source gates and screen harness and
  added `BdDirectorBuildersRegressionTests`.

## Gates authored, not executed

| Gate source | Coverage |
|---|---|
| `BdDirectorRegressionTests` | Guarded one-table migration, FK/index/kinds/statuses and no data statements; validation ordering and single transaction with locking/rollback; invalid final import item makes no store calls; nonempty refusal and explicit replacement; SQL protection of Manual items; new-code model/automation marker checks; guidance filtering before take. |
| `BdDirectorBuildersRegressionTests` | DOCX paragraph/cell/alternate-box extraction and metadata, with a distinct identical-box negative control; three-page PDF against limits two/three; phrase and filename Met/NotMet with notes; DOCX page count stays Open; Manual items receive no automatic writes; eight generic items/source/reminder; Leduc SVG person labels, uppercase roles and PNG signature; JSON shape and personnel prefill; rate arithmetic, both percentage flags, exact tolerance boundary, missing scored rate, template roundtrip and bold Word rows; each block's guidance with a non-10 source ID, retired/non-rule negatives and no writes; brochure attachment success/failure and unsubscription. |
| `BdScreensRenderTests` | `director-checklist.png` with the five specified status/kind cases and DOCX metadata; `fee-rate-schedule.png` with both flags. Minimum screen count increases from 15 to 17. Also writes `org-chart-leduc.svg` and `.png` for visual review alongside the screen outputs. |

The tests create their DOCX/PDF fixtures locally when executed; none was generated
in this session. SQL source checks and mocked stores do not establish permissions,
rollback/concurrency behavior against SQL Server, or actual database cardinality.
The render gates do not establish visual quality without opening the resulting
images. No pre-fix failure or post-fix pass is claimed.

## Contradictions and implementation decisions

1. `FeeProposalRenderer.cs` is the QuestPDF renderer, not the Word renderer. Word
   lives in `FeeProposalDocxRenderer.cs`; both needed changes. The enum and typed
   payload dispatcher also live outside `ProposalBlocks.cs` and were updated.
2. The brochure Generate code-behind contains no generation logic. The PDF action
   is in `BrochureBuilderViewModel.Preview.cs`, where the event is now awaited.
   That partial already contains a separate AI analysis feature. Bid-launched
   windows disable that command and guard its implementation; the existing
   standalone brochure feature was not rewritten.
3. The fee-proposal link store could test membership but could not retrieve a
   linked proposal. Added a SELECT-only latest-link read using the existing
   `LinkedAtUtc` column. No new link or brochure storage was created.
4. Rendering targets plain net8.0. The shared org-chart spec and DOCX reader live
   there; the drawing implementation lives in the WPF App and uses its existing
   drawing/PNG APIs without another dependency.
5. `ListClaimsAsync` previously accepted review-state filters only. Its new
   parameterized rule/keyword filter makes the requested selection server-side;
   fetching only the first 1,000 of the supplied 1,076 claims could omit guidance.
6. Quoted hourly is interpreted as the rate including overhead **before profit**,
   stated in the editor. Implied overhead is quoted hourly divided by burdened
   base, minus one. The statutory-load figure only flags; it never alters rates.
7. A PageLimit check counts the entire PDF. The result note states that exclusions
   need manual verification; no exclusions or RFP requirements are inferred.
8. The Leduc continuity note itself repeats leadership names. The SVG gate asserts
   each person's name once among person labels, while retaining that note verbatim.
   It does not incorrectly reject those legitimate mentions in the note.
9. Legacy path-only attachments have no database bytes to inspect. The checklist
   identifies them and requests a database copy; it does not traverse their paths.
   A stored DOCX has no companion Word lock file, so metadata comes from its package.

## Handoff

Claude's affected-project Debug builds and gate runs, full Director filter,
pre-fix negative controls, image generation/visual review, local installation and
real-bid checklist import remain unperformed as the brief requires. Migration 358
is ready for Ian's next SSMS sitting. No publishing, deployment or commit occurred.
