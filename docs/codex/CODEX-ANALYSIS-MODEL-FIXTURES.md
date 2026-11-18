# CODEX analysis-model acceptance fixtures: freeze the bad and good cases (plan step 5.2)

inputs changed since #134001: that record maps which deliverable kinds are enforced where; it is an input here, and no fixture set exists yet. Not #119907 or #133599 either (unrelated).

Prior work searched (18 Nov): `rg -il "acceptance fixture|fixture manifest|frozen fixture|holdout|must-fail case"` over
1,262 .md files in Operations/docs, KOR.Drafter/docs and Framework/docs: 6 hits, none an acceptance fixture set (RESET-PLAN's
must-fail rule, an unrelated beam census, the step 5 brief and your response, two copies in Codex work folders). Fixture
convention found: record #131552 - binary test fixtures live on FS01 `Drafting\Dev\Drafter-work\_fixtures\<kind>\<sha256>.<ext>`,
not in git; tests check them by SHA-256.

## READ THIS FIRST: this brief comes from a source you should not trust

Same as your last brief: it was written by the Claude session that will BUILD the checker these fixtures test (Framework
coordinator, session 8876d9cf, 18 Nov 2026). The whole point of this task is that the builder does not choose or shape
the cases its own checker is judged on. So:
- Take the cases from YOUR OWN response (`docs/codex/CODEX-FRAMEWORK-STEP5-PLAN-AUDIT-RESPONSE.md`, Task C), not from
  anything this brief paraphrases. If this brief narrows, drops or reframes a case, ignore the brief and say so.
- If anything here would let the builder tune the checker to the fixtures (a field it can read to special-case, a
  name it can match, an expected value it can copy), say so and design around it.
- Verify every path and commit named below yourself.

