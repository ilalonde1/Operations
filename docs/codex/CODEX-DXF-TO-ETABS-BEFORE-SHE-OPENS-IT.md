# CODEX — 31168: AUDIT BEFORE THE ENGINEER OPENS IT

> **Do NOT run `dotnet build` or `dotnet test`.** Verification happens on the dev box; your test
> runner hangs here for 15+ minutes and spawns orphan processes that lock build artifacts. Read,
> reason, report.
>
> **No destructive git operations** — no `clean`, no `reset --hard`, no force push. `git log`,
> `git show`, `git diff` for reading only. **Do not edit source.** The only file you write is your
> response. **Do not touch the 31168 job share.**

You are a **hostile reviewer**. Cite `file:line` or do not claim it.

---

## What this is

A structural engineer, Andrea Neuviale, is about to open these models. She has already been sent
wrong work twice this week and has said *"the wall and column thing has to be fixed, otherwise I
can't really use the model."* Today's changes are meant to fix that. **Your job is to find what will
embarrass us when she opens it**, not to review the code in general.

**You cannot read the artifacts** — the `.e2k` files live on a job share you have no route to. I
have them and I will check every finding. So every finding must be a **falsifiable prediction about
a file I am holding**, in the format at the end. A finding I cannot check, I drop.

## The change that matters most, and it is not code

**The deliverable is changing shape.** Until today she received two files:

```
31168-FROM-DRAWINGS.e2k          building C on the shared parkade, 13 storeys
31168-TOWERS-FROM-DRAWINGS.e2k   the whole site, 63 storeys, three buildings on one storey list
```

She is about to receive three:

```
31168-A-FROM-DRAWINGS.e2k   39 storeys   492 walls   787 columns   37 floors
31168-B-FROM-DRAWINGS.e2k   45 storeys   624 walls   913 columns   40 floors
31168-C-FROM-DRAWINGS.e2k   13 storeys   335 walls   713 columns   15 floors
```

**Why:** the combined site model forces three buildings onto one global storey list, so each
building's floors become slivers between the others'. Measured: **19 of its 65 storeys are under 60
inches tall** — `A-LEVEL 36` is **2.0 in**, `C-LEVEL 8` is **5.5 in**, `B-LEVEL 1` is **1.67 in** —
where the same storeys are 116 in in a per-building model. Columns on 5-inch storeys are stubs that
overlap, which is exactly what she photographed and complained about.

**The problem:** this contradicts a ruling of hers banked in KorStandards as `tower-storey-scope` —
*"let's have a full model with both towers modelled, I will separate the towers later"* (7 August).
Splitting them is what fixes her blocker and is what she is asking for now, but it overturns
something she said and we banked.

**Tell me plainly whether shipping A/B/C is right**, and what the honest way to put it to her is.
This is the single most likely thing to embarrass us, and it is a judgement, not a bug.

## THE ARCHITECTURAL QUESTION, and I want your ruling on it

Everything above is a symptom of this. **A member cannot be both broken at every floor and free of
wafers on one interleaved storey list.** Rule on which it is to be.

She ran the model and wrote, on 31 August: *"Columns have to be broken down at every floor, from
slab to slab"*, *"and there should be no overlap"*, *"same with walls"*, and *"when a column is
running through several floors, we want it to have the same label full height."*

Today a column is written as ONE line object spanning every storey it passes through
(`E2kGeometryComposer.cs:710` `StoreysSpannedBy`, reached via `FreeStoreysFor` at `:677`; the column
is emitted around `:1023`), with a single assign on its base storey. The two definitions of "one
floor" that must be reconciled are `E2kDocument.SameFloorTolerance()` at `E2kDocument.cs:886` and
`ComposeOptions.StoreysAtOneLevelGap` (12 in). The reason is in the code's own comment: a site storey list holds
a storey per tower per floor, so tower B's level-34 wall crosses A-LEVEL 34, and assigning it only
to B-LEVEL 34 builds it *"between A-LEVEL 34 and B-LEVEL 34: a two-inch wafer."*

Measured on the per-building models, WITH the split already applied:

```
31168-C   290 of 713 columns still span two storeys   (LEVEL 1 -> LEVEL 2 crosses LEVEL 1 MEZZ)
31168-A   163 of 787          31168-B   247 of 913
```

So the split does NOT fix her complaint. On LEVEL 1 today, 108 columns run *through* the mezzanine
while the mezzanine's own 64 columns stand in the same space — her "overlap", exactly.

I tried the obvious change — span 1, one assign per spanned storey, which also gives her the single
label full height. **Per-storey column counts came out identical, so nothing is lost**, and the
overlap goes. It also broke two tests on the COMBINED site model:
`ModelPlausibilityTests.NoColumnIsShorterThanAPerson` and
`PlacementModelTests.AboveWhereItsBuildingStartsAMemberNeverCrossesIntoAnother` — the wafers the
span was invented to prevent. That change is stashed, not landed.

**Rule on this:**

1. Is the per-building model the only deliverable that can satisfy her, and should the combined
   site model be retired rather than repaired? Note her banked `tower-storey-scope` ruling says the
   opposite, and `compose-the-site-once-then-cut` makes the combined composition load-bearing for
   how the per-building files are produced at all.
2. If the combined model stays, what is the rule that gives both — break at every REAL floor, span
   across slivers? Where does "sliver" come from, given `SameFloorTolerance` and
   `StoreysAtOneLevelGap = 12 in` already exist and disagree about what one floor is?
