# KOR engineering tools — the foundation

Written 2026-09-08, the day the PDF intake first reached ETABS. This is the purpose the tools are
built to, the three products they ship as, and the rule that keeps them composable. It is the
document to read before adding a reader, a verb, or a window to `Kor.Operations.EngineeringTools`.

## 1. Purpose, in one sentence

**Everything a drawing set carries is read once, as objects, and every tool is a small module that
takes those objects and hands the engineer or drafter one decision to check — so that the modules
compose, from "what changed on this reissue" all the way to "draft the model" and "do the design",
with a human signing each step.**

## 2. Where the hours go

No office measures a structural consultancy's week; ours does not record time well enough to
try. The industry research agrees on the shape, and the shape is enough to choose:

| Finding | Source |
|---|---|
| Design changes are the largest source of rework; rework runs to 20% of contract value | McKinsey, construction productivity series |
| Engineers spend a quarter to 40% of their time finding, re-entering and re-drawing information that exists somewhere | engineering time-use surveys, 1993–2023 |
| Bad data — inconsistent, untimely, inaccessible — caused 14–16% of rework | Autodesk/FMI, 3,900 professionals, 2021 |
| A&E utilisation sits near 60% while profit swings; the lost capacity is in the handoffs | Deltek Clarity, 46th and 47th studies |

For a structural office that is: reacting to the architect's reissues, building and rebuilding
models from drawings, checking sets before issue, and construction administration. The tools go
where those hours are, and nowhere else.

## 3. The three products

Each is an output segment of the one intake. Each has a user, a moment, an input, an output, and a
done-when that a person can check.

### 3.1 Reissue Impact — "what changed"

| | |
|---|---|
| Who, when | the engineer, the morning a reissued architectural or structural set arrives |
| In | two issues of a set (PDF), or one issue and the model |
| Out | a change list per sheet — columns moved, added, removed; walls and piers changed; openings; slab edges; footings; grid axes; storeys; schedule rows — and an overlay PDF colouring each |
| Time | the day of re-checking a reissue by eye becomes an hour of confirming a list |
| Done when | on three real reissues an engineer checks by hand, the list misses no moved column and no changed opening |
| Modules | `SheetRecord` × 2 → `SheetDelta` (`Intake/SheetDiff`) → `set-diff` verb, overlay, and the rebar change delta as its reinforcing tab |

### 3.2 Model Start — "drawings to model in an hour"

| | |
|---|---|
| Who, when | the engineer starting analysis; the drafter starting the Revit model |
| In | the stick file or the architect's set, and (optionally) the office's reference model |
| Out | an ETABS or SAFE model on the grid, with storeys, columns, walls as piers, plates, footings for SAFE; the same objects to Revit through the Drafter Bridge |
| Time | the first model build and every rebuild after a reissue |
| Done when | 31168's parkade and tower build with plates in under an hour and the engineer signs the column count storey by storey |
| Modules | `pdf-takeoff` (named sheets on the office's layers) → `DxfToEtabsService` (grid by name, §23 of `PdfIntake.md`) → E2K; the Bridge's verbs for Revit |

### 3.3 Set Check — "the set checks itself"

| | |
|---|---|
| Who, when | the gatekeeper and the drafter, before an issue goes out |
| In | the set (PDF), and the model if there is one |
| Out | one page per sheet and one for the set: marks with no placement, schedule rows nothing uses, levels and scales that disagree, footings without labels, grid names that disagree between sheets, storeys that disagree with the model |
| Time | the checking pass |
| Done when | on the last three issues it finds every defect the gatekeeper found, and at least one they did not |
| Modules | the ledger (`SheetInventory`) and the agreements already computed (`PlanAgreesWithItsSchedule`, `StoreyAgreement`, grid by name) → a report verb and a page |

Rebar takeoff and change are not a fourth product: takeoff is a Model Start output, change is
Reissue Impact's reinforcing tab.

## 4. The rule that keeps the modules composable

1. **One reader.** A drawing is read by `DrawingIntake.ReadSheet` into a `SheetRecord`. No module
   opens the PDF itself. A tool that needs something the record does not carry adds it to the
   record, with a ledger row, for everyone.
2. **Typed objects in, typed objects out.** A module takes records or the objects they carry
   (columns, walls, footings, grid axes, storeys, schedules, dimensions) and returns a typed result
   (`SheetDelta`, `DxfToEtabsReport`, `StoreyAgreement`). No module returns prose, a PNG or a
   file as its only output; those are renderings of the result.
3. **One universal statement per rule**, written in the code where it is applied, measured on the
   five local sets before and after, with the ledger, the DXF census, the overlay crops and the
   banked counts. `docs/PdfIntake.md` is the record of them.
4. **Every chain ends at a person.** A module's output names what a human is being asked to
   confirm, and how: the column count per storey, the change list, the QA page. Nothing publishes
   a model or a drawing without that step.
5. **A verb and a test per module.** Each module has a `takeoff` verb that runs it on a file and
   prints its result, and a test that states what it covers and what it does not.

## 5. The module map

```
PDF ──DrawingIntake.ReadSheet──▶ SheetRecord (geometry, schedules, grid, storeys, dimensions, ledger)
                                     │
        ┌────────────────────────────┼───────────────────────────────┐
        ▼                            ▼                               ▼
   SheetDiff (3.1)          DxfExporter → DxfToEtabsService (3.2)   SheetInventory + agreements (3.3)
   SheetDelta               E2K on the grid, by name                 QA page per sheet / per set
        │                            │                               │
   overlay PDF               Drafter Bridge → Revit           gatekeeper's sign-off
   rebar change tab          column loop → schedule
```

Later modules plug into the same record: the column design loop (ETABS results → calc → schedule)
is a consumer of the model that Model Start built; the Revit draft is the Bridge reading the same
objects; a reinforcing model reads the rebar callouts the record already carries.

## 6. What is not built here

Generic drawing-to-BIM, automated member design as a product, and a chat window over drawings.
Others are ahead on each, and none is where the office's hours go. Where a piece of one is needed
(a column designed from the model's forces), it is a module in the chain above, checked by a person.

## 7. Order of work

1. Reissue Impact: `SheetDiff` on two real issues of one job, measured before it is designed.
2. Model Start: parkade plates from the perimeter; storey heights into a shell built without a
   reference; the tower.
3. Set Check: the report verb and its page from what the ledger already holds.
4. Then the Bridge and the design loop as consumers, in the order the engineers ask for them.
