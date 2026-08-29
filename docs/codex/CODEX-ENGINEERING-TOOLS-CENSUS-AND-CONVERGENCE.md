# Every engineering tool we have, what it is for, and what to do with it

Written 2026-08-29, on Ian's question:

> "Does it make sense to name all the tools we have now and intelligently converge them and retire
> others"

Yes, and it should have come before any of the convergence work rather than after four separate
discoveries of the same duplication. The evidence that it was needed: in one session PdfToSafe was
found re-typing a scale beside `SheetScaleReader`, keeping a second PDF reader beside
`VectorPageReader`, and I hand-rolled a DXF group-code scanner beside `DxfPlanReader` — then wrote
`storey_census.py` to answer a question `takeoff e2k-ask storeys` already answers, having built
`e2k-ask` myself that morning.

**Enumerated, not recalled: 5 app tools, 46 CLI verbs, 57 Core classes.**

---

## 1. The five things a person actually uses

| tool | what it is for | source → result |
|---|---|---|
| **DXF to ETABS** | build a lateral model from issued drawings | DXF (Revit bridge) → `.e2k` |
| **PdfToSafe** | read an engineer's Bluebeam markup into a model | marked-up PDF → `.f2k` / `.e2k` / DXF |
| **Quantity Takeoff** | concrete quantities off a drawing set | PDF → xlsx |
| **Rebar Change** | what reinforcing changed between two issues | two PDFs → report |
| **Structural Takeoff** | quantities from a model or schedules | `.e2k` / IFC / Revit CSV → xlsx |

**None of these should merge.** They answer different questions for different people. What converges
is underneath.

---

## 2. The layer that SHOULD be shared, and mostly is not

Every one of the five reads a drawing. Reading a drawing is four questions, and each has exactly one
right answer that should live in exactly one place:

| question | the answer that exists | who uses it | who should |
|---|---|---|---|
| what geometry is on this page? | `VectorPageReader` | takeoff, PdfToSafe *(since 29 Aug)* | all |
| what scale is it drawn at? | `SheetScaleReader` | takeoff, PdfToSafe *(since 29 Aug)* | all |
| which sheet is this? | `SheetTitleReader` | takeoff | DXF-to-ETABS has its own: `PlanSheetNaming` |
| what does a DXF contain? | `DxfPlanReader` | DXF-to-ETABS | anything that writes a DXF, to check it |

Two of those were converged this session. The other two are the remaining work.

⚠ **`SheetTitleReader` (PDF, by position) and `PlanSheetNaming` (DXF, by vocabulary rules) are two
answers to "which sheet is this".** They are not obviously mergeable — one reads a title block, the
other a filename and a layer set — but nobody has looked, and that is exactly the shape of the
duplication this document exists to stop.

---

## 3. The CLI: 46 verbs, and most are scaffolding

Grouped by what they belong to:

**Ships and is used** — `dxf-to-etabs` · `verify-e2k` · `publish` · `dxf-buildings` ·
`dxf-import-rules` · `e2k-takeoff` · `revit-takeoff` · `ifc-takeoff` · `sco-schedule`

**Diagnostics worth keeping** — `dxf-inspect` · `dxf-render` · `e2k-ask` · `e2k-compare` ·
`pdf-readable` · `scale-scan` · `corpus-read`

⭐These are the ones I keep failing to reach for. `dxf-inspect` found the wall/centreline mismatch in
one command after a hand-rolled scanner had reported phantom layers.

**The `vector-*` family — 13 verbs** — `vector-takeoff` `vector-plate` `vector-plate-auto`
`vector-zones` `vector-synth` `vector-digest` `vector-sched` `vector-dump` `vector-geom`
`vector-signals` `vector-words` + `measure` `estimate` `vision-estimate`.
These are one pipeline's development scaffolding — the vision-fusion takeoff — exposed one stage per
verb. **Candidates for retirement or folding into `vector-takeoff --stage`.** Needs someone to say
which are still load-bearing.

**Single-purpose probes, likely retirable** — `col-text` · `elev-scan` · `graycomp` · `hatch` ·
`overlay` · `perim` · `single` · `render` · `wallconcrete` · `wallplan` · `wallsched` ·
`sched-read` · `sched-tokens` · `dedupe-probe` · `footings` · `rebar`

**⛔Nothing here should be deleted on this document's say-so.** Each needs one check — is it called
by a script, a runbook, or a person — and that check has not been done.

---

## 4. What to do, in order

1. **Finish the reading layer.** One page reader ✅, one scale reader ✅, then look at whether the two
   sheet-identity readers should be one.
2. **Make the DXF the interchange format it can now be.** It has units and text since 29 Aug; the
   open question is a wall's shape (prompt 8) and whether `dxf.column-layer-patterns` /
   `dxf.slab-layer-patterns` widen to accept what our own tools emit.
3. **Audit the 46 verbs for callers**, then retire or fold. Cheap to do, and nobody has.
4. **Leave the five tools separate.** Converging their INTAKE is the win; converging their PURPOSE
   would rebuild the thing Ian already warned about — one tool that emits everything and leaves the
   engineer to sort it out.

---

## 5. The rule this document is really about

**Look before you write, every time, including for a throwaway check.**

A throwaway is where an unverified instrument does its damage: mine invented DXF layers called "6",
"10" and "66" and I reported them as real, where the shipped reader would not have. And the shipped
one has survived things mine has not.

Rule 1 of `CLAUDE.md` already says this. It was written after a corpus walker was built beside one
that already existed. It happened again four times in one day, so the rule is right and the practice
is not — a census is what makes looking cheap enough to actually do.
