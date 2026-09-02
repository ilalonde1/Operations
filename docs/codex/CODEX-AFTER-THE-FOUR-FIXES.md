# CODEX — AUDIT WHAT WAS JUST CHANGED, AND FINISH THE LIST

> **Do NOT run `dotnet build` or `dotnet test`.** Verification happens on the dev box; your runner
> hangs for 15+ minutes here and spawns orphan processes that lock build artifacts.
>
> **No destructive git operations. Do not write to `\\Kor-fs01`. Do not publish. Do not commit.**
> Read, measure, report.
>
> Reading the share is expected — the drawings, the reference models and the published output all
> live there. Read them; change nothing.

## Read these two first

- `docs/etabs-handoff/FINDINGS-2026-09-01.md` — the current state of every known issue, updated.
- `docs/codex/CODEX-INTAKE-TO-PUBLISH-AUDIT-RESPONSE.md` — your own last pass. Its three SERIOUS
  findings are the previous state of play; #1 is fixed (see below), #2 and #3 are open and are
  yours to press on.

**Do not re-report anything already in either file.** If your best finding is on one of those
lists, the audit did not happen.

## What changed since your last pass, and why you should distrust it

Four things were worked in one evening, by one person, at the end of a long day. Three of the four
were WRONG first and were caught only because both reference buildings were measured every time.
One was caught by the publish gate after it had already been staged. That is the context: this code
is fresh, it was written under pressure, and it moves structural members.

Commit `8eb181a5`, files `Kor.Operations.EngineeringTools.Core/Dxf/E2kDocument.cs` and
`Kor.Operations.EngineeringTools.Core/Dxf/E2kGeometryComposer.cs`.

### 1. `ModelDoubleHeightMembersOnBothFloors()` — fills a one-storey hole in a stack

Her rule, 1 Sep: *"some columns are double height. In that case they should be modelled on both
floors ... otherwise they're just hanging from L2"*, and *"the same for walls too"*.

**Attack this.** Specifically:

- It keys stacks by **rounded plan position** (`{X:0.#},{Y:0.#}` joined and sorted). Two different
  members at the same rounded position merge into one stack. What does that do at a wall junction,
  or where a column sits on a wall line? Is the rounding safe at the coordinate magnitudes in play
  (these models sit at x≈40,000, y≈28,000 inches)?
- It fills **exactly one** empty row and refuses wider gaps. Is one row always one storey? What
  happens where two storeys sit within `dxf.storeys-at-one-level-gap` of each other and are one
  physical level — does it fill a hole that is not a hole?
- The filled assign is a **textual copy** of the row above with the storey name swapped. What else
  rides along on that line — pier labels, section, mesh flags, restraints — that should NOT be
  copied downward?
- The building-tag guard is now conditional on the model holding more than one tag. In a
  one-building file it is off entirely. Find a case in the shipped 31168 or 31138 where that fills
  something it should not.

### 2. The carry-down — a member whose base lands on nothing goes down one floor

Same method. `Supported` = a FLOOR object on the storey below whose ring contains the member's
first plan point.

**Attack this hardest of the four.** It ADDS structural members, it is capped at one storey by a
loop condition (`row <= bottom + 1`) that is easy to misread, and support is judged by **the first
plan point only** — for a long wall, one end being over a slab counts as supported. Is that right?
What about a member whose base point sits in a floor OPENING, or just outside a plate edge by an
inch? What about a plate on the storey below that belongs to another building in the site file?

Its three withdrawn versions are in the findings file, section F. Do not propose any of them back.

### 3. `max(6, thickness/2)` for "already modelled"

`E2kGeometryComposer.cs`, the wall placement loop. Hers is a centreline, ours comes off an outline,
so the same wall can differ by half its width. Six inches is still the floor.

**Check the direction of the risk.** This makes the tool DROP more of its own walls. On a thick
wall — 30, 42, 60 inches — half-thickness is 15 to 30 inches of tolerance. Is that suppressing
walls that are genuinely separate from hers? Measure it on 31138, where 353 walls are already
skipped as hers.

### 4. Diagonals and KW164 — two things RECLASSIFIED rather than fixed

Both are argued in the findings file with numbers. **Check the arguments, not the conclusions.**
The diagonal claim rests on every failing outline measuring 2.0–3.8 in against a 4 in minimum; the
KW164 claim rests on a 114 in overlap with 179 in extending beyond. If either measurement is wrong,
a real defect is sitting behind it marked closed.

## Then: what is still open

From the findings file, untouched and yours to take further — **B3** wall lengths off by 9–21 in,
**B4** 63 drawn rings that make no panel, **B5** walls standing in a floor void, **C1** the
reconciliation baseline that counts only closed rings and is not trustworthy, **D1** the bridge with
no manifest, **D2** unread wall strengths, **D3** the Bluebeam re-test.

And your own #2 and #3 from last pass: the pre-build reach path that can use fallback rules, and the
tests that return green when the share is missing. Both stand.

## The bar

`file:line`, a concrete input, and the wrong output it produces. Measured on **both** reference
jobs — 31168 is a three-building site with interleaved storeys, 31138 is one building where the
engineer had already modelled the walls by hand. They fail differently, and every mistake this
evening showed up on only one of them.

Rank BLOCKING / SERIOUS / MINOR. Separate confirmed findings from LEADs you could not construct an
input for. A pass that says "these four changes hold up, and here is what I checked" is worth more
than a manufactured finding.

## How to report

Write to `docs/codex/CODEX-AFTER-THE-FOUR-FIXES-RESPONSE.md`.

Lead with a verdict on each of the four changes above: does it hold, and what did you check. Then
findings, BLOCKING first. Then LEADs.

**Do not fix anything. Do not build, test, publish, commit, or write to the share.**
