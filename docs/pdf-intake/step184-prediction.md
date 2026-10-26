# Step 184 — a floor the set frames in wood carries no concrete plate

## Written 2026-10-26, after measuring and looking, before the code replay.

**Knob:** `KOR_STEP184_OFF=1` takes every framing-over sheet's floor as before. Rule row: `dxf.wood-framed-floor-carries-no-concrete`
(migration 110, KOR.Drafter, applied by operations-ee 2026-10-26).

**Target set:** 120 of 279 corpus sets (run 45) carry a FRAMING OVER plan sheet, 722 sheets; 20 of the 48 with the engineer's
model; 21 of the 57 sets of the corpus gate (144 sheets). Those are CANDIDATES: the rule drops a sheet's floor only where its
building draws that storey as the framing over the level below. 99 of the 120 have no engineer's model, mostly small wood
houses ("MAIN FLOOR SHOWING UPPER FLOOR FRAMING OVER"); their only judges are the ledger's counts and the render.

## The rule

A framing-over sheet ("LEVEL N SHOWING LEVEL N+1 FRAMING OVER") whose own storey its building draws as the framing over the
level below is the plan of a wood floor. Its walls and columns stand; its floor plates and openings are not written. The
lowest framing-over sheet's own storey is framed by nothing (the podium top) and keeps its plate. Buildings by title tag.

## Measured before the code (scratchpad framingover.py over the 21fb2b10 gate's built sheets)

- 19 of the 21 engineers' models stop at the concrete (top storey L1 to L3); 31101 and 31143 name their wood storeys and
  plate none.
- On 135 framing-over sheets given one storey, against each job's own engineer's model (names normalised): 102 dropped
  where she plates nothing; 26 kept where she plates; 4 dropped where another plan sheet gives her plate; 2 LOST, 31162 L2
  (its concrete plan S2.06 reads 0 slabs, a fault of its own) and 31066 building 2 L2; 1 over-read kept, 31082 L5
  ("SHOWING LEVEL FRAMING OVER" names no storey).
- LOOKED at 31162 S2.11: a wood plan of stud party walls. Its "plate", 7,068 sq ft at L3 to L5, is the legend box along the
  sheet's foot, taken because the legend's sample wall stands in it (class 4b, separate: 20 of 56 gate sets have a storey
  whose plate misses 80%+ of its members).

## Replayed (2026-10-26) — the prediction was wrong twice, and the rule changed

Built on and off (`KOR_STEP184_OFF`) over the 24 gate sets that are framing-over sets or banked, against one off arm,
storey by storey against each engineer's model (scratchpad arms.py), members by `tools/CorpusGateBisect/own_vs_extended.py`.

1. **Every framed floor (as first written):** 90 storeys moved; 5 LOST an L2 she plates (31064, 31066, 31082, 31162, 31185 -
   3,976 to 25,300 sq ft). "LEVEL 1 SHOWING LEVEL 2 FRAMING OVER" frames the CONCRETE podium as often as a wood floor; the
   pre-code scoring above had read only titles saying FRAMING OVER and only each sheet's own storey. Three separators were
   measured and do not separate the 5 from the right drops: stud walls on the framing sheet, the plate's thickness, its level.
2. **Narrowed to the second link of a chain** (a floor is wood only where the sheet framing it is itself framed): 69 storeys
   moved, 69 removed where she plates nothing, 0 lost. The 21 reverted are exactly the chains' bottom links: the 5 and 16
   LEFT AS READ (31003 L3, 31032 L3, 31048 L4, 31066 L3, 31076 L2, 31082 L5, 31085 L4, 31093 L3, 31101 L2, 31108 L3,
   31117 L3, 31129 L2, 31143 L2, 31155 L2, 31158 L2, 31171 L3) - the first wood floor above a podium, no separator known.
   BUT members moved on 13 of 21 sets: own (drawn) members on 0 storeys; stacks extended from the storey above 137 lost,
   171 gained - E2kDocument's carry-down reads plates to decide what hangs.
3. **A wood floor holds a member up:** 173 lost, 1 gained - 31082's roof wall KW228 carried through L5 onto the wood L4.
4. **...but is not a landing (final):** plates 69 of 69 right, 0 lost; own members changed on 0 storeys of 24 sets;
   extended 173 lost (every one an invention on a fake plate), 0 gained. The six banked sets and Halifax byte-identical.
   LOOKED at 31117 off and on: the plates removed were the legend box and corridor fills; L2 down untouched.

## Predicted on the replay (written before it)

- Halifax: 0 of its 24 sheets carry a framing-over clause, so raw e2k byte-identical.
- The six banked sets: none is a wood-frame set, so expected identical; any change there is a fault of this step.
- The 21: plates leave the framed storeys above the podium. The gate sees almost none of it (it judges shared storeys
  only, item 5), except 30986-02, 31006 and 31032, whose framed storeys she does plate from a concrete plan sheet.
