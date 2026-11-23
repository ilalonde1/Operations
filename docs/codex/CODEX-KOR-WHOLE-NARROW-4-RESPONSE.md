# Kor whole round 4 — Codex response, 23 November 2026

The gate now allows the tested read-only recovery calls and work-saving `kor record` / `kor state` calls, credits accounting carried by a completed look immediately, recognizes the cited zero-deficit and attributed-count forms, and supports evidence-backed `NOT LISTABLE`. **221 gate cases pass, including all 161 existing cases unchanged.** The full Framework comparison has **zero new failing test names**, 60 added tests all passing, and no missing baseline tests. This is not a claim that every historical refusal is repaired: three decisions I judge wrong retain other grounds, detailed below.

Work started on `kor-whole-narrow-4` at `6b32321d`, whose only difference from `2763a763` was the round-4 brief. The full baseline suite actually ran with HEAD detached at **2763a763**; the branch was then restored. Production changes are confined to `Kor.Operations.Framework/Whole/WholeGate.cs`. `WholeTranscript.cs` and the original `WholeGateTests.cs` are unchanged. The other tracked changes are the additive `WholeGateRound4Tests.cs` and this response.

Prior art searched/read: `AGENTS.md`, `CLAUDE.md`, `docs/HOW-WE-BUILD.md`, all three earlier briefs and responses, the gate and transcript parser, existing gate tests, CLI hook/replay and testdiff code, and `tools/ClaudeGuardAudit` / `tools/SqlQuery`. Local frozen transcripts supply ground truth; the complete gate suite is the regression harness; independent decisions inspect actual tool inputs, outputs and refusal keys. There is no graphical deliverable to render.

No push, installation, remote process, settings change, hook edit, or database write was performed. Database access was SELECT-only against `KorStandards.oversight.GuardRefusal`, through `tools/SqlQuery/oq.py`. All returned Judgement fields were NULL; the judgments below are mine and were not written back. TEMP and TMP retained their normal values. Every commit and temporary checkout used command-local `-c core.hooksPath=Kor.Operations.Framework.Tests/obj/whole-round4/no-hooks`, an empty directory, because the shared hooks write to the database.

**Evidence and judgments**

`C` denotes `C:\Users\ilalonde\.claude\projects\C--VIsual-Studio-Projects-Framework\cc3b7be3-1a3f-4b77-a674-537c582d930a.jsonl`; `F` denotes the adjacent `80383e99-22b4-4bef-8ca8-dd95ed6a068f.jsonl`. Line numbers are physical, one-based JSONL lines. The database timestamps include **-07:00**; I converted them to match transcript UTC timestamps rather than treating them as UTC already. All 38 rows for these two sessions were retrieved and matched to transcript refusals within two seconds. The stored Matched field is a clipped selection of grounds, so the transcript refusal supplies the rest.

Across the **38 unique live refusals**, my judgments are **2 RIGHT / 31 WRONG / 5 NOT SETTLED**. RIGHT means there is a valid reason to refuse the action even if another listed ground is wrong. The class totals overlap where one refusal demonstrates more than one class:

| Class | Right / wrong / not settled, of N | Refusal IDs |
|---|---|---|
| Recovery deadlock | 0 / 15 / 0 of 15 | 10707–10710, 10712, 10715, 10716, 10720, 10721, 10723, 10729–10732, 10743 |
| Mid-turn accounting claims | 1 / 2 / 1 of 4 | RIGHT 10717; WRONG 10702, 10733; NOT SETTLED 10701 |
| Pass, empty and baseline counts | 0 / 13 / 2 of 15 | WRONG 10704, 10706, 10711, 10713, 10734–10738, 10740, 10741, 10744, 10745; NOT SETTLED 10714, 10719 |
| Other people's numbers | 0 / 4 / 0 of 4 | 10711, 10712, 10742, 10743 |
| Unlistable populations | 1 / 2 / 0 of 3 | RIGHT 10722; WRONG 10721, 10723 (both stopped reads) |
| Other current review-scope grounds, outside the five numerical/recovery classes | 0 / 0 / 2 of 2 | 10724, 10747 |

The moments and reasons behind those judgments:

