# CODEX — Standard Details: a visual sheet composer (drag details on a to-scale sheet)

## Goal

Let the user **compose a sheet visually** — drag detail thumbnails around a to-scale picture of the
sheet and *see* the layout — instead of typing X/Y millimetres into a grid. The practice lead was
explicit: "you're going to have to be able to see them to make it useful." The composer already
plots real Revit sheets; this brief replaces the blind text-coordinate entry with a visual canvas
that writes the very same coordinates.

## Why / ground truth (do not relitigate)

- The Revit pipeline is done and must not change: `SheetComposer.ComposeAsync(request, …)` builds
  the sheet (`newsheet` / `placeview` at `x_mm`/`y_mm` / `savedoc`) and asserts each view landed
  within 1 mm of the requested `Xmm`/`Ymm`. Keep that contract.
- The data model is `SheetComposerPlacement(DetailNumber, CanonicalViewName, Xmm, Ymm, Scale)` and
  `SheetComposerRequest(SheetNumber, SheetName, LikeSheet, Placements)`. The canvas is just a
  **visual editor for `Xmm`/`Ymm`/`Scale`** — same numbers, better input.
- Today `SheetComposerWindow` holds `_placements` in a `PlacementsGrid` with `XmmText`/`YmmText`
  string fields; `Add_Click` drops a detail at a naive tiled default (`100 + (i%3)*150`,
  `100 + (i/3)*95`); `Save_Click` → `ComposeAsync` → Open PDF.
- Real per-detail art exists in `detail.RenderedImage` (`LoadRenderedImageAsync`, keyed by
  DetailNumber) — use it for the thumbnails so the user sees the actual drawing, not a box.
- `OpenSheetPdfAsync` already opens the composed sheet in Bluebeam. Reuse it after Save.

## What to build

### 1. A to-scale sheet canvas
- A WPF `Canvas` representing the target plottable sheet, drawn to scale (aspect-correct) with the
  sheet border and the title-block region indicated. Get the sheet's mm extents from the `LikeSheet`
  (via the bridge if it can report sheet size; otherwise a configured KOR standard sheet size) and
  map **sheet-mm ↔ canvas-px** with one consistent transform used everywhere.

### 2. Placed details as draggable, scalable items
- Render each placement as an item on the canvas positioned at its (`Xmm`,`Ymm`), sized from the
  detail's rendered-image aspect ratio × `Scale`, showing the **actual `RenderedImage`** (fallback
  to a labeled box if no art), with the detail number.
- **Drag** to move → update `Xmm`/`Ymm` live from the transform (keep the item within the sheet
  bounds). A **resize/scale** handle updates `Scale`. Optional light **snap-to-grid**. The
  `PlacementsGrid` may remain as a synced read-out, but the canvas is the primary editor.

### 3. Add / remove from the palette
- The existing available-details list adds to the canvas (button and/or drag-onto-canvas); a new
  placement lands at the first free spot (avoid overlapping existing items) and is then draggable.
  Remove deletes from the canvas.

### 4. Save unchanged
- `Save_Click` builds the same `SheetComposerRequest` from the canvas positions and calls the
  existing `ComposeAsync` (which still verifies ≤1 mm), then `OpenSheetPdfAsync`. No change to the
  Revit/bridge side.

### Goals (do the first two; the rest are stretch, structure for them)
- Auto-arrange / auto-size (fit N details tidily; big for few, small for many).
- Flow to a second sheet when the first is full (multi-sheet compose) — leave a clean seam for it
  even if only one sheet ships now.

## Constraints
- Additive to `SheetComposerWindow`. Do NOT change `ComposeAsync`, the placement/request records,
  the bridge verbs, or the 1 mm verification. The canvas only produces `Xmm`/`Ymm`/`Scale`.
- Reuse `LoadRenderedImageAsync`, `OpenSheetPdfAsync`, the placement model, and existing styles/DI.
- One mm↔px transform, used for both drawing and reading back positions, so what the user sees is
  what Revit plots.
- Keep it responsive with a realistic count of placements (virtualize/relate to the fast-lists work
  if needed). Build gate: warnings are errors; no new warnings. No build/test steps.

## Verification (done by the requester, not Codex)
Open the composer, drag 2-3 details onto the sheet, confirm the thumbnails show real art and move
smoothly, positions stay in bounds, and the read-out mm match the drag. Save → the composed Revit
sheet places each view where it sat on the canvas (within the existing 1 mm check) and opens in
Bluebeam. Adding/removing and scaling behave; a bridge-down case shows a clean message.
