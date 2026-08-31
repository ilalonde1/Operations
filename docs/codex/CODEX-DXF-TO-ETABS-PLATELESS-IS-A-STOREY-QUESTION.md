# CODEX — "NO PLATE AT ALL" IS A QUESTION ABOUT A STOREY, NOT ABOUT ITS FLOOR

> **Do NOT run `dotnet build` or `dotnet test`.** I have run both; the results are below and they are
> what this brief is made of. **No destructive git operations.** **Do not touch the 31168 job share.**

Round two on `CODEX-DXF-TO-ETABS-REPORT-AFTER-THE-CUT.md`. I built it, ran the filtered suite, and
regenerated both 31168 models from a local DXF copy to a scratch folder. **Most of it is right and
must not be disturbed.** One thing is wrong, one row regressed, and one of your own tests fails —
and I think the first and the third are the same bug.

## What landed and is verified. Do not change any of this.

Measured against the shipped 28 Aug files, regenerating both models from the same drawings:

- **Orphan joints gone.** Building-C model: 1835 POINT definitions → **760, of which 760 are
  referenced. Zero orphans.** Site model unchanged at 1835/1835, correctly, because nothing is cut
  from it.
- **Geometry untouched, which was the hard constraint.** Both models: walls 335 / 1410, columns
  713 / 2365, floors 15 / 89 — identical to the shipped files. The building-C storey list is
  **byte-identical** to the shipped one. `LEVEL 1 MEZZ` still carries its three plates.
- **The sheet table now sums to the model.** The 11 placed rows total exactly **335 walls, 713
  columns, 15 slabs**. That is the strongest evidence the recount is right, and it is what the whole
  change was for. The tower sheets have moved to *"Read but not placed on any storey in this model
  (67)"* with zero counts, which is correct and is what the code comment at `:2217` always asked for.
- **`LEVEL 1 MEZZ` is no longer reported as having no plate.** The false line is gone from the report.
- **Workbook S7 no longer asks about `B-LEVEL 28`** in the building-C file. The row is absent.
- **A latent count error was fixed as a side effect** — and I want you to know it was not a
  regression, because it will look like one. "Storeys built" went 13 → 14 (building C) and 63 → 64
  (site). Both files carry exactly one more `STORY` row than the old number, the last being `Base`.
  The new count equals *(STORY rows − 1)* in both, which is the definition the publish script states.
  **The old number was under by one and nobody noticed.**

## The defect: a storey with no slab is being called "partially covered"

`B-LEVEL 28` is the one storey in the site model that genuinely has **no slab** — its outline will
not close, the engineer was told so, and `-InferFloors` stays off precisely so the hole stays
visible. Measured in the regenerated site model:

```
plates assigned to B-LEVEL 28 : 0
members on B-LEVEL 28         : 40
plates on A-LEVEL 28          : 1        STORY "B-LEVEL 28" HEIGHT 36
                                         STORY "A-LEVEL 28" HEIGHT 104
```

The two towers' 28th storeys are **36 inches apart, so they are one FLOOR by elevation.**

Shipped report, correct:

> *1 storey(s) carry walls or columns and no floor plate, so they have no diaphragm: **B-LEVEL 28**.
> Nothing was borrowed or invented for them; add a plate if these storeys need one.*

Regenerated report, wrong:

> *1 storey(s) have floor plate(s), but most of their walls and columns stand outside every plate on
> the floor: **B-LEVEL 28**.*

It does not have floor plates. It has none.

**The cause** is in `E2kDocument.FloorGapDetails()`: the new branch is `if (above.Count == 0)
plateless.Add(...)`, and `above` is `platesOnFloor[floor]` — **the plates on the whole floor, which
includes the other tower's.** Tower A's plate, two hundred feet away horizontally, makes tower B's
plateless storey look plated.

⚠ **Do not fix this by grouping per storey instead of per floor.** That reintroduces a bug the
comment above the code describes: after the ground-floor merge the shared plate sits on `B-LEVEL 1`
while its 108 columns sit on `A-LEVEL 1` an inch and a half below, and a storey-wise reading called
that a slab supported by air. The floor grouping is load-bearing and stays.

**The rule that satisfies both cases is coverage, not presence.** You already compute it:

- `covered == 0` → **no plate at all.** Nothing on this floor stands over any of this storey's
  members. That is `B-LEVEL 28`, and it earns the strong "add a plate" sentence.
- `0 < covered * 2 < count` → **mostly uncovered.** Some plate does reach some of it. That is the
  partial-coverage note, and it is where a mezzanine would land if it were not suppressed.
- Shared ground floor: A-LEVEL 1's columns genuinely sit under B-LEVEL 1's plate, so coverage is
  high and neither fires. The existing behaviour is preserved.

One predicate, three outcomes, and it keeps the floor grouping.

## Your failing test — I believe it is the same bug

```
E2kDocumentTests.FloorGapDetailsSeparatesNoPlateFromMostlyUncoveredAndSuppressesMezzanineCoverage
  DxfToEtabsTests.cs:2309
  Assert.Contains() Failure: Collection: []   Not found: "LEVEL 1"
```

`gaps.MostlyUncovered` came back empty. In that fixture `LEVEL 1` has plate `KF1` and three columns,
one of which (`KP5` at 50,50) is inside it and two of which (`KP6`, `KP7` at x 300 and 340) are far
outside — so it is the textbook mostly-uncovered storey and nothing was added. Your first assertion
passed, which tells us `LEVEL 3` reached `FloorsWithNoPlate`; the question is what swallowed
`LEVEL 1`. **Fix the predicate first, then re-derive what that fixture should assert** — and check
whether `LEVEL 1` was landing in `plateless` all along, which would make the test's first assertion
pass for the wrong reason.

Everything else is green: **681 passed, 1 failed, 682 total, 20 s.**

## The regression: workbook row F2 now names no storeys

Building-C workbook, regenerated:

> **F2** — *"OUR DECISION — these are left without a plate rather than given an invented one:
> **the storeys named in the report**. They need a slab edge drawn… There is no closed outline on any
> slab layer of these storeys."*

"The storeys named in the report" is a placeholder where a list belongs. The building-C model now has
no plateless storeys at all, so **the row should not be emitted**. As it stands it asserts a
confident fact about an empty set and gives her nothing to act on — which is the same defect class
the whole exercise was about, in a new place. Suppress the row when the list is empty; name the
storeys when it is not.

While you are there: `J1` is now absent from the building-C workbook. Confirm that is deliberate
(the list is empty) and not the same placeholder problem resolving differently.

## What I want back

The predicate fixed, the F2 row suppressed when empty, and the fixture asserting the corrected
intent. Then a short note on whether `covered == 0` changes any other storey in either 31168 model —
I will regenerate both and diff the reports against what I have, so tell me what to expect.

Write to `docs/codex/CODEX-DXF-TO-ETABS-PLATELESS-IS-A-STOREY-QUESTION-RESPONSE.md`. Ping when
applied.