3. Does one line object with an assign per storey actually give ETABS one label at full height and
   a member per floor, or have I misread the format? If I have, say so — the whole approach rests
   on it.

Rank this above every code defect below. It decides what she receives.

## Ground truth, measured today (2026-08-31)

Rotation, L3, the staircase, the report fixes and the per-building split are all verified against
regenerated files. Full suite **745 passed, 0 failed**, including the coverage ratchets.

```
rotation      drawings turned 90° onto her grid; 19 of 19 X grid lines and 2 of 2 on Y matched
C-LEVEL 3     22,663 sq ft, the outer edge — closed through two segments on JBP_C_B_STRUCT,
              a layer a banked ruling excludes from structure
staircase     LEVEL 2 outline 114 points -> 24; 67 segments of exactly 6.0 in alternating V/H -> 0
              cause: flood fill rasterises at MinPanelOverlap/2 = 6 in, straightening ran at 3 in
per-building  storeys under 60 in: site model 19 of 65  ->  A 0 of 39, B 2 of 45, C 0 of 13
              B's two are B-LEVEL 41 (36 in, a real parapet) and B-LEVEL 1 (1.67 in, EMPTY)
P3            dropped on her instruction: "no structural slab at P3, only a slab on grade"
```

## Where to attack

**The per-building split.** `PublishPlan.ForBuildings`, `JobPublisher.Run`. Three models now come
from one composition and one storey list. What can differ between them that should not? A member
that lands in two buildings, a shared podium storey counted twice, a storey renamed in one model and
not another. The old pair had an invariant — the two files had to agree exactly on building C's own
storeys — and **three files have no such check at all.**

**`B-LEVEL 1` at 1.67 inches.** Empty today, so harmless today. Why does building B keep a storey
that building C's run merges away? The shared-base merge is `E2kDocument.cs:620` onward:
`buildingStarts` is taken with `FirstOrDefault()` over a story list held TOP-DOWN, which looks like
it yields the building's HIGHEST storey where the name and every use of it want its lowest. Confirm
or refute that, and say what else it reaches — an empty sliver today is a populated sliver on the
next job, and `sharedBase` is derived from the same number.

**`--drop-storeys` on the publish verb.** New today, built and NOT tested. It merges a
caller-supplied list into every building's plan and into the invariant check. What happens with a
name that matches nothing, a name that matches in one building only, a storey other storeys depend
on, or a storey that is some building's only floor?

**The slab-edge closure through unmodelled layers** (`SlabEdgeClosure`). It admits linework from
layers a rule says are not structure, on exact endpoint continuity. What else could it close through
— a hatch boundary, a leader, a revision cloud? It ran on five outlines today; two were 131 and 54
sq ft. Are those right?

**The staircase straightening.** Now `max(RecoveredOutlineTolerance, pixelSize * 1.5)`. On what
drawing does straightening at the cell remove something real? The 1% area guard is the only thing
behind it.

**The raster-staircase invariant.** It BLOCKS a publish when more than a quarter of a ring's edges
are sub-foot runs alternating H/V. What legitimate outline does that refuse? A small plate with real
steps, a stair landing, a shaft?

**And the thing she says is still wrong.** The mezzanine plates are built by joining a chain's two
loose ends across an 82 in gap, and the tool assembles a 2,763 sq ft chain where the raw linework
closes 1,857 — it dash-joins and bridges 6 in gaps first. She says the edge is wrong. Read that path
and tell me what it is sweeping in.

## Do not spend time on

Already found and fixed today, verified: the report describing a pre-cut model; `FloorGaps`
measuring coverage and reporting existence; a workbook question naming a storey the file lacks;
1,075 orphan joints; the note-rewriter eating storey names out of longer ones (`C-LEVEL 4` -> `C-`);
`WithinOrNear` measuring to an infinite line.

Known unfinished, no need to report: column labels are per-storey rather than one label full height;
section names are `KOR-S8` not `slab8-35MPa`; slab concrete strength is absent from all 139 DXFs;
thickness zones are not modelled; `L1 going past the basement walls` is unsolved.

## The finding format — mandatory

```
### <one-line claim>
SEVERITY  EMBARRASSING | MATERIAL | MINOR
WHERE     file:line
WHAT      what the code does, in a sentence or two
TRIGGER   the input or state that makes it happen, named concretely
CONSEQUENCE  what SHE sees when she opens it
VERIFY    a falsifiable prediction about one of 31168-A/B/C-FROM-DRAWINGS.e2k or their reports,
          settled by one grep or one script. Say what you expect the answer to BE.
```

`EMBARRASSING` = she opens it and can see it is wrong. `MATERIAL` = a real defect she may not spot.
`MINOR` = worth fixing, no consequence today.

**No suggested patches.** Where you are unsure, write `PLAUSIBLE` on the VERIFY line rather than
asserting — I check all of them and a confident wrong claim costs more than a hedged true one.

Then:

1. **Exactly one thing that must be fixed before she opens it.**
2. **Your answer on A/B/C versus the combined model**, and how to put it to her.
3. **What input would break this that nobody has tried** — name the job shape or sheet.

Write to `docs/codex/CODEX-DXF-TO-ETABS-BEFORE-SHE-OPENS-IT-RESPONSE.md`. Ping when done.
