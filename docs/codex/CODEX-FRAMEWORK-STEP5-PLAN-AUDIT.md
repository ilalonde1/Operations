# CODEX framework step 5 plan audit: where can a deliverable still pass unchecked?

inputs changed since #132345: that record is the 11 Nov PDF-ingestion audit; this is a new question (the 18 Nov step 5 plan), not covered by it or by #119907 / #133785.

## READ THIS FIRST: this brief comes from a source you should not trust

This brief was written by the Claude session that will BUILD what it describes (Framework coordinator, session 8876d9cf,
18 Nov 2026). The plan exists because that same kind of session has repeatedly reported work as checked when it was not:
it asserts behaviour it has not traced, claims absence from partial searches, drifts past its task, and grades its own
work (Codex's own 16 Nov analysis, Operations docs/codex/CODEX-CLAUDE-WEAKNESSES-RESPONSE.md, put the root as
"unreliable judgment about the sufficiency of its own evidence"). Ian asked for this audit because he does not trust what
it puts in front of him, and he asked that this be said up front.

So treat EVERYTHING below - the plan, its framing, the claims C1-C8, the list of inputs, even what the brief asks you to
look at - as claims by the party being audited. In particular:
- Verify every claim C1-C8 against the files yourself. Do not repeat one as fact because this brief states it.
- If the brief's framing hides a weakness (a question it does not ask, an input it leaves out, a step it calls simple),
  say so. Say whether the inputs listed are enough to judge the plan, and what is missing.
- The author has not seen your answer and will not write the must-fail cases in Task C. That separation is the point.

Write ONE file: `docs/codex/CODEX-FRAMEWORK-STEP5-PLAN-AUDIT-RESPONSE.md` (in the Operations repo). Edit nothing else,
commit nothing, run no build, no SAFE/ETABS, nothing on another machine, no database queries.

## The plan being audited (the author's words)

