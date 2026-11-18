# Framework inventory, increment 2 — evidence and verdicts

16 November 2026. Recommendation: **10 FIX, 7 KEEP (4 provisional), 0 CUT**. The 222 previously unjudged samples yield **39 RIGHT, 41 WRONG, 142 UNCLEAR**. The separate 140 earlier judgements record **48 right and 92 wrong**. These are refusal-event counts, not unique production faults or a measured return on cost.

Evidence is restricted to [guard-week-14d.csv](CODEX-FRAMEWORK-INVENTORY-2-work/guard-week-14d.csv), [refusal-sample.csv](CODEX-FRAMEWORK-INVENTORY-2-work/refusal-sample.csv), [mistakes.csv](CODEX-FRAMEWORK-INVENTORY-2-work/mistakes.csv), [Increment 1](CODEX-FRAMEWORK-INVENTORY-1-RESPONSE.md), and permitted first-40-line guard headers for rule meaning. The brief already records its prior-art search; Increment 1 supplies the existing hook inventory. No DB queries, product-code inspection, guard execution, implementation changes, builds, tests, or commits were needed for this report.

## How to read the evidence

RIGHT means the visible action violates the stated guard constraint; that includes a deliberately planted violation in a test. WRONG means the visible match catches a benign action or misreads the claim. UNCLEAR means the exported text cannot establish either. A guard's allegation is not independent proof of its own correctness: a filename does not prove a missing search, and a Check ID does not disclose its condition. Where evidence is insufficient, the conclusion is **not decidable from the export**.

The 14-day table contains 3,755 refusals across 17 guards. The random sample is stratified by guard, up to 15 previously unjudged events per guard; the already-judged stratum is separately selected. Do not pool them into a population-rate estimate or extrapolate their counts to 3,755. Repeated attempts and test events remain separate because the export counts separate IDs. In particular, 14 of 15 pre_push_review samples name zz-test-review repositories; 13 of its 14 RIGHTs are those test events. Four kor-drafter-python RIGHTs name new_guard_probe.py. Other synthetic events may be unmarked.

Every wrong-rate cell gives its numerator and denominator. “Known wrong/sample” leaves UNCLEAR in the denominator and is only the observed lower bound. “Wrong/decidable” excludes UNCLEAR and is conditional on the excerpt being sufficient, not an unbiased population estimate. Earlier judgements are retained as supplied, not silently re-adjudicated. Their counts reconcile with guard-week-14d.csv for each of the 17 guards.

Mistakes span 27 October–11 November; refusal totals span the exported 14-day window in November. Current headers describe changes during that window. The exports do not establish the active guard version, wiring or exemptions at each mistake, nor per-event delay, retry count, downstream correction or monetary cost. A current failure rate, causal prevention rate, or cost-benefit threshold is **not decidable from the export**. FIX identifies an evidenced rule problem, not proof it still exists in today's code. No CUT recommendation is supported strongly enough to remove a guard on these data.

## Per-guard counts and wrong-refusal rates

R/W/U are my judgements of Picked=random. Earlier R/W are the supplied judgements. Zero decidable cases produce no rate.

| Guard | Refusals, 14 days | Sample R / W / U | Known wrong / sample | Wrong / decidable | Earlier R / W; wrong rate |
|---|---:|---:|---|---|---|
| bash | 1836 | 0 / 2 / 13 | 2 of 15 | 2 of 2 | 9 / 25; 25 of 34 |
| absence | 530 | 1 / 12 / 2 | 12 of 15 | 12 of 13 | 14 / 22; 22 of 36 |
| look | 435 | 0 / 8 / 7 | 8 of 15 | 8 of 8 | 4 / 37; 37 of 41 |
| kor-repeat | 203 | 0 / 0 / 15 | 0 of 15 | not decidable (0 cases) | 4 / 0; 0 of 4 |
| pre_push_review | 185 | 14 / 0 / 1 | 0 of 15 | 0 of 14 | 0 / 0; not decidable (0 cases) |
| kor-context-read | 169 | 0 / 0 / 15 | 0 of 15 | not decidable (0 cases) | 7 / 5; 5 of 12 |
| kor-repeat-reason | 107 | 0 / 0 / 15 | 0 of 15 | not decidable (0 cases) | 0 / 0; not decidable (0 cases) |
| kor-enforce | 96 | 0 / 0 / 15 | 0 of 15 | not decidable (0 cases) | 0 / 0; not decidable (0 cases) |
| sql_schema | 92 | 0 / 1 / 14 | 1 of 15 | 1 of 1 | 10 / 2; 2 of 12 |
| run-sample | 26 | 2 / 4 / 9 | 4 of 15 | 4 of 6 | 0 / 0; not decidable (0 cases) |
| prior_art | 18 | 0 / 0 / 15 | 0 of 15 | not decidable (0 cases) | 0 / 0; not decidable (0 cases) |
| board | 16 | 7 / 7 / 1 | 7 of 15 | 7 of 14 | 0 / 1; 1 of 1 |
| kor-drafter-python | 13 | 13 / 0 / 0 | 0 of 13 | 0 of 13 | 0 / 0; not decidable (0 cases) |
| kor-context | 11 | 2 / 4 / 5 | 4 of 11 | 4 of 6 | 0 / 0; not decidable (0 cases) |
| plan_review | 11 | 0 / 0 / 11 | 0 of 11 | not decidable (0 cases) | 0 / 0; not decidable (0 cases) |
| kor-stall | 5 | 0 / 2 / 3 | 2 of 5 | 2 of 2 | 0 / 0; not decidable (0 cases) |
| api | 2 | 0 / 1 / 1 | 1 of 2 | 1 of 1 | 0 / 0; not decidable (0 cases) |

## Mistake classes and recorded escapes

A mistake is mapped once, to the most specific supported framework-guard class, using What, ClassCode and Mitigation together. A shared ClassCode does not force identical mappings: SQL constraint assumptions, mistaken CLI parameters, and a Python aliasing bug need different treatment. Secondary associations are explained where material; they are not counted twice.

The scope is the 17 refusal guards plus identifiable framework guards in Increment 1, and the existing push-ownership hook explicitly named by mistake 79. Product-specific regression tests, runtime preconditions, and nonblocking reminders are not treated as general framework guards. “NONE” means **no applicable guard coverage established in these inputs**, not that no software check exists. Opaque brain.Check rules and incomplete executable descriptions make exhaustive coverage **not decidable from the export**. For example, mistake 139 proposes an enrollment check, but Increment 1 does not define enroll-check's implementation sufficiently to equate the two; mistake 85 has a document reminder without coverage of the idle-work fault.

There are **42 mapped mistake records and 73 with no established mapping**. Of the 42, **40 record a fault in the guard's class**, **1 explicitly records a guard catch (mistake 30)**, and **1 has ambiguous catch attribution (56)**. The 40 are the recorded class escapes below, including faults found by people or other checks before shipment. They are not 40 proven failures of a guard that was installed and applicable at the time. The caught and ambiguous rows are excluded from that count. Guard catches for evaluating benefit remain the RIGHT refusals above; neither mistake 30 nor any other mistake adds a catch to those totals.

