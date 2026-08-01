# Structural Quantity Takeoff — Consolidation & Limit-Closing Build Plan

**Status:** Proposed (pre-build). Drafted by Claude for Ian Lalonde, 2026-06-26.
**Supersedes the UX split in:** `Kor.Operations.QuantityTakeoff.plan.md` (Rev 2).
**Baseline (measured 2026-06-26):** `EngineeringTools.Core` builds 0 errors; `Core.Tests` 31/31 pass.
This is the zero-regression floor — every phase must hold it.

---

## 1. What we're building (plain language)

Collapse the two existing Engineering-Tools windows (Concrete Quantity Delta + Rebar Takeoff
& Change) into **one** tool — **Structural Quantity Takeoff** — and close every known limit so it
is client-grade and Rory-proof.

- **One issue in** → an absolute per-floor takeoff: concrete + rebar + formwork (the "Lindley"
  deliverable; rebar = concrete volume × standard density per element/slab-type).
- **Two issues in** → comparison: exact reinforcing change-list + **visual on-drawing markup**
  (added green / removed red) + per-floor/element delta.
- **Metric *and* imperial** throughout (BC = `15M@200`/kg/m³; US = `#5 @ 12"`/lb/cuyd).
- **No silent failure:** non-text PDFs get an OCR fallback, flagged "verify"; extents are
  explicit inputs, never invented; rebar tonnage is labelled estimate, calibratable.

The four things Ian heard as "four tools" are **one tool**: concrete + rebar are *sections of one
report*, the visual markup is *a view*, the delta is *a mode*.

## 2. The zero-regression contract (structural, not hope)

- **All new logic is additive** — new files in `EngineeringTools.Core` / `Core.Tests`. Existing
  Core types are extended **only** by new sibling types and **appended optional params/overloads**
  — never by changing an existing default, signature order, or regex. Specifically: `RebarDensityTable`
  (`TakeoffModels.cs`) and `RebarWeightEstimator.DefaultDensities` stay **byte-identical** (their
  values are asserted by 6+ of the 31 tests); the element×slab-type table is a **new** type.
