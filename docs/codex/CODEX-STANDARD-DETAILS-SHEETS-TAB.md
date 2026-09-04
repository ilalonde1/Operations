# CODEX — Standard Details: a "Sheets" tab pulled from the existing details

## Goal

Add a **Sheets** tab beside Details/Parts. Sheets are NOT a separate entity or store — they are
the sheet-shaped items already in the catalog: **general notes, schedules, and multi-component
collections** (a bunch of text and/or several sub-figures), as opposed to a single focused
detail. The tab shows those, grouped into their collections, in a **bigger format** (they're
text/table-heavy and need the room). This is the practice lead's model: "aren't they just details?
… the ones with a bunch of text and multiple components are sheets."

## Why / ground truth (do not relitigate)

- Details live in `detail.Detail` / `detail.vw_PaletteCatalog`. The census
  `detail.DetailOccurrence` also carries **`ViewKind`** (DraftingView vs **Legend**) and
  **`ViewGroup`** — the drafter's own collection ("00 - General Notes & Schedules",
  "01 - Concrete Standard Details", "02 - Wood Frame Standard Details", "04 - Shear Wall Details",
  "05 - Basement Wall Details", the `000_GENERAL_NOTES_SIZE_*` groups, etc.). ViewGroup IS the
  collection axis; there is nothing to compose.
- Sheet-ness cannot be reliably auto-derived from the image: notes/schedules ARE text-heavy
  multi-component, but plain details with concrete stipple or rebar-dot fills also score
  "many components." So the sheet/detail split is a **curated flag**, seeded from the strong
  signals (Legend, notes/schedule ViewGroups) and adjusted by the gatekeeper — not a naive metric.
- The tabs are `DetailsTab`/`PartsTab` RadioButtons (GroupName "Catalog") → `CatalogTab_Checked`;
  the list load is `LoadDocumentsUiAsync`. Discipline/kind filters are SQL-side + parameterized;
  grids are virtualized. Keep all that.
- `SheetComposer.OpenSheetPdfAsync(sheetNumber, timeout)` opens a sheet's PDF in Bluebeam — reuse
  for any per-sheet PDF action.

## What to build

### 1. Schema — migration `KOR.Drafter\db\077_DetailIsSheet.sql` (house style of 073–076)
- `ALTER TABLE detail.Detail ADD IsSheet bit NOT NULL CONSTRAINT DF_Detail_IsSheet DEFAULT 0`
  (guarded/idempotent). Curated, **not** auto-populated — every row starts 0; the requester seeds
  it separately.
- `detail.SetDetailIsSheet(@DetailNumber nvarchar(64), @IsSheet bit)`, `GRANT EXECUTE … TO
  standards_promoter` (mirror `SetDetailKind`).
- Expose **`IsSheet`** and **`ViewGroup`** in `detail.vw_PaletteCatalog`: read the current view
  definition and `CREATE OR ALTER` it adding exactly those two columns (ViewGroup from the same
  canonical occurrence the view already joins) — **preserve every existing column, join, filter and
  grouping**; the view is load-bearing.

### 2. App — the Sheets tab
- Add a `SheetsTab` RadioButton (same group/style) and a mode so `LoadDocumentsUiAsync` has a Sheets
  branch. Sheets branch loads details **WHERE IsSheet = 1**, via a parameterized predicate (same
  idiom as `@discipline`/`@kind`), **grouped/sorted by `ViewGroup`** (the collection). Details tab
  loads **IsSheet = 0** (so a sheet shows in exactly one tab). Parts unchanged.
- Carry `IsSheet` and `ViewGroup` on `PaletteDetailRow`/`DocumentRow`.
- Discipline + search still apply on the Sheets tab; kind may too.

### 3. Bigger format for sheets
- When a sheet is selected (or when the Sheets tab is active), the preview must render **larger** —
  give the drawing pane materially more width/height than the compact detail preview, because
  sheets are text/table/multi-figure. Click-to-zoom stays. A per-sheet **Open PDF** (Bluebeam via
  `OpenSheetPdfAsync`) remains available.
- A gatekeeper control to toggle a row's `IsSheet` (via a new
  `KorStandardsPromoterRepository.SetDetailIsSheetAsync` → `detail.SetDetailIsSheet`), so
  misclassifications are fixed in-app — same governance pattern as the kind setter.

## Constraints
- Additive. `IsSheet` defaults 0; nothing breaks unseeded (Sheets tab is simply empty until the
  seed runs). Do NOT auto-classify in the migration or app.
- Parameterize predicates; no client-side filtering; preserve `vw_PaletteCatalog`'s contract
  (only add the two columns).
- Reuse existing plumbing (SqlClient/`AddNullableNVarChar`, promoter repo, tab/chip/pane styles,
  `OpenSheetPdfAsync`, virtualization). Don't duplicate an export path or a second data store.
- Build gate: warnings are errors; no new warnings. No build/test steps — verification is the
  requester's.

## Verification (done by the requester, not Codex)
Run 077; confirm `IsSheet` is 0 for all and `vw_PaletteCatalog` returns the same columns plus
`IsSheet`, `ViewGroup`. In the app: with the curated seed applied, the Sheets tab lists the
notes/schedules/collections grouped by ViewGroup and renders them noticeably larger; toggling a
row's IsSheet moves it between Details and Sheets; discipline/kind/search still scope; Parts and
per-selection image are unaffected. (The curated IsSheet seed of the 605 is a separate follow-up.)
