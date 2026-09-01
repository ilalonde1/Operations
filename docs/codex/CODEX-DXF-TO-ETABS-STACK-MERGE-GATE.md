# CODEX — THE GATE THAT MUST EXIST BEFORE THE STACK MERGE

> **Do NOT run `dotnet build` or `dotnet test`.** Verification happens on the dev box; your runner
> hangs here for 15+ minutes and spawns orphan processes that lock build artifacts.
>
> **No destructive git operations. Do not apply, pop or drop any stash.** **Do not touch the 31168
> job share. Do not publish anything.**

## Write the gate. Do NOT write the merge.

This is the whole task. The merge already exists as a stashed attempt and it is wrong; it will be
finished in the next step, by hand, against this gate. If you write the merge as well, the gate has
nothing independent to catch and the exercise is pointless.

**Deliverable: a check that proves a rename-only transform renamed only.**

## Why it exists before the code it checks

The engineer's complaint, 31 August, having run the model: *"when a column is running through
several floors, we want it to have the same label full height"* — with a photograph of one column
reading **C360, C359, C363** up the building. Three names for one column.

Her own 31138 model is the specification, measured not inferred:

```
87 column objects · EVERY ONE has LINE span 1 · 57 assigned to 5.1 storeys each (C100/C102/C103 → 19)
247 area objects  · 101 carry >1 assign, avg 9.4 storeys
```

**One object, span 1, an assign per storey.** Ours writes a fresh object per storey — each storey is
read from its own sheet — so 31168 building C stands at **713 column objects on 219 plan points**,
678 of them in stacks, every member of every stack differently named.

The merge is therefore a **renaming**: 713 objects become ~219, and *the building does not change*.
Every column that stood somewhere still stands there, on the same storeys.

The stashed attempt broke exactly that. It is recorded as:

> *"stack merge: right idea (her 31138 convention) but adds members - LEVEL 2 columns 36->60"*

A merge cannot add a member. Something else moved, and nothing in the build said so — it was found
by counting one storey by hand. **That is the failure this gate has to make impossible to miss.**

Read the attempt without applying it: `git stash show -p 'stash@{1}'`. Do not fix it.

## What the gate must assert

**The multiset of `(kind, plan position, storey)` is identical before and after.**

Multiset, not set — multiplicity carries the meaning. Two column objects at one plan point on one
storey is a real condition (`DropMembersDuplicatedOnOneFloor` exists for it). If there were two
before, there must be two after. One means the merge ate a member; three means it invented one.

Three properties, and each has a reason:

1. **Object identity is NOT part of the key.** Names are the thing being changed. A gate keyed on
   names asserts the merge did nothing.
2. **Section is NOT part of the key.** A column stepping 24x24 → 18x18 partway up must stay ONE
   object with one label and two sections — the section rides on the assign, which is what her model
   does. A key including section forbids the merge its main purpose. You may carry section into the
   failure MESSAGE; it must not decide equality.
3. **Object COUNT is expected to fall.** 713 → ~219 is success. Only the assignment multiset is
   invariant.

## The seams — both halves already exist, do not write new parsing

Checked before writing this so you do not have to hunt:

| what | where |
|---|---|
| object → the joints it stands on, in plan | `E2kDocument.PlanPointsOfObjects()` — `Kor.Operations.EngineeringTools.Core/Dxf/E2kDocument.cs:834` |
| object → every storey it is assigned to | `E2kDocument.StoreysByObject()` — same file, `:1078` |
| object → kind (`Kind` field) | `E2kModelContents.Objects`, `E2kObjectContents` at `:20` |

The gate is a join of the first two. **`PlanPointsOfObjects` already resolves joints to coordinates
and its docstring states the governing fact: the e2k keeps joints in plan only, so an object's plan
points ARE its whole position and the assign supplies the storey.** Do not re-parse the file with
regex — that is how the report and the file came to disagree on 31 August and it took four rounds to
clean up.

Prior art for the identity itself, which you should read rather than reinvent:
`docs/etabs-handoff/overlap.py` already computes "two columns sharing one storey and position", and
its output is what proved the engineer's "overlap" was span and not duplicate assigns.

## The two decisions I want you to make explicitly, and defend

**1. Position tolerance.** Coincident joints are NORMAL in this format, and two joints a hair apart
may be one member or two. Too coarse and distinct members collapse into one bucket, so a real loss
cancels against a real gain and the gate passes on a broken merge. Too fine and floating-point noise
across a rewrite splits one member into two and the gate cries wolf on a correct one.

State the rounding you chose, in inches, and say what evidence supports it. If the honest answer is
that the transform must not move coordinates at all — so exact equality is the right test and any
movement is itself a defect — say that instead; it may well be the better answer.

**2. Wall identity.** A wall panel's plan footprint is a segment, and a rewrite may emit its two
endpoints in either order. `(A,B)` and `(B,A)` are one wall. Normalise, and say how.

## Shape and wiring

Yours to choose, but it must satisfy all three:

- **Usable around an in-memory transform.** The merge runs mid-pipeline on a live `E2kDocument`
  (`DxfToEtabsService.cs`, just after `RenameStoreysInAssigns`). Snapshot before, compare after.
- **Usable on two finished files**, so a shipped model can be checked against its predecessor.
- **A runtime guard, not only a test.** A rename-only transform that is not rename-only must stop
  the build, not produce a note somebody reads later. `ShippedModelInvariants` is the wrong home —
  that reads ONE finished file, and this is differential. A new type is right.

Name it `MemberPlanStoreyMultisetPreserved` or say why something else is better.

## The failure message is most of the value

When it fires, the person reading it must know **which storey and which kind** without opening
anything. `C LEVEL 2: columns 36 → 60` is the message that would have ended the last attempt in one
run instead of five. Report per-storey counts by kind for every storey that moved, plus a few
example positions that gained or lost. Do not print all 713.

## Tests

The gate is the thing being trusted here, so it needs its own proof, and **a check that has never
been seen to fail is not a check** — that phrase is in the client dossier and it applies to you:

- a rename-only change **passes** (this is the one that matters — build a small document, rename
  objects, keep every assign, assert silence)
- an added assign **fails**, and the message names the storey
- a dropped assign **fails**
- a member moved to a different plan position **fails**
- a legitimate section change up a stack **passes**
- object count falling 3 → 1 with the same assignments **passes**

Hand-built `E2kDocument.Parse(...)` fixtures, in the style of
`PublishDiscoveryTests.ModelContentsIncludesHeadersAndOpeningsWithoutCallerCountingText`. No share
access, no real job files, and nothing that needs the rules DB.

## What to report

`docs/codex/CODEX-DXF-TO-ETABS-STACK-MERGE-GATE-RESPONSE.md`: the shape you chose, your answers to
the two explicit decisions above with the reasoning, and anything you found in the stashed attempt
that suggests **why** it added members — as an observation for whoever finishes it, NOT as a fix.

If you conclude the multiset is the wrong invariant, or that it is insufficient on its own, say so
first and plainly. That is worth more than a gate I asked for and did not need.

Then apply the gate. Ping when applied.
