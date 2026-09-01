# Checking a generated ETABS model

These are the scripts that found the defects of 26–27 August, kept out of a scratchpad because
every one of them found something no test did. Run them against a **shipped `.e2k`**, not against
the composer's output — the whole lesson of those two days is that the artifact is the thing.

Paths below assume the 31168 job folder:

    \\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)\
       02 Engineering\02 Lateral Design\01 ETABS Models\

## The checks

| script | what it answers | what it found |
|---|---|---|
| `members_by_storey.py A.e2k B.e2k` | do two models of one building agree on every shared storey — walls, columns AND plates? | six YMCA storeys shipping with no vertical structure at all |
| `orphan_openings.py M.e2k …` | is any opening bigger than half the plate it cuts, or cutting nothing? | the slab/opening inversion the engineer rejected, still present in three plates |
| `compare_shared.py A.e2k B.e2k` | per-storey area and thickness across two deliverables | LEVEL P1/P2 at 12" in one file and 10" in the other |
| `selfcross.py M.e2k` | rings that cross themselves or repeat a point | (clean — the real fault was a *reversal*, which this misses by design; see below) |
| `plate_areas.py M.e2k` | plate count, median and total area, openings | — |
| `read_questions.py Q.xlsx …` | the workbook's front sheet, as text | rows marked DECIDED that were describing unresolved faults |
| `plan_sheet.py M.e2k out.svg "title" [cols] [px]` | **every storey drawn, on one sheet** | eight faults in one look, after a day of count tables found none of them |

Added 2026-08-31, each one written because a count table could not see what she saw:

| script | what it answers | what it found |
|---|---|---|
| `steps.py M.e2k "<storey>"` | is a plate's outline a raster staircase? segment lengths and H/V alternation | LEVEL 2 shipped with 67 segments of exactly 6.0 in alternating V,H — 114 vertices where the drawing has twenty. **Bounding box could not see it; she sent a picture** |
| `plate_by_storey.py M.e2k ["<storey>"]` | area, point count, bbox and every vertex of each floor plate | C-LEVEL 3 at 12,862 against 14,988 on every floor above — the step, not the outer edge |
| `chains.py plan.dxf [LAYER]` | chains the raw linework and reports each ring's area, gap and loose ends | the L3 outer edge is ONE chain with a 214 in gap, closed by two segments on `JBP_C_B_STRUCT` |
| `overlap.py M.e2k` | members on more than one storey, and two columns sharing one storey and position | proved her "overlap" was NOT duplicate assigns — it is span, which no invariant reads |
| `extents.py M.e2k` | plan extents and w/h of joints and of GRID lines | the model was north-south (0.708) against her grid east-west (1.427) |
| `tags.py plan.dxf` | slab thickness and step call-outs with positions | L3 prints **60** call-outs across 11 thicknesses; L2 prints 22 |
| `mpa.py plan.dxf` | concrete strength printed anywhere on a sheet | every `MPa` in all 139 sheets is a WALL type — slab strength is in no DXF |
| `joints.py M.e2k …` | POINT definitions vs joints actually referenced | 1,075 orphan joints left in the building-C file by the cut |
| `annots.py markup.pdf` | every Bluebeam annotation: author, type, colour, position, text | her three drafting-convention call-outs, and that "Typical slab YMCA (AN)" is rebar work, not feedback |
| `renderpage.py pdf N out.png [zoom]` | one PDF page rendered WITH annotations | her yellow tracing of the L3 outer edge, four days before she had to say it aloud |
| `order.py <teams .log>` / `idbprose.py` | her Teams messages in order, with image ids | the whole 31 Aug thread — see the memory file for the store path |

**What none of them can see** is what ETABS refuses. `AREAASSIGN … Line Ignored.` on import is the
only thing that found KF54 — an outline running down 24 in along one x and back up 96 along the
same one. Right area, right position, no coincident joints, no *proper* self-crossing. There is a
blocking invariant for that shape now, but the general lesson stands:

> **Open the file in ETABS.** A green suite and a clean report describe what the tool believes.

And before that, **render it**. `plan_sheet.py` draws all 63 storeys on one page in about a second,
and on 27 August it showed at a glance what a day of per-storey count tables had not: storeys with a
floor and nothing under it, storeys with structure and no floor, one tower's storey carrying both
towers' columns, and the whole site's ground slab under a single building. Every fault that day was
otherwise found by the engineer opening the model — which is the most expensive way to find one.

## Driving the bridge

`Send-Bridge.ps1 -File <command.json> [-ComputerName KOR-302N]` drops a command in the Revit
bridge's inbox and waits for its reply. Revit must already be open on that machine — a GUI app
cannot be launched into its desktop session remotely, and the scheduled-task route was tried and
does not work.

`Run-Migration.ps1 -Path <db\0NN_*.sql>` applies a KorStandards migration through the same
connection string the tool and the tests use. `sqlcmd` cannot: integrated auth is refused for this
account. Batches split on a line that is exactly `GO`, the way SSMS does it.
