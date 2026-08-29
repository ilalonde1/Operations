# Codex 6 of N — when the title block is silent, say what the sheet does state

## Goal

A sheet can state its scale under the viewport instead of in the title block. `SheetScaleReader`
correctly refuses to read captions. So PdfToSafe should **find the caption, say where it is, and use
it — visibly and overridably** — instead of silently falling back to a typed default.

## The case

`~/Desktop/OAP-parcel11-arch-markup.pdf`. Measured word positions:

```
SCALE   at fx 0.92, fy 0.15   the title-block field — with NO value beside it
SCALE:  at fx 0.10, fy 0.05   the real note, under the viewport, bottom LEFT
1/8"    at fx 0.11, fy 0.05
```

The title-block SCALE field is empty. The sheet states `1/8" = 1'-0"` — 1:96 — once, unambiguously,
as a viewport caption. `SheetScaleReader.FromPage` returns null, **and it is right to**: it cannot
tell a sheet scale from a stair detail's `SCALE: 1:20`, and that refusal is the reason its answer can
be trusted anywhere else.

Today PdfToSafe therefore loads that sheet at a defaulted 1:100 and flags it. The flag is honest and
the number is 4% wrong.

## What to build

**A second method on `SheetScaleReader`. Do not change `FromPage`.**

Something of the shape `ScaleNotesAnywhere(PageContent page)`, returning every parseable scale note
on the sheet with **where it sits** — the fractional x and y are what let a caller say "under the
viewport, bottom-left" rather than "somewhere".

Reuse `FromPage`'s existing rules exactly, minus the title-block region filter:

- a candidate is text following a `SCALE` label on the same baseline, within reach
- it must start with a digit — `AS NOTED` is not a scale
- it must convert through `PlanGeometry.MetresPerPixel(candidate, 96)`
- notes that agree within 1% are the same scale, not two

Then in PdfToSafe's load:

1. Title block first, always. If `FromPage` gives a note, use it and say so. **It must keep winning**
   where it has an answer.
2. If not, and the sheet states **exactly one** distinct scale anywhere: use it, and say what and
   where — *"The title block states no scale. The sheet says 1/8" = 1'-0" (1:96) under the viewport
   at the bottom left — using that. Change the scale box if it is wrong."*
3. If the sheet states **several** that disagree: do not guess. Fall back as today, and name them —
   *"The sheet states more than one scale (1:50, 1:20); using entered 1:100 — set it if that is
   wrong."*

This is an offer, not a rule. Nothing is stored, nothing is banked, and the engineer overrides it by
typing in the box he already has.

## Why this is allowed to touch Core, when prompt 4 was not

I refused this in prompt 4 because `SheetScaleReader` has the slab takeoff downstream and its
documented fallback is grid-spacing calibration, which might beat a caption. **Ian overruled that:**
*"the change is ok to make even when considering the takeoff tool downstream - because that tool was
never put through its rigors anyhow. It's not finished."* I was protecting a baseline that has never
been proven.

**But the safe shape still applies:** add a method, do not alter `FromPage`. The takeoff continues to
behave exactly as it does today and can adopt the new method later, deliberately, when someone is in
a position to check what it does to its numbers. That is not timidity — it is that a change to the
takeoff should be verified against the takeoff, and this prompt cannot do that.

## Constraints

- `SheetScaleReader.FromPage` must be **unchanged**. Add beside it.
- Do not touch `PlanGeometry`, `VectorPageReader`, `SlabTakeoffEngine` or any takeoff logic.
- Do not build, do not run tests, do not publish. I verify.
- No destructive operations: no deletions, no git commands, no schema changes.
- If a note is found but does not convert, drop it silently — an unparseable string is not a scale.

## What I will check

- Parcel 11 loads at **96**, with a message naming the caption and where it is
- `31202-01` still reads **96 from its title block** — the title-block path still wins
- a sheet stating several scales falls back and names them rather than picking one
- `Core` suite 678, App suite 439, unchanged — `FromPage` untouched means the takeoff is untouched
- `TheScaleIsPrintedOnTheSheetMeasurement` still shows 1:96 reproducing the architect's printed suite
  areas within 0.6%
- **`OmarsDxfMeasurement` can delete `const int scale = 96` and read the scale instead.** That
  constant is the last band-aid from the night this started; removing it is how I know this worked.
