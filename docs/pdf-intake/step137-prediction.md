# Step 137 — a parkade level that arrives last is still the sheet's level

Written 2026-09-24 01:1X PDT, **before** the judgement ran. The rule itself was written 2026-09-23 and has sat
unjudged on its branch since.

## The rule

Where a title names no level at all, and is not a roof or a top floor, a trailing parkade token is the sheet's
level: `Phase 2a & 2b Parkade Plan - P1`, and a view named nothing but `P2`. Read **only** as a fallback of
last resort, so a title that already says what it is cannot be talked out of it by a trailing token. The
anchor is the whole rule — `^` or a dash **with a space on both sides** — because the first cut took any dash
and read `job-p01`, and every `…Stickfile-p07` in the corpus, as parkade level 1. Knob `KOR_STEP137_OFF`.

## Why it cannot be judged by the corpus gate

Its target sets — **30824-01, 30827-01, 30905-01** — have none of the engineer's models. The 2026-09-18 rule
covers exactly this: build the target set and judge there, or park. 30864-01 does have her model and the rule
touches it, so the corpus gate can see that one.

## What is already measured

The title ratchet `TheTitlesTheReaderDidNotUnderstandTests` went **9 titles / 1,888 slabs** (2026-09-23 00:45)
→ **6 / 967** when migration 099 banked FLR as a floor noun → **0 / 0** with this rule in. Every one of the
sixteen titles the reader could not understand now reads something. That is measured, not predicted: the
ratchet was rewritten to 0 and the suite is green at 1,589.

## The prediction

1. **30824-01 gains a parkade it does not have at all.** Its five `Phase 2a & 2b Parkade Plan - P<n>` sheets
   (973 slabs) currently name no storey. Expect P-levels to appear and its storey count to rise by 3–5.
2. **30827-01 gains P1 and P2** — two sheets, 233 slabs.
3. **30905-01 gains its missing P2** — two sheets, 78 slabs.
4. **A set that LOSES a storey is a stop, not a rounding.** This rule only ever fires where nothing else named
   a level, so it cannot take one away; if one goes, the fallback is speaking over something.
5. **30864-01 is the one the corpus can judge**: the rule touches 45 of its slabs. Expect her plate figures to
   move little or not at all, and thickness/openings not to fall.
6. **The six-set gate stays green.** None of the six has a title of this shape — and if one does, the movement
   must be a parkade appearing, nothing else.

## What would stop the bank

- Any set losing a storey.
- 30864-01 falling on her plate area, thickness or openings.
- The six-set gate moving in anything but a new parkade storey.

## What this does NOT claim

It does not place those sheets on a grid, and it does not make the parkade's geometry right — it only stops
the reader discarding a sheet because its title names the level in a place the reader was not looking.
