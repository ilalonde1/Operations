# CODEX — DXF→ETABS: FULL AUDIT OF THE SHIPPED SYSTEM

> **IMPORTANT: Do NOT run `dotnet build` or `dotnet test`.**
> Verification happens on the dev box on Claude's side. Your test runner consistently hangs here for
> 15+ minutes and spawns orphan dotnet processes that lock build artifacts. Read, reason, report.
>
> **Do NOT run any destructive git operation** — no `git clean`, no `reset --hard`, no force push.
> `git log`, `git show`, `git diff` for reading only. **Do not edit any source file.** The only file
> you write is your response.

You are a **hostile reviewer**. Cite `file:line` or do not claim it.

---

## The division of labour, and why this brief is shaped the way it is

You read code. **You cannot read the artifact** — the two shipped `.e2k` files live on a job share
you have no route to. I have them locally and I will check every finding you make against them.

So this is not "report what looks wrong". **Every finding must be a falsifiable prediction about a
file I am holding**, phrased so that one command settles it. A finding I cannot check is a finding
I will drop, however well argued. See *The finding format* at the end — it is mandatory, and it is
the whole point of this audit.

This division exists because reading is what made the last audit blind. Its eight findings were all
real and all fixed, and then **three more defects appeared that reading had not caught** — they only
showed up when the code was RUN on inputs it had never been given. Predict the artifact, and I will
run it.

## The system

`Kor.Operations.EngineeringTools.Core/Dxf/` — **29 files, 16,119 lines**. Tests in
`Kor.Operations.EngineeringTools.Core.Tests/` — **600 facts, 16,432 lines**.

| File | Role | Lines |
|---|---|---|
| `StructuralPlanClassifier.cs` | what a piece of linework IS — wall / column / slab / opening | largest |
| `DxfToEtabsService.cs` | orchestrator: read → classify → place → compose → **cut** → report | 2nd |
| `E2kGeometryComposer.cs` | places geometry on storeys; `RisesTo` picks the storey a member rises to | 3rd |
| `ModelQuestionnaire.cs` | the questions put to the engineer, and the workbook | 4th |
| `E2kDocument.cs` | the `.e2k` itself; `FloorOfStorey`, `FloorGaps`, the post-cut passes | 5th |
| `ShippedModelInvariants.cs` | the blocking checks a model must pass before it is copied to her folder | |
| `PlanSheetNaming.cs` | which storeys a sheet's title claims (`MatchStories`) | |
| `SheetSetGlossary.cs` | learns the drawing set's own shorthand (`WEST` = `BLDG A & B`) | |
| `LoopGeometry.cs` | `SelfIntersects`, `HasNarrowNeck` — the two guards a plate must pass | |

One composition produces two deliverables. **The site is composed once and the one-building model is
cut from it afterwards**, so the smaller is a subset of the larger *by construction*. That invariant
was broken for a week and holds now; if your reading says something breaks it, that is a finding.

## Ground truth — measured today, 2026-08-31, from the shipped files

Both published 2026-08-28 19:48/19:50. No ETABS code has changed since (`git diff e03677aa..HEAD`
over `Dxf/` is empty — the commits since are all architecture-map work).

```
31168-FROM-DRAWINGS.e2k         13 storeys   335 walls   713 columns   15 floor plates
31168-TOWERS-FROM-DRAWINGS.e2k  63 storeys  1410 walls  2365 columns   89 floor plates
```

Per-storey agreement, from `docs/etabs-handoff/members_by_storey.py` — **identical on all 8 of
building C's own storeys, proper subsets on the 4 shared**. This currently holds:

```
C-LEVEL 3..9, C-ROOF   identical      LEVEL 1 MEZZ  61/64/3 vs 61/64/4   subset (the cut)
LEVEL P1, LEVEL P2     identical      LEVEL 2       15/36/1 vs 34/60/3   subset (the cut)
```

Floor plates in the building-C file, by storey and section — note the six on the reference model's
`Rvt-Floor0` section and the three on `LEVEL 1 MEZZ`, which are the engineer's three mezzanine
slabs, closed on 28 Aug (2,754 + 2,330 + 1,095 sq ft):