| IDs | Actual evidence and judgment |
|---|---|
| 10707–10710 | C calls L1157, L1175, L1203, L1224 save state. **WRONG** to deny the work-saving operation. C L1201's successful Edit already accounted the scope-only key; the next refusal dropped that ground. This contradicts a universal “mid-turn lines never count” explanation. |
| 10712, 10715, 10716, 10720, 10743 | C L1369, L2020, L2054, L2238, L3225 save findings. **WRONG** to block preservation of the work. These are now allowed regardless of outstanding flags. |
| 10721, 10723 | C L2272 and L2387 `cd` into `docs/one-tool/run-on-210`, list files, read send-script usage and read the holder text. **WRONG**: neither invokes a send. Both were W3, not W1. The working-directory token was sufficient to classify the read as an expensive step. |
| 10729–10732 | F L608, L620, L626 run `kor record --help`; L656 does the same through PowerShell. **WRONG**. F L650 shows `git status` working in this interval. This was not every Bash/PowerShell call being refused. |
| 10701 | C L608 is a follow-up SendMessage; the frozen call's visible text ends in an ellipsis. Its refusal cites the current 5/14 audit and review limitations. **NOT SETTLED**: incomplete message text prevents a reliable judgment of its claims. The new gate retains W1. |
| 10702 | C L624 explicitly accounts all four keys. **WRONG** to refuse that reply. The complete-prefix baseline already allows it, as does the new gate. The surviving transcript cannot establish exactly which text/look the live hook had seen; I do not claim a demonstrated parser-timing repair for this row. |
| 10704 | C L924 prints `expected FAIL states printed PASS: 0 of 215` and `scope-only focuses (not asserted): 3 of 11 met`. C L948 describes the eight unmet focuses. **WRONG**: zero false acceptances is clean, and three met is not three failures. The zero flag is removed; the old nonzero `met`/key-matching problem still leaves W1 here. |
| 10706 | C L1083 writes the README; the refusal combines the false 215 flag with an earlier 5/14 acceptance summary. **WRONG**: fixture “expected PASS” wording is not dismissing that bundle's unresolved acceptance items. The zero flag is removed; W2 still associates the generic word `acceptance` with the other population. |
| 10711, 10712 | C L1301/L1305 reads a review saying it could not verify the brief's numbers, including 6/14. It did not run that bundle check. **WRONG** attribution; those quoted numbers no longer create findings. The zero-acceptance ground at 10711 also disappears. |
| 10713 | C L1710 reports 45/46 and names the failing IanBrain memory check. C L1714 explicitly states the one failure and its existing board row. **WRONG** to demand undisclosed accounting. The flag remains real; the full reply now accounts it. An expensive step without opening still refuses. |
| 10714 | C L2003 describes the A18 hole and says the same 45 tests fail on the baseline. The refusal includes genuine mesh/acceptance/scope findings, repeated test lines and the false 215 count. **NOT SETTLED** as a whole decision; the zero ground is wrong, but I have not established discharge of every other population. Unlike F L315, these failure totals and the baseline comparison arrived in separate outputs. |
| 10717 | C L2062 asserts `OPENED 71 of 71`, `OPENED 45 of 45` and `OPENED 46 of 46`; the adequate later uncut failure-name lists are at C L2281/L2288, and the fragment inspection is later again. **RIGHT** to reject unsupported OPENED assertions. Its false 215/W2 ground disappears; W1 remains. |
| 10719 | C L2221 describes diagnostic options; the refusal mixes the two fixture-scope readings with new mesh/acceptance readings from L2140. **NOT SETTLED** as a whole decision. Removing zero false acceptances does not settle its remaining scope and subject attribution. |
| 10722 | C L2319 reports 71 missing. L2326 prints the NTS total and listability split; the later Python listing prints 60 records, clipping each serialized record to 160 characters. C L2364 nevertheless says OPENED 71/71. **RIGHT** to refuse that assertion; the separate “65 missing” ground is wrong (`65 missing 0 []`). No historical NOT LISTABLE declaration or single qualifying whole look exists. The new cases construct that correction rather than pretend it happened. |
| 10724 | C L2588's reviewer explicitly says other branches were not checked; C L2627 announces the brief. **NOT SETTLED** whether that review limitation belongs to this reply's required scope. It is the reviewer's actual limitation, not a quoted failure count; I did not blanket-exempt review output. |
| 10733 | F L687 explicitly reports A's 23 failing new cases, B's 82/82 and the same 49 baseline failures. **WRONG** to treat those numerical results as hidden. The zero flag, the same-output 49-name comparison and the review's quoted counts are repaired, but W1 remains on other wording/keys, including the unrelated review's cut-diff limitation. |
| 10734–10738, 10740, 10741, 10744, 10745 | F L947, L1012, L1026, L1035, L1062, L1089, L1107, L1133, L1147. **WRONG**: every stored matched ground is the inverted 0/215 count. All nine decisions now allow. The word `expected` in the copied key also triggered W2; there is no deficit to dismiss. |
| 10742, 10743 | C L3189/L3194 prints added lines from another session's HANDOVER commit, then reads ruling data. `23 fail` and `49 failures` are git diff content. **WRONG** attribution. Marked diff content is ignored while an unprefixed current result in the same command remains measurable. |
| 10747 | C L3332 contains the reviewer's real statement that it did not open previous responses/transcripts. C L3362 announces the brief. **NOT SETTLED** whether that unperformed review scope must be carried into this announcement. It is not evidence that the coordinator performed a failing count. W1 is retained. |

