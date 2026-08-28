# Codex brief — every engineering tool we have, what we want, and the gap between

## Why

We keep building things we already own. Today an engineer was found hand-tracing prelim PDFs in
DraftSight while `Kor.Operations.App/EngineeringTools/PdfToSafe/` — 45 source files, 28 test files,
57 development batches — sat in the app already exporting PDF geometry to DXF, ETABS, SAFE and
SAP2000. Nobody knew. That is the third time this month.

So: a complete accounting first, then the target, then the gap. Nothing gets built until this exists.

Read-only. No builds, no tests, no git commands, no file changes. Read the code and report.

## Part 1 — the inventory

Account for **every** engineering capability in the solution. Not a file listing — a capability
listing. For each one:

| field | meaning |
|---|---|
| capability | what it does, in a structural engineer's words |
| entry point | class/CLI verb/UI window a person actually invokes |
| project | which .csproj it lives in |
| inputs / outputs | file formats in and out |
| state | tested? how many tests? last touched? |
| reachable | can a user run it today, and how — CLI, WPF window, MCP tool, or not at all |
| overlaps | anything else here doing the same job |

Start points, not an exhaustive list — find what these miss:

```
Kor.Operations.EngineeringTools.Core/            DXF→ETABS, rebar, IFC, plan estimate, vision
Kor.Operations.EngineeringTools.Core/Dxf/        the DXF→ETABS engine (28 files)
Kor.Operations.EngineeringTools.TakeoffCli/      the CLI: run it with no args for the verb list
Kor.Operations.App/EngineeringTools/             the WPF side, incl. PdfToSafe/
Kor.Operations.App/EngineeringTools.Tests/
Kor.Operations.Mcp/Tools/                        the /ask surface — no engineering tool on it today
C:\VIsual Studio Projects\KOR.Drafter\           the Revit bridge (separate repo)
KorStandards on KOR-APP01\SQLEXPRESS, schema analysis   77 rulings, the rule settings
```

Flag in particular: anything with **no reachable entry point**, anything **untested**, and anything
**duplicated**. One duplication is already known and is the shape of the problem —
`PdfToSafe/EtabsE2kExporter.cs` (351 lines) writes ETABS `.e2k` independently of
`Dxf/E2kGeometryComposer.cs` (1,851 lines) and `Dxf/E2kDocument.cs`. Two engines, one destination,
and only one of them knows the firm's rules.

## Part 2 — what we want

Stated so the gap analysis has a target. This is the intent; challenge it if the code suggests
something better.

**One pipeline.** Drawings in — PDF, DXF, Revit, IFC — structural model out: ETABS, SAFE, SAP2000.
One geometry model, one classifier, one set of reading rules, whatever the source format. A PDF and
a DXF of the same floor must produce the same model.

**One rule set, in the database.** `KorStandards.analysis` already holds 77 rulings from the
engineer. Every path must read them. Today the DXF path does and the PDF path does not.

**The tool never guesses.** Where the drawings do not settle something, it says so and asks. This
exists on the DXF path — a questionnaire with the question, what the tool did, why it matters, and
the measurement behind it — and the engineer's answer imports straight back to the database.

**Answers are SCOPED FACTS, not global rules.** 73 of the 77 rulings are prose only a person can
act on, which is why every correction has become a hand-written global rule that destabilises the
last one. The first scoped fact was banked today — `slab-count.31168.LEVEL 1 MEZZ = 3` — and the
tool now checks itself against it and reports its own shortfall rather than re-asking. That pattern
should be the default: a small set of permanent base rules about how drawings are read, everything
else scoped to a job/storey/sheet and replayed on every run.

**Iterative and conversational.** Engineer drops files in a hopper, gets a broad first model, comes
back with specifics — "the mezzanine has three slabs", "that's not a column" — and the tool applies
them and re-runs. That loop exists today, but with a human translating prose into code between every
turn, which is why it takes days.

**An AI layer over it.** `/ask` on the MCP server (kor-app01:5500) as PM Tools and Financials have.
No engineering tool is on that surface today.

**Verified by looking, not counting.** Shipped-model invariants and a renderer that draws every
storey (`docs/etabs-handoff/`) exist on the DXF path. They should cover every path.

## Part 3 — the gap analysis

Against that target:

1. **What already satisfies it** — name it, so nobody rebuilds it. Be generous here; the cost of
   this exercise being wrong in the other direction is another PdfToSafe.
2. **What exists but is unreachable or unknown** — built, works, no way for a user to get at it.
3. **What is duplicated** — two implementations, which should survive, what it costs to converge.
4. **What is genuinely missing** — and of that, what is a day, a week, a month.
5. **What should be deleted** — dead paths, superseded prototypes, things a newer tool replaced.

Then a build order, justified by dependency rather than by appetite: what must exist before the
next thing is worth starting.

## Two specific questions to answer inside that

- **Can the PDF path feed the DXF path's engine?** `PdfGeometryExtractor` produces slabs, columns
  and lines; `StructuralPlanClassifier` produces the same shapes from DXF into `PlanGeometrySet`.
  If the extractor emitted `PlanGeometrySet`, a PDF would inherit the rules database, the
  questionnaire, the invariants and the compose-once-cut-after architecture, and one ETABS writer
  would retire. Is that as clean as it looks, and what breaks?

- **What would it take to read ETABS results back** — utilisation per member — using the CSI OAPI
  wrapper PdfToSafe already has (`ISafeOapiDriver`, `ReflectionCsiOapiDriver`, `EtabsApiExporter`)?
  We generate models and never look at what the analysis says about them. A competitor is selling
  AI reinforcement optimisation into our clients; optimisation without result-reading is not
  possible, and result-reading is useful on its own.

Rank everything by whether it removes work an engineer is doing by hand today.