```
KF1 LEVEL P2 · KF2 LEVEL P1 · KF3 LEVEL 1 · KF5,KF6,KF7 LEVEL 1 MEZZ   Rvt-Floor0
KF9 LEVEL 2 · KF83..KF90 C-LEVEL 3..9, C-ROOF                          KOR-S7/8/9/14
```

47 rules are read live from `KorStandards` (`analysis` schema on `KOR-APP01\SQLEXPRESS`). A missing
one stops a run by design; there is no fallback value.

## Two defects I have already found and verified. They are your calibration, not your task.

Both were found by comparing the **shipped report** against the **shipped file**. Both are live code
paths, not stale text. **Do not re-report them.** Report the rest of their family.

### 1. The report tells the engineer to add a slab to a storey that has three

`31168-FROM-DRAWINGS-report.txt:197` says:

> *1 storey(s) carry walls or columns and no floor plate, so they have no diaphragm: **LEVEL 1
> MEZZ**. Nothing was borrowed or invented for them; add a plate if these storeys need one.*

`LEVEL 1 MEZZ` carries `KF5`, `KF6`, `KF7`. The sentence is false, and it is false about the one
storey on this job the engineer personally corrected us on twice.

The cause is not staleness — the line is correctly recomputed **after** the cut against the finished
document (`DxfToEtabsService.cs:1731`, which deliberately filters out the composer's pre-cut
version). The cause is that `E2kDocument.FloorGaps()` (`E2kDocument.cs:908`) does not measure what
the sentence says. Its `plateless` test, `E2kDocument.cs:973`, is
`mine.Count(m => Covered(m, above)) * 2 < mine.Count` —
**fewer than half this storey's members stand under a plate**. That is coverage, not existence. A
mezzanine is a partial floor by definition: 125 members, three small slabs, so it trips every time.

**The damning part**: the *other* call site already knows. `E2kModelQuery.cs:141` names the same
return value `mostlyUncovered` and carries this comment —

> *FloorGaps measures COVERAGE, not existence. A storey here may well carry a slab object — LEVEL 1
> of the published 31168 model carries an 11,026 sq ft one — and still be reported… Saying "has no
> slab" of a storey that visibly has one in the table above is the tool contradicting itself, so it
> says what it actually measured.*

The fix was written, understood, and applied to the `/ask` path. **It was never swept to the report
path**, which is the one the engineer reads. `FloorGaps` has **zero test references** in the repo.

### 2. A number computed before the cut, printed in a report about the model after it

Both reports carry the identical sentence:

> *Slab thickness still ASSUMED: 5 of **90** floor plate(s)…
> Storeys affected: LEVEL P2, LEVEL P1, **LEVEL 1, LEVEL 1**, LEVEL 1 MEZZ.*

The building-C file has **15** plates. The site file has **89**. Neither is 90. The line is emitted
at `E2kGeometryComposer.cs:1600` during composition, and `LEVEL 1` appears twice because pre-cut it
is still `A-LEVEL 1` and `B-LEVEL 1` — the shared ground floor, named for two of the three buildings.

There **is** a guard for exactly this class: `NotesAboutStoreysThisModelHas` (defined
`DxfToEtabsService.cs:1905`, applied to flags at `:1722` and to warnings at `:1727`), added after a
workbook cited three storeys the engineer's file did not have. **It rewrites storey NAMES and never
looks at NUMBERS**, so a count walks straight past it — the
same shape as the `RuleTopic` string-match that let a settled question be asked again.

### What these two have in common — this is the thesis of the audit

**A fix landed at one call site and not the other's, and nothing measured the report against the
file.** Not one of the 600 tests asserts that a number printed in a report is true of the `.e2k`
beside it. That is the gap both of these fell through, and it is where I expect the rest to be.

## What to hunt

Go where the code takes you. These are named because they are non-obvious.

**Every other number in the report.** Take each count, area and list the report prints and ask which
model it describes — the composition, the pre-cut document, or the file as saved. Sheet tallies,
member counts, "drawn but not modelled", flag counts, the wall/column percentile lines, the layer
segment totals, the questionnaire's own numbers. Which are computed on one side of the cut and
printed on the other? The two files' reports being **byte-identical on a line** is itself the tell —
one is a 13-storey building, the other is a 63-storey site.