F L705–L720 is direct counterevidence to the supposed turn-end-only rule: accounting was followed by a tool call in the same turn; that call reached `kor`, which rejected the description's length, and the shortened record then ran. The baseline code also called `Account` on assistant text, tool inputs and the prospective door. The actual additional hole I found and fixed was different: **a tool's accounting was processed before its own look output, but never reconsidered after that output arrived**. Conversely, a denied call's input previously could receive accounting credit; it now cannot.

**Cases and changes**

The failure class is losing the distinction between *a recovery operation and an expensive action*, or between *a result and text describing a result*. The paired cases also require real deficits and unsupported completeness claims to keep refusing. All old tests remain byte-for-byte unchanged; no MUST REFUSE expectation was changed to pass.

| Commit | Contents and pre-fix evidence |
|---|---|
| `58957d9d` — `kor whole round 4 A: cases (codex)` | 52 new cases, committed alone before production edits. Unchanged gate: 192 pass / 21 fail of 213. The 31 already-green additions are refusal/control cases and conformance checks, not claimed as newly detected defects. |
| `a4452aac` — A2 | Five additive cases: actual full self-test sentence, completed-look accounting, denied-input accounting, state output-file pipeline, numbered text listability. All five failed before their fixes. Original baseline DLL with all 218 then-existing cases: 192 pass / 26 fail. A was not amended. |
| `b7519f47` — `kor whole round 4 B: keep recovery open and require population evidence (codex)` | Main implementation and rule-comment update. At this point 218/218 passed. |
| `0222310f` — A3 | Two more reader forms, `git -C` and direct Python text reading of runner documentation: both failed before the follow-up fix. |
| `d996a49b` — A4 | Python reading a log produced by this session must still supply failures. This exposed a regression in B's new Python reader exemption; it failed on B and is restored by B2. This is not claimed as a defect in 2763a763. |
| `dba4d56d` — B2 | Recognizes those reader invocations while retaining same-session result provenance. Final 221/221. |
| Response commit | This file alone, committed last. |

The 60 added cases cover recovery commands and mixed expensive commands; text/current-tool/completed-tool accounting; empty, inverted and nonzero counts; same-output baseline name comparisons versus equal totals or new names; attributed document/review numbers versus current check results; and NOT LISTABLE with its missing, premature, mismatched, cut, refused, short and duplicate-list counterexamples. The unchanged cases also caught my initial loss of the relative send's working directory; that was corrected before B.

Recovery classification is separate from result provenance. Reading a known own-run log is permitted as an operation and still contributes its failures. `kor record/state` do not become a bypass for an expensive command or board/task close joined to them. Numeric parsing gives an explicit `missing 0 []` priority over the preceding file total, and recognizes false acceptances in the expected-FAIL diagnostic. Baseline treatment requires an explicit by-name comparison with zero new names, or the observed counted lists plus two empty `comm` differences; equal totals alone do not suffice. Existing testdiff-summary-only refusal cases remain unchanged and pass.

