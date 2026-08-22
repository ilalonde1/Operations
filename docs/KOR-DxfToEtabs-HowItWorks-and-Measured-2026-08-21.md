# DXF → ETABS: how it works, and what it actually scores

Written 2026-08-21, after the first day this tool was measured against engineers' own models on
buildings it had never seen. Supersedes nothing; the older docs describe the two jobs it was built
on, which is the problem this one is about.

---

## The flow, end to end

1. **The drafter models in Revit.** Their normal production work. Nothing is done for this tool.
2. **The bridge exports.** `KOR.Drafter.Bridge`, verb `exportdxf`: opens the model, walks the plan
   views, exports each with shared coordinates, and writes `--Structural Plan - <level>.dxf`. It
   reads `view.GenLevel.Name` in the same loop, which is where the level list comes from.
   Pass `viewtype: "EngineeringPlan"` — the default is `FloorPlan`, which is the architectural set.
3. **This tool reads those DXFs** and writes an `.e2k`.
4. **The engineer imports the `.e2k`** into ETABS — one import, whole building — then does the
   engineering: loads, stiffness modifiers, design.

Two things that are commonly assumed and are wrong:

- **ETABS does not import the DXFs.** It can show one as an underlay; it will not build members
  from one. The `.e2k` this tool writes is the only thing imported.
- **The `.e2k` is not a Revit export.** There *is* a direct Revit→ETABS exporter — CSiXRevit, which
  is what produced 31168's `_detached` file and why its sections are named `Rvt-Wall0` — but it
  carries levels, materials and grids with almost no geometry: 31 wall panels for a 40-storey site.
  That is the gap this tool fills.

SAFE is not on this path. Slab design has its own tool in the app (PDF → SAFE, F2K).

## What it needs as input

**A folder of structural plan DXFs, and a list of levels.** That is all.

Until 2026-08-21 it also demanded an engineer's `.e2k`, which is why it had run on two jobs in two
weeks: those were the two that had one. Measured on 31168, of everything in the output 98% was read
off the drawings and the reference contributed 25 members. What the reference actually carried:

| | where it can come from instead |
|---|---|
| level names and elevations | Revit, via the bridge. The only thing a plan drawing cannot say, being flat. |
| materials and sections | an office template — these are KOR standards, not job facts |
| grids | already drawn, on `JBP_G_GRAPH2`, on every sheet |

`E2kShellBuilder` now builds the shell from a level list (`name, elevation` a line, comma or tab,
pasted headers ignored). CLI: `dxf-to-etabs <dxf> - <out> --levels levels.csv`.

## How it reads a drawing

Per sheet, per layer family (`JBP_V-WALL`, `-1`, `-2` are pooled):

- dashed runs stitched, endpoints joined within 0.05", gaps bridged to 6", giving closed loops and
  open chains
- **wall layers** — opposite faces of a closed loop are paired: parallel, overlapping, with the
  material between them *inside* the outline. The wall goes on the centreline, thickness = the gap.
  Solid blobs become piers. Anything that pairs with nothing is flagged.
- **column layers** — footprints, sized and angled
- **slab layers** — largest rings are plates, rings inside them are openings, under 400 sq ft is
  linework

The sheet's **filename** decides which storey it lands on. That is not cosmetic — it is the only
link between a drawing and a level.

## What it scores

Method: take an engineer's own model, strip it to a shell (storeys, materials, sections, grids, no
members), build from the drawings against that shell, and diff the result against the full model.
The engineer's model is **only ever the yardstick**, never an input.

### 31065 — 5350–5430 Heather Street · never seen before · 14 sheets

| storey | her wall length found | walls within 12" |
|---|---|---|
| L2, L3, L10, L15 | 94–95% | ~55–65% |
| P1, P2 | 91–93% | 34/50 |
| L19 | 83% | |
| **L1** | **56%** | |
| **L5** | **65%** | |

Columns: **1,077 of 1,097 within 6 inches, zero offset, median residual 0.0"**. Extents identical.