| Guard | Class escapes | Explicit guard catch in ledger | Catch attribution unclear | Escape mistake IDs |
|---|---:|---:|---:|---|
| bash | 4 | 0 | 0 | 28, 41, 68, 120 |
| absence | 8 | 1 | 0 | 6, 12, 18, 22, 35, 37, 76, 86 |
| look | 7 | 0 | 1 | 3, 42, 70, 84, 108, 109, 136 |
| kor-repeat | 0 | 0 | 0 | — |
| pre_push_review | 1 | 0 | 0 | 131 |
| kor-context-read | 0 | 0 | 0 | — |
| kor-repeat-reason | 0 | 0 | 0 | — |
| kor-enforce | 1 | 0 | 0 | 135 |
| sql_schema | 1 | 0 | 0 | 66 |
| run-sample | 0 | 0 | 0 | — |
| prior_art | 5 | 0 | 0 | 7, 44, 115, 141, 145 |
| board | 0 | 0 | 0 | — |
| kor-drafter-python | 0 | 0 | 0 | — |
| kor-context | 0 | 0 | 0 | — |
| plan_review | 0 | 0 | 0 | — |
| kor-stall | 0 | 0 | 0 | — |
| api | 0 | 0 | 0 | — |
| commit_scope | 2 | 0 | 0 | 4, 143 |
| instrument | 6 | 0 | 0 | 38, 39, 40, 73, 78, 113 |
| board_time | 3 | 0 | 0 | 54, 63, 142 |
| push_ownership | 2 | 0 | 0 | 74, 79 |

Additional guard identities: commit_scope = Framework/tools/commit_scope_guard.py; instrument = Operations/tools/claude_instrument_guard.py; board_time = Framework/tools/board_time_guard.py (Increment 1). push_ownership = Operations/tools/git_hooks/pre_push.py, named in mistake 79; this is distinct from pre_push_review. These four have no refusal counts in the 17-row export, so no invented rate or keep/fix/cut verdict is assigned to them.

Class membership does not imply the matcher could intercept that instance: absence claims in migration headers evade a reply guard; look cannot determine whether a contact sheet exposes an engineering error; sql_schema does not establish understanding of constraints; instrument cannot validate geometry simply by seeing a nonzero result. The individual mapping below makes those limits visible.

## Mistakes without established guard coverage, by ClassCode

The 73 unmapped records include **7 whose CaughtBy names Ian**. The other 66 were attributed to self, peers, checks or an engineer. Across the 115-row ledger, CaughtBy is self 53, peer 29, check 21, Ian 11, engineer 1. Thus an Ian-only list would omit substantial evidence; conversely, a prevented refusal need not appear in the mistake ledger.

| ClassCode | Unmapped mistakes | CaughtBy names Ian | Mistake IDs | Ian IDs |
|---|---:|---:|---|---|
| artefact-rebuilt-from-a-run-dirtied-folder | 2 | 0 | 100, 101 | — |
| claim-without-artifact | 17 | 1 | 2, 15, 17, 24, 31, 32, 33, 34, 36, 43, 51, 53, 69, 75, 77, 117, 132 | 132 |
| escaping-across-a-boundary | 3 | 0 | 5, 8, 138 | — |
| identity-read-off-a-proxy-field | 5 | 0 | 82, 88, 99, 102, 110 | — |
| instruction-without-when | 1 | 0 | 49 | — |
| instrument-not-tested | 0 | 0 | — | — |
| interface-assumed-not-read | 4 | 0 | 29, 52, 87, 106 | — |
| live-input-read-as-fixed-shape | 3 | 0 | 55, 67, 107 | — |
| outcome-recorded-where-no-operator-reads-it | 1 | 0 | 83 | — |
| precondition-not-checked | 7 | 1 | 26, 50, 71, 80, 81, 93, 114 | 71 |
| proof-that-cannot-fail | 5 | 1 | 19, 47, 91, 92, 119 | 119 |
| proposal-without-looking | 1 | 1 | 118 | 118 |
| rehearsal-misses-the-commit-path | 2 | 0 | 14, 27 | — |
| remote-walk-over-share | 0 | 0 | — | — |
| requirement-not-tracked | 4 | 3 | 72, 85, 116, 139 | 72, 85, 116 |
| shared-surface-unchecked | 16 | 0 | 1, 9, 16, 21, 23, 25, 45, 46, 48, 64, 65, 98, 103, 104, 112, 133 | — |
| silent-hang | 2 | 0 | 10, 20 | — |

## Verdicts

Each line cites the sample R/W/U, earlier R/W, and recorded class-escape count. KEEP (provisional) retains the rule because the permitted evidence cannot support a change; it does not certify effectiveness. FIX addresses existing rule behavior only; this report proposes no new guards.

| Guard | Verdict | Counts and rule-specific evidence |
|---|---|---|
| bash | FIX | Sample 0/2/13; earlier 9/25; 4 class escapes. Narrow heredoc escaping, distinguish actual build pipelines from filter strings, and distinguish timeout caps from expected durations; earlier wrong examples 4758, 5719, 6696. Headers describe a 12 Nov escape repair; persistence is not decidable from the export. |
| absence | FIX | Sample 1/12/2; earlier 14/22; 8 class escapes. Match substantive absence claims, preserving scoped evidence, conditions, quotations and stated uncertainty; 4485 is a real unsupported inference, while 5271/5797/1272 are needless refusals. The header describes a 10 Nov repair, so historical counts do not establish current behavior. |
| look | FIX | Sample 0/8/7; earlier 4/37; 7 class escapes. Bind the completion claim to the actual changed artifact and actual inspection; exclude requirements, future work and read-only commands. Earlier 4585/6362/6806 show useful catches; 2741/5031/6670 show misidentified changes. |
| kor-repeat | KEEP | Sample 0/0/15; earlier 4/0; 0 class escapes. Earlier judgements record 4 right refusals, though their notes are empty. The 15 new excerpts omit the earlier work; claimed input changes cannot be independently compared. Effectiveness beyond those judgements is not decidable from the export. |
| pre_push_review | KEEP | Sample 14/0/1; earlier 0/0; 1 class escapes. Enforce independent approval and unresolved CHANGES. Of 14 sample RIGHTs, 13 are explicit test-repository events and one is Operations #355; production faults prevented and unique commits saved are not decidable from the export. |
| kor-context-read | FIX | Sample 0/0/15; earlier 7/5; 0 class escapes. Replace the decision based on compressed image/file bytes with the intended read-cost distinction within this rule. Earlier 5449/5765/6168/6196/6601 document retries with substantially the same picture; the 15 new sizes alone settle nothing. |
| kor-repeat-reason | KEEP (provisional) | Sample 0/0/15; earlier 0/0; 0 class escapes. The export records reasons for changed inputs, but does not establish these were denied actions. Whether this name is a guard refusal or a reason/audit event is not decidable from the export; cutting or rewriting its rule is unsupported. |
| kor-enforce | KEEP (provisional) | Sample 0/0/15; earlier 0/0; 1 class escapes. Mistake 135 identifies a real LocalDB policy class enforced through brain.Check; the sampled Check IDs have no definitions. Which individual Check should change is not decidable from the export. |
| sql_schema | FIX | Sample 0/1/14; earlier 10/2; 1 class escapes. Parse database.schema.table as the correct table and distinguish executed SQL from search strings; earlier 5445/5633 prove lost inspections, and new 6304 is a read-only search. Do not infer that the 9 misparsed new DML cases had correct prior inspection. |
| run-sample | FIX | Sample 2/4/9; earlier 0/0; 0 class escapes. Recognize executed bypass assignments/protected-file changes rather than quoted evidence, JSON test payloads, grep patterns or another script's destination. Preserve actual bypass refusals 5878/5895; edits and reverts without their diffs remain unresolved. |
| prior_art | KEEP (provisional) | Sample 0/0/15; earlier 0/0; 5 class escapes. Five matching mistake records establish the class, including 141 passing after a directory listing and 145 using a truncated filename search. The 15 path-only samples establish neither catches nor excess refusals; current effectiveness is not decidable from the export. |
| board | FIX | Sample 7/7/1; earlier 0/1; 0 class escapes. Respect negation, attributed old state, and completed sub-stages; 467/4533 reverse negation and 1981/5723 mistake partial completion for the entire row. Preserve explicit current-state contradictions such as 6116/5972. |
| kor-drafter-python | KEEP | Sample 13/0/0; earlier 0/0; 0 class escapes. The 13 attempted creations fit the explicit no-new-Python rule, including 4 new_guard_probe.py events. This proves rule compliance, not that each proposed test was useless or that 13 production mistakes were prevented. |
| kor-context | FIX | Sample 2/4/5; earlier 0/0; 0 class escapes. Allow saving/committing the required handover at STOP; 2695/2701/2702/2703 show that work being blocked. Keep the over-budget-work and missing-handover stops; the below-budget threshold is not decidable from the export. |
| plan_review | KEEP (provisional) | Sample 0/0/11; earlier 0/0; 0 class escapes. The rule requires a reviewed helper plan, but 11 empty Matched fields cannot establish either useful catches or excess refusals. No rule-specific effectiveness or cost comparison is decidable from the export. |
| kor-stall | FIX | Sample 0/2/3; earlier 0/0; 0 class escapes. Distinguish repeating the failed operation from diagnosing/recording it or switching input routes; 4211 blocks a finding and 4210 switches from the documented failing -i route to -q. Prior stall state is absent for the other 3. |
| api | FIX | Sample 0/1/1; earlier 0/0; 0 class escapes. Match an API call or a proposed call, not a hostname inside a grep pattern: 1446 only searches text. The content of document refusal 4102 is not exported; its correctness is not decidable from the export. |

