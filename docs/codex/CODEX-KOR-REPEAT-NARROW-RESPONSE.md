# Kor repeat narrowing — Codex response, 21 November 2026

The narrowed gate passes the 21 supplied new-work moments and still refuses all three token-breakdown repeats, naming #129845. The focused Debug run passes **57/57**: 51 added cases and all six unchanged `RepeatGateTests`. **Final full-suite proof is currently incomplete: I stopped the run on discovering a non-rollback database path caused by my test TEMP configuration.**

The four present-day replays change from **31 refusals to 1** over 1,700 calls. The remaining refusal is the pad census against #135767, a finding recorded **after** that call. The census allows at its actual `--as-of` moment. A present-day replay is not a historical correctness score.

**Scope and evidence handling**

Started on `kor-repeat-narrow` at `71a4d0fc`, not the brief's stated `59b76e06`. The only difference between those commits is the brief; baseline implementation/tests match `59b76e06`. Production changes are confined to `RepeatGate.cs` and `Program.Repeat.cs`. No existing RepeatGate test was edited. No push, install, settings/hook edit, holdout access, or remote shell/process launch was performed. The database exception is disclosed below.

I read the gate summary, `BrainRecall`, the CLI dry-run/replay/hook paths, `RepeatGateTests`, `AGENTS.md`, `CLAUDE.md`, and the prior whole-narrow responses. Prior-art searches covered `tools/`, `docs/codex/`, and `../KOR.Drafter/db/tools/` for repeat/transcript/replay tooling. Existing `repeat --as-of`, `repeat --replay`, `testdiff`, and `tools/verify_built_strings.py` supplied the instruments; no new production tool or gate was added.

The inputs are local: five supplied main transcripts and their eight available helper transcripts were inspected. All 24 cited calls were found in the main transcripts. Fixture `Kor.Operations.Framework.Tests/Fixtures/repeat-narrow-moments.json` retains the original tool inputs, UTC timestamps, source paths, line numbers and source SHA-256 hashes. Commands in that fixture are data; they were never executed. Tests invoke only the repeat dry run with their text.

Dry run and replay dispatch to `BrainRecall.Build` through `WithStores`/`OpenRead`; the index builder issues SELECTs and does not call refusal logging. The hook does write. Existing Live tests call that hook with a rolled-back Board transaction. The brief's blanket read-only rule therefore conflicts with its test requirement. **Ian explicitly authorized the existing rolled-back tests in this session.** That authorization did not cover the non-rollback path discovered later; see the incident below.

The shared post-commit hook invokes installed `kor.exe capture-git post-commit`, which writes the database. All task commits use the authorized command-local override:

```text
git -c core.hooksPath=Kor.Operations.Framework.Tests/obj/repeat-narrow/no-hooks commit ...
```

Test temporary files and retained proof artifacts are under this worktree's `Kor.Operations.Framework.Tests/obj/repeat-narrow/`; the response and fixtures are the durable committed evidence.

**Test-harness incident — my error**

To keep temporary file writes inside the worktree, I set TEMP/TMP to `Kor.Operations.Framework.Tests/obj/repeat-narrow/temp`. That changed Git discovery for `CaptureTests.HookModeCaptureGitOutsideARepoExitsZeroSilently`: its temporary “notRepo” directory inherited this worktree. The test invokes `Program.Run(capture-git post-commit)` without a supplied `BrainDb`; `CaptureGit` reaches `BrainStore.Insert` and a committing freshness path, rather than failing before any write. Its baseline failure (expected two error-log lines, actual one) is consistent with that path succeeding. Earlier full runs had already executed it. **Those runs cannot be described as entirely rolled back.** I have not queried the resulting rows, so I do not claim an exact event count or scope of persisted freshness changes.

