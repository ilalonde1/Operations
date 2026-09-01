# CODEX — INTAKE TO PUBLISH, THE WHOLE SYSTEM, ADVERSARIALLY

> **Do NOT run `dotnet build` or `dotnet test`.** Verification happens on the dev box; your runner
> hangs for 15+ minutes here and spawns orphan processes that lock build artifacts.
>
> **No destructive git operations. Do not write to `\\Kor-fs01`. Do not publish. Do not commit.**
> Read, measure, report. Fixes are applied here after you land.
>
> Reading files on the share is fine and is expected — the drawings, the reference models and the
> published output all live there. Read them; change nothing.

## What this is

This tool rebuilds a structural engineer's ETABS model from the drawings her office already
produces. It has shipped to one engineer, Andrea, on two real jobs. It is now good enough that the
remaining faults are not obvious, and the last five audits each found real defects that no test
caught. **Assume there are more and that they are in the places nobody has looked.**

You are being asked four questions. They are ranked; if you run out of room, answer them in order.

1. **Are we extracting everything the intake actually contains?**
2. **Are we using it correctly and efficiently once we have it?**
3. **Is the model we produce the best one this input supports?**
4. **Are we banking the knowledge, or re-deriving it every time?**

## The bar for a finding

A finding is `file:line`, a concrete input that triggers it, and the wrong output it produces.
"Could be fragile" is not a finding. "This reads X where it means Y, so on `31168 C-LEVEL 3` it
reports 4 walls when the file has 8" is a finding.

Rank every finding **BLOCKING / SERIOUS / MINOR** and say which of the four questions it answers.
If you cannot construct the failing input, say so and mark it a LEAD, not a finding.

Where you can measure on the real jobs, measure. Both are readable:

```
\\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models
\\Kor-fs01\Projects\Projects\03 Residential\31138-01 (2170 W 1st Ave Vancouver BC)\02 Engineering\02 Lateral Design\01 ETABS Models
```

A claim measured on one job and not the other is half a claim. 31168 is a three-building site
(YMCA + two towers, interleaved storeys); 31138 is one 29-storey building where the engineer had
already modelled the walls by hand. They fail differently, on purpose.

---

## Question 1 — are we extracting everything intake contains?

### The intake surfaces, and who reads each

| Source | Reader | What we take from it |
|---|---|---|
| Revit → DXF export | `KOR.Drafter/src/KOR.Drafter.Bridge/BridgeExec.cs` (`exportdxf`) | the plan set, one DXF per view |
| DXF plans | `Dxf/DxfPlanReader.cs`, `Dxf/StructuralPlanClassifier.cs`, `Dxf/LayerLedger.cs`, `Dxf/DrawingVocabulary.cs` | walls, columns, slab edges, openings, grids, sheet titles |
| Stick-file PDF | `Dxf/StickFileSlabThicknessReader.cs`, `PdfPageTextReader.cs`, `VectorPageReader.cs` | slab thicknesses per storey |
| Bluebeam-annotated DXF | `Dxf/AnnotationOverlay.cs`, `DrawingDigest.cs` | the engineer's markups |
| Schedules | `ScheduleGridReader.cs`, `FootingScheduleReader.cs`, `ColumnDemandSchedule.cs` | member sizes, footings, demands |
| Her reference `.e2k` | `Dxf/E2kDocument.cs`, `Dxf/E2kGeometryReader.cs`, `Dxf/ReferenceRules.cs` | storeys, her own members, sections, materials |
| KorStandards DB | `Dxf/RuleSettings.cs` → `analysis.FormatConvention`, `analysis.Ruling` | every threshold the run uses |
| Her answers back | `Dxf/ModelQuestionnaire.cs`, `takeoff dxf-import-rules` | rulings that overrule defaults |

### What to actually check

- **What is in the DXFs that no reader touches?** Enumerate the layers present across the 139 files
  in `_DXF-from-Revit-2026-08-26` and the 28 in 31138's `_DXF-plans-for-rebuild`, and diff that
  against what `LayerLedger` and `StructuralPlanClassifier` consume. Every layer we ignore is either
  correctly ignored or a silent loss — say which, per layer, with counts of entities on it.
- **Text and blocks.** Dimensions, tags, leaders, attributes, block attributes. Slab thickness
  call-outs are read; what about beam call-outs, column tags, opening dimensions, section marks?
- **Are the schedules being read at all in the DXF→ETABS path,** or only by the takeoff side? Wall
  strengths per wall type are known to be unread today. What else?
- **The stick file.** We take slab thickness. It is a full structural document — what else does it
  carry that we ask the engineer for instead of reading?
- **The Bluebeam markups.** A previous pass concluded they are rebar detailing and therefore not
  useful here. **Re-test that conclusion** — it was reached quickly and it decides a whole input.

### One known-live intake risk, already characterised — confirm or refute, do not re-derive