**Every other consumer of `FloorGaps` and of anything else with two call sites.** One knows the
semantics, one does not. Find the other pairs: a helper whose meaning is documented at one caller and
assumed at another. `git log -S` on the fixing commit is a fast route to which sweeps were partial.

**The guards that check the wrong noun.** `NotesAboutStoreysThisModelHas` checks names, not numbers.
`RuleTopic` matching checks a topic string, not the question. `member-on-another-building` is
documented as blind where buildings are not separable on plan — **which is 31168**. What else is
guarded by a check that cannot see the thing it was written for?

**Pre-cut versus post-cut, exhaustively.** `DxfToEtabsService` cuts storeys (`-DropStoreys`,
`-TopStorey`) and cuts buildings (`TowerOnly`), renames storeys, merges floors, and dedups assigns.
Enumerate every value that crosses that boundary. Which summary fields are captured before and
surfaced after? Does anything count objects the cut removed, or miss objects the merge renamed?

**The questionnaire.** `ModelQuestionnaire.cs` is the 4th largest file and its output goes to the
engineer as an `.xlsx` she answers. Both models report "Questions for you: 3". Are those three the
same three in both files, and should they be? A question about a storey a model does not have is the
defect that has already shipped once.

**`ShippedModelInvariants` — what does it NOT check?** It is the last gate before a file is copied to
her folder. It checks storey cuts, provenance, members on two storeys, openings with no floor,
openings bigger than half their floor, outlines doubling back, joints too close. **Neither of my two
findings would have been caught by it**, because it reads the file and never reads the report. What
else can reach her folder untouched by it?

**Inputs this code has never been given.** A job with one building. A model with no reference shell.
A storey list with no prefixes at all. A sheet naming a building the glossary never learned. A
foundation-only run. An empty drawing folder. `LEVEL 1 MEZZ` is the only mezzanine in the corpus —
what does a second one do? This is the section that found the real bugs on the architecture map, and
reading alone is what made that audit blind to them.

**Where a heuristic silently substitutes for a measurement.** `min-floor-coverage = 0.6`,
`flood-fill-bridge = 36 in`, `same-ground-centre-tolerance = 24 in`, the `* 2 <` in `FloorGaps`. Each
is a threshold standing where a fact was not available. Which of them can be crossed by a legitimate
building and produce a confident wrong answer with nothing in the report saying so?

## Rank by one thing

**Can it put wrong structure in front of a structural engineer, or a false statement about her model,
without anything in the report saying so?** That failure mode has now happened five times and it is
the only ranking that matters. A defect that fails loudly is worth less than a sentence that is
quietly untrue.

## The finding format — mandatory

Every finding, in this shape. A finding without a `VERIFY` line I can execute will be dropped.

```
### <one-line claim>
SEVERITY  CRITICAL | MATERIAL | MINOR
WHERE     file:line  (the code that is wrong, not the symptom's location)
WHAT      what the code does, in one or two sentences
TRIGGER   the input or state that makes it happen — concrete, named
CONSEQUENCE  what the engineer sees or receives that is wrong
VERIFY    a falsifiable prediction about one of the two shipped .e2k files or their
          reports, that I can settle with one grep, one python script from
          docs/etabs-handoff/, or one number compared against another.
          Say what you expect the answer to BE.
```

`CRITICAL` = wrong structure, silent loss, or a number a person would act on and be misled by.
`MATERIAL` = a real defect with a plausible trigger. `MINOR` = worth fixing, no consequence today.

**No suggested patches.** I want the fault characterised, not repaired — this system has cost four
days to single-symptom patching once already, and the rule that came out of it is that the second
regression means stop and characterise.

Where you are uncertain, say `PLAUSIBLE` on the VERIFY line rather than asserting. A wrong confident
claim costs more than a hedged true one, because I check all of them.

## Then, three things

1. **Exactly one ship-blocker.** The single thing that must be fixed before the engineer opens these
   files again.
2. **What input would break this that nobody has tried.** Name the job shape, the sheet, or the
   storey list that does it. Be concrete.
3. **One statement of what the report is supposed to be** — is it a description of the composition,
   of the saved file, or of the building? It is currently all three in different sentences, and I
   want the one answer that makes every line of it decidable.

Write to `docs/codex/CODEX-DXF-TO-ETABS-FULL-AUDIT-RESPONSE.md`. Ping when done.