I stopped the verified testhost immediately after identifying the path. No database inspection or cleanup was attempted. A local, database-free probe confirmed the cause and a possible containment: the temp subfolder resolves to this worktree without a ceiling; with command-local `GIT_CEILING_DIRECTORIES` set to the test-temp root, Git exits 128 (“not a git repository”). I asked Ian whether to continue with a repaired and audited harness or stop with incomplete proof, as the brief requires. This was caused by my harness choice; the rollback-test permission did not authorize it.

**Verdict on the claimed causes**

- **(a) Confirmed, but incomplete.** The old three-word test admitted job/set words without establishing the quantity. It also admitted incidental body matches and generic action words. At 9001, `pad/b1/l02` sufficed for stiff-pad and mesh findings that did not characterize the requested overlap groups. At 8962, `file/size/folder` matched a yardstick-location record. At 8236, `merge/diff/piece` matched a JM drift finding. At 8240/8243, “measurement” modified test cases/runs; it did not say a new measurement was being started. A blacklist of job numbers would not fix this class.
- **(b) Confirmed.** `Take(3)` happened before lift lookup. A valid named record could disappear after re-ranking. The changed-input marker itself also supplied `input/change/since` topic words and generated unrelated refusals, including migration #131667. Conversely, any of the old three matches could lift a refusal even when it answered a different quantity, and an empty explanation counted. Added negative cases cover both defects.
- **(c) Confirmed for the supplied Write, with a narrower interpretation.** `Intent` explicitly selects newly created files; it does not mistake an existing-file rewrite for a creation. The error is treating the type phrase “SAFE analysis model” in a new README's title as an analysis task, then borrowing results about other models/READMEs. New documents are not exempted wholesale: the added slab-thickness analysis document still refuses when its question has already been answered.

**Implementation and limits**

The decision now follows this sequence:

```text
intent -> requested subject and explicit identifiers
       -> existing BM25 floor + three intent-word matches
       -> subject and identifiers in result name/lead
       -> relevant nonempty changed-input reference, without a top-three limit
       -> allow, or display up to three refusal records
```

Subject extraction uses the existing work vocabulary and reads bounded grammatical forms: the object of measure/survey/census/etc.; the words inside “break X down”; and noun titles such as “slab thickness analysis”. It retains quantity word `top`, which the recall tokenizer otherwise discards as SQL vocabulary. Scope/dimension clauses do not substitute for the measured subject. Explicit identifiers in the question must occur in the result name/lead. Audit candidates must identify an audit, rather than merely discuss the audited artifact. The model-type phrase `analysis model` is not treated as commissioning an analysis.

The score floor, BM25 ranker, three-word floor, result kinds, hook wiring, and three-record display cap are retained. Commands and bodies continue to rank results, but a subject found only there cannot establish a refusal. A suffix changed-input explanation does not replace the question. A valid lift names an indexed finding/dead-end about that subject and gives a nonempty reason; it can be outside the displayed three. For an audit, the referenced input finding need not itself be an audit result. Unknown, unrelated and empty references do not lift an established repeat.

`Program.Repeat.cs` now passes the full moment to lift parsing while retaining the existing 1,000-character ranking limit. Previously the CLI truncated both, unlike the hook/replay. Its ALLOW explanation now describes the actual subject test.

This is deliberately a lexical check, not a semantic proof. It cannot prove an author's claimed input change, resolve synonyms, reconstruct historical database versions, or see results stored only in files. Requiring the name/lead trades some recall for precision: a real result described only in its body can now escape. Scope expressed only in a command path is not established by the explicit-identifier check. Compound intents are not a complete natural-language parser. These are limitations, not claims of solved repeat detection.

**Cases and commit ordering**

There are **51 added cases: 19 MUST REFUSE and 32 MUST PASS**. The 24 transcript cases are 3 refuse / 21 pass; the 27 additional cases are 16 refuse / 11 pass.

