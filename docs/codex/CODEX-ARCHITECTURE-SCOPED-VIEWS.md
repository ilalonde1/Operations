# CODEX — Architecture map: scoped (single-component) views, in the app

## Goal

Make "draw a focused map of ONE component" a first-class feature of the
`Kor.Operations.Architecture` project, rendered by the app's own
`VisioRenderer`, versioned and diffable exactly like the whole-app map — and
**retire the scratchpad PowerShell script that currently does it**.

Today `Draw-EstateMap.ps1` (a scratchpad one-off) re-implements the house Visio
recipe — palette, automation switches (`ScreenUpdating`/`EventsEnabled`/
`DeferRecalc`), glued connectors, PNG export — that already lives in
`Kor.Operations.Architecture\VisioRenderer.cs`. That is the exact
"tools/ prototype graduates and is never retired" anti-pattern this project
exists to kill. The capability belongs IN the app, and a mechanical gate must
make the regression impossible.

## What a scoped view IS

A named view that draws a subset of the estate on one page, using the SAME
renderer and layout code as the whole-app pages. Two sources of nodes, combined:

1. **Derived** — a projection over the SAME `ArchModel` the whole-app map already
   extracts: nodes selected by cluster (e.g. the Standard-Details types in
   `Kor.Operations.App`) or by an explicit include-list of project/type ids. No
   second extraction, no re-scan. The whole-app map stays 100% code-derived; a
   scoped view is a FILTER over it.

2. **Authored overlay** — the cross-boundary nodes/edges a Roslyn scan of one
   solution cannot see: other repos (KOR.Drafter bridge), the KorStandards SQL
   database, the Revit AUTHORING/MASTER templates, the human gatekeeper. These
   are committed DATA in the project (typed records, or a committed JSON the app
   reads), each element tagged `live` or `built` → solid or dashed edge. Authored,
   yes — but in the codebase, versioned and tested, NOT re-plumbed in PowerShell.

The renderer draws (derived subset ∪ authored overlay) with the existing house
recipe and glued connectors.

## Deliverables

1. **A `ScopedView` scene model** in `Kor.Operations.Architecture` — a committed,
   declarative definition: view name, title/subtitle, the derived-node selector
   (cluster name or id list), and the authored overlay (nodes: title/detail/fill/
   `live|built`; edges: from/to/label/`live|built`). Sort everything and carry NO
   timestamp in the committed model (same rule as `ArchModel`), so a redraw of an
   unchanged estate diffs clean.

2. **The `standards-estate` scene** as the first one, reproducing today's 8-node
   estate with CURRENT truth (from the retired script + this session's state):
   AUTHORING(all)+MASTER(curated 604); KorStandards SQL (604 details + 288 parts,
   confidence ladder); Operations App — Publish-to-Master BUILT + audited; the
   bridge (headless Revit + Publish-to-Master derive); Details Palette LIVE on the
   302N pilot (604 placeable, was dormant/0); KOR Tools ribbon + Quick Insert
   governed from SQL (288 parts, unit-aware); Drafters' Revit (copy the MASTER);
   the Gatekeeper (Serban/Jim 2024). Solid = live on the 302N pilot; the ONE dashed
   edge = "deploy the app to the fleet" (the honest remaining step).

3. **CLI**: `--view <name>` renders that scoped view; `--list-views` enumerates
   the committed scenes; no `--view` = the whole-app map exactly as today. A scoped
   view writes to its own numbered version folder (e.g.
   `docs/architecture/views/<name>/vNNN/`) with the same `summary.json` +
   `CHANGES.txt` comparison the whole-app map uses, so before/after falls out of a
   re-run.

4. **The regression gate** — a test in `Kor.Operations.Architecture.Tests` that
   FAILS if `Visio.Application`, the Visio ProgID, or `ConnectorToolDataObject`
   appears in ANY `*.ps1` in the repository. Visio rendering belongs to
   `VisioRenderer.cs` alone; a new "draw X" PowerShell script must break the build,
   not pass review. The test's own summary MUST state what it covers and what it
   does NOT: it catches PowerShell that opens Visio or drops connectors; it does
   NOT catch a C# re-implementation of the recipe outside `VisioRenderer`, nor a
   different COM app — name those as out of scope.

5. **Delete `Draw-EstateMap.ps1`** (its content is the spec for the
   `standards-estate` scene; once the scene renders, the script dies).

## Constraints

- REUSE `VisioRenderer.cs`, `GraphBuilder`/`ArchGraph`, and `MapVersions`. Do NOT
  re-implement the palette, the automation switches, glued connectors, or PNG
  export anywhere else.
- The whole-app map's behaviour and output paths are unchanged. A scoped view is
  additive; the freshness test and every existing page keep working.
- The authored overlay is the ONLY hand-authored data, and it is committed and
  typed. Do not hand-place derived nodes; let the layout compute positions, same
  as the whole-app graph pages.
- No network, no DB connection, no Revit — the scene is static committed data plus
  the derived model. (The estate DIAGRAM names the DB and Revit; it does not talk
  to them.)
- No build/test runs in this brief; no destructive operations beyond deleting the
  one named scratchpad script.

## Not in scope (say so, don't do it)

- No in-app sheet composer, no changes to the Standard-Details product, no bridge
  changes. This is the architecture MAP only.
- An adversarial-audit companion brief follows separately.