The wall count reads worse than the wall coverage because she splits a wall at every intersection
and this produces one longer panel over the same run — 36 panels against her 52, covering 95% of
the same length. **Panelisation is not cosmetic**: pier labels and the forces she designs from
depend on where panels split.

L1's gap has a diagnosed cause (below). L5's is six walls that continue up from lower storeys and
are not redrawn on the L5 sheet.

### 31104 — 3080 West Broadway · never seen before · 9 sheets

**It does badly, and this is the important result.**

| | 31065 | 31104 |
|---|---|---|
| columns within 6" | 1,077 / 1,097 | **14 / 276** |
| median column residual | 0.0" | **70"** |
| walls within 12" (P1) | 34 / 50 | **3 / 34** |

Overall extents match to the inch (X −571..1797 against −572..1797), so the building is in the right
place; individual members are 70–130" from hers. Not a global offset — something per-member or
per-storey.

Three of nine sheets placed nowhere, losing 51 walls and 55 columns:

- `ELEV- OVERRUN` — no level number in the name
- `LEVEL 1 MEZZ AND LEVEL2 CANOPY` — reads as levels 1 and 2, lands on neither
- `LEVEL P2` — **the model has no P2.** The drawings cover a parkade level the engineer's model
  does not have.

Its sheet names also carry a job number and address before the title —
`31104-01 2520 Balaclava-Structural Plan - LEVEL P2.dxf` — where 31168 and 31065 both use
`--Structural Plan - `.

**Read together: 31065 shares 31168's drafting conventions and scores well; 31104 does not and the
tool degrades sharply.** Every threshold in this tool was measured on two buildings drawn by one
office to one convention, and the agnosticism tests cover layer NAMES only. This is the first
measurement of what that costs.

## Open, with causes known

**Wall faces in different open chains are never paired.** The per-chain pass finds a wall only where
one chain traced both faces. On 31065's ground floor the exterior wall arrives as 19 open chains
that never close, its two faces in different chains; pooled they pair at 9.8" and 11.8" — her own
walls are 10 to 16 — the largest run 85 feet. Implementing it took L1 from 56% to 82%. It is **not
committed**: it also raised the coverage ratchet on both reference buildings (26→30, 7→10), meaning
three walls read and then dropped, cause not found. That counter may only come down.

**Level 1 slab edges do not enclose a floor, on more than one job.** On 31168, `JBP_C_SLABEDG` spans
the full 334×235 ft footprint as 66–79 open chains, and at every gap from 0.05" to 72" the largest
region it encloses is 119 sq ft. There is nothing to close; no tolerance produces that floor. Those
storeys now take the plate from below, marked INFERRED.

**Hatch is never read.** Every 31065 sheet carries concrete as HATCH on `JBP_C_HATCH` — 88 on Level
2 alone — and none of it is read. If hatch ever carries structure rather than fill, that is a hole
on every job. Unanswered; worth asking drafting.

## What is banked in KorStandards

~40 rules, printed with every run, each with its authority. Two worth knowing:

- `dxf.max-wall-thickness = 60` — measured across 1,126 engineer models, where 42" appears 1,256
  times and 48" 831 times, all ordinary tower core walls. **Do not roll it back**; it is portfolio-
  correct. It is also too loose for any single job: check against the thickest wall in *that job's*
  reference instead.
- `dxf.max-column-size = 132` — same measurement, 99.2% of 7,538 columns.

## The failure that produced most of this document

31168 was published to an engineer with eight storeys of tower in it — a building she had said was
out of scope — plus a wall 132 inches thick. Both survived because the output was never opened and
looked at before it was sent. The cut used, `-TopStorey C-ROOF`, removes storeys *above* an
elevation; the towers' `LEVEL 3`–`LEVEL 10` carry no prefix and sit *below* the mid-rise's roof.
Neither a name filter nor an elevation filter can see them. `--drop-storeys` names them.

---

# Drafting conventions, and what travels between them