| Commit | Cases/change | Reason |
|---|---|---|
| `837df000` — `kor repeat narrow A: cases (codex)` | 24 historical + 13 constructed | Committed before production edits; never amended. |
| `83a2011e` — A2 | Five additional boundaries | Add an unrelated re-rank, a lift in the description, an audit outcome control, an above-floor body-only match, and a lift beyond the CLI ranking cutoff. |
| `f8162d04` — A3 | Correct one fixture's ranking premise | A2's asserted unrelated top three did not actually outrank the measurement. Lengthened that measurement's incidental body; the expected ALLOW and required logged reference stayed unchanged. |
| `a00ff09e` — A4 | Two different-input cases | Same quantity on a different explicit job/storey was missing from the first set. Both failed before the scope check. |
| `b93b9ac7` — A5 | Four retained grammar controls | The first matcher dropped noun and inflected forms. All four pass on baseline, failed on the first implementation, and pass after correcting object selection and inflection handling. |
| `cbd740b0` — A6 | Three remaining work-recognizer controls | Extending that audit found investigation/characterisation nouns and read-off. All three pass on baseline, failed on the candidate, and pass after using the original work vocabulary and preserving ReadOff. |

Constructed sources and expected outcomes:

| Source in `RepeatGateNarrowTests` | Refuse | Pass | Boundary |
|---|---:|---:|---|
| `SameQuantityRewordedMustRefuse` | 3 | 0 | Same slab thickness, job and storey; reordered wording and survey synonym. |
| `NounFormsOfTheSameMeasurementStillRefuse` and `InflectedAuditStillRefusesTheSameAuditOutcome` | 4 | 0 | Prefix/suffix noun forms and inflected audit retain real repeats. |
| `OtherExistingWorkFormsRetainTheSameQuestion` | 3 | 0 | Investigation/characterisation nouns and read-off remain guarded, including hook eligibility. |
| `SameJobDifferentQuestionMustPass` | 0 | 3 | Deflection, concrete strength, overlap groups versus thickness. |
| `SameQuantityOnAnExplicitlyDifferentInputMustPass` | 0 | 2 | Different job or storey. |
| `BodyMentionsAreNotAnAnswerToFileSizes` and `BodyOnlyQuestionAboveTheScoreFloorStillIsNotAnAnswer` | 0 | 2 | Location finding with incidental body vocabulary; the second asserts that score exceeds the floor. |
| `UnrelatedMissingOrEmptyLiftMustNotPermitRepeat` | 3 | 0 | Unrelated window finding, missing record, empty reason. |
| `AnUnrelatedLiftInTheDescriptionCannotReplaceTheQuestion` | 1 | 0 | Same negative lift placed in the description. |
| `RelevantLiftSurvivesRankingBelowTheThreeDisplayedResults` and `RelevantLiftSurvivesAnUnrelatedRerank` | 0 | 2 | Relevant reference below similar results and below unrelated results. |
| `NewDocumentDoesNotInheritAnUnrelatedResultForItsJob` | 0 | 1 | New analysis-model README. |
| `NewDocumentThatActuallyRepeatsTheQuantityStillRefuses` | 1 | 0 | New slab-thickness analysis document with unchanged input. |
| `AuditOfTheSameModelBundleWithAnAuditOutcomeStillRefuses` | 1 | 0 | Repeated audit remains guarded. |
| `LiveDryRunReadsTheLiftAfterTheRankingCutoff` | 0 | 1 | Existing #129845, valid explanation after character 1,000. |

The first 13 constructed cases were 6 passed / 7 failed against unchanged production code. Twenty additional cases were run against the retained, unchanged baseline Framework/CLI DLLs: **7 passed / 13 failed**. The seven A5/A6 controls separately passed against those same baseline DLLs. Combined with the 24 baseline CLI moments (3 correct refusals, 21 false refusals), the final harness has **34 demonstrated baseline failures and 17 baseline passes**. No MUST REFUSE expectation was weakened.

**Historical moments, through the built CLI**

