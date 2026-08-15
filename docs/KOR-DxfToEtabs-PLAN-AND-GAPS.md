# DXF → ETABS generator: the product

What this tool is required to be. Written as the standard to audit the code against.
It states no opinion about how far the code currently meets it.

---

## 1. What it is for

A structural engineer points it at any job and gets back an ETABS model with the building's
geometry already entered, so the hours go into engineering instead of typing.

Any job. Not a rescue built around particular buildings.

---

## 2. What it is given

1. The folder of plan DXFs drafting already exports for that job.
2. Any ETABS file from that job — even an empty shell carrying only the storey list.

Nothing made specially for it. No per-job configuration.

---

## 3. What it returns

- An ETABS `.e2k` carrying the geometry read from the drawings: walls at their true
  thicknesses on their centrelines and connected to one another, columns sized and oriented as
  drawn, floor plates, headers over openings, shaft and stair openings cut, pier labels.
- A report stating, location by location, everything it could not do.
- A workbook of the questions it could not decide, each with what it did instead and the
  evidence behind it, for the engineer to answer in a column.

---

## 4. The three properties that make it a tool

**Agnostic.** Nothing in it knows about any particular building. No job numbers, no per-project
branches, no value tuned so that a specific model comes out right.

**Honest.** Anything read and not modelled, discarded, skipped, deduplicated, or fallen back on
appears in the report. Silence about a discard is a defect in itself, whatever the geometry
does — a correct decision made quietly is indistinguishable from a bug.

**Learning.** An engineer's answer becomes a rule that applies to every job afterwards, without
a code change and without her being asked the same question twice. The rules and the evidence
behind them live in `KorStandards`; the code is the machine that applies them.

That requires two separate paths, both mandatory:

1. The generated questions workbook must carry machine-readable metadata beside the visible
   question: rule scope, rule topic, optional setting key, optional setting units, and confidence.
   Visible prose alone is not a rule; it cannot be imported reliably.
2. An answered workbook must be imported back into `KorStandards.analysis.Ruling`, with
   `analysis.RulingEvidence` pointing to the workbook. The next model run then reads it through
   `analysis.vw_RuleSetting`. If a rule changes only a spreadsheet and never reaches that view,
   the tool has not learned.
3. Any question that governs a numeric or boolean generator rule must name its `dxf.*` setting
   key in hidden metadata. A question with only scope/topic metadata records a judgement, but it
   cannot change `analysis.vw_RuleSetting` and therefore cannot change the next generated model.

Production generation must refuse to run when `KorStandards` is not available or when any rule
setting the generator applies is missing from `analysis.vw_RuleSetting`. A built-in value is
allowed only for tests/core harnesses, never from the publish or CLI generation path, and the
report must say that it happened.

---

## 5. What it must not do

No loads, diaphragms, stiffness modifiers, section properties, meshing or design. Those belong
to the engineer and are left untouched. It must never overwrite or duplicate geometry the
engineer has already modelled.

---

## 6. Where things are

| | |
|---|---|
| Engine | `Kor.Operations.EngineeringTools.Core/Dxf/` |
| Tests | `Kor.Operations.EngineeringTools.Core.Tests/` |
| Publish | `tools/Publish-EtabsModel.ps1` |
| Shipped documents | `docs/KOR-DxfToEtabs-*.html` |
| Rules and evidence | `KorStandards` on `KOR-APP01\SQLEXPRESS`, schema `analysis` |
| Rule contract | `analysis.vw_RuleSetting` — key, value, units, confidence, authority |
| Migrations | `C:\VIsual Studio Projects\KOR.Drafter\db\` |

Rule workflow:

1. Generate model/questions:
   `takeoff dxf-to-etabs <dxfFolder> <reference.e2k> <out.e2k> --rules-db <connection> --questions <questions.xlsx>`.
2. Engineer answers the `YOUR ANSWER` column.
3. Import the answers:
   `takeoff dxf-import-rules <questions.xlsx> --engineer <name> --rules-db <connection>`.
4. Generate again. The imported rulings now apply through `analysis.vw_RuleSetting`; no code edit
   is part of the loop.

Two jobs have been run through it. Each folder holds the generated `.e2k`, the report, the
questions workbook, and the drawings under `_DXF-plans-for-rebuild`:

```
\\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)\
    02 Engineering\02 Lateral Design\01 ETABS Models\
\\Kor-fs01\Projects\Projects\03 Residential\31138-01 (2170 W 1st Ave Vancouver BC)\
    02 Engineering\02 Lateral Design\01 ETABS Models\
```

Roughly fourteen hundred ETABS models built by KOR engineers sit elsewhere under
`\\Kor-fs01\Projects\Projects`. Some files in the two job folders are this tool's own output
round-tripped through ETABS rather than an engineer's work; objects named `K` followed by
digits were written by this tool.

---

## 7. Gaps found in the current implementation

These are the holes this plan was missing or that the code did not yet satisfy:

- **No write path from questions to rules.** The workbook had a `YOUR ANSWER` column, but no
  import command and no hidden rule metadata. An answer could sit in Excel forever and never
  change the next model. Fixed: the workbook carries hidden rule metadata and
  `takeoff dxf-import-rules` writes `analysis.Ruling`, `analysis.RulingEvidence`, and
  `analysis.RulingHistory`.
- **Fallback rules were treated as normal production.** The loader returned an empty dictionary
  when the DB was unset, unreachable, or parse-broken. That makes the model run on C# defaults
  while looking successful.
- **Only a subset of settings were applied.** Migration 036 seeds the generator's rule set, but
  the service was reading only a handful of keys. Most thresholds and switches still came from
  `PlanClassificationOptions` and `ComposeOptions`.
- **One active code rule had no DB setting.** `dxf.extend-limit` still existed as a generator
  option but was not seeded in migration 036, so a strict rule check would fail or the code would
  have to keep a hidden fallback.
- **Workbook claims could drift from the model.** The questions workbook was written from CLI
  defaults rather than the rules actually applied by the service, and the spandrel lower clamp
  text still said 20 inches while the rule value was 18. Fixed: the report carries the applied
  rule options into the workbook.
- **Unit scaling missed two composed rules.** `ComposeOptions.InUnitOf` scaled opening height and
  existing-member tolerance, but not the spandrel depth floor/ceiling. A non-inch ETABS model
  would clamp headers with inch numbers in another unit system. Fixed.

Remaining:

- The shipped dossier/one-pager still describe the two checked projects and should not be copied
  to a third job as if they are project-specific evidence for that job.
- Several questions remain judgement-only because no generator switch exists for them yet
  (`O1`, slab-closure policy, perimeter-wall floor fallback). Those answers will be recorded as
  rulings, but they will not change geometry until a corresponding setting and code path exists.
- The code still carries default option values for tests and core harnesses. Production CLI and
  publish paths are DB-authoritative, but the defaults remain a second copy that must be guarded
  by tests and DB checks.
