# CODEX increment 2: which guards earn their keep (small, evidence only)

Written 16 Nov 2026. Increment 1 (`CODEX-FRAMEWORK-INVENTORY-1-RESPONSE.md`, verified 153 of 153 rows) listed the
hooks. This one judges the guards on evidence, so the lead can keep, fix or cut each. Prior art searched: no earlier
doc in Operations docs/ or Framework docs/ gives a per-guard verdict; `CODEX-FLAWS-AND-FRAMEWORK-REBUILD-RESPONSE.md`
warns that counting Ian's catches alone is biased - honour that (step 3).

Write ONE file: `docs/codex/CODEX-FRAMEWORK-INVENTORY-2-RESPONSE.md`. Edit nothing else, and commit nothing.

## Inputs: read these, and only these

All are in `docs/codex/CODEX-FRAMEWORK-INVENTORY-2-work/`, exported 16 Nov from KorStandards (do not query the DB):

- `guard-week-14d.csv` (17 rows): per guard, refusals in the last 14 days, how many already judged right or wrong.
- `refusal-sample.csv` (362 rows): per guard, up to 15 random UNJUDGED refusals (`Picked=random`), plus every
  refusal already judged (`Picked=judged`). `Matched` is the triggering text, cut to 400 characters; `Rule` is the
  guard's rule.
- `mistakes.csv` (115 rows): all of `oversight.Mistake` (27 Oct - 11 Nov): faults caught, by whom (`CaughtBy`),
  and how they were mitigated.
- Increment 1's response, for which script each guard name is.

You may open the first 40 lines of a guard's script to read what its rule means. Nothing else.

## The task

1. **Judge each sampled unjudged refusal**: RIGHT (it stopped a real fault named in its rule), WRONG (the action
   was correct; the refusal only cost rewording or a retry), or UNCLEAR (`Matched` is too short to tell).
   One line each: Id | guard | RIGHT/WRONG/UNCLEAR | the reason in under 15 words.
2. **Per guard**: refusals in 14 days; your sample's RIGHT / WRONG / UNCLEAR; the earlier judgements; and the
   wrong-refusal rate WITH the sample it rests on (for example "9 of 15").
3. **Mistakes against guards**: for each of the 115 mistakes, name the guard whose class it falls in, if any, from
   `What`, `ClassCode` and `Mitigation`. Per guard, count the mistakes in its class that still got through. Then
   count the mistakes no guard covers, grouped by `ClassCode`, and how many of those `CaughtBy` names Ian.
   A guard's catches are its RIGHT refusals (step 2), not this table: a fault it prevented never became a mistake.
4. **Verdict per guard**: KEEP, FIX (it names a real fault but refuses too much; say which rule), or CUT (it costs
   more than it catches). One line of evidence each, citing the counts from steps 2 and 3.

## Limits

- Facts from the files only. Where the files cannot decide, write "not decidable from the export".
- A sample of 15 is a sample: state every rate as X of Y. Do not write "all", "never" or "always" from it.
- Do not search the codebase, read product code, or propose new guards (that is increment 5).
- Budget: about 20 minutes. If you are going past that, stop and write what you have.