`BridgeExec.SafeName` maps every `Path.GetInvalidFileNameChars()` to `_`. Two Revit views whose
names differ only in such a character produce one filename. Before `v1.0.35` (commit `95bf6e0`,
1 Sep) the loser was deleted and the winner moved over it — **a lost drawing, reported as a skip,
in a run reporting success.** 1.0.35 keeps both. It is committed and **not deployed**; the shipped
31168 set was exported 26 Aug from a build without it.

Checked here already: every storey carrying members traces to a sheet, and the only storey with no
sheet (`B-LEVEL 41`) is explained — building B's top sheets are LEVEL 39 (ROOF) and LEVEL 40 (UPPER
ROOF), and the storey-shift rule puts that sheet's 2 walls on storey 41. **That is evidence, not
proof**, because the collision leaves no trace in the output folder.

**Your job on this one:** is there any signal in the 139 files, or anywhere else, that would detect
a collision after the fact? If there genuinely is not, say so plainly — that is a finding about
observability, and the answer is that the export needs a manifest.

---

## Question 2 — are we using it correctly and efficiently?

### The pipeline, in order

`Dxf/DxfToEtabsService.cs` is the spine. Read it end to end before judging any part of it.

```
discover ──► classify sheets ──► compose the whole site ──► CUTS ──► stack merge ──► invariants ──► publish
             StructuralPlan-      E2kGeometry-             KeepOnlyTower      Merge-      Shipped-    JobPublisher
             Classifier           Composer                 DropStoreys        Stacked-    Model-      PublishExplainers
                                                           KeepStoreysUpTo    Members     Invariants
```

