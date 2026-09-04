# CODEX — Standard Details: fast lists (push filters to SQL + virtualize the grid)

## Goal

The catalog and composer lists must open and re-filter **in well under a second**. The engineer's
bar (from the 2026-09-03 review) is literal: "if it takes more than 5 seconds to load, I'm already
doing something else." Today the main list loads **all ~609 details** on open and on every filter,
filters by discipline **client-side in LINQ**, and binds the whole list to an un-virtualized grid.
Fix both halves: fetch only what's asked for, and render only what's visible.

## Why / ground truth (do not relitigate)

- `StandardDetailsWindow.Logic.cs::LoadDocumentsUiAsync` (details branch) calls
  `_korStandardsRepo.LoadPaletteDetailsAsync(q)` — which returns **every** detail matching the
  search — then does `details.Where(d => d.Discipline == _selectedDiscipline)` **in memory**, then
  `DocumentsGrid.ItemsSource = _documentSnapshot`. A discipline chip click re-runs the whole
  load-all-then-filter. This directly violates the repo rule "push filters to the source, not to
  LINQ."
- `KorStandardsReadRepository.LoadPaletteDetailsAsync` currently takes only the search string; its
  SQL reads `detail.vw_PaletteCatalog` with no discipline predicate.
- Images are already lazy and must stay that way: `LoadRenderedImageAsync` is a per-selection
  `SELECT TOP 1 Png …`. The list queries are metadata-only. **Do not** pull `Png`/bytes into any
  list query.
- Parts branch (`LoadQuickInsertPartsAsync`) is search-only by design (discipline chips don't apply
  to parts) — leave that filtering model as is, but it benefits from the same grid virtualization.

## What to build

### 1. Push the discipline filter into SQL
- Extend `LoadPaletteDetailsAsync` to accept an optional discipline (e.g.
  `LoadPaletteDetailsAsync(string query, string? discipline = null)`), and add a parameterized
  `AND (@discipline IS NULL OR Discipline = @discipline)` to the `vw_PaletteCatalog` query.
- Update `LoadDocumentsUiAsync` to pass `_selectedDiscipline` to the repo and **remove the
  client-side `.Where(...)`** on discipline. "All" (null/empty) still returns everything.
- Keep the search term server-side as it already is.

### 2. Virtualize the grids
- On `DocumentsGrid` (main list) and the Sheet Composer's detail list, ensure UI virtualization is
  on and not defeated: `EnableRowVirtualization="True"`, `EnableColumnVirtualization="True"`,
  `VirtualizingPanel.IsVirtualizing="True"`, `VirtualizingPanel.VirtualizationMode="Recycling"`,
  `ScrollViewer.CanContentScroll="True"`.
- Verify nothing defeats virtualization: the row/status-pill `DataTemplate` must not sit inside a
  non-virtualizing container, the grid must not be inside a height-unbounded `ScrollViewer`/
  `StackPanel`, and `ItemsSource` should stay a plain materialized list (no per-row live queries).

### 3. Don't reload-all to re-filter
- A discipline chip or Details/Parts tab switch should issue **one targeted query** (via #1), not
  reload the full set and filter in memory. If a trivial in-memory cache of the last-loaded set
  helps snappiness for repeated identical filters, that's fine, but correctness comes from the SQL
  predicate, not the cache.

### 4. Composer parity
- The Sheet Composer list (`SheetComposerWindow`) currently loads the full catalog on open; give it
  the same virtualization and the same discipline/search-scoped load so it doesn't stall.

## Constraints
- Additive/minimal; do not change what the lists *show*, only how fast they load. Keep the
  confidence-ladder status, discipline grouping, and per-selection lazy image exactly as they are.
- Parameterize the discipline predicate (no string interpolation into SQL).
- Follow existing app conventions (the repo's Dapper/SqlClient pattern, DI, the ListRow/pill styles)
  and the repo build gate (warnings are errors; no new warnings).
- No build or test steps in your change; leave verification to the requester.

## Verification (done by the requester, not Codex)
Open the window and click through All / Concrete / Steel / General / Wood Frame and Parts: each must
paint in well under a second, and switching a chip must not visibly reload the whole set. Confirm the
counts per discipline are unchanged from today, scrolling 600+ rows stays smooth, and selecting a
detail still shows its image (per-selection lazy load intact).
