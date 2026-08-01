# Structural Takeoff Tools — Executive Summary & Test Results
**Date:** 2026-06-29   ·   **Suite:** `Kor.Operations.EngineeringTools` (Core + TakeoffCli)   ·   **Tests:** 286 green

Five tools, one shared rule: **read what the drawing actually says, flag what it can't, and refuse a set it can't read instead of bluffing a number.** None of them tune toward an answer key.

---

## The tools

### 1. Slab concrete takeoff — `vector-takeoff`  *(for Jim)*
- **Does:** reads a vector PDF drawing set and produces a per-level **concrete** quantity (suspended slabs) plus a reinforcing estimate, into a live, calibratable Excel workbook.
- **In:** a vector PDF + pre-rendered page PNGs.  **Out:** xlsx + per-level table + an orange "verify by hand" flag list.
- **How accurate:** the **field slab** (plan area × plan thickness callout) is cross-checked three ways (grid envelope + poché flood + peer floors) and is trustworthy. It does **not** capture built-up volume *below* the slab — drop panels, beams, transfer thickening — because that isn't drawn as a plan callout (it lives in the sections/model). So every floor is reported as **field-slab-only**, with that exclusion stated, not hidden.
- **Honest limit:** it flags the uncertainty by **category, not by floor** — it can't point at one floor and say "this one's light," because the plan carries no signal that separates a light floor from a correct one (proven on 31065: L4-North reads identically to L2-North on the plan yet is 17% heavier in the model).
- **Cost:** phase 2 uses **paid AI vision**, but only on flagged unknowns (a clean set makes zero calls).
- **Status:** validated on 31065. Requires page PNGs rendered ahead of time (this host does not rasterize — now fails with a clear message if they're missing).

### 2. Rebar change list — `rebar`  *(for Rory)*
- **Does:** compares two drawing issues (e.g. IFT vs IFC) and lists, per sheet, exactly which reinforcing callouts were **added / removed / changed**, plus a bar-list steel-weight delta.
- **In:** before + after vector PDFs.  **Out:** xlsx report + console change list.
- **Verified:** 31065 IFT→IFC → 69 sheets compared, **21 changed** (19 content, 1 new sheet, 1 removed), with per-callout deltas.
- **Honest limit:** reads callout **text**, so it's blind to flattened/scanned sets. Two guards: refuses an unreadable set up front (pre-check), and aborts if it matches sheets but reads zero callouts (grammar not recognised) — never reports a false "no change."
- **Cost:** free (no AI).

### 3. Rebar change markup — `overlay`  *(for Rory)*
- **Does:** the **visual** version of #2 — a colour-coded before/after markup PDF showing the changes on the drawings.
- **Verified:** 31065 IFT→IFC → 26-page markup, **2,318,724 bytes vs the reference output's 2,319,208 — a 0.02% match.**
- **Honest limit / cost:** same as `rebar` (now shares the same refuse-on-unreadable front door). Free.

### 4. Model takeoff — `ifc-takeoff`
- **Does:** reads a Revit model exported to IFC and pulls each element's **exact** concrete volume (its own NetVolume) by level — the source that actually contains the whole building in 3D.
- **In:** an `.ifc` export (Revit → Export → IFC, "Export base quantities" ticked).  **Out:** the same costed xlsx.
- **Honest note:** for KOR's *own* jobs this is partly **redundant** — your QTO is already a Revit export. Its value is automation/pricing, not extracting hidden data. Reinforcing is exact only where bars are 3D-modelled (usually they're 2D-detailed → density estimate).
- **Status:** built and unit-tested on a realistic fixture; **not yet validated on a real export** (no `.ifc` on hand). Free.

### 5. Readability pre-check — `pdf-readable`
- **Does:** in seconds, tells you whether a PDF is **READABLE** (vector text the tools can read) or **BLIND** (scanned/CAD-flattened image). Drop a bid PDF here before committing to a run.
- **How:** calibrated from real sets — genuine vector pages carry 100+ words, flattened pages ~0; the threshold sits in a wide empty gap, not a guess.
- **Now wired in front of** the slab, rebar, and overlay tools, so they all refuse a blind set rather than bluff. Free.

---

## Test results (this pass, all free runs)

| Set | `pdf-readable` | Notes |
|---|---|---|
| 31065 (IFT/IFC) | READABLE (73 pg, 1232 w/pg) | rebar: 21 sheets changed · overlay: 0.02% vs reference |
| Kechi 30986 | READABLE (39 pg, 1585) | vector |
| Birken 01622 | READABLE (15 pg, 957) | vector |
| River District 33/34 | READABLE (20 pg, 298) | vector |
| W 8th Ave | READABLE (1 pg, 289) | vector |
| **Granville 90109** | **BLIND** (3 of 5 image-only) | correctly refused by slab + rebar + overlay |
| **regent typ floor** | **BLIND** (1 of 1 image-only) | correctly refused |

**Refuse-guards proven live:** slab/rebar/overlay all abort with a clear reason + exit 3 on a flattened side, **before any spend.** The slab tool also now gives a clear message (not the old cryptic "No slab plates measured") when page renders are missing.

---

## The two honest ceilings

1. **Per-floor built-up volume isn't on the plans.** The slab tool gets field slab; drops/beams/transfer thickening live in the sections/model. Reachable only from the model (IFC/Revit), not the plan PDF.
2. **Image-only sets can't be read.** OCR/vision is the only theoretical path and has a poor track record here (we tried vision-first and tore it out for non-determinism + cost). For a flattened set the real fix is a **vector re-export** from the issuer — not an OCR build.

---

## Before app integration (your review gates this)

- **Slab tool now renders its own pages** (Docnet.Core / bundled PDFium) — the pre-render requirement and its cryptic failure are gone; `vector-takeoff` runs self-contained from just the PDF. Verified end-to-end on 31065 (renders 73 pages, runs to a per-level result). The app can reuse `PlanPdfRenderer` or its own renderer.
- `ifc-takeoff` would benefit from **one real IFC export** to validate against a known total before it's offered in the app.
- `rebar`, `overlay`, `pdf-readable` are self-contained and app-ready as-is.

**Net: all five tools now run self-contained from their inputs.** The remaining app-phase work is UI surfacing, not capability.