Every convention below was met by running the tool against a real model, not by reasoning about
what drafting might do. **The rule this section exists to state: nothing about a drawing set is
assumed until a model has been run through and the result measured.** Each new model either
confirms the table or adds a row, and adding a row should be a change to the matcher, not a
per-job flag somebody remembers to type.

## Sheet naming — what tells the tool which storey a plan belongs to

| source | example filename | how it places |
|---|---|---|
| KOR, 31168 / 31065 | `--Structural Plan - LEVEL 5.dxf` | level number in the title |
| KOR, 31168 ranges | `LEVEL 4 PLAN (L4-L14) - CONCRETE OUTLINE - A &B` | range expands to 11 storeys; `BLDG A&B` narrows the building |
| KOR, 31104 | `31104-01 2520 Balaclava-Structural Plan - LEVEL P2.dxf` | job number and address prefixed before the title |
| KOR, sheet-numbered | `S2-40-1_1_LEVEL 3 PLAN - CONCRETE OUTLINE - BLDG C` | sheet identifier stripped first, or its digits read as levels |
| Autodesk sample | `--Structural Plan - Parking.dxf`, `Top of Footing`, `R2`, `Parapet 2` | **no number at all — matched on the level's NAME** |

The last row is the one that broke it. Matching on level numbers is a fact about one office's
naming, not about buildings: four of Autodesk's nine structural plans landed nowhere and the
building came through with those floors missing. Names are now compared directly once numbers find
nothing — case, underscores and hyphens set aside, whole words only, so `L2` cannot claim
`L1_43_High`. Numbers still win where there are any, so no numeric job is affected.

Placing all nine took that sample from 5 storeys / 51 walls / 70 columns to
**9 storeys / 110 walls / 104 columns.**

## Layer naming — what the tool reads

| source | walls | columns | slab edge |
|---|---|---|---|
| KOR | `JBP_V-WALL` (+ `-1`, `-2`) | `JBP_V_COL` | `JBP_C_SLABEDG` (+ `-1`, `-2`) |
| Autodesk sample (AIA) | `A-WALL` | `S-COLS-SYMB` | `A-FLOR` |

Defaults are `WALL`, `_COL`, `SLABEDG`. On the AIA set that reads walls, **no columns** — `S-COLS-SYMB`
has a hyphen where the default expects an underscore — **and no slabs**. Supplying
`--wall-layers A-WALL --column-layers S-COLS --slab-layers A-FLOR` fixes it.

**Still a per-job flag, and it should not be.** Nothing derives the layer roles from the drawing.
That is the next convention to make travel: the tool can see every layer, how many segments each
carries, and what shapes they close into, which is enough to propose a mapping and ask once.

Layers KOR draws that are deliberately NOT read: `JBP_C_B_STRUCT` (ruled out by the engineer,
migration 042), `JBP_C_HATCH` (unread everywhere — if hatch ever carries structure that is a hole
on every job, and it has not been asked about).

## Getting the drawings out of Revit

`KOR.Drafter.Bridge`, verb `exportdxf`, on a machine with Revit and the add-in — today only
**KOR-302N** (Revit 2020–2026 + bridge). KOR-308 has Revit and no bridge; KOR-210 has Revit 2024
and ETABS.

Drop `<id>.json` in `C:\KOR.Drafter\bridge\inbox`, read the reply from `outbox`:

```json
{ "verb": "opendoc",   "path": "C:\Temp\Model.rvt", "detach": true }
{ "verb": "exportdxf", "doc": "Model", "folder": "C:\Temp\dxf", "viewtype": "EngineeringPlan" }
```

`viewtype` matters — the default is `FloorPlan`, which is the architectural set. It exports with
shared coordinates and names each file after the level, which is what makes placement possible at
all.

Measured on the Autodesk sample: open 39 s, export of 9 plans 21 s. The bridge answered Revit's
unresolved-references dialog by itself.

One limit found: **the Revit API cannot close the active document.** `closedoc` fails if the model
is the one on screen and another document is open, so a model opened this way on somebody's live
session has to be closed by hand.
