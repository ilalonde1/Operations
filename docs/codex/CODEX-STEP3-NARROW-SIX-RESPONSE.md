# Step 3 — six guards: independent review and bounded fixes

22 November 2026. Operations base: 28953cb0734811098e87f868e7a883c461a05fd0. Framework base: 7a3b0a8225b7ba38d5f4fa17d17c97d8225e4e05. Both worktrees were initially clean on step3-narrow6. This response is committed last. No push or installation was performed.

All six guards are **FAULT** at the reviewed base: historical misfires or executable false-allow cases establish a fault in each. The bounded fixes below are committed. This does not certify all six as sound after these fixes, the held-back check as passing, or the entire test suite as green.

| Guard | RIGHT | WRONG | Not settled | Selected | Full action recovered |
|---|---:|---:|---:|---:|---:|
| kor-context-read | 52 | 0 | 0 | 52 | 52 |
| kor start-docs | 10 | 0 | 0 | 10 | 10 |
| plan_review | 29 | 0 | 0 | 29 | 29 |
| kor-enforce | 19 | 7 | 12 | 38 | 27 |
| board | 7 | 19 | 0 | 26 | 26 |
| kor-stall | 2 | 5 | 0 | 7 | 7 |
| Total | **119** | **31** | **12** | **162** | **151** |

## Bound, sampling, evidence and containment

Every GuardRefusal query included RefusedAtUtc < '2026-11-21T00:00:00-07:00'. Population: board 26, start-docs 10, context-read 240, enforce 117, stall 7, plan_review 29: **429**. I did not query, open or judge the six held-back rows, or open C:\Users\ilalonde\Documents\KOR-holdout. No population expansion was used to settle a class.

Sampling: Python random.Random(20261122).sample(sorted(unjudged_ids), 30), resetting the seed separately for context-read and enforce. All previously judged rows in the allowed population were also included. The four smaller populations were selected in full. Exact IDs and judgments appear in the ledger below.

I read the prior guards-review and inventory-2 responses, then checked source contracts and the bounded evidence. Database access was SELECT-only through python -I tools/SqlQuery/oq.py -q, limited to the two authorized tables and the requested schema inspection. JSON fragments of 200 characters preserved fields across sqlcmd's display width. Worker labels selected rows; they were not the answer key.

Full actions were matched to local main/subagent JSONL transcripts by session, contents, tool-result ID and time. Board recovery matched the logged excerpt inside the full reply, rather than choosing the nearest assistant message. Transcript entries at/after the cutoff were excluded from evidence. The appendix records account, session/file and line for every recovered action. Row 7984 is a temporal association: an Agent call 0.615 seconds before the row, whose visible result names another simultaneous hook; its next retry has the explicit plan-review refusal. Rows 7481/7483 have identical recovered message text; this does not establish two distinct deliveries.

Eleven enforcement actions are unrecovered: nine synthetic row1210-... sessions with no local transcript, plus 8223/8224 with NULL Session. Matched alone does not establish the whole action. Row 5825 is recovered but unsettled: its numeric PID came from a background Python launch's $!, and the permitted evidence does not establish its MSYS/native-PID behavior.

Replays used pure functions and copied inputs. Stop/transcript guards received prefix copies under obj/step3-narrow6/copies, never a live transcript. Board states came only from the historical refusal's named contradictions; the full historical board snapshot is unavailable, and truncated Matched can omit further contradictions. Context-read used logged bytes and recovered Read arguments. Start-docs replayed both versions' Scan against prefix copies and the logged STATE NOW range: all ten have zero delivered HANDOVER coverage after the boundary, sufficient to prove refusal without inventing old BUILD-PLAN contents. Plan replay reconstructed four SQL files from preceding Write/Edit events and supplied the negative review state visible in the refusal (absent, or CHANGE for 8442); it does not restore the whole historical review directory. No review subprocess or recovered command was executed.

C# context/enforce/stall CLI paths can log even without a hook flag. Replays instead used ReadGuard.Refuses, StartDocs.Scan, StallTranscript/Decide, and pure enforcement matching/cache delegates. Enforcement matching explicitly disables script reads: PowerShell can coerce a null string argument to empty, so cwd=$null alone was insufficient. The final comparison sets readScripts=false on both sides. No full script replay is claimed for 5703/6297.

Python payloads omit hook_event_name; guard_log.record returns before connecting in that case. The board suite's live Commitment read was disabled with an invalid child-process connection override; those cases are reported skipped. C# tests normally write inside rollback transactions, which this brief forbids. Child-process overrides set KORBRAIN_SQL, KOR_ENGINEERINGTOOLS_STANDARDSDB and KOR_OPPORTUNITIES_OPPORTUNITIESDB to the nonempty invalid value step3-offline. Parsing fails before connecting and prevents registry fallback. TEMP/TMP stayed at their normal C:\Users\ilalonde\AppData\Local\Temp values. No database-backed result is represented as passing.

All commits used git -c core.hooksPath=obj/step3-narrow6/empty-hooks commit in the respective repo. No shared hook, settings, other guard, remote machine or installed binary was changed. Ignored inputs, logs, TRX files and replay scripts remain under Operations obj/step3-narrow6; they are not additional response files or committed fixtures.

## kor-context-read — FAULT

Historical judgments: **52 RIGHT / 0 WRONG / 0 not settled**. Each is a main-session Read beyond the explicit byte/range contract: an image over 50,000 bytes, or another file over 40,000 bytes without an allowed range of at most 300 lines. None has a subagent exemption. The allow_large field in 7941 is not a supported exemption. Later splitting, cropping or recompression does not make the original call comply; a later helper also does not prove that refusal was better than cropping. 8956/8957 are different west/east pictures, both over the threshold. The contract explicitly offers cropping when helpers are unavailable.

Executable fault: PDF page specifications 0, 0-1, and a sum of enormous ranges can acquire a small-page allowance. Three new tests fail on the base, then pass. The fix requires positive page numbers and uses a wide accumulator with early saturation. Existing one/two-page controls remain passing. Thresholds and the no-helper policy were not changed.

Operations cases-only commit **a84d0c45**; fix **5426f037**. Historical replay: **52 deny before / 52 deny after; no transition**. There are no PDF page-range examples in this sample; the three red tests prove this repair.

Rule 11: invalid/unbounded page specifications must not acquire a bounded allowance. The cases cover zero endpoints and aggregate overflow together. Does not cover decoded image token cost, every malformed Read schema or cumulative reads. Same-class miss: repeated legal two-page reads can bring in a whole large PDF. The image byte proxy remains weak; worker outcome labels supply no measured replacement budget, so no guessed pixel/token threshold was introduced.

## kor start-docs — FAULT

Historical judgments: **10 RIGHT / 0 WRONG / 0 not settled**. The contract requires delivered Read-tool coverage after the latest compaction and explicitly refuses Bash/PowerShell until then. Read-only shell commands are not exempt. 8607 invokes sed alongside date/git log; 8608 is a separate kor start --part board call. 8939 and 8940 also differ: 8940 runs sessions.ps1.

