# CODEX — Standard Details: the `kind` axis (general-note / typical / custom)

## Goal

Add a second classification axis to details — their **kind** — orthogonal to discipline, so the
catalog can be organized the way the engineer thinks about a drawing set, not just by material.
This is the foundation the **Sheets/collections** view (a later brief) groups by.

Three kinds, from the practice lead's own words (2026-09-03 review):
- **general-note** — general notes and schedules (some embedded in the notes, some standalone).
- **typical** — vetted, recurring details that go on most jobs.
- **custom** — project-specific details (back-of-set, job-scoped); may or may not be vetted.

**Out of scope: do NOT auto-classify existing details.** Leave `Kind` unset (NULL) on seed. The
initial classification is a curated pass the requester runs (a best-effort derivation the
gatekeeper then adjusts) — a blind auto-label would bake in wrong guesses. This brief only builds
the column, the setter, the exposure, and the app controls to read/filter/set it.

## Why / ground truth (do not relitigate)

- Details live in `detail.Detail` (keyed by `DetailNumber`); the app reads them through
  `detail.vw_PaletteCatalog`, which already surfaces `DetailNumber`, `Title`, `Discipline`,
  `Confidence`, `IsPlaceable`, `VariantsDiverge`, `ViewName`, `ViewKind`, `SizeToken`.
- Discipline filtering was just made SQL-side and parameterized:
  `KorStandardsReadRepository.LoadPaletteDetailsAsync(query, discipline)` applies
  `AND (@discipline IS NULL OR Discipline = @discipline)`. **Mirror that exact pattern for kind** —
  do not reintroduce client-side filtering.
- Governance writes go through the promoter principal (e.g. `detail.PromoteComponent`,
  `detail.SetRenderedImage`, `detail.DeleteRenderedImage` all `GRANT EXECUTE … TO standards_promoter`).
  A kind setter follows the same shape.

## What to build

### 1. Schema — a migration at `KOR.Drafter\db\076_DetailKind.sql`
Match the house style of `073_`/`074_`/`075_` (header comment explaining why; idempotent; grants
inline; no data change beyond what's stated).
- `ALTER TABLE detail.Detail ADD Kind NVARCHAR(16) NULL` (guard with a column-exists check so the
  migration is re-runnable), plus a CHECK constraint allowing only
  `('general-note','typical','custom')` or NULL.
- `detail.SetDetailKind(@DetailNumber NVARCHAR(64), @Kind NVARCHAR(16))` — validates `@Kind` is one
  of the three (or NULL to clear), `UPDATE detail.Detail SET Kind=@Kind WHERE DetailNumber=@DetailNumber`,
  returns rows affected. `GRANT EXECUTE … TO standards_promoter`.
- **Expose `Kind` in `detail.vw_PaletteCatalog`**: read the view's current definition
  (`sys.sql_modules` / the migration that created it) and `CREATE OR ALTER VIEW` it with `Kind`
  added as one more selected column — **preserve every existing column, join, filter and grouping
  exactly**; this view is load-bearing. Do not change its shape otherwise.

### 2. App — read, filter, and set kind
- `KorStandardsReadRepository.LoadPaletteDetailsAsync`: select `Kind`, carry it on `PaletteDetailRow`
  and thence `DocumentRow`. Add an optional `kind` parameter applied as a parameterized
  `AND (@kind IS NULL OR Kind = @kind)` — the same idiom as `@discipline`. (Also thread it through
  `LoadSheetComposerDetailsAsync` for parity.)
- A **kind filter** in the Standard Details window, alongside the discipline chips — a compact
  control (a second small chip row, or a dropdown) with All / General notes / Typical / Custom,
  wired like `DisciplineChip_Checked` to re-run the scoped SQL load.
- A **set-kind control** in the detail pane so the gatekeeper can classify the selected detail:
  a small dropdown (General note / Typical / Custom / — unset) that persists via a new
  `KorStandardsPromoterRepository.SetDetailKindAsync(detailNumber, kind)` calling
  `detail.SetDetailKind`, then refreshes the row. This is a classification, independent of
  approve/reject — don't entangle it with `IsPlaceable`.

## Constraints
- Additive. `Kind` is nullable and optional everywhere; nothing breaks when it's unset. Do not
  auto-populate it.
- Parameterize every predicate (no SQL string interpolation), and do not reintroduce client-side
  filtering for discipline or kind.
- Preserve `vw_PaletteCatalog`'s existing contract exactly (only add the one column).
- Follow existing app conventions (the SqlClient/`AddNullableNVarChar` pattern, the promoter repo,
  the chip/pane styles) and the build gate (warnings are errors; no new warnings).
- No build or test steps in your change; leave verification to the requester.

## Verification (done by the requester, not Codex)
Run migration 076; confirm `Kind` is NULL for all details (no auto-classification) and
`vw_PaletteCatalog` returns the same columns as before plus `Kind`. In the app: set a detail's kind
and confirm it persists and the kind filter scopes the list (parameterized SQL, counts correct),
discipline+kind combine, and per-selection image + approve/reject are unaffected. (The curated seed
of the 605 details is a separate follow-up, not part of this verification.)