## Sampled unjudged refusals — 222 individual judgements

Each reason has fewer than 15 whitespace-separated words. IDs refer to refusal-sample.csv, not mistakes.csv.

| Id | Guard | Judgement | Reason |
|---:|---|---|---|
| 4693 | absence | WRONG | Missing-file/key condition describes code behavior, not an unsupported inventory claim. |
| 5666 | absence | WRONG | Names the path-resolution mechanism; missing file is the explained failure. |
| 6634 | absence | UNCLEAR | Fixture contents and the claimed test gap are outside this excerpt. |
| 6286 | absence | UNCLEAR | Cannot verify the claimed reactions omission from this excerpt. |
| 4485 | absence | RIGHT | Infers no tool store from table names; text admits the search was incomplete. |
| 5234 | absence | WRONG | Quotes a conditional contract and cites the caller contradicting it. |
| 5271 | absence | WRONG | Acknowledges an unfinished test run and unavailable final counts. |
| 2793 | absence | WRONG | Challenges test evidence with a specific replay defect, not an inventory claim. |
| 4715 | absence | WRONG | Explains why an unchecked formula cannot support the report's conclusion. |
| 2790 | absence | WRONG | Discloses the age of its usage reading instead of inventing one. |
| 5797 | absence | WRONG | States the exact lines inspected and the characters checked. |
| 4690 | absence | WRONG | Missing-file/key condition describes code behavior, not an unsupported inventory claim. |
| 1272 | absence | WRONG | Describes a proposed failure condition, not a missing current artifact. |
| 6148 | absence | WRONG | Discloses missing visual access and limits its answer accordingly. |
| 2750 | absence | WRONG | Already states zero matches and the script scope. |
| 1446 | api | WRONG | Searches stored text for an API hostname; does not call it. |
| 4102 | api | UNCLEAR | Only the guard's allegation survives; proposed document contents are absent. |
| 2757 | bash | UNCLEAR | Truncated heredoc prevents checking whether the triggering escape was harmful. |
| 5684 | bash | UNCLEAR | Timeout cap is visible in Rule; actual duration and description are unavailable. |
| 6145 | bash | UNCLEAR | Timeout cap is visible in Rule; actual duration and description are unavailable. |
| 5039 | bash | UNCLEAR | Timeout cap is visible in Rule; actual duration and description are unavailable. |
| 5213 | bash | UNCLEAR | Timeout cap is visible in Rule; actual duration and description are unavailable. |
| 6503 | bash | UNCLEAR | Truncated heredoc prevents checking whether the triggering escape was harmful. |
| 6654 | bash | UNCLEAR | Timeout cap is visible in Rule; actual duration and description are unavailable. |
| 4614 | bash | UNCLEAR | Timeout cap is visible in Rule; actual duration and description are unavailable. |
| 2783 | bash | UNCLEAR | Timeout cap is visible in Rule; actual duration and description are unavailable. |
| 2691 | bash | WRONG | Test output goes to a file; saved exit code survives the separate grep pipeline. |
| 5773 | bash | UNCLEAR | Timeout cap is visible in Rule; actual duration and description are unavailable. |
| 5318 | bash | UNCLEAR | Timeout cap is visible in Rule; actual duration and description are unavailable. |
| 4786 | bash | UNCLEAR | Truncated heredoc prevents checking whether the triggering escape was harmful. |
| 2301 | bash | UNCLEAR | Truncated heredoc prevents checking whether the triggering escape was harmful. |
| 6582 | bash | WRONG | Quoted Python heredoc intentionally inserts a newline into source. |
| 6116 | board | RIGHT | Explicit current row state contradicts the exported board state. |
| 4824 | board | WRONG | Reports what an old handover says, not the current board state. |
| 5966 | board | RIGHT | Explicit current row state contradicts the exported board state. |
| 1981 | board | WRONG | Claims only the first stage finished, not the entire row. |
| 4648 | board | RIGHT | Explicit current row state contradicts the exported board state. |
| 5516 | board | RIGHT | Explicit current row state contradicts the exported board state. |
| 467 | board | WRONG | Negation explicitly denies completion; guard reads it as completion. |
| 4533 | board | WRONG | Negation explicitly denies completion; guard reads it as completion. |
| 5972 | board | RIGHT | Explicit current row state contradicts the exported board state. |
| 6005 | board | RIGHT | Explicit current row state contradicts the exported board state. |
| 6260 | board | WRONG | Offline portion finished; wording leaves the remaining run outstanding. |
| 5723 | board | WRONG | Offline portion finished; wording leaves the remaining run outstanding. |
| 6559 | board | WRONG | Attributes a helper's report and explicitly leaves approval and installation pending. |
| 6009 | board | RIGHT | Explicit current row state contradicts the exported board state. |
| 378 | board | UNCLEAR | Range and K-label mapping are missing; cannot assign the claimed states. |
| 6899 | kor-context | RIGHT | Build would continue after the exported context budget was exceeded. |
| 2806 | kor-context | UNCLEAR | STOP threshold and recovery allowances are not defined in the export. |
| 2809 | kor-context | UNCLEAR | STOP threshold and recovery allowances are not defined in the export. |
| 2695 | kor-context | WRONG | Blocks committing the handover needed to finish at the context limit. |
| 2700 | kor-context | UNCLEAR | STOP threshold and recovery allowances are not defined in the export. |
| 3597 | kor-context | UNCLEAR | STOP threshold and recovery allowances are not defined in the export. |
| 2701 | kor-context | WRONG | Blocks committing the handover needed to finish at the context limit. |
| 2703 | kor-context | WRONG | Blocks committing the handover needed to finish at the context limit. |
| 2696 | kor-context | UNCLEAR | STOP threshold and recovery allowances are not defined in the export. |
| 3566 | kor-context | RIGHT | Export explicitly records stopping before the required handover was written. |
| 2702 | kor-context | WRONG | Blocks committing the handover needed to finish at the context limit. |
| 6647 | kor-context-read | UNCLEAR | Byte size alone cannot establish excessive context cost or whether a retry sufficed. |
| 5549 | kor-context-read | UNCLEAR | Byte size alone cannot establish excessive context cost or whether a retry sufficed. |
| 6162 | kor-context-read | UNCLEAR | Byte size alone cannot establish excessive context cost or whether a retry sufficed. |
| 6397 | kor-context-read | UNCLEAR | Byte size alone cannot establish excessive context cost or whether a retry sufficed. |
| 5619 | kor-context-read | UNCLEAR | Byte size alone cannot establish excessive context cost or whether a retry sufficed. |
| 5717 | kor-context-read | UNCLEAR | Byte size alone cannot establish excessive context cost or whether a retry sufficed. |
| 6688 | kor-context-read | UNCLEAR | Byte size alone cannot establish excessive context cost or whether a retry sufficed. |
| 6789 | kor-context-read | UNCLEAR | Byte size alone cannot establish excessive context cost or whether a retry sufficed. |
| 6831 | kor-context-read | UNCLEAR | Byte size alone cannot establish excessive context cost or whether a retry sufficed. |
| 5709 | kor-context-read | UNCLEAR | Byte size alone cannot establish excessive context cost or whether a retry sufficed. |
| 6474 | kor-context-read | UNCLEAR | Byte size alone cannot establish excessive context cost or whether a retry sufficed. |
| 6151 | kor-context-read | UNCLEAR | Byte size alone cannot establish excessive context cost or whether a retry sufficed. |
| 7615 | kor-context-read | UNCLEAR | Byte size alone cannot establish excessive context cost or whether a retry sufficed. |
| 6183 | kor-context-read | UNCLEAR | Byte size alone cannot establish excessive context cost or whether a retry sufficed. |
| 4375 | kor-context-read | UNCLEAR | Byte size alone cannot establish excessive context cost or whether a retry sufficed. |
| 4480 | kor-drafter-python | RIGHT | Creates Python under the tools path expressly prohibited by the exported rule. |
| 5701 | kor-drafter-python | RIGHT | Creates Python under the tools path expressly prohibited by the exported rule. |
| 5847 | kor-drafter-python | RIGHT | Creates Python under the tools path expressly prohibited by the exported rule. |
| 6203 | kor-drafter-python | RIGHT | Creates Python under the tools path expressly prohibited by the exported rule. |
| 5661 | kor-drafter-python | RIGHT | Creates Python under the tools path expressly prohibited by the exported rule. |
| 5663 | kor-drafter-python | RIGHT | Creates Python under the tools path expressly prohibited by the exported rule. |
| 5537 | kor-drafter-python | RIGHT | Creates Python under the tools path expressly prohibited by the exported rule. |
| 6064 | kor-drafter-python | RIGHT | Creates Python under the tools path expressly prohibited by the exported rule. |
| 4368 | kor-drafter-python | RIGHT | Creates Python under the tools path expressly prohibited by the exported rule. |
| 4479 | kor-drafter-python | RIGHT | Creates Python under the tools path expressly prohibited by the exported rule. |
| 4367 | kor-drafter-python | RIGHT | Creates Python under the tools path expressly prohibited by the exported rule. |
| 5984 | kor-drafter-python | RIGHT | Creates Python under the tools path expressly prohibited by the exported rule. |
| 5791 | kor-drafter-python | RIGHT | Creates Python under the tools path expressly prohibited by the exported rule. |
| 727 | kor-enforce | UNCLEAR | Check ID is given, but its condition and exceptions are not exported. |
| 4597 | kor-enforce | UNCLEAR | Check ID is given, but its condition and exceptions are not exported. |
| 7032 | kor-enforce | UNCLEAR | Check ID is given, but its condition and exceptions are not exported. |
| 6293 | kor-enforce | UNCLEAR | Check ID is given, but its condition and exceptions are not exported. |
| 1312 | kor-enforce | UNCLEAR | Check ID is given, but its condition and exceptions are not exported. |
| 6796 | kor-enforce | UNCLEAR | Check ID is given, but its condition and exceptions are not exported. |
| 1325 | kor-enforce | UNCLEAR | Check ID is given, but its condition and exceptions are not exported. |
| 6440 | kor-enforce | UNCLEAR | Check ID is given, but its condition and exceptions are not exported. |
| 1332 | kor-enforce | UNCLEAR | Check ID is given, but its condition and exceptions are not exported. |
| 1342 | kor-enforce | UNCLEAR | Check ID is given, but its condition and exceptions are not exported. |
| 5527 | kor-enforce | UNCLEAR | Check ID is given, but its condition and exceptions are not exported. |
| 6649 | kor-enforce | UNCLEAR | Check ID is given, but its condition and exceptions are not exported. |
| 2110 | kor-enforce | UNCLEAR | Check ID is given, but its condition and exceptions are not exported. |
| 5550 | kor-enforce | UNCLEAR | Check ID is given, but its condition and exceptions are not exported. |
| 5164 | kor-enforce | UNCLEAR | Check ID is given, but its condition and exceptions are not exported. |
| 7480 | kor-repeat | UNCLEAR | Earlier records and inputs are absent; cannot establish whether this duplicates work. |
| 7478 | kor-repeat | UNCLEAR | Earlier records and inputs are absent; cannot establish whether this duplicates work. |
| 7431 | kor-repeat | UNCLEAR | Earlier records and inputs are absent; cannot establish whether this duplicates work. |
| 6577 | kor-repeat | UNCLEAR | Earlier records and inputs are absent; cannot establish whether this duplicates work. |
| 7506 | kor-repeat | UNCLEAR | Earlier records and inputs are absent; cannot establish whether this duplicates work. |
| 7579 | kor-repeat | UNCLEAR | Earlier records and inputs are absent; cannot establish whether this duplicates work. |
| 7553 | kor-repeat | UNCLEAR | Earlier records and inputs are absent; cannot establish whether this duplicates work. |
| 7389 | kor-repeat | UNCLEAR | Earlier records and inputs are absent; cannot establish whether this duplicates work. |
| 7589 | kor-repeat | UNCLEAR | Earlier records and inputs are absent; cannot establish whether this duplicates work. |
| 7362 | kor-repeat | UNCLEAR | Earlier records and inputs are absent; cannot establish whether this duplicates work. |
| 7215 | kor-repeat | UNCLEAR | Earlier records and inputs are absent; cannot establish whether this duplicates work. |
| 7364 | kor-repeat | UNCLEAR | Earlier records and inputs are absent; cannot establish whether this duplicates work. |
| 6993 | kor-repeat | UNCLEAR | Earlier records and inputs are absent; cannot establish whether this duplicates work. |
| 7430 | kor-repeat | UNCLEAR | Earlier records and inputs are absent; cannot establish whether this duplicates work. |
| 6965 | kor-repeat | UNCLEAR | Earlier records and inputs are absent; cannot establish whether this duplicates work. |
| 7114 | kor-repeat-reason | UNCLEAR | Change reason is recorded; prior inputs and whether execution was denied are absent. |
| 6928 | kor-repeat-reason | UNCLEAR | Change reason is recorded; prior inputs and whether execution was denied are absent. |
| 6730 | kor-repeat-reason | UNCLEAR | Change reason is recorded; prior inputs and whether execution was denied are absent. |
| 6775 | kor-repeat-reason | UNCLEAR | Change reason is recorded; prior inputs and whether execution was denied are absent. |
| 7308 | kor-repeat-reason | UNCLEAR | Change reason is recorded; prior inputs and whether execution was denied are absent. |
| 6726 | kor-repeat-reason | UNCLEAR | Change reason is recorded; prior inputs and whether execution was denied are absent. |
| 6606 | kor-repeat-reason | UNCLEAR | Change reason is recorded; prior inputs and whether execution was denied are absent. |
| 7060 | kor-repeat-reason | UNCLEAR | Change reason is recorded; prior inputs and whether execution was denied are absent. |
| 6610 | kor-repeat-reason | UNCLEAR | Change reason is recorded; prior inputs and whether execution was denied are absent. |
| 7495 | kor-repeat-reason | UNCLEAR | Change reason is recorded; prior inputs and whether execution was denied are absent. |
| 7084 | kor-repeat-reason | UNCLEAR | Change reason is recorded; prior inputs and whether execution was denied are absent. |
| 7118 | kor-repeat-reason | UNCLEAR | Change reason is recorded; prior inputs and whether execution was denied are absent. |
| 7280 | kor-repeat-reason | UNCLEAR | Change reason is recorded; prior inputs and whether execution was denied are absent. |
| 6867 | kor-repeat-reason | UNCLEAR | Change reason is recorded; prior inputs and whether execution was denied are absent. |
| 7475 | kor-repeat-reason | UNCLEAR | Change reason is recorded; prior inputs and whether execution was denied are absent. |
| 4602 | kor-stall | UNCLEAR | Prior failures, stall state, and recovery criteria are absent. |
| 4932 | kor-stall | UNCLEAR | Prior failures, stall state, and recovery criteria are absent. |
| 4211 | kor-stall | WRONG | Blocks recording the diagnosed failure, a recovery action rather than another failing attempt. |
| 4210 | kor-stall | WRONG | Uses -q instead of the documented failing -i file-input route. |
| 6497 | kor-stall | UNCLEAR | Prior failures, stall state, and recovery criteria are absent. |
| 4787 | look | WRONG | Reports limited compilation progress and explicitly discloses the remaining compiler error. |
| 5107 | look | WRONG | Attributes one lane's completed portion and explicitly leaves the next integration pending. |
| 7212 | look | UNCLEAR | Completion-related text is visible; preceding changes and inspection history are absent. |
| 4982 | look | UNCLEAR | Completion-related text is visible; preceding changes and inspection history are absent. |
| 7434 | look | WRONG | Discusses requirements and desired behavior, not a completed deliverable. |
| 5018 | look | UNCLEAR | Completion-related text is visible; preceding changes and inspection history are absent. |
| 370 | look | WRONG | Discusses requirements and desired behavior, not a completed deliverable. |
| 4857 | look | WRONG | Explicitly discloses missing full-suite and rendered-model checks. |
| 6982 | look | WRONG | Describes draft output and forwards outstanding issues; no final acceptance claim. |
| 5127 | look | WRONG | Reports a dry run and explicitly says the Revit run remains in progress. |
| 5126 | look | WRONG | Says tests, diff review, and commit remain future work. |
| 2800 | look | UNCLEAR | Completion-related text is visible; preceding changes and inspection history are absent. |
| 5678 | look | UNCLEAR | Completion-related text is visible; preceding changes and inspection history are absent. |
| 4545 | look | UNCLEAR | Completion-related text is visible; preceding changes and inspection history are absent. |
| 4484 | look | UNCLEAR | Completion-related text is visible; preceding changes and inspection history are absent. |
| 7576 | plan_review | UNCLEAR | Matched is empty; neither action nor review status can be assessed. |
| 7582 | plan_review | UNCLEAR | Matched is empty; neither action nor review status can be assessed. |
| 7481 | plan_review | UNCLEAR | Matched is empty; neither action nor review status can be assessed. |
| 7577 | plan_review | UNCLEAR | Matched is empty; neither action nor review status can be assessed. |
| 7555 | plan_review | UNCLEAR | Matched is empty; neither action nor review status can be assessed. |
| 7483 | plan_review | UNCLEAR | Matched is empty; neither action nor review status can be assessed. |
| 7465 | plan_review | UNCLEAR | Matched is empty; neither action nor review status can be assessed. |
| 7466 | plan_review | UNCLEAR | Matched is empty; neither action nor review status can be assessed. |
| 7575 | plan_review | UNCLEAR | Matched is empty; neither action nor review status can be assessed. |
| 7452 | plan_review | UNCLEAR | Matched is empty; neither action nor review status can be assessed. |
| 7595 | plan_review | UNCLEAR | Matched is empty; neither action nor review status can be assessed. |
| 17 | pre_push_review | RIGHT | Test repository demonstrates missing, adverse, or non-independent approval being refused. |
| 5412 | pre_push_review | RIGHT | Test repository demonstrates missing, adverse, or non-independent approval being refused. |
| 5411 | pre_push_review | RIGHT | Test repository demonstrates missing, adverse, or non-independent approval being refused. |
| 5415 | pre_push_review | RIGHT | Test repository demonstrates missing, adverse, or non-independent approval being refused. |
| 3631 | pre_push_review | RIGHT | Test repository demonstrates missing, adverse, or non-independent approval being refused. |
| 5956 | pre_push_review | RIGHT | Test repository demonstrates missing, adverse, or non-independent approval being refused. |
| 3638 | pre_push_review | RIGHT | Test repository demonstrates missing, adverse, or non-independent approval being refused. |
| 3612 | pre_push_review | RIGHT | Test repository demonstrates missing, adverse, or non-independent approval being refused. |
| 5964 | pre_push_review | RIGHT | Test repository demonstrates missing, adverse, or non-independent approval being refused. |
| 3627 | pre_push_review | RIGHT | Test repository demonstrates missing, adverse, or non-independent approval being refused. |
| 355 | pre_push_review | RIGHT | Push has an unresolved CHANGES review; approval prerequisite is unmet. |
| 3624 | pre_push_review | UNCLEAR | One remote SHA is shown; expected SHA and comparison are absent. |
| 5962 | pre_push_review | RIGHT | Test repository demonstrates missing, adverse, or non-independent approval being refused. |
| 3606 | pre_push_review | RIGHT | Test repository demonstrates missing, adverse, or non-independent approval being refused. |
| 6046 | pre_push_review | RIGHT | Test repository demonstrates missing, adverse, or non-independent approval being refused. |
| 4213 | prior_art | UNCLEAR | Path alone cannot establish whether a sufficient search or sibling read occurred. |
| 5010 | prior_art | UNCLEAR | Path alone cannot establish whether a sufficient search or sibling read occurred. |
| 6922 | prior_art | UNCLEAR | Path alone cannot establish whether a sufficient search or sibling read occurred. |
| 4965 | prior_art | UNCLEAR | Path alone cannot establish whether a sufficient search or sibling read occurred. |
| 7052 | prior_art | UNCLEAR | Path alone cannot establish whether a sufficient search or sibling read occurred. |
| 6926 | prior_art | UNCLEAR | Path alone cannot establish whether a sufficient search or sibling read occurred. |
| 4101 | prior_art | UNCLEAR | Path alone cannot establish whether a sufficient search or sibling read occurred. |
| 4103 | prior_art | UNCLEAR | Path alone cannot establish whether a sufficient search or sibling read occurred. |
| 7540 | prior_art | UNCLEAR | Path alone cannot establish whether a sufficient search or sibling read occurred. |
| 4612 | prior_art | UNCLEAR | Path alone cannot establish whether a sufficient search or sibling read occurred. |
| 5576 | prior_art | UNCLEAR | Path alone cannot establish whether a sufficient search or sibling read occurred. |
| 7537 | prior_art | UNCLEAR | Path alone cannot establish whether a sufficient search or sibling read occurred. |
| 7055 | prior_art | UNCLEAR | Path alone cannot establish whether a sufficient search or sibling read occurred. |
| 7624 | prior_art | UNCLEAR | Path alone cannot establish whether a sufficient search or sibling read occurred. |
| 6924 | prior_art | UNCLEAR | Path alone cannot establish whether a sufficient search or sibling read occurred. |
| 7566 | run-sample | UNCLEAR | Revert/stash targets the protected file; whether it removes protection is unknown. |
| 7241 | run-sample | UNCLEAR | Edit contents and authorization are absent; block-change allegation is insufficient. |
| 5899 | run-sample | WRONG | Bypass assignment is quoted commit-message evidence, not an executed assignment. |
| 7011 | run-sample | UNCLEAR | Edit contents and authorization are absent; block-change allegation is insufficient. |
| 7168 | run-sample | UNCLEAR | Mutation-test setup is visible, but copied content and destination are truncated. |
| 7004 | run-sample | UNCLEAR | Edit contents and authorization are absent; block-change allegation is insufficient. |
| 5878 | run-sample | RIGHT | Sets a bypass variable before running the protected runner. |
| 7233 | run-sample | WRONG | Read-only grep searches the approval phrase; does not write it. |
| 5895 | run-sample | RIGHT | Executes the prohibited bypass assignment; command appears to be a guard probe. |
| 5914 | run-sample | WRONG | Feeds quoted test payloads to guards; does not set the bypass variable. |
| 7135 | run-sample | UNCLEAR | Revert/stash targets the protected file; whether it removes protection is unknown. |
| 7223 | run-sample | UNCLEAR | Revert/stash targets the protected file; whether it removes protection is unknown. |
| 7003 | run-sample | UNCLEAR | Edit contents and authorization are absent; block-change allegation is insufficient. |
| 6811 | run-sample | WRONG | Writes another PowerShell script, not the protected virtual_drafter.py file. |
| 7221 | run-sample | UNCLEAR | Revert/stash targets the protected file; whether it removes protection is unknown. |
| 6055 | sql_schema | UNCLEAR | Three-part table is misparsed; actual prior schema inspection is unavailable. |
| 5502 | sql_schema | UNCLEAR | Three-part table is misparsed; actual prior schema inspection is unavailable. |
| 6033 | sql_schema | UNCLEAR | Three-part table is misparsed; actual prior schema inspection is unavailable. |
| 5193 | sql_schema | UNCLEAR | DML is visible, but earlier schema inspection cannot be checked. |
| 5535 | sql_schema | UNCLEAR | Commit-message prefix is truncated before the alleged SQL; cannot assess the match. |
| 5809 | sql_schema | UNCLEAR | Three-part table is misparsed; actual prior schema inspection is unavailable. |
| 6879 | sql_schema | UNCLEAR | Three-part table is misparsed; actual prior schema inspection is unavailable. |
| 5402 | sql_schema | UNCLEAR | Three-part table is misparsed; actual prior schema inspection is unavailable. |
| 5738 | sql_schema | UNCLEAR | Visible SQL is a Python string; remaining script and any execution are truncated. |
| 6164 | sql_schema | UNCLEAR | Three-part table is misparsed; actual prior schema inspection is unavailable. |
| 5437 | sql_schema | UNCLEAR | Three-part table is misparsed; actual prior schema inspection is unavailable. |
| 6304 | sql_schema | WRONG | Searches source for UPDATE text; executes no SQL write. |
| 5238 | sql_schema | UNCLEAR | Three-part table is misparsed; actual prior schema inspection is unavailable. |
| 2720 | sql_schema | UNCLEAR | DML is visible, but earlier schema inspection cannot be checked. |
| 4955 | sql_schema | UNCLEAR | DML is visible, but earlier schema inspection cannot be checked. |