`NOT LISTABLE` uses a deliberately bounded, documented format. For example, after the 71-missing flag, a fresh uncut look can print:

```text
missing: count 71; listable: 60
1: <first piece and its details>
...
60: <last listable piece and its details>
```

The ellipsis above abbreviates documentation, not acceptable evidence: the actual output must contain every distinct record numbered 1 through 60. Numbered JSON objects and the observed `pieces > 1e-9 60` header are also supported. Then:

```text
NOT LISTABLE: 71 missing - inspected measurement.json: 60 pieces listed; 11 fragments have no individual records below 1e-9.
```

The source named in the explanation must occur in that later look. A count-only output, unrelated population/source, pre-flag look, duplicate/short list, refused/cut call, or bare declaration cannot discharge W3. NOT OPENED still does not discharge it. This acknowledges a bounded inspection; it is not an assertion that the remainder was individually opened.

**Proof and testdiff**

All builds/tests were Debug and scoped to Framework tests and their dependencies. Commands were not piped; complete output and exit codes were retained. The full baseline and final invocations each ran **once**, sequentially, with no other suite running. The full suite contains live database tests, so process-local nonempty invalid values were set for `KORBRAIN_SQL`, `KOR_ENGINEERINGTOOLS_STANDARDSDB` and `KOR_OPPORTUNITIES_OPPORTUNITIESDB`. For Framework connections, the first two prevent registry fallback and fail parsing before opening a connection. No repository settings were edited. This is a comparison with database dependencies disabled, not a passing live integration run.

| Check | Baseline | Final |
|---|---:|---:|
| Existing WholeGate cases | 161 pass, 0 fail | All 161 unchanged and passing |
| Complete gate suite | See staged red runs above | **221 pass, 0 fail, 0 skipped** |
| Full Framework | **1107 pass, 257 fail**, 1364 results; 26 s | **1167 pass, 257 fail**, 1424 results; 28 s |
| Distinct test names | 1358 | 1418 |
| Failing names in both | — | **254** |
| Only-new failing names / missing baseline tests | — | **0 / 0** |
| Added tests | — | **60, all passing** |

Both full test commands exited 1. `testdiff` exited **0**, `NO NEW FAILURES`. Of the 257 failed results, 187 directly report the deliberately invalid connection string; the other 70 are also unchanged by name. I do not label those as regressions. Started-but-unfinished cases are **not measured**, because no blame sequence was supplied.

Commands actually run, with `P` abbreviating `Kor.Operations.Framework.Tests/obj/whole-round4`:

```text
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug -nologo --filter FullyQualifiedName~WholeGate --logger "trx;LogFileName=whole-final.trx" --results-directory P
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug -nologo --logger "trx;LogFileName=base.trx" --results-directory P
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug -nologo --logger "trx;LogFileName=new.trx" --results-directory P
dotnet Kor.Operations.Framework.Cli/bin/Debug/net8.0-windows/kor.dll testdiff P/base.trx P/new.trx
```

The last two full invocations were at 2763a763 and the restored final branch respectively. All commands used the built assemblies, never installed kor. Final Framework DLL SHA-256: `36f518dc3510063db4bf5f1a2a22314557f397a4a16fff942af5fdda7172dca9`.

The local probe separately rebuilt a ledger from each frozen transcript prefix and called `Decide` **without Comply**, hook logging, SQL file execution or database access. It tested **39 moments**: the 38 database rows and the round-1 held-out RIGHT pad send, A1 `e7287342` L6516. Refusals were **38 before / 11 after**. Among the 38 live rows, 37 before and 10 after refuse; 10702 already allowed in the full-prefix baseline. The held-out send remains **W3**, including all three pad deficits at A1 L6434 (176/180, 159/163, 108/112). Counts are decisions on these prefixes, not a false-positive rate.

