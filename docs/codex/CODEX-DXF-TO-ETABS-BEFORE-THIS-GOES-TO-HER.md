# CODEX — ADVERSARIAL AUDIT BEFORE THIS MODEL GOES TO THE ENGINEER

> **Do NOT run `dotnet build` or `dotnet test`.** Verification happens on the dev box; your runner
> hangs here for 15+ minutes and spawns orphan processes that lock build artifacts.
>
> **No destructive git operations. Do not touch the 31168 job share. Do not publish anything.**
> **Do not commit.** Report; the fixes are applied here after you land.

## What you are auditing

Two days of change to a tool whose output goes to a structural engineer who has already rejected
two revisions. **Nothing has been published.** This audit is the gate before it is.

The suite is 771 green and green is not the question. The question is what is wrong that green
cannot see. Be adversarial: a defect is worth more than a compliment, and "that claim is not
supported by what you measured" is worth more than either.

## The change, in order

```
01d12399  publishing moved out of PowerShell into takeoff publish; the script deleted
17700f7e  four gate defects the port carried in
5c4518cd  MemberPlanStoreyMultisetPreserved — the gate for the stack merge
d361f4df  the gate proven on a real model; the belief it was built to catch, falsified
13565e2c  the merge, and four checks that were counting labels instead of members
36aba8ba  slab and wall properties carry their grade
cf72a013  L1 carried the whole site, because a join cannot be undone by a cut
410d665c  the mezzanine lead, and the closure idea that was falsified
6a12fa76  a ring the draftsman closed beats a fill of the same linework
194e9d29  a join is an interruption, not an edge; a match line is cut, not held back
1abebad2  THE STACK MERGE IS ON. It was never geometry — it was the sheet report
ab9b39e4  the publish gate counted labels too
81ff38c6  one storey per member, after the cut — the other half of her overlap
27a34216  she answered the thickness question; I asked it twice anyway
```

Plus migration `060_AJoinIsAnInterruptionNotAnEdge.sql`, **applied to KorStandards on KOR-APP01**.

## The through-line, and where it is most likely still wrong

One fault repeated in seven places: **object-level attribution breaks the moment a member carries
one label across several storeys.** The merge made that real, and each place had to change from
counting labels to counting members:

1. `E2kModelContents.ReadContents` — the report, summary and dossier counts
2. `ShippedModelInvariants` — the publish gate (it refused a correct model: *"report says Columns is
   744; the .e2k beside it contains 238"*)
3. `ModelCoverageTests` — sections per member
4. `EngineerModelBenchmarkTests` — sections per member, on BOTH models
5. `ModelPlausibilityTests` — heights per assign, not per label
6. `SheetsAfterCut` — sheet storeys from PLACEMENT, not from objects
7. `LiveProjectBaselineTests` — structure counts

**Find the eighth.** Anything that groups by object name, or assumes one object is one member on one
storey, is suspect. That is the single most valuable thing you can do here.

## The judgements most likely to be a story fitted to a small sample

**1. `dxf.slab-chain-join-fraction = 0.10`.** Derived from ONE job. Closures on 31168 fall in two
groups — 1–8% real, 17–48% invented — and ten per cent sits in the gap. That gap may be an accident
of this building. Does it refuse anything on 31138 that the drawing genuinely leaves open? Should it
instead be bounded by the interruption width the fill already uses (`FloodFillBridge`, 36 in)?

**2. The span reset** (`E2kDocument.SpanEveryGeneratedMemberOneStorey`, called only when
`TowerOnly` is set). Every generated column and wall is forced to span 1 after the per-building cut,
on the reasoning that nothing is left to step over. **Is that true for every cut?** `--tower C`
leaves 21 storeys, not 13 — tower levels C does not reach are still in the list. If two of C's own
storeys are not adjacent in the surviving list, span 1 puts a column short of its floor, and
`NoColumnIsShorterThanAPerson` may not see it because the storeys are a full height apart.

**3. The seam cut** (`DxfToEtabsService`, search `seamClipFor`). A plate recovered across a match
line is clipped to the side the building's own half-sheet is on, and members on the far side are
dropped. The side comes from the MEAN of that half-sheet's linework — a centroid, and this codebase
has been bitten by centroids before. An L-shaped half? A title block across the seam? A seam that is
not the building boundary?

**4. A drawn ring beats a fill of the same ground** at ≥60% (`StructuralPlanClassifier`, search
`WHICH OF TWO READINGS SURVIVES`). Sixty per cent came from a single pair: 1,903 against 2,330, 82%.
What legitimate case sits between 60 and 82? A fill correctly spanning two bays where the drawing
closed only one is exactly that shape.

**5. `GradeSuffix`** (`E2kGeometryComposer`) parses `(\d+)\s*MPa` from the reference model's material
name. Materials in psi, `35MPa` with no space, two floor materials of different grades?

## Two ratchets were RESET, not tightened

`MembersAreTheSizeTheEngineerMadeThem` went from 133/139 columns and 60/66 walls to **201/214 and
112/122**, because both models now offer every section a member carries rather than the first. The
denominators moved with the numerators. **Check that the reset is honest** — a ratchet reset to make
a change pass is how a suite stops meaning anything, and I reset two.

Three fixtures were also changed (`TagGatedSlabRecoveryTests`, `PlateReadTwiceTests`) because they
closed a rectangle with a whole side missing — 27%, the invented band — while their own summaries
said "interrupted". **Was I right?**

## Also worth your attention

- `LoopGeometry.ClipToSideOf` — new Sutherland–Hodgman. A ring entirely on one side, a ring touching
  the line, a seam through a vertex.
- `SlabEdgeClosure` may now borrow WALL linework to finish a slab edge. It changed nothing
  measurable on 31168. Is it inert, or waiting to do something bad?
- `MemberPlanStoreyMultisetPreserved` guards the merge and could not see the span change or the
  sheet-report corruption. **What else can it not see that it should?**

## What is NOT open, so you do not report it as new

Slab thickness ZONES. The engineer deferred it herself, in the recording now at
`docs/etabs-handoff/transcripts/andrea-2026-08-31-recording.txt`: *"I think it's OK for us to model
it for now"* and *"if I can just get the outer edge, that'd be good for now"*. It was twice reported
as a question she owed an answer to. It is not.

## What to report

`docs/codex/CODEX-DXF-TO-ETABS-BEFORE-THIS-GOES-TO-HER-RESPONSE.md`.

Ranked by what it would cost to ship. For each: **measured** or **suspected**, file and line, and what
you would measure next. If a claim above is not supported by the evidence I cite for it, say that
first and plainly.