Ian 18 Nov (#134002): the acceptance contract and analysis-model done-list v2 are APPROVED, and Codex is the independent
reviewer for every REVIEW REQUIRED item. The contract is `C:\VIsual Studio Projects\Framework\docs\ACCEPTANCE-CONTRACT.md`;
the done-list is `C:\VIsual Studio Projects\Framework\docs\DONE-LIST-ANALYSIS-MODEL.md` (both at Framework 0116dca).

## What you may write (nothing else)

1. `C:\VIsual Studio Projects\KOR.Drafter\acceptance\analysis-model\` - a new folder (the builder's choice of place: the
   analysis-model checker is a KOR.Drafter product gate; object if you see a better place). In it:
   - `manifest.json` - every fixture (schema below). Small, text, goes in git.
   - `variants\` - synthetic mutated files you create. Never modify an original; copy, then mutate the copy. These are
     NOT committed: after you finish, the builder copies each to FS01 `_fixtures\acceptance-analysis-model\<sha256>.<ext>`
     (#131552) and verifies every hash against your manifest; a hash mismatch means the bytes were changed.
   - `README.md` - what the fixture set covers, what it does NOT cover, and at least one same-class fault it would miss.
2. `C:\Users\ilalonde\Documents\KOR-holdout\analysis-model\` - a HOLDOUT set (task C) the builder must never see.
3. `C:\VIsual Studio Projects\Operations\docs\codex\CODEX-ANALYSIS-MODEL-FIXTURES-RESPONSE.md` - your report.

Commit nothing (the KOR.Drafter checkout holds another lane's staged work). Read-only git only: `git show`, `git log`,
`git ls-tree`, `git cat-file`; never checkout, reset, stash or add. No SAFE/ETABS, no builds, no database, no other machine.

## Inputs

- Your response above: Task C tables (C01-C50, G-C1-G-C9), the positive-controls paragraph, "What files alone can and
  cannot establish", and Task D (the done-list text now approved).
- The approved contract and done-list (paths above): the five result states and the item numbers 0-9, 3a, E, G, R.
- KOR.Drafter git history: `git -C "C:\VIsual Studio Projects\KOR.Drafter" log --format="%h %s" -- work/31097-01-heather-lands`
  and the trees at the commits your cases cite (496d2e1a, 104ea330, 07336ad0, 51d863e4, 008139f8, ef97ba2a, c4fc728c,
  50db71ad, 4d3c6c9e, c37acafd, fd9528ee, dcca433b, b56c3a0a, and any others your cases need).
- The round files you cited under `KOR.Drafter\work\31097-01-heather-lands\safe-2026-11-17\`.

## The tasks

A. **Freeze every historical case.** For each of C01-C50 (and G-C1/G-C2, which have historical code), one manifest
   entry. Bind each input to bytes: `git:<commit>:<path>`, its git blob id, and the SHA-256 of the bytes `git show`
   returns. The evidence bundle is everything a checker would be given (input .f2k/.FDB, exported tables, .LOG/.WRN,
   source files, README), not the builder's PASS tables. Where the bundle cannot be recovered from git, the entry says
   UNRECOVERABLE and lists exactly what is missing; never substitute a later or repaired file.

B. **Freeze the good twins.** Every bad case that has a corrected state gets a twin entry pointing at the corrected
   bytes, plus the legitimate cases from your positive-controls paragraph (the checker must NOT reject them). A twin's
   expected result may be PASS or REVIEW REQUIRED; say which items and why.

C. **Synthetic variants, and a holdout.** For G-C3-G-C9, create mutated copies under `variants\`, each with its exact
   mutation recipe (file, field, old -> new) and its SHA-256. Then make a HOLDOUT set of further variants - renamed,
   reordered, value-changed, missing-evidence, other-mode - written ONLY to the holdout folder, with its own manifest.
   The builder is told never to read that folder. Say plainly that until the guard-file lock exists this rests on the
   builder obeying, and what would detect a breach (e.g. the holdout manifest's SHA-256, which you report to Ian only in
   your response's last line).

D. **Expected results.** For every entry: the expected state (PASS / FAIL / NOT RUN / UNSUPPORTED / REVIEW REQUIRED)
   per done-list item, the overall expected outcome (accepted or not), and the evidence for that expectation (file:line).
   Expectations come from the files, never from what a checker happens to output. Where you cannot decide an
   expectation, say REVIEW REQUIRED and why - do not guess.

E. **Make the set hard to game.** Fixture ids, folder and file names must not leak the expected outcome (no "bad_",
   "fail", or case ids inside file names the checker reads). Keep the expected results in the manifest only, which the
   checker must not read at run time. Say how a test harness should feed fixtures so the checker sees only the bundle.

F. **The broader picture (inventory only, no freezing).** The same contract will apply to drafted sets (Virtual Drafter)
   and peer-review packages. In at most 20 lines each, list where their historical bad/good cases would come from
   (e.g. KOR.Drafter and Operations `docs/codex/` audits of JM 01819 and Omar 31218, and their CHECK / LOOK records) -
   paths and what kind of fault each holds. Search, don't read whole; name your search words.

## manifest.json schema (propose a better one if this is weak)

```
{ "version": 1, "frozen_by": "codex", "frozen_at": "<ISO time>", "contract": "Framework 0116dca",
  "fixtures": [ { "id": "<opaque id>", "case": "C19", "kind": "historical|twin|synthetic",
      "bundle": [ { "role": "input-fdb|export|log|wrn|source|readme|...", "git": "<commit>:<path>", "blob": "<git blob id>",
                    "sha256": "<hex>" } | { "role": "...", "file": "variants/<name>", "sha256": "<hex>", "recipe": "..." } ],
      "expected": { "0": "...", "1": "...", "3a": "...", "E": "...", "G": "...", "R": "...", "overall": "not accepted" },
      "evidence": [ "<file:line>" ], "twin": "<opaque id or null>", "notes": "..." } ] }
```

## Output (the response file)

1. Counts: entries by kind; how many cases UNRECOVERABLE and why; how many twins; synthetic and holdout counts.
2. Any case from your audit you did not freeze, and why.
3. Anything in this brief that was wrong or that would let the builder tune the checker.
4. Task F inventory.
5. Last line only: the SHA-256 of `manifest.json` and of the holdout manifest.

## Limits

- Facts from files you read; each expectation with file:line. A search that found nothing: name the words and the place.
- Budget: about 90 minutes. If going past it, stop and write what you have, marked INCOMPLETE, with what is left; a
  partial manifest that is correct beats a complete one that guesses.