All ten prefixes lack HANDOVER Read coverage after startup/compaction. Compaction boundaries are source line 4748 for 8939/8940, 3216 for 8981, 2364 for 8982, and 2948 for 9197. The other five precede the first qualifying reads. Earlier reads do not satisfy the post-compaction contract.

The base nevertheless has false allows: it recognizes the compaction flag only with one exact JSON spacing. Other legal spacing preserves stale read credit. String-valued numeric Read metadata throws instead of earning no coverage. Three new cases fail before the fix and pass after structural flag parsing and numeric-kind checks.

Operations cases-only commit **bab30806**; fix **414ef8d6**. Historical replay: **10 deny before / 10 deny after; no transition**. Its actual compaction formatting already worked on the base. Own suite: **13/13 pass**.

Rule 11: equivalent or malformed transcript encodings must not preserve/award read credit. Cases cover two legal spacings and malformed delivery metadata. Does not prove comprehension, universally detect later document edits, or verify settings list every required document. Same-class miss: reading all lines and then ignoring them. The six-denial fail-open and reviewer/subagent exemptions remain explicit policy limits; this edit repairs parsing.

## plan_review — FAULT

Historical judgments: **29 RIGHT / 0 WRONG / 0 not settled**. These are unreviewed helper launches without an exempt agent type, long messages to helper IDs, or SQL files actually inserting commitments without the required GO. Read-only prose in a general-purpose/fork brief is not the named Explore exemption. 7452 is an intentional live probe, still a right refusal. A later GO is not a preflight GO.

8442 includes both accept --because and oq.py -i in one shell call. At preflight the file has CHANGE; merely containing accept does not prove that it will succeed before the insert. Run accept separately. 8980's actual refusal is **not reviewed**, not CHANGE; a later review/search result is a different event.

Source review found false allows involving relative paths, a single-quoted oq executable, multiple board files, optional T-SQL INTO, and prose mentioning VERDICT: GO. It also found false refusals on echo mentions and SQL strings/comments. Ten cases were committed alone: nine failed; a Python-code mention control already passed. The fix reuses the existing quote-aware splitter, resolves ordinary literal cwd changes and payload cwd, checks each recognized oq input, excludes SQL strings/comments from insertion detection, and requires a standalone verdict line.

Framework cases-only commit **ae1497b**; fix **c10ea6f**. Historical replay: **29 deny before / 29 deny after**, under the frozen negative preconditions described above. SQL bytes recovered for 7749, 8442, 8980 and 8989. All ten new cases pass. Whole suite: **41/42**, versus **32/42** on the cases-only commit. Existing case 22 fails both: its bounded prior-art search returns twelve earlier matching paths and omits plan_review_guard.py. It is not counted as repaired.

Rule 11: judge executed inputs and review evidence, not incidental text or the first matching filename. Cases cover mentions, paths, multiple files, SQL syntax/data and verdict framing together. Does not interpret arbitrary shell control flow, aliases, generated filenames/SQL, or validate reviewer judgment. Same-class miss: an alias invokes oq.py. Remaining authority fault: the agent can write its own review store/use accept; repairing trusted ownership requires the excluded permissions/store workflow. Prior-art truncation remains: one omitted filename does not establish a sound replacement ranking/completeness rule, so this change leaves that search policy alone.

## kor-enforce — FAULT

Historical judgments: **19 RIGHT / 7 WRONG / 12 not settled**. LocalDB probes, direct remote CIM outside the required route, SQL integrated authentication and unauthorized side worktrees match their lessons. **8902 is RIGHT**: its full command invokes worktree add after the evidence-bearing report.

| WRONG ID | Independent reason |
|---:|---|
| 5164 | kill -0 $$ checks Bash itself; the rule concerns native Windows process liveness. |
| 5703 | bash -n checks syntax; it does not execute the Codex command inside the script. |
| 6297 | Assigning paths then git add/commit does not execute the scheduled-task script named in that list. The refusal explicitly matched inside that file. |
| 7030 | #980 retired 12 Nov 23:32:35.566; refusal 23:32:43.042 still enforced it. Retirement records the newer permission. |
| 8587 | The user selected the proposed parallel wave (source line 1163: “2 - parallel, launch wave 1”); the broad rule had not been narrowed. Policy lag, not an after-retirement cache hit. |
| 8588 | #978 retired 19 Nov 20:51:52.059; refusal 20:51:56.822 still enforced it. The transcript includes the user's retirement and readback. |
| 8589 | The finite loop produces six approved product-part folders; #3982 reads literal $f. |

Outside the existing outage backoff, every call now attempts a live refresh; a fresh cache no longer outranks a successful database read. Two cases—retirement and replacement within seconds—were committed alone, failed on the base, and pass after the fix. Outage fallback/backoff remains. Cost: one live-check read per healthy call; latency/load was not benchmarked under the no-database-test constraint.

Operations cases-only commit **843ce0c6**; fix **259f6f09**.

Command-only replay with recorded patterns: **25 deny / 2 allow of 27 recovered**, unchanged. 5703/6297 depend on historical script expansion, excluded from that replay; their allows are not credited to the cache fix. Additional frozen cache/authority replays:

| ID | Before | After healthy refresh |
|---:|---|---|
| 7030 | Cached #980 refuses | Retired #980 absent; this checked rule allows |
| 8588 | Cached #978 refuses | #978 absent; replacement #3982 still refuses the literal loop |

These use recorded retirement/replacement facts, an injected healthy reader and a fresh cache, not all historic live checks/outages. **8588 does not become an overall allow.**

Rule 11: cache must not outrank a successful live authority read. Removal, replacement and cache persistence are checked together. Does not solve outages, retirement after the SELECT or parser/rule faults. Same-class miss: retirement during an outage leaves the old rule in explicit stale-cache fallback; an unavailable authority cannot reveal its new state.

Remaining faults: 5703/6297 arise in shared Capture/ShellText script/assignment interpretation, outside the six named implementations; local pattern exemptions could hide real execution. 8589 needs bounded variable/data-flow interpretation; allowing arbitrary $f would admit forbidden folders. #3982's line-wide allowed-name test can also accept an approved name outside the destination; correcting both needs an argv-aware worktree policy, not another unproved regex exception. #978 is already retired/replaced, so no second retirement is proposed. 5825 lacks enough PID provenance and remains unsettled.

### Exact proposed brain.Check SQL — not executed

This narrowly exempts the known Bash-own-PID forms $$ and "$$". Other numeric/dynamic targets retain the existing rule. It does not exempt $!, whose provenance is unsettled here. Offline Rules.KnownAnswerFailures with the stored examples and three added own-PID controls gives **3 old-pattern failures / 0 replacement failures**. The recovered 5164 match becomes allow under this proposed pattern. This is proposed database policy, not an installed change or a committed red/green code-test pair. The transaction aborts if #945 has changed.

