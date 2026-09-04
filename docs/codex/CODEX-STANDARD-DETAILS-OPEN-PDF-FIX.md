# CODEX — Standard Details: fix "Open PDF" for catalog items (Sheets/Details tabs)

## The bug

Clicking **Open PDF** on a Sheets- or Details-tab item fails with:

> Drafter active document is KOR-Standards-Master-R25; expected AUTHORING
> 'KOR-Standards-Authoring-R25' before export sheet PDF.

The catalog Open PDF reuses `SheetComposer.OpenSheetPdfAsync`, which was written for the composer:
it (a) calls `AssertAuthoringActiveAsync` — requiring the AUTHORING doc — and (b) uses the
`exportsheets` verb, which plots a composed **ViewSheet** by number. But catalog items are
**published drafting views / legends** (a general-notes view is not a ViewSheet), and the reviewer
has the **Master** open (correct for browsing published content). So it asserts the wrong document,
and `exportsheets` couldn't find the item as a sheet even if it got past the assertion.

## The fix

The catalog Open PDF must export the **selected view itself** — by ElementId, via the **`exportviews`**
bridge verb — from whatever document is active (Master is fine). No AUTHORING requirement.
`exportviews` is the same vector path that captured all 605 detail images and it resolves these
views from `KOR-Standards-Master-R25` (verified: the full export ran with the Master active).

Leave the **composer's** post-compose Open PDF as-is — after composing, the sheet really is a
ViewSheet in the AUTHORING doc, so `OpenSheetPdfAsync` (authoring + `exportsheets`) is correct
there. Only the **catalog** (Sheets/Details tab) Open PDF changes.

## Ground truth

- `exportviews` (bridge v1.0.36, deployed) takes `{ folder, colors, views:[{id,key}] }`, exports one
  vector PDF per view by ElementId, returns per-view `{ pdf, exists }`. Works on the active doc.
- The view ElementId per detail is in `detail.DetailOccurrence` (`ViewElementId`, `DocumentName`),
  which `standards_reader` can SELECT since migration 074. The canonical occurrence = prefer
  `ViewKind='DraftingView'`, then smallest `ViewElementId` (same rule the art export used).
- `OpenSheetPdfAsync` (SheetComposer.cs ~207) already has the reusable tail: export to
  `{BridgeRoot}\exports\...`, copy to `%TEMP%\KOR-StandardDetails`, `Process.Start(..., UseShellExecute=true)`.
- `KorStandardsReadRepository` is the reader; add the ElementId lookup there.

## What to build

1. **Reader lookup** — add `KorStandardsReadRepository.GetCanonicalViewElementIdAsync(detailNumber)`
   returning the canonical `ViewElementId` (long) for that detail:
   `SELECT TOP 1 o.ViewElementId FROM detail.DetailOccurrence o JOIN detail.Detail d ON d.Id=o.DetailId
    WHERE d.DetailNumber=@dn ORDER BY CASE WHEN o.ViewKind='DraftingView' THEN 0 ELSE 1 END,
    o.ViewElementId;` (parameterized). Return null if none.

2. **New export path** — `OpenDetailPdfAsync(detailNumber, timeout)` (in `SheetComposer` or a small
   service the window already has):
   - Resolve the ElementId via the reader lookup; if none, show a clean "no drawing view recorded
     for this item" message.
   - **Do NOT assert the authoring doc.** Require only that the bridge has *a* document open (reuse
     the existing "is a doc active?" check, or just let the bridge error surface cleanly).
   - Call `exportviews` with `views:[{ id: elementId, key: detailNumber }]`, `colors:"color"`,
     `folder = {BridgeRoot}\exports\standard-details-views`.
   - Take the returned per-view `pdf`, copy to `%TEMP%\KOR-StandardDetails\<detailNumber>-<ts>.pdf`,
     open with the default handler (`UseShellExecute=true`). Reuse the existing temp-copy/cleanup/open
     tail from `OpenSheetPdfAsync`.

3. **Wire the catalog Open PDF** (the button on the Sheets tab, and the same action on Details) to
   `OpenDetailPdfAsync(selectedDetailNumber, …)`. Remove the call to `OpenSheetPdfAsync` from the
   catalog path. The composer's own Open PDF keeps calling `OpenSheetPdfAsync`.

4. Failure handling: bridge/Revit down, no doc open, `exists=false`, or unreadable PDF → the existing
   clean message-box pattern (and make the dialog wide/tall enough to show the full message — the
   current one is clipped).

## Constraints
- No schema change, no new migration. Reuse `exportviews`, the census (via reader), and the
  temp/open tail. Do not touch the composer's compose/authoring flow.
- Parameterize the lookup. Build gate: warnings are errors; no new warnings. No build/test steps.

## Verification (done by the requester, not Codex)
With the Master open, select a Sheets-tab item (e.g. a General Notes view) → Open PDF opens a vector
PDF of that view in Bluebeam, no authoring error. Same works on a Details-tab item. Composing a new
sheet still opens its PDF as before. A bridge-down case shows a full, readable message.
