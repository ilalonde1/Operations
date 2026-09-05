# CODEX — Standard Details: composer loads details without needing the AUTHORING model

## The bug

In the Sheet Composer, the details list is empty and Search does nothing whenever the bridge doesn't
have the AUTHORING model active (i.e. the MASTER is open, or Revit/the bridge is down — which is now
the normal state, since the runtime is Revit-free).

Cause: `SheetComposerWindow.LoadDetailsAsync` (~line 83) loads the catalogue details from the DB
(`_catalogRepository.LoadSheetComposerDetailsAsync`, line 88) and then immediately calls
`_composer.LoadOccupiedDetailsAsync` (line 89), which asserts the AUTHORING doc is active through
the bridge (`SheetComposer` ~line 62, `AssertAuthoringActiveAsync("load sheet occupancy")`). If that
assert throws, the whole method falls to its `catch` and `_details` is never populated — so nothing
lists and search returns nothing.

## The fix — decouple browsing from occupancy

Browsing/searching the approved details is a pure DB read and must ALWAYS work, with or without the
bridge. The occupancy annotation (which details are already committed to a sheet) is a nice-to-have
that needs the AUTHORING model — it must be **best-effort**, never a gate on the load.

In `LoadDetailsAsync`:
1. Load the rows from the DB and **populate `_details` first** (this is the part that must always
   succeed). Sort/filter as today.
2. Then attempt the occupancy check in its **own try/catch**: call `LoadOccupiedDetailsAsync`; on
   success, annotate the already-sheeted rows exactly as now (they stay visible but can't be added).
   On failure (no AUTHORING doc / bridge down / Revit closed), **swallow it** — leave every row
   addable and set a small status note like "Occupancy check unavailable — open the AUTHORING model
   to see which details are already on a sheet." Do NOT show a blocking error dialog for this case,
   and do NOT clear or skip the list.
3. Keep the existing summary text when occupancy succeeds.
4. **Fail fast, don't hang.** The occupancy call currently waits on a 2-minute bridge ping timeout,
   so with the bridge down the composer freezes for two minutes before showing anything. Give the
   occupancy attempt a SHORT timeout (≈5–10s) so the list appears promptly and the "occupancy
   unavailable" note shows quickly when the bridge isn't up. (The actual compose can keep its longer
   timeout — only the best-effort occupancy check needs to fail fast.)

Net: the composer lists and searches details with the bridge closed and no two-minute hang;
occupancy info appears when the AUTHORING model is active.

## Constraints
- Do not change `ComposeAsync` or the actual compose/place flow — composing genuinely writes to the
  AUTHORING model and correctly needs it. Only the LOAD is decoupled.
- The DB call (`LoadSheetComposerDetailsAsync`) already filters to placeable, non-sheet details with
  parameterized discipline/kind — leave it as is; just make sure its result populates `_details`
  before the occupancy call.
- Reuse the existing status/summary label for the "occupancy unavailable" note. Build gate: warnings
  are errors; no new warnings. No build/test steps.

## Verification (done by the requester)
Open the Sheet Composer with the MASTER open (or Revit closed): the details list populates and
Search works, with a small "occupancy unavailable" note. Then with the AUTHORING model active:
already-sheeted details show as before and can't be added. Compose still works when AUTHORING is up.
