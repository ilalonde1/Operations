# CODEX — ADVERSARIAL AUDIT BEFORE THIS MODEL GOES TO THE ENGINEER

> **Do NOT run `dotnet build` or `dotnet test`.** Verification happens on the dev box; your runner
> hangs here for 15+ minutes and spawns orphan processes that lock build artifacts.
>
> **No destructive git operations. Do not touch the 31168 job share. Do not publish anything.**
> **Do not commit.** Report; the fixes are applied here after you land.

## What you are auditing

Everything that changed on 31 August, in one day, on a tool whose output goes to a structural
engineer who has already rejected two revisions. The suite is 771 green and green is not the
question. The question is what is WRONG that green cannot see.

Be adversarial. The most useful thing you can produce is a defect, not a compliment. The second
most useful is "this claim is not supported by what you measured".

## The changes, and the reasoning behind each

Read the commits in order; the reasoning is in the messages, not just the code.

```
01d12399  publishing moved out of PowerShell into takeoff publish; the script is deleted
17700f7e  four gate defects the port carried in
5c4518cd  MemberPlanStoreyMultisetPreserved -- the gate for the stack merge
d361f4df  the gate proven on a real model; the belief it was built to catch, falsified
13565e2c  the merge, and four checks that were counting labels instead of members
36aba8ba  slab and wall properties carry their grade
cf72a013  L1 carried the whole site, because a join cannot be undone by a cut
410d665c  the mezzanine lead, and the closure idea that was falsified
6a12fa76  a ring the draftsman closed beats a fill of the same linework
194e9d29  a join is an interruption, not an edge; a match line is cut, not held back
```

Plus migration `060_AJoinIsAnInterruptionNotAnEdge.sql`, applied to KorStandards.

## Where I most expect to be wrong

Attack these first. Each is a judgement I made from measurement, and any of them could be a
plausible story fitted to a small sample.

**1. `dxf.slab-chain-join-fraction = 0.10`.** Derived from ONE job. The closures on 31168 fall in
two groups — 1–8% real, 17–48% invented — and ten per cent sits in the gap. That gap may be an
accident of this building. 31138 is the other reference project and the suite builds it: does the
threshold refuse anything there that the drawing genuinely leaves open? Is a fraction even the
right shape of rule, or should it be bounded by the interruption width the fill already uses
(`FloodFillBridge`, 36 in)?

**2. The seam cut** (`DxfToEtabsService`, search `seamClipFor`). A plate recovered across a match
line is clipped to the side the building's own half-sheet is on, and members on the far side are
dropped. The side is chosen from the mean of that half-sheet's own linework. That is a centroid,
and centroids have been wrong in this codebase before — the file says so where an opening was
matched to a plate by its centre. **Where does this fail?** An L-shaped half? A sheet whose title
block sits on the far side of the seam? A seam that is not the building boundary?

**3. A drawn ring now beats a flood fill of the same ground** when it is ≥60% of the fill
(`StructuralPlanClassifier`, search `WHICH OF TWO READINGS SURVIVES`). Sixty per cent was chosen
from a single pair: the mezzanine's 1,903 ring against its 2,330 fill, 82%. What legitimate case
sits between 60 and 82? A fill that correctly spans two bays where the drawing closed only one
would be exactly that shape.

**4. Slab and wall property names now carry the grade** read from the reference model's materials
(`E2kGeometryComposer`, `GradeSuffix`). It parses `(\d+)\s*MPa` out of a material name. What
happens on a job whose materials are named in psi, or `35MPa` with no space, or two floor materials
of different grades? The name is silent when it finds no MPa — is silent right, or is a wrong-looking
name better than an incomplete one?

**5. Members counted, not objects** (`E2kDocument.ReadContents`). Every published count — report,
summary page, dossier claims gate — now counts assigns rather than connectivity rows. Today they
are identical, so nothing moved. Find where they are NOT identical and say what breaks.

## The one thing that is OFF, and why

`const bool MergeStacksIntoOneLabel = false` in `DxfToEtabsService`. This is the engineer's stated
blocker — *"the wall and column thing has to be fixed, otherwise I can't really use the model"* —
and it is one line from on.

Placement is proven exact: 1,769 column objects become 268 with all 29 storeys identical, confirmed
by `MemberPlanStoreyMultisetPreserved` AND independently by `docs/etabs-handoff/members_by_storey.py`,
which shares no code with it. The "adds members, LEVEL 2 columns 36→60" it was stashed for does not
reproduce and was never the merge.

What holds it off: three coverage checks report 10 columns on 31168 and 4 on 31138 as *"drawn with
arcs but built rectangular"*. I instrumented it and got this far, and no further:

- the merged and unmerged models have the SAME position, SAME storeys, SAME section for the
  complained-of column, and differ only in object count (6 objects → 1)
- yet on `LEVEL 1 MEZZ` the unmerged run sees `8 seg, curves=0` in the window and the merged run
  sees `26 seg, curves=18` — same storey, same point, same reach
- the two runs' reports place the same sheets on that storey

Something in `ModelCoverageTests.EveryGeneratedMemberHasTheSizeItWasDrawnAt` reads differently when
object count changes, and I could not find it. **If you find it, that is the most valuable thing in
this audit** — it is what stands between the engineer and the fix she asked for.

## Also worth your attention

- **`SlabEdgeClosure` may now borrow WALL linework** to finish a slab edge, not just unroled layers.
  It changed nothing measurable on 31168. Is it inert, or is it waiting to do something bad?
- **`LoopGeometry.ClipToSideOf`** is new Sutherland–Hodgman. Degenerate cases: a ring entirely on
  one side, a ring touching the line, a seam through a vertex.
- **Two fixtures were changed** (`TagGatedSlabRecoveryTests`, `PlateReadTwiceTests`) because they
  closed a rectangle with a whole side missing while their own summaries said "interrupted". Changing
  a test so a change passes is exactly how a suite stops meaning anything. **Was I right?**

## What is still open, so you do not report it as new

Slab thickness ZONES (L3 prints 60 call-outs across 11 thicknesses; one plate, one thickness is
modelled) and `LEVEL P1 MEZZ`, which is not placed because the engineer's own reference model has no
such storey. Both are recorded in PART 3 of `docs/KOR-DxfToEtabs-PLAN-AND-GAPS.md`.

## What to report

`docs/codex/CODEX-DXF-TO-ETABS-BEFORE-THIS-GOES-TO-HER-RESPONSE.md`.

Ranked by what it would cost to ship. For each: **measured** or **suspected**, the file and line, and
what you would measure next. If one of my claims above is not supported by the evidence I cite, say
that first and plainly — a wrong belief held confidently is worth more to me than a new finding.