~~~sql
USE KorStandards;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @old nvarchar(400) = N'(?:(?:while|until|if)\s+(?:!\s+)?)?kill\s+-0\b';
    DECLARE @new nvarchar(400) = N'(?:(?:while|until|if)\s+(?:!\s+)?)?kill\s+-0\b(?!\s+(?:\$\$|"\$\$")(?:\s|$|[;&|]))';
    DECLARE @ids TABLE (Id int);
    UPDATE brain.[Check]
       SET RetiredAtUtc = SYSDATETIMEOFFSET(),
           RetiredBy = N'step3-narrow6 author-applied',
           RetiredBecause = N'Replaced: kill -0 $$ checks Bash itself, not a native Windows PID (GuardRefusal 5164). Other PID checks remain covered.'
     WHERE Id = 945 AND RetiredAtUtc IS NULL AND Pattern = @old;
    IF @@ROWCOUNT <> 1 THROW 50001, 'Check 945 changed or is already retired; review again.', 1;

    INSERT brain.[Check]
        (MemoryId, Module, Tool, Field, Pattern, MustRefuse, MustAllow, Says, CreatedBy)
    OUTPUT inserted.Id INTO @ids
    SELECT MemoryId, Module, Tool, Field, @new, MustRefuse,
           JSON_MODIFY(JSON_MODIFY(JSON_MODIFY(MustAllow,
               'append $', N'kill -0 $$ 2>/dev/null'),
               'append $', N'if ! kill -0 $$; then echo impossible; fi'),
               'append $', N'kill -0 "$$"'),
           N'Git Bash kill -0 is not the native Windows PID liveness check. Use PowerShell Get-Process -Id <pid> for native Windows processes. Bash own-PID checks using $$ or "$$" are allowed.',
           N'step3-narrow6 author-applied'
      FROM brain.[Check] WHERE Id = 945;
    COMMIT TRANSACTION;
    SELECT Id AS ReplacementCheckId FROM @ids;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
~~~

SQL rule-11 boundary: plain, conditional and double-quoted own-PID checks pass while all old refuse examples and mention controls remain covered. Does not classify arbitrary variables. Same-class miss: a variable known by the session to hold Bash's own PID is still refused. No SQL here was run, including inside a rollback transaction.

## board — FAULT

Historical judgments: **7 RIGHT / 19 WRONG / 0 not settled**. Wrong refusals attach state words to a condition, recommendation, attribution, component/subset or corrected statement instead of a current state assertion about the whole row.

| Class | WRONG IDs |
|---|---|
| Conditional/future/recommendation/attribution scope | 467, 1464, 4824, 4648, 6260, 6559, 7983, 8624, 8707, 8774 |
| Partial work, another subject, subset, negation/correction | 378, 1981, 4533, 5723, 8003, 8211, 8892, 8999 |
| Cross-sentence board-state disclosure, still misread | 8927 |

4648 puts “the JM work (row 1970) is finished” in an until bullet list; the inventory's RIGHT judgment missed the condition. 378 reports K1/K2 closed and K3 in progress, not every row closed. 8003 identifies bare 2140 as done after naming 2140–2143. 8999 says files/stashes are done while branches remain parked; its matching reply is about 22 minutes older than the Stop refusal. 8927 says implementation is done, then explicitly says the row was left doing pending acceptance: the formal board state is disclosed.

Seventeen cases were committed alone and all failed. The fix handles longer conditional scope, conditional bullet lists, pre-reference intent, bounded attribution/other-subject forms, partial work and later negation of the same state. Existing direct-current-state refusal controls remain passing.

Framework cases-only commit **b03d835**; fix **442264d**.

Historical replay against states recorded in Matched: **26 deny -> 8 deny / 18 allow**. Every changed verdict is a WRONG refusal becoming allow: **378, 467, 1464, 1981, 4533, 4648, 4824, 5723, 6260, 6559, 7983, 8003, 8211, 8624, 8707, 8774, 8892, 8999**. All seven RIGHT rows still refuse. 8927 still refuses. Own offline suite: **108/108 checks pass**, live cases skipped.

Rule 11: retain the state word's subject and assertion scope. All seventeen bounded shapes are checked together alongside direct-refusal controls. Does not resolve arbitrary pronouns, label-to-row mappings, or prove board truth. Same-class miss: a condition followed by an unconditional contradictory claim in the same unsplit clause may be skipped. 8927 remains because reconciling implementation with a later “its state” needs cross-sentence subject resolution; a blanket “left doing” override could hide another row's contradiction. Once-only Stop and DB-down fail-open remain unchanged.

## kor-stall — FAULT

Historical judgments: **2 RIGHT / 5 WRONG / 0 not settled**.

| ID | Judgment | Evidence and base -> new replay |
|---:|---|---|
| 4210 | WRONG | sqlcmd failed opening bare C:; a changed inline query did not repeat that malformed command. deny -> allow |
| 4211 | WRONG | Record plus corrected retry, not record-only; same bare-drive misattribution. deny -> allow |
| 4602 | RIGHT | Two KOR-210 AccessDenied results, then another attempt on the resource. deny -> deny |
| 4932 | WRONG | Absolute -i corrected to relative filename after changing directory. deny -> allow |
| 6497 | WRONG | “Retry 28 also timed out” is heartbeat data, not an ls failure. deny -> deny |
| 8983 | RIGHT | Repeated access refusal on the same FS01 archive resource. deny -> deny |
| 9007 | WRONG | locate.py edited for the encoding failure before retry. allow -> allow on supplied base/new |

Three cases were committed alone and all failed. The fix treats sqlcmd's exact bare-drive input-file error as a command failure rather than permission evidence against every named resource. The identical third command still refuses; a corrected command reopens it. Permission failures are not globally excused.

Operations cases-only commit **f2b8da56**; fix **7cdaea37**.

Historical replay: **6 deny / 1 allow -> 3 deny / 4 allow**, exactly **4210, 4211, 4932** changing. Prefix copies and a delegate returning an actual C# null avoided DB access; records/edits in the prefix remained visible. An initial PowerShell null-to-string coercion simulated an empty diagnosis and was corrected before these results. 9007 already allows on the base, so no fix credit is claimed; installed-version/timing differences remain unresolved.

Rule 11: malformed client input paths must not cause permission stalls on unrelated resources. Cases cover inline query, relative input, record plus retry and identical-retry refusal. Does not cover other clients' diagnostics or attribute compound-command output to individual steps. Same-class miss: another client's ambiguous AccessDenied input-path error can still be assigned too broadly. 6497 remains because per-step output provenance is unavailable; blanket exemptions for timeout prose could hide real failures.

## Builds, tests and scope

Only Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj was built/tested, in Debug; its references build the worktree CLI. No full solution, App, Release build or unrelated test project. New warnings would fail the build; no shared-compilation setting changed.

| Guard | New cases red on old implementation | Own suite after fix |
|---|---:|---|
| ReadGuard | 3/3 fail | 27 pass / 2 existing DB-disabled failures, 29 total |
| StartDocs | 3/3 fail | 13 pass / 0 fail |
| CheckCache/K7 | 2/2 fail | 4 pass / 29 existing DB-disabled failures, 33 total |
| Stall | 3/3 fail | 29 pass / 1 existing DB-disabled failure, 30 total |
| Board | 17/17 fail | 108 checks pass; live cases skipped |
| Plan review | 9/10 fail; 1 passing control | 41/42; only existing case 22 fails |