Every row below used the full original description plus command and its own UTC timestamp. The Write uses exactly the filename/title that `Intent` supplies, not its full document body. Every invocation exited 0 with a decision and no error. Baseline was REFUSE for every row; final outcomes and named records are listed below. For 8986, final ALLOW explicitly credits #134049; the other 20 historical passes no longer establish a repeat. All three true repeats finally name only #129845.

5565/5572 are nested hook probes. The outer transcript calls at the listed times invoke the payload extracted from `cde1e168` at `2026-11-10T19:25:41.525Z` (the extraction is visible at `7e017d71` line 1128). The fixtures preserve that payload, not the outer installation/probe command. Their UTC times are the outer invocations, not independently queried GuardRefusal row times. Guard refusal IDs are the brief's labels, correlated to transcript calls/results; direct GuardRefusal queries were outside authorization.

| Refusal label | Source:line | UTC call timestamp | Baseline named records | Final |
|---|---|---|---|---|
| 8236 | a0ed29c9:201 | 2026-11-19T19:39:56.088Z | #133797 | ALLOW |
| 8237 | a0ed29c9:214 | 2026-11-19T19:40:02.957Z | #134064, #134056, #133674 | ALLOW |
| 8240 | a0ed29c9:960 | 2026-11-19T20:22:22.671Z | #131550, #133413, #131881 | ALLOW |
| 8243 | a0ed29c9:1824 | 2026-11-19T21:04:48.573Z | #134068 | ALLOW |
| 8540 | c5eddf43:2534 | 2026-11-20T03:05:11.594Z | #134026, #131734, #131755 | ALLOW |
| 8541 | c5eddf43:2543 | 2026-11-20T03:05:16.829Z | #131729, #133809, #131667 | ALLOW |
| 8962 | ee8754e5:1115 | 2026-11-20T18:35:33.863Z | #131675 | ALLOW |
| 8958 | e7287342:336 | 2026-11-20T18:17:10.483Z | #134025, #124823, #132267 | ALLOW |
| 8960 | e7287342:462 | 2026-11-20T18:18:41.849Z | #133457, #133782, #131656 | ALLOW |
| 8964 | e7287342:1851 | 2026-11-20T18:53:04.785Z | #134049, #133949, #133936 | ALLOW |
| 8966 | e7287342:1874 | 2026-11-20T18:53:28.296Z | #134049, #134033, #134035 | ALLOW |
| 8970 | e7287342:2118 | 2026-11-20T19:04:58.227Z | #134035, #135743, #134049 | ALLOW |
| 8974 | e7287342:2565 | 2026-11-20T19:40:18.855Z | #132352, #124434, #135650 | ALLOW |
| 8975 | e7287342:2881 | 2026-11-20T19:56:25.644Z | #135743, #135744, #134033 | ALLOW |
| 8976 | e7287342:2898 | 2026-11-20T19:56:30.909Z | #135744, #134015, #133103 | ALLOW |
| 8984 | e7287342:4593 | 2026-11-20T22:46:15.866Z | #134049 | ALLOW |
| 8986 | e7287342:5032 | 2026-11-20T23:01:28.673Z | #135744, #131667 | ALLOW |
| 8993 | e7287342:5930 | 2026-11-20T23:59:13.456Z | #131667 | ALLOW |
| 8995 | e7287342:6109 | 2026-11-21T00:08:43.138Z | #124976, #124845 | ALLOW |
| 8996 | e7287342:6123 | 2026-11-21T00:08:49.775Z | #135757, #134006, #132727 | ALLOW |
| 9001 | e7287342:6332 | 2026-11-21T00:16:38.784Z | #134049, #135766, #135760 | ALLOW |
| 5565 | 7e017d71:1166 | 2026-11-11T06:31:53.962Z | #129845, #131860 | REFUSE |
| 5572 | 7e017d71:1240 | 2026-11-11T06:34:51.719Z | #129845, #131860 | REFUSE |
| 5573 | 7e017d71:1290 | 2026-11-11T06:36:03.791Z | #129845, #114270, #130176 | REFUSE |

**Four transcript replays, refusal by refusal**