- **`Core` stays `net8.0`.** WinRT APIs (`Windows.Media.Ocr`, `Windows.Data.Pdf` render) live in the
  **App** layer (`net8.0-windows10.0.19041.0`, where `PdfToSafeWindow` already uses them). Retargeting
  Core would break all 31 `Core.Tests` (net8.0 can't reference net8.0-windows). Only the PdfPig
  overlay may live in Core (Core already references PdfPig).
- **PdfToSafe is untouched** (read-only reuse of its render pattern only).
- **The only edits to existing *App* files happen LAST** (Phase 6): the Engineering-Tools
  card/handlers and `AppModule` DI. Until then nothing a user touches today changes.
- **Gate after every phase:** `Core` builds; **`Core.Tests` ≥ 31 pass** (never fewer); new tests
  green; `git status` shows no out-of-scope file touched. Commit the green phase before the next.
- Build only — never publish; revert any MSBuild `<PublishUrl>`/`<ApplicationVersion>` bumps.

## 3. Reuse map (verified present in the tree)

| Need | Reuse | Status |
|---|---|---|
| Concrete volume per floor/element | `TakeoffCsvImporter` | built |
| Rebar = volume × density | `RebarWeightEstimator` | built — **validated to Jim's Lindley at 0.000%** |
| Issue-to-issue concrete delta | `TakeoffDiffService` | built |
| Reinforcing call-out extraction (metric) | `RebarCalloutExtractor` | built |
| Field-grid pricing (metric) | `RebarGridPricer` | built |
| Workbook output | `RebarChangeReportGenerator`, `TakeoffReportGenerator`, ClosedXML 0.105 | built |
| Visual markup (page copy + draw boxes) | PdfPig 0.1.9 `PdfDocumentBuilder.AddPage(doc,n)` + `DrawRectangle`/`AddText` | **proven standalone 2026-06-26** |
| PDF page render | `Windows.Data.Pdf` (`PdfDocument.RenderToStreamAsync`) | used in `PdfToSafeWindow` |
| OCR fallback | `Windows.Media.Ocr` (built-in, no new package) | to wire |
| DI / window pattern | `EngineeringToolsWindow`, `AppModule` | built |

## 4. Phases (each independently buildable + testable; risky UI is last)

### Phase 0 — Protect & baseline
- Commit the currently-staged engineering-tools code (already staged) so nothing is lost.
- Record baseline: Core 0 errors, Core.Tests 31/31. (Done 2026-06-26.)

### Phase 1 — Unit-aware Core engine (pure logic; NEW files only, no existing-file edits)
- New `UnitSystem { Metric, Imperial }` + a `TakeoffUnits` converter with **both directions
  named explicitly** and round-trip-tested:
  - mass: kg→lb ×2.204623, lb→kg ×0.453592
  - volume: m³→yd³ ×1.307951, yd³→m³ ×0.764555
  - area: m²→ft² ×10.763910, ft²→m² ×0.092903
  - density: kg/m³→lb/yd³ **×1.685553**, lb/yd³→kg/m³ **×0.593276**  *(was stated backwards;
    fixed — 0.593276 is the lb/yd³→kg/m³ direction)*
- New `StructuralDensityTable` keyed by **(element, variant)** (variant = slab-type or mat
  thickness: Roof, Residential Flat, Parking, Podium, Ground, Mat-Fdn-132″…; `null`→element
  default). Seeded with KOR standard ratios from the Lindley set; carries its own `UnitSystem`.
  **`RebarDensityTable` / `RebarWeightEstimator.DefaultDensities` are NOT touched.**
- New `StructuralTakeoffService` — single-issue absolute takeoff (concrete + rebar + formwork
  per floor) = volume × `StructuralDensityTable.For(element, variant)`, computed in the result's
  `UnitSystem`. Any new optional param on an existing method is **appended** (positional callers
  in `RebarChangeWindow`/`TakeoffCli` must not shift).
- **Golden tests:** reproduce the Lindley imperial totals (columns 1,975,750 / shear 3,862,750 /
  other 606,129 / mat-fdn exacts) to ±0.1%; a metric case; conversion round-trips (catch direction).
- Gate: Core builds, Core.Tests ≥ 31 + new green.

### Phase 2 — Absolute-takeoff report (additive)
- New report generator: per-floor table (area, concrete by element, total, reinforcing total +
  by element + lb/sqft-or-kg/m² intensity, formwork) + basis sheet + optional fabricator-
  reconciliation column. Metric/imperial driven by `UnitSystem`.
- Smoke tests: produces a valid non-empty xlsx; totals tie to the service.

### Phase 3 — Imperial call-out + grid parsing (additive; metric path byte-identical)
- Imperial matching lives behind `UnitSystem.Imperial` with its **own separate** regex
  (`#\d{1,2}\s*@\s*\d{1,2}(?:"|″)?`), its **own inch bounds** (≈3–48″, NOT the 75–750 mm filter),
  and its **own** `#`-bar mass table. The existing `CalloutRe` and the 75–750 constants in
  `RebarCalloutExtractor.cs` / `RebarGridPricer.cs` are **not edited** — imperial is a parallel
  path selected by unit, never run on a metric set (a `#5` + nearby `@12` must never fabricate a
  metric call-out). Smart-quote ″ (U+2033) handled.
- Tests: imperial extraction on a synthetic Lindley-style page; **every existing metric test stays
  green** (proves no regression to Rory's 31065 path).

### Phase 4 — Visual overlay in Core (the markup, in-app-ready)
- New `RebarOverlayGenerator` — port the proven standalone: position-aware call-out boxes
  (word `BoundingBox`), `AddPage` copy, IFT(red removed)/IFC(green added) pairs, cover/legend.
- Tests: synthetic before/after → annotated PDF with expected page count + non-zero size.

### Phase 5 — OCR fallback (additive, flagged) — **in the App layer, not Core**
- `Windows.Media.Ocr` is WinRT → lives in `Kor.Operations.App` (net8.0-windows10.0.19041.0),
  alongside the existing `Windows.Data.Pdf` usage in `PdfToSafeWindow`. Core stays net8.0.
- Detect text-layer-less pages (≈0 words from PdfPig); App OCRs them and feeds recovered text
  into the Core extractor; surface a per-sheet `LowConfidence/verify` flag. Never silent-fail.
- Tests: App-layer test for flag-on-image-only-page; clean page unaffected; Core extractor
  unit-tested on the recovered-text string (no WinRT in Core tests).

### Phase 6 — Consolidated UI (only existing-file edits; verified by RUNNING the app)
- New `StructuralQuantityTakeoffWindow`: project + 1-or-2 issues; concrete schedule + drawing
  PDFs; mode single/compare; outputs takeoff + change-list + overlay + delta. Reuses the two
  current windows' logic.
- Edits (additive): `EngineeringToolsWindow` (one card replacing two), `AppModule` DI.
- **Verify by launching the app, UIA-driving the window, and screenshotting** (per the standing
  rule that WPF click/blank-list bugs pass build+review but fail live). Old two windows retired
  only after the consolidated one is verified.
- Implement `IAiContextProvider` on the new window.

### Phase 7 — Adversarial review + remediation
- Full adversarial deep-review of the whole feature (sub-agent / Codex); remediate to green:
  Core + App build, Core.Tests ≥ 31 + new green, app launches, no out-of-scope edits, no version
  bumps. Then declare done.

## 5. Honest residual limits (by design, not defects)
- A typical detail's physical extent is a drawing-reading judgement → explicit, flagged input;
  never invented.
- Rebar tonnage before the fabricator's bar schedule is an estimate → labelled + calibratable.
These are inherent to the discipline (Jim's own sheet carries the same caveats); the tool never
states them as exact and never fails silently.

## 6. Inputs needed (non-blocking; verify, don't assume)
- KOR firm-standard density table — confirm the Lindley ratios are current firm standard.
- Default unit per region (BC metric / US imperial) — confirm vs. assume both, metric default.