Full C# commands, with the child overrides described above:

~~~powershell
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug --logger 'trx;LogFileName=<base-or-new>.trx' --results-directory obj/step3-narrow6 -nologo
dotnet Kor.Operations.Framework.Cli/bin/Debug/net8.0-windows/kor.dll testdiff obj/step3-narrow6/base.trx obj/step3-narrow6/new.trx
~~~

Test output was redirected to logs and exit codes captured, never piped through a command masking failure. Both full runs exited 1 because of disconnected baseline tests.

| Run | Passed | Failed | Skipped | Total |
|---|---:|---:|---:|---:|
| Base | 1,094 | 257 | 0 | 1,351 |
| New | 1,105 | 257 | 0 | 1,362 |

Worktree kor testdiff: **NO NEW FAILURES**, exit 0. By distinct name: 254 failing in both; zero only in new; zero formerly failing now passing; eleven new tests, all passing; no missing base tests. Theory results share some names, hence 257 results versus 254 names. No blame sequences: hung cases **NOT MEASURED** by testdiff, though both runs finished (about 21/25 seconds). The larger baseline than a normal live run is deliberate isolation, not 257 new regressions.

Scope: eight Operations guard/test source files, four Framework guard/test files, plus this response. git diff --check passed in both repos. Both were clean before this response was written. No proposed SQL was applied.

## Claims that did not survive checking

- The 429-row pre-cutoff population and 22/8 previously judged selections are correct. The advertised sixteen start-docs rows/eight wrong labels mix the held-back period with the allowed ten rows, seven previously marked wrong. I did not inspect excluded rows to reconcile their labels.
- Reading the same content later in cuts, or obtaining GO later, does not make the original refusal wrong under these contracts. All selected context-read and plan_review rows remain RIGHT.
- 8608 was board-start, not sed; 8940 was sessions.ps1, not the same command as 8939. Reads before compaction do not satisfy start-docs.
- 8442 was not accept-only: it includes an insert. 8980's actual refusal is not-reviewed, not CHANGE.
- 8902 really invokes worktree add outside its evidence text. 8588 is a confirmed cache fault; 8589 is a finite-loop misfire. 8587 is earlier policy lag, not a retired-cache hit.
- 9007 is an edited-script historical misfire, already allowed by the supplied base replay. Prior inventory reasoning that 4211 was record-only was wrong; it includes a retry.
- The earlier RIGHT judgment for board 4648 misses a governing condition. Truncated range/partial-work excerpts cannot establish every referenced row as done.
- Full-action recovery is 151/162, not 162/162. Eleven unattributable enforcement rows remain unsettled, as does recovered row 5825's PID provenance.
- A full Framework test run without isolation would violate the brief: rollback transactions still write. Omitting a C# hook flag alone also does not prevent logging.

Remaining misfires and bypasses have specific limits above; no guard was removed. The held-back check remains for the author.

## Exact selected IDs and source ledger

**kor-context-read sample** — previously judged: 5449, 5659, 5762, 5765, 5833, 6168, 6196, 6252, 6273, 6335, 6368, 6601, 8590, 8864, 8877, 8895, 8907, 8913, 8914, 8915, 8956, 8957. Random 30: 5450, 5466, 5649, 6162, 6313, 6315, 6342, 6397, 6412, 6586, 6611, 6619, 6629, 6733, 6819, 6831, 6860, 6863, 6895, 6921, 7598, 7644, 7655, 7658, 7753, 7759, 7763, 7773, 7941, 8213.

**kor-enforce sample** — previously judged: 8239, 8587, 8588, 8589, 8606, 8902, 8917, 8935. Random 30: 726, 1308, 1312, 1322, 1326, 1334, 1335, 1338, 1341, 1343, 2110, 5164, 5550, 5703, 5727, 5825, 6262, 6296, 6297, 6337, 6381, 6542, 6836, 6884, 7030, 7098, 7311, 7836, 8223, 8224.

**kor-context-read** — RIGHT: 5449, 5450, 5466, 5649, 5659, 5762, 5765, 5833, 6162, 6168, 6196, 6252, 6273, 6313, 6315, 6335, 6342, 6368, 6397, 6412, 6586, 6601, 6611, 6619, 6629, 6733, 6819, 6831, 6860, 6863, 6895, 6921, 7598, 7644, 7655, 7658, 7753, 7759, 7763, 7773, 7941, 8213, 8590, 8864, 8877, 8895, 8907, 8913, 8914, 8915, 8956, 8957. WRONG: none. NOT SETTLED: none.

**kor start-docs** — RIGHT: 8607, 8608, 8939, 8940, 8943, 8955, 8981, 8982, 9005, 9197. WRONG: none. NOT SETTLED: none.

**plan_review** — RIGHT: 7452, 7465, 7466, 7481, 7483, 7555, 7575, 7576, 7577, 7582, 7595, 7666, 7749, 7984, 7986, 7989, 8208, 8209, 8210, 8442, 8611, 8946, 8947, 8948, 8949, 8950, 8951, 8980, 8989. WRONG: none. NOT SETTLED: none.

**kor-enforce** — RIGHT: 726, 2110, 5550, 5727, 6262, 6296, 6337, 6381, 6542, 6836, 6884, 7098, 7311, 7836, 8239, 8606, 8902, 8917, 8935. WRONG: 5164, 5703, 6297, 7030, 8587, 8588, 8589. NOT SETTLED: 1308, 1312, 1322, 1326, 1334, 1335, 1338, 1341, 1343, 5825, 8223, 8224.

**board** — RIGHT: 5516, 5966, 5972, 6005, 6009, 6116, 9196. WRONG: 378, 467, 1464, 1981, 4533, 4648, 4824, 5723, 6260, 6559, 7983, 8003, 8211, 8624, 8707, 8774, 8892, 8927, 8999. NOT SETTLED: none.

**kor-stall** — RIGHT: 4602, 8983. WRONG: 4210, 4211, 4932, 6497, 9007. NOT SETTLED: none.

Source notation: I = C:/Users/ilalonde/.claude/projects/; K = C:/Users/kor-claude/.claude/projects/. The remaining path and 1-based JSONL line are literal. Hash prefixes are SHA-256 of recovered action text, not file-size claims. Full hashes and payloads remain in the ignored recovery manifest. Historical source commands were not executed.

