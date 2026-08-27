# Codex brief — the YMCA mezzanine: the engineer says three slabs, the tool models one

## The instruction

Andrea Neuviale, 26 August, unprompted, correcting a question we asked her:

> **"there are actually 3 slabs at mezzanine level for the YMCA"**

Banked in KorStandards as ruling `mezzanine-has-three-slabs`, marked NOT YET READ CORRECTLY. It is
still not read correctly two days later. She has seen the wrong answer twice.

Do not build or run anything. Read the code and reason. No file changes, no git operations.

## What ships today

`31168-FROM-DRAWINGS.e2k` — `LEVEL 1 MEZZ`: **one** plate, 1,903 sq ft.
`31168-TOWERS-FROM-DRAWINGS.e2k` — `LEVEL 1 MEZZ`: **two** plates, 2,587 sq ft (1,903 from building
C's sheet + 684 from `WEST`'s). Her three are all at the YMCA end, so we have one of them.

I checked every model generated on 27 August — 100 files. The mezzanine is 1 plate (YMCA) or 2
(site) in every single one. The only "3" was a run with `--infer-floors`, where it borrowed
`LEVEL P1`'s 76,967 sq ft parkade slab: 79,554 sq ft on a mezzanine. Not her slabs. **This has
never worked.**

## The sheet

`--Structural Plan - S2.12.1_1_LEVEL 1 MEZZ PLAN - CONCRETE OUTLINE - BLDG C.dxf`

Everything the run says about it:

```
JBP_C_SLABEDG: a floor plate of 1,903 sq ft is 12"            <- the one that works
slab edges: 13 outline(s) would not close (11788 units of edge ignored)
3 further outline(s) closed at the interruption width but carry no slab
   thickness call-out inside them, so they were NOT modelled as floors
JBP_C_SLABEDG: closed ring of 115 sq ft on its own — too small for a floor
   plate and not inside one, so it is linework rather than slab; not modelled
11 unreadable DXF entities carrying shape: 11 CIRCLE on JBP_G_NOTES
8 edge(s) were drawn more than once on the same layer family and were read once
```

**11,788 drawing units — 982 feet — of slab edge is read and discarded.** Her other two slabs are
in there. Everything else on this sheet works: 15 walls, 36 columns, the one plate priced at 12"
from a call-out printed inside it.

## What I tried, and why each was wrong

Four attempts, all reverted, all in `git stash`. Recorded so you do not repeat them.

**1. The flood fill returns only the largest enclosed region.** True, and I changed
`LargestSolidComponent` to `SolidComponents` returning all of them, keeping the largest plus any at
least 15% of it that passes the same fill-ratio test. **No effect on any storey in either model.**

**2. The interruption-width rescue is gated on a call-out inside each outline.** Also true —
`StructuralPlanClassifier` around line 489 requires a slab-thickness tag inside each rescued loop,
and this sheet prints its thickness only inside the plate that already closed. I loosened it to
"the sheet prices a slab anywhere". It fired 25 times across the site model and **changed no plate
count anywhere**, because the three outlines it was refusing are **7 sq ft, 7 sq ft, 7 sq ft** —
scraps, not her slabs. This is the most useful negative result: the rescue path never sees her
slabs at all.

**3. The flood fill is a FALLBACK, not a supplement.** Its trigger is
`if (result.Slabs.Count == 0 || chainClosedCount > 0)`. A sheet that closes one outline and abandons
thirteen is therefore never filled. I added a supplement mode — run it also where slab-edge chains
were left open, and add only regions overlapping no existing slab. **The supplement branch never
executed.**

**4. A diagnostic for why.** I added a flag for "supplement ran and recovered nothing". **It never
fired either**, which means `supplement` evaluated false. Since `slabEdgeLeftOpen` is certainly true
(the sheet reports 13 unclosed outlines on role `slab edges`, and `RoleSlab == "slab edges"`), the
remaining explanation is that **`result.Slabs.Count` is still 0 at the point the fill trigger is
evaluated** — i.e. the 1,903 plate is added to `result.Slabs` LATER than line ~902 — so the original
fallback branch runs, and `best` is then discarded by one of the enclosure branches below it, or
`TryRecover` fails outright. I could not tell which without another cycle and stopped.

## What I want

1. **Where in the pipeline are the 13 unclosed slab-edge chains on this sheet lost, exactly?**
   Trace `S2.12.1_1`'s `JBP_C_SLABEDG` linework from `Prepare` to `result.Slabs`. Name the line
   that drops them.

2. **Is `result.Slabs` populated before or after the flood-fill trigger at
   `StructuralPlanClassifier` ~line 902?** If after, that trigger has been testing a value that is
   always 0 and the fill has been running on every sheet all along — which would change what
   attempts 3 and 4 mean, and may be a live defect elsewhere.

3. **What is the correct reading of a sheet that draws several slabs at one level and closes only
   one of them?** Not a patch — the rule. It must not regress the two cases that constrain it:
   - `LEVEL P1`: one parkade slab of 75,832 sq ft. An earlier attempt at multi-region fill turned it
     into four fragments totalling 3,182.
   - `C-LEVEL 3`: a chain closes into 22,676 sq ft, the fill recovers 12,830, and **12,830 is the
     banked answer** — she confirmed it when she said "level 3 has its own slab edge, it's on the
     drawings".

4. **The invariant that would have caught this on day one**, assertable against a finished `.e2k` or
   a report: a sheet that discards 982 feet of slab edge while modelling one plate is not a sheet
   that has been read.

Rank by whether it can put wrong structure in front of a structural engineer with nothing in the
report saying so. This one is worse than that: the report *does* say it, in a line nobody read, and
the engineer told us the answer out loud two days ago.
