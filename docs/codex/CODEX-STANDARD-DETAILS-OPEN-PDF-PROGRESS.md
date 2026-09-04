# CODEX — Standard Details: "Generating PDF…" progress state on Open PDF

## Goal

Open PDF takes a few seconds (Revit plots the view, the file lands, the app copies + opens it).
Today the button gives no feedback during that wait, so it feels hung. Show a clear busy state
while it works, and restore it when the PDF opens or an error is shown.

## Ground truth

- The catalog Open PDF button is `OpenSheetPdfButton` in `StandardDetailsWindow.xaml`; its Click
  handler (`OpenSheetPdf_Click` in `StandardDetailsWindow.Logic.cs`, ~line 921) awaits
  `SheetComposer.OpenDetailPdfAsync(detailNumber, reader, timeout)`.
- There is a status strip (`ActivityMessageText` / the `SetActivityMessage(...)` helper) already
  used for transient messages.

## What to build

- On click, before awaiting: disable `OpenSheetPdfButton`, change its label to **"Generating…"**
  (keep it the same size so nothing reflows), and set a status-strip message like
  **"Generating PDF…"**.
- In a `finally`, always restore the button (label back to "Open PDF", re-enabled) and clear the
  status message. On success the PDF opens as now; on failure the existing error dialog still shows.
- Guard against double-clicks while generating (the disabled button covers this; make sure re-entry
  can't fire a second export).
- Keep it lightweight — a text/label swap (and optionally a small inline spinner) is enough; no new
  windows or long-lived state.

## Constraints
- Additive, UI-only. Do not change `OpenDetailPdfAsync`, the export path, or the composer.
- Reuse the existing status-strip helper and styles. Build gate: warnings are errors; no new
  warnings. No build/test steps.

## Verification (done by the requester)
Click Open PDF: the button reads "Generating…" and is disabled, the status strip says generating,
then the PDF opens and the button returns to "Open PDF". A failure still shows the error and the
button restores. Double-clicking doesn't launch two exports.
