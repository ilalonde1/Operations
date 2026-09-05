# CODEX — Scoped views must render as AUTHORED BOX SCENES, not force-directed graphs

## The defect

`--view standards-estate` currently renders through the force-directed graph
layout (`GraphBuilder.BuildScoped` → `GraphPage`): circles at computed positions,
no readable content. That is the RIGHT drawing for the 62-project whole-app map
and the WRONG drawing for a curated 8-box scene. The estate the app is meant to
reproduce is a **hand-composed labeled-box diagram** — placed rectangles with a
bold title + detail lines, a header, and solid/dashed labelled ties. The two are
different *kinds* of drawing, so a "before/after" is meaningless and the scene is
far less legible than the diagram it replaced.

The scaffolding is correct and stays: the `ScopedView` model, `--list-views` /
`--view`, the versioned `docs/architecture/views/<name>/vNNN/` output, the
`summary.json`/`CHANGES.txt` diff, and the PowerShell-Visio gate. Only the LAYOUT
and RENDERING for scoped views change.

## The fix

Scoped views are **authored box scenes**, drawn by a new box path in
`VisioRenderer` that reproduces the house box recipe the retired
`Draw-EstateMap.ps1` used — but in the app, versioned and tested. Force-directed
stays for the derived whole-app graphs (`Relationships`, `Recipes`) only.

### 1. Give scene nodes committed geometry

Extend the `ScopedView` model with a page size and, on every node (authored AND
derived), an authored box rectangle in inches: `X, Y, W, H` (Visio origin is
bottom-left, same as the old script). Add a `Header` string on the view. Positions
are authored, NOT computed — a curated scene means the composer chose the layout.

### 2. Draw boxes, not circles

Add `ScenePage(doc, view, graph)` to `VisioRenderer` that, on a page sized to the
view:
- draws a borderless header text block (bold first line);
- for each node draws `DrawRectangle(X, Y, X+W, Y+H)` with `FillForegnd` = the
  node fill, `Rounding` 0.07 in, `LineWeight` 1.1 pt, `LinePattern` 2 when the
  node's state is `built` (else solid), text = `title\ndetail` with the title line
  bold at 8.5 pt;
- for each edge drops a connector glued `BeginX`→`from.PinX`, `EndX`→`to.PinX`,
  `EndArrow` 5, solid ink for `live` and dashed hairline for `built`, with the
  label at 7.5 pt.
This is exactly `New-Box`/`New-Tie`/the header from the retired script — port it,
do not invent a new style, and keep it inside `VisioRenderer` (the gate forbids a
`.ps1`). Reuse the automation switches and PNG-export path already in `Render`.

### 3. Route scoped views to the box path

`Render(..., onlyGraphs, fileStem)` gains an authored-scene branch: when a scene
is supplied, call `ScenePage` instead of `GraphPage`. Keep the single-page,
reuse-the-default-page fix already in place so no blank `Page-1.png` is exported.
Also drop the doubled name — the scoped PNG should be `<fileStem>.png`
(e.g. `KOR-Architecture-standards-estate.png`), not `<fileStem>-<pageName>.png`.

### 4. The standards-estate composition (authored positions, current truth)

Page 12.2 × 8.6 in. Header: "KOR STANDARD DETAILS — THE ESTATE / solid = LIVE on
the 302N pilot · dashed = deploy to the fleet, the one step left · 2026-09-02".
Boxes (x, y, w, h · fill · state · title · detail):

- 0.6, 4.10, 2.35, 1.50 · RGB(255,244,214) · live · **AUTHORING + MASTER** ·
  "AUTHORING: all details / ~1,079 views · MASTER: curated to 604 approved ·
  KOR-D identity, de-branded · conformance 8/8 GREEN"
- 0.6, 1.70, 2.35, 1.15 · RGB(222,235,247) · live · **BRIDGE (KOR-302N)** ·
  "drives Revit headless · census / renders / id writes · + Publish-to-Master derive"
- 4.05, 4.10, 2.50, 1.50 · RGB(238,238,238) · live · **KORSTANDARDS (SQL)** ·
  "identity / provenance / rulings · confidence ladder (placeable = content-verified &
  up) · 604 details + 288 parts"
- 4.05, 6.15, 2.50, 1.30 · RGB(226,240,226) · live · **OPERATIONS APP — STD DETAILS** ·
  "author → approve → PUBLISH · Publish-to-Master BUILT + audited"  (this is the
  DERIVED node: its `IncludeIds` are the four StandardDetails types already in the
  scene; keep the "N/N derived type(s)" annotation appended to the detail)
- 0.6, 6.15, 2.35, 1.15 · RGB(255,255,255) · live · **THE GATEKEEPER** ·
  "Champion: Serban (Jim, 2024) · approval = promotion"
- 7.55, 4.10, 2.20, 1.50 · RGB(252,236,219) · live · **DETAILS PALETTE** ·
  "LIVE on the 302N pilot · SQL-direct, approved-only · serves 604 placeable ·
  (was dormant, 0 placeable)"
- 10.05, 4.10, 1.85, 1.50 · RGB(238,230,246) · live · **KOR TOOLS RIBBON** ·
  "live fleet, additive law · + Quick Insert governed (SQL) · 288 parts, unit-aware"
- 10.05, 1.70, 1.85, 1.15 · RGB(244,244,244) · live · **DRAFTERS' REVIT** ·
  "copy the MASTER to start · loader auto-update · hash-verified publish"

Ties (from → to · label · state):
bridge→templates "runs the model" live · templates→korstandards-sql
"census + conformance" live · gatekeeper→operations-app "approves in the app" live ·
operations-app→korstandards-sql "approval → placeable" live · operations-app→bridge
"Publish to Master → rebuild" live · korstandards-sql→details-palette
"catalog: placeable only" live · details-palette→kor-tools "additive tab (branch)"
live · kor-tools→drafters-revit "publish.ps1 → share" live · operations-app→
drafters-revit "deploy app to fleet (last step)" **built (dashed)**.

### 5. Tests

Update `ScopedViewTests` to assert the scene shape: 8 boxes each with a non-zero
`W`/`H`, 10 ties, exactly one `built` (dashed) tie, and the derived operations-app
box still carries "4/4 derived type(s)". Keep the PowerShell-Visio gate test as is.

## Constraints

- Do NOT touch the whole-app map path, its pages, its output, or the force-directed
  layout used by `Relationships`/`Recipes`.
- Do NOT re-introduce a `.ps1`; the box recipe lives in `VisioRenderer`.
- Authored positions are committed data; nothing computes them.
- No build/test/render run in the brief; no destructive ops.