Both sides use the built CLI and one current index per transcript. Calls include duplicate transcript entries without timestamps; those duplicates are reported separately below, not counted as independent historical moments.

| Transcript | Calls | Work-word calls | Baseline refused / lifted | Final refused / lifted |
|---|---:|---:|---:|---:|
| a0ed29c9 | 417 | 8 | 5 / 1 | 0 / 0 |
| c5eddf43 | 357 | 4 | 2 / 0 | 0 / 0 |
| ee8754e5 | 294 | 2 | 1 / 0 | 0 / 0 |
| e7287342 | 632 | 34 | 23 / 8 | 1 / 6 |
| Total | 1,700 | 48 | 31 / 9 | 1 / 6 |

Every baseline refusal follows, numbered within its transcript. ALLOW means the final replay did not establish a repeat; LIFT means its output explicitly credited the reference. No new refusal appeared outside this list.

| Transcript / ordinal | Refused intent | Baseline records | Final |
|---|---|---|---|
| a0ed29c9 / 1 | Measure the merge diff sizes and read the --piece docs | #133797 | ALLOW |
| a0ed29c9 / 2 | inputs changed since #133797: unrelated record (JM 01819 drift); measuring port-merge-19nov redo merge diff sizes for review | #134064, #135719, #134067 | ALLOW |
| a0ed29c9 / 3 | Run the two never-finishing PdfToSafe measurement cases alone on both trees, 90-min limit | #134068, #134071, #131550 | ALLOW |
| a0ed29c9 / 4 | Run the two never-finishing PdfToSafe measurement cases alone on both trees, 90-min limit | #134068, #134071, #131550 | ALLOW |
| a0ed29c9 / 5 | Compare the long measurement-case runs with testdiff | #134068, #135711 | ALLOW |
| c5eddf43 / 1 | Survey which Framework tool files kor-claude can write | #134026, #131734, #131755 | ALLOW |
| c5eddf43 / 2 | inputs changed since #134026: all gate files, not one - survey writability | #131729, #133809, #131667 | ALLOW |
| e7287342 / 1 | Measure every column top against the slab outlines in model and source | #135744, #134025, #124823 | ALLOW |
| e7287342 / 2 | Measure every column top against the slab outlines in model and source | #135744, #134025, #124823 | ALLOW |
| e7287342 / 3 | Measure the CAD slab edge at C14-C19 and draw the crop | #135744, #133457, #133782 | ALLOW |
| e7287342 / 4 | Measure the CAD slab edge at C14-C19 and draw the crop | #135744, #133457, #133782 | ALLOW |
| e7287342 / 5 | README 31097-01 Building 1 L02 - SAFE analysis model (acceptance proof, board row 2143) | #134049, #135744, #135752 | ALLOW |
| e7287342 / 6 | Run model-audit on the new bundle (draft README) | #135744, #134033, #134049 | ALLOW |
| e7287342 / 7 | Run model-audit on the new bundle (draft README) | #135744, #134033, #134049 | ALLOW |
| e7287342 / 8 | inputs changed since #134049: new run pass3 bundle - run model-audit on it | #135744, #135743, #134033 | ALLOW |
| e7287342 / 9 | inputs changed since #134049: README final - re-run model-audit on the pass3 bundle | #135744, #135743, #134035 | LIFT #134049 |
| e7287342 / 10 | inputs changed since #134049: README final - re-run model-audit on the pass3 bundle | #135744, #135743, #134035 | LIFT #134049 |
| e7287342 / 11 | inputs changed since #134049: Codex pass 3 dispositions - re-run model-audit | #135744, #135743, #134035 | ALLOW |
| e7287342 / 12 | Bind Codex's disposition into the bundle and re-run model-audit | #135744, #134035, #135743 | ALLOW |
| e7287342 / 13 | Run fixture and model-audit tests against the change | #135748, #136017, #132352 | ALLOW |
| e7287342 / 14 | inputs changed since #134035: bundle for pass 4 - run model-audit | #135743, #135744, #134033 | ALLOW |
| e7287342 / 15 | inputs changed since #134035: bundle for pass 4 - run model-audit | #135743, #135744, #134033 | ALLOW |
| e7287342 / 16 | inputs changed since #135743: rulings 187, fixed README, registration evidence - model-audit for pass 4 | #135744, #134015, #133103 | ALLOW |
| e7287342 / 17 | Build takeoff and audit the 2143 bundle | #134049 | ALLOW |
| e7287342 / 18 | inputs changed since #134049: new item-3a mesh check - audit 2143 bundle | #135744, #135743, #135757 | LIFT #134049 |
| e7287342 / 19 | inputs changed since #134049: 3a header fix - re-audit 2143 bundle | #135744, #131667 | LIFT #134049 |
| e7287342 / 20 | inputs changed since #135744: 3a overlap check + ruling 189 - re-audit 2143 | #131667 | ALLOW |
| e7287342 / 21 | Census of pad overlaps and walls in pads per storey | #135767, #135766, #135771 | REFUSE #135767 |
| e7287342 / 22 | inputs changed since #124976: different job (31097 gen) - pad census per storey | #135766, #135757, #134006 | ALLOW |
| e7287342 / 23 | Characterise each overlapping pad group on B1 L02 | #134049, #135766, #135760 | ALLOW |
| ee8754e5 / 1 | Measure file sizes in the triage folder | #131675 | ALLOW |

