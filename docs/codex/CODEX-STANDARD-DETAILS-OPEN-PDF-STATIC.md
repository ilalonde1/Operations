# CODEX — Standard Details: Open PDF serves the static stored PDF (no live Revit)

## Goal

Make **Open PDF** instant and **Revit-free** for everyone by serving a pre-captured vector PDF from
the governed store, instead of re-plotting through Revit on every click. Live export stays only as a
fallback for the rare case a PDF hasn't been captured yet (and only when a bridge/Revit is actually
available — i.e. the gatekeeper). This is what takes the runtime off any standing-by Revit machine.

## Why / ground truth (do not relitigate)

- The store now carries a per-view PDF: `detail.RenderedImage.Pdf` (nullable varbinary) + proc
  `detail.SetRenderedPdf` (migration 078). `standards_reader` already has SELECT on the table.
- Images are read today via `KorStandardsReadRepository.LoadRenderedImageAsync(kind, key)`
  (`SELECT TOP 1 Png …`). Add the exact sibling for the PDF.
- `SheetComposer.OpenDetailPdfAsync(detailNumber, reader, timeout)` currently: look up the canonical
  ViewElementId → call `exportviews` (live Revit) → copy to `%TEMP%` → open with the default handler.
  It already has the temp-copy/open tail; keep that.

## What to build

1. **Reader accessor** — `KorStandardsReadRepository.LoadRenderedPdfAsync(string entityKind, string
   entityKey)` returning `byte[]?`: `SELECT TOP 1 Pdf FROM detail.RenderedImage WHERE EntityKind=@kind
   AND EntityKey=@key AND Pdf IS NOT NULL;` (parameterized; return null if none). Mirror
   `LoadRenderedImageAsync`, including its swallow-if-column/table-missing behavior.

2. **Static-first in `OpenDetailPdfAsync`**:
   - First call `LoadRenderedPdfAsync("detail", detailNumber)`. If bytes come back, write them to
     `%TEMP%\KOR-StandardDetails\<detailNumber>-<ts>.pdf` and open with the default handler
     (`UseShellExecute=true`) — **no bridge, no Revit**. Done. (This is the normal path.)
   - Only if there is **no** stored PDF, fall back to the current live `exportviews` path (unchanged),
     which needs a bridge/Revit. If no bridge/Revit is available either, show the existing clean
     message ("no PDF captured for this item yet — publish to capture it").
   - Keep the existing "Generating…" busy state; for the static path it'll simply be very brief.

## Constraints
- Additive. Do not change the migration, `exportviews`, `SetRenderedPdf`, or the composer's own
  post-compose Open PDF.
- Parameterize the query; reuse the temp-copy/open tail and the message pattern. Build gate:
  warnings are errors; no new warnings. No build/test steps.

## Verification (done by the requester)
With PDFs captured (the one-time seed or a publish), Open PDF on a detail/sheet opens **instantly**
from the store with Revit closed (kill the bridge and it still works). For an item whose PDF isn't
captured, it falls back to live export if the bridge is up, or shows a clean "not captured yet"
message if not.
