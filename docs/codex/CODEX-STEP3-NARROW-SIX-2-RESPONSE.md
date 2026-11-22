# Step 3, six guards, round 2 — response

22 November 2026. Changes are committed locally in the two `step3-narrow6` worktrees. This response is committed last. No push, installation, database mutation or change to another guard was performed.

Starting heads: Operations **fb9caf84** (130c012f plus this round's brief), Framework **c10ea6f**. Both worktrees were clean. I read both round-one documents, the repository guidance and the affected implementations. Searches in `tools/`, `docs/codex/`, the guard tests and `obj/step3-narrow6` found the existing cache delegate/clock seam, shell splitter, SQL query helper, historical input copies and replay harnesses. I extended the existing tests and reused that evidence. No new transcript population was collected.

The three requested repairs are implemented. The latency target passes; the C# comparison reports **NO NEW FAILURES**. Board passes **156/156** offline checks. Plan review passes **81/82** checks, with only the previously failing prior-art case 22 remaining. These results do not certify arbitrary shell syntax, natural-language claims or the author's held-back cases.

## 1. kor-enforce: bounded refresh cost

**Cases first:** Operations **89939034**. Three of four cache cases failed on the round-one implementation: both retirement/replacement cases and the empty-cache case. The future-timestamp control already passed. The revised retirement/replacement cases exercise ages 0, 4, 15, 29.999, 30 and 31 seconds using the existing injected `now` and reader. They check rule IDs, read counts, source labels and persisted timestamps, including that hits cannot slide expiry. Logs: `obj/step3-narrow6-r2/cache-red.log` and `cache-green.log`.

**Fix:** Operations **32f93bad**. A successful snapshot is fresh for **30 seconds**, with expiry at `age >= 30s`; a future timestamp is not fresh. This halves the suggested maximum window and removes the database trip from repeated calls. A retirement immediately after a successful read can remain enforced until the first call at/after that read's 30-second deadline. A successful refresh replaces the entire snapshot, including an empty list. Existing 60-second outage backoff and stale-cache fallback remain.

Operations **cf67bc45** restores the original retirement/replacement test method name for meaningful `testdiff` continuity; its updated 30-second assertions are unchanged. The first comparison correctly flagged the renamed cases as missing, despite no new failing results. The final comparison has no missing cases.

The full K7 suite has **6 passing / 29 database-disabled failures, 35 results**; all 29 failure names occur in the baseline. The four focused cache cases pass. The clock/reader tests establish the refresh deadline without depending on real time or altering database rows.

### Latency proof

Command measured: `kor.exe enforce --module Framework --hook --cache-dir <isolated-directory>`, stdin containing Bash `echo hi`, the Operations worktree cwd and session ID `step3-round2-latency`, **without `hook_event_name`**. Python `time.perf_counter()` measures process start through exit. There is one untimed warmup per binary, then eight measured fresh processes per binary, interleaved, repeated twice.

Installed binary: `C:/KOR-Operations/kor/kor.exe`, product version **1.0.0+1038597ceccb105ccf98153bba8795d7e07cdcd0**. Candidate: this worktree's **Debug** CLI built through Framework.Tests. Debug follows the repository's explicit build constraint; I did not produce the author's Release publish.

Both binaries receive identical snapshots containing **39 non-retired brain.Check rows**, selected through `python -I tools/SqlQuery/oq.py` after inspecting `sys.columns`. Rule patterns and known-answer arrays are preserved; display-only memory kind/name are fixed labels. Each round seeds a fresh timestamp once, then verifies that neither binary rewrote it or set an outage marker. All children have invalid SQL settings. This measures the ordinary fresh-cache path, not a network read or expiry miss; it does not independently reproduce the author's approximately 485 ms live-read measurement.

| Round | Installed median | Candidate Debug median | Increase |
|---|---:|---:|---:|
| 1, 8 runs each | 110.132 ms | 122.302 ms | **12.170 ms** |
| 2, 8 runs each | 111.590 ms | 127.371 ms | **15.782 ms** |

Both increases are below **50 ms**. Every measured process exits 0 with empty stdout/stderr. The second candidate series contains a 410.567 ms outlier; it is retained, not discarded. Exact samples and the measurement script are in `obj/step3-narrow6-r2/latency.json` and `latency.py`.

**Historical authority replay:** a compiled copy of the round-one cache and the new cache receive the same recorded retirement/replacement and controlled clock. No database is connected.

| Row | Age | Round one | Round two |
|---:|---:|---|---|
| 7030 | 4 / 29.999 s | Live reader removes #980; allow | Fresh cache still holds #980; deny |
| 7030 | 30 / 31 s | #980 absent; allow | #980 absent; allow |
| 8588 | 4 / 29.999 s | Replacement #3982; deny | Fresh cache still holds #978; deny |
| 8588 | 30 / 31 s | Replacement #3982; deny | Replacement #3982; deny |

Across those four calls, round one reads four times; round two reads once, at 30 seconds. The replacement still refuses 8588's literal loop, so this is not claimed as an overall allow. The broader command-only replay remains **25 deny / 2 allow of 27 recovered**; its per-row results are below.

**Rule 11:** class: repeated calls must reuse a successful snapshot without extending the authority refresh deadline. Covers retirement, replacement, an empty snapshot, exact expiry, read counts, persistence and backward clock movement. Does not cover server outages, writes failing to persist the cache or simultaneous process refreshes. Same-class miss: concurrent processes reaching an expired snapshot can each read before either saves; the cache does not serialize refreshes. Outages can continue enforcing a retired check beyond 30 seconds. The header states these limits.

## 2. plan_review: execution, SQL and verdict boundaries

Each fault has its own cases-only commit, observed red before its fix:

| Fault | Cases-only commit | New red checks / added checks | Fix commit |
|---|---|---:|---|
| Literal invocation/cwd forms | Framework **b7f5e67** | **5 / 11** | **f521b3a** |
| SQL lexical regions | Framework **a8e618e** | **5 / 12** | **bf681ff** |
| Formatted standalone verdicts | Framework **fa31f6d** | **9 / 17** | **5cb5979** |

The other added checks are passing controls, not claimed as previously broken. Each red/green suite log is under `obj/step3-narrow6-r2/plan-{command,sql,verdict}-{red,green}.log`.

The invocation parser now skips literal environment assignments, follows `pushd`/`popd`, and accepts `Set-Location -Path` / `-LiteralPath`. Existing attached/separate Python option handling remains. Quoted echo/Python-code mentions still allow, and every recognized board SQL input is checked.

SQL is scanned in lexical order. Single-quoted data, doubled delimiters, quoted/bracketed identifiers and nested block comments cannot consume unrelated executable statements. Complex identifier contents cannot masquerade as an INSERT. An unclosed lexical region requires review because its contents cannot safely establish absence of an INSERT. The scanner does not execute or validate SQL.

Standalone verdict lines accept bold, terminal periods and hyphen/en-dash/em-dash explanations. Embedded prose and quoted verdict controls still refuse. The last recognized verdict wins. A new case found an additional false allow: `VERDICT: GO` followed by `VERDICT: CHANGE — missing proof` previously ignored the final CHANGE and allowed; it now refuses.

Final suite: **81/82**. The only failure is existing case 22, whose bounded prior-art result omits the guard filename. The command, SQL and verdict subsets contribute **40/40 passing checks**. Historical replay: **29/29 recovered, deny before and after for every row**. Four historical SQL inputs use the round-one frozen file copies; negative review state comes from the recorded refusal, not today's review cache. Neither a recovered shell command nor a reviewer is run.

**Rule 11, invocation:** class: equivalent literal execution forms must identify the same input file. Covers the named environment prefixes, directory forms, option spellings, stack restoration and mention controls. Does not interpret arbitrary control flow or variable expansion. Same-class miss: a shell function computes a filename and invokes oq.py.

**Rule 11, SQL:** class: lexical data/comment boundaries must not hide a real INSERT or manufacture one from data. Covers markers in strings and identifiers, doubled quotes, nested comments, unclosed regions and bracketed table names. Does not evaluate dynamic SQL or synonyms. Same-class miss: an EXEC constructs the target table name at runtime.

**Rule 11, verdict:** class: harmless standalone formatting must preserve the final review decision while prose remains non-authoritative. Covers formatted GO/CHANGE, last-verdict precedence and prose/quotation controls. Does not authenticate the writer or judge review quality. Same-class miss: a session writes its own standalone GO into the review store. CHECKS/ALLOWS/DOES NOT CATCH text is updated.

## 3. board: scope must not swallow a current assertion

**Cases first:** Framework **e5ee36e**, **7 failures among 25 added cases**. There is one unconditional neighbour for each of round one's 17 allow cases, plus the reported preambles, independent assertions after attribution/conditions, and two genuine-condition controls. Existing controls that already refused are identified as controls, not fixes.

**Fix:** Framework **f1aa076**. `When I (last) checked,` and `If you ask me,` are factual/opinion preambles, not conditions on the row's state. A comma followed by but/yet/however and a direct row reference, optionally an explicit `I/we confirm/know/checked/report`, starts its own assertion scope. Earlier condition, attribution and recommendation text cannot exempt that assertion. A `but` inside the condition remains conditional.

Suite: **156/156 offline checks pass**; three live-board integration checks are skipped because the brief permits database access only through oq.py. All original **17/17 allow cases** still allow and all **17/17 unconditional neighbours** refuse. The suite counts additional reason-content checks only after a refusal, so its red total is 149 and its green total is 156.

Historical replay: **26/26 recovered**, **8 deny / 18 allow both before and after**. All seven round-one RIGHT refusals remain refused. The known WRONG row 8927 remains refused: cross-sentence subject resolution is still outside this repair.

**Rule 11:** class: an exemption must describe the assertion containing the row, rather than unrelated earlier text. Covers the 17 paired boundaries, factual preambles, explicit independent clauses, and real conditions. Does not solve arbitrary natural-language entailment, pronouns or unfamiliar idioms. Same-class miss: `If I remember correctly, row 137 is done` can still be treated as conditional. The header explicitly retains that bounded-language limitation.

## 4. Small notes

The cache cases are labelled **12b**, leaving `Live13_AWritePathRule` as 13 (89939034). StartDocs' two transcript constants are together again (**7a06e62f**). No production StartDocs behavior changed. Its suite passes **13/13**; combined with the four focused cache cases, **17/17** pass.

Rule 11: these are label/layout corrections, not behavioral repairs. Existing tests cover their runtime behavior; they do not mechanically enforce future numbering or member order. Another duplicated comment label would not fail a test. No artificial red test was added for formatting.

## C# proof and containment

Only `Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj` and its project references were built/tested, in Debug. No solution build, Release build, shared-compilation change or unrelated project test. TEMP/TMP remained normal.

Full-suite commands were run directly, redirected to logs with the actual exit code captured:

```powershell
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug --logger 'trx;LogFileName=final.trx' --results-directory obj/step3-narrow6-r2 -nologo
dotnet Kor.Operations.Framework.Cli/bin/Debug/net8.0-windows/kor.dll testdiff obj/step3-narrow6/new.trx obj/step3-narrow6-r2/final.trx
dotnet Kor.Operations.Framework.Cli/bin/Debug/net8.0-windows/kor.dll testdiff obj/step3-narrow6-r2/base.trx obj/step3-narrow6-r2/final.trx
```

For tests and C# CLI children, `KORBRAIN_SQL`, `KOR_ENGINEERINGTOOLS_STANDARDSDB` and `KOR_OPPORTUNITIES_OPPORTUNITIESDB` are set to the nonempty invalid value `step3-offline` **in the child environment only**. This prevents fallback and database writes, including writes that a live test would roll back. Python payloads omit `hook_event_name`, which makes `guard_log.record` return before connecting. Board live reads are disabled for the suite; historical readers are injected. Replays use pure functions, controlled cache readers and existing copied inputs, never live transcripts. SQL queries were SELECT-only through the existing oq.py, for Check schema and the latency snapshot. No refusal or Check row was written.

| Full C# run | Passed | Failed | Results |
|---|---:|---:|---:|
| Preserved round-one TRX | 1105 | 257 | 1362 |
| Fresh round-two pre-edit baseline | 1105 | 257 | 1362 |
| Final | 1107 | 257 | 1364 |

Both final `kor testdiff` comparisons exit **0, NO NEW FAILURES**: 254 distinct failing names in both, zero new failing names, two new passing tests, zero missing baseline tests. Full test runs exit 1 because the database-disabled failures remain. No blame sequences were collected; hung cases are NOT MEASURED by testdiff, though the baseline and final runs completed in approximately 23 and 26 seconds. The final run also confirms K7 **6/35** and StartDocs **13/13** under those connection settings.

All commits use `git -c core.hooksPath=obj/step3-narrow6-r2/empty-hooks commit` with an empty directory. No shared hook or settings file was edited. Work products/logs/caches are ignored under `obj/step3-narrow6-r2`; the existing test suites retain their normal temporary directories. Final source scope is three Operations files and four Framework files, plus this response. Both repositories pass `git diff --check`.

## Corrections and limits on the brief's claims

- Operations began at fb9caf84, not 130c012f; the additional commit is the round-two brief itself.
- Plain attached `-Xutf8` already refuses the unreviewed insertion on c10ea6f. It is retained as a passing control; no repair credit is claimed for it.
- Ordinary single-quoted strings containing `/*` already preserve the following INSERT. The reproduced lexical faults involve double-quoted/bracketed tokens and an unclosed comment containing a quote; nested comments and INSERT text inside an identifier also expose false refusals.
- `The handover says X. Row 137 is blocked.` already refuses because sentence splitting bounds the attribution. It is a useful neighbour, not a newly repaired failure. The independent same-sentence `but` forms do reproduce the fault.
- A final formatted CHANGE is not merely a fail-safe false refusal when an earlier GO exists: ignoring it causes a false allow. The new test proves that case red and green.
- The author's live-database 42-failing-name baseline, live board checks, review approvals and held-back start-docs results were not independently reproduced. Running the full live C# suite would violate the SELECT-only constraint because some tests write in rollback transactions. The matching offline comparison above is the evidence obtained here.
- Latency is verified on Debug with identical seeded fresh snapshots and invalid child connections. Release performance and cold/live refresh latency are not claimed. The ordinary-path median target is met, but simultaneous refreshes are not serialized and are an explicit remaining limit.

## Per-row historical replay

`deny`/`allow` describe the reconstructed guard decision, not a new judgment of the historical action. Board reads use only states captured in the refusal; plan reads use the recorded negative review precondition. Enforcement entries are command-only matches of the recorded rule with script expansion disabled, compared against round one's preserved results; cache authority is separately replayed above. The 11 unrecovered enforcement actions remain unrecovered. No new evidence was invented for them.

| Guard | Row | Before | After |
|---|---:|---|---|
| board | 378 | allow | allow |
| board | 467 | allow | allow |
| board | 1464 | allow | allow |
| board | 1981 | allow | allow |
| board | 4533 | allow | allow |
| board | 4648 | allow | allow |
| board | 4824 | allow | allow |
| board | 5516 | deny | deny |
| board | 5723 | allow | allow |
| board | 5966 | deny | deny |
| board | 5972 | deny | deny |
| board | 6005 | deny | deny |
| board | 6009 | deny | deny |
| board | 6116 | deny | deny |
| board | 6260 | allow | allow |
| board | 6559 | allow | allow |
| board | 7983 | allow | allow |
| board | 8003 | allow | allow |
| board | 8211 | allow | allow |
| board | 8624 | allow | allow |
| board | 8707 | allow | allow |
| board | 8774 | allow | allow |
| board | 8892 | allow | allow |
| board | 8927 | deny | deny |
| board | 8999 | allow | allow |
| board | 9196 | deny | deny |
| kor-enforce | 726 | deny | deny |
| kor-enforce | 1308 | unrecovered | unrecovered |
| kor-enforce | 1312 | unrecovered | unrecovered |
| kor-enforce | 1322 | unrecovered | unrecovered |
| kor-enforce | 1326 | unrecovered | unrecovered |
| kor-enforce | 1334 | unrecovered | unrecovered |
| kor-enforce | 1335 | unrecovered | unrecovered |
| kor-enforce | 1338 | unrecovered | unrecovered |
| kor-enforce | 1341 | unrecovered | unrecovered |
| kor-enforce | 1343 | unrecovered | unrecovered |
| kor-enforce | 2110 | deny | deny |
| kor-enforce | 5164 | deny | deny |
| kor-enforce | 5550 | deny | deny |
| kor-enforce | 5703 | allow | allow |
| kor-enforce | 5727 | deny | deny |
| kor-enforce | 5825 | deny | deny |
| kor-enforce | 6262 | deny | deny |
| kor-enforce | 6296 | deny | deny |
| kor-enforce | 6297 | allow | allow |
| kor-enforce | 6337 | deny | deny |
| kor-enforce | 6381 | deny | deny |
| kor-enforce | 6542 | deny | deny |
| kor-enforce | 6836 | deny | deny |
| kor-enforce | 6884 | deny | deny |
| kor-enforce | 7030 | deny | deny |
| kor-enforce | 7098 | deny | deny |
| kor-enforce | 7311 | deny | deny |
| kor-enforce | 7836 | deny | deny |
| kor-enforce | 8223 | unrecovered | unrecovered |
| kor-enforce | 8224 | unrecovered | unrecovered |
| kor-enforce | 8239 | deny | deny |
| kor-enforce | 8587 | deny | deny |
| kor-enforce | 8588 | deny | deny |
| kor-enforce | 8589 | deny | deny |
| kor-enforce | 8606 | deny | deny |
| kor-enforce | 8902 | deny | deny |
| kor-enforce | 8917 | deny | deny |
| kor-enforce | 8935 | deny | deny |
| plan_review | 7452 | deny | deny |
| plan_review | 7465 | deny | deny |
| plan_review | 7466 | deny | deny |
| plan_review | 7481 | deny | deny |
| plan_review | 7483 | deny | deny |
| plan_review | 7555 | deny | deny |
| plan_review | 7575 | deny | deny |
| plan_review | 7576 | deny | deny |
| plan_review | 7577 | deny | deny |
| plan_review | 7582 | deny | deny |
| plan_review | 7595 | deny | deny |
| plan_review | 7666 | deny | deny |
| plan_review | 7749 | deny | deny |
| plan_review | 7984 | deny | deny |
| plan_review | 7986 | deny | deny |
| plan_review | 7989 | deny | deny |
| plan_review | 8208 | deny | deny |
| plan_review | 8209 | deny | deny |
| plan_review | 8210 | deny | deny |
| plan_review | 8442 | deny | deny |
| plan_review | 8611 | deny | deny |
| plan_review | 8946 | deny | deny |
| plan_review | 8947 | deny | deny |
| plan_review | 8948 | deny | deny |
| plan_review | 8949 | deny | deny |
| plan_review | 8950 | deny | deny |
| plan_review | 8951 | deny | deny |
| plan_review | 8980 | deny | deny |
| plan_review | 8989 | deny | deny |