The remaining e7287342 census refusal names #135767. The original census is line 6109 at **2026-11-21T00:08:43.138Z**. The transcript shows the record command at line 6315 and the successful `recorded brain.Memory #135767` result at line 6318, **00:16:24.465Z**, about 7m41s later. The record reports the overlap census. It is legitimate evidence in a current-index replay but unavailable at the census's real moment; its historical test allows. I did not discard this later finding to make the replay counter zero.

**Build/test proof**

All builds/tests were scoped to Framework/CLI/tests, in Debug. No solution build or piped build was used. `tools/verify_built_strings.py` found the new CLI explanation and the new model-type matcher as UTF-16LE literals in the built DLLs before the final comparisons.

Commands run (temporary directory environment set to this worktree for test runs):

```text
dotnet build Kor.Operations.Framework.Cli/Kor.Operations.Framework.Cli.csproj -c Debug -nologo
dotnet build Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug -nologo
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug -nologo --no-build --filter "FullyQualifiedName~RepeatGate" --logger "trx;LogFileName=final-repeat.trx" --results-directory Kor.Operations.Framework.Tests/obj/repeat-narrow
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug -nologo --no-build --logger "trx;LogFileName=new-full.trx" --results-directory Kor.Operations.Framework.Tests/obj/repeat-narrow --blame-hang-timeout 5m
```

The unmodified full suite used the same project/configuration and result options with `base-full.trx` (including its build), before the new tests were compiled. Supplemental tests were also executed against a local copy of the retained baseline Framework and CLI DLLs; tests call no new API, so the identical assertions run on both implementations. The fixture tests and explicit CLI invocations cover the same 24 moments independently.

| Run | Results | Passed | Failed | Status |
|---|---:|---:|---:|---|
| Unmodified baseline | 1,300 | 1,241 | 59 | Completed; harness incident applies. |
| First candidate (`pre-grammar-full.trx`) | 1,344 | 1,285 | 59 | Completed; harness incident applies. |
| Second candidate (`pre-work-forms-full.trx`) | 1,348 | 1,289 | 59 | Completed; harness incident applies. |
| Final focused RepeatGate run | 57 | 57 | 0 | Completed on final code. |
| Final full suite (`new-full.trx`) | 1,215 emitted | 1,157 | 58 | Intentionally stopped; NOT a complete suite result. |

The two completed candidate comparisons both returned `NO NEW FAILURES`, exit 0: 56 distinct failing names in both baseline and candidate, no new failing names, and no missing baseline names. Those are intermediate-code comparisons, not a final full-suite proof.

