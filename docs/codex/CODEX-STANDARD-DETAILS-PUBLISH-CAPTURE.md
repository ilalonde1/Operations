# CODEX — Standard Details: Publish to Master captures per-view PDFs into the store

## Goal

Fold artifact capture into **Publish to Master** so it happens on the **gatekeeper's own Revit
session**, not a standing-by machine. When the gatekeeper publishes, the published views are also
exported to vector PDFs and written into the governed store, keeping "Open PDF" static and fresh.
After this, Revit is needed **only** during a publish — never at runtime.

## Why / ground truth (do not relitigate)

- `MasterPublisher` performs "Publish to Master" through the bridge (derives/saves the master from
  the authoring template). Publishing is gatekeeper-gated already.
- The store carries the PDF: `detail.RenderedImage.Pdf` + `detail.SetRenderedPdf(@EntityKind,
  @EntityKey, @Pdf)` (promoter EXECUTE, migration 078). It UPDATEs the existing image row by
  identity (the image is captured first).
- `exportviews` (bridge v1.0.36) exports one vector PDF per view by ElementId from the active doc;
  the census `detail.DetailOccurrence` (readable via mig 074) gives the canonical `ViewElementId`
  per detail (prefer `ViewKind='DraftingView'`, then smallest id).
- The bridge location is `StorageOptions.StandardDetailsBridgeRoot` (App.config
  `StandardDetails.BridgeRoot`). For the gatekeeper it points at **their own machine** (local), so
  this whole flow runs against the Revit they already have open to publish. No code assumes 302N.

## What to build

1. **After the master is published** (master doc active on the gatekeeper's bridge), enumerate the
   **published/placeable** details and their canonical `ViewElementId`s (a reader query on
   `detail.DetailOccurrence` joined to the placeable set), then call `exportviews` in reasonable
   batches (e.g. ~125) to a bridge-reachable folder, keyed by DetailNumber.
2. For each returned PDF, read the bytes and call `detail.SetRenderedPdf("detail", detailNumber,
   bytes)` (via a new `KorStandardsPromoterRepository.SetRenderedPdfAsync`). Track captured / skipped
   (no image row) / failed and report a summary in the publish result.
3. Make it resilient: a per-view export failure skips that one and continues; the publish itself is
   not rolled back by a capture miss (capture is a post-publish enrichment, not part of the master
   derivation). Show progress ("Capturing PDFs… N of M").
4. **Do not block on images here.** Images are already captured; refreshing PNGs on publish (from
   the same views) is a separate follow-up — note it, don't build it in this brief.

## Constraints
- Additive; gatekeeper-only (runs inside the existing publish flow). Reuse `exportviews`,
  `SetRenderedPdf`, the census reader, and the bridge client. Use `StandardDetailsBridgeRoot` for the
  bridge location — never hardcode a machine.
- No schema change (078 already in). Parameterize queries. Build gate: warnings are errors; no new
  warnings. No build/test steps.

## Verification (done by the requester)
On the gatekeeper's machine with Revit open and the Master published, the publish reports "captured
N PDFs of M." Then, from any other machine with Revit closed, Open PDF opens those views instantly
from the store (static). A single bad view is skipped with the publish still succeeding.