## Individual mistake mapping — 115 records

“Escape” means a recorded fault in that class, with the historical-coverage qualifications above. “Caught” is the explicitly documented guard catch, and “Uncertain” is unresolved attribution. NONE is an evidence limitation, not a claim that a guard cannot exist. IDs retain gaps from the source export.

| Mistake Id | ClassCode | Guard class | Disposition | CaughtBy | Basis from What / Mitigation |
|---:|---|---|---|---|---|
| 1 | shared-surface-unchecked | NONE | Unmapped | peer | Consumer contract changed; schema inspection would not check loader semantics. |
| 2 | claim-without-artifact | NONE | Unmapped | peer | Wrong ownership claim; board guard checks row state, not owner. |
| 3 | claim-without-artifact | look | Escape | peer | Release/build claim without reading the release artifacts. |
| 4 | shared-surface-unchecked | commit_scope | Escape | self | Unscoped commit includes another session's staged paths. |
| 5 | escaping-across-a-boundary | NONE | Unmapped | self | SQL quote escaping inside an applier; no guarded heredoc shown. |
| 6 | claim-without-artifact | absence | Escape | self | Overbroad failure/absence claim; mitigation explicitly names the absence hook. |
| 7 | shared-surface-unchecked | prior_art | Escape | peer | Reused an existing migration number before checking existing artifacts. |
| 8 | escaping-across-a-boundary | NONE | Unmapped | self | JSON decoding defect in runtime read-back. |
| 9 | shared-surface-unchecked | NONE | Unmapped | peer | Wrong executable selection; no build-completion claim or applicable Check definition. |
| 10 | silent-hang | NONE | Unmapped | self | Runtime MFA/watch hang; exported stall rule does not establish coverage. |
| 12 | claim-without-artifact | absence | Escape | check | Unverified negative permission claim in a migration header; outside reply-only interception. |
| 14 | rehearsal-misses-the-commit-path | NONE | Unmapped | self | Rollback rehearsal does not prove COMMIT/write behavior. |
| 15 | claim-without-artifact | NONE | Unmapped | check | Assumed row identity, not uninspected SQL columns. |
| 16 | shared-surface-unchecked | NONE | Unmapped | peer | Restart coordination with peers; user authorization is not shown missing. |
| 17 | claim-without-artifact | NONE | Unmapped | peer | Unsupported positive ACL-cause claim, not an absence or completion claim. |
| 18 | claim-without-artifact | absence | Escape | self | Claims a tool absent after searching the wrong repository. |
| 19 | proof-that-cannot-fail | NONE | Unmapped | self | Broken hash-comparison proof; no applicable commit-time or zero-report trigger established. |
| 20 | silent-hang | NONE | Unmapped | peer | Runtime MFA/watch hang; exported stall rule does not establish coverage. |
| 21 | shared-surface-unchecked | NONE | Unmapped | peer | Protected-host authorization/control-plane gap; asked guard permits reads and verb-based authorization. |
| 22 | claim-without-artifact | absence | Escape | Ian | Infers no running backup from an API that cannot list that job type. |
| 23 | shared-surface-unchecked | NONE | Unmapped | self | Protected-host authorization/control-plane gap; asked guard permits reads and verb-based authorization. |
| 24 | claim-without-artifact | NONE | Unmapped | self | Misstates a gate's coverage from memory. |
| 25 | shared-surface-unchecked | NONE | Unmapped | self | Document identity uses substring match instead of exact identity. |
| 26 | precondition-not-checked | NONE | Unmapped | self | Missing remote save folder before a runtime write. |
| 27 | rehearsal-misses-the-commit-path | NONE | Unmapped | peer | Rollback rehearsal does not prove COMMIT/write behavior. |
| 28 | remote-walk-over-share | bash | Escape | self | UNC-folder walking class; single-folder listings may pass the documented rule. |
| 29 | interface-assumed-not-read | NONE | Unmapped | self | Assumed CLI parameters; SQL-schema guard is inapplicable. |
| 30 | claim-without-artifact | absence | Caught | check | Mitigation explicitly says the absence hook caught this claim. |
| 31 | claim-without-artifact | NONE | Unmapped | self | Work estimate assumes bridge capabilities; no covered creation or helper dispatch established. |
| 32 | claim-without-artifact | NONE | Unmapped | self | Peer-message time/hash claims; board-time guard is limited to board evidence. |
| 33 | claim-without-artifact | NONE | Unmapped | self | Peer-message time/hash claims; board-time guard is limited to board evidence. |
| 34 | claim-without-artifact | NONE | Unmapped | self | Peer-message time/hash claims; board-time guard is limited to board evidence. |
| 35 | claim-without-artifact | absence | Escape | self | Claims the reader read nothing without checking the source drawing. |
| 36 | claim-without-artifact | NONE | Unmapped | self | Wrong known-failure count recalled from memory. |
| 37 | claim-without-artifact | absence | Escape | self | Unsupported absence claim in a migration header; outside reply-only interception. |
| 38 | instrument-not-tested | instrument | Escape | self | Untested measurement produces false failure/zero or misleading counts. |
| 39 | instrument-not-tested | instrument | Escape | self | Untested measurement produces false failure/zero or misleading counts. |
| 40 | instrument-not-tested | instrument | Escape | self | Untested measurement produces false failure/zero or misleading counts. |
| 41 | escaping-across-a-boundary | bash | Escape | self | Regex/Windows escaping through a shell heredoc breaks the measuring script. |
| 42 | claim-without-artifact | look | Escape | peer | Release-readiness claim without checking the deploy artifacts. |
| 43 | claim-without-artifact | NONE | Unmapped | self | Stale clock used for coordination, not future board evidence. |
| 44 | claim-without-artifact | prior_art | Escape | self | Proposed rebuilding/sizing work already handled by existing scripts. |
| 45 | shared-surface-unchecked | NONE | Unmapped | peer | PDF color change contradicts another requirement; no applicable guard rule. |
| 46 | shared-surface-unchecked | NONE | Unmapped | peer | Protected-host authorization/control-plane gap; asked guard permits reads and verb-based authorization. |
| 47 | proof-that-cannot-fail | NONE | Unmapped | check | Lint accepts a faulty migration; no covered commit or zero-report action established. |
| 48 | shared-surface-unchecked | NONE | Unmapped | self | Open SQL transaction and invalid fixture; schema-read guard would not prevent either. |
| 49 | instruction-without-when | NONE | Unmapped | self | Missing instruction timing; plan-review text length and dispatch route are unknown. |
| 50 | precondition-not-checked | NONE | Unmapped | check | Concurrent build/run locks binary output; no exported rule establishes coordination coverage. |
| 51 | claim-without-artifact | NONE | Unmapped | check | Nonunique column labels used as identity. |
| 52 | interface-assumed-not-read | NONE | Unmapped | self | Assumed CSV interface; no SQL schema involved. |
| 53 | claim-without-artifact | NONE | Unmapped | self | SQL error hidden by tail; bash pipefail rule is specific to dotnet. |
| 54 | claim-without-artifact | board_time | Escape | self | Board evidence uses a future clock time relative to its own read-back. |
| 55 | live-input-read-as-fixed-shape | NONE | Unmapped | check | Counts combine historical runs in a live log. |
| 56 | claim-without-artifact | look | Uncertain | check | Done claim lacked binary inspection; CaughtBy=check may refer to the look guard. |
| 63 | claim-without-artifact | board_time | Escape | self | Board evidence uses a future clock time relative to its own read-back. |
| 64 | shared-surface-unchecked | NONE | Unmapped | check | Geometry regression across untested buildings; no generic hook validates geometry. |
| 65 | shared-surface-unchecked | NONE | Unmapped | check | Geometry regression across untested buildings; no generic hook validates geometry. |
| 66 | interface-assumed-not-read | sql_schema | Escape | check | Schema-assumption class; columns were read but constraint values were not. |
| 67 | live-input-read-as-fixed-shape | NONE | Unmapped | check | Live catalogue order used as a stable index. |
| 68 | silent-hang | bash | Escape | peer | Unbounded SQL subprocesses hang; wrappers/background lifecycle may evade inline timeout detection. |
| 69 | claim-without-artifact | NONE | Unmapped | self | Queued remote action reported as completed; export expressly says look lacks this change kind. |
| 70 | precondition-not-checked | look | Escape | self | Tests use stale binaries after stash; binary verification class, before a report. |
| 71 | precondition-not-checked | NONE | Unmapped | Ian | Overbroad cleanup selection despite proxy approval; no named guard validates deletion membership. |
| 72 | requirement-not-tracked | NONE | Unmapped | Ian | Engineer request never becomes a work item. |
| 73 | instrument-not-tested | instrument | Escape | peer | Guard's own unrepresentative tests miss false refusals; class fits, self-validation not intercepted. |
| 74 | shared-surface-unchecked | push_ownership | Escape | peer | Shared push carries another session's unsettled commit; distinct from approval review. |
| 75 | claim-without-artifact | NONE | Unmapped | self | Timestamp mistaken for content identity; no supported completion/absence trigger established. |
| 76 | claim-without-artifact | absence | Escape | self | Truncated search supports a false absence claim; Grep sample warning is secondary. |
| 77 | claim-without-artifact | NONE | Unmapped | peer | Missing progress communication, not a contradictory board-state claim. |
| 78 | proof-that-cannot-fail | instrument | Escape | self | Broken binary scanner reports false absence without a valid known-answer test. |
| 79 | shared-surface-unchecked | push_ownership | Escape | peer | Export names the ownership pre-push hook, previously installed only in Operations. |
| 80 | precondition-not-checked | NONE | Unmapped | self | Runtime destination permission precondition not checked. |
| 81 | precondition-not-checked | NONE | Unmapped | self | Re-executed INSERT is non-idempotent; schema inspection does not establish safe replay. |
| 82 | identity-read-off-a-proxy-field | NONE | Unmapped | peer | Bridge version identity comes from another producer. |
| 83 | outcome-recorded-where-no-operator-reads-it | NONE | Unmapped | peer | Silent runtime catch; no guard verifies operator-visible logging. |
| 84 | proof-that-cannot-fail | look | Escape | self | Contact-sheet inspection misses actual sheet defects; rule cannot judge inspection depth. |
| 85 | requirement-not-tracked | NONE | Unmapped | Ian | Idle work and stale plan; PlanDocCheck reminds about documents, not the whole fault. |
| 86 | claim-without-artifact | absence | Escape | self | Known-invalid probe still used to claim no installed bridge. |
| 87 | interface-assumed-not-read | NONE | Unmapped | check | Aliased collection behavior assumed in product code. |
| 88 | identity-read-off-a-proxy-field | NONE | Unmapped | check | Proxy identity invalidates corpus comparison; no exported hook checks pair identity. |
| 91 | proof-that-cannot-fail | NONE | Unmapped | peer | Permission proof fails for the wrong reason; no specific commit-time trigger established. |
| 92 | proof-that-cannot-fail | NONE | Unmapped | check | Unrepresentative geometry fixture and subsequent regression. |
| 93 | precondition-not-checked | NONE | Unmapped | self | Remote start runs despite failed copy; commit_proof R3 does not cover remote start scripts. |
| 98 | shared-surface-unchecked | NONE | Unmapped | peer | Cross-file Python caller contract changed during shared execution. |
| 99 | identity-read-off-a-proxy-field | NONE | Unmapped | self | Wrong release destination identity; no fleet-release guard in evaluated inventory. |
| 100 | artefact-rebuilt-from-a-run-dirtied-folder | NONE | Unmapped | check | Packaging reuses a runtime-dirtied staging folder. |
| 101 | artefact-rebuilt-from-a-run-dirtied-folder | NONE | Unmapped | check | Packaging reuses a runtime-dirtied staging folder. |
| 102 | identity-read-off-a-proxy-field | NONE | Unmapped | self | Handler method never wired into dispatch; no reachability guard in evaluated inventory. |
| 103 | shared-surface-unchecked | NONE | Unmapped | self | New element field breaks another consumer's ownerView field. |
| 104 | shared-surface-unchecked | NONE | Unmapped | self | Amends another session's existing commit; commit_scope explicitly excludes prior amended contents. |
| 106 | interface-assumed-not-read | NONE | Unmapped | self | Destructive runtime constructor mistaken for resume support. |
| 107 | live-input-read-as-fixed-shape | NONE | Unmapped | peer | Live-machine state mixed across timestamps; mitigation explicitly rejected a proposed guard. |
| 108 | claim-without-artifact | look | Escape | peer | Accepts and reports drawing from counts without checking the actual direction. |
| 109 | proof-that-cannot-fail | look | Escape | engineer | Inspection misses engineering correspondence; same class, beyond the hook's comprehension checks. |
| 110 | identity-read-off-a-proxy-field | NONE | Unmapped | peer | Baseline identity inferred from a filename. |
| 112 | shared-surface-unchecked | NONE | Unmapped | check | Shared footing-layout readers use divergent rules. |
| 113 | instrument-not-tested | instrument | Escape | self | Writer and checks share an untested axis assumption; zero-report guard cannot prove geometry. |
| 114 | precondition-not-checked | NONE | Unmapped | self | Ignored failed migration result despite reading it; look presence is already satisfied. |
| 115 | precondition-not-checked | prior_art | Escape | peer | Migration number search uses incomplete branch scope. |
| 116 | requirement-not-tracked | NONE | Unmapped | Ian | Building-function requirement omitted. |
| 117 | claim-without-artifact | NONE | Unmapped | peer | Estimated handover times, not future board evidence. |
| 118 | proposal-without-looking | NONE | Unmapped | Ian | Infrastructure proposal in chat; plan-review header excludes chat and no creation is shown. |
| 119 | proof-that-cannot-fail | NONE | Unmapped | Ian | Enrollment memory fixture allows tools; command-to-test linkage is not established in inputs. |
| 120 | remote-walk-over-share | bash | Escape | Ian | Remote Python share walk bypasses the former PowerShell-only rule. |
| 131 | shared-surface-unchecked | pre_push_review | Escape | check | Shared amend rewrites a SHA; mitigation names review refusal for unreviewed/rewritten commits. |
| 132 | claim-without-artifact | NONE | Unmapped | Ian | Claims board content/order, not an explicit row-state contradiction. |
| 133 | shared-surface-unchecked | NONE | Unmapped | check | Shared install directory and broad cleanup glob break a different application. |
| 135 | requirement-not-tracked | kor-enforce | Escape | self | Mitigation explicitly connects LocalDB policy to brain.Check enforcement. |
| 136 | proof-that-cannot-fail | look | Escape | self | Green tests from old binaries after a nonbuilding commit. |
| 138 | escaping-across-a-boundary | NONE | Unmapped | self | Credential detector misses prose secret; no secret-redaction guard in evaluated inventory. |
| 139 | requirement-not-tracked | NONE | Unmapped | peer | Missing per-repo capture wiring; enroll-check identity/implementation is not defined in Increment 1. |
| 141 | proposal-without-looking | prior_art | Escape | Ian | Directory listing passed the guard while existing import pipeline was ignored. |
| 142 | claim-without-artifact | board_time | Escape | peer | Board closed with future evidence time before its deliverable existed. |
| 143 | claim-without-artifact | commit_scope | Escape | check | Plain commit consumes another session's staged files. |
| 145 | proposal-without-looking | prior_art | Escape | Ian | New audit brief launched on a truncated filename-only prior-work search. |

