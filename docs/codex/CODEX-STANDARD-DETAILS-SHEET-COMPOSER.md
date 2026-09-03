# CODEX — Standard Details: à la carte sheet composer, in the app

## Goal

Let the standards maintainer, inside the Operations app, **compose a new typical-
detail sheet from approved details** — pick details, arrange them on a new S-sheet,
name it, and save — without opening Revit by hand. Every drafting step is driven
through the KOR.Drafter bridge against the AUTHORING template; the new sheet is
then governed (approval / Publish-to-Master) like everything else.

The engine already does every step; what is missing is the in-app composer that
drives it and the governance around the result. Reuse the proven pieces, do not
rebuild them.

## Study first, reuse these (do not re-implement)

- `Kor.Operations.App/StandardDetails/DrafterBridgeClient.cs` — the file-drop
  bridge client (inbox `.tmp`→`.json`, poll outbox, throw on `ok:false`). All
  Revit work goes through this.
- `Kor.Operations.App/StandardDetails/KorStandardsReadRepository.cs` —
  `LoadApprovedDetailNumbersAsync` reads `vw_PaletteCatalog WHERE IsPlaceable=1`.
  The composer offers ONLY approved (placeable) details; extend this repository if
  you need each detail's `KOR-D` number, title, and current view/sheet, rather
  than adding a second SQL path.
- `MasterPublisher.cs` — copy its bridge discipline verbatim: `ping`/assert the
  active document is the AUTHORING template before any mutating verb, act, then
  verify by reading back. The file-lock rule holds (a doc open elsewhere cannot be
  saved).
- `StandardDetailsFileStore.cs` / the registers / `CreateStandardDocumentWindow` —
  where a standard document/sheet is recorded. The composed sheet becomes a record
  here, not a loose artefact.

## Bridge choreography (verbs are in KOR.Drafter/docs/PROTOCOL.md)

Against the open AUTHORING template:
1. `newsheet { number, name, like }` — create the sheet. `like` (an existing
   S-sheet) is effectively REQUIRED, it supplies the title block; make it a first-
   class input, not optional.
2. For each chosen detail, `placeview { sheet, view, x_mm, y_mm }` — viewport
   centre in sheet coordinates. `placeview` refuses a view already on a sheet
   (Revit's one-sheet rule, via `CanAddViewToSheet`) and returns the post-commit
   box centre — assert on that echo, do not assume.
3. `setscale` where a placed view's scale needs normalising for the sheet.
4. `savedoc` the AUTHORING template.

## The identity decision — make it EXPLICIT and safe

An approved detail view generally already lives on its own sheet, so it cannot be
placed on a second sheet as-is. The tempting fix — `duplicateview withdetailing`
to place a copy — creates a SECOND Revit view. If that copy carries the same
`View Prefix` (`KOR-D-#####`), it breaks every reader keyed on the object
name/prefix: the census, the coverage audit, and Publish-to-Master's own
delete-by-prefix all assume one view per `KOR-D`. This is the exact class the
stack-merge label bug was (see `CLAUDE.md` rule 11) — do not reintroduce it.

So the composer must state, in code and in its summary, how identity is handled,
and default to the safe option:
- **Default (safe):** the composer only offers details whose canonical view is not
  already committed to a sheet, and places that single canonical view — one view,
  one `KOR-D`, no duplication. Details already on a sheet are shown as such and are
  not silently duplicated.
- If duplication onto a composed sheet is wanted later, it is a SEPARATE decision
  with its own identity rule (a distinct copy `KOR-D`, or a first-class "shown-on"
  relation) — out of scope here; the composer must refuse it rather than guess.

State which detail can and cannot be placed, and why, in the UI — never place
silently.

## Transactional save

Compose is all-or-nothing. Create the sheet, place every view, verify each echoed
centre; on any failure, roll back (delete the sheet just created) and report what
failed — never leave a half-built sheet in AUTHORING. Assert the active document
throughout, exactly as `MasterPublisher` does.

## Governance

- The composed sheet is built ONLY from approved details, so Publish-to-Master
  (which deletes un-approved detail views and preserves composed sheets) carries it
  to MASTER whole, with no holes. Verify that invariant holds for a composed sheet.
- Record the new sheet through the existing store/registers so it appears in the
  app's governance, with who/when.

## UI

A composer window following the app's existing WPF + `CompositionModules` DI
patterns (see how `StandardDetailsWindow` is wired): a searchable list of approved,
placeable details on one side; a sheet layout (a simple grid of viewport positions
is enough) on the other; add / remove / position; inputs for sheet number, name,
and the `like` title-block sheet; a Save that runs the choreography above and
reports per-view results. Gate it behind the same policy that gates Publish-to-
Master (`StandardDetailsAccessPolicy`).

## Constraints

- Bridge + Revit must be up on 302N with the AUTHORING template open; degrade with
  a clear message when they are not (reuse how Publish-to-Master reports this).
- No second bridge client, no second approved-details SQL path, no new Visio/PS.
- Address sheets and views by NAME/NUMBER, never by id (bridge field finding).
- No build/test run in this brief; the only writes are the sheet the user composes
  (and its rollback on failure).

## Not in scope (say so, don't do it)

- No duplication of a detail onto a second sheet under a shared `KOR-D` (see the
  identity decision).
- No changes to the architecture map, the publish flow itself, or the bridge.
- An adversarial-audit companion brief follows separately.