| ID | Guard | Judgment | Source JSONL:line | Action SHA-256 prefix |
|---:|---|---|---|---|
| 378 | board | WRONG | I/C--VIsual-Studio-Projects-Framework/d0313f99-7306-4d8f-b228-bd7e99c83bbe.jsonl:6550 | 24f6c84595df23d4 |
| 467 | board | WRONG | I/C--VIsual-Studio-Projects-KOR-RevitTools/b882ae93-e0a6-46b3-88cc-0c46d811c979.jsonl:28 | bb851bba79dc51d1 |
| 726 | kor-enforce | RIGHT | I/C--VIsual-Studio-Projects-Framework/d0313f99-7306-4d8f-b228-bd7e99c83bbe.jsonl:9852 | aa324d9ca04f928e |
| 1308 | kor-enforce | NOT SETTLED | Unrecovered | — |
| 1312 | kor-enforce | NOT SETTLED | Unrecovered | — |
| 1322 | kor-enforce | NOT SETTLED | Unrecovered | — |
| 1326 | kor-enforce | NOT SETTLED | Unrecovered | — |
| 1334 | kor-enforce | NOT SETTLED | Unrecovered | — |
| 1335 | kor-enforce | NOT SETTLED | Unrecovered | — |
| 1338 | kor-enforce | NOT SETTLED | Unrecovered | — |
| 1341 | kor-enforce | NOT SETTLED | Unrecovered | — |
| 1343 | kor-enforce | NOT SETTLED | Unrecovered | — |
| 1464 | board | WRONG | I/C--VIsual-Studio-Projects-Framework/d0313f99-7306-4d8f-b228-bd7e99c83bbe.jsonl:11781 | 8d23ac0e56143e2c |
| 1981 | board | WRONG | I/C--VIsual-Studio-Projects-Framework/d0313f99-7306-4d8f-b228-bd7e99c83bbe.jsonl:13424 | 769a4b5324062adf |
| 2110 | kor-enforce | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/006abbce-df7c-44a1-856d-1f7f00796dfb.jsonl:4286 | d23ffa2549ce20ba |
| 4210 | kor-stall | WRONG | I/C--VIsual-Studio-Projects-Operations/92443cad-4b61-4cc7-a9c8-87a6e22977a8.jsonl:309 | 3d8d8f5ce5c02ee2 |
| 4211 | kor-stall | WRONG | I/C--VIsual-Studio-Projects-Operations/92443cad-4b61-4cc7-a9c8-87a6e22977a8.jsonl:326 | 850fa38eae7c9419 |
| 4533 | board | WRONG | I/C--VIsual-Studio-Projects-Operations/5a3ea005-d656-4519-b2f8-877f22d1bdd5.jsonl:611 | 2d8ad4e451a9e3ce |
| 4602 | kor-stall | RIGHT | I/C--VIsual-Studio-Projects-Framework/f05ed50c-4024-405c-a522-b316840c25d1.jsonl:376 | 9106f9110a580d47 |
| 4648 | board | WRONG | I/C--VIsual-Studio-Projects-KOR-Drafter/0f005e1c-d069-48bb-b744-a81031023947.jsonl:1574 | 7f97054c804a98f8 |
| 4824 | board | WRONG | I/C--VIsual-Studio-Projects-KOR-Drafter/151fa70f-cbe2-4108-abb2-4180dd37aeb6.jsonl:1841 | 5a657651bb1f85ce |
| 4932 | kor-stall | WRONG | I/C--VIsual-Studio-Projects-Framework/a812dedc-9cef-496a-88ea-8f104b3c1393.jsonl:1703 | c32aa572990e776b |
| 5164 | kor-enforce | WRONG | I/C--VIsual-Studio-Projects-KOR-Drafter/e03fbab4-b9eb-492f-a07c-48c2cf043630.jsonl:1200 | ed8d3933427add85 |
| 5449 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-Framework/5f0008d6-e841-4aee-b0ce-2d2d4e0f1e35.jsonl:1124 | 3b3fd17a3d7e1ed2 |
| 5450 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-Framework/5f0008d6-e841-4aee-b0ce-2d2d4e0f1e35.jsonl:1126 | 2cbc70d06c64f130 |
| 5466 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/3a38b179-246d-4737-8c90-e256cf177a90.jsonl:511 | 59dbbf5309b5c19d |
| 5516 | board | RIGHT | I/C--VIsual-Studio-Projects-Framework/5f0008d6-e841-4aee-b0ce-2d2d4e0f1e35.jsonl:1800 | acbe2ec2a8737517 |
| 5550 | kor-enforce | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/1aff60f9-9000-40a4-9e04-1869c2fc0319.jsonl:988 | fbc6673c2b73b16c |
| 5649 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-Framework/b9e16bff-a4e4-4f51-a189-ca7bb5fc35a9.jsonl:79 | ce4e61cb3c1edcbc |
| 5659 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/a3e8d3b4-6533-468b-b80d-36027b553312.jsonl:266 | 5a0b54a26525a80f |
| 5703 | kor-enforce | WRONG | I/C--VIsual-Studio-Projects-Framework/cac2f109-c32d-4e4b-9c12-5641906fcad6.jsonl:1196 | 110f2c36b1514192 |
| 5723 | board | WRONG | I/C--VIsual-Studio-Projects-KOR-Drafter/294489f8-bf56-4818-8728-134dff138649.jsonl:977 | 1c035ce06b7228c6 |
| 5727 | kor-enforce | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/621532ac-1814-4aec-b5d4-31ffe5c70564.jsonl:469 | c850943bdd471112 |
| 5762 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/1f0e88c3-571a-4dd8-b836-491f67666096.jsonl:518 | 49817599dd269ab3 |
| 5765 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-Framework/faa35655-d50c-422b-aa2b-964d4e774dde.jsonl:821 | 908f7be6ba1217e2 |
| 5825 | kor-enforce | NOT SETTLED | I/C--VIsual-Studio-Projects-KOR-Drafter/863782f9-0021-4f02-b376-3fd85ac06f4d.jsonl:327 | 3e5f42bf069d5227 |
| 5833 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/95b4b465-c283-402d-ae17-7b2a80ea4936.jsonl:176 | 359a82d4f2f5892a |
| 5966 | board | RIGHT | I/C--VIsual-Studio-Projects-Operations/68cee091-880c-4f05-9e2c-af0cbe7ed4f0.jsonl:605 | 51d5b2ea168ba583 |
| 5972 | board | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/9e616844-8124-4d13-8d10-ada3493bbe75.jsonl:958 | 263d6ae346fc7f11 |
| 6005 | board | RIGHT | I/C--VIsual-Studio-Projects-Framework/945b51ba-885f-4ef9-87ef-d21ad2956df2.jsonl:453 | 900eafc10ce3d604 |
| 6009 | board | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/a93b5af6-16db-4947-be6c-bf8483a7d223.jsonl:1318 | 30dd4883a7add199 |
| 6116 | board | RIGHT | I/C--VIsual-Studio-Projects-Framework/953fd3b6-ee71-4cd8-b195-c8deec2c54b9.jsonl:478 | 65d78d5858b55365 |
| 6162 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/f1f56b21-daa6-44b6-9439-5fedc00b6cd5.jsonl:103 | 3174a0574242f61d |
| 6168 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/fff54590-8d1c-4537-967e-462310889d98.jsonl:103 | 106e6c13efa1fa1a |
| 6196 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/f4fbc7c3-8dd1-49ca-bddd-de0b5e215c20.jsonl:461 | c2ef1c56b4d97d35 |
| 6252 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/24ff8024-7b6b-4f0c-a375-a0bf09928864.jsonl:70 | a60d28a99c562f7b |
| 6260 | board | WRONG | I/C--VIsual-Studio-Projects-KOR-Drafter/f28ab4a1-0dce-4cb1-816a-c6e4d42e271a.jsonl:817 | 36a6d31311d96120 |
| 6262 | kor-enforce | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/f9fd482e-a4ea-4597-bb02-de329856f0c2.jsonl:64 | f58395c037c42898 |
| 6273 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/2e1ab5b4-838e-4813-a240-f7b39a8da47f.jsonl:159 | ede2c996c09b97b0 |
| 6296 | kor-enforce | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/f5279b69-307d-4bec-a5ef-b08d241cb277.jsonl:954 | 0910531ed4f52989 |
| 6297 | kor-enforce | WRONG | I/C--VIsual-Studio-Projects-KOR-Drafter/91e67eee-3734-4542-a96f-129f60ae243f.jsonl:1802 | 7be2834bd95b2d4b |
| 6313 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/ee701c6e-7b13-496d-867e-4030bce629b7.jsonl:118 | e06775da64dec523 |
| 6315 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/7007a5b5-4c3e-48bc-a614-5a1de748d066.jsonl:149 | 2526d3b213949089 |
| 6335 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/86bfb63e-81c5-440d-8da0-69220497bec6.jsonl:262 | f7182b8671a9aaa5 |
| 6337 | kor-enforce | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/5efc21ef-4a02-42c6-a64a-30e40d7e7579.jsonl:108 | a408dd580c52c0ad |
| 6342 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/2985782f-5e92-4dee-a0d1-a5562a7d02a7.jsonl:869 | 02f0bc519b470432 |
| 6368 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/71da8c1f-056e-47b1-86fe-22154f090527.jsonl:463 | 82dbd617704ee310 |
| 6381 | kor-enforce | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/415b4cca-e72e-4ba3-b9f6-a23540667e64.jsonl:150 | e7a44ffce0e6c506 |
| 6397 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-Framework/f8da8536-b67b-43ad-8041-153c4a880e04.jsonl:773 | 10dc344e4591a629 |
| 6412 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/fd98cb93-af4c-49aa-ba7b-093a427c8a43.jsonl:230 | ad9e557288f5cf4a |
| 6497 | kor-stall | WRONG | I/C--VIsual-Studio-Projects-Framework/7371e71a-a16b-4d21-a26c-dd68d7fed1a6.jsonl:430 | 228c701f249fe919 |
| 6542 | kor-enforce | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/fe13af24-de9e-4429-b257-5633289bdfd0.jsonl:368 | 396ce968f4e6152a |
| 6559 | board | WRONG | I/C--VIsual-Studio-Projects-Framework/843a3ed2-6ca4-4a59-a791-cc23a9dfca9b.jsonl:3082 | 9f7e0034d1ca83c4 |
| 6586 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/dd35f412-4a53-44c2-9efd-0e42c9b98cc8.jsonl:389 | 6fa3a205d46f563e |
| 6601 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/0f9ce266-ec18-44c8-859b-1548f15e1a04.jsonl:600 | cfaaef90385a1f32 |
| 6611 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/0f9ce266-ec18-44c8-859b-1548f15e1a04.jsonl:923 | 7406624c3d274399 |
| 6619 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/dc3fffd9-a7da-495c-8190-1b348f4e94e8.jsonl:144 | cc87ddc80fa17270 |
| 6629 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-Framework/1676068e-dccc-4254-b8ae-5351c89b7eda.jsonl:271 | 82f6a8f9a36adeb9 |
| 6733 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter-wt-selfcheck-text/5c5c5d83-85aa-418f-8da3-2869d76f83f7.jsonl:779 | 630650fb212acd88 |
| 6819 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-Framework/7bff9b74-fa00-4a00-bd4b-c795fee665bf.jsonl:499 | b3a0dedc1dce133d |
| 6831 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/8885dec0-c083-46d7-9d05-424d25b09267.jsonl:208 | 8c151c6a0b4aa7ae |
| 6836 | kor-enforce | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/3e8db236-3dfc-4ec9-a75d-c94d1c8f74dd/subagents/agent-a47f678fab8360a5b.jsonl:126 | 9548baf997faf261 |
| 6860 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter-wt-selfcheck-geom/5610b5cc-ef57-42a8-a0e8-961669087161.jsonl:452 | 54f92332117939cc |
| 6863 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/3e8db236-3dfc-4ec9-a75d-c94d1c8f74dd.jsonl:1002 | 841ab4a3bf611456 |
| 6884 | kor-enforce | RIGHT | I/C--VIsual-Studio-Projects-Framework/1e2750b2-53e5-4577-9193-2382b4f78165.jsonl:1091 | 35aba9da66db7c58 |
| 6895 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter-wt-selfcheck-core/668f0136-c247-4817-a762-bf7c2dc416a8.jsonl:521 | a0c1e255db6b172b |
| 6921 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-Framework/1e2750b2-53e5-4577-9193-2382b4f78165.jsonl:2664 | 2f8ac42b96a193ba |
| 7030 | kor-enforce | WRONG | I/C--VIsual-Studio-Projects-Framework/2c92fbae-ad51-417d-bad0-908766b4de05.jsonl:2579 | c8277fa864ab0119 |
| 7098 | kor-enforce | RIGHT | I/C--VIsual-Studio-Projects-Framework/2c92fbae-ad51-417d-bad0-908766b4de05.jsonl:5750 | daf36182fc469252 |
| 7311 | kor-enforce | RIGHT | I/C--VIsual-Studio-Projects-Framework/2c92fbae-ad51-417d-bad0-908766b4de05/subagents/agent-a1c4c2d00bcec34d2.jsonl:55 | 7b030df19057d1f8 |
| 7452 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/1e2750b2-53e5-4577-9193-2382b4f78165.jsonl:9986 | 320a260ac1900071 |
| 7465 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/1e2750b2-53e5-4577-9193-2382b4f78165/subagents/agent-adb5ebb121fe304ce.jsonl:191 | a6f948208d92931d |
| 7466 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/1e2750b2-53e5-4577-9193-2382b4f78165/subagents/agent-adb5ebb121fe304ce.jsonl:195 | 8e0cd31943427b0a |
| 7481 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/1e2750b2-53e5-4577-9193-2382b4f78165.jsonl:11182 | be2fc91c02815a95 |
| 7483 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/1e2750b2-53e5-4577-9193-2382b4f78165.jsonl:11182 | be2fc91c02815a95 |
| 7555 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/2fd5ef67-6301-45f9-a31a-218e32cd1411.jsonl:1012 | ae12301492d68a7c |
| 7575 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/2fd5ef67-6301-45f9-a31a-218e32cd1411/subagents/agent-ac4ef84d6e6abf660.jsonl:481 | dd6d52873f4db3ef |
| 7576 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/2fd5ef67-6301-45f9-a31a-218e32cd1411/subagents/agent-ac4ef84d6e6abf660.jsonl:485 | 8f56b8a4bfbe3f3c |
| 7577 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/2fd5ef67-6301-45f9-a31a-218e32cd1411/subagents/agent-ac4ef84d6e6abf660.jsonl:489 | 2501634b083855c5 |
| 7582 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/2fd5ef67-6301-45f9-a31a-218e32cd1411.jsonl:2361 | 27ad4d3e50b12f5c |
| 7595 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/2fd5ef67-6301-45f9-a31a-218e32cd1411.jsonl:2720 | f3acab0b8792662e |
| 7598 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/bb105a7a-c7fd-49e3-a70f-48033588679b.jsonl:485 | fb1f4ea73879ebfa |
| 7644 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/fa3b9ca6-5a7e-4565-a00a-e339dc9070e1.jsonl:538 | 8c139ee9c9e9fe6c |
| 7655 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/e1a67b7f-7d24-44b0-abd7-5082ff2087ea.jsonl:212 | 959b567fe824051f |
| 7658 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/bc471b16-6033-438f-a97c-80a25f8cb6de.jsonl:1647 | e054836ecd64529e |
| 7666 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/17bc90f5-eff2-4702-8ba3-085ff6ce2db8.jsonl:1965 | 3f89158c4c7318bf |
| 7749 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/17bc90f5-eff2-4702-8ba3-085ff6ce2db8.jsonl:4965 | c296dc1f5b0bf4db |
| 7753 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/b81d7c67-ca70-4fe9-9892-cde67e2e4d0f.jsonl:73 | 7168edf8238994fb |
| 7759 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/ed5e7fef-b4b1-4bdf-a3c6-b032862186df.jsonl:208 | 1fde12c46724dff5 |
| 7763 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/e1d78291-a7e6-41a1-8075-44d58ff9862f.jsonl:760 | 3875bc9cb240edb6 |
| 7773 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/bbcef775-65e8-4e00-b1b3-8c46d6004e7b.jsonl:298 | 92bb0f4caeba4f21 |
| 7836 | kor-enforce | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/bfd3cbae-c3b8-448a-a649-b6bc40ee92cb.jsonl:2125 | 45d9ec7c948377e4 |
| 7941 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-KOR-Drafter/559cf920-3fe3-4710-9f9c-89284e7a609c.jsonl:60 | a9180b4c6c44dde7 |
| 7983 | board | WRONG | I/C--VIsual-Studio-Projects-Framework/8876d9cf-7957-4f76-b11f-07958295d64a.jsonl:1900 | fe901ed3748ae2db |
| 7984 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/8876d9cf-7957-4f76-b11f-07958295d64a.jsonl:2007 | 5502fd750759f73d |
| 7986 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/8876d9cf-7957-4f76-b11f-07958295d64a.jsonl:2020 | 8f40d0627c661075 |
| 7989 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/8876d9cf-7957-4f76-b11f-07958295d64a.jsonl:2084 | 3058d3e1cf8bb317 |
| 8003 | board | WRONG | I/C--VIsual-Studio-Projects-Framework/8876d9cf-7957-4f76-b11f-07958295d64a.jsonl:2587 | 29e83bb7225b65e9 |
| 8208 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/1ec8f69e-8b81-4da0-9f22-26aada6db3e4.jsonl:3202 | 293764eaa1c19134 |
| 8209 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/1ec8f69e-8b81-4da0-9f22-26aada6db3e4.jsonl:3204 | 4df404faa2d6d246 |
| 8210 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/1ec8f69e-8b81-4da0-9f22-26aada6db3e4.jsonl:3206 | b9d5c0250ad69648 |
| 8211 | board | WRONG | I/C--VIsual-Studio-Projects-Framework/1ec8f69e-8b81-4da0-9f22-26aada6db3e4.jsonl:3320 | b119087eb77a502b |
| 8213 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-Operations/54ba440a-d280-4fb5-a139-ca5954803ee7.jsonl:33 | 4e18da0daafbbffa |
| 8223 | kor-enforce | NOT SETTLED | Unrecovered | — |
| 8224 | kor-enforce | NOT SETTLED | Unrecovered | — |
| 8239 | kor-enforce | RIGHT | I/C--VIsual-Studio-Projects-Framework/a0ed29c9-a893-4264-b14d-14cca0d980b5.jsonl:300 | 5cf817f37a57d9a0 |
| 8442 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/a0ed29c9-a893-4264-b14d-14cca0d980b5.jsonl:4672 | 05351d31f68c8cfe |
| 8587 | kor-enforce | WRONG | K/C--VIsual-Studio-Projects-KOR-Drafter/ac80da06-010b-48fe-b321-e576eb44a116.jsonl:1235 | cbb473e512c47392 |
| 8588 | kor-enforce | WRONG | K/C--VIsual-Studio-Projects-KOR-Drafter/ac80da06-010b-48fe-b321-e576eb44a116.jsonl:1348 | 579edc40d58a766d |
| 8589 | kor-enforce | WRONG | K/C--VIsual-Studio-Projects-KOR-Drafter/ac80da06-010b-48fe-b321-e576eb44a116.jsonl:1393 | b7bae338e8b23177 |
| 8590 | kor-context-read | RIGHT | K/C--VIsual-Studio-Projects-KOR-Drafter-markup-intake/3920cc62-71f9-45e4-9c3a-70b447e9aada.jsonl:188 | edf95af716cb0284 |
| 8606 | kor-enforce | RIGHT | K/C--VIsual-Studio-Projects-KOR-Drafter-revit/620177ea-ccf2-48eb-a705-f3d2648bf7eb.jsonl:1147 | a144ec4becf30084 |
| 8607 | kor start-docs | RIGHT | I/C--VIsual-Studio-Projects-Framework/6b17620c-3f84-43fa-9ebb-7f75c62b3930.jsonl:43 | 75bc17d299e8b67f |
| 8608 | kor start-docs | RIGHT | I/C--VIsual-Studio-Projects-Framework/6b17620c-3f84-43fa-9ebb-7f75c62b3930.jsonl:44 | dc591d1c1b27ebee |
| 8611 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/6b17620c-3f84-43fa-9ebb-7f75c62b3930.jsonl:334 | 12c4269dc82c080a |
| 8624 | board | WRONG | K/C--VIsual-Studio-Projects-KOR-Drafter/ac80da06-010b-48fe-b321-e576eb44a116.jsonl:2355 | e4349ff0e72bc543 |
| 8707 | board | WRONG | I/C--VIsual-Studio-Projects-Framework/6b17620c-3f84-43fa-9ebb-7f75c62b3930.jsonl:1238 | 42949e2ddabedf19 |
| 8774 | board | WRONG | K/C--VIsual-Studio-Projects-KOR-Drafter/ac80da06-010b-48fe-b321-e576eb44a116.jsonl:3222 | 05f4ae6bd2eeeca5 |
| 8864 | kor-context-read | RIGHT | K/C--VIsual-Studio-Projects-KOR-Drafter-geometry-gates/5750502b-789a-4668-9619-607de3348260.jsonl:93 | f6e711479ca47278 |
| 8877 | kor-context-read | RIGHT | K/C--VIsual-Studio-Projects-KOR-Drafter-geometry-gates/5750502b-789a-4668-9619-607de3348260.jsonl:137 | f6e711479ca47278 |
| 8892 | board | WRONG | I/C--VIsual-Studio-Projects-Framework/6b17620c-3f84-43fa-9ebb-7f75c62b3930.jsonl:1947 | aa54df7490168e08 |
| 8895 | kor-context-read | RIGHT | K/C--VIsual-Studio-Projects-KOR-Drafter-quantities/7bcc602c-0bd7-47ef-bb44-bb862a1bbe07.jsonl:1563 | 776e3dc5dbbfe7b0 |
| 8902 | kor-enforce | RIGHT | K/C--VIsual-Studio-Projects-KOR-Drafter-viewer/6b7bc486-2f5a-4010-afd4-b88ac55a9f44.jsonl:669 | eaccb13af294c16f |
| 8907 | kor-context-read | RIGHT | K/C--VIsual-Studio-Projects-KOR-Drafter-viewer/6b7bc486-2f5a-4010-afd4-b88ac55a9f44.jsonl:962 | f1836301c38227ba |
| 8913 | kor-context-read | RIGHT | K/C--VIsual-Studio-Projects-KOR-Drafter-quantities/780c3992-0bf1-4ce7-9bea-f1d1c32ed2c7.jsonl:575 | 65ed5848882ba76d |
| 8914 | kor-context-read | RIGHT | K/C--VIsual-Studio-Projects-KOR-Drafter-drafted-set/591ae542-6565-4396-b5e5-949c359d47d1.jsonl:132 | a02b87901aff5cc3 |
| 8915 | kor-context-read | RIGHT | K/C--VIsual-Studio-Projects-KOR-Drafter-drafted-set/591ae542-6565-4396-b5e5-949c359d47d1.jsonl:189 | a02b87901aff5cc3 |
| 8917 | kor-enforce | RIGHT | K/C--VIsual-Studio-Projects-KOR-Drafter-integration/a0a18409-e144-4601-8071-ab5826abc95d.jsonl:1172 | 80673ca1d4d9988c |
| 8927 | board | WRONG | K/C--VIsual-Studio-Projects-KOR-Drafter-integration/a0a18409-e144-4601-8071-ab5826abc95d.jsonl:1857 | 53dbbfb0a150fc61 |
| 8935 | kor-enforce | RIGHT | K/C--VIsual-Studio-Projects-KOR-Drafter/e1b293aa-38e6-491e-b759-ee7c949a963f.jsonl:118 | bffa0c8796dd1c18 |
| 8939 | kor start-docs | RIGHT | I/C--VIsual-Studio-Projects-Framework/c5eddf43-6013-474a-8392-6c886f21fb51.jsonl:4804 | bba1ed5623251781 |
| 8940 | kor start-docs | RIGHT | I/C--VIsual-Studio-Projects-Framework/c5eddf43-6013-474a-8392-6c886f21fb51.jsonl:4805 | 095d24843f876fa2 |
| 8943 | kor start-docs | RIGHT | K/C--VIsual-Studio-Projects-Framework/ee8754e5-8d40-4410-94b9-004d09152ab6.jsonl:49 | 5d774178bd96c178 |
| 8946 | plan_review | RIGHT | K/C--VIsual-Studio-Projects-Framework/ee8754e5-8d40-4410-94b9-004d09152ab6.jsonl:391 | ddd60410908f4375 |
| 8947 | plan_review | RIGHT | K/C--VIsual-Studio-Projects-Framework/ee8754e5-8d40-4410-94b9-004d09152ab6.jsonl:395 | 931f1b17aafdd707 |
| 8948 | plan_review | RIGHT | K/C--VIsual-Studio-Projects-Framework/ee8754e5-8d40-4410-94b9-004d09152ab6.jsonl:399 | 3a21a3316995796f |
| 8949 | plan_review | RIGHT | K/C--VIsual-Studio-Projects-Framework/ee8754e5-8d40-4410-94b9-004d09152ab6.jsonl:403 | 6557d72e80112739 |
| 8950 | plan_review | RIGHT | K/C--VIsual-Studio-Projects-Framework/ee8754e5-8d40-4410-94b9-004d09152ab6.jsonl:407 | 3f00a6fc370b5ddd |
| 8951 | plan_review | RIGHT | K/C--VIsual-Studio-Projects-Framework/ee8754e5-8d40-4410-94b9-004d09152ab6.jsonl:411 | a33c599a01c19b2c |
| 8955 | kor start-docs | RIGHT | I/C--VIsual-Studio-Projects-Framework/e7287342-6067-4ac2-818a-e923a9744797.jsonl:46 | c15793de87f1cf42 |
| 8956 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-Framework/e7287342-6067-4ac2-818a-e923a9744797.jsonl:210 | 79826572ea926587 |
| 8957 | kor-context-read | RIGHT | I/C--VIsual-Studio-Projects-Framework/e7287342-6067-4ac2-818a-e923a9744797.jsonl:211 | 7b7de356b5e99015 |
| 8980 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/e7287342-6067-4ac2-818a-e923a9744797.jsonl:3058 | b4bf469661aa67ca |
| 8981 | kor start-docs | RIGHT | I/C--VIsual-Studio-Projects-Framework/e7287342-6067-4ac2-818a-e923a9744797.jsonl:3268 | 5b86715ed49e92b4 |
| 8982 | kor start-docs | RIGHT | K/C--VIsual-Studio-Projects-Framework/ee8754e5-8d40-4410-94b9-004d09152ab6.jsonl:2424 | 86cb8b4612d4c022 |
| 8983 | kor-stall | RIGHT | K/C--VIsual-Studio-Projects-Framework/ee8754e5-8d40-4410-94b9-004d09152ab6.jsonl:2573 | 7fa16aa5fe2f6db5 |
| 8989 | plan_review | RIGHT | I/C--VIsual-Studio-Projects-Framework/e7287342-6067-4ac2-818a-e923a9744797.jsonl:5216 | 11b8a9e33b89a63c |
| 8999 | board | WRONG | K/C--VIsual-Studio-Projects-Framework/ee8754e5-8d40-4410-94b9-004d09152ab6.jsonl:3301 | 55de1fd312149f94 |
| 9005 | kor start-docs | RIGHT | I/C--VIsual-Studio-Projects-Framework/b6568d08-9e05-4227-9081-c24680ae497d.jsonl:45 | a61d4202b1617cb2 |
| 9007 | kor-stall | WRONG | I/C--VIsual-Studio-Projects-Framework/b6568d08-9e05-4227-9081-c24680ae497d.jsonl:493 | 31a0a0fa775550da |
| 9196 | board | RIGHT | I/C--VIsual-Studio-Projects-Framework/b6568d08-9e05-4227-9081-c24680ae497d.jsonl:3001 | f5abdcc46a89b193 |
| 9197 | kor start-docs | RIGHT | I/C--VIsual-Studio-Projects-Framework/b6568d08-9e05-4227-9081-c24680ae497d.jsonl:3016 | 3eef4d4b3a044254 |
