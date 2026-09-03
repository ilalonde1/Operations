# CODEX — Sheet composer: final adversarial audit (tough as nails)

## Stance

Assume this code is wrong until a concrete input or sequence proves it right. The
sheet composer drives headless Revit to create sheets in the **production AUTHORING
template**; the bar is "could this corrupt, strand, or silently mis-state something
in AUTHORING." Do NOT rubber-stamp. For every finding, give the exact inputs /
call sequence that triggers it, the resulting bad state, and a severity. If a
suspected issue is actually safe, say so and say why — a false alarm costs trust.

Rank findings; fix only the CONFIRMED correctness/safety ones with the smallest
change that holds, and report each (what, why, the fix). Leave style alone.

Primary target: `Kor.Operations.App/StandardDetails/SheetComposer.cs` and its
window/wiring, `KorStandardsReadRepository.LoadSheetComposerDetailsAsync`,
`StandardDetailsRepository.CreateDocumentAsync`. Context you must read:
`MasterPublisher.cs` (the discipline being mirrored), `DrafterBridgeClient.cs`,
and `KOR.Drafter/docs/PROTOCOL.md` (the verb contracts). Do NOT change the bridge
or the publish flow.

## The transactional model — attack it

1. **Save-then-govern.** `ComposeAsync` does `savedoc`, THEN
   `CreateDocumentAsync`. If the DB write throws, the catch runs `RollBackAsync`
   (delete sheet + `savedoc`). Trace every failure point:
   - governance throws after `savedoc` → rollback deletes a *saved* sheet, then
     saves again. If that second `savedoc` fails, AUTHORING on disk keeps the sheet
     with NO governance record — an orphan. Is that reachable? How is it surfaced?
   - `RollBackAsync`'s own `savedoc` persists whatever is in memory. If the maintainer
     had OTHER unsaved edits in AUTHORING when compose ran, does rollback silently
     save them? Does compose assume it owns the document?
2. **Partial placement.** `placeview` #k fails mid-loop. Confirm rollback removes
   the sheet AND every viewport, and leaves the source detail VIEWS intact (verified
   once live for the happy path — prove the failure path). What if `newsheet`
   succeeded but returned no `id` (older bridge)? `sheetId` is null → the sheet is
   NOT rolled back. Reachable? PROTOCOL says newsheet echoes `id` — confirm, and
   decide whether a missing id should abort before any placement.
3. **Rollback delete cascade.** Deleting the sheet returned `deletedTotal:23` live
   (sheet + title block + viewport + guide grid). Confirm none of those 23 can be a
   shared/source element (e.g., the title block TYPE, or a detail view). Prove the
   source view is never in the delete set.

## Identity & occupancy — attack it

4. **The View-Prefix occupancy** (just reworked): occupancy now reads each placed
   view's `View Prefix` over `getparams` and keys by `KOR-D`. Probe:
   - a detail with MULTIPLE views sharing one `KOR-D`, one placed and one free —
     the whole detail reads occupied; is refusing it correct, or should the free
     view be placeable? State the intended rule.
   - `LoadSheetComposerDetailsAsync` uses `MIN(ViewName)` as the canonical view. If
     that name is NOT the view actually carrying the `KOR-D` (variant mismatch),
     `placeview` targets the wrong or a non-existent view. Can catalog `ViewName`
     and the Revit view name diverge? What happens then?
   - a placed view whose `View Prefix` is empty/non-detail (general notes) — confirm
     it is excluded from the KOR-D map but still caught by the view-name fallback.
5. **TOCTOU.** Occupancy is read, then the sheet is built. Another actor (or a
   second composer) places one of the chosen views between. Confirm `placeview`'s
   one-sheet refusal + rollback is the real backstop, and that the window can't
   double-place. Is the single-Revit/single-bridge serialization actually guaranteed?

## Coordinates, scale, inputs — attack it

6. **Off-sheet placement.** PROTOCOL: `placeview` with off-sheet coords throws a bare
   `NullReferenceException`. Does the composer bound the UI grid coordinates to the
   title block, or does it rely on catch→rollback? A user dragging a detail past the
   sheet edge should get a clean refusal, not a failed compose. Check `AssertPlacedCenter`'s
   1 mm tolerance for false failures when Revit snaps a viewport centre.
7. **Scale permanence.** On success, `setscale` leaves the canonical detail view at
   the new scale (only restored on rollback). Composing a sheet thus mutates the
   standard detail's scale globally. Intended? Can it silently change a detail shown
   on ANOTHER sheet? If unintended, this is a finding.
8. **Inputs.** Sheet number/name/`like` and the JSON that carries them: any injection
   or malformed-value path through `DrafterBridgeClient` serialization? Duplicate
   sheet numbers, empty `like`, a `like` sheet that doesn't exist, a Unicode/very-long
   name — each should fail cleanly, not half-build.

## Failure surfacing — attack it

9. Bridge/Revit down, AUTHORING not the active doc, write-guard refusal (a
   central-bound doc): confirm each throws a clear, actionable message and leaves no
   partial state. Confirm the window re-enables and reports which detail failed.

## Secondary: architecture scoped views (already hand-verified — a lighter pass)

10. `PowerShellVisioGateTests`: can the gate be trivially dodged — a `.ps1` that
    builds the ProgID by concatenation (`"Visio.App"+"lication"`) or via
    `New-Object -ComObject`? State what the gate cannot catch (it already declares
    this) and whether the stated coverage matches the check.
11. Confirm the whole-app map path is byte-for-byte unchanged by the scoped-view
    work (the `onlyGraphs`/`scene` branches must not alter the no-arg render).

## Constraints

- No build/test run in this brief; no bridge or publish-flow changes; fixes are the
  smallest that hold, each reported. Report findings ranked with proof even where
  you do not fix them.
