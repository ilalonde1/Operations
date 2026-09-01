# CODEX — SECOND PASS, HARDER, AND THE LAST GATE BEFORE PUBLISH

> **Do NOT run `dotnet build` or `dotnet test`.** Verification happens on the dev box; your runner
> hangs here for 15+ minutes and spawns orphan processes that lock build artifacts.
>
> **No destructive git operations. Do not touch the 31168 job share. Do not publish anything.**
> **Do not commit.** Report; fixes are applied here after you land.

## Read your own first pass before you start

`docs/codex/CODEX-DXF-TO-ETABS-BEFORE-THIS-GOES-TO-HER-RESPONSE.md`. **All ten findings are fixed
or answered**, and three of them were right in a way that mattered:

- **#1** the merge ran before the building cut, so 2,591 of the towers' members rode into the YMCA's
  model on building-C labels. Foreign members removed went **753 → 3,344**. Fixed by ORDER: the
  merge now runs after every cut.
- **#2** the sheet ledger was read after the merge, so a sheet whose objects were absorbed vanished
  from it. Read from a snapshot taken before the merge now.
- **#3** the size ratchet ignored storey. Per storey it is **849/929 (91%)**, not 201/214 (94%). You
  were right that the reset was inflated.

#6 and #7 were answered with the second building rather than defended: the join threshold refuses
25 closures on 31138, all 27–91%, nothing between 10 and 27; the drawn-ring rule fires three times
on 31168 at 93/94/94% and never on 31138.

**Do not re-report any of those.** Assume they are done and look past them.

## What your first pass did NOT catch, and what that tells you

You found the fault in `MergeStackedMembers` → building attribution. You did not find:

- **`E2kModelQuery.Sections`** reporting `StoreysByObject()[obj][0]` — the storey of an object's
  first assign — while the row it read carried its own storey. A section used on twelve floors was
  reported as used on the lowest. That is the NINTH instance of one class, and it reaches an
  engineer-facing answer (`takeoff e2k-ask … sections`).
- **The grade suffix and the material it names** were two separate lookups of the same expression,
  free to drift.

Both are the same shape as everything else: **a reader that keys on an object name, or that takes
one value where a member now has several.** You looked at the publish path and the tests. The next
one is somewhere you did not look — the App, the takeoff/quantity path, the questionnaire, the MCP
surface, anything that consumes an `.e2k` this tool wrote.

**Hunt that class first, and hunt it everywhere, not only in `Dxf/`.**

## The one thing built to end this

`StackMergeChangesNothingButLabelsTests` builds each reference job with the merge ON and OFF and
asserts everything the engineer can see is identical: every (kind, plan position, storey, section)
placement with multiplicity, the counts she reads, how many members the cut removed as somebody
else's, and which drawing each storey came from. Object names and object counts are the only things
allowed to differ. It found the sheet-ledger fault within seconds of existing.

**Attack the harness itself.** What can it still not see? Concretely:

- it compares two builds of the SAME job — a fault present in both is invisible to it
- it does not compare thicknesses, materials, diaphragms, pier labels, openings, or point counts
- it reads the report, not the workbook or the summary page
- it asserts on the SITE model; the deliverable is `--tower C`, which it never builds

If the harness is missing a property that would have caught an earlier finding, that is the most
valuable thing you can report.

## Everything that changed since your first pass

```
be149902  the merge ran before the building cut and let the towers into her model
95ac33f3  build the job both ways and diff it, instead of finding these one at a time
dbf429a7  rule 11, and the three findings the audit was right about
884d8422  the three called minor, and one of them was the ninth of the class
```

Plus migration `061_ADrawnRingBeatsAFillOfItself.sql`, applied to KorStandards, and **CLAUDE.md
rule 11**: on the second instance of anything, stop fixing and build the check that finds them all.

## Judge these specifically

**1. Is the merge in the right place NOW?** It runs after the building cut, the storey cuts and the
top-storey cut, and immediately before `DropObjectsWithNoAssign` / `DropGeneratedOrphanPoints` /
the final readback. Is there any reader of object identity still downstream of it? `summary` is
captured before; `saved` after; `provenance` before. **Is that split right, or is one of the three
reading the wrong side?**

**2. `SpanEveryGeneratedMemberOneStorey`** forces every generated span to 1 when `TowerOnly` is set,
on the reasoning that a per-building cut leaves nothing to step over. `--tower C` leaves 21 storeys,
not 13 — tower levels C never reaches are still in the list. If two of C's own storeys are not
adjacent in the surviving list, span 1 puts a column short of its floor. You flagged this; it is NOT
fixed, because I could not construct the case. **Construct it or show it cannot happen.**

**3. Two ratchets were reset again** — 849/929 and 571/583, per storey this time. Same question as
before, and it is a fair one to keep asking: is the reset honest, or does the new comparison still
credit something it should not?

**4. Rule 11 itself.** It says: on the second instance, stop and build the check. Is that rule
sound, or does it have a failure mode — a class named too broadly, a harness that costs more than
the faults it catches, a trigger that fires on coincidence?

## What is NOT open

Slab thickness zones: the engineer deferred it herself —
`docs/etabs-handoff/transcripts/andrea-2026-08-31-recording.txt`, *"I think it's OK for us to model
it for now"*. `LEVEL P1 MEZZ`: her own reference model has no such storey.

## What to report

`docs/codex/CODEX-DXF-TO-ETABS-SECOND-PASS-BEFORE-PUBLISH-RESPONSE.md`.

Ranked by what it would cost to ship. **measured** or **suspected**, file and line, what you would
measure next. If you find nothing that would cost anything, say so plainly and say what you looked
at — a clean second pass is a useful result, and a manufactured finding is not.