Two orderings in there are load-bearing and were both wrong at some point: **compose once, cut
after** (composing per building lost members that cross a match line), and **merge after every cut**
(merging first renamed the objects the cut hunted by name, and 2,591 tower members rode into the
YMCA's model).

### What to actually check

- **Every transformation must be applied to every deliverable, or to none.** 31168 ships as two
  files: `31168` is building C, `31168-TOWERS` is the whole site, and the first is the second cut.
  A rule applied to one and not the other gives two answers for one building — this is not
  hypothetical, it shipped on 1 Sep with the same wall priced at 699 yd³ and 301 yd³. Enumerate
  every transformation in the spine and state, for each, whether it runs in both files and whether
  it can change anything an engineer sees.
- **Efficiency where it is actually spent.** `Directory.EnumerateFiles(root, "*.dxf")` not `"*.*"`
  plus a `.Where` (rule 4 in `CLAUDE.md`). Repeated full-document regex passes over `.e2k` files —
  `E2kDocument` re-reads its own line list many times per query; is that costing anything real at
  139 sheets and 65 storeys, or is it fine? Measure before recommending; do not refactor for taste.
- **The rules DB is not a cache.** A missing rule stops a production run by design. Check nothing
  has quietly grown a fallback default.

---

## Question 3 — is this the best model the input supports?

This is the question the other three serve, and it is the hardest to answer from code. Anchor it in
the two reference buildings.

- **Coverage.** `ModelCoverageTests` holds ratchets that may only come down. What fraction of what
  the drawings contain actually reaches the model, per member kind, per job? Where the gap is
  largest, is it a reader limitation or a drawing that genuinely does not say?
- **Her conventions.** `reference_andrea_etabs_modelling_rules` in the repo docs and her own 31138
  model are the ground truth: one object, `LINE` span 1, an assign per storey — 57 of 87 columns
  assigned to ~5.1 storeys each. Where does our output still differ in FORM from a model she would
  have built by hand? Form matters: she has to work in this file.
- **What we refuse to model, and whether we should.** The tool deliberately invents nothing — a slab
  edge that will not close becomes a question rather than a guess. Audit the refusals on both jobs:
  how many, how much area, and is each one genuinely unreadable or just unread?
- **The questions we ask her.** `ModelQuestionnaire` asks what the tool could not decide. Are any of
  them answerable from data we already hold? Every such question is a defect — we are asking a
  senior engineer to hand-enter something on her desk.

### Known open, decided deliberately — do not report these as discoveries

- **Four of her shaft walls.** In the site model `W17/W18/W19` sit on `LEVEL 3/4/5`; in the
  building-C cut they land on `C-LEVEL 3/4/5`, 5.5 in higher, because the cut drops the towers'
  `LEVEL 3/4/5`. The shaft stands inside building C's own floor plate. **Whether `LEVEL 3` is
  building C's storey or a tower's is a question about her model**, and it is being put to her. Two
  tests fail on exactly this and are expected to. If you can settle it from the drawings rather
  than from her, that is a genuinely valuable finding.
- Slab **strength** is on no drawing — page 30 of the stick file prints `14" SLAB`, thickness only.
- Thickness **zones** were deferred by the engineer, not missed.
- Bridge 1.0.35 is not deployed. That is a deploy, not a code fault.

---

## Question 4 — are we banking the knowledge?

The commercial question. Every threshold in this system was measured once, at cost, and the value of
the tool is that it does not have to be measured again.

- **Rules.** `analysis.FormatConvention` and `analysis.Ruling` in `KorStandards` on
  `KOR-APP01\SQLEXPRESS`. Migrations live in `C:\VIsual Studio Projects\KOR.Drafter\db\`. Every rule
  carries a value, a unit, a confidence and a "why it holds". **Audit the rules as a set:** is each
  one a universal truth with a project-independent value, or a number that only happens to work on
  these two jobs? Name any that are the latter — that is the difference between a product and a
  pair of bespoke scripts.
- **The scoping direction.** The intended design is: universal rules with project-scoped overrides
  written from her answers (`slab-count.31168.LEVEL 1 MEZZ = 3`, migration 058). Is that mechanism
  actually load-bearing yet, or is it one example with everything else still global?
- **The invariants are the real asset.** `Dxf/ShippedModelInvariants.cs`, the differential harnesses
  (`StackMergeChangesNothingButLabelsTests`, `ShippedModelsAgreeWithEachOtherTests`,
  `MemberPlanStoreyMultisetPreserved`), and ~20 tests that rebuild both buildings from real
  drawings. **Audit their coverage honestly:** what class of fault could pass all of them?
- **Is any knowledge held only in prose?** Code comments here are unusually rich and carry the
  reasoning behind decisions. Where a comment states a rule that no test enforces, that rule will be
  broken by the next edit. List those — comment-only invariants are the highest-yield thing you can
  find under this question.

---

## The failure classes this system actually has

Every audit so far has found instances of the same few shapes. Hunt these first; they are where the
next defect is.

1. **A reader keyed on an object NAME where a member is an ASSIGN.** The stack merge gives one
   object a label for its whole height, so `StoreysByObject()[obj][0]` silently means "the lowest
   storey this label appears on". **Found nine times** — report counts, publish gate, coverage
   audit, benchmark, plausibility heights, sheet ledger, baseline counts, building attribution,
   `E2kModelQuery.Sections` — and a tenth on 1 Sep in `FloorGapDetails`, which named four plateless
   storeys where the file had three. **Search every consumer of a `.e2k` this tool writes**, not
   just `Dxf/`: the App, the takeoff path, the questionnaire, the MCP surface.
2. **A check that cannot fail until after it is too late.** `ShippedModelsAgreeWithEachOther` read
   only the published files on the share, so by construction it could not fail until a publish had
   already happened — which is how a wall got two heights in two shipped files. It now also runs on
   models built in the test. **Are there others?** Any check whose input is an artifact rather than
   a build is suspect.
3. **A gate that passes by not running.** The same test skipped silently on a null projects root and
   reported green in 2 ms. Find every `return` that turns "I could not check" into "it passed".
4. **A harness whose name is broader than its coverage.** `CLAUDE.md` rule 11 requires every check to
   state what it covers, what it does not, and one same-class fault it would miss. Check that the
   harnesses actually carry that, and that the statement is true.
5. **A differential is blind to a fault present in both runs.** Where that matters, an invariant on
   the finished file has to be the first gate.

---

## Already audited — do not re-report

Read these before starting; all findings in them are fixed or answered:

- `docs/codex/CODEX-DXF-TO-ETABS-WHOLE-SYSTEM-AUDIT.md` — the characterisation of storey/building
  attribution
- `docs/codex/CODEX-DXF-TO-ETABS-FULL-AUDIT{,-RESPONSE}.md`
- `docs/codex/CODEX-DXF-TO-ETABS-BEFORE-THIS-GOES-TO-HER{,-RESPONSE}.md` — ten findings
- `docs/codex/CODEX-DXF-TO-ETABS-SECOND-PASS-BEFORE-PUBLISH{,-RESPONSE}.md` — five findings
- `docs/codex/CODEX-DRAWING-INTAKE-CONVERGENCE-AUDIT{,-RESPONSE}.md` and the three
  `CODEX-DXF-INTAKE-AUDIT*.txt` — earlier intake passes
- `CLAUDE.md` — eleven working rules, each written after it was broken at cost. Rules 9, 10 and 11
  are about this exact system.

The current state: **810 of 812 tests green**, the 2 failures being the known `LEVEL 3` question
above. All three published models pass every publish-blocking invariant.

## How to report

Write to `docs/codex/CODEX-INTAKE-TO-PUBLISH-AUDIT-RESPONSE.md`.

Lead with the four questions and a one-line verdict on each — including, where it is true, "yes,
and here is the evidence". A pass that says nothing is worth more than a manufactured finding, but
if you cannot find anything under a question, say what you checked so the next reader knows what is
covered.

Then the findings, BLOCKING first, each with `file:line`, the triggering input, and the wrong
output. Then the LEADs you could not confirm.

**Do not fix anything. Do not build, test, publish, commit, or write to the share.**