The final comparison was run using:

```text
dotnet run --project Kor.Operations.Framework.Cli -c Debug --no-build -- testdiff Kor.Operations.Framework.Tests/obj/repeat-narrow/base-full.trx Kor.Operations.Framework.Tests/obj/repeat-narrow/new-full.trx
```

It returned **INCOMPLETE**, exit 1: the testhost was stopped, 128 baseline test names are missing, and 43 added names were emitted. Its zero newly failing names among emitted results does not establish equivalence. Complete output is retained in `interrupted-testdiff.txt`. The final full-suite requirement remains owed, pending Ian's decision about the repaired harness. No database-effect query or cleanup is authorized or performed.

The 59 baseline failures are environment-specific, not a claim about the canonical checkout: EnrollCheck 40; DrafterPythonGuard 12; StartPagesLive 2; Capture, BrainRecall, K5b, FileMemoryLive and StartSplitLive one each. In particular, placing TEMP inside Git invalidates the “outside a repo” premise in Capture/K5b; it also introduces spaces into temporary command paths. I should have audited those assumptions before the first full run. Existing RepeatGateTests passed all six in the baseline and all six in the final focused run.

The testdiff instrument also says started-but-unfinished cases are not measured without a blame sequence. The explicit abort and missing-case result is retained; it has not been converted to a green result.


Local evidence is retained under `Kor.Operations.Framework.Tests/obj/repeat-narrow/`: `base-full.trx`, `new-full.trx`, `final-repeat.trx`, `base-added-tests-final.trx`, `base-moments.json`, `final-moments.json`, and the eight `base-replay-*.txt` / `final-replay-*.txt` files. The baseline binaries are in `base-cli/`. These generated artifacts are ignored by Git; the cases and this report are committed.

**Rule 11 and corrections to the brief**

Failure class: **context overlap was being treated as proof that a result answered the requested question**. The unified check is the RepeatGate filter over both test classes: all 51 additions and the six retained controls. Its own summary names its coverage and limits. It compares verdicts, retained #129845 provenance, subject/scope controls, lift validity and logging identity; the full-suite `testdiff` separately compares test names/outcomes. It does not validate every historical result's scientific truth, every possible wording, the actual input files, or hidden holdouts. Same-class miss: a genuine repeated slab measurement whose quantity is described only in the result's body can now pass. This harness does not claim to enumerate every production false positive or false negative.

The brief correctly identified all 21 cited false refusals and the three desired repeat refusals, but its causal framing was incomplete. Its “four transcripts” means the four false-refusal sessions, not the fifth transcript containing the true controls. All listed calls were found in main transcripts despite the helper warning. UTC dates for the local 10 Nov token probes are 11 Nov, as retained in the fixtures. `--as-of` excludes records created later but uses current text and current retired/superseded/board state; it is not a historical database snapshot. Some baseline named records consequently differ from the original transcript output (notably 8970 and 9001). The read-only/testing conflict was resolved by Ian's explicit rollback-test authorization, not silently ignored.

One session, no delegated agents, no install/deploy. Three full-suite runs completed (baseline and two intermediate candidates). The fourth, on final code, was stopped for the harness incident above. The required before/after CLI passes and the focused runs are retained. Dollar/token costs were not available. Two process checks mattered: asserting the ranking premise caught a bad test setup, and expanding the positive controls to noun/inflected forms caught an implementation regression. After that regression, the invariant was stated explicitly: the same activity/object must survive a change in word form or object position, while a modifier such as “measurement cases” must not invent a measurement. Seven forms were gated against both baseline and the candidate. The root cause was maintaining a second vocabulary inside the subject matcher; the final code uses the original work vocabulary plus its existing break-down/read-off recognizers. The final focused and CLI proof was repeated afterward; the final full-suite proof remains incomplete because of the subsequently discovered harness incident.
