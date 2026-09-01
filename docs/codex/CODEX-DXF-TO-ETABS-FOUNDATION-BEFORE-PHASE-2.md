# CODEX — IS THE FOUNDATION SOLID ENOUGH TO BUILD PHASE 2 ON?

> **Do NOT run `dotnet build` or `dotnet test`.** Verification happens on the dev box; your runner
> hangs here for 15+ minutes and spawns orphan processes that lock build artifacts.
>
> **No destructive git operations.** **Do not touch the 31168 job share.** **Do not publish anything.**
> **Do not commit.** Report, do not apply — this one is a review, not a change.

## Why you are being asked

The owner's words, and they set the standard for this review:

> *"I can't tell you how important building solid foundation on another solid foundation is the way."*

Phase 2 is about to change how members are NAMED and ASSIGNED across storeys — the stack merge
(713 column objects at 219 plan positions become ~219 objects, one per position, assigned per
storey) and then span-1. That work rewrites the composer's output shape. If the ground under it is
soft, every symptom it produces will be blamed on the merge.

So: **before** that starts, find what is still wrong, missing, or merely believed.

⚠ **You wrote the publish port yourself** (`CODEX-DXF-TO-ETABS-PUBLISH-IN-THE-APP-RESPONSE.md`).
Be adversarial about your own work specifically. Four defects were found in it on first real use
and are described below — assume there are more, and assume the ones already found were not the
worst. Do not defend the design; look for what it drops.

## What changed since you last saw this

`tools\Publish-EtabsModel.ps1` (848 lines) is **deleted**. `takeoff publish` is the command. The
port landed as `01d12399`, and `17700f7e` fixed four gate defects found running it end to end:

1. **The prose scanner invented claims out of a table.** The dossier compares two jobs side by side
   ("Wall panels 335 205 / Columns 713 304"). `HtmlToText` turns every tag into a space, so it
   flattens to `...335 205 Columns 713 304 Floor plates`, and a `<number> <noun>` scanner paired each
   label with the previous row's OTHER column. It refused a publish whose model matched the dossier
   exactly. Tables are now dropped before the prose scan; `CheckDossierTable` reads them by cell.
2. **Staleness was asked of the HTML, which never ships** — only the two PDFs are copied. That made
   the gate unclearable except by editing prose that was already correct, i.e. by laundering a
   timestamp. It now asks the artifacts that ship.
3. **Nothing checked the other direction** — prose edited, PDF never re-rendered. Now refused.
4. **An unreadable PDF threw `PdfDocumentFormatException` through the publisher** instead of being
   refused by name.

Plus: the staleness gate now excludes the delivery-pipeline files (`JobPublisher`, `PublishPlan`,
`PublishDiscovery`, `PublishSummary`, `PublishExplainers`, `PublishExternalTools`), because
publishing moved INTO `Core\Dxf` on 31 August and finding a job folder cannot change a count.

**Verified state:** 701 tests green. `takeoff publish 31168 --tower C --top-storey C-ROOF
--drop-storeys "LEVEL P3"` runs clean end to end — 335 walls, 713 columns, 15 floors, every
publish-blocking invariant passed, all four explainer gates cleared, staged only.

## What I want you to answer

### 1. Did the port lose anything from the script? (highest value)

The script is gone from the working tree but recoverable: `git show 01d12399^:tools/Publish-EtabsModel.ps1`.

Read it in full against the C# and report **anything the C# does not do**. Its comments are in
several places the only record of an incident. I care much more about a silently dropped behaviour
than about style. Particular suspicion:

- the one-page shortening loop (8/6/4/3/2, re-measured with `pdfinfo`) and what it reports as dropped
- `RequireRuleSettings` / refusing to publish without KorStandards
- the withdraw-on-failure path (a failing source takes its stale copy OUT of the job folder)
- `-Variant` and `-SkipDossier` semantics
- anything about ordering: what must be true before a file enters the job folder

### 2. Are the four fixes right, or did I weaken a gate?

Especially #2 and the delivery-pipeline exclusion. State plainly if you think either makes the gate
weaker in a way that matters. The counter-argument I acted on is that the claims gate still checks
every number independently — test that argument rather than accepting it.

### 3. The intake side — what is amiss?

The owner asked directly and I have only partly answered. Facts established today:

- **The bridge is C#** (`C:\VIsual Studio Projects\KOR.Drafter\src\KOR.Drafter.Bridge\`), not a
  script. Last commit `34f8ada`, 26 August — the same day 31168's 139 DXFs were exported.
- ⚠ **`BridgeApp.cs` and `BridgeExec.cs` have UNCOMMITTED changes**: version 1.0.35, fixing a real
  defect. Under 1.0.34, two views whose names differ only in characters `SafeName` replaces produced
  the SAME filename; the code added a "skipped" note and then **overwrote the first file with the
  second** — a lost drawing, announced as a skip, in a run reporting success. 1.0.35 keeps both.
  **Unknown: whether 1.0.35 is deployed to KOR-302N, and whether the 26 August export ran under
  1.0.34.** If it did, a drawing may be missing from the set every model since has been built from.
  No `(2)`-suffixed files exist in the set, which is consistent with either "no collision" or
  "collision, first file lost".
- **Slab concrete strength cannot arrive by any current path.** Confirmed in source, two independent
  reasons: `ExportDxf` collects `.OfClass(typeof(ViewPlan))` (schedules and drafting views are
  neither), and skips any view with `GenLevel == null` (general notes carry no level). And **none of
  the bridge's 34 verbs reads a material or a concrete grade.** Every `MPa` string in all 139 sheets
  is a wall type. This is the one number the engineer has been asked for twice.
- **Not a fault:** the export deliberately takes all views and the classifier refuses 57 of 139 by
  the banked rule `dxf.non-structural-sheet-patterns`, naming each. Key plans are kept ON PURPOSE —
  building C's ground-floor walls exist only on the site-wide core-wall key plan, and dropping them
  once cost 66 walls and 108 columns. I raised this as an alarm and was wrong; do not re-raise it.

**Answer:** is reading the slab grade from Revit parameters (rather than from a drawing, or by
asking the engineer every job) the right fix, and what is the smallest honest version of it? And is
there anything else on the intake side that is believed rather than measured?

### 4. One concrete open defect

`B-LEVEL 28` ships carrying members with **no floor plate** (advisory `storey-with-no-floor`). A
drawing for it exists and was exported: `--Structural Plan - S2.32.1_1_LEVEL 28 PLAN - CONCRETE
OUTLINE - BLDG B.dxf`. So the sheet arrived and the plate did not. `B-LEVEL 41` also has no plate,
but no drawing exists above LEVEL 40, so that one is honest.

Say what you think happened to B-LEVEL 28's plate. Do not guess a fix; say what you would measure.

### 5. Is the ground good enough for the stack merge?

The merge must **only RENAME** — preserve the exact set of (plan position, storey) pairs. A previous
attempt ADDED members (C LEVEL 2 columns 36 → 60) and is stashed. Target shape comes from the
engineer's own 31138 model: 87 column objects, every one LINE span 1, 57 of them assigned to ~5
storeys each.

Given what you find above, say whether anything must be fixed BEFORE that starts, and name the
check that would catch the merge going wrong on the first run rather than the fifth.

## What to report

`docs/codex/CODEX-DXF-TO-ETABS-FOUNDATION-BEFORE-PHASE-2-RESPONSE.md`.

Rank findings by what they would cost if left. For each, say whether it is **measured** or
**suspected**, and name the file and line. A finding I can act on beats five I have to investigate.
If something I have asserted here is wrong, say so first and plainly — that is the most useful
thing in the report.
