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

**What none of them can see** is what ETABS refuses. `AREAASSIGN … Line Ignored.` on import is the
only thing that found KF54 — an outline running down 24 in along one x and back up 96 along the
same one. Right area, right position, no coincident joints, no *proper* self-crossing. There is a
blocking invariant for that shape now, but the general lesson stands:

> **Open the file in ETABS.** A green suite and a clean report describe what the tool believes.

## Driving the bridge

`Send-Bridge.ps1 -File <command.json> [-ComputerName KOR-302N]` drops a command in the Revit
bridge's inbox and waits for its reply. Revit must already be open on that machine — a GUI app
cannot be launched into its desktop session remotely, and the scheduled-task route was tried and
does not work.

`Run-Migration.ps1 -Path <db\0NN_*.sql>` applies a KorStandards migration through the same
connection string the tool and the tests use. `sqlcmd` cannot: integrated auth is refused for this
account. Batches split on a line that is exactly `GO`, the way SSMS does it.