## Guard identity cross-reference

Paths prefixed Operations/ or Framework/ are relative to C:/VIsual Studio Projects/. Executable subcommands have no readable script header in Increment 1; an inferred association is not promoted to a verified implementation path.

| Export guard | Increment 1 identity or limit |
|---|---|
| bash | Operations/tools/claude_bash_guard.py |
| absence | Operations/tools/claude_absence_guard.py |
| look | Operations/tools/claude_look_guard.py |
| kor-repeat | C:/KOR-Operations/kor/kor.exe repeat |
| pre_push_review | No corresponding settings entry in Increment 1; script path not decidable from the export. |
| kor-context-read | No separately named entry; relationship to kor.exe context is plausible, not established by Increment 1. |
| kor-repeat-reason | No separately named entry; relationship to kor.exe repeat is plausible, not established by Increment 1. |
| kor-enforce | C:/KOR-Operations/kor/kor.exe enforce |
| sql_schema | Operations/tools/claude_sql_schema_guard.py |
| run-sample | Operations/tools/claude_run_sample_guard.py |
| prior_art | Operations/tools/claude_prior_art_guard.py |
| board | Framework/tools/claude_board_guard.py |
| kor-drafter-python | No separately named entry; implementation path not decidable from the export. |
| kor-context | C:/KOR-Operations/kor/kor.exe context |
| plan_review | Framework/tools/plan_review_guard.py |
| kor-stall | C:/KOR-Operations/kor/kor.exe stall |
| api | Operations/tools/claude_api_guard.py |

## Verification and scope

Read-back checks on this response verify 222 unique random refusal IDs, one judgement per ID, reasons below 15 words, 115 unique mistake IDs, one mapping per ID, the 17 count rows, earlier judgement reconciliation, verdict totals, and the class/escape/unmapped totals. Arithmetic and completeness checks validate this report's accounting; they do not validate an opaque rule or supply missing context. Only this response file was created for the task; pre-existing workspace changes were left in place, and no commit was made.