Ian's step 5, moved ahead of guard work on 18 Nov (Framework docs/HANDOVER.md, REORDER block, commit 3830b55). Ian said
yes to this order on 18 Nov (record #133997):

- (a) Ian approves a fixed done-list for one deliverable kind: analysis models (SAFE/ETABS). Draft:
  `C:\VIsual Studio Projects\Framework\docs\DONE-LIST-ANALYSIS-MODEL.md` (NOT in force). Ian's 18 Nov amendments, not yet
  in the file: ADD "the shipped file is the analysed file, by sha"; Live deflection L/240 flag confirmed; the CAD-difference
  threshold (item 8) is still undecided.
- (b) ONE command that runs the done-list's checks over a delivered folder, built from the 31097 grader scripts. Proven
  both ways: it must PASS the shipped 31097 set and FAIL on known-bad states from 31097's history (Task C writes those).
  It must state what it cannot pass: item 4 "looked at whole" and item 9 "README true" need judgement; it produces
  evidence and flags only.
- (c) The launcher refuses a bulk run of this kind without the done-list and a passing ONE-SAMPLE record (done-list item 0).
- (d) Risk tiers set mechanically by the files a change touches (README-only wording: edit + diff check; model writer or
  gate code: full check), and every check brief makes the checker read the source and the built file BEFORE the
  builder's own write-up.
- (e) Guard and policy files locked so a session cannot edit them (an OS ACL or managed settings needing Ian's admin);
  only Ian changes them.
- Then re-decide steps 3 and 4 (rebuilding the reply-wording guards), which may shrink to trimming noisy guards.

## Claims to verify (each one: TRUE / FALSE / PARTLY / CANNOT TELL, with file:line)

- C1. There is no lane launcher file to put gate (c) in. `launch_lane.py`, named in the done-list draft line 8, does not
  exist; lanes were started by hand-written `claude -p` commands. (Author's search: filenames `launch_lane*` in Framework
  and Operations/tools; content search for "launch_lane|def launch|lane launcher" in Framework only. It found
  `Framework/tools/kor_handover.py:300 def launch(...)` for handovers.) Where could (c) actually be enforced?
  Consider `Framework/tools/plan_review_guard.py` (a PreToolUse hook on Agent|Bash|PowerShell|SendMessage) and
  `Framework/tools/kor_task.py`.
- C2. Codex's 16 Nov rebuild review found 2 FATAL: "no forced implementation" and "pre-check described not wired".
  (The author took this from the commit title of Operations 870d3729 and has NOT read the response.) Does this plan
  repeat either fault?
- C3. 31097 round 3: the parking model that was analysed was not the file shipped (KOR.Drafter commit 50db71ad title,
  "analysed P3 copy != shipped FDB").
- C4. 31097 round 3: 296 sf of Building 3 L07 slab was unmeshed by SAFE while equilibrium passed (50db71ad; d7d43881).
- C5. The look and absence guards are log-only since Operations 1e66e0f0; KOR_GUARD_BLOCK=1 restores blocking.
- C6. "Most current Framework guards police the reply's wording, not what was built" (record #133993, from an outside
  analysis; the author has NOT measured it). Test it against your own CODEX-FRAMEWORK-INVENTORY-1/-2 responses.
- C7. The 31097 graders exist only as per-round copies under
  `KOR.Drafter\work\31097-01-heather-lands\safe-2026-11-17\` (fix2/scripts, check3..check8/scripts), e.g.
  results_2b.py -> results_2b4.py copied into check4..check8; there is no single maintained version.
- C8. The done-list draft covers every fault the 31097 check rounds found (the author believes it does NOT; see Task D).

## Inputs: read these; search, do not read whole, where marked

Plan and history:
- `C:\VIsual Studio Projects\Framework\docs\HANDOVER.md` lines 1-35 only (STATE NOW and the REORDER block).
- `C:\VIsual Studio Projects\Framework\docs\DONE-LIST-ANALYSIS-MODEL.md` (38 lines).
- `C:\VIsual Studio Projects\Operations\docs\codex\CODEX-FLAWS-AND-FRAMEWORK-REBUILD-RESPONSE.md` (16 KB).
- `C:\VIsual Studio Projects\Operations\docs\codex\CODEX-FRAMEWORK-INVENTORY-1-RESPONSE.md` and `-2-RESPONSE.md`
  (36 KB, 59 KB): search them for what each guard reads.

31097 (the job the plan is built from), in `C:\VIsual Studio Projects\KOR.Drafter\work\31097-01-heather-lands\safe-2026-11-17\`:
- `CHECK2.md`, `CHECK-ROUND3.md` .. `CHECK-ROUND7.md`, `CHECK-ALL.md` (8-73 KB each): SEARCH for findings and verdicts.
- `FIX-ROUND8.md` (36 KB): the shipped state.
- Scripts: `check8/scripts/*.py` (21 files) and `fix2/scripts/stability.py`, `equilibrium.py`, `results_2b.py`; about
  176 KB together. Read what you need to judge (b); you need not read all of them.
- `git -C "C:\VIsual Studio Projects\KOR.Drafter" log --format="%h %s" -- work/31097-01-heather-lands` for the round
  commits (read-only git is allowed; checking out or resetting is not).
- Independent audits already run: `C:\VIsual Studio Projects\KOR.Drafter\docs\codex\CODEX-31097-SAFE-AUDIT-ALL-R3..R8-RESPONSE.md`.

Launch and gate code (for C1 and step c):
- `C:\VIsual Studio Projects\Framework\tools\plan_review_guard.py`, `kor_task.py` (361 lines), `kor_handover.py`
  around line 300.

Lessons recorded during 31097 (record ids in KorStandards brain.Memory; the text here is the author's paraphrase, so
check each against the round files): #133955 an analysis deliverable is runs + sane results; #133968 0 negative
eigenvalues and equilibrium passed while a column pad meshed apart from the slab; #133970 an equilibrium residual can hide
unmeshed slab; #133971 an export diff by row count misses renames and value changes; #133977 a mean width is not a max
width; #133980 a grader must know every run mode; #133982 SAFE floor CSV rows are continuations; #133989 delete an
unmeasured claim class, don't reword it.

## The tasks

A. The plan. For each step (a)-(e): (1) the exact file or mechanism that would ENFORCE it, or "none named"; (2) how a
   session under deadline would get a deliverable past it without being checked; (3) what it would cost Ian (his time,
   his decisions). Then: is the ORDER right? Is anything missing that would have stopped 31097's failures earlier?

B. The claims C1-C8. A verdict and file:line each.

C. MUST-FAIL CASES for step (b), written by you so the builder cannot tune them. From 31097's history, list every
   known-bad state you can find. For each: the KOR.Drafter commit (or file) holding it; the model(s); the fault in one
   line; which done-list item must FAIL on it; and the evidence (file:line in a CHECK or Codex audit). Also list any
   fault the check COULD NOT catch from files alone (needs SAFE running, or a person looking). Aim for completeness over
   polish; say how you searched so a missing class can be found.

D. The done-list draft. Every 31097 fault no item would catch; items that are untestable as written; wording that lets
   a grader pass something wrong ("explained", "agreed", "looked at"). Propose exact replacement text.

## Output format

1. A one-paragraph verdict: would this plan, built as described, have stopped 31097's failures? Yes / partly / no, and why.
2. Task A table, Task B table, Task C table, Task D list.
3. FATAL / MAJOR / MINOR for each problem found; FATAL = a deliverable can still pass unchecked.
4. What you could not judge from these inputs, and what you would need.

## Limits

- Read-only. Facts from files you read, each with file:line. A search that found nothing: name the words and the place.
- Budget: about 45 minutes. If going past it, stop and write what you have, marked INCOMPLETE with what is left.