The ten live decisions still refusing are **10701, 10704, 10706, 10714, 10717, 10719, 10722, 10724, 10733, 10747**. In particular, **10704, 10706 and 10733 remain wrong on other grounds** described above. The work-saving and read-only calls still work in their presence; new NOT LISTABLE cases demonstrate the required correction rather than altering the historical statements.

Local ignored proof material in `P` includes frozen JSONL copies, SELECT output, all TRXs/logs, testdiff, the inspection/probe sources, and before/after decision JSON. Frozen inputs:

| Input | Lines | SHA-256 |
|---|---:|---|
| C | 3469 | `0791fd8bb9cb556379587d5dd5696c0eb4066fe21b8797423aad35f9483e1b6c` |
| F | 1157 | `f25639d7121485a00e895b7a93b1a965680bfac8552401925ff0d29debf2f371` |
| A1 | 7359 | `18b8a2a00cce7924bd88d3990ee4b394893d69071ae3edf1c9e7062f80f027ba` |

**Rule 11: covers, does not cover, same-class misses**

| Class | Covers | Does not establish / one concrete same-class miss |
|---|---|---|
| Recovery | Recognized shell readers, git reads including global path options, kor help/record/state, mixed steps and relative sends | Not a shell interpreter. A wrapper or alias can hide a send; a reader-looking program can execute code. Unknown invocation forms can still be misclassified. |
| Accounting | Assistant text, prospective tool text, successful look-carried accounting, denied-call rejection | Not semantic understanding of arbitrary disclosure. `23 of the 82` and alternative labels can fail to match; reused labels can wrongly attribute one look to a different population. Existing OPENED line-count evidence can accept unrelated lines. |
| Counts/baselines | The cited zero forms, real nonzero counterparts, complete same-output name comparison versus equal totals/new names | Does not prove a comparison program honest, nor interpret every diagnostic grammar. `3 of 11 met` beside `not asserted` still has the wrong polarity; an arbitrary “4 pads irregular” remains unrecognized. A falsely printed comparison can hide an actual failure. |
| Quoted text | Known readers, marked git additions in mixed output, explicitly attributed document/review counts, own-run result preservation | Does not interpret arbitrary Python or unmarked mixed stdout. A checker emitting `+ 4 failed` beside a git diff can be mistaken for diff content; generic W2 subject words can still connect unrelated populations. Current review limitations are not blanket-exempt. |
| Unlistable populations | Post-flag, uncut same-call total plus numbered listable records, source named in the explanation; missing/duplicate/mismatched evidence refuses | Does not prove that no other tool can enumerate the remainder, that records are genuine, or that identical labels/counts identify the same population. A dishonest checker could print a made-up listability limit and numbered filler. Other formats require normalization to the documented output grammar. |

The harness does not cover live installation/logging, the author's unseen cases, other sessions' unobserved producers, arbitrary filesystem identity, or populations never mentioned. The old command-wide record/replay exemptions and W4 basename heuristic remain. The whole-source fault “sample five pads with no total, then claim all pads are four-sided” can still pass: no recognized deficit and no numbered sampled-range claim exist.

**Corrections to the brief**

The branch tip was two brief-only commits beyond 2763a763. The sources show a substantial recovery problem, but not refusal of every shell call: runner-documentation refusals were W3, and F explicitly said git and other tools still worked. Mid-turn accounting was already implemented and demonstrably worked in F; not every disputed OPENED statement had a qualifying look. A 45/46 result is a real one-item deficit, not a clean pass. F's 23 failures are that coordinator's real red-case verification run, whereas C's later 23/49 handover numbers are quotations. Those origins must remain distinct.

The “71 missing” evidence supports a 71/60 enumeration gap in the observed tooling, not proof that no possible tool can enumerate the remaining eleven. The historical listing and total were separate calls, and the listing clipped each record. A constructed fresh look and explicit NOT LISTABLE statement are therefore necessary to demonstrate the requested new discharge rule.

Finally, “every new case failing on current code” cannot literally include the requested MUST REFUSE controls that already refuse correctly. The red defects and already-green controls are reported separately above; A was never amended. A4 specifically records a regression introduced during this work and repaired before the final full run. Process: one Codex session, no subagents, no external model calls, no permission questions; no reliable billed-token/cost meter was available.
